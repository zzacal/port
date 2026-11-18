# Research: Focused Photo Edit Screen

Read on 2026-11-17 against `main` at 121e150 (all of 016 merged). File references are to that tree.

## R-01 Address of the edit screen

**Decision**: `GET`/`HEAD /admin/library/images/{iid}`, new route **L6**, on the protected `/admin`
group beside L2 (`POST` to the same path). Query: `page` (the library page to return to; same
`PageOf` rule as the other routes, so a missing or bad value is 1) and `saved=1` after a save.

**Rationale**: The form's action is already `/admin/library/images/{iid}` (L2), so the edit
screen's own address equals its form target, the way `/admin/galleries/{gid}` is both B1 and B2.
It inherits the owner policy and the antiforgery filter from the group, so FR-011 costs nothing.
A bad id is 404 like every `{iid}` route (`OpaqueId.IsValid`).

**Alternatives**: `/admin/photos/{iid}` (a new top-level noun the owner bar doesn't have, and the
breadcrumb root would still be Library); keeping `/admin/library?open=` with the grid hidden (one
address doing two jobs; rejected by FR-001's "own address").

## R-02 Layout: AdminLayout's existing side-panel slot

**Decision**: The page renders `AdminLayout` with `Aside` = the panel form (unchanged `SidePanel`
contents) and `main` = one photo stage. Breadcrumb `Library › {title}`, `Current=Library`.

**Rationale**: `AdminLayout` already owns a main + 320 px aside grid at ≥ 1200 px (stacked
below, aside after main) — `src/Gallery.Web/Components/Templates/AdminLayout.razor.css:25-35` — the
same 320 px column the builder's panel uses (`GalleryBuilder.razor.css:6-8`). The library page is
that slot's only consumer today (`LibraryPage.razor:15`); moving the panel to the new page keeps
the slot in use, keeps the chrome in the one container (IX.2), and gives "photo on the left, form
on the right, stacked on phones" with no new layout CSS.

**Alternatives**: a page-local two-column grid inside `main` (duplicates the layout's frame,
which IX.2 assigns to `AdminLayout`); reusing `SoloViewer` (brings the public h1 title, EXIF bar
and pager, which the spec puts out of scope).

## R-03 Photo rendering: size, fit, priority

**Decision**: one `Picture` with `Fit=Contain`, `Priority=High`, `ShowThumbLayer=true`, inside a
page-scoped stage that copies the solo view's stage rules (`SoloViewer.razor.css`: sizer
`max-height: 80vh`, frame centred). `sizes` comes from a new `Sizes.ForAside()` sibling of
`Sizes.ForGrid`, built from the same tokens:
`(min-width: 1200px) calc(100vw - 448px), (min-width: 600px) calc(100vw - 64px), calc(100vw - 32px)`
(448 = 2 × SpaceL page padding + SpaceM aside gap + 320 aside; 64 = 2 × SpaceM; 32 = 2 × SpaceS).

**Rationale**: FR-003 asks for the largest suitable stored size, never the thumbnail: the
derivative ladder is 320…2560 (`Ladder.Widths`), and `sizes` that states the real column width
lets the browser pick e.g. 1280 at 1440 px instead of the grid's 320/640. The 80vh cap is the
public solo view's rule; at 1440 × 900 the chrome above main is about 150 px by estimate (owner
bar 8+44+8, breadcrumb row 44, body padding 48; the SC-002 browser test measures it), so
720 + 150 < 900 and the whole photo is visible (SC-002).
`ShowThumbLayer` paints the cached grid derivative first (IV.15's technique; the owner arrives
from the library tile, so it is in cache). The stored width/height keep layout shift at zero
(IV.8). The original is never referenced (III.1): `Picture` only knows derivatives.

**Alternatives**: `sizes="100vw"` like `SoloViewer` (over-fetches by about the panel's width at
desktop); a measured `calc(100vh - chrome)` cap (ties the page to the chrome's pixel heights;
80vh already fits).

## R-04 Reads and timing

**Decision**: `LibraryReader.PhotoAsync(string iid)` returns the photo as a `LibraryTile` (or null
when unknown/tombstoned) plus the gallery list, with three timed reads: one image find, logged
under the existing op name `db.library.page` (as the panel's off-page read and the builder's
member read already are), then `GalleriesAsync()` (`db.galleries.list`, `db.site.order`). The
tile-building lambda inside `PageAsync` (`LibraryReader.cs:93-115`) becomes a private static
method both call, with identical output. Request timing (`http.request`) is already emitted by
the middleware for every route.

**Rationale**: The library page with a panel open today costs four reads (count, page, galleries,
site order) plus one when the photo is off-page. The edit screen needs no count or page, so it
does strictly fewer reads (FR-013). Reusing the op name keeps `LibraryTimingEvidenceTests.Ops`
and the p95 tooling unchanged; a new name would be cosmetic churn (V.4).

**Alternatives**: a new op `db.library.image` (clearer, but adds a name to pinned lists for no
behavioural gain).

## R-05 Save, refusal and delete landings (L2, L5)

