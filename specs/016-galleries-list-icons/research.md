# Research: 016 Icon Controls on the Galleries List

Code findings are from `016-galleries-list-icons` at eef9585 (main a87360e plus the spec). The
probes were `/tmp/probe-016.sh`, `/tmp/probe-016b.sh` and a measured browser probe of the row
widths (R-07). One decision is still OPEN: R-07, the row width at tablet widths.

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

## R-07 Row width at tablet widths: OPEN (owner)

Measured in Chromium (LibraryBrowserFixture). Tile widths and the existing rows:

| width | list tile | builder tile | 015 builder row (4 icons) | today's list row |
|---|---|---|---|---|
| 600 | 136 px | 136 px | icons squeezed to 34 px | 5 lines, 107 px tall |
| 700 | 169 | 169 | icons squeezed to 42 px | 5 lines, 85 px |
| 760 | 189 | 189 | fits | 5 lines, 70 px |
| 800–1199 | 203–304 | 203–303 | fits | 5 lines, 51–70 px |
| ≥1200 | 304–384 | 128–188 | fits | 5 lines, 47–51 px |

- A row of four 44 px icons needs 188 px. Between 600 and about 755 px wide, the 3-column grids
  give cards 136–188 px, so a one-row, four-icon card can't keep 44 px targets there.
- The same squeeze already exists in feature 015's builder: its icons shrink below 44 px in that
  window. 015's browser tests measured 390, 800 and 1440 px, so they missed it.
- Options:
  - A: 2 columns at medium width on the list and the builder (grids 3/2/1 and 4/2/1). One row and
    44 px everywhere, larger thumbnails on tablets. Amends the design record (D-20/D-23 grids)
    and changes both pages' image `sizes`.
  - B: let the row wrap into two rows of two only while it does not fit. CSS only, a shared rule
    for the list and builder rows. 44 px targets kept everywhere; the row is 92 px tall in that
    window. Every card at a given width wraps the same way, so equal heights hold (IX.3).
  - C: accept squeezed targets in that window, as 015 does today. Fails the 44 px target rule
    (015 FR-009).

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
- (B) baselines: `index.html` (+1 `li`); new `molecules/gallery-actions.html`. Every other page
  is byte-identical: Tile, DragHandle and IconButton defaults are untouched; IconPage and
  IconButtonPage move only through the two new icons (`atoms/icon.html` and
  `atoms/icon-button.html` regenerate, and the diff must be only the eye/eye-off additions).
- (C) unchanged: `NewTabLinkTests` (the view icon carries the boxed ↗; the rule from 015 R-06
  holds), `AdminNavConsistencyTests`, `LayoutConsistencyBrowserTests`, `LibrarySourceGuardTests`
  (no `gal-` class reaches a public page), `PublicOutputUnchangedTests`, `OrganismStatePageTests`
  (ContactSheet specimen untouched), `IconRegistryGuardTests` (eye/eye-off are used by
  GalleryActions, so the set stays equal).
