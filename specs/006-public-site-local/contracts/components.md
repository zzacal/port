# Contract: Component changes and new page components (006)

**Rule for every existing component change**: the new parameters are optional, and with them unset
the output is byte-identical. `PublicOutputUnchangedTests` (005 baselines) and every existing test
must pass **unmodified**. A failure there means the change is wrong, not the test (SC-010).

## Changes to existing components

| Component | New optional parameter | Effect when set |
|---|---|---|
| `Picture` | `string? TransitionId` | Adds `id="p-{TransitionId}"` to the root `.pic` div. Nothing else changes. |
| `Tile` | `string? TransitionId` | Passed to `Picture`. |
| `Tile` | `string? PreloadSrcSet`, `string? PreloadSizes` | Adds `data-preload-srcset`/`data-preload-sizes` to the root `<a>`. |
| `Tile` | `Image` may be null when `EmptyTitle` is set: `string? EmptyTitle` | Renders `<a class="tile tile-empty" href>` with an empty `<div class="cell">` (page background) and `<Title Text=EmptyTitle>` (E-16). `Image` loses `[EditorRequired]`; a null `Image` without `EmptyTitle` throws `ArgumentException`. |
| `ContactSheetItem` | record gains `string? Title = null`, `string? TransitionId = null`, `string? PreloadSrcSet = null`, `string? PreloadSizes = null`; `Image` becomes `ImageView?` | ContactSheet passes them to Tile. Existing two-argument constructions compile unchanged. |
| `SoloViewer` | `string? Caption` | Renders `<p class="solo-caption">{Caption}</p>` after `<Title>`, before `<MetadataBar>`. |
| `SoloViewer` | `string? TransitionId`, `string? PrefetchUrls` | `TransitionId` goes to Picture. `PrefetchUrls` becomes `data-prefetch` on `<article class="solo">`. |
| `PublicLayout` | `bool Enhance` (default false) | Emits, at the end of `<body>`, the speculation-rules script and the predictive-loader module (contracts/http.md, Markup). |
| `PublicLayout` | `RenderFragment? HeadExtra` | Rendered at the end of `<head>`, after the inlined style. Used for the per-page view-transition `<style>`. |

- `@view-transition { navigation: auto; }` goes in `PublicLayout.razor.css`. That changes only the
  inlined stylesheet, which the baseline test strips (005 note).
- The new `.solo-caption` rule goes in `SoloViewer.razor.css`: `max-width: 60ch; margin: 0 auto;
  color: var(--color-text);`.
- New `/ui/` specimens, appended after existing ones (allowed by the per-specimen baseline rule):
  - Tile "Empty gallery card"
  - SoloViewer "With caption"
  - PublicLayout specimens are unchanged (`Enhance` off).

## New page components (`src/Gallery.Web/Site/Pages/`)

| Component | Parameters | Renders |
|---|---|---|
| `HomePage` | `IReadOnlyList<ContactSheetItem> Cards` | `PublicLayout Title="Gallery" Enhance=true` + `ContactSheet Large=3 Medium=3 Small=1`. With zero cards, only the layout. |
| `GalleryPage` | `string Title, string? Description, bool NoIndex, IReadOnlyList<ContactSheetItem> Items` | `PublicLayout Title=@Title NoIndex Enhance HeadExtra=transitions` + `<header class="gallery-head"><h1>` (Title atom, `Element="h1"`) + optional `<p class="gallery-desc">` + `ContactSheet Large=4 Medium=3 Small=1` (omitted when empty). |
| `PhotoPage` | `ImageView Image, string? Caption, string? PrevHref, string? NextHref, string? PrefetchUrls, bool NoIndex, string GalleryTitle` | `PublicLayout Title="{Image.Title} — {GalleryTitle}"` + `SoloViewer`, with all of it passed through. |
| `NotFoundPage` | — | `PublicLayout Title="Not found" NoIndex` + `<p>Not found.</p>` + `TextLink Href="/"` "Back to galleries". |

- Page CSS: `GalleryPage.razor.css` has `.gallery-head { max-width: 960px; margin: var(--space-m)
  auto var(--space-s); padding: 0 var(--space-s); text-align: center; }` and `.gallery-desc {
  max-width: 60ch; margin: 8px auto 0; }`. It uses token custom properties only.
- These page components are not catalogue components: the `/ui/` index is unchanged. Their
  behaviour is covered by component tests and the live `verify.sh`.

## Predictive loader (inline module, PublicLayout, `Enhance=true`)

- It exits immediately if `navigator.connection?.saveData`.
- On `pointerenter` and `touchstart` (passive) on a `[data-preload-srcset]` element: once per
  element, it appends `<link rel="preload" as="image" imagesrcset imagesizes fetchpriority="high">`
  to `<head>`.
- On `load`: for each URL in `article.solo[data-prefetch]`, it appends `<link rel="prefetch"
  as="image" href>`.
- No `unload`/`beforeunload` listeners. It is under 30 lines, and a test asserts both the absence
  of `unload` and the Save-Data guard string.
