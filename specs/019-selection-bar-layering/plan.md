# Implementation Plan: Selection bar stays on top of the photos

**Branch**: `019-selection-bar-layering` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/019-selection-bar-layering/spec.md`

## Summary

Photos draw over the library's pinned selection bar and take its clicks. Cause (research R-01): the
`Picture` atom and the `Tile` molecule give their inner layers z-index values (picture 1, placeholder
0, title strip 1, select overlay 2) without containing them, so those values compete with the whole
page, and the pinned but un-layered bar loses. Fix at the source (R-02): each component keeps its
layers to itself (`isolation: isolate` on Picture's `.pic` and Tile's `.cell`). The pinned bars then
paint above the photos with no number chosen to beat the tiles' numbers. Probe: 0 of 120 sample
points covered at 390, 800 and 1440 px (today 96–108 of 120), select circle still on top of its photo.
The upload batch footer is fixed by the same Picture line. One browser fact per bar guards it.

## Technical Context

**Language/Version**: C# / .NET 10, Razor components with scoped `.razor.css`

**Primary Dependencies**: none added

**Storage**: N/A (presentation only)

**Testing**: xUnit + Playwright browser facts, existing fixtures `LibraryBrowserFixture` and
`UploadBrowserFixture`

**Target Platform**: admin pages in desktop and phone browsers

**Project Type**: web app (`src/Gallery.Web`, `tests/Gallery.Web.Tests`)

**Performance Goals**: unchanged; no server work added

**Constraints**: existing tests unmodified (FR-007); the bar's look unchanged (FR-005)

**Scale/Scope**: 2 CSS declarations, 2 browser facts

## Constitution Check

| Principle | Status |
|---|---|
| I Single owner | PASS: not touched. |
| II Public visibility | PASS: no data or route change. Picture is also on public pages; isolation changes only what its own layers compete with, never layout (R-03). |
| III Published images leak nothing | PASS: not touched. |
| IV Performance | PASS (IV.1–2 justified N/A): no server work, no new request; request timing events unchanged. IV.5: the effect is visual, verified by real-browser measurement (T002, T003). |
| V Minimal scope | PASS. V.4 additions beyond the literal request: (a) upload footer check: the fix itself costs nothing there (same Picture line); the fact is about 40 lines; benefit: the same defect on the upload page can't return unseen. (b) Library regression fact: today's only check samples one point in a gap and never saw this defect. Dropped as marginal: a z-index on the bars as a second guard (decisions #3). |
| VI Official CLIs | PASS: no scaffolding. |
| VII Design before code | PASS: the design record says nothing about layering; no deviation, no record edit. |
| VIII Cost ceiling | PASS. |
| IX Shared containers | PASS: the fix lives in two shared components, no page edits; chrome geometry unchanged (paint order only), so the measured chrome test is unaffected. |

Post-design re-check: unchanged, all PASS.

## Project Structure

### Documentation (this feature)

```text
specs/019-selection-bar-layering/
├── spec.md, plan.md, research.md, quickstart.md, decisions.md, tasks.md
├── checklists/requirements.md
└── evidence/  observed-before.txt, before-1440.png, probe-isolation.txt, gate.sh, non-vacuity.txt
```

No data-model.md (no data) and no contracts/ (no interface change; the two declarations are given
verbatim in tasks.md).

### Source Code

```text
src/Gallery.Web/Components/Atoms/Picture.razor.css      .pic gains isolation: isolate
src/Gallery.Web/Components/Molecules/Tile.razor.css     .cell gains isolation: isolate
tests/Gallery.Web.Tests/LayeringBrowserTests.cs          new: library bar fact
tests/Gallery.Web.Tests/UploadLayeringBrowserTests.cs    new: upload footer fact
```

**Structure Decision**: new facts in new files, so every existing test file stays byte-identical.

## Complexity Tracking

None.
