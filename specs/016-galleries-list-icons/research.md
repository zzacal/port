# Research: 016 Icon Controls on the Galleries List

Code findings are from `016-galleries-list-icons` at eef9585 (main a87360e plus the spec). The
probes were `/tmp/probe-016.sh`, `/tmp/probe-016b.sh` and `/tmp/probe-016c.sh`, plus two measured
browser probes of row and grid widths (R-07). No open questions remain.

## R-01 Reuse 015's atoms: one new molecule, `GalleryActions`

- Decision: a new molecule `GalleryActions`, built like `PhotoActions` (015 K4). It holds the
  card's row: move (`DragHandle Icon="true"`), visibility (`IconButton`), view (`IconButton`,
  link or unavailable) and edit (`IconButton` link).
  - The pure rules (the visibility icon and label) live in `GalleryActions.cs` and are
    unit-tested.
  - The page passes every href and form id; the molecule builds no admin URL (as FR-020 required
    of 015).
- Rationale:
  - FR-003: one icon-control style. FR-014: its own catalogue page.
  - It is the list's equivalent of PhotoActions, so the two read alike.
- Alternatives:
  - Put the IconButtons inline in `GalleriesPage.razor`. Rejected: no catalogue page (FR-014),
    and the rules could not be unit-tested away from the HTTP host.
  - Generalise PhotoActions to take N actions. Rejected: its parameters and labels are the
    library's, and widening it would move its baselines for no gain.

## R-02 The visibility control: a submit button on the existing `gal-vis-{id}` form

- Today: a `Switch` (checkbox `role="switch"`, `form="gal-vis-{gid}"`, `value="on"`).
  `library.js` submits that form when the switch changes. G4 reads `public=on` to publish, and
  anything else unpublishes. The empty-private switch is disabled, with the reason as a visible
  hint.
- Decision: an `IconButton` `<button type="submit" form="gal-vis-{gid}">`:
  - Private with photos: `eye-off`, label "Private. Make public", `name="public" value="on"`,
    so G4 publishes.
  - Public (with or without photos): `eye`, label "Public. Make private" (or "Public, hidden
    until it has a photo. Make private"). It sends no `public` field, so G4 unpublishes.
  - Private, empty: `eye-off`, Unavailable (a span, `role="img"`), label "Private. Add a photo
    before making this gallery public". The server keeps refusing whatever is posted (the
    PublishRefusalTests [SAFETY] path is unchanged).
- Rationale:
  - A submit button posts its form with no script, so the list works with JS off (spec US2 AS4)
    and needs no library.js handler.
  - The server route, form, CSRF token, `from=list` and the refusal are unchanged (FR-006).
  - No `aria-pressed`: a "pressed eye" reads as "hidden" to some users, and the label already
    states both the state and the action. This differs from the builder's cover star, which is a
    true on/off toggle.
- The `library.js` branch that submits `gal-vis-*` on `change` loses its only consumer. It is
  removed (no dormant code), and its header comment is updated. The builder's switch path
  (return early for non-`gal-vis` forms) is kept.
- Icons: `eye` and `eye-off`, Lucide 1.70.0 (fetched, both present). `Icons.Paths` grows from 8
  to 10. IconTests pins each path's SHA-256, so it gains two hashes. Its fixed `Keys` list and
  its counts (IconPage sizes `8+8+1`, IconButtonPage `5 + Keys.Length`) are stated test changes.

## R-03 Order and position

- Order: move, visibility, view, edit (spec default). Edit is right-most on every card, because
  view stays in place as unavailable when there is no page (as 015 does, IX.3).
- The row centres in the card, like 015's rows.

## R-04 Status words

- `GalleryRow.Status` ("Public", "Private · no photos", …) has one consumer, the list's
  `.gal-status` span. That span is removed (FR-008); the state now reads from the eye and its
  label.
- `GalleryRow.Status` is replaced by the pure label rule in `GalleryActions`, so no orphaned
  property is left.
- Private cards stay dimmed (`is-private`, unchanged).

## R-05 Title on the cover, and the empty card

- Cards with a cover: `Tile TitleOnPhoto="true"` (015 K3). Nothing else changes in Tile.
- Empty cards are drawn by the page (`.gal-tile`, a blank `.cell`). The title moves into that
  cell's bottom edge in a page-scoped band with the same values as 015's `.on-photo`
  (`rgba(0,0,0,.6)`, `#fff`, `4px 8px`). Those values are a photo-overlay pattern, not tokens
  (015 R-04).
