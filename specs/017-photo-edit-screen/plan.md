# Implementation Plan: Focused Photo Edit Screen

**Branch**: `017-photo-edit-screen` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/017-photo-edit-screen/spec.md`

## Summary

A library photo's edit and gallery icons now open the photo's own screen,
`/admin/library/images/{id}?page=N` (new route L6, the same address its Save form already posts
to). It renders through `AdminLayout` with the breadcrumb `Library › {title}`:

- main: the one photo, whole and large (contain-fit, capped at 80vh like the public solo view,
  `sizes` set to the real column width so a large derivative loads);
- aside: the existing Photo panel moved verbatim from the library (same ids, fields, combobox,
  delete confirm).

Save lands back on the edit screen with "Saved"; refusals re-render it; delete returns to library
page N. The library loses its side panel, its panel-only CSS and the panel-scroll script, and an
old `?open=` address shows the plain library. No service, stored data or purge set changes.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), Razor components, vanilla ES module `library.js`.

**Primary Dependencies**: none new.

**Storage**: none changed (data-model.md).

**Testing**: xUnit (`LibraryHost` HTTP tests, `Render.Html<T>`, `Gallery.Core.Tests/SizesTests`),
`LibraryBrowserFixture` Playwright tests, a Category=Evidence timing test.

**Target Platform**: Azure App Service Linux behind Cloudflare; local dev.

**Project Type**: server-rendered web app (admin area).

**Performance Goals**: SC-005: edit-screen p95 ≤ library-with-panel p95 × 1.10 + 1 ms, from logs;
3 reads instead of 4.

**Constraints**:

- no new package, design token, `style=` attribute or extra `<script>` (one module, library.js);
- public output byte-identical; `/ui/` catalogue and baselines untouched;
- existing tests unmodified except the stated changes (contracts/test-impact.md).

**Scale/Scope**: one photo per screen; library pages of up to 100 tiles.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I owner-only, I.6 CSRF | PASS | L6 is on the protected `/admin` group; the form carries the token and posts the existing L2/L5 routes. LibraryAccessTests gains L6 (signed-out refusal). |
| II visibility | PASS | No visibility rule changes; publication still happens only through the existing combobox save path (014 warning kept). |
| III leak nothing | PASS | No public markup changes. The photo renders from derivatives only (`Picture`); no original URL (III.1). Admin pages stay `noindex`. |
| IV.1, IV.4 timing | PASS | `http.request` and `op.timing` on L6 (3 reads); pre-change capture + p95.sh (R-09). |
| IV.8 no layout shift | PASS | Stored width/height on the `Picture`; the cap is CSS. |
| IV.15/IV.17 perceived speed | PASS | Thumb layer paints the cached grid derivative; the photo is `fetchpriority=high`. |
| V.1 traces to spec | PASS | FR-001…014. |
| V.2 minimal deps | PASS | None. |
| V.3 admin JS | PASS | The screen works without JS (plain forms; combobox no-JS mode). |
| V.4 proportional effort | PASS | Extras below; dead code removed rather than left dormant. |
| VI official CLIs | N/A | No scaffolding. |
| VII.2 design is source of truth | DEVIATION-handled | D-23 Library line and D-20's library container-query clause amended by a scripted draft edit + assemble/validate in this feature (R-10). |
| VIII cost | N/A | No new metered resource; a larger derivative per edit view is a handful of owner requests. |
| IX.1–3 shared containers | PASS | The page uses `AdminLayout`'s existing aside slot; no page-level frame CSS; the status line and stage sit inside `main`. |
| IX.4 measured test | PASS | `AdminLayoutConsistencyBrowserTests` gains the photo page (test-impact B2). |

Additions beyond the literal request (V.4):

| Addition | Benefit | Cost | Kept |
|---|---|---|---|
| Return to library page N at the tile (US2) | no lost place while editing in a row | one query field, two hrefs | yes (spec) |
| Panel removed from the library (FR-009) | one way to edit; less code | ~40 test lines retargeted | yes (spec default) |
| Panel-only CSS/JS removed | no dormant behaviour | 2 small deletions, 3 test deletions | yes |
| `PhotoActions.Active` option kept, unused by pages | avoids catalogue churn | none | yes (severable later) |

Post-design re-check: unchanged. Everything is PASS, N/A, or DEVIATION-handled.

## Project Structure

### Documentation (this feature)

```text
specs/017-photo-edit-screen/
├── plan.md  research.md  data-model.md  quickstart.md
├── contracts/
│   ├── http.md           # L6 new; L1, L2 changed
│   ├── components.md     # P1–P4, J1
│   └── test-impact.md    # A1–A18, B1–B2, C, D1–D2
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Core/Images/Sizes.cs                     # + ForAside(), AsideWidth (P2)
src/Gallery.Web/
├── Admin/Pages/PhotoEditPage.razor(.css)            # new (P1)
├── Admin/Pages/LibraryPage.razor(.css)              # panel removed, icon hrefs, container query removed (P3, P4)
├── Library/LibraryEndpoints.cs                      # L6, L2 landing, PhotoEditPageModel, LibraryPageModel trimmed
├── Library/LibraryReader.cs                         # PhotoAsync, shared tile builder, PageAsync(page)
└── wwwroot/admin-assets/library.js                  # panel-into-view block removed (J1)

specs/001-architecture-stack-design/draft/02d-decisions-delivery.md   # D-20, D-23 (R-10)

tests/Gallery.Core.Tests/SizesTests.cs               # + ForAside exact string
tests/Gallery.Web.Tests/
├── PhotoEditPageTests.cs            # new: L6 markup, 404s, HEAD, reads, saved
├── PhotoEditBrowserTests.cs         # new: SC-002 geometry, SC-004 return, no-JS save, delete landing
├── PhotoEditTimingEvidenceTests.cs  # new, Category=Evidence
└── existing files: test-impact rows only
```

**Structure Decision**: one web project. The new page sits beside the other admin pages; the
panel markup moves with it; reads stay in `LibraryReader`.

## Complexity Tracking

None.
