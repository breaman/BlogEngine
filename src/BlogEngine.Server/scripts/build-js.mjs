// Bundles the admin JavaScript with esbuild (design 5.3, T1.9).
//
//   npm run js-build   one-off minified build (the .NET build of BlogEngine.Client runs this)
//   npm run js-watch   rebuild on every change while developing
//
// The sources live next to the components that use them, in src/BlogEngine.Client/scripts, and the bundles
// are written to src/BlogEngine.Client/wwwroot/js, where Blazor serves them as static web assets. The npm
// packages are installed once, in this project, so the build resolves imports from this node_modules folder.

import * as esbuild from 'esbuild';
import { fileURLToPath } from 'node:url';

const serverDir = fileURLToPath(new URL('..', import.meta.url));
const clientDir = fileURLToPath(new URL('../../BlogEngine.Client/', import.meta.url));
const watch = process.argv.includes('--watch');

/** @type {import('esbuild').BuildOptions} */
const options = {
    entryPoints: {
        // CodeMirror editor and preview highlighting for MarkdownEditor/MarkdownPreview.
        editor: `${clientDir}scripts/editor.js`,
        // Small helpers for other admin components (TagInput keys, the editor's unsaved-changes link guard).
        admin: `${clientDir}scripts/admin.js`
    },
    outdir: `${clientDir}wwwroot/js`,
    bundle: true,
    // Loaded with import() from .NET through IJSRuntime, so the bundles are ES modules.
    format: 'esm',
    target: 'es2022',
    minify: true,
    sourcemap: 'linked',
    // The sources are outside this folder, so tell esbuild where the packages are.
    nodePaths: [`${serverDir}node_modules`],
    logLevel: 'info'
};

if (watch) {
    const context = await esbuild.context(options);
    await context.watch();
} else {
    await esbuild.build(options);
}
