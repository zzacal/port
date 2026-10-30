# Tasks: Component UI Pages

**Input**: Design documents from `/specs/003-component-ui-pages/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/components.md, contracts/http.md, quickstart.md

**Tests**: Included. The plan (research R-09) and the constitution's build/test gate require them. Component tests render through the built-in `HtmlRenderer`. Core logic gets unit tests.

## Rules for every task (executor guards)

- Repo root is `/Users/zzacal/repos/gallery`. Set `DOTNET_NOLOGO=1`.
- **No packages.** Never run `dotnet add package`. Never add a `<PackageReference>`. If something seems to need a package, stop and escalate.
- **No `style` attributes** in any `.razor` output. No `<script>` elements. Styling goes only in the component's `.razor.css` or in tokens.
- **No hand-written project files.** Don't edit `.csproj` or `.slnx` by hand.
- Component parameters, outputs, and states are exactly as written in `contracts/components.md`. Formatting rules are in `data-model.md`. If a contract seems wrong, stop and escalate. Don't improvise.
- Gate after every task that touches code: `dotnet build Gallery.slnx -warnaserror` has 0 warnings and 0 errors, and `dotnet test` passes. A test written by an earlier task must pass **unmodified**. If one fails, your change is wrong, not the test.
- Namespaces follow folders: `Gallery.Core.Design`, `Gallery.Core.Images`, `Gallery.Core.Observability`, `Gallery.Web.Components.Atoms`, etc.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

**Purpose**: Remove template placeholders, produce the sample photos, and turn on Razor components.

- [X] T001 Delete the template placeholders `src/Gallery.Core/Class1.cs`, `tests/Gallery.Core.Tests/UnitTest1.cs`, and `tests/Gallery.Web.Tests/UnitTest1.cs`. Then confirm `dotnet build Gallery.slnx -warnaserror` still passes.
- [X] T002 Write `scripts/build-samples.sh` (bash, `set -euo pipefail`, re-runnable) following research R-04. It must:
  - **Inputs**: check that `ffmpeg`, `cwebp`, `sips`, `curl`, `jq`, and `shasum` exist, and fail with a clear message naming any that are missing. Use this table of `id role title`, with picsum ids:
    - `1018 landscape "Mountain Lake"`
    - `1015 landscape "River Valley"`
    - `1016 landscape "Canyon Light"`
    - `1043 landscape "Harbour at Dusk"`
    - `1025 portrait "Pug in a Blanket"`
    - `1027 portrait "Woman in the Field"`
    - `1019 square "Still Water"`
    - `1039 panorama "Long Ridge"`
  - **Steps per photo**:
    1. Download a 3000 px-wide source from `https://picsum.photos/id/{id}/{w}/{h}` into a temp dir, cropped to its role's shape: landscape 3000×2000, portrait 2000×3000, square 2400×2400, panorama 3600×1200.
    2. Read the author and URL from `https://picsum.photos/id/{id}/info`.
    3. For each width W in 320 640 960 1280 1920 2560 with W ≤ the source width:
       - AVIF: `ffmpeg -hide_banner -loglevel error -y -i src.jpg -vf scale=W:-2,format=yuv420p -map_metadata -1 -frames:v 1 -c:v libsvtav1 -crf 32 -preset 6 out.avif`
       - WebP: `cwebp -quiet -q 78 -resize W 0 src.jpg -o out.webp`
       - Name each file `{first 16 hex of shasum -a 256}.{avif|webp}`.
    4. Placeholder: `cwebp -quiet -q 40 -resize 32 0`, encoded as base64 into `data:image/webp;base64,…`.
    5. Dominant colour: `ffmpeg … -vf scale=1:1 -f rawvideo -pix_fmt rgb24 -`, read with `od -An -tu1`, converted to `#rrggbb`.
    6. Final width and height of the largest variant, read with `sips -g pixelWidth -g pixelHeight`.
  - **EXIF fixture**: write it into the manifest per data-model.md "Fixture EXIF sets":
    - landscapes: full EXIF (Make FUJIFILM, Model X-T5, Lens "XF33mmF1.4 R LM WR", FocalLengthMm 33, FocalLength35Mm 50, FNumber 2.0, ExposureSeconds 0.004, Iso 400). Vary the values slightly per photo; for the 4th landscape set ExposureSeconds to 1.5.
    - portrait 1027: partial (Make Canon, Model "Canon EOS R5", Iso 800 only).
    - portrait 1025: full.
    - square: full.
    - panorama: `null`.
  - **Output**:
    - Delete old files in `src/Gallery.Web/wwwroot/samples/` first.
    - Write the derivatives there, plus `samples.json` in exactly the data-model.md shape, built with `jq`. URLs are `/samples/{file}`. `credit.license` is `"Unsplash License"`.
    - Finally, fail if `grep -c -a -E 'Exif|xmpmeta|GPS'` is non-zero for any output file.
