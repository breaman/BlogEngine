// CodeMirror 6 surface for the MarkdownEditor component, and highlight.js for MarkdownPreview
// (design 10.2, T1.10, T1.11). Bundled by src/BlogEngine.Server/scripts/build-js.mjs into
// BlogEngine.Client/wwwroot/js/editor.js and loaded from .NET with import().
//
// .NET owns the Markdown: the editor reports changes (debounced), and .NET renders the preview with the
// shared BlogMarkdownPipeline. This module only edits text, syncs scrolling, highlights code and uploads images
// that are pasted or dropped into the editor (design 9.5, A10).

import { EditorState, EditorSelection, Annotation, Prec } from '@codemirror/state';
import { EditorView, keymap, drawSelection, placeholder as placeholderText } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { markdown, markdownLanguage } from '@codemirror/lang-markdown';
import { syntaxHighlighting, HighlightStyle, indentOnInput, bracketMatching } from '@codemirror/language';
import { tags } from '@lezer/highlight';
import hljs from './highlighter.js';
import 'highlight.js/styles/github.css';
// GitHub Dark under [data-bs-theme="dark"], so the preview follows the admin's theme (P14; see build-js.mjs).
import 'virtual:hljs-dark-theme.css';

/** Delay before a change is sent to .NET, so the preview re-renders once per pause in typing. */
const changeDebounceMs = 200;

/** Marks transactions made by setValue, so .NET isn't told about text it just sent. */
const externalChange = Annotation.define();

ensureStylesheet();

/**
 * Creates an editor inside `host`.
 * @param {HTMLElement} host Empty element owned by this module (Blazor never renders into it).
 * @param {any} dotNet DotNetObjectReference of the MarkdownEditor component.
 * @param {{ value?: string, placeholder?: string, imageHandler?: boolean,
 *           uploads?: { url: string, headerName: string, token: string | null } | null }} options
 *   `uploads` turns on paste and drop image upload to the media library endpoint, with the antiforgery header.
 */
export function createEditor(host, dotNet, options) {
    return new MarkdownEditorHandle(host, dotNet, options ?? {});
}

/** Highlights every code block under `element` that hasn't been highlighted yet. */
export function highlight(element) {
    if (!element) {
        return;
    }

    for (const block of element.querySelectorAll('pre code:not([data-highlighted])')) {
        hljs.highlightElement(block);
    }

    keepFragmentLinksInPreview(element);
}

const previewsWithFragmentLinks = new WeakSet();

/**
 * Heading anchors and footnote links in the preview are bare "#id" links, which the page's <base href="/"> would send
 * to the site's home page (the public pages resolve them on the server). In the preview they scroll to their target
 * instead. Blazor ignores clicks whose default was prevented, and admin.js's link guard ignores "#" links.
 */
function keepFragmentLinksInPreview(element) {
    if (previewsWithFragmentLinks.has(element)) {
        return;
    }

    previewsWithFragmentLinks.add(element);
    element.addEventListener('click', event => {
        const link = event.target instanceof Element ? event.target.closest('a[href^="#"]') : null;
        if (!link || !element.contains(link)) {
            return;
        }

        event.preventDefault();
        const id = decodeURIComponent(link.getAttribute('href').slice(1));
        element.querySelector(`[id="${CSS.escape(id)}"]`)?.scrollIntoView({ block: 'start' });
    });
}

