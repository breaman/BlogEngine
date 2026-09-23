// Code highlighting and copy buttons for public post pages (design 10.3, P12, T1.23). Bundled by
// scripts/build-js.mjs into BlogEngine.Server/wwwroot/js/code-blocks.js (and the theme into code-blocks.css), and
// imported by public.js only on pages whose post has code blocks.
//
// It uses the same highlight.js language set and theme as the admin preview (BlogEngine.Client/scripts), so a
// published post looks exactly like its preview.

import hljs from '../../BlogEngine.Client/scripts/highlighter.js';
import 'highlight.js/styles/github.css';

/** How long the "Copied" state is shown on a copy button. */
const copiedResetMs = 2000;

ensureStylesheet();

/**
 * Highlights every code block under `container` and gives each one a copy button. Safe to call again on the
 * same content: blocks that were already handled are skipped.
 * @param {Element} container
 */
export function enhance(container) {
    for (const code of container.querySelectorAll('pre > code:not([data-highlighted])')) {
        hljs.highlightElement(code);
    }

    for (const pre of container.querySelectorAll('pre:not([data-copy-button])')) {
        addCopyButton(pre);
    }
}

/** Adds a button that copies the block's text to the clipboard. */
function addCopyButton(pre) {
    const code = pre.querySelector('code') ?? pre;
    pre.dataset.copyButton = 'true';
    pre.classList.add('has-copy-button');

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'code-copy-button btn btn-sm btn-light border';
    setState(button, 'idle');

    button.addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(code.innerText);
            setState(button, 'copied');
        } catch {
            // The Clipboard API needs a secure context and permission; say so rather than fail silently.
            setState(button, 'failed');
        }

        clearTimeout(button.resetTimer);
        button.resetTimer = setTimeout(() => setState(button, 'idle'), copiedResetMs);
    });

    pre.appendChild(button);
}

/** Updates a copy button's icon and accessible label. */
function setState(button, state) {
    const [icon, label] = {
        idle: ['bi-clipboard', 'Copy code'],
        copied: ['bi-clipboard-check', 'Copied'],
        failed: ['bi-x-lg', 'Copy failed']
    }[state];

    button.replaceChildren();
    const glyph = document.createElement('span');
    glyph.className = `bi ${icon}`;
    glyph.setAttribute('aria-hidden', 'true');
    button.appendChild(glyph);
    button.setAttribute('aria-label', label);
    button.title = label;
}

/** Adds the highlight.js theme that esbuild emits next to this bundle (code-blocks.css), once. */
function ensureStylesheet() {
    const href = new URL('./code-blocks.css', import.meta.url).href;
    if (document.querySelector(`link[href="${href}"]`)) {
        return;
    }

    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
}