- [X] T003 Run `bash scripts/build-samples.sh`. Confirm `src/Gallery.Web/wwwroot/samples/samples.json` lists 8 photos with roles 4 landscape, 2 portrait, 1 square, 1 panorama; that every variant file it references exists; and that the metadata grep printed only zeros.
- [X] T004 In `src/Gallery.Web/Program.cs`, add `builder.Services.AddRazorComponents();`. Create `src/Gallery.Web/Components/_Imports.razor` with these `@using` lines:
  - `Microsoft.AspNetCore.Components`
  - `Microsoft.AspNetCore.Components.Web`
  - `Gallery.Core.Design`
  - `Gallery.Core.Images`
  - `Gallery.Web.Components.Atoms`
  - `Gallery.Web.Components.Molecules`
  - `Gallery.Web.Components.Organisms`
  - `Gallery.Web.Components.Templates`

  Create `src/Gallery.Web/Ui/Pages/_Imports.razor` with the same lines plus `Gallery.Web.Ui`. Keep the existing `/` "Hello World!" endpoint unchanged. Build passes.

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: Core pure logic and its tests, plus the Web host plumbing every page needs.

- [X] T005 [P] Create `src/Gallery.Core/Design/Tokens.cs`: a static class with the constants from the data-model.md Tokens table (`SpaceL=48`, `SpaceM=32`, `SpaceS=16`, `BreakLarge=1200`, `BreakMedium=600`, `CellRatioW=4`, `CellRatioH=3`, `TypeCaption=12`, `TypeBody=14`, `TypeBrand=16`, the four colours, `FontStack`). Add `public static string ToCssRoot()`, which returns `:root{--space-l:48px;--space-m:32px;--space-s:16px;--cell-ratio:4/3;--type-caption:12px;--type-body:14px;--type-brand:16px;--color-bg:#fff;--color-text:#000;--color-muted:#666;--color-rule:#ddd;--font:<FontStack>}`. Also add `public static IReadOnlyList<(string Group, string Name, string Value)> All()` for the tokens page.
- [X] T006 [P] Create `src/Gallery.Core/Images/Exif.cs` (a record with the optional fields from data-model.md "Exif"), `src/Gallery.Core/Images/ImageView.cs` (the records `ImageView` and `Variant` from data-model.md, with derived `AspectRatio` and `ThumbVariant`), and `src/Gallery.Core/Images/ExifFormat.cs`. `ExifFormat` has static methods `Camera(Exif?)`, `Lens(Exif?)`, `FocalLength(Exif?)`, `Aperture(Exif?)`, `Shutter(Exif?)`, and `Iso(Exif?)`, each returning `string?` per the data-model.md formatting table, formatted with invariant culture.
- [X] T007 [P] Create `src/Gallery.Core/Images/SrcSet.cs` with `SrcSet.Avif(ImageView)` and `SrcSet.Webp(ImageView)`, each returning `"{url} {w}w, …"` in ascending width, and `SrcSet.FallbackWebp(ImageView)`, which returns the URL of the largest webp variant with width ≤ 1280 (or the smallest variant if none is ≤ 1280). Create `src/Gallery.Core/Images/Sizes.cs` with `Sizes.ForGrid(int large, int medium, int small)`, returning `"(min-width: 1200px) calc((100vw - {2*48 + (L-1)*48}px) / L), (min-width: 600px) calc((100vw - {2*32 + (M-1)*32}px) / M), calc((100vw - {2*16 + (S-1)*16}px) / S)"`, with the arithmetic done in C# and using the Tokens constants. It throws `ArgumentOutOfRangeException` when any count is outside 1–6.
- [X] T008 [P] Create `src/Gallery.Core/Observability/ServerTiming.cs` with `ServerTiming.Format(string name, double ms)`, which returns `"{name};dur={ms:0.##}"` (invariant culture).
- [X] T009 [P] Create `tests/Gallery.Core.Tests/ExifFormatTests.cs` covering every row of the data-model.md formatting table, including:
  - Camera de-duplication ("Canon" + "Canon EOS R5" gives "Canon EOS R5")
  - focal length with and without the equivalent, and with an equal equivalent (no suffix)
  - f/2.0 and f/11.0
  - shutter 0.004 gives "1/250s", 1.5 gives "1.5s", 2 gives "2s"
  - ISO
  - every method returning null for a null `Exif` and for a missing field
