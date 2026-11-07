# Data Model: 015 Icon Controls on Library Photos

No stored shape changes: no new collection, field, index, route or read.

## Existing, reused

- `LibraryTile.GalleryCount` (LibraryReader, already read on every library GET) → the
  membership state. `ViewHref` → the view icon (link or unavailable). `Image.Id`, `Image.Title`.
- `LibraryPageData.Open` (from `?open={iid}`) → which tile's edit and gallery icons are active.
- Builder: `GalleryBuilder` parameters (`ItemIds`, `PinnedIndex`, `RemoveFormId`, `CoverFormId`,
  `UnpinFormId`, `PhotoHrefBase`) are unchanged; only their rendering changes.

## Derived values (pure, `PhotoActions.cs`)

| Value | Rule |
|---|---|
| `Membership` | `Count <= 0` → None; `== 1` → One; else Many |
| icon name | None → `folder-plus`, One → `folder`, Many → `folders` |
| count label | "Not in a gallery" / "In 1 gallery" / "In {n} galleries" (FR-006, owner copy) |

## Tile edit state (per request, no storage)

| `?open=` | This tile | Edit and gallery icon href | Icons state |
|---|---|---|---|
| absent, or another photo | not open | `?page={p}&open={id}#t-{id}` | normal |
| this photo | open | `?page={p}#t-{id}` | active (`aria-current="true"`) |

At most one tile is active because `?open=` holds one id (FR-012).

## Builder cover state → icon (from `CoverState`)

| CoverState | Icon | Element | Label | Pressed |
|---|---|---|---|---|
| Unpinned | star (outline) | button, submits b-cover with `image={id}` | "Make {title} the cover" | false |
| Automatic | star (filled, muted) | span (unavailable) | "Cover: first photo" | n/a |
| Pinned | star (filled) | button, submits b-unpin | "Unpin cover" | true |

## Icon registry (`Icons.Paths`)

There are 8 entries, keyed by Lucide name (research R-01). Invariant (guard test): the set of keys
equals the set of names used in `src/`.
