# Implementation Plan: Icon Controls on the Galleries List

**Branch**: `016-galleries-list-icons` | **Date**: 2026-11-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/016-galleries-list-icons/spec.md`

## Summary

The admin galleries list gets feature 015's treatment:

- Each card's title sits on its cover in the dark band, or on the blank cell of an empty gallery.
- Under the card, one row of icons replaces the status words, the drag glyph, the Public switch,
  "View ↗" and "Edit". The order is move, visibility (eye or crossed-out eye), view, edit, with
  edit right-most.
- The row is a new `GalleryActions` molecule, built from 015's `IconButton` and the DragHandle
  icon mode.
- The eye is a submit button on the existing `gal-vis-{id}` form, so publishing keeps the same
  route, CSRF and refusal, and works without JS.

Owner rule from Q2: a photo is never narrower than its icon row. So the library, builder and list
grids show 2 columns at medium widths, and 2 columns at large widths whenever a side panel leaves
the grid too narrow for 4 (a container query). This fixes the sub-44 px builder icons that 015
shipped at those widths.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), Razor components, vanilla ES module `library.js`.

**Primary Dependencies**: none new. 015's atoms; two more Lucide 1.70.0 paths (`eye`, `eye-off`).

**Storage**: none changed.

**Testing**:

- xUnit, using `Render.Html<T>` component tests and `LibraryHost` HTTP tests;
- `LibraryBrowserFixture` Playwright tests;
- `UiPagesUnchangedTests` baselines;
- a Category=Evidence timing test.

**Target Platform**: Azure App Service Linux behind Cloudflare; local dev.

**Project Type**: server-rendered web app (admin area and component catalogue).

**Performance Goals**: SC-005. The gallery list's p95 must stay within pre × 1.10 + 1 ms, with
the same reads (three), measured from logs.

**Constraints**:

- no new package, `<script>` element, `style=` attribute or design token;
- public output byte-identical;
- existing tests unmodified except the stated changes (contracts/test-impact.md);
- no literal `44px` in `.razor.css`.

**Scale/Scope**: a few dozen gallery cards; library up to 100 tiles; builder up to a gallery's size.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I.6 CSRF | PASS | The eye posts the existing `gal-vis-{id}` form with its token; no new write route. |
| II.5–6 visibility | PASS | G4 unchanged: publish only through `PublishAsync` (emptiness first). The empty-private eye is unavailable, and a forged post is still refused (PublishRefusalTests, unchanged guard). Nothing becomes public as a side effect: unpublish sends no `public` field, as today. |
| III leak nothing | PASS | No public markup changes; `LibrarySourceGuardTests` keeps `gal-` classes off public pages. |
| IV.1, IV.4 timing | PASS | No new route or read; a pre-change capture plus an Evidence test, checked by p95.sh (R-08). |
| IV.8 dimensions / no shift | PASS | The title band is absolutely positioned in the frame; the row is a fixed 44 px; the column changes are CSS grid with stored image dimensions. |
| IV.22 no client framework on public pages | PASS | No public change. |
| V.1 traces to spec | PASS | FR-001…015. |
| V.2 minimal deps | PASS | Two path strings from the set 015 vendored. |
| V.3 admin may need JS | PASS | The eye needs no JS; a library.js branch is removed (no dormant code). |
| V.4 proportional effort | PASS | Extras are listed below. |
| VI official CLIs | N/A | No scaffolding. |
| VII.2 design is source of truth | DEVIATION-handled | D-20 (admin grid counts: medium 2, and the panel rule), D-21 (`GalleryActions` molecule) and the D-23 gallery-list line are amended by a scripted draft edit plus assemble/validate in this feature. |
| VIII cost | N/A | |
| IX.1–3 shared containers | PASS | The column rule lives in each grid's own component; chrome is unchanged. The row is the same 44 px on every card (view stays unavailable rather than absent). |
| IX.4 measured test | PASS | LayoutConsistencyBrowserTests stays unmodified. A new browser test measures photo ≥ row width and 44 px targets across 600–1920 px, with and without the panel (SC-007). |

Additions beyond the literal request (V.4):

| Addition | Benefit | Cost | Kept |
|---|---|---|---|
| Title on the cover | "same treatment" as 015 | one parameter, one page-scoped band | yes (spec default) |
| Status words removed | minimum words (015 rule) | 4 test assertions move | yes (spec default) |
| `GalleryActions` catalogue page | owner review in the library | 1 page, 1 baseline | yes (FR-014) |
| 2-column grids on the library and builder | owner Q2; fixes 015's sub-44 px builder icons | CSS + `sizes`, 1 baseline | yes (owner) |

Post-design re-check: unchanged. Everything is PASS or DEVIATION-handled.

## Project Structure

### Documentation (this feature)

```text
specs/016-galleries-list-icons/
├── plan.md  research.md  data-model.md  quickstart.md
├── contracts/
│   ├── components.md     # G1–G6, J1
│   └── test-impact.md    # A1–A10, B1–B5, C
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Web/
├── Components/Atoms/Icons.cs                      # + eye, eye-off (G1)
├── Components/Molecules/
│   ├── GalleryActions.razor(.css), GalleryActions.cs   # new (G2)
├── Components/Organisms/GalleryBuilder.razor(.css)   # 4/2/1 + panel query (G5)
├── Admin/Pages/GalleriesPage.razor(.css)             # cards: TitleOnPhoto, GalleryActions, 3/2/1 (G3, G4)
├── Admin/Pages/LibraryPage.razor(.css)               # 4/2/1 + panel query (G5)
├── Library/LibraryEndpoints.cs                       # GalleryRow.Status removed (G3)
├── Ui/UiCatalog.cs, Ui/Pages/GalleryActionsPage.razor   # G6
└── wwwroot/admin-assets/library.js                   # gal-vis change branch removed (J1)

tests/Gallery.Web.Tests/
├── GalleryActionsTests.cs          # new: rules, markup, labels, form wiring
├── GalleryListBrowserTests.cs      # new: publish/unpublish by click (JS on/off), reorder, band
├── GridWidthBrowserTests.cs        # new: SC-007 across widths, panel open/closed, all three grids
├── GalleryListTimingEvidenceTests.cs  # new, Category=Evidence
└── existing files: test-impact (A) only
```

**Structure Decision**: one web project. The molecule lives in the shared component library; page
wiring stays in `GalleriesPage`, `LibraryPage` and `GalleryBuilder`.

## Complexity Tracking

None.