- [X] T010 [P] Create `tests/Gallery.Core.Tests/SrcSetTests.cs` (ordering, format string, fallback choice, including the "no variant ≤ 1280" case) and `tests/Gallery.Core.Tests/SizesTests.cs`. The exact expected strings:
  - 3/3/1: `(min-width: 1200px) calc((100vw - 192px) / 3), (min-width: 600px) calc((100vw - 128px) / 3), calc((100vw - 32px) / 1)`
  - 4/3/1: `(min-width: 1200px) calc((100vw - 240px) / 4), (min-width: 600px) calc((100vw - 128px) / 3), calc((100vw - 32px) / 1)`
  - It throws for 0 and for 7.
- [X] T011 [P] Create `tests/Gallery.Core.Tests/ServerTimingTests.cs` (`Format("app", 3.456)` gives `"app;dur=3.46"`) and `tests/Gallery.Core.Tests/TokensTests.cs` (`ToCssRoot()` contains `--space-l:48px` and `--cell-ratio:4/3`; `All()` has an entry for every constant).
- [X] T012 Create `tests/Gallery.Core.Tests/TokenMediaAgreementTests.cs`. It finds the repo root by walking up from `AppContext.BaseDirectory` until a directory containing `Gallery.slnx` is found (no fixed `..` count). It reads every `src/Gallery.Web/**/*.razor.css`, extracts every number in `@media (min-width: Npx)` / `(max-width: Npx)`, and asserts each one is in {1200, 600, 1199, 599}, the token breakpoints and their −1 complements. It passes trivially while no `.razor.css` exists, and it must keep passing unmodified as components are added.
- [X] T013 Create `src/Gallery.Web/Observability/RequestTimingMiddleware.cs` per research R-08 and contracts/http.md. Register it in `Program.cs` right after `app.UseRouting()`, adding an explicit `app.UseRouting()` so the endpoint is known. Also in `Program.cs`, replace the default console logger with `builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole(o => { o.JsonWriterOptions = new() { Indented = false }; });`. The log call is `logger.LogInformation("http.request {method} {route} {status} {duration_ms}", …)` with a logger category of `Gallery.Request`.
- [X] T014 Create `src/Gallery.Web/Styles/InlineStyles.cs`: a singleton that reads `Gallery.Web.styles.css` once from `IWebHostEnvironment.WebRootFileProvider`, uses an empty string if it is missing, and exposes `string Css => Tokens.ToCssRoot() + bundle`. Register it in `Program.cs` and call `app.UseStaticFiles()` / `app.MapStaticAssets()` so scoped CSS resolves in Development. Log the load with `op.timing` (`op=startup.ui-assets`, `duration_ms`, `outcome`).
- [X] T015 Create `src/Gallery.Web/Ui/SampleCatalog.cs`: a singleton that loads `wwwroot/samples/samples.json` with `System.Text.Json` into `ImageView`s (data-model.md "SampleCatalog") and exposes `Landscape`, `Portrait`, `Square`, `Panorama`, and `All`. Static files under `/samples/` are served with `Cache-Control: public, max-age=31536000, immutable`, set through `StaticFileOptions.OnPrepareResponse` for paths that start with `/samples/`.
- [X] T016 Create `src/Gallery.Web/Ui/UiCatalog.cs` with the 14 `ComponentEntry` rows from data-model.md, in this order and with these slugs:
  - `tokens`
  - `atoms/picture`, `atoms/title`, `atoms/text-link`, `atoms/exif-field`
  - `molecules/tile`, `molecules/metadata-bar`, `molecules/pager`, `molecules/brand`, `molecules/nav`
  - `organisms/contact-sheet`, `organisms/solo-viewer`, `organisms/site-header`
  - `templates/public-layout`

  `PageType` is the Razor page type for that slug.