class MarkdownEditorHandle {
    constructor(host, dotNet, options) {
        this.dotNet = dotNet;
        this.imageHandler = options.imageHandler === true;
        this.uploads = options.uploads ?? null;
        /** Placeholders of uploads in flight: { text, from, to }, kept in step with edits in onUpdate. */
        this.pendingUploads = [];
        this.uploadRequests = new Set();
        this.changeTimer = 0;
        this.preview = null;
        this.syncFrame = 0;

        const commands = createCommands(this);

        this.view = new EditorView({
            parent: host,
            state: EditorState.create({
                doc: options.value ?? '',
                extensions: [
                    history(),
                    drawSelection(),
                    indentOnInput(),
                    bracketMatching(),
                    EditorView.lineWrapping,
                    // markdown() also adds its keymap: Enter continues lists and quotes, Backspace removes markup.
                    markdown({ base: markdownLanguage }),
                    syntaxHighlighting(markdownHighlightStyle, { fallback: true }),
                    placeholderText(options.placeholder ?? ''),
                    // Our shortcuts win over the defaults (for example Mod-i, which selects the parent syntax node).
                    Prec.high(keymap.of([
                        { key: 'Mod-b', run: commands.bold },
                        { key: 'Mod-i', run: commands.italic },
                        { key: 'Mod-k', run: commands.link },
                        { key: 'Mod-`', run: commands.code },
                        { key: 'Mod-Shift-i', run: commands.image },
                        { key: 'Mod-s', run: commands.save, preventDefault: true }
                    ])),
                    keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
                    EditorView.updateListener.of(update => this.onUpdate(update)),
                    EditorView.domEventHandlers({
                        blur: () => this.onBlur(),
                        paste: (event, view) => this.onPaste(event, view),
                        drop: (event, view) => this.onDrop(event, view),
                        dragover: event => this.onDragOver(event)
                    }),
                    EditorView.contentAttributes.of({ 'aria-label': 'Markdown content', spellcheck: 'true' }),
                    editorTheme
                ]
            })
        });

        this.commands = commands;
        this.onScroll = () => this.scheduleSync();
        this.view.scrollDOM.addEventListener('scroll', this.onScroll, { passive: true });
    }

    /** Current Markdown. */
    getValue() {
        return this.view.state.doc.toString();
    }

    /** Replaces the whole document (reload, restore) without reporting it back as an edit. */
    setValue(value) {
        const text = value ?? '';
        if (text === this.getValue()) {
            return;
        }

        this.view.dispatch({
            changes: { from: 0, to: this.view.state.doc.length, insert: text },
            selection: EditorSelection.cursor(0),
            annotations: externalChange.of(true)
        });
    }

    /** Inserts text at the cursor, replacing the selection. */
    insertText(text) {
        this.view.dispatch(this.view.state.replaceSelection(text ?? ''));
        this.view.focus();
    }

    /**
     * Inserts `text` as a paragraph of its own at the cursor, adding blank lines around it as needed, so an image
     * from the media picker renders as a figure rather than inside the surrounding text (design 9.5).
     */
    insertBlock(text) {
        const { state } = this.view;
        const range = state.selection.main;
        const before = state.sliceDoc(Math.max(0, range.from - 2), range.from);
        const after = state.sliceDoc(range.to, Math.min(state.doc.length, range.to + 2));

        const prefix = range.from === 0 || before === '\n\n' ? '' : before.endsWith('\n') ? '\n' : '\n\n';
        const suffix = range.to === state.doc.length ? '\n' : after === '\n\n' ? '' : after.startsWith('\n') ? '\n' : '\n\n';
        const insert = prefix + (text ?? '') + suffix;

        this.view.dispatch({
            changes: { from: range.from, to: range.to, insert },
            selection: EditorSelection.cursor(range.from + insert.length),
            scrollIntoView: true
        });
        this.view.focus();
    }

    focus() {
        this.view.focus();
    }

    /** Runs a toolbar command by name: bold, italic, link, code, image, heading, quote, bulletList, orderedList. */
    runCommand(name) {
        const command = this.commands[name];
        if (command) {
            command(this.view);
            this.view.focus();
        }
    }

    /** The scrollable preview pane to keep in step with the editor; blocks carry data-line attributes. */
    setPreview(element) {
        this.preview = element ?? null;
        this.scheduleSync();
    }

    /** Re-aligns the preview, for example after it re-rendered. */
    syncPreview() {
        this.scheduleSync();
    }

    /** Sends any pending change to .NET now, so a save uses the latest text. */
    async flush() {
        if (this.changeTimer) {
            clearTimeout(this.changeTimer);
            this.changeTimer = 0;
            await this.notifyChanged();
        }
    }

