# Tasks: 019 Selection bar stays on top of the photos

**Input**: plan.md, research.md, spec.md in this folder. No data-model or contracts.

## Rules

- Existing tests MUST pass unmodified (FR-007). A red existing test means the change is wrong; escalate,
  never edit the test.
- The bar's look, position, width and markup stay unchanged (FR-005): no edits to SelectionBar or
  BatchFooter files.
- The facts are written first and MUST fail on today's code (FR-006); the failing output is saved before
  the fix lands.

## Phase 1: Setup

- [ ] T001 Write `specs/019-selection-bar-layering/evidence/gate.sh <label>`: build `-warnaserror`, full suite
  into `artifacts/`, print only `Passed!/Failed!` lines, restore earlier features' evidence dirs on exit
  (copy of 018's gate.sh with 019 paths, adding `specs/018-local-dev-signin/evidence` to the restore list).

## Phase 2: User Story 1 - The selection bar is never behind the photos (P1)

**Independent test**: both new facts green; by hand per quickstart.md.

- [ ] T002 [P] [US1] New file `tests/Gallery.Web.Tests/LayeringBrowserTests.cs`, class `LayeringBrowserTests`,
  `[Collection("library-browser")]`, `[Trait("Category","Browser")]`, ctor `(LibraryBrowserFixture fx)`.
  One `[Theory]` over widths 390, 800, 1440 (height 600): `site.ImagesAsync(16)`, open `/admin/library`,
  wait for `section.lib .selection-bar[data-script='ready']`, click the first two `label.select-mark`s,
  assert the bar is visible. Then for scroll stops 0..max in 100 px steps: sample 40 x-points across the
  bar (inset 2 px) on 3 rows (top+2, middle, bottom-2) with `elementFromPoint`; each must be the bar or
  inside it; on failure report width, scroll stop, point and the hit element's tag.class. Then the click:
  in the page, find a scroll position where some `.lib-tile .cell` lies under the centre of the bar's
  "Clear" button (scrollTo, re-measure); assert such a position exists; `page.Mouse.ClickAsync` at that
  point; assert the bar becomes hidden (`el.hidden`). Covers FR-001, FR-002, FR-006, SC-001, SC-003.
- [ ] T003 [P] [US1] New file `tests/Gallery.Web.Tests/UploadLayeringBrowserTests.cs`, class
  `UploadLayeringBrowserTests`, `[Collection]` and fixture as `UploadBrowserTests` uses (read its header),
  `[Trait("Category","Browser")]`. Open the upload page (`site.OpenUploadAsync()`), set files
  `orientation-6.jpg` and `gps-camera.jpg` on the page's file input (same way `UploadBrowserTests.Pick`
  does), wait until both items are `succeeded` (same wait as `UploadBrowserTests.WaitForState`), resize to
  390x500, then for scroll stops 0..max in 50 px steps sample `upload-batch > .batch-footer` exactly as
  T002 samples the bar; assert at least one stop had a `.bcell` overlapping the footer's box (so the fact
  is not vacuous). Covers FR-003, SC-002.
- [ ] T004 [US1] Run T002 and T003 on today's code; both MUST fail. Save the failing lines to
  `specs/019-selection-bar-layering/evidence/non-vacuity.txt`. If either passes, the fact is wrong: fix
  the fact, not the code.
- [ ] T005 [US1] The fix, verbatim:
  - `src/Gallery.Web/Components/Atoms/Picture.razor.css`, in `.pic { … }` after `width: 100%;`:
    `isolation: isolate; /* 019: the layers below stay inside the picture, so a pinned bar is never under a photo */`
  - `src/Gallery.Web/Components/Molecules/Tile.razor.css`, in `.cell { … }` after `margin-bottom: 8px;`:
    `isolation: isolate; /* 019: the overlay and title layers stay inside the tile (research R-02) */`
- [ ] T006 [US1] Re-run T002 and T003: both green. Append the passing lines to `evidence/non-vacuity.txt`.

## Phase 3: Polish

- [ ] T007 `bash specs/019-selection-bar-layering/evidence/gate.sh final`: build clean, full suite green,
  counts = baseline + 4 (3 theory rows + 1 fact). Save the Passed lines to `evidence/gate-final.txt`.
- [ ] T008 Live check on the dev stack in a real browser (as `evidence/probe-isolation.txt`, without
  injection): 0/120 covered at 390, 800, 1440; screenshot to `evidence/after-1440.png`; append to
  `evidence/probe-isolation.txt`.

## Dependencies

T001 → (T002 ∥ T003) → T004 → T005 → T006 → T007 → T008. All run by the parent (decisions #5).