- [X] T017 Create `src/Gallery.Web/Ui/Pages/UiLayout.razor` and `src/Gallery.Web/Ui/Pages/Specimen.razor` (with a `.razor.css` for each). UiLayout: doctype, `<html lang="en">`, a head with `<meta charset>`, the viewport meta, `<title>@Title · UI</title>`, `<meta name="robots" content="noindex">`, and one `<style>@((MarkupString)Styles.Css)</style>` using the injected InlineStyles. The body holds an `/ui/` back link, `<h1>@Title</h1>`, then `@ChildContent`. Specimen takes `Label` and `ChildContent` and renders `<section class="specimen"><h2>@Label</h2><div class="specimen-body">…</div></section>`.
- [X] T018 Create `src/Gallery.Web/Ui/UiEndpoints.cs` with `MapUi(this WebApplication app)`. It maps `/ui` and `/ui/` to the index, and each `UiCatalog` slug to `RazorComponentResult` of its `PageType`. Every response adds the header `X-Robots-Tag: noindex`. In `Program.cs`, call it only inside `if (app.Environment.IsDevelopment())`.
- [X] T019 Create `tests/Gallery.Web.Tests/Render.cs`, a helper that builds a `ServiceCollection` with logging, `InlineStyles` (constructed with a stub `IWebHostEnvironment` whose `WebRootFileProvider` is a `PhysicalFileProvider` over `src/Gallery.Web/wwwroot`, found by walking up to `Gallery.slnx`), and `SampleCatalog` from the same path. It exposes `Task<string> Html<T>(object? parameters = null)` using `HtmlRenderer`. Also add `tests/Gallery.Web.Tests/Guards.cs` with `AssertNoInlineStyle(html)` (fails on `style=` attributes, but allows the `<style>` element) and `AssertNoScript(html)` (fails on `<script`).

**Checkpoint**: The Core tests pass. The app starts, `/` still says "Hello World!", and one `http.request` JSON line is logged per request with `Server-Timing` on responses.

---

## Phase 3: User Story 1 - Browse every component from one index (P1) 🎯 MVP

**Goal**: `/ui/` lists the tokens and all 13 components grouped by level, each linked.

**Independent Test**: Open `/ui/`. It shows 5 groups and 14 links in UiCatalog order.

