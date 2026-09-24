## Bootstrap
Bootstrap and Bootstrap Icons come from npm and are self-hosted (no CDN), so install the npm packages once after cloning. The build copies `bootstrap.bundle.min.js` and the Bootstrap Icons font files from `node_modules` into `wwwroot`, and it fails if they are missing:

```
cd src/BlogEngine.Server
npm install
```

Since this template utilizes bootstrap scss, the initial css file needs to be generated. There are two scripts included for doing this, one is sass-dev that will run the process in watch mode and the other is sass-prod that will compress the css file for production use. In order to do this perform the following steps in your terminal:

```
cd src/BlogEngine.Server
npm run sass-dev (or sass-prod depending on which one you want)
```

## JavaScript
The site's JavaScript is bundled with esbuild (`src/BlogEngine.Server/scripts/build-js.mjs`) into two groups. The bundles are build output and are not committed.

- **admin**: the editor (CodeMirror 6 and highlight.js), the media upload queue, the image editor (Cropper.js v2) and small admin helpers. Sources in `src/BlogEngine.Client/scripts`, written to `src/BlogEngine.Client/wwwroot/js` (`editor.js`, `editor.css`, `media.js`, `cropper.js`, `admin.js`).
- **public**: `public.js`, a tiny loader on every public page, and `code-blocks.js`/`code-blocks.css` (highlight.js and copy buttons), which it imports only for posts with code blocks. Sources in `src/BlogEngine.Server/scripts`, written to `src/BlogEngine.Server/wwwroot/js`.

You don't normally run anything by hand: building `BlogEngine.Client` builds the admin group and building `BlogEngine.Server` (or `aspire run`) builds the public group, whenever a script or the npm packages changed. They use the same `node_modules` as the Sass build, so `npm install` in `src/BlogEngine.Server` is the only setup. To build or watch the bundles yourself:

```
cd src/BlogEngine.Server
npm run js-build   (or js-watch to rebuild on every change; add "admin" or "public" to build one group:
                    node ./scripts/build-js.mjs public)
```

The Sass output (`wwwroot/css/site.css`) is not rebuilt automatically; run `npm run sass-dev` or `sass-prod` after changing `styles/site.scss`.

## Media library
Uploaded images are stored on the file system under `MediaStorage:RootPath` (default `App_Data/media`, relative to the server's content root; git-ignored). Each item keeps its original upload (`{publicId}/original.{ext}`) and the current edited version (`{publicId}/v{version}/current.{ext}`). When running with Aspire the server is a project on your machine, so the folder survives restarts; to keep uploads elsewhere, set `MediaStorage:RootPath` in the AppHost's user secrets. In a container, mount a persistent volume at that folder. `/health` includes a `media-storage` check that the folder is writable.

Images are processed with [ImageSharp](https://github.com/SixLabors/ImageSharp), licensed under the [Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE): free for open source software and for companies with less than $1M annual gross revenue; others need a commercial license. The package is pinned to 3.x because ImageSharp 4 requires a Six Labors license key at build time.

## EF Migrations
This project adds EF as a dotnet tool, so before running any EF commands, one needs to run the following command from the project folder (there is also a command in the Aspire dashboard to run this restore command if the app is started before the restore command is run manually):

```
dotnet tool restore
```

After creating a new project with this template, migrations need to be run since there is some authentication that has been added. To do this, run the following command in your terminal:

```
cd src/BlogEngine.Server
dotnet ef migrations add InitialDatabase -p ../BlogEngine.Data
```

Migrations will run when the aspire project is started, so no need to run the migration manually after it is created.

## Aspire
This project is configured with aspire and should use the aspire cli, so the recommended way to kick off project execution is the following:

```
aspire run
```

### Admin account
On first run there are no accounts. Browse to `/setup` to create the admin (display name, email and password); the page returns 404 once any account exists. Public registration is closed by default (`AllowRegistration` in site settings).

To skip `/setup`, seed the admin from Aspire parameters in the AppHost's user secrets. They are only used while no account exists:

```
cd aspire/BlogEngine.AppHost
dotnet user-secrets set "Parameters:admin-email" "you@example.com"
dotnet user-secrets set "Parameters:admin-password" "<password>"
```

## Tests
The tests use [TUnit](https://tunit.dev) on Microsoft.Testing.Platform (enabled for `dotnet test` in `global.json`):

- `tests/BlogEngine.UnitTests`: fast tests for shared utilities. Snapshot tests use [Verify](https://github.com/VerifyTests/Verify); commit `*.verified.*` files, never `*.received.*` files. Verify requires a sponsorship declaration: this repo claims the OpenSource exemption in `Directory.Build.props`, which expires 2027-09 and must be renewed.
- `tests/BlogEngine.IntegrationTests`: hosts the server in memory against a SQL Server container (Testcontainers) shared by the whole test run, with migrations applied on startup. **Docker must be running.**

Run everything from the repository root:

```
dotnet test
```
