# Research: Component UI Pages (003)

Each decision below was checked on this machine on 2026-10-27, either with a throwaway probe
project or by running the tool. No NEEDS CLARIFICATION items remain.

## R-01: Rendering components from minimal APIs

- **Decision**: Use `builder.Services.AddRazorComponents()` and endpoints that return
  `RazorComponentResult<TPage>`. Components are `.razor` files in `src/Gallery.Web/Components/`.
  No `blazor.web.js` and no interactivity.
- **Verified**: a probe `dotnet new web` app compiled a `.razor` component with no added package
  and served it as plain HTML. The response also carries a harmless `blazor-enhanced-nav: allow`
  header, and no script is emitted.
- **Rationale**: D-21 chose this. It's built into .NET 10 (V.2) and renders HTML with no JS (V.3).
- **Alternatives**: Razor Pages and RazorSlices were already rejected in D-21.

## R-02: Stylesheet, tokens, and inlining

- **Decision**:
  - Each component keeps its CSS in a `.razor.css` file beside it. The SDK's built-in CSS
    isolation bundles them into `Gallery.Web.styles.css`, with selectors scoped per component.
  - Design tokens live in C# (`Gallery.Core.Design.Tokens`) as the single source. At startup the
    app turns them into a `:root { --… }` block.
  - At startup the app reads the bundled stylesheet once through `IWebHostEnvironment.WebRootFileProvider`.
    Layouts inline `tokens + bundle` in one `<style>` element. No stylesheet request is made
    (IV.22).
  - Media queries can't use CSS variables, so the breakpoints (1200 / 600 px) are written
    literally in the scoped CSS. A unit test reads every `.razor.css` file and fails if any
    `@media` value differs from `Tokens`, which stops drift.
- **Verified**: the probe built `obj/.../scopedcss/bundle/<App>.styles.css` and served it from
  the web root at runtime.
- **Change from D-21**: D-21 said "concatenate tokens, atoms, molecules, organisms in order".
  Scoped selectors can't collide, so order between components doesn't matter. Only tokens must
  come first. D-21 is updated.
- **Alternatives**: A hand-written single site.css was rejected because it loses
  per-component files. A Node build step was rejected because it adds a toolchain (V.2).

## R-03: Placeholder and thumbnail layers without inline style

- **Decision**: Picture renders a stack with three layers in one CSS grid area:
  1. The placeholder: an `<img>` whose `src` is a tiny data URI, with `alt=""` and
     `aria-hidden="true"`.
  2. In SoloViewer only, the thumbnail: the 640 px derivative, already cached from the grid.
  3. The real `<picture>`.

  Every layer uses the same `width`/`height` attributes and the same fit rule, so all layers
  occupy exactly the same box:
  - **contain** (Tile and SoloViewer): `width:100%; height:100%; object-fit:contain`. The photo
    is letterboxed inside the parent's fixed box, and the placeholder is letterboxed identically,
    so it covers only the photo area, as D-20 requires.
  - **natural** (standalone Picture): `width:100%; height:auto`. The aspect ratio comes from the
    `width`/`height` attributes, so the space is reserved before the image loads (IV.8).
- **Rationale**: FR-003 bans inline `style` attributes, and per-image values (the placeholder
  URI, the thumbnail URL) can't live in a shared stylesheet. As stacked `<img>` elements they
  need no per-image CSS at all. No blur filter is used, because a 32 px image upscaled by the
  browser already looks blurred, and a filter would bleed into the letterbox.
- **Change from D-13 and D-20**: those entries said the placeholder is "inlined as a CSS
  background". It is now a stacked image layer. The behaviour is the same, and both entries are
  updated.
- **Alternatives**:
  - A per-page `<style>` block of per-image classes in `<head>`: it works, but every page has to
    collect its images first.
  - `<style>` elements inside the body: non-conforming HTML.
  - CSS `attr()` with a URL: browsers don't support it.

## R-04: Sample photos and their derivatives

- **Decision**: An authored script, `scripts/build-samples.sh`, does this:
  1. Downloads 8 openly licensed photos from picsum.photos (Unsplash License). Credits are
     recorded in the manifest.
  2. Crops each to its target shape: 4 landscape (3:2), 2 portrait (2:3), 1 square, 1 panorama
     (3:1).
  3. Encodes the width ladder 320 / 640 / 960 / 1280 / 1920 / 2560, never upscaling, as:
     - **AVIF**: `ffmpeg -map_metadata -1 … -c:v libsvtav1`
     - **WebP**: `cwebp`, which writes no metadata by default
  4. Makes a 32 px WebP placeholder (`cwebp -resize 32 0 -q 40`, about 170 bytes) and inlines it
     as a data URI.
  5. Reads the dominant colour from a 1×1 ffmpeg scale.
  6. Reads each output's final dimensions with `sips`.
  7. Names every file by content hash (`{sha256-16}.{avif|webp}`), the D-07 naming.
  8. Writes `src/Gallery.Web/wwwroot/samples/samples.json`.

  Camera metadata for each sample (full, partial, or empty) is fixture data in that JSON, because
  picsum strips EXIF.