- [X] T020 [US1] Create `src/Gallery.Web/Ui/Pages/Index.razor` (Title "Components"), which renders `UiCatalog` grouped by Level as `<section><h2>Level</h2><ul><li><a href="/ui/{slug}">Name</a></li>…` inside UiLayout.
- [X] T021 [US1] Create `tests/Gallery.Web.Tests/IndexTests.cs`. It renders Index and asserts: exactly 14 `href="/ui/` links matching the UiCatalog slugs in order; the 5 headings Tokens, Atoms, Molecules, Organisms, Templates; the robots noindex meta; `AssertNoInlineStyle`; and `AssertNoScript`.

**Checkpoint**: The index renders. Links to pages built in later phases return 404 until those phases land. That is expected.

---

## Phase 4: User Story 2 - Each component on its own, in all its states (P1)

**Goal**: The atoms, the molecules, SiteHeader, PublicLayout, and the tokens page exist, each with its labelled states per FR-007.

**Independent Test**: Open each of these 11 pages and confirm every FR-007 state appears with its label.

Atoms (each a component + `.razor.css`, then its page). Different files, so they can run in parallel:

- [X] T022 [P] [US2] Create `src/Gallery.Web/Components/Atoms/Picture.razor` and `Picture.razor.css` per the contracts/components.md "Picture" section and research R-03:
  - The `.pic` element is a CSS grid with every layer in `grid-area: 1/1`.
  - Fit `Natural`: layers get `width:100%;height:auto`.
  - Fit `Contain`: `.pic` and its layers get `width:100%;height:100%;object-fit:contain`.
  - The placeholder `<img>` has `alt="" aria-hidden="true"` and the same `width`/`height` attributes as the main image.
  - The thumbnail layer appears only when `ShowThumbLayer` is true.
  - The main image is inside `<picture>` with the AVIF source, then the WebP source, then `<img>` using `SrcSet.FallbackWebp`.
- [X] T023 [P] [US2] Create `src/Gallery.Web/Components/Atoms/Title.razor` and `.razor.css` (one line: `white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:var(--type-caption)`, with a `title` attribute holding the full text). Create `TextLink.razor` and `.razor.css` (inherits the colour, underlined on hover).
- [X] T024 [P] [US2] Create `src/Gallery.Web/Components/Atoms/ExifField.razor` and `.razor.css`. It renders nothing when `Value` is null or empty. Otherwise it renders `<span class="exif"><span class="visually-hidden">@Label: </span>@Value</span>`, with the `.visually-hidden` rules in its css.
- [X] T025 [P] [US2] Create the atom pages in `src/Gallery.Web/Ui/Pages/`:
  - `PicturePage.razor`: Specimens for Landscape, Portrait, Square, and Panorama (Natural, Lazy) plus "High priority" (Landscape[0], High). Use `Sizes="(min-width: 600px) 50vw, 100vw"` and put each in a fixed-width specimen body.
  - `TitlePage.razor`: "Short" ("Mountain Lake"), and "Long, truncated" (a 120-character title in a 240 px-wide specimen body).
  - `TextLinkPage.razor`: "Normal".
  - `ExifFieldPage.razor`: one specimen per field from `ExifFormat` applied to Landscape[0].Exif, plus "Shutter ≥ 1 s" (Landscape[3]), plus "No value", which shows a visible caption noting that nothing renders.
- [X] T026 [P] [US2] Create `tests/Gallery.Web.Tests/AtomPageTests.cs`:
  - **Picture**: one `<picture>` per specimen; each has `type="image/avif"` and `type="image/webp"` sources; every `<img>` carries `width` and `height`; the High specimen has `fetchpriority="high"` and `loading="eager"`, and the others `loading="lazy"`; the placeholder img has `aria-hidden="true"` and a `data:image/webp;base64,` src.
  - **Title**: the long title has the `title` attribute.
  - **ExifField**: rendering it directly with `Value=null` produces an empty string. The page contains "1.5s", "1/", "f/", and "ISO".
  - All four pages pass `AssertNoInlineStyle` and `AssertNoScript`.

