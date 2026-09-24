// Enhancements for the public site (design 5.1, 10.3, T1.23, T3.3). Loaded on every page, so it stays tiny: it
// looks for a post whose render flagged code blocks (data-code-blocks on .post-content) and then imports the
// highlight.js bundle, so text-only pages never download highlighting code; and it adds "remember me" to the
// comment form.
//
// Copied (minified, not bundled) by scripts/build-js.mjs into BlogEngine.Server/wwwroot/js/public.js. It must
// stay unbundled: esbuild would otherwise inline code-blocks.js and defeat the lazy load.

const codeBlocksSelector = '.post-content[data-code-blocks]';

/** Highlights and adds copy buttons to the code blocks on the current page, if it has any. */
async function enhanceCodeBlocks() {
    const containers = document.querySelectorAll(codeBlocksSelector);
    if (containers.length === 0) {
        return;
    }

    const { enhance } = await import(new URL('./code-blocks.js', import.meta.url).href);
    for (const container of containers) {
        enhance(container);
    }
}

const rememberKey = 'blogengine:commenter';
const rememberedForms = new WeakSet();

/**
 * "Remember me" for the comment form (design 8.1): the name, email and website live in this browser's
 * localStorage, never in a cookie, so nothing is sent to the server until the reader posts a comment.
 */
function rememberCommenter() {
    const form = document.querySelector('form[data-comment-form]');
    const option = form?.querySelector('[data-comment-remember]');
    const checkbox = option?.querySelector('input[type="checkbox"]');
    if (!checkbox) {
        return;
    }

    const fields = form.querySelectorAll('[data-remember-field]');
    let saved = null;
    try {
        saved = JSON.parse(localStorage.getItem(rememberKey) ?? 'null');
    } catch {
        // Storage blocked or unreadable: the form still works, it just won't be pre-filled.
    }

    option.hidden = false;
    if (saved) {
        checkbox.checked = true;
        for (const field of fields) {
            field.value ||= saved[field.dataset.rememberField] ?? '';
        }
    }

    // An enhanced form post patches the same form element, so only listen once per element.
    if (rememberedForms.has(form)) {
        return;
    }

    rememberedForms.add(form);
    form.addEventListener('submit', () => {
        try {
            if (checkbox.checked) {
                const values = Object.fromEntries([...fields].map(field => [field.dataset.rememberField, field.value.trim()]));
                localStorage.setItem(rememberKey, JSON.stringify(values));
            } else {
                localStorage.removeItem(rememberKey);
            }
        } catch {
            // Storage full or blocked: posting the comment matters more than remembering the details.
        }
    });
}

function enhancePage() {
    enhanceCodeBlocks();
    rememberCommenter();
}

enhancePage();

// Blazor's enhanced navigation swaps page content without a full load, so scripts don't rerun; enhance again
// after each enhanced page update. blazor.web.js is a classic script earlier in the page, so Blazor is defined.
window.Blazor?.addEventListener('enhancedload', enhancePage);