    dispose() {
        for (const controller of this.uploadRequests) {
            controller.abort();
        }

        this.uploadRequests.clear();
        this.pendingUploads = [];
        clearTimeout(this.changeTimer);
        cancelAnimationFrame(this.syncFrame);
        this.view.scrollDOM.removeEventListener('scroll', this.onScroll);
        this.view.destroy();
        this.preview = null;
        this.dotNet = null;
    }

    onUpdate(update) {
        if (!update.docChanged) {
            return;
        }

        // Keep upload placeholders pointing at their text while the author keeps typing around them.
        for (const pending of this.pendingUploads) {
            pending.from = update.changes.mapPos(pending.from, 1);
            pending.to = update.changes.mapPos(pending.to, -1);
        }

        if (update.transactions.some(t => t.annotation(externalChange))) {
            return;
        }

        clearTimeout(this.changeTimer);
        this.changeTimer = setTimeout(() => {
            this.changeTimer = 0;
            this.notifyChanged();
        }, changeDebounceMs);
    }

    async onBlur() {
        await this.flush();
        await this.invoke('OnEditorBlur');
    }

    notifyChanged() {
        return this.invoke('OnContentChanged', this.getValue());
    }

    /** Pasted image files (a screenshot, or files copied in a file manager) are uploaded instead of pasted as text. */
    onPaste(event, view) {
        const files = imageFiles(event.clipboardData);
        if (!this.uploads || files.length === 0) {
            return false;
        }

        event.preventDefault();
        this.uploadFiles(files, view.state.selection.main.from, view.state.selection.main.to);
        return true;
    }

    /** Dropped image files are uploaded and inserted where they were dropped. */
    onDrop(event, view) {
        const files = imageFiles(event.dataTransfer);
        if (!this.uploads || files.length === 0) {
            return false;
        }

        event.preventDefault();
        const pos = view.posAtCoords({ x: event.clientX, y: event.clientY }) ?? view.state.selection.main.head;
        this.uploadFiles(files, pos, pos);
        return true;
    }

    /** Allows dropping files; without it the browser would open a dropped image instead. */
    onDragOver(event) {
        if (this.uploads && Array.from(event.dataTransfer?.types ?? []).includes('Files')) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'copy';
            return true;
        }

