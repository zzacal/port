# Data Model: 020 Admin Workspace

No collection is added. Photos (`images`), galleries (`galleries`) and the site document are
unchanged in shape and in who writes them.

## Upload set (collection `batches`, the one document with `state: "open"`)

| Field | Change | Meaning |
|---|---|---|
| `destination` | NEW, string, omitted when unset | the gallery id photos of this upload set also join. Set by U3 only while the set has no items and no `added` ids; unset when that gallery is found gone at a join; cleared by U13 |
| `added` | NEW, array of image ids, default empty | photos joined in this upload set, oldest first. Pushed by U12, cleared by U13 and when U3 starts a new set |
| `items[].state` | NEW value `joining` | claimed by a join; never reported as interrupted; U8 and U9 refuse it |
| `confirmStartedAt`, `confirmedAt`, `discardedAt`, `confirmedCount` | no longer written or read | documents written before this feature keep them; nothing reads them |
| `state` | only `open` is written from now on | `confirmed` and `discarded` documents from before stay in the collection, inert |

The unique partial index on `{state: "open"}` stays: at most one upload set.

Item state machine:

```text
queued → processing → uploading → succeeded → joining → (leaves the batch; image exists)
   any of the first three → failed | interrupted (as today)
   failed | interrupted → queued (U8 restart, new attempt)
   joining → failed ("changed after validation", copy refused)
   any state except joining → removed (U9)
   failed | interrupted → removed (U13 clear)
```

An open batch found at deploy time with `succeeded` items (awaiting the old confirm) is handled by
the normal resume path: the tray reopens and joins them into the library (no destination).

## Read models (not stored)

- **Collection**: `all`, `unplaced` (live photos whose `galleryIds` is empty), or a gallery id.
- **Chunk cursor**: `{createdAt ticks}.{id}` of the last tile sent.
- **Tile data** carried on each cell: id, title, caption, gallery ids.
- **Gallery row**: id, slug, title, public, photo count, in `site.homeOrder` order.

## Validation rules (all existing; the workspace adds none)

| Rule | Source |
|---|---|
| title not blank, ≤ `Library:TitleMax`; caption ≤ `CaptionMax`; description ≤ `DescriptionMax` | `ImageService`, `GalleryService.TitleProblem` |
| a bulk action takes 1 to `Library:PageSize` ids | `ImageService.BulkAddAsync`, `ImageDeleteService.DeleteManyAsync`; NEW the same check in `GalleryService.RemoveManyAsync` |
| a gallery needs a photo to be public | `GalleryService.PublishAsync` (II.6) |
| an order must name exactly the current members | `GalleryService.ReorderAsync`, `ReorderHomeAsync` (409 stale) |
| upload limits, per-photo checks | `UploadValidator`, `MasterCheck`, `DerivativeValidator` (III.5 to III.7) |
| NEW: an upload aimed at a public gallery needs `visibleToVisitors: true` at U3 | constitution II.5a |