- Rejected: change Tile's empty branch to take a caption. That branch renders an `<a>` with
  `href`, and its default output is pinned byte-identical (PublicOutputUnchangedTests), so the
  change moves pinned markup for one card type.
- SC-004 contrast is measured on cover cards in the browser test, as in 015.

## R-06 Catalogue

- New `molecules/gallery-actions` page:
  - specimens: "Public", "Private", "Public, no photos", "Private, no photos", "Long title";
  - each one a full card (Tile with TitleOnPhoto plus the row) at `w-320`.
- `UiCatalog`: insert the entry directly after `VisibilityToggle`. That keeps
  `NavCataloguePageTests`' `TakeLast(5)` molecule list unchanged.
- Catalogue count 39 → 40 (IndexTests, a stated change); `index.html` baseline +1 `li`.
- The ContactSheet page's "Admin gallery list, 2 private" specimen is not touched. It is pinned
  byte-identical by the pre-005 public-output baseline (spec FR-014 amended at plan time).

## R-07 Grid columns: 2 wherever a photo would be narrower than its row (owner, Q2)

Owner rule: the icons are spread across the photo's whole width, so a photo must be at least as
wide as its row, and below that width the grid shows 2 columns. Rows never wrap and never shrink.

Measured in Chromium (`/tmp/016-grid.txt`, LibraryBrowserFixture). Each row's minimum width is
n × 44 px + (n − 1) × 4 px gaps:

- library: 3 icons, 140 px;
- galleries list: 4 icons, 188 px;
- builder: 4 icons, 188 px.

| window | grid as today | library tile | library + panel | builder tile | list tile |
|---|---|---|---|---|---|
| 600 | 3 cols (medium) | 136 | 136 | 136 | 136 |
| 700 | 3 | 169 | 169 | 169 | 169 |
| 760 | 3 | 189 | 189 | 189 | 189 |
| 1199 | 3 | 336 | 336 | 336 | 336 |
| 1200 | 4 (lib, builder) / 3 (list) | 216 | **128** | **128** | 304 |
| 1300 | | 241 | **153** | **153** | 337 |
| 1440 | | 276 | 188 | 188 | 384 |
| 1920 | | 396 | 308 | 308 | 544 |

Two separate squeezes:

1. Medium widths (600–1199): 3 columns give tiles of 136–188 px from 600 to about 760 px. At
   2 columns the narrowest medium tile is (536 − 2·32 − 32) / 2 = 220 px (the grid is 536 px wide
   at a 600 px window). Every row fits, so medium is 2 columns on all three grids.
2. Large widths with a panel beside the grid (the library's side panel, and the builder's
   always-present panel column): the grid is 352 px narrower than main, so 4 columns give
   128–188 px tiles up to about 1440 px. The width that decides this is the grid's own, not the
   window's.

Decision:

- Medium column count 3 → 2 on the library (4/2/1), the builder (4/2/1) and the galleries list
  (3/2/1). The `sizes` attribute follows from the same counts (`Sizes.ForGrid`, D-20: one config
  produces both), so the browser still picks the right derivative.
- At large widths, a CSS container query on a wrapper around each icon-row grid switches the
  4-column grids to 2 columns when the grid itself is narrower than a 4-column row needs:
  - 4 tiles need 4 × row + 3 × 48 px gaps + 2 × 48 px padding;
  - library: 4 × 140 + 240 = 800 px;
  - builder: 4 × 188 + 240 = 992 px.
  The queries sit inside `@media (min-width: 1200px)`, so the one-column phone layout and the
  medium rule are untouched.
  - Precedent: SoloViewer already uses `container-type: inline-size`.
  - A wrapper `div` is needed because a container query reads an ancestor's width, never the
    grid's own, and `section.lib` holds the sticky selection bar, which containment would affect.
- The list keeps 3 columns at large: its narrowest large tile is 304 px, and it has no panel.
- `sizes` at large stays the 4-column value. Inside the container rule the tile can be up to
  about 2× wider than `sizes` says. The browser then fetches a smaller derivative than ideal in
  that case only (the panel open at 1200–1440 px). Accepted: owner-only pages, and the image is
  still shown contain-fit, not upscaled past its derivative.