        return false;
    }

    /**
     * Inserts an `![Uploading name…]()` placeholder per file as a paragraph of its own between `from` and `to`, then
     * uploads the files one by one and replaces each placeholder with the image's Markdown, or removes it on failure
     * (.NET shows why).
     */
    uploadFiles(files, from, to) {
        const { state } = this.view;
        const before = state.sliceDoc(Math.max(0, from - 2), from);
        const after = state.sliceDoc(to, Math.min(state.doc.length, to + 2));
        const prefix = from === 0 || before === '\n\n' ? '' : before.endsWith('\n') ? '\n' : '\n\n';
        const suffix = to === state.doc.length ? '\n' : after === '\n\n' ? '' : after.startsWith('\n') ? '\n' : '\n\n';

        const placeholders = files.map(file => `![Uploading ${placeholderName(file.name)}…]()`);
        const insert = prefix + placeholders.join('\n\n') + suffix;

        let offset = from + prefix.length;
        const pending = placeholders.map(text => {
            const entry = { text, from: offset, to: offset + text.length };
            offset += text.length + 2;
            return entry;
        });

        this.view.dispatch({
            changes: { from, to, insert },
            selection: EditorSelection.cursor(from + insert.length),
            scrollIntoView: true
        });
        this.pendingUploads.push(...pending);
        this.view.focus();

        // One at a time, in order, so the images arrive in the order they were pasted or dropped.
        (async () => {
            for (let i = 0; i < files.length; i++) {
                const markdown = await this.uploadFile(files[i]);
                this.replacePlaceholder(pending[i], markdown ?? '');
            }
        })();
    }

    /** Uploads one file; resolves to the Markdown .NET builds from the response, or null when it failed. */
    async uploadFile(file) {
        const controller = new AbortController();
        this.uploadRequests.add(controller);

        let status = 0;
        let body = '';
        try {
            const form = new FormData();
            form.append('files', file, file.name || 'pasted-image.png');
            const headers = { Accept: 'application/json' };
            if (this.uploads.token) {
                headers[this.uploads.headerName] = this.uploads.token;
            }

            const response = await fetch(this.uploads.url, { method: 'POST', body: form, headers, signal: controller.signal });
            status = response.status;
            body = await response.text();
        } catch (error) {
            if (controller.signal.aborted) {
                return null;
            }
        } finally {
            this.uploadRequests.delete(controller);
        }

        return await this.invokeForResult('OnImageUploaded', file.name || 'pasted image', status, body);
    }

    /**
     * Swaps a placeholder for `markdown`, or removes it (and the blank line it added) when `markdown` is empty. The
     * placeholder is found where its tracked position says, or by its text if an edit moved it; if the author deleted
     * it, nothing is inserted.
     */
    replacePlaceholder(pending, markdown) {
        this.pendingUploads = this.pendingUploads.filter(p => p !== pending);
        if (!this.dotNet) {
            return;
        }

        const doc = this.view.state.doc;
        let from = pending.from;
        let to = pending.to;
        if (doc.sliceString(from, to) !== pending.text) {
            from = doc.toString().indexOf(pending.text);
            if (from < 0) {
                return;
            }

            to = from + pending.text.length;
        }

        if (!markdown) {
            const following = doc.sliceString(to, Math.min(doc.length, to + 2));
            const preceding = from === 0 ? '\n' : doc.sliceString(from - 1, from);
            if (preceding === '\n') {
                to += following.startsWith('\n\n') ? 2 : following.startsWith('\n') ? 1 : 0;
            }
        }

        this.view.dispatch({ changes: { from, to, insert: markdown } });
    }

    /** Calls the component and returns its result, or null if it was disposed or the call failed. */
    async invokeForResult(method, ...args) {
        if (!this.dotNet) {
            return null;
        }

        try {
            return await this.dotNet.invokeMethodAsync(method, ...args);
        } catch (error) {
            if (this.dotNet) {
                console.error(`MarkdownEditor.${method} failed`, error);
            }

            return null;
        }
    }

    /** Calls the component, ignoring calls made after it was disposed (for example during navigation). */
    async invoke(method, ...args) {
        if (!this.dotNet) {
            return;
        }

        try {
            await this.dotNet.invokeMethodAsync(method, ...args);
        } catch (error) {
            // The component can be disposed between the check above and the call completing.
            if (this.dotNet) {
                console.error(`MarkdownEditor.${method} failed`, error);
            }
        }
    }

    scheduleSync() {
        if (!this.preview || this.syncFrame) {
            return;
        }

        this.syncFrame = requestAnimationFrame(() => {
            this.syncFrame = 0;
            this.syncPreviewNow();
        });
    }

    /**
     * Scrolls the preview so the block rendered from the editor's top visible line is at the top of the
     * pane. Blocks carry the 0-based source line Markdig recorded (UsePreciseSourceLocation); between two
     * blocks the position is interpolated so scrolling stays smooth.
     */
    syncPreviewNow() {
        const preview = this.preview;
        const scroller = this.view.scrollDOM;
        if (!preview || !preview.isConnected) {
            return;
        }

        if (scroller.scrollTop <= 0) {
            preview.scrollTop = 0;
            return;
        }

        if (scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2) {
            preview.scrollTop = preview.scrollHeight;
            return;
        }

        const topBlock = this.view.lineBlockAtHeight(scroller.getBoundingClientRect().top - this.view.documentTop);
        const line = this.view.state.doc.lineAt(topBlock.from).number - 1;

        let before = null;
        let after = null;
        for (const element of preview.querySelectorAll('[data-line]')) {
            const elementLine = Number(element.dataset.line);
            if (elementLine <= line) {
                before = { line: elementLine, top: element.offsetTop };
            } else {
                after = { line: elementLine, top: element.offsetTop };
                break;
            }
        }

        if (!before) {
            preview.scrollTop = 0;
            return;
        }

        const end = after ?? { line: this.view.state.doc.lines, top: preview.scrollHeight };
        const fraction = end.line > before.line ? (line - before.line) / (end.line - before.line) : 0;
        preview.scrollTop = before.top + fraction * (end.top - before.top);
    }
}