Molecules (after the atoms they use):

- [X] T027 [P] [US2] Create `src/Gallery.Web/Components/Molecules/Tile.razor` and `.razor.css`. It renders `<a class="tile" href>` containing `<div class="cell">` (`aspect-ratio:var(--cell-ratio)`; the Picture inside uses `Fit=Contain`) and a centred Title below.
- [X] T028 [P] [US2] Create `src/Gallery.Web/Components/Molecules/MetadataBar.razor` and `.razor.css`. It renders the six ExifFields in the contract order, separated by a `·` that appears only between present fields (build the list of non-null formatted values first, then join). It renders nothing when all values are null.
- [X] T029 [P] [US2] Create `src/Gallery.Web/Components/Molecules/Pager.razor` and `.razor.css`: "Previous" and "Next" TextLinks, with a missing direction rendering nothing.
- [X] T030 [P] [US2] Create `src/Gallery.Web/Components/Molecules/Brand.razor` and `Nav.razor`, each with a `.razor.css`. Nav marks the current item with `aria-current="page"`.
- [X] T031 [US2] Create the molecule pages:
  - `TilePage.razor`: "Landscape", "Portrait", and "Long title". Each tile is in a 320 px-wide specimen body, with `Sizes="320px"`.
  - `MetadataBarPage.razor`: "All fields" (Landscape[0]), "Some fields" (Portrait 1027, partial), and "No fields" (Panorama), with the caption "renders nothing".
  - `PagerPage.razor`: "First image" (next only), "Middle" (both), and "Last image" (previous only), using `#` hrefs.
  - `BrandPage.razor` and `NavPage.razor`: "Default".
- [X] T032 [US2] Create `tests/Gallery.Web.Tests/MoleculePageTests.cs`:
  - **Tile**: an `<a class="tile"`, the `cell` wrapper, and the title text.
  - **MetadataBar**: rendering it directly with full Exif contains 5 separators for 6 values; with partial Exif it contains "Canon EOS R5", "ISO 800", and exactly 1 separator; with null it produces an empty string.
  - **Pager**: first has no "Previous", last has no "Next", middle has both.
  - **Nav**: has `aria-current="page"`.
  - All pages pass the guards.

Organism, template, and tokens for this story:

- [X] T033 [US2] Create `src/Gallery.Web/Components/Organisms/SiteHeader.razor` and `.razor.css` (Brand left, Nav right, flex, `padding: var(--space-s)`, and `var(--space-l)` at ≥ 1200 px through a `@media (min-width: 1200px)` rule). Create `src/Gallery.Web/Components/Templates/PublicLayout.razor` and `.razor.css` per the contract: the head is the same as UiLayout's, with the robots meta only when `NoIndex` is set; the body holds SiteHeader ("Gallery", Nav [Galleries → /]) and `<main>@ChildContent</main>`; `body { margin:0; font-family:var(--font); background:var(--color-bg); color:var(--color-text) }`.
- [X] T034 [US2] Create `SiteHeaderPage.razor` ("Default", inside UiLayout), `PublicLayoutPage.razor` (rendered with **PublicLayout** as its root, not UiLayout, with `NoIndex=true` and a single paragraph "Empty page body" in main), and `TokensPage.razor`. TokensPage lists `Tokens.All()` grouped, with value text. Colours get a swatch element with the class `swatch swatch-{name}`, and spacing gets a ruler with the class `ruler ruler-{name}`. Each swatch and ruler class is defined in `TokensPage.razor.css` using the matching `var(--…)`, never an inline style.
- [X] T035 [US2] Create `tests/Gallery.Web.Tests/TemplatePageTests.cs`:
  - **PublicLayout page**: exactly one `<style>`, contains `--space-l:48px`, has the robots noindex meta, no `<script`, and no `<link rel="stylesheet"`.
  - **Tokens page**: contains every name from `Tokens.All()`.
  - **SiteHeader page**: contains "Gallery".
  - All pages pass the guards.