- **Verified**:
  - `sips` writes AVIF but leaves an Exif box, so it is rejected for encoding (III.3). ffmpeg
    AVIF and cwebp WebP both contain no Exif/XMP.
  - ffmpeg needs `format=yuv420p` and `-frames:v 1`. `-still-picture` is not available in this
    build.
  - The AVIF's stored size can differ from the encoder's padded frame (1280×854 stored vs
    1280×856 encoded), which is why dimensions are read back with `sips` after encoding.
  - A 3000×2000 source gives about 100 KB per format at 1280 px.
- **Tools**: ffmpeg (already installed), `cwebp` (`brew install webp`, approved by the owner, dev
  machine only), and `sips` and `curl` (built into macOS). None of these is a dependency of the
  app. The generated files are committed, so building or running the app never needs the tools.
- **Size**: about 8 photos × up to 6 widths × 2 formats, roughly 80 files and about 6 MB
  committed. The script is idempotent and re-runnable.
- **Constitution III.8**: the server never decodes pixels. The samples are prepared offline on
  the developer machine.

## R-05: Shared view model for images

- **Decision**: `Gallery.Core.Images.ImageView` holds id, title, width, height, variants
  (width → avif URL, webp URL), placeholder data URI, dominant colour, and an optional
  `Exif` record. `Gallery.Core` also holds the pure logic:
  - `SrcSet.For(variants, format)`
  - `Sizes.ForGrid(columns, tokens)`
  - `ExifFormat` (shutter speed, aperture, focal length with 35 mm equivalent, ISO, camera
    make/model de-duplication)

  The sample catalog maps the JSON onto `ImageView`. Real images will map onto the same type
  later.
- **Rationale**: This follows D-22, where rule-heavy pure logic lives in Core and gets fast unit
  tests.

## R-06: Grid column counts without inline style

- **Decision**: ContactSheet takes `Large`, `Medium`, and `Small` column counts (each 1–6, checked
  when the component renders). It emits modifier classes (`cs-l3 cs-m3 cs-s1`). The scoped CSS
  defines `grid-template-columns: repeat(n, 1fr)` for n = 1–6 inside each breakpoint's media
  query. The `sizes` attribute for every Tile comes from `Sizes.ForGrid` using the same counts
  and the same token gaps. A unit test checks the generated `sizes` against hand-computed values
  for 3/3/1 and 4/3/1.
- **Edge case "fewer photos than one row"**: columns are `1fr` tracks of a fixed count, so a short
  row keeps its cells the same size rather than stretching them.

## R-07: Access gate and noindex

- **Decision**: The `/ui` route group is mapped only when `app.Environment.IsDevelopment()`. In
  any other environment the routes don't exist, so they return 404 (FR-010, SC-006). UI pages
  send the response header `X-Robots-Tag: noindex` and `<meta name="robots" content="noindex">`
  (FR-009). Nothing public links to `/ui`.
- **Later**: when owner sign-in ships, the condition becomes the owner authorization policy.

## R-08: Request timing (FR-011)

- **Decision**:
  - `RequestTimingMiddleware` in Gallery.Web starts a `Stopwatch` and writes
    `Server-Timing: app;dur=<ms>` in `Response.OnStarting`. After the request it logs one
    `http.request` event with `method`, `route` (the endpoint route pattern, or the raw path
    when nothing matched), `status`, and `duration_ms`.
  - The header value comes from the pure `Gallery.Core.Observability.ServerTiming.Format`.
  - Logging uses the built-in JSON console formatter (`AddJsonConsole`, single line).
- **Rationale**: Constitution IV.1 and IV.3 apply to the first real endpoint, which this feature
  delivers. It is all built in, so no package is added.

## R-09: Testing without added packages

- **Decision**:
  - **Gallery.Core.Tests**: unit tests for `ExifFormat`, `SrcSet`, `Sizes`, `ServerTiming`, and
    the token/`@media` agreement.
  - **Gallery.Web.Tests**: component tests using the built-in `HtmlRenderer`
    (`Microsoft.AspNetCore.Components.Web`). These need no package because the project already
    references Gallery.Web, which brings the ASP.NET Core shared framework, and a probe test
    passed this way. They assert every FR-007 state, the 13-entry index, no `style=` attributes
    (SC-008), and empty EXIF fields rendering nothing.
  - **Live checks** (`evidence/verify.sh` against a running server): the dev-only gate, noindex,
    `Server-Timing`, and one log event per request (SC-006, SC-007).
  - **Browser checks**: column counts at three widths, no cropping, no layout shift (SC-003 to
    SC-005). These are measured in a real browser, and the readings are saved to `evidence/`.
- **Rationale**: The owner wants no unrequested dependencies. In-process HTTP tests would need
  `Microsoft.AspNetCore.Mvc.Testing`, and the live checks cover those behaviours anyway.
- **Alternatives**: Mvc.Testing or bUnit were rejected because each is an added package.
