# Implementation Plan: Icon Controls on Library Photos

**Branch**: `015-tile-icon-controls` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/015-tile-icon-controls/spec.md`

## Summary

Library and gallery-builder photo tiles get their title on the photo's bottom edge, in a dark
band inside the photo's own letterboxed rectangle. Under the photo, one row of icon buttons
replaces the words.

- **Library:** gallery (three membership icons), view, edit.
- **Builder:** move, cover, remove, view.

The edit and gallery icons are links that toggle the existing `?open=` address. That gives one
active photo at a time, with no JS needed.

The icon system is 8 Lucide icons (ISC), copied as path strings into one C# table and rendered
as inline SVG in the current text colour. It adds no package and no request. It is exposed as
`Icon` and `IconButton` atoms plus a `PhotoActions` molecule, each with a `/ui` page for owner
review. No server operation, route or stored data changes.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`); vanilla ES module `library.js`.

**Primary Dependencies**: none new. Razor components; Lucide icon paths are vendored as source
strings (not a package).

**Storage**: none changed.

**Testing**: xUnit; `Render.Html<T>` component tests; `LibraryHost` HTTP tests;
`LibraryBrowserFixture` Playwright tests; `UiPagesUnchangedTests` baselines; a Category=Evidence
timing test.

**Target Platform**: Azure App Service Linux behind Cloudflare; local dev.

**Project Type**: server-rendered web app (admin area and component catalogue).

**Performance Goals**: SC-005: library and builder p95 within 10% (+1 ms) of the pre-change
capture, with the same `db_reads`, from logs. Zero icon requests.

**Constraints**:

- no new script element, `style=` attribute, design token or package;
- public output byte-identical;
- existing tests unmodified except test-impact (A).

**Scale/Scope**: up to 100 tiles per library page (PageSize).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I.6 CSRF | PASS | No new write; the builder buttons post the same forms. |
| II (public visibility) | PASS | Public tiles byte-identical (K3 defaults); `PublicOutputUnchangedTests`. |
| III (leak nothing) | N/A | No public markup changes. |
| IV.1, IV.4 timing | PASS | No new route or op; existing `http.request` and `db.*` timers. Evidence: a pre-change capture (Setup) vs an Evidence test, checked by `p95.sh` (R-09). |
| IV.8 dimensions / no shift | PASS | The title moves into the photo frame (absolute positioning); the tile height is fixed by the cell plus a 44 px row. Measured in a browser test. |
| IV.22 no client framework on public pages | PASS | No public change; icons are static markup. |
| V.1 traces to spec | PASS | |
| V.2 minimal deps | PASS | No package: 8 path strings with the ISC notice (R-01). |
| V.3 admin may need JS | PASS | The toggle works without JS; J1 only scrolls. |
| V.4 proportional effort | PASS | Sprite rejected (R-02: would move 37 baselines for 0.25 KB). Extras are listed below. |
| VI official CLIs | N/A | No scaffolding. |
| VII.2 design is source of truth | DEVIATION-handled | D-21 (atoms Icon and IconButton, molecule PhotoActions replaces MembershipStatus, icon-set rule) and D-23 (library and builder tiles: title on the photo, icon row). A scripted draft edit plus assemble/validate is a task in this feature (decisions 17). |
| VIII cost | N/A | |
| IX.1–3 shared containers | PASS | The sticky aside is a rule in `AdminLayout`, the one admin container (IX.2), applied to every page with an aside; chrome geometry is unchanged. The row is a fixed 44 px whatever its parts (IX.3: the view icon stays as unavailable). |
| IX.4 measured test | PASS | `AdminLayoutConsistencyBrowserTests` stays unmodified; a new browser test measures the row height, the title band inside the photo, and the select-mark/band separation. |

Additions beyond the literal request (V.4):

| Addition | Benefit | Cost | Kept |
|---|---|---|---|
| Builder tiles (US4) | One look across both photo grids | K6, 9 stated test changes | yes (owner, clarify Q2) |
| Unavailable view icon | Fixed edit position (IX.3) | one span | yes |
| Remove `MembershipStatus` | No dead component | 1 baseline deleted, tests replaced | yes |
| Sticky aside + J1 scroll | Keeps "Galleries field in view" true at every width | 1 CSS rule, ≤ 8 JS lines | yes |
| Icon sprite | 0.25 KB gzip per 100 tiles | 37 baselines move | dropped (R-02) |

Post-design re-check: unchanged; all PASS or DEVIATION-handled.

## Project Structure

### Documentation (this feature)

```text
specs/015-tile-icon-controls/
├── plan.md  research.md  data-model.md  quickstart.md  decisions.md
├── contracts/
│   ├── components.md     # K1–K8, J1
│   └── test-impact.md    # A1–A14, B1–B7, C
├── evidence/             # pre-015 baseline copy, pre-timings.log, p95.sh, baseline-diff.py, gate.sh, non-vacuity.sh
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Web/
├── Components/Atoms/
│   ├── Icons.cs                 # new: Lucide 1.70.0 path table + ISC notice (K1)
│   ├── Icon.razor(.css)         # new (K1)
│   └── IconButton.razor(.css)   # new (K2)
├── Components/Molecules/
│   ├── PhotoActions.razor(.css), PhotoActions.cs   # new (K4)
│   ├── Tile.razor(.css)         # + TitleOnPhoto, Id (K3)
│   ├── DragHandle.razor(.css)   # + Icon mode (K6)
│   ├── CoverMarker.razor(.css)  # + Icon mode (K6)
│   └── MembershipStatus.razor   # deleted
├── Components/Organisms/GalleryBuilder.razor(.css)   # TitleOnPhoto, icon row (K6)
├── Components/Templates/AdminLayout.razor.css        # sticky aside ≥1200 (K7)
├── Admin/Pages/LibraryPage.razor(.css)               # PhotoActions row (K5)
├── Ui/UiCatalog.cs, Ui/Pages/{Icon,IconButton,PhotoActions}Page.razor  # K8; MembershipStatusPage deleted
└── wwwroot/admin-assets/library.js                   # J1

tests/Gallery.Web.Tests/
├── IconTests.cs                 # new: K1/K2 markup, registry ⇄ usage guard, catalogue pages
├── PhotoActionsTests.cs         # new: rules, markup, order, labels
├── TileIconBrowserTests.cs      # new: geometry, contrast, toggle (JS on/off), builder flows, phone width
├── TileTimingEvidenceTests.cs   # new, Category=Evidence
└── existing files: test-impact (A) only
```

**Structure Decision**: one web project. The atoms and the molecule live in the shared component
library (FR-019/020). Page wiring stays in `LibraryPage` and `GalleryBuilder`.

## Complexity Tracking

None.