**Checkpoint**: 11 of the 14 index links resolve.

---

## Phase 5: User Story 3 - Grid and viewer at every screen size (P2)

**Goal**: ContactSheet and SoloViewer exist with their pages, and the columns follow the configured counts at each breakpoint.

**Independent Test**: The ContactSheet page at 1440, 900, and 390 px shows 3/3/1 and 4/3/1 columns, and a short row keeps its cell size.

- [X] T036 [US3] Create `src/Gallery.Web/Components/Organisms/ContactSheet.razor` and `.razor.css` per the contract and research R-06:
  - It validates `Large`, `Medium`, and `Small` (1–6) when it renders.
  - It renders `<div class="cs cs-l{L} cs-m{M} cs-s{S}">` with one Tile per item, every Tile using `Sizes.ForGrid(L, M, S)`, and the first `PriorityCount` Tiles at High priority.
  - CSS:
    - base (small): `display:grid; gap:var(--space-s); padding:0 var(--space-s)`, with `.cs-s1`…`.cs-s6 { grid-template-columns: repeat(n, minmax(0,1fr)) }`
    - `@media (min-width: 600px)`: gap and padding `var(--space-m)`, with `.cs-m1`…`.cs-m6`
    - `@media (min-width: 1200px)`: gap and padding `var(--space-l)`, with `.cs-l1`…`.cs-l6`

    Because these are scoped CSS files, each media block re-declares the classes for that level.
- [X] T037 [US3] Create `src/Gallery.Web/Components/Organisms/SoloViewer.razor` and `.razor.css`. A `.stage` box (`height:80vh`; the photo is letterboxed inside it by `Fit=Contain`) contains `Picture Fit=Contain Priority=High ShowThumbLayer=true Sizes="100vw"`. Below it, in order: the Title, the MetadataBar, and the Pager.
- [X] T038 [US3] Create `ContactSheetPage.razor` and `SoloViewerPage.razor`:
  - ContactSheetPage specimens:
    - "Home configuration (3/3/1)": all 8 samples.
    - "Gallery configuration (4/3/1)": all 8 samples.
    - "Fewer than one row (4/3/1, 2 items)".

    Item hrefs are `#`.
  - SoloViewerPage specimens: "Landscape, full metadata" (Landscape[0], prev and next) and "Portrait, partial metadata" (Portrait 1027, prev only).
- [X] T039 [US3] Create `tests/Gallery.Web.Tests/OrganismPageTests.cs`:
  - **ContactSheet**: the page contains `cs-l3 cs-m3 cs-s1` and `cs-l4 cs-m3 cs-s1`.
  - In the 3/3/1 specimen, the first 3 tiles have `fetchpriority="high"` and the 4th has `loading="lazy"`.
  - Every tile's `sizes` equals `Sizes.ForGrid` for its configuration.
  - Rendering ContactSheet directly with `Large=7` throws `ArgumentOutOfRangeException`.
  - **SoloViewer**: it has the thumbnail layer and `fetchpriority="high"`; the landscape specimen shows a full MetadataBar and the portrait specimen shows "Canon EOS R5".
  - All pages pass the guards.

**Checkpoint**: All 14 index links resolve.

---

## Phase 6: Polish & Verification