/** The image files in a paste or drop; other files and plain text are left to the default handling. */
function imageFiles(transfer) {
    return Array.from(transfer?.files ?? []).filter(file => file.type.startsWith('image/'));
}

/** A file name that can't end the placeholder's alt text early. */
function placeholderName(name) {
    return (name || 'image').replace(/[\[\]\\\n\r]/g, ' ').trim() || 'image';
}

/** Formatting commands shared by the toolbar and the keyboard shortcuts. */
function createCommands(handle) {
    return {
        bold: view => toggleWrap(view, '**', 'bold text'),
        italic: view => toggleWrap(view, '*', 'italic text'),
        code: view => insertCode(view),
        link: view => insertLink(view, false),
        image: view => {
            if (handle.imageHandler) {
                // The media picker inserts the image through insertBlock.
                handle.invoke('OnImageRequested');
                return true;
            }

            return insertLink(view, true);
        },
        heading: view => toggleLinePrefix(view, '## ', /^#{1,6}\s+/),
        quote: view => toggleLinePrefix(view, '> ', /^>\s?/),
        bulletList: view => toggleLinePrefix(view, '- ', /^[-*+]\s+(\[[ xX]\]\s+)?/),
        orderedList: view => toggleLinePrefix(view, '1. ', /^\d+[.)]\s+/),
        save: () => {
            handle.flush().then(() => handle.invoke('OnSaveRequested'));
            return true;
        }
    };
}

/** Wraps each selection in `marker`, or unwraps it when it is already wrapped. */
function toggleWrap(view, marker, sample) {
    const { state } = view;
    const size = marker.length;

    view.dispatch(state.changeByRange(range => {
        const before = state.sliceDoc(range.from - size, range.from);
        const after = state.sliceDoc(range.to, range.to + size);
        if (before === marker && after === marker) {
            return {
                changes: [{ from: range.from - size, to: range.from }, { from: range.to, to: range.to + size }],
                range: EditorSelection.range(range.from - size, range.to - size)
            };
        }

        const text = range.empty ? sample : state.sliceDoc(range.from, range.to);
        return {
            changes: { from: range.from, to: range.to, insert: marker + text + marker },
            range: EditorSelection.range(range.from + size, range.from + size + text.length)
        };
    }));

    return true;
}

/** Inline code for a single-line selection, a fenced block for a multi-line one. */
function insertCode(view) {
    const { state } = view;
    const range = state.selection.main;
    const text = state.sliceDoc(range.from, range.to);

    if (!text.includes('\n')) {
        return toggleWrap(view, '`', 'code');
    }

    const fenced = '```\n' + text + (text.endsWith('\n') ? '' : '\n') + '```';
    view.dispatch({
        changes: { from: range.from, to: range.to, insert: fenced },
        selection: EditorSelection.cursor(range.from + 3)
    });

    return true;
}

/** Inserts `[text](url)` (or `![alt](url)`) around the selection and selects the URL placeholder. */
function insertLink(view, image) {
    const { state } = view;
    const range = state.selection.main;
    const text = range.empty ? (image ? 'alt text' : 'link text') : state.sliceDoc(range.from, range.to);
    const prefix = image ? '![' : '[';
    const url = 'https://';
    const insert = `${prefix}${text}](${url})`;
    const urlStart = range.from + prefix.length + text.length + 2;

    view.dispatch({
        changes: { from: range.from, to: range.to, insert },
        selection: EditorSelection.range(urlStart, urlStart + url.length)
    });

    return true;
}

