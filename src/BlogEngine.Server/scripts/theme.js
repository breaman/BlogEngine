// Light and dark themes (P14, T4.14) with Bootstrap 5.3 color modes: sets data-bs-theme on <html> from the reader's
// choice in localStorage, or from the OS setting when they chose "auto" (the default).
//
// Loaded as a small blocking script in <head>, before anything is painted, so a dark page never flashes light. Built
// (minified, not bundled) by scripts/build-js.mjs into BlogEngine.Server/wwwroot/js/theme.js. The menu that changes the
// choice is wired up by public.js through window.blogEngineTheme.

(() => {
    const storageKey = 'blogengine:theme';
    const root = document.documentElement;
    const darkQuery = window.matchMedia('(prefers-color-scheme: dark)');

    // The choice made on this page when localStorage is blocked (private mode, strict settings).
    let unsavedChoice = 'auto';

    /** The reader's choice: 'light', 'dark' or 'auto'. */
    function preference() {
        try {
            const value = localStorage.getItem(storageKey);
            return value === 'light' || value === 'dark' ? value : 'auto';
        } catch {
            return unsavedChoice;
        }
    }

    /** The theme to show for a choice. */
    function resolve(choice) {
        return choice === 'auto' ? (darkQuery.matches ? 'dark' : 'light') : choice;
    }

    function apply() {
        const theme = resolve(preference());
        // Only write when it differs, so the observer below doesn't trigger itself forever.
        if (root.getAttribute('data-bs-theme') !== theme) {
            root.setAttribute('data-bs-theme', theme);
        }
    }

    apply();

    // Blazor's enhanced navigation copies the server's <html> attributes onto the page, which removes data-bs-theme.
    // Mutation observer callbacks run before the next paint, so putting it back here causes no flash.
    new MutationObserver(apply).observe(root, { attributes: true, attributeFilter: ['data-bs-theme'] });

    // Follow OS changes in "auto", and choices made in another tab.
    darkQuery.addEventListener('change', apply);
    window.addEventListener('storage', event => {
        if (event.key === storageKey) {
            apply();
            window.dispatchEvent(new CustomEvent('blogengine:themechange'));
        }
    });

    window.blogEngineTheme = {
        /** The reader's choice: 'light', 'dark' or 'auto'. */
        get: preference,

        /** Saves and applies a choice. */
        set(choice) {
            unsavedChoice = choice === 'light' || choice === 'dark' ? choice : 'auto';
            try {
                if (unsavedChoice === 'auto') {
                    localStorage.removeItem(storageKey);
                } else {
                    localStorage.setItem(storageKey, unsavedChoice);
                }
            } catch {
                // Can't remember it across pages; it still applies to this one.
            }

            apply();
            window.dispatchEvent(new CustomEvent('blogengine:themechange'));
        }
    };
})();
