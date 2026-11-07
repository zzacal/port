# Test impact: 015

Grepped the whole `tests/` tree (source-only) for every marker this feature changes:
`lib-row`, `lib-status`, `MembershipStatus`, `>Edit</a>`, `View ↗`, `>Remove<`, `Make cover`,
`Unpin`, `cover-tag`, `Cover · first photo`, `&#x2807;`, `class="title"`, `text-link` counts,
`UiCatalog.Entries` consumers and `TakeLast(`, and the `target="_blank"` rule.

## (A) Stated changes: the parent edits the expected output (same property, new markup)

| # | File:line | Old | New |
|---|---|---|---|
| A1 | `LibraryPageTests.cs:170-182` (status + View) | `StatusOf` reads `span.lib-status` text: "Not in a gallery" / "Alpha +1" / "Alpha +1 · public in 1"; View is `<a class="text-link" href="/g/{a}/alpha/{iid}"…>View ↗` | read `.pa-group`'s `aria-label`: "Not in a gallery" / "In 2 galleries" / "In 2 galleries"; its `data-icon` is `folder-plus` / `folders` / `folders`; View is `<a class="icon-button pa-view" href="/g/{a}/alpha/{iid}"` with `target="_blank"`; `none` has a `span.is-unavailable` `pa-view` and no `/g/` (kept) |
| A2 | `LibraryPageTests.cs:204,212-218` (row under the title) | `lib-row` holds `lib-status`, `>Edit</a>` with `href="?page=1&open={id}"`, `View ↗`; title before the row; the row after the cell | `photo-actions` holds, in order, `pa-group`, `pa-view` and `pa-edit`; `pa-edit` href = `?page=1&open={id}#t-{id}`; the title is INSIDE the cell (in `.on-photo`, not in `.overlay`); the row is after the cell. `Assert.DoesNotContain("lib-row", overlay)` → `"photo-actions"` |
| A3 | `LibraryMembershipTests.cs` (whole file, 5 facts) | `lib-status` text and link rules (first gallery by title, +N, public in M) | the same fixture (none / one / three); each fact asserts the FR-006 rule instead: `data-icon` and `aria-label` of `.pa-group` ("Not in a gallery"/`folder-plus`, "In 1 gallery"/`folder`, "In 3 galleries"/`folders`); no gallery title and no "public" anywhere in the tile's row. The case-insensitive-first-title fact becomes "the label never names a gallery". The file summary is rewritten. Requirement change (owner clarify: count only). |
| A4 | `LibraryFlowTests.cs:99-104` | 40 tiles whose status text is "Coast · public in 1" | 40 tiles whose `.pa-group` `aria-label` is "In 1 gallery" |
| A5 | `LibraryBrowserTests.cs:488` | row selector `.lib-row` | `.photo-actions`. The hits assertion is unchanged: only the select mark is over the photo. The on-photo title is a `<p>`, not a control. |
| A6 | `BuilderTests.cs:25-31` (`TileOf`, `CoverText`) | find a tile by `<button … value="{iid}">Remove</button>`; `CoverText` = `span.cover-tag` text | find by `<button … name="image" … value="{iid}"` with `aria-label="Remove … from this gallery"`; `CoverState(tile)` = the cover IconButton's `aria-label` ("Unpin cover" / "Cover: first photo" / "Make {title} the cover") |
| A7 | `BuilderTests.cs:63-67` | `<a class="text-link" href="/g/{gid}/coast/{iid}"…>View ↗</a>`; `<button…>Remove</button>` with `class="btn btn-quiet"` | `<a class="icon-button" href="/g/{gid}/coast/{iid}"` with `target="_blank"`; the remove `<button class="icon-button"` keeps `type="submit"`, `form="b-remove"`, `name`/`value`, no danger-confirm |
| A8 | `BuilderTests.cs:276,285-307` | `CoverText` "Cover", "Cover · first photo", ""; `>Unpin</button>`, `>Make cover</button>`, `DoesNotContain(">Unpin<")` | `CoverState` "Unpin cover" (`aria-pressed="true"`), "Cover: first photo", "Make {title} the cover"; the same form/name/value regexes keyed on `aria-label`; `DoesNotContain("aria-label=\"Unpin cover\"")` |
| A9 | `LibraryComponentAdditionsTests.cs:352-370` (K6 builder row) | `class="handle"`, `class="cover-marker"`, `>Remove</button>` with `btn btn-quiet`, `class="text-link"` `View ↗`; `>Unpin</button>`, `>Make cover</button>` | the same order check on `class="handle`, `class="cover-marker"`, the `image-minus` button and the `square-arrow-out-up-right` link; button attrs as A7; unpin/make-cover keyed on `aria-label` |
| A10 | `OrganismComponentTests.cs:264` | exactly one `class="cover-tag">Cover<` | exactly one `aria-label="Unpin cover"` |
| A11 | `NavCataloguePageTests.cs:30,67,113-118` | `MembershipStatusPage` labels; `TakeLast(5)` has MembershipStatus; the fixed-copy fact | `PhotoActionsPage` labels (K8); `TakeLast(5)` has `("PhotoActions","molecules/photo-actions",typeof(PhotoActionsPage))` in the same slot; the fixed-copy fact asserts the three `CountLabel`s |
| A12 | `NavComponentTests.cs:308-330` (K8 MembershipStatus, 2 facts) | MembershipStatus markup | deleted with the component; replaced by `PhotoActionsTests` (new file). Requirement change (decisions). |
| A13 | `IndexTests.cs:16` | `Assert.Equal(37, …)` | `39`, comment `+ 2 from 015 (Icon, IconButton) and MembershipStatus → PhotoActions` |
| A14 | `NewTabLinkTests.cs:27-37` | arrow = text ends in ↗ | arrow = text ends in ↗ OR the anchor's inner markup holds `data-icon="square-arrow-out-up-right"` (R-06); both directions kept |
| A15 | `MoleculePageTests.cs:16-17` (found at implement; missed in plan) | TilePage has exactly 3 `<a class="tile"` and 3 `<div class="cell"` | still 3 `<a class="tile"` (the K8 "Title on photo" specimens take no Href, like library tiles); 6 `<div class="cell"`, comment `+ 3 Title on photo specimens (015 K8)` |

