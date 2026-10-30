# Implementation Plan: Component UI Pages

**Branch**: `003-component-ui-pages` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-component-ui-pages/spec.md`

## Summary

This feature builds:
- the D-21 public components (tokens, 4 atoms, 5 molecules, 3 organisms, PublicLayout) as
  server-rendered Razor components in Gallery.Web, with the pure logic (EXIF formatting, srcset,
  sizes, tokens, Server-Timing) in Gallery.Core
- a development-only `/ui/` index plus one page per component, showing every state with committed
  sample photos prepared offline as AVIF and WebP ladders with no metadata
- request timing and `Server-Timing` on every response

Zero NuGet packages are added.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.403)

**Primary Dependencies**: none added. It uses the built-in Razor components
(`AddRazorComponents`, `RazorComponentResult`), CSS isolation, `HtmlRenderer`, and the JSON
console logger (research R-01, R-02, R-08, R-09).

**Storage**: N/A. Sample data is a committed JSON manifest plus static image files (R-04).

**Testing**: xUnit (template packages). Core unit tests; Web component tests through
`HtmlRenderer`; live `verify.sh`; browser measurements.

**Target Platform**: local development on macOS. The `/ui` routes are mapped in Development only.

**Project Type**: web app (server-rendered) plus class library.

**Performance Goals**:
- Pages make no stylesheet request (CSS is inlined).
- Images reserve their box (zero layout shift).
- The smallest sufficient derivative is chosen through srcset/sizes.

**Constraints**:
- no `style` attributes
- no scripts on these pages
- no new packages
- derivatives carry no metadata
- the server never decodes pixels

**Scale/Scope**: 13 components plus tokens, 15 `/ui` routes, 8 sample photos (about 80 derivative
files, about 6 MB).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I.3 Only the owner gets admin | PASS | Nothing admin. `/ui` is mapped in Development only (R-07). It becomes owner-only when sign-in exists. |
| II. Visibility | PASS (justified N/A) | No galleries. `/ui` is noindex and linked from nothing. |
| III.1–III.3 Images leak nothing | PASS | Sample derivatives are encoded with metadata stripped (ffmpeg `-map_metadata -1`; cwebp writes none). `sips` was rejected because it leaves an Exif box (R-04). Quickstart check 11 greps every file. |
| III.8 Server never decodes pixels | PASS | Samples are prepared offline by a script on the developer machine. |
| IV.1 Request event | PASS | `http.request` middleware (R-08). Checked by verify.sh (SC-007). |
| IV.2 Operation timing | PASS (justified N/A) | No performance-sensitive operations: no I/O and no image processing on the server. The catalog and stylesheet are loaded once at startup, and that time is logged as `op.timing` `startup.ui-assets`. |
| IV.3 Server-Timing | PASS | On every response. |
| IV.4 Verifiable from logs | PASS | SC-007 is checked from the captured log. |
| IV.6 Width ladder, AVIF + WebP | PASS | Samples use 320–2560, no upscaling, both formats. Picture emits `<picture>` with both sources plus srcset/sizes. |
| IV.7 Placeholder | PASS | 32 px WebP data URI inlined as a stacked layer (R-03). The dominant colour is stored. |
| IV.8 Dimensions in markup | PASS | `width`/`height` on every image layer. |
| IV.15 Thumbnail first | PASS (component level) | SoloViewer has the thumbnail layer. It's proven at page level when the public pages ship. |
| IV.17 Priority | PASS | Picture `Priority`; ContactSheet's first row is High. |
| IV.11–14, 16, 18–20 | PASS (justified N/A) | These are page-level behaviours for the public pages feature (spec Assumptions). |
| IV.21 Immutable content-addressed assets | PASS | Sample files are named by hash and served `immutable`. |
| IV.22 No framework, inlined CSS, system fonts | PASS | No script, one inlined `<style>`, system font stack. |
| V.1 Scope | PASS | Public set only, view-only (clarifications). |
| V.2 Dependencies | PASS | Zero packages. The dev tools (ffmpeg, cwebp) are not app dependencies, and cwebp was approved by the owner. |
| V.3 No JS | PASS | No scripts are emitted, and a test asserts it. |
| VI. CLI scaffolding | PASS (justified N/A) | No new projects or packages. All files are authored source. |
| VII.2 Design stays current | PASS | D-13, D-20, and D-21 are updated in this feature (see Design updates). |
| VIII. Cost | PASS (justified N/A) | Local only. |

No violations, so Complexity Tracking is empty.

**Post-design re-check**: unchanged, all PASS. Phase 1 produced three design refinements,
recorded below. None of them weakens a principle.

## Design updates (Constitution VII.2, applied in this feature)

1. D-21: the component pages are at `/ui/`, not `/styleguide`. They are Development-only until
   owner sign-in ships, then owner-only. Order between component styles doesn't matter because
   scoped CSS can't collide; only tokens come first. Tokens are single-sourced in C#.
2. D-13 and D-20: the placeholder is a stacked `<img>` data-URI layer fitted exactly like the
   photo (`object-fit: contain`), not a CSS background. The visible behaviour is identical, and it
   needs no per-image CSS (R-03).
3. Decisions log row #27 records these, plus the cwebp development tool.

## Project Structure

### Documentation (this feature)

```text
specs/003-component-ui-pages/
├── plan.md  research.md  data-model.md  quickstart.md
├── contracts/components.md  contracts/http.md
├── checklists/requirements.md
├── evidence/                 # verify.sh, outputs, browser.md (implement)
└── tasks.md                  # /speckit-tasks
```

### Source Code (repository root)

```text
scripts/build-samples.sh                         # offline sample prep (R-04)
src/Gallery.Core/
├── Design/Tokens.cs                             # single source of tokens; CSS :root generator
├── Images/ImageView.cs  Images/Exif.cs  Images/ExifFormat.cs
├── Images/SrcSet.cs  Images/Sizes.cs
└── Observability/ServerTiming.cs
src/Gallery.Web/
├── Program.cs                                   # AddRazorComponents, JSON console, timing, /ui (Dev only), static samples
├── Observability/RequestTimingMiddleware.cs
├── Styles/InlineStyles.cs                       # tokens + scoped bundle, read once at startup
├── Components/_Imports.razor
├── Components/Atoms/{Picture,Title,TextLink,ExifField}.razor (+ .razor.css)
├── Components/Molecules/{Tile,MetadataBar,Pager,Brand,Nav}.razor (+ .razor.css)
├── Components/Organisms/{ContactSheet,SoloViewer,SiteHeader}.razor (+ .razor.css)
├── Components/Templates/PublicLayout.razor (+ .razor.css)
├── Ui/UiCatalog.cs  Ui/SampleCatalog.cs  Ui/UiEndpoints.cs
├── Ui/Pages/UiLayout.razor  Ui/Pages/Index.razor  Ui/Pages/Specimen.razor
├── Ui/Pages/{Tokens,Picture,Title,TextLink,ExifField,Tile,MetadataBar,Pager,Brand,Nav,ContactSheet,SoloViewer,SiteHeader,PublicLayout}Page.razor
└── wwwroot/samples/{hash}.avif|webp, samples.json
tests/Gallery.Core.Tests/{ExifFormatTests,SrcSetTests,SizesTests,ServerTimingTests,TokenMediaAgreementTests}.cs
tests/Gallery.Web.Tests/{Render.cs,IndexTests,AtomPageTests,MoleculePageTests,OrganismPageTests,NoInlineStyleTests,NoScriptTests}.cs
```

The template files `Class1.cs` and both `UnitTest1.cs` are deleted, because real code and tests
replace them.

**Structure Decision**: The D-22 layout. Components are grouped by atomic level under
`Components/`. `/ui` code lives in its own `Ui/` folder, so it can be removed or put behind the
owner policy without touching the components.

## Complexity Tracking

No entries.
