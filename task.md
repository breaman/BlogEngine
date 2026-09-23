# BlogEngine — Task Tracker

| | |
|---|---|
| **Source** | [`design.md`](design.md), [`requirements.md`](requirements.md) |
| **Created** | 2026-09-22 |
| **How to use** | Check off a task (`- [x]`) when every "Done when" item is true. Tasks within a phase are roughly in dependency order. Feature IDs (A1, P3, M2, …) and § references point back to `design.md`. |

---

## Progress Summary

| Phase | Goal | Tasks | Done |
|---|---|---|---|
| [0: Foundation](#phase-0--foundation) | Admin can log in to an empty dashboard | 16 | 0 |
| [1: Posts MVP](#phase-1--posts-mvp) | Can write and publish posts end to end | 25 | 0 |
| [2: Media MVP](#phase-2--media-mvp) | Can add, edit and insert photos | 13 | 0 |
| [3: Comments MVP](#phase-3--comments-mvp) | Readers can comment; the author moderates | 11 | 0 |
| [4: V1 Polish](#phase-4--v1-polish) | Ready for public launch | 35 | 0 |
| [5: Later](#phase-5--later) | As desired | 10 | 0 |

Update the **Done** column as tasks are completed.

---

## Resolved Decisions

The open questions in `design.md` §21 are resolved with the recommended answers. Tasks below assume these.

| # | Decision | Affects |
|---|---|---|
| Q1 | Commenters are anonymous: name + email (+ optional website). No accounts. | T3.3 |
| Q2 | Every comment requires approval at launch. The "auto-approve returning commenters" setting exists but defaults **off**. | T3.4, T4.20 |
| Q3 | Edits to a published post are **staged** until **Update** is clicked; autosave never changes live content. | T1.11, T1.13 |
| Q4 | Scheduled publishing is in scope (V1). | T4.1 |
| Q5 | Media is stored on the **file system** behind `IMediaStorage`; Azure Blob comes later. | T2.1, T5.8 |
| Q6 | Accept **JPEG, PNG, GIF, WebP** only. HEIC is rejected with a friendly message. | T2.3 |
| Q7 | Code highlighting is **client-side highlight.js**, loaded only on pages with code blocks. | T1.23 |
| Q8 | Rotation is **90° steps plus flips**. Freeform straightening comes later. | T2.9, T5.10 |
| Q9 | No email newsletter. RSS covers subscriptions. | (not planned) |
| Q10 | URL dates use the blog's **configured local time zone** (`SiteSettings.TimeZoneId`). | T0.8, T1.2 |
| Q11 | Standalone pages (About, etc.) ship in **V1**. | T4.10 |

---

## Conventions for Every Task

These apply to all tasks and are not repeated below:

- Interactive UI lives in `BlogEngine.Client` with `@rendermode InteractiveWebAssembly` only (never InteractiveServer), using the dual-service pattern and `[PersistentState]` + `??=` loads (`.claude/rules/blazor.instructions.md`).
- No `@code` blocks in `.razor` files; use code-behind (`.razor.cs`).
- Form inputs are wrapped in Bootstrap `form-floating`. Icons are Bootstrap Icons.
- New entities derive from `FingerPrintEntityBase` (join tables excepted) and get an `IEntityTypeConfiguration<T>`. String lengths come from `FieldLengths`.
- Every schema change ships with an EF Core migration.
- Every new service/utility with logic gets unit tests; every new endpoint or public route gets integration tests.

---

## Phase 0 — Foundation

**Exit criteria:** the admin can log in and see an empty dashboard; shared utilities are tested.

### Project setup and cleanup

- [ ] **T0.1 — Create test projects**
  - Add `tests/BlogEngine.UnitTests` and `tests/BlogEngine.IntegrationTests` (**TUnit**) to `BlogEngine.slnx`, with package versions in `Directory.Packages.props`.
  - TUnit runs on Microsoft.Testing.Platform: set `"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json` so `dotnet test` works on the .NET 10 SDK.
  - Snapshot tests (T0.13, T2.7) use `Verify.TUnit`.
  - Integration tests: `TUnit.AspNetCore`'s `TestWebApplicationFactory<Program>` + a SQL Server test container (Testcontainers), shared across the run with `[ClassDataSource<…>(Shared = SharedType.PerTestSession)]`; the fixture applies migrations on startup.
  - **Done when:** both projects build, a placeholder test passes in each, and the tests run in Visual Studio Enterprise and via `dotnet test`.

- [ ] **T0.2 — Replace `FieldLengths` with blog-specific constants** (§2, §6.9)
  - Remove the barcode/promotion/payment constants; add the constants listed in §6.9.
  - **Done when:** the solution builds with no references to the old constants.

- [ ] **T0.3 — Remove template sample content**
  - Remove `ClientHello.razor` and any other sample pages/nav links that aren't part of the blog.
  - **Done when:** nav shows only real routes; solution builds.

- [ ] **T0.4 — Self-host Bootstrap Icons** (§12.2)
  - Add `bootstrap-icons` to `src/BlogEngine.Server/package.json`, copy/bundle the font + CSS into `wwwroot`, and remove the CDN `<link>` from `App.razor`.
  - **Done when:** icons render with no external requests.

- [ ] **T0.5 — Add core NuGet packages** (§5.3)
  - `Markdig` (Shared), `HtmlSanitizer` (Server), `System.ServiceModel.Syndication` (Server). (ImageSharp is added in Phase 2.)
  - **Done when:** packages are centrally versioned and restore cleanly.

### Domain model and database

- [ ] **T0.6 — Add enums and core entities** (§6.2–6.7)
  - Enums: `PostStatus` (`Draft`, `Published`), `CommentStatus` (`Pending`, `Approved`, `Rejected`, `Spam`), `CommentBlockKind` (`Email`, `IpHash`, `Keyword`, `Domain`), `RevisionKind` (`Autosave`, `Manual`, `Publish`).
  - Entities: `Post`, `PostRevision`, `Tag`, `PostTag`, `Comment`, `CommentBlock`, `MediaItem`, `MediaRendition`, `PostMedia`, `Page`, `Redirect`, `SiteSettings`, `PreviewToken`.
  - `Post` and `Page` implement `ISoftDeletable`; `Post` has a `RowVersion` concurrency token.
  - **Done when:** all entities exist with properties matching §6 and are registered as `DbSet`s on `ApplicationDbContext`.

- [ ] **T0.7 — Entity configurations and indexes** (§6.2, §6.4, §6.7, §6.8)
  - `Post`: globally unique `Slug`; unique `(PublishedDateLocal, Slug)` filtered on `Status = Published`; index `(Status, PublishedOn DESC)`.
  - `Tag`: unique `NormalizedName`, unique `Slug`. `PostTag`: composite PK.
  - `Comment`: self-reference via `ParentCommentId`. `Redirect`: unique `FromPath`. `MediaItem`: unique `PublicId`, index on `ContentHash`.
  - Global query filter hiding soft-deleted `Post`/`Page` rows; cascade deletes from `Post` to `PostTag`, `Comment`, `PostRevision`, `PostMedia`, `PreviewToken`.
  - **Done when:** configurations compile and the model validates (no EF warnings about keys or relationships).

- [ ] **T0.8 — `SiteSettings` single row + `ISettingsService`** (§13)
  - All settings groups from §13 as columns with defaults (posts per page = 10, feeds = full content, comments enabled, require approval = true, auto-approve returning = false, close after 0 days, max links = 2, avatars off, max upload 20 MB, rendition widths 320/640/960/1280/1920, `TimeZoneId`, `AllowRegistration = false`, discourage search engines = false).
  - Seed the single row in a migration. `ISettingsService` in Shared; server implementation cached with `HybridCache` and evicted on save.
  - **Done when:** settings can be read anywhere via DI and a unit/integration test proves the cache is evicted on update.

- [ ] **T0.9 — Initial blog migration**
  - Create the migration for T0.6–T0.8 and verify the Aspire EF migrations resource applies it.
  - **Done when:** `aspire run` starts with an up-to-date database containing all new tables.

### Shared utilities (with unit tests)

- [ ] **T0.10 — `SlugGenerator`** (§7.2)
  - Lowercase, strip diacritics, non-alphanumeric runs → `-`, trim hyphens, truncate to 80 chars at a word boundary; helper for `-2`, `-3` collision suffixes.
  - **Done when:** unit tests cover diacritics, punctuation, long titles, empty/whitespace input and collision suffixing.

- [ ] **T0.11 — `TagNormalizer` and tag slugs** (§6.4)
  - Trim, collapse whitespace, NFKC; `NormalizedName = ToUpperInvariant()`; reject empty, >50 chars, commas/semicolons.
  - Tag slugs map `#`→`sharp`, `+`→`plus`, leading `.`→`dot`, `&`→`and` before slugifying.
  - **Done when:** tests prove `C#`/`c#`/` c# ` collide, `C#` ≠ `C`, `C++` → `cplusplus`, `.NET` → `dotnet`, and Unicode cases behave.

- [ ] **T0.12 — `ReadingTime` and summary generation** (§6.2, A7, A12)
  - Word count and reading minutes from Markdown (ignoring code fences/syntax); auto-summary from the first ~160 characters of plain text.
  - **Done when:** unit tests pass for plain text, code-heavy and very short posts.

- [ ] **T0.13 — `BlogMarkdownPipeline` (post + comment pipelines)** (§10.1, §8.2)
  - Post pipeline: pipe/grid tables, task lists, footnotes, auto identifiers, emphasis extras, auto links, media links (YouTube/Vimeo → `youtube-nocookie.com`), generic attributes, figures, precise source locations. Raw HTML allowed.
  - Comment pipeline: paragraphs, emphasis, inline code, code blocks, links, blockquotes, lists only; `DisableHtml()`.
  - `ExternalLinkRewriter`: `rel="noopener"` + external-link icon. Expose a "contains code blocks" flag on the render result.
  - Must run in WebAssembly (no server-only APIs).
  - **Done when:** snapshot tests cover each extension and prove the comment pipeline drops headings, images, tables and HTML.

- [ ] **T0.14 — Public visibility rule** (§6.3)
  - `IQueryable<Post>.VisibleToPublic(TimeProvider)` = `Published && PublishedOn <= now && !IsDeleted`. Register `TimeProvider.System` in DI.
  - Time-zone helper that computes `PublishedDateLocal` from a UTC `DateTimeOffset` and `TimeZoneId`.
  - **Done when:** unit tests cover draft, scheduled, published, deleted, and midnight/DST edge cases for the local date.

### Security and admin shell

- [ ] **T0.15 — Admin account, `/setup` and registration lockdown** (§12.1, O5)
  - `AdminOnly` authorization policy (requires `Admin` role).
  - `/setup` (static SSR): only when no users exist; creates the admin with email, password and display name and assigns `Admin`; returns 404 afterwards.
  - Optional seeding from Aspire parameters `admin-email` / `admin-password`.
  - `/Account/Register` returns 404 and its nav link is hidden when `AllowRegistration = false`.
  - **Done when:** integration tests prove `/setup` works once then 404s, and registration 404s by default.

- [ ] **T0.16 — Admin layout, dashboard shell and admin API plumbing** (§5.1, §7.3, §7.4)
  - `AdminLayout.razor` in `BlogEngine.Client` with nav (Dashboard, Posts, Media, Comments, Tags, Settings) and a link back to the public site.
  - `/admin` dashboard page (empty cards for now), protected by `AdminOnly`.
  - `/api/admin` route group requiring `AdminOnly` + antiforgery validation on mutating verbs.
  - Client `DelegatingHandler` that sends the antiforgery token header on the WASM `HttpClient`.
  - **Done when:** logging in as admin shows the dashboard; anonymous users are redirected to login; an integration test proves an `/api/admin` POST without the token is rejected.

---

## Phase 1 — Posts MVP

**Exit criteria:** you can write, preview, tag and publish a post, and readers can find it via `/posts`, archives, tags, feeds and the sitemap.

### Post services and API

- [ ] **T1.1 — Post DTOs, validators and `IPostAdminService` contract** (§5.2)
  - DTOs: `PostEditDto`, `PostSummaryDto`, `PostListQuery`, `TagDto`, publish request (`publishOn?`). Shared validators (title required, lengths, slug format `[a-z0-9-]`).
  - **Done when:** contracts compile in Shared and validators have unit tests.

- [ ] **T1.2 — `ServerPostAdminService`: create, update, save pipeline** (§6.2, §7.2)
  - On save: render `ContentHtml` (sanitized with the permissive post allowlist: `iframe` only from YouTube/Vimeo), compute word count/reading time, auto-summary if blank, slug from title while draft, `PublishedDateLocal`, rebuild `PostMedia`.
  - Optimistic concurrency via `RowVersion` → a typed conflict result.
  - **Done when:** integration tests cover create, update, slug uniqueness (`-2` suffix), and a stale `RowVersion` conflict.

- [ ] **T1.3 — Tag resolution on save** (§6.4, A4)
  - Match existing tags by `NormalizedName` (keeping original casing); create new ones in the same transaction; on a unique-violation race, reload and reuse the existing tag.
  - **Done when:** an integration test with two concurrent saves creating `c#` and `C#` ends with exactly one tag.

- [ ] **T1.4 — Publish and unpublish** (§6.3, A3)
  - Publish now sets `Status = Published`, `PublishedOn` (first publish only unless edited), writes a `Publish` revision, and locks the slug.
  - Unpublish returns to `Draft` (public URL 404s). MVP rejects a future `publishOn` (scheduling is T4.1).
  - **Done when:** integration tests prove a draft is never visible publicly and a published post is.

- [ ] **T1.5 — Automatic redirects on slug/date change** (§6.7, P16)
  - When a published post's slug or `PublishedDateLocal` changes, insert a `Redirect` (old path → new path, 301). Collapse chains so old redirects point to the newest URL.
  - **Done when:** an integration test changes a published slug and the old URL returns 301 to the new one.

- [ ] **T1.6 — Autosave with staged changes** (§10.2, A6, Q3)
  - Autosave writes a `PostRevision(Kind = Autosave)`, keeping only the latest 20 autosaves per post.
  - Drafts: autosave also updates the post. Published posts: autosave stores a pending revision only; **Update** applies it.
  - **Done when:** an integration test proves autosaving a published post leaves the public content unchanged until Update.

- [ ] **T1.7 — Admin post and tag API endpoints** (§7.4)
  - `GET /posts` (status/tag/text filter, paged), `GET /posts/{id}`, `POST /posts`, `PUT /posts/{id}`, `POST /posts/{id}/autosave`, `POST /posts/{id}/publish`, `POST /posts/{id}/unpublish`, `DELETE /posts/{id}` (to trash via soft delete), `POST /posts/slug-check`.
  - `GET /tags?search=` (top 10 by usage, case-insensitive).
  - **Done when:** each endpoint has an integration test including a 401/403 for non-admins and 409 for concurrency conflicts.

- [ ] **T1.8 — Client service implementations**
  - `ClientPostAdminService`, `ClientTagService` over `HttpClient`; register in `Client/Program.cs`; server implementations registered in `Server/Program.cs`.
  - **Done when:** admin pages work both prerendered and after WASM hydration.

### Admin UI

- [ ] **T1.9 — JS build pipeline (esbuild)** (§5.3)
  - Add `esbuild`, `codemirror` v6 and `@codemirror/lang-markdown` to npm; add a `js-build` script next to `sass-dev`/`sass-prod` that outputs `BlogEngine.Client/wwwroot/js/editor.js`. Wire it into the build (or document the manual step in `README.md`).
  - **Done when:** a clean clone can produce `editor.js` with one command.

- [ ] **T1.10 — `MarkdownEditor` component (CodeMirror interop)** (§10.2, A1, A11)
  - CodeMirror with Markdown highlighting, line wrapping, list continuation.
  - Toolbar + shortcuts: `Ctrl/Cmd+B`, `I`, `K` (link), `` ` `` (code), heading, quote, list, `Shift+I` (image — wired in Phase 2), `S` (save).
  - Debounce changes 200 ms in JS before calling .NET. Dispose interop cleanly.
  - **Done when:** typing, toolbar actions and shortcuts work, and navigating away doesn't leak JS instances.

- [ ] **T1.11 — `MarkdownPreview` and split layout** (§10.1–10.2, A2)
  - Renders with the shared `BlogMarkdownPipeline` in WASM (no server round trip); runs highlight.js on the preview.
  - Layout toggle: split / editor only / preview only; tabs on narrow screens.
  - Scroll sync using Markdig source positions.
  - **Done when:** preview output matches the published page for the same Markdown.

- [ ] **T1.12 — `TagInput` component** (§6.4, A4)
  - Chips; autocomplete from `GET /tags?search=`; Enter/Tab/comma commits; typing a match of an existing tag produces that tag's original casing; client-side duplicate prevention via `TagNormalizer`.
  - **Done when:** typing `c#` with an existing `C#` tag yields the chip `C#`, and duplicates can't be added.

- [ ] **T1.13 — Post editor page** (`/admin/posts/new`, `/admin/posts/{id}`; §10.2)
  - Title, editor + preview, sidebar: publish controls (Publish now / Update / Unpublish), slug with inline validation and "changing this creates a redirect" warning once published, summary, tags, allow comments, featured, word count and reading time.
  - Autosave every 30 s and on blur when dirty; status indicator ("Saved 10:42").
  - `localStorage` backup under `draft:{postId}` with a "Restore unsaved changes from 10:42?" prompt.
  - `NavigationLock` + `beforeunload` warning on unsaved changes.
  - Concurrency conflict dialog: "Changed in another tab. Reload or overwrite?"
  - **Done when:** a post can be created, autosaved, recovered after a simulated crash, and published from this page.

- [ ] **T1.14 — Posts list page** (`/admin/posts`; O2)
  - Status filter tabs (All, Drafts, Published; Scheduled/Trash added in Phase 4), tag filter, text search, paging; row actions: edit, view, unpublish, delete.
  - **Done when:** filters combine correctly and prerendered state carries over to WASM.

- [ ] **T1.15 — Settings page** (`/admin/settings`; O4)
  - Edit the §13 settings that matter for MVP: identity (title, tagline, description, author name/bio, social links), reading (posts per page, feed mode, home mode), time zone (IANA picker), comments policy, SEO "discourage search engines".
  - `GET/PUT /api/admin/settings` endpoints.
  - **Done when:** settings save, cache is evicted, and public pages reflect changes.

- [ ] **T1.16 — Time zone change maintenance** (§7.1)
  - When `TimeZoneId` changes, recompute `PublishedDateLocal` for all posts and create redirects for any URLs that changed.
  - **Done when:** an integration test changes the time zone and old post URLs 301 to the new ones.

### Public site (static SSR)

- [ ] **T1.17 — `PublicPostQueries` + `HybridCache` + `CacheInvalidator`** (§11)
  - Queries for lists, archives, tag counts, single post by slug — all via `VisibleToPublic`.
  - Cache entries tagged `posts`, `post:{id}`, `tag:{id}`; `CacheInvalidator` evicts on publish, update, unpublish, delete and settings change.
  - **Done when:** an integration test proves a publish is visible immediately despite caching.

- [ ] **T1.18 — Public layout and shared blog components** (§5.2, §14)
  - Public `MainLayout` with site title/tagline, nav, search box placeholder (GET form, wired in Phase 4), footer with social links.
  - Components: `PostCard`, `Pager` (« Newer / Page X of Y / Older »), `TagBadge`, `Breadcrumbs`, basic `SeoHead` (title, meta description, canonical URL, feed autodiscovery `<link>`).
  - **Done when:** components render with no WebAssembly loaded on public pages.

- [ ] **T1.19 — `/posts` index and home page** (§7.1, P3)
  - `/posts`: title, date, reading time, tags, summary, newest first, `?page=N` (canonical omits `?page=1`).
  - `/`: featured post(s) then latest N posts, with a link to `/posts`.
  - **Done when:** paging, ordering and featured pinning work, and out-of-range pages 404.

- [ ] **T1.20 — Year, month and day archives** (§7.1, P2, P4)
  - `/posts/{year}`, `/posts/{year}/{month}`, `/posts/{year}/{month}/{day}` reusing the list component; headings like "Posts from September 2026"; breadcrumbs to parent periods; accept `9` and `09` but always link with two digits.
  - A valid date with no visible posts returns 404.
  - **Done when:** integration tests cover 200, 404 (empty period and invalid date) for each level.

- [ ] **T1.21 — Post page** (`/posts/{yyyy}/{mm}/{dd}/{slug}`; §7.1, §14.2, P1)
  - Resolution: find visible post by slug → 301 to canonical if the date doesn't match → else check `Redirect` table (301) → else `NavigationManager.NotFound()`.
  - Renders title, date (linked to the day archive), reading time, tags, cached `ContentHtml`, tags again, share links (plain links).
  - **Done when:** integration tests cover 200, wrong-date 301, redirect-table 301, draft 404 and unknown 404.

- [ ] **T1.22 — Tag index and tag pages** (§7.1, P5)
  - `/tags` with post counts (only tags with ≥1 visible post); `/tags/{slug}` paginated using the list component.
  - **Done when:** tags with no visible posts don't appear and their pages 404.

- [ ] **T1.23 — Code highlighting and copy button** (§10.3, P12, Q7)
  - Add `highlight.js` to npm; build a small deferred module in `BlogEngine.Server/wwwroot/js` that highlights and adds copy buttons.
  - Load it only when the post's render result flags code blocks.
  - **Done when:** a text-only post loads no highlighting JS; a code post is highlighted with working copy buttons.

- [ ] **T1.24 — RSS and Atom feeds** (§16, P6)
  - `/feed.xml` (RSS 2.0), `/atom.xml`, `/tags/{slug}/feed.xml`: latest 20 visible posts, full sanitized HTML with absolute URLs, tags as categories. Output-cached; evicted by `CacheInvalidator`.
  - **Done when:** feeds validate (W3C feed validator) and drafts never appear.

- [ ] **T1.25 — Sitemap and robots.txt** (§16, P7)
  - `/sitemap.xml`: home, `/posts`, every visible post (`lastmod = LastUpdatedOn ?? PublishedOn`), tag pages with posts.
  - `/robots.txt`: `Disallow: /admin`, `/api`, `/preview`, `/Account`, a `Sitemap:` line, and `Disallow: /` when "discourage search engines" is on (plus `noindex` meta sitewide).
  - **Done when:** integration tests verify contents for both settings states.

---

## Phase 2 — Media MVP

**Exit criteria:** you can upload, crop/rotate/resize, and insert photos into posts; EXIF GPS data never survives upload.

- [ ] **T2.1 — `IMediaStorage` + `FileSystemMediaStorage`** (§9.3, Q5)
  - Interface from §9.3; file system implementation rooted at a configurable path (default `App_Data/media`); key layout `{publicId}/original.{ext}`, `{publicId}/v{version}/current.{ext}`.
  - Aspire: bind mount/volume so media survives restarts. Health check for storage writability (§18).
  - **Done when:** unit/integration tests cover save, read, delete and prefix delete; `/health` reports storage status.

- [ ] **T2.2 — Add ImageSharp and `MediaProcessor`** (§5.3, §9.1)
  - Add `SixLabors.ImageSharp` (note the Six Labors Split License in `README.md`).
  - `MediaProcessor`: decode, auto-orient, strip EXIF/IPTC/XMP, optional downscale above N px, SHA-256 hash, apply edit operations (rotate/flip → crop → resize), encode.
  - **Done when:** unit tests with fixture images prove orientation is baked in, GPS metadata is gone, and edit operations produce the expected dimensions.

- [ ] **T2.3 — Upload pipeline and endpoint** (§9.1, M1, M5, Q6)
  - `POST /api/admin/media` (multipart, multiple files, per-file size limit from settings).
  - Content type from decoding, not extension; allow JPEG/PNG/GIF/WebP; reject SVG and HEIC with a clear message.
  - Generate `PublicId` (12 chars, random) and slugified `FileName`; save original; current = original; warn on duplicate hash. Per-file results so one failure doesn't block others.
  - **Done when:** integration tests cover a valid upload, a renamed non-image, an oversized file, and GPS removal.

- [ ] **T2.4 — Public media endpoint** (`/media/{publicId}/{fileName}`; §9.4)
  - Stream the current version from storage with `Content-Type`, `ETag`, `X-Content-Type-Options: nosniff`; `Cache-Control: public, max-age=31536000, immutable` when `?v=` matches the current version, otherwise short max-age + ETag. (`?w=` renditions come in T4.19.)
  - **Done when:** integration tests verify headers and 304 on matching ETag.

- [ ] **T2.5 — Media metadata, listing and delete endpoints** (§7.4, M4)
  - `GET /media?search=&page=&unused=`, `PUT /media/{id}` (alt text, caption), `DELETE /media/{id}?force=`.
  - Delete of a used item without `force` returns the list of posts using it; with `force`, removes the row and all storage keys.
  - **Done when:** integration tests cover both delete paths and the unused filter.

- [ ] **T2.6 — `IMediaService` contract and client/server implementations**
  - **Done when:** media admin pages work prerendered and after hydration.

- [ ] **T2.7 — `MediaLinkRewriter`** (§9.4, §9.5)
  - In the shared pipeline: turn library image links into `<figure><img …><figcaption>` with explicit `width`/`height`, `loading="lazy"`, `decoding="async"`, the `?v=` cache-buster and size classes (`{.img-medium}`). Missing media renders a placeholder in admin preview and is omitted publicly.
  - Media metadata lookup is injected so it works on server and in WASM.
  - **Done when:** snapshot tests cover a normal image, captioned image, size hint and missing image.

- [ ] **T2.8 — Media library page** (`/admin/media`; M1)
  - Grid with search, "Unused" filter, multi-file drag-and-drop upload with per-file progress bars, alt text/caption editing, delete with confirmation and "Used in N posts: …" warning.
  - Reusable `ConfirmDialog` component.
  - **Done when:** upload, edit metadata and both delete paths work from the UI.

- [ ] **T2.9 — Cropper.js interop** (§9.2, M2, Q8)
  - Add `cropperjs` v2 to npm and bundle `cropper.js` interop via esbuild.
  - `ImageCropper` component wrapping `cropper-canvas`/`-image`/`-selection`/`-shade`/`-handle`; exposes crop rect in **natural pixel coordinates**, rotate 90° left/right, flip H/V, aspect presets (Free, Original, 1:1, 4:3, 3:2, 16:9), live pixel dimensions, and a preview thumbnail.
  - **Done when:** the crop rectangle reported to .NET matches natural image coordinates after rotation.

- [ ] **T2.10 — Media editor page and edit endpoint** (`/admin/media/{id}`; §9.2, M2)
  - Rotate, flip, crop, resize (width/height with aspect lock, no upscaling beyond the cropped size).
  - `POST /media/{id}/edit` applies operations server-side to the **original**, stores `EditOperationsJson`, increments `Version`, writes `v{version}/current.{ext}`, and re-renders `ContentHtml` for every post in `PostMedia`.
  - Save and Cancel (Save as copy and Revert come in T4.18).
  - **Done when:** an edited image shows the new version in existing posts with a new `?v=`.

- [ ] **T2.11 — `MediaPicker` in the Markdown editor** (§9.5, A5, M3, M4)
  - Toolbar button and `Ctrl+Shift+I` open a modal: searchable grid, Upload tab, "Recently used" row.
  - After selection: alt text (pre-filled; prompt if empty), size (full/medium/small), alignment (none/center); inserts standard Markdown `![alt](/media/…/file.jpg "caption"){.img-medium}` at the cursor.
  - **Done when:** an image inserted from the picker renders identically in the preview and on the published post.

- [ ] **T2.12 — `PostMedia` usage tracking**
  - Verify T1.2's `PostMedia` rebuild parses library URLs correctly and powers "Used in N posts" and the Unused filter.
  - **Done when:** adding/removing an image from a post updates usage counts.

- [ ] **T2.13 — Media log events** (§18)
  - Structured Serilog events `MediaUploaded`, `MediaEdited`.
  - **Done when:** events appear in logs with media ID and size.

---

## Phase 3 — Comments MVP

**Exit criteria:** readers can comment on posts; nothing appears publicly until you approve it; spam is filtered and commenters can be blocked.

- [ ] **T3.1 — Comment DTOs and `ICommentModerationService`**
  - `CommentDto`, `CommentSubmission`, moderation query/actions contracts in Shared.
  - **Done when:** contracts compile; validators (name, email, URL, body length) have unit tests.

- [ ] **T3.2 — Comment rendering and sanitization** (§8.2)
  - Render with the restricted comment pipeline, sanitize with `HtmlSanitizer`, force `rel="nofollow ugc noopener"` on all links (including `AuthorUrl`).
  - **Done when:** unit tests prove script, images, headings and raw HTML are removed.

- [ ] **T3.3 — Public comment form (static SSR)** (§5.1, §8.1, C1, Q1)
  - `CommentForm` with `EditForm` + `FormName` + `[SupplyParameterFromForm]` (enhanced form post): name, email, website (optional), comment.
  - Hidden honeypot field (`website2`) and a Data-Protection-signed render timestamp.
  - After submit: "Your comment is awaiting moderation." (pending comments are not shown back).
  - "Remember me" via a tiny `localStorage` script.
  - Respect `CommentsEnabled`, `AllowComments`, and show "Comments are closed." when closed.
  - **Done when:** a reader can submit a comment with JavaScript disabled.

- [ ] **T3.4 — `SpamGuard`** (§8.3, C4, Q2)
  - Ordered checks: honeypot (discard), time trap <3 s or >24 h (discard), block list match (Spam), keyword blocklist (+50), >2 links (+30 each extra), body <3 chars or URL-only (+40). Score ≥ 50 → Spam, else Pending (auto-approve path exists but setting is off).
  - Store `SpamScore`, `SpamReasons`, `IpHash` (SHA-256 of IP + secret salt), `UserAgent`. Bots get a generic "thanks".
  - **Done when:** unit tests cover every check and the score threshold.

- [ ] **T3.5 — Comment rate limiting** (§8.3)
  - ASP.NET Core rate limiter: fixed window, 3 comments / 5 minutes per IP hash; friendly 429 message.
  - **Done when:** an integration test's 4th comment in the window is rejected.

- [ ] **T3.6 — Approved comments on the post page** (§14.2)
  - `CommentList`: approved comments oldest first; author name (linked to website if given), relative date, colored-initial avatar (Gravatar optional via setting). Emails never displayed.
  - Cache tagged `comments:{postId}`, evicted on moderation.
  - **Done when:** approving a comment makes it appear; pending/spam/rejected never appear.

- [ ] **T3.7 — Moderation API** (§7.4, C2, C3)
  - `GET /comments?status=`, `POST /comments/{id}/approve|reject|spam`, `DELETE /comments/{id}` (hard delete), `POST /comments/bulk`, `POST /comment-blocks`, `DELETE /comment-blocks/{id}`.
  - "Block commenter": adds email + IP-hash blocks and marks all of that commenter's comments as Spam. "Empty spam": deletes spam older than 30 days.
  - **Done when:** integration tests cover each action and blocking.

- [ ] **T3.8 — Moderation queue page** (`/admin/comments`; §8.4)
  - Tabs: Pending (default), Approved, Spam, Rejected. Rows show post title, author name/email, relative time, body preview, spam score and reasons, actions (Approve, Reject, Spam, Delete, Block commenter). Multi-select bulk actions. "Empty spam" button.
  - **Done when:** all actions work from the UI with confirmation for destructive ones.

- [ ] **T3.9 — Keyword/domain blocklist management**
  - UI on the Settings page (or comments page) to add/remove `Keyword` and `Domain` blocks and view email/IP blocks.
  - **Done when:** a blocked keyword pushes a comment to Spam.

- [ ] **T3.10 — Dashboard counts** (O1)
  - Dashboard cards: drafts, published count, pending comments (badge), recent activity (latest posts and comments). Pending count badge in the admin nav.
  - **Done when:** counts are accurate and update after moderation.

- [ ] **T3.11 — Comment log events** (§18)
  - `CommentSubmitted` (with spam score), `CommentModerated`; failed spam checks logged.
  - **Done when:** events appear in logs.

---

## Phase 4 — V1 Polish

**Exit criteria:** feature-complete for public launch; Lighthouse ≥ 95 on a post page; security headers in place.

### Authoring

- [ ] **T4.1 — Scheduled publishing** (§6.3, A9, Q4)
  - Allow a future `publishOn` (date/time picker in the blog's time zone); "Unschedule" returns to Draft.
  - `ScheduledPublishWatcher` `BackgroundService`: every minute, evict `posts` cache (and trigger notifications) when a scheduled post goes live.
  - Admin posts list gains a **Scheduled** tab; dashboard shows scheduled posts.
  - **Done when:** an integration test using a fake `TimeProvider` shows a scheduled post appearing once its time passes.

- [ ] **T4.2 — Paste/drag-drop image upload in the editor** (§9.5, A10)
  - CodeMirror paste/drop hooks upload the file, insert `![Uploading name…]()` at the cursor, and replace it with the real link when done (or remove it with an error toast on failure).
  - **Done when:** pasting a screenshot inserts a working image link.

- [ ] **T4.3 — Revision history** (`/admin/posts/{id}/revisions`; A13)
  - `GET /posts/{id}/revisions`; list with kind and timestamp; side-by-side diff against current; "Restore" loads a revision into the editor.
  - **Done when:** restoring a revision and saving produces the old content.

- [ ] **T4.4 — Private preview links** (`/preview/{token}`; §6.7, A14)
  - `POST /posts/{id}/preview-token` (256-bit URL-safe, 7-day default expiry, revocable). Preview page renders the draft with `X-Robots-Tag: noindex` and a "Preview" banner.
  - "Get preview link" in the editor sidebar.
  - **Done when:** expired/revoked tokens 404 and the response carries `noindex`.

- [ ] **T4.5 — Cover images** (A15)
  - Pick a cover from the media library in the editor sidebar; render on the post page and as the default social image.
  - **Done when:** cover shows on the post page and is tracked in media usage.

- [ ] **T4.6 — Per-post SEO overrides** (A16)
  - Meta title, meta description and social image fields in the sidebar, with character counters.
  - **Done when:** overrides appear in the rendered `<head>`.

### Reading

- [ ] **T4.7 — Full `SeoHead`** (§14.2, P8)
  - Open Graph, Twitter card, JSON-LD `BlogPosting`, canonical rules (§16: lowercase, zero-padded, no trailing slash, no `?page=1`).
  - **Done when:** a post passes a rich-results / OG validator check.

- [ ] **T4.8 — "Last updated" date** (P15)
  - Set `LastUpdatedOn` when content changes after publish; show "Updated …" on the post page; use in sitemap `lastmod`.
  - **Done when:** editing a published post shows the updated date.

- [ ] **T4.9 — Site search** (`/search?q=`; §15, P9)
  - `LIKE` over `Title`, `Summary`, `ContentMarkdown` for visible posts, title matches ranked first, paginated; navbar GET form. Rate limited (§12.4).
  - **Done when:** search returns visible posts only and works without JavaScript.

- [ ] **T4.10 — Standalone pages** (`/{pageSlug}`, `/admin/pages`; §6.7, A17, Q11)
  - Admin list + editor reusing `MarkdownEditor`; nav visibility and order; reserved words rejected as slugs (`posts`, `tags`, `admin`, `api`, `media`, `preview`, `search`, `archive`, `setup`, `account`, `feed.xml`, …).
  - Lowest-priority public route; include pages in the sitemap. Provide a privacy page template (§12.3).
  - **Done when:** an About page is reachable at `/about` and appears in the nav.

- [ ] **T4.11 — Previous/next and related posts** (§14.2, P10)
  - Prev/next by publish date; up to 3 related posts by shared tags.
  - **Done when:** links are correct at the first/last post boundaries.

- [ ] **T4.12 — Archive overview** (`/archive`; P11)
  - Years → months with post counts, linking to archives.
  - **Done when:** counts match the archive pages.

- [ ] **T4.13 — Heading anchors and table of contents** (P13)
  - Anchor links on headings; TOC for posts with ≥ 4 H2 headings.
  - **Done when:** TOC links jump to the right headings.

- [ ] **T4.14 — Light/dark theme** (P14)
  - Bootstrap 5.3 color modes; follow OS by default with a toggle persisted in `localStorage`; no flash of wrong theme; highlight.js theme follows.
  - **Done when:** both themes pass contrast checks.

### Comments

- [ ] **T4.15 — Author replies** (§8.4, C5)
  - `POST /comments/{id}/reply` from the queue or post; auto-approved, `IsAuthorReply` with an "Author" badge; approving via reply also approves the parent.
  - **Done when:** a reply appears under its parent with the badge.

- [ ] **T4.16 — One level of threaded replies** (C6)
  - Public "Reply" link sets `ParentCommentId`; replies to replies attach to the top-level parent; replies indented.
  - **Done when:** nesting never exceeds one level.

- [ ] **T4.17 — Comment closing** (§8.5, C7)
  - Per-post `AllowComments` toggle in the editor; `CommentsCloseOn` set from the "close after N days" setting at publish time.
  - **Done when:** closed posts show existing comments and "Comments are closed."

- [ ] **T4.18 — Non-destructive media editing: Revert and Save as copy** (§9.2, M7)
  - `POST /media/{id}/revert`; "Save as copy" creates a new `MediaItem` from the original + operations.
  - **Done when:** revert restores original dimensions and bumps the version.

- [ ] **T4.19 — Responsive renditions** (§9.4, M6)
  - Generate WebP renditions at 320/640/960/1280/1920 (skipping widths wider than the image) on upload and edit; `?w=` selects the nearest rendition, `?f=webp` the format.
  - `MediaLinkRewriter` emits `<picture>` with `srcset`/`sizes` per §9.4.
  - Background job or admin action to backfill renditions for existing media.
  - **Done when:** a post image serves WebP at the appropriate width in the browser.

- [ ] **T4.20 — Auto-approve returning commenters** (C8, Q2)
  - Honor the setting (default off): an email with a previously approved comment goes straight to Approved (unless spam-scored).
  - **Done when:** tests cover the setting on and off.

- [ ] **T4.21 — Email notifications** (§8.4, C9)
  - Replace `IdentityNoOpEmailSender` with an SMTP (or SendGrid) `IEmailSender` configured via Aspire parameters/user secrets.
  - Email the author on each new pending comment when the setting is on.
  - **Done when:** a pending comment triggers an email in dev (e.g. via a local SMTP catcher like Mailpit/smtp4dev in Aspire).

### Administration

- [ ] **T4.22 — Tag management** (`/admin/tags`; §6.4, O3)
  - List with usage counts; rename (re-normalize, check collisions); merge (move `PostTag`s, add redirect from old tag slug); delete only when unused.
  - Endpoints `PUT /tags/{id}`, `POST /tags/{id}/merge/{targetId}`, `DELETE /tags/{id}`.
  - **Done when:** merging `csharp` into `c-sharp` redirects the old tag URL.

- [ ] **T4.23 — Trash** (§6.8, O6)
  - Trash tab in the posts list (`IgnoreQueryFilters()`); `POST /posts/{id}/restore` (restores to Draft); "Empty trash" hard-deletes with cascades.
  - **Done when:** restore and empty trash work and cascades remove related rows.

- [ ] **T4.24 — Export** (`GET /api/admin/export`; §17, O7)
  - Stream `blog-export-{date}.zip`: `posts/*.md` and `pages/*.md` with Hugo/Jekyll-style YAML front matter, `media/{publicId}/original.*` + `metadata.json`, `comments.json`, `settings.json`. Button on the settings page.
  - **Done when:** the zip opens and a post's front matter contains title, slug, date, tags, summary, status and cover.

- [ ] **T4.25 — Settings: remaining groups** (§13)
  - Author avatar, favicon, default social image, `robots.txt` extras, notification toggle, media settings (max upload, downscale threshold, rendition widths), avatars on/off.
  - **Done when:** every §13 setting is editable.

- [ ] **T4.26 — Dashboard completion** (O1, §12.1)
  - Scheduled posts, recent activity, and a nudge when the admin has neither a passkey nor 2FA.
  - **Done when:** the dashboard shows all cards from O1.

- [ ] **T4.27 — Post publish log events** (§18)
  - `PostPublished`, `PostUnpublished` structured events.
  - **Done when:** events appear in logs.

### Launch hardening

- [ ] **T4.28 — Security headers and CSP** (§12.2)
  - CSP: self only, plus YouTube/Vimeo frames; `Referrer-Policy: strict-origin-when-cross-origin`, `X-Content-Type-Options`, `Permissions-Policy`. Verify WASM admin still works under the CSP.
  - **Done when:** securityheaders.com (or equivalent) grade A, with no console CSP violations on public and admin pages.

- [ ] **T4.29 — Rate limiting for login and search** (§12.4)
  - **Done when:** integration tests show limits enforced.

- [ ] **T4.30 — Response compression and HTTP/2** (§11)
  - Brotli/gzip for HTML, CSS, JS, feeds.
  - **Done when:** responses are compressed in production config.

- [ ] **T4.31 — Output caching for feeds, sitemap, robots** (§11)
  - Confirm output caching (tag-evicted) for feeds/sitemap/robots; post pages explicitly **not** output-cached (antiforgery tokens).
  - **Done when:** a second feed request is served from cache and publishing evicts it.

- [ ] **T4.32 — Playwright end-to-end suite** (§19)
  - Scenarios: write → preview → tag → publish → view; upload → crop → insert → publish; comment → moderate → visible; a draft is never publicly reachable.
  - **Done when:** the suite passes locally against the Aspire app.

- [ ] **T4.33 — Accessibility checks** (§19)
  - axe checks on home, `/posts`, post page, tag page, search.
  - **Done when:** no serious/critical axe violations.

- [ ] **T4.34 — Lighthouse CI** (§11, §19)
  - Lighthouse against a seeded post page in CI (`.github/workflows`).
  - **Done when:** performance, accessibility, best practices and SEO are all ≥ 95; TTFB < 200 ms from cache.

- [ ] **T4.35 — Launch checklist**
  - Turn off "discourage search engines", verify `robots.txt`/sitemap, set real site settings, confirm backups of the database and media folder, confirm passkey/2FA on the admin account.
  - **Done when:** the site is live.

---

## Phase 5 — Later

Pick up as desired after launch.

- [ ] **T5.1 — Focus / full-screen writing mode** (A18)
- [ ] **T5.2 — Duplicate upload detection with "reuse existing"** (M9)
- [ ] **T5.3 — Non-image attachments (PDF, zip)** (M10)
- [ ] **T5.4 — External spam/CAPTCHA service** (Turnstile, hCaptcha or Akismet; C10)
- [ ] **T5.5 — Import from Markdown / Hugo / Jekyll zip** (§17, O8)
- [ ] **T5.6 — Manual redirect management UI** (`/admin/redirects`; O9)
- [ ] **T5.7 — Privacy-friendly view counter** (`PostDailyViews`, excludes bots and admin, dashboard aggregates; §18, O10)
- [ ] **T5.8 — Azure Blob media storage** (`AzureBlobMediaStorage`, Azurite in dev via Aspire; §9.3, Q5)
- [ ] **T5.9 — Full-text search** (SQL Server `CONTAINSTABLE` or Lucene.NET; §15)
- [ ] **T5.10 — Freeform rotation / straightening in the image editor** (Q8)

### Not planned

- Email newsletter / subscribe by email (O11) — out of scope per Q9; RSS covers subscriptions.
- HEIC upload support — revisit only if iOS uploads become a problem (Q6; would require Magick.NET).