## (B) Baselines (`specs/006-public-site-local/evidence/ui-baseline/`, strict byte identity)

| # | File | Change |
|---|---|---|
| B1 | `index.html` | +2 atom `li` after SelectMark; the MembershipStatus `li` becomes PhotoActions (same slot). Regenerate; the diff is exactly these |
| B2 | `molecules/tile.html` | 3 specimens appended; earlier specimens byte-identical |
| B3 | `molecules/drag-handle.html` | 1 specimen appended |
| B4 | `molecules/cover-marker.html` | 3 specimens appended |
| B5 | `organisms/gallery-builder.html` | tiles change through K6 (title on photo, icon row): a stated change |
| B6 | `molecules/membership-status.html` | deleted |
| B7 | `atoms/icon.html`, `atoms/icon-button.html`, `molecules/photo-actions.html` | new |

Verify before replacing (speckit rule): copy the baseline dir to
`evidence/pre-015-ui-baseline/` in Setup, regenerate, then `evidence/baseline-diff.py` strips
the intended changes per file and must report EQUAL for B1–B4. It must report RESIDUE on a
self-test with one stray edit.

## (C) Unchanged, and why

- `PublicOutputUnchangedTests`: Tile defaults are byte-identical (K3); TilePage specimens are
  only appended (append-tolerant test).
- Every other `UiPagesUnchangedTests` slug: no component they render changes default output.
  `DragHandle`/`CoverMarker` changes are opt-in (`Icon`); `ContactSheetPage` and `GalleriesPage`
  use the defaults.
- `MoleculeComponentTests` CoverMarker/DragHandle facts and `LibraryComponentAdditionsTests:269-296`
  (K4): defaults unchanged.
- `GalleryListTests` (cards, `>Edit</a>`, `View ↗`, `class="handle"`): the gallery list is out of
  scope (FR-021).
- `LibraryTileGeometryTests`: `.title`, `.pic .main img` and `.select-mark .dot` all still exist;
  the select mark is still placed from the photo's corner.
- `LibraryBrowserTests:505-525` (filled buttons `["Save"]`, `[]` at rest): icon buttons are
  transparent and active is a ring (R-05).
- `LibraryBrowserTests` builder drag/keyboard facts (`.b-ctl .handle`, `[role=status]`,
  `button[name='image'][value]`): every hook is kept (K6).
- `AdminLayoutConsistencyBrowserTests`: the sticky aside changes no chrome geometry; it is
  measured with the panel closed and with it open.
- `GalleryFieldBrowserTests`, `LibraryEditTests`, `LibraryMembershipEditTests` and the
  timing/evidence tests: they open the panel by URL; the panel is unchanged.
- `LibraryPageTests.cs:182` (`DoesNotContain("/g/")` for a photo in no gallery): still true; the
  unavailable view is a `<span>`.
- `TileContactSheetAdditionsTests:44`: Tile default output.
- `LibrarySourceGuardTests`: J1 adds no sink.

Any other existing test that goes red is a defect in the change, not a test to edit.