/**
 * Adds `prefix` to every selected line, or removes the existing marker (matched by `pattern`) when all
 * selected lines already have one.
 */
function toggleLinePrefix(view, prefix, pattern) {
    const { state } = view;
    const lines = [];
    for (const range of state.selection.ranges) {
        for (let pos = range.from; pos <= range.to;) {
            const line = state.doc.lineAt(pos);
            if (!lines.some(l => l.number === line.number)) {
                lines.push(line);
            }

            pos = line.to + 1;
        }
    }

    const remove = lines.every(line => pattern.test(line.text));
    const changes = lines.map(line => remove
        ? { from: line.from, to: line.from + line.text.match(pattern)[0].length }
        : { from: line.from, insert: prefix });

    view.dispatch({ changes });
    return true;
}

/**
 * CodeMirror's defaultHighlightStyle, with each color written as light-dark(light, dark). The light colors are
 * CodeMirror's own; its dark blues (links, URLs, code fence languages) are unreadable on the dark admin theme, so
 * dark mode uses GitHub Dark colors to match the preview's code blocks. light-dark() follows the color-scheme
 * that Bootstrap sets under [data-bs-theme="dark"], so the editor switches with the theme toggle without being
 * reconfigured.
 */
const markdownHighlightStyle = HighlightStyle.define([
    { tag: tags.meta, color: 'light-dark(#404740, #8b949e)' },
    { tag: tags.link, textDecoration: 'underline' },
    { tag: tags.heading, textDecoration: 'underline', fontWeight: 'bold' },
    { tag: tags.emphasis, fontStyle: 'italic' },
    { tag: tags.strong, fontWeight: 'bold' },
    { tag: tags.strikethrough, textDecoration: 'line-through' },
    { tag: tags.keyword, color: 'light-dark(#708, #d2a8ff)' },
    { tag: [tags.atom, tags.bool, tags.url, tags.contentSeparator, tags.labelName], color: 'light-dark(#219, #79c0ff)' },
    { tag: [tags.literal, tags.inserted], color: 'light-dark(#164, #7ee787)' },
    { tag: [tags.string, tags.deleted], color: 'light-dark(#a11, #ff7b72)' },
    { tag: [tags.regexp, tags.escape, tags.special(tags.string)], color: 'light-dark(#e40, #ffa657)' },
    { tag: tags.definition(tags.variableName), color: 'light-dark(#00f, #79c0ff)' },
    { tag: tags.local(tags.variableName), color: 'light-dark(#30a, #d2a8ff)' },
    { tag: [tags.typeName, tags.namespace], color: 'light-dark(#085, #7ee787)' },
    { tag: tags.className, color: 'light-dark(#167, #56d4dd)' },
    { tag: [tags.special(tags.variableName), tags.macroName], color: 'light-dark(#256, #79c0ff)' },
    { tag: tags.definition(tags.propertyName), color: 'light-dark(#00c, #79c0ff)' },
    { tag: tags.comment, color: 'light-dark(#940, #8b949e)' },
    { tag: tags.invalid, color: 'light-dark(#f00, #ff7b72)' }
]);

/** Sizes the editor to its host so the host decides the height, and matches Bootstrap's look. */
const editorTheme = EditorView.theme({
    '&': { height: '100%', fontSize: '0.95rem', backgroundColor: 'var(--bs-body-bg)' },
    '&.cm-focused': { outline: 'none' },
    '.cm-scroller': {
        overflow: 'auto',
        fontFamily: 'var(--bs-font-monospace)',
        lineHeight: '1.6'
    },
    '.cm-content': { padding: '0.75rem 0' },
    '.cm-line': { padding: '0 0.75rem' }
});

/** Adds the highlight.js theme that esbuild emits next to this bundle (editor.css), once. */
function ensureStylesheet() {
    const href = new URL('./editor.css', import.meta.url).href;
    if (document.querySelector(`link[href="${href}"]`)) {
        return;
    }

    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
}