- Rejected:
  - wrapping or shrinking rows (owner: a row narrower than the photo is a design error);
  - 2 columns at large for every width (it throws away the 4-column grid at 1440+ where it fits).

Scope: the three owner grids with icon rows (spec FR-015). The upload grid (4/3/2, no icon row) and
every public grid (D-20 counts) are unchanged.

## R-08 Timing evidence (FR-012, SC-005)

- The gallery list makes the same reads before and after: `reader.GalleriesAsync` (two) plus
  `db.galleries.list` (one). No read is added.
- Evidence: a Setup task captures 100 GETs of `/admin/galleries` with 30 galleries (24 with
  photos, 6 empty, a mix of public and private) before the change (`evidence/pre-timings.log`).
  An Evidence test repeats it after. `p95.sh` (copied from 015, route
  `GET /admin/galleries 200`) checks p95 ≤ pre × 1.10 + 1 ms, with `db_reads` equal on every
  line.
- The cost per card is 4 IconButtons. That is the per-icon component cost 015 measured (issue #3),
  but over about 30 cards instead of 100 tiles, so it is well under 015's library cost. If #3
  lands first, it shrinks further. No dependency is taken on #3.

## R-09 Test impact (grepped, source-only)

- (A) stated changes:
  - `GalleryListTests`:
    - `Card_statuses_dimming_View_and_Edit_links_and_switch_states`: status spans, View/Edit text
      links, `.gal-row` parts and the switch become `GalleryActions` assertions (eye/eye-off and
      labels, view link or unavailable span, edit href, `form="gal-vis-{gid}"` on the
      visibility button);
    - the `SwitchOf` and `CardOf` helpers (CardOf keys on the edit href, which keeps working).
  - `BuilderTests:357-361`: list status text "Public · hidden until it has a photo" plus the
    switch checked/not disabled become the eye label "Public, hidden until it has a photo. Make
    private" on an enabled button with no `public` field.
  - `PublishRefusalTests:207-218`: the empty card's disabled switch and its visible `<p
    class="hint">` become the unavailable eye-off span with the reason in its label. The builder
    half of that test is unchanged.
  - `LibraryBrowserTests:238`: `.gal-row .handle` becomes `.gallery-actions .handle`.
  - `LibraryBrowserTests:404-424`: the switch click and checked state become a click on the
    visibility button, then the eye state. Publish and unpublish are still asserted from storage.
  - `LibraryBrowserTests:496`: row selector `.gal-row` becomes `.gallery-actions`.
  - `IconTests`: Keys +2, two hashes, the counts above.
  - `IndexTests`: 39 → 40.
- (B) baselines: `index.html` (+1 `li`); new `molecules/gallery-actions.html`;
  `organisms/gallery-builder.html` (grid class `cs-m3` → `cs-m2`, the tiles' `sizes` medium term
  `/ 3` → `/ 2` with the medium gap, and the new grid wrapper `div`). Every other page is
  byte-identical: Tile, DragHandle and IconButton defaults are untouched; IconPage and
  IconButtonPage move only through the two new icons (`atoms/icon.html` and
  `atoms/icon-button.html` regenerate, and the diff must be only the eye/eye-off additions).
- R-07 grid change, (C) unchanged: no test pins the admin pages' column counts. The `cs-m3`
  assertions (`OrganismPageTests:19-20`, `OrganismStatePageTests:67-68`, `CameraPageComponentTests`,
  `PageComponentTests:99`, `TileContactSheetAdditionsTests:51`) render the ContactSheet catalogue
  page and the public pages, which keep their counts. Browser tests that index tiles (`Nth(n)`) or
  measure one tile are column-agnostic.
- (C) unchanged: `NewTabLinkTests` (the view icon carries the boxed ↗; the rule from 015 R-06
  holds), `AdminNavConsistencyTests`, `LayoutConsistencyBrowserTests`, `LibrarySourceGuardTests`
  (no `gal-` class reaches a public page), `PublicOutputUnchangedTests`, `OrganismStatePageTests`
  (ContactSheet specimen untouched), `IconRegistryGuardTests` (eye/eye-off are used by
  GalleryActions, so the set stays equal).
