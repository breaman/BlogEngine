// Enhancements for the public site (design 5.1, 10.3, T1.23, T3.3, T4.14). Loaded on every page, admin included, so it
// stays tiny: it looks for a post whose render flagged code blocks (data-code-blocks on .post-content) and then imports
// the highlight.js bundle, so text-only pages never download highlighting code; it adds "remember me" to the comment
// form; and it runs the light/dark theme menu (theme.js, loaded in <head>, applies the theme itself).
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

const themeIcons = { light: 'bi-sun-fill', dark: 'bi-moon-stars-fill', auto: 'bi-circle-half' };

/**
 * Shows the reader's theme choice in every theme menu on the page (ThemeToggle, in the public navbar or the admin
 * layout): the button's icon and label, and which option is pressed.
 */
function syncThemeMenus() {
    const choice = window.blogEngineTheme?.get();
    if (!choice) {
        return;
    }

    for (const menu of document.querySelectorAll('.theme-toggle')) {
        const icon = menu.querySelector('[data-theme-icon]');
        if (icon) {
            icon.className = `bi ${themeIcons[choice]}`;
        }

        menu.querySelector('.dropdown-toggle')?.setAttribute('aria-label', `Color theme: ${choice}`);
        for (const option of menu.querySelectorAll('[data-theme-value]')) {
            const selected = option.dataset.themeValue === choice;
            option.classList.toggle('active', selected);
            option.setAttribute('aria-pressed', String(selected));
        }
    }
}

// One delegated listener, so menus re-rendered by enhanced navigation keep working without being wired up again.
document.addEventListener('click', event => {
    const option = event.target instanceof Element ? event.target.closest('[data-theme-value]') : null;
    if (option) {
        window.blogEngineTheme?.set(option.dataset.themeValue);
    }
});
window.addEventListener('blogengine:themechange', syncThemeMenus);

function enhancePage() {
    enhanceCodeBlocks();
    rememberCommenter();
    syncThemeMenus();
}

enhancePage();

// Blazor's enhanced navigation swaps page content without a full load, so scripts don't rerun; enhance again
// after each enhanced page update. blazor.web.js is a classic script earlier in the page, so Blazor is defined.
window.Blazor?.addEventListener('enhancedload', enhancePage);
