// Enhancements for the public site (design 5.1, 10.3, T1.23). Loaded on every page, so it stays tiny: it only
// looks for a post whose render flagged code blocks (data-code-blocks on .post-content) and then imports the
// highlight.js bundle, so text-only pages never download highlighting code.
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

enhanceCodeBlocks();

// Blazor's enhanced navigation swaps page content without a full load, so scripts don't rerun; enhance again
// after each enhanced page update. blazor.web.js is a classic script earlier in the page, so Blazor is defined.
window.Blazor?.addEventListener('enhancedload', enhanceCodeBlocks);
