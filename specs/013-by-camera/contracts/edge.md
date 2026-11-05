# Contract: purge and warm for camera pages (013)

Extends `Edge/PurgeSets.cs` and `Library/OperationResult.cs`. Existing methods keep their
signatures and outputs, because the camera sets are separate functions that are unioned in.

## Paths

- `PurgeSets.CameraListingPath = "/c"`.
- `PurgeSets.CameraPath(slug) = $"/c/{slug}"`.
- `PurgeSets.CameraPhotoPath(slug, iid) = $"/c/{slug}/{iid}"`.

## E1 `PurgeSets.Cameras(IEnumerable<(CameraSnapshot? Before, CameraSnapshot? After)> changes, bool cardsChanged, EdgeBases bases)` (P-CAM)

- `/c` when `cardsChanged`.
- For each pair, for each non-null side: `CameraPath(side.Slug)`.
- For each pair: `CameraPhotoPath(slug, id)` for every id in `CamChanged(before, after)`, under
  the slug of each side that holds that id. `CamChanged` is `PurgeSets.Changed` applied to the
  two items lists: ids in exactly one list; every id in both when the count differs; ids whose
  neighbours or 1-based index differ.

## E2 `PurgeSets.CameraText(string iid, IEnumerable<CameraSnapshot> after, EdgeBases bases)` (P-CAMTEXT)

For each camera holding `iid`: `CameraPath(slug)` and `CameraPhotoPath(slug, iid)`. Never `/c`.

## E3 Scope wiring

- `OperationScope` records the BEFORE camera snapshots from the FIRST projection in the scope
  (`RefreshAsync` or `CompleteAsync`), and the AFTER snapshots from the last.
- `CompleteAsync` adds `PurgeSets.Cameras` over the combined before/after of every changed key,
  plus `CameraText` for every `TextImages` id, to the union.
- It is still ONE `EdgeWork` per request (contracts of 009), and local eviction goes through the
  same `PageCacheEvictor.EvictAsync(set.PagePaths)`.

## E4 Warm

The warm-id loop in `CompleteAsync` (`OperationResult.cs:274-281`) learns two `/c` shapes, using
the AFTER camera snapshots, which the scope already holds (no read):
- `/c/{slug}`: the first 4 items;
- `/c/{slug}/{iid}`: that id.

`PurgeSets.WarmFiles` learns `/c` the same way it handles `/g`: the first 3 camera cards' cover
AVIFs. That uses a new optional parameter `cameraCards` (default empty) so existing callers and
tests are unchanged.

## Worked examples (become PurgeSetTests facts)

| Operation | Before | After | Pages purged (camera part) |
|---|---|---|---|
| Publish gallery G holding a,b (camera K, empty before) | K absent | K items [b,a] | `/c`, `/c/k`, `/c/k/a`, `/c/k/b` |
| Unpublish G (only listed holder of a,b) | K [b,a] | K absent | `/c`, `/c/k`, `/c/k/a`, `/c/k/b` |
| Add c (newest) to a listed gallery | K [b,a] | K [c,b,a] | `/c` (count, cover), `/c/k`, `/c/k/a`, `/c/k/b`, `/c/k/c` |
| Retitle a | K [b,a] | unchanged | `/c/k`, `/c/k/a` |
| Reorder inside a gallery | K [b,a] | K [b,a] | none |
