# Data Model: By Camera

## New collection `cameras` (derived; single writer `SiteProjector`)

One document per camera that has at least one visible photo. A camera with no visible photo has
no document (FR-009). The collection is entirely derived: deleting it and running any projection
rebuilds it.

| Field | Type | Rule |
|---|---|---|
| _id | string | Grouping key: `norm(make) + "\u001f" + norm(model)` (research R-02). `norm` trims, collapses whitespace runs to one space and lower-cases invariantly. A missing value is `""`. Never both empty. |
| slug | string | `Slug.From(name)`. Unique across the collection. A key keeps the slug already stored on its document while that slug is still unique. A new key takes `Slug.From(name)`, or the first free `-2`, `-3` and so on. So adding a camera never moves another camera's address (FR-007). Indexed unique. |
| name | string | Display name: `ExifFormat.Camera` over the whitespace-collapsed (case kept) make and model of the camera's newest visible photo (FR-005). |
| items | id[] | Visible photo ids, `createdAt` desc then `_id` asc (R-03). Each id exactly once (FR-003). |
| rank | int | 0-based position in the listing: count desc, then `name` ordinal ignore-case, then `_id` (FR-006). Indexed. |
| card | CameraCard | Denormalised so the listing is one read (IV.9, FR-016). |

### CameraCard (embedded)

| Field | Type | Rule |
|---|---|---|
| slug | string | Copy of the document's slug. |
| name | string | Copy of the document's name. The card caption and alt text. |
| imageCount | int | `items.Count`, at least 1. |
| cover | CoverImageDoc | The existing gallery-card cover shape (`Data/Documents.cs` `CoverImageDoc`) for `items[0]`: imageId, width, height, placeholder, derivatives. |

## Visibility (research R-09, [SAFETY] II.2 / II.5)

A photo is **visible** when all of these hold:
- it is in the `items` of at least one **listed** gallery (public and `items` non-empty, the same
  test as `SiteProjector.Cards`, which uses card `imageCount >= 1`);
- it has no `deletingAt`;
- its `camera` has a make or model that is non-empty after `norm`.

Nothing else makes a photo visible. In particular, upload confirm (in no gallery) and the library
never do.

## Pure projection

`SiteProjector.Cameras(IReadOnlyList<GalleryDoc> listed, IReadOnlyList<ImageDoc> images,
IReadOnlyDictionary<string, string> storedSlugs) -> List<CameraGroupDoc>`

This is deterministic, so equal input gives a BSON-equal output. `listed` comes from `public static SiteProjector.Listed(homeOrder, galleries)`, the same rule `Cards` uses. It is public so the test fixture `InvariantChecker` can call it. `storedSlugs` maps key → slug
from the current `cameras` documents, for slug stability.

## Write (inside `ProjectWithNavAsync`, timer `db.site.project`)

1. `db.cameras.load`: one `images` find over the union of listed galleries' items (projection as
   in research R-05), then one `cameras` find (all documents).
2. Compute `Cameras(...)`.
3. `db.cameras.write`, run only when something differs: delete the documents whose key left, and
   replace-upsert each document whose BSON differs. Unchanged documents are not written.
4. Return `CameraChange(Before, After)`: for each key in the symmetric difference or whose document
   changed, its stored document (or null) and its new one (or null). `ProjectionResult` gains a
   third member, `Cameras`. `ProjectAsync()` keeps its signature.

## Existing entities: no stored change

`images`, `galleries` and `site` gain no field and no write. `ProjectionResult` gains a member. Its
constructor is called only in `SiteProjector.cs:131` (grep: no test constructs it).

## Value objects (web layer)

- `CameraSnapshot(Key, Slug, Items)`: what `PurgeSets.Cameras` / `CameraText` consume, built from
  a `CameraGroupDoc`.
- `CameraChange`: `IReadOnlyList<(CameraSnapshot? Before, CameraSnapshot? After)>`, plus whether
  any card or rank changed (for `/c`).
