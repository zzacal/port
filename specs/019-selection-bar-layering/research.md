# Research: 019 Selection bar stays on top of the photos

## R-01 Cause

Decision: the defect is layer leakage from two components, not a missing value on the bar.

`Picture.razor.css` gives `.main` z-index 1 and `.ph`/`.thumb` z-index 0. `Tile.razor.css` gives `.overlay`
(the select circle) z-index 2 and `.on-photo` (title strip) z-index 1. Their containers (`.pic`, `.cell`)
are `position: relative` with z-index auto, which does not contain those values, so every photo layer
competes with the whole page. The pinned bar (`SelectionBar.razor.css`, `position: sticky`, z-index auto)
is below any element with a positive z-index, so photos paint over it and receive its clicks
(`evidence/observed-before.txt`).

## R-02 Fix: contain the layers where they are created

Decision: add `isolation: isolate` to `.pic` (Picture) and `.cell` (Tile). Each becomes its own stacking
context, so its z-index values only order its own children. The bar, later in the page than the grid,
then paints above every tile.

Rationale: one rule fixes the class: any pinned or overlapping element now meets a photo as one flat
box. (Implement correction, decisions #6: the live upload page renders no `Picture` in batch items, so its
footer was never covered; only `/ui` catalogue pages show previews.) Probe: 0/120 covered at every width,
select circle still on top of its photo (`evidence/probe-isolation.txt`).

Alternatives rejected:
- `z-index: 3` on the two bars. Works, but ties the bars to Tile's internal numbers; the next layer added
  inside a tile (an icon at 3) silently brings the defect back. The probe shows `z-index: 1` alone still
  loses 3–4 of 120 points to the select overlay.
- Both (isolation plus `z-index: 1` on the bars). Would also cover a positioned element placed after the
  bar in the page. Nothing positioned follows either bar today (the library's Pager and page count are
  static), so it is marginal work under V.4; recorded as a cheap later step (decisions #3).
- Removing the z-index values inside Picture and Tile. The layer order inside a tile (placeholder under
  picture under title strip under select circle) is needed and is pinned by earlier features.

## R-03 Other consumers of Picture and Tile

`Picture` is used by Tile, BatchItem, SoloViewer (public photo page), SignInCard, PhotoEditPage and the
`/ui` catalogue. `isolation` changes paint order only, never layout, so box sizes and chrome geometry
are unchanged (probe: solo picture 1344x720 before and after). SignInCard's card (z-index 1) over its
background Picture now wins outright instead of by page order. The `TokenCombobox` listbox (z-index 10)
and the drag preview (`position: fixed`, z-index 10) are not inside a Picture or Tile cell, so they still
escape as before. The drag preview is a clone appended inside `.admin-main`, outside any `.cell`
(AdminLayout.razor.css:62-69).

## R-04 Test design

Decision: two new browser facts in new files, sampling 120 points across the pinned bar at several
scroll stops, plus a real mouse click through a spot with a photo under it.

- Library (`LibraryBrowserFixture`): 16 images; viewports 390x600, 800x600, 1440x600 (short windows put
  several photo rows under the bar). Tick two photos, then for scroll stops from 0 to the bottom in
  100 px steps assert every sample hits the bar or a descendant. Then pick a stop where a tile's cell
  lies under "Clear" (computed in the page), check `elementFromPoint` there is a photo layer before the
  click would matter, click with the mouse at that point and assert the bar hides (count 0). Fails today:
  probe shows 96–108 of 120 points covered.
- Upload (`UploadBrowserFixture`): pick two fixture photos, wait for both "succeeded", resize to 390x500,
  scroll in 50 px steps and assert every footer sample hits the footer.
- Images in the library fixture answer 404, but `img`/`picture` boxes keep their stored size and are still
  hit-test targets, so the geometry is real (existing UI-2 facts rely on the same).

## R-05 Test impact

Scoped CSS is not part of rendered markup, so no markup snapshot or baseline moves
(`grep -rln "isolation" specs/*/evidence` and the fixture's inlined-stylesheet check only test length
> 2000). No existing assertion samples paint order over a photo except LibraryBrowserTests.cs:588-590,
which samples a button behind an open modal dialog; a modal is in the browser's top layer, above any
stacking context, so it is unaffected. Expected changes to existing tests: none.
