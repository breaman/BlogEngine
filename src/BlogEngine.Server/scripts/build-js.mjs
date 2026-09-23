// Bundles the site's JavaScript with esbuild (design 5.3, T1.9, T1.23).
//
//   npm run js-build            one-off minified build of every bundle
//   npm run js-watch            rebuild on every change while developing
//   node ./scripts/build-js.mjs admin|public [--watch]
//                               only one group (the .NET builds of BlogEngine.Client and BlogEngine.Server
//                               each run their own group)
//
// admin:  sources next to the components that use them, in src/BlogEngine.Client/scripts, bundled into
//         src/BlogEngine.Client/wwwroot/js.
// public: sources in src/BlogEngine.Server/scripts, written to src/BlogEngine.Server/wwwroot/js. public.js is
//         loaded on every public page and only minified, never bundled, so its dynamic import of
//         code-blocks.js (highlight.js and copy buttons) stays a separate, lazily loaded file.
//
// The npm packages are installed once, in this project, so every build resolves imports from this
// node_modules folder. Blazor serves the output folders as static web assets.

import * as esbuild from 'esbuild';
import { fileURLToPath } from 'node:url';

const serverDir = fileURLToPath(new URL('..', import.meta.url));
const clientDir = fileURLToPath(new URL('../../BlogEngine.Client/', import.meta.url));
const watch = process.argv.includes('--watch');
const requested = process.argv.slice(2).filter(arg => !arg.startsWith('--'));

/** Options shared by every bundle. @type {import('esbuild').BuildOptions} */
const common = {
    // Loaded as ES modules (import() from .NET, or <script type="module">).
    format: 'esm',
    target: 'es2022',
    minify: true,
    sourcemap: 'linked',
    // Some sources are outside this folder, so tell esbuild where the packages are.
    nodePaths: [`${serverDir}node_modules`],
    logLevel: 'info'
};

/** @type {Record<string, import('esbuild').BuildOptions[]>} */
const groups = {
    admin: [{
        ...common,
        entryPoints: {
            // CodeMirror editor and preview highlighting for MarkdownEditor/MarkdownPreview.
            editor: `${clientDir}scripts/editor.js`,
            // Small helpers for other admin components (TagInput keys, the editor's unsaved-changes link guard).
            admin: `${clientDir}scripts/admin.js`
        },
        outdir: `${clientDir}wwwroot/js`,
        bundle: true
    }],
    public: [
        {
            ...common,
            // highlight.js (the admin preview's language set) and copy buttons, with its theme as code-blocks.css.
            entryPoints: { 'code-blocks': `${serverDir}scripts/code-blocks.js` },
            outdir: `${serverDir}wwwroot/js`,
            bundle: true
        },
        {
            ...common,
            // The loader on every public page: minified only, so the import() above stays lazy.
            entryPoints: { public: `${serverDir}scripts/public.js` },
            outdir: `${serverDir}wwwroot/js`,
            bundle: false
        }
    ]
};

const unknown = requested.filter(name => !(name in groups));
if (unknown.length > 0) {
    console.error(`Unknown bundle group(s): ${unknown.join(', ')}. Expected: ${Object.keys(groups).join(', ')}.`);
    process.exit(1);
}

const builds = (requested.length > 0 ? requested : Object.keys(groups)).flatMap(name => groups[name]);

if (watch) {
    for (const options of builds) {
        const context = await esbuild.context(options);
        await context.watch();
    }
} else {
    await Promise.all(builds.map(options => esbuild.build(options)));
}
