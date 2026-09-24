// Small DOM helpers for admin components that Blazor can't express on its own. Bundled by
// src/BlogEngine.Server/scripts/build-js.mjs into BlogEngine.Client/wwwroot/js/admin.js.

/**
 * Keyboard defaults for a TagInput text box (T1.12). Blazor's @onkeydown:preventDefault can't depend on
 * which key was pressed, so this listener cancels only the keys the component handles itself; Blazor still
 * receives the event and commits the tag.
 *
 * - Enter never submits the surrounding form.
 * - Comma never types a comma (tags can't contain one).
 * - Tab commits instead of moving focus, but only when there is text to commit.
 * - Arrow keys move through the suggestions instead of the caret while the list is open.
 *
 * @param {HTMLInputElement} input
 * @returns {{ dispose: () => void }}
 */
export function attachTagInput(input) {
    const onKeyDown = event => {
        const hasText = input.value.trim().length > 0;
        const listOpen = input.getAttribute('aria-expanded') === 'true';

        switch (event.key) {
            case 'Enter':
            case ',':
                event.preventDefault();
                break;
            case 'Tab':
                if (hasText && !event.shiftKey) {
                    event.preventDefault();
                }
                break;
            case 'ArrowDown':
            case 'ArrowUp':
                if (listOpen) {
                    event.preventDefault();
                }
                break;
        }
    };

    input.addEventListener('keydown', onKeyDown);

    return {
        dispose: () => input.removeEventListener('keydown', onKeyDown)
    };
}

/**
 * Asks .NET before following in-app links while `enabled` (T1.13). Needed because a Blazor Web App with
 * static routing handles link clicks with enhanced navigation, which doesn't run NavigationLock's
 * OnBeforeInternalNavigation; only programmatic navigation does. The listener runs in the capture phase so
 * it sees the click before Blazor's enhanced navigation, and hands the URL to `OnGuardedLinkClicked`, which
 * decides and navigates programmatically. Reloads, closing the tab and typed URLs are covered by
 * NavigationLock's beforeunload prompt instead.
 *
 * @param {any} dotNet DotNetObjectReference with an `OnGuardedLinkClicked(href)` method.
 * @returns {{ setEnabled: (enabled: boolean) => void, dispose: () => void }}
 */
export function guardLinkNavigation(dotNet) {
    let enabled = false;

    const onClick = event => {
        if (!enabled || event.defaultPrevented || event.button !== 0
            || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }

        const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
        // Bare "#id" links (heading anchors and footnotes in the editor preview) stay on the page; editor.js scrolls.
        if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')
            || link.getAttribute('href').startsWith('#')) {
            return;
        }

        const url = new URL(link.href, document.baseURI);
        const samePage = url.pathname === location.pathname && url.search === location.search;
        if (url.origin !== location.origin || (samePage && url.hash)) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        dotNet.invokeMethodAsync('OnGuardedLinkClicked', url.href);
    };

    document.addEventListener('click', onClick, true);

    return {
        setEnabled: value => { enabled = value === true; },
        dispose: () => document.removeEventListener('click', onClick, true)
    };
}