**Decision**:
- L2 success: `303 /admin/library/images/{iid}?page={page}&saved=1` (was
  `/admin/library?page={page}&open={iid}&saved=1`).
- L2 refusal (422, 409, 404-gallery, 400): re-render the edit screen with the same status code,
  reason, and kept entries — exactly the arguments the library re-render passes today.
- L2 photo not found: unchanged (re-render the library with 404 "That photo no longer exists."),
  so the existing refusal test keeps its meaning.
- L5: unchanged (`303 /admin/library?page={page}&deleted=…`). The edit screen posts its `page`.

**Rationale**: FR-005/006/007. The services (`ImageService.SavePanelAsync`,
`ImageDeleteService.DeleteAsync`) are untouched, so stored results, purges and timing events are
the same by construction. Only the handler's render target and one redirect string change.

## R-06 Library page after the panel goes

**Decision**: `LibraryPage` loses `Aside`, the `lib-del-one` form, `Panel`, `TokensFor`,
`GalleryOptions`, `DangerFor`, `DeleteConsequence` (moves to the new page) and the Add-button
demotion (always Primary). `LibraryPageModel` loses `Saved` and the `Edit*` fields;
`LibraryPageData` loses `Open`; `PageAsync` loses its `open` parameter (only caller is
`RenderLibraryAsync`; no test calls it). L1 no longer reads `open`, so an old `?open=` address
renders the plain library (FR-009). Each tile's gallery and edit icons link to
`/admin/library/images/{id}?page={page}`; no tile is ever `Active`.

`PhotoActions` keeps its `Active` parameter and its "Close the editor for …" label: the catalogue
page and `PhotoActionsTests` component facts still render them (no dormant *page* behaviour; it is
a component option no page passes, like other catalogue-only states). Severable: removing the
parameter would move the catalogue baseline and four component assertions for a cosmetic gain.

**Dead code removed** (no dormant behaviour):
- `library.js` "panel into view" block (lines 617-626) and its header comment: there is no
  panel under a grid any more. On the edit screen the stacked panel follows one photo, not 100
  tiles, so no scroll help is needed.
- `LibraryPage.razor.css` `.lib-grid` container query (016 R-07): it only fired while the panel
  narrowed the grid below 800 px. The `.lib-grid` wrapper stays (GridWidthBrowserTests selects it).
- `StatusFromQuery`'s `saved=1 → "Saved"` branch on the library: kept, because it is shared
  wording the edit screen reuses for its own status line (R-07).

## R-07 Status line and Saved state on the edit screen

**Decision**: The edit screen shows a `role="status"` line above the photo (same markup class as
the library's `lib-result`, so `LibraryHtml.Status` finds it) with `Saved` after a save or the
refusal reason, and the panel's `sp-status` says `Saved` — the same two places the library shows
today. Text comes from the existing `StatusFromQuery` (only `saved` applies here).

## R-08 Script

**Decision**: the edit screen loads `library.js` (one module script), which already drives the
TokenCombobox and the DangerConfirm fallback anywhere on a page. Its library-section code is keyed
to `section.lib`, which the edit screen does not render, so none of it runs there. The screen
works without JS: Save and Delete are plain forms, the combobox degrades to its no-JS form (014).

## R-09 Evidence for SC-005

**Decision**: Before any code change, a Setup task runs a Category=Evidence capture on the current
tree: 100 images, 100 `GET /admin/library?page=1&open={iid}` loads (photo on page 1), log saved to
`evidence/pre-timings.log`. After the change the same harness loads
`GET /admin/library/images/{iid}?page=1` 100 times into `evidence/photo-timings.log`. `p95.sh`
(adapted from 016's) compares post p95 ≤ pre p95 × 1.10 + 1 ms and post reads ≤ pre reads (3 ≤ 4).

**Note**: the request log prints the path without its query, so the pre capture must run alone in
its own host (it does: the Evidence test owns its `LibraryHost`).

## R-10 Design record (VII.2)

The 001 draft fragment `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md`
carries both amended texts: line 44 (D-20: "library below 800 px" container-query clause) and
line 71 (D-23 Library: "edit and gallery toggle the side panel"). A scripted edit
(`evidence/design-record-edit.py`, asserting each old string's count) rewrites them, then
`assemble.sh` + `validate.sh` regenerate `docs/design.md`. New D-23 Library text: "…a
gallery/view/edit icon row; edit and gallery open the photo's own edit screen
(`/admin/library/images/{id}`): the whole photo beside the panel (title, caption, galleries,
View, delete), breadcrumb Library › title (feature 017)."

## R-11 Test impact

Sorted in `contracts/test-impact.md`. Summary: 6 redirect-location assertions change string; 12
panel reads retarget from `/admin/library?open=` to the edit screen (selectors stay valid because
the ids `lib-edit`, `lib-gf`, `lib-del-one` and the `side-panel` markup are kept); 3
`PhotoActions` href/active facts and 3 TileIcon panel-toggle facts are rewritten for the new
behaviour; 1 GridWidth case is deleted; 3 scan/route lists gain the new route.