- [X] T040 Create `tests/Gallery.Web.Tests/AllPagesTests.cs`. For every `UiCatalog` entry, it renders its PageType and asserts non-empty output, `AssertNoInlineStyle`, and `AssertNoScript` (SC-008, FR-004). It covers new pages automatically.
- [X] T041 Write `specs/003-component-ui-pages/evidence/verify.sh` (run from the repo root). It must:
  1. Build with `-warnaserror` and run `dotnet test`.
  2. Start the app in Development on :5080 in the background, with stdout going to `evidence/dev-run.log`. Wait for it to be ready.
  3. Request `/ui/` and every UiCatalog slug (parse the hrefs from `/ui/`), plus `/ui/nope`. Require 200 for each slug, 404 for `nope`, the `X-Robots-Tag: noindex` and `Server-Timing: app;dur=` headers, and no `<script` in any body (SC-001, FR-009, FR-011).
  4. Send 20 requests, then require exactly 20 new `http.request` lines in the log (SC-007).
  5. Stop the app. Start it with `ASPNETCORE_ENVIRONMENT=Production --no-launch-profile --urls http://localhost:5081` and require `/ui/` to return 404 (SC-006). Stop it.
  6. Run the metadata grep over `wwwroot/samples` (FR-008).

  It prints PASS/FAIL lines and a failure count. Run it with output going to `evidence/verify-output.txt`. Grep for the actual `http.request` field name in the log before counting.
- [X] T042 Take browser measurements with the app running in Development, recorded in `specs/003-component-ui-pages/evidence/browser.md`. Scope every DOM probe to its specimen section, never the whole document.
  - At viewport widths 1440, 900, and 390, count the distinct tile `left` offsets in the first row of each ContactSheet specimen. Expect 3/3/1, 4/3/1, and in the 2-item specimen the cells must be the same width as the 4-column specimen at 1440 (SC-003).
  - For every photo, the rendered `<img>` natural aspect ratio must equal its `width/height` attributes within 1%, and the img box must fit inside its cell (SC-004).
  - With CPU and network throttling and a `PerformanceObserver` for `layout-shift`, the cumulative shift must be 0 on the ContactSheet and Picture pages (SC-005).

  Save one screenshot per page at 1440 px under `evidence/shots/`.
- [X] T043 Re-check the design is current: `bash specs/001-architecture-stack-design/evidence/assemble.sh && bash specs/001-architecture-stack-design/evidence/validate.sh` reports 0 failures, and `grep -c '/ui/' docs/design.md` is at least 1.
- [X] T044 Commit, in order:
  1. `scripts/` plus `wwwroot/samples`, as "chore(003): sample photos".
  2. The code and tests.
  3. Docs and evidence. Use `git add -f` for any `*.log` in evidence.

  `git status --short` must show no `bin/`/`obj/`. Push the branch `003-component-ui-pages`.

---

## Dependencies & Execution Order

- **Setup** (T001–T004) comes first. T003 needs T002.
- **Foundational** (T005–T019) blocks every story. T005–T011 are Core and can run in parallel (separate files). T012 can run in parallel with them. T013–T018 edit `Program.cs` or depend on each other, so they run in order. T019 needs T014 and T015.
- **US1** (T020–T021) needs only Foundational.
- **US2** (T022–T035):
  - The atom tasks T022–T025 can run in parallel; T026 follows them.
  - The molecules T027–T030 come after T022–T024 and can run in parallel; T031 and T032 follow.
  - T033–T035 come after the molecules.
- **US3** (T036–T039) needs T027 (Tile), T028, T029, and T022.
- **Polish** (T040–T044) comes after all stories.

## Parallel Example: User Story 2 atoms

```text
T022 Picture.razor        T023 Title + TextLink
T024 ExifField            T025 atom pages (after T022–T024 compile)
```

## Implementation Strategy

- **MVP**: Setup, Foundational, and US1. The index renders and timing works.
- Then US2: 11 component pages.
- Then US3: the grid and viewer. These carry most of the D-20 visual design.
- Finish with Polish: live verification, browser measurements, commit.

**Tiering**:
- Phase 1, T013–T019, and the browser measurements (T042) run at the high tier. Those are sample prep, host plumbing, and visual judgement.
- T005–T011 can go to one cheaper executor.
- The atom, molecule, and page tasks can go to cheaper executors with disjoint file ownership. The parent re-runs build and tests after each batch.
