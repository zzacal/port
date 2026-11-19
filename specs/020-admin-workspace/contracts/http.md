# HTTP Contract: 020 Admin Workspace

Everything here is in the existing protected `/admin` group: owner policy plus
`AdminAntiforgeryFilter` (Constitution I.3, I.6). Every non-GET request needs the `X-CSRF-TOKEN`
header (value rendered in `<admin-workspace data-csrf>`). Every response carries the existing admin
`Cache-Control: no-store` and `X-Robots-Tag: noindex` (`AdminHeaders`). Every request emits one
`http.request` event.

Shared rules:

- `{gid}` and `{iid}` must pass `OpaqueId.IsValid`, else 404.
- JSON in, JSON out. Errors are `{ "error": "<code>", "message": "<owner-facing sentence>" }` with
  the status the service's `OperationResult.StatusCode` gives (404, 409, 422). A body that is not
  JSON, or is missing a required field, is 400 `bad-request`.
- `message` is the result line the page shows (FR-018). The server writes it
  (`Workspace/Messages.cs`); the script never composes one.
- Body limits (`RequestSizeLimitAttribute`; oversize → 413): 4 KiB unless stated.
- **AMENDED (tasks) C-14**: A-routes read their JSON with `UploadEndpoints.ReadAsync<T>(ctx)` inside the handler (no `[FromBody]` binding), so the antiforgery filter answers 400 first and a bare POST with the CSRF header answers 400 `bad-request`, never 500.
- Signed out: GET → 302 `/admin/signin`; POST → the 007 behaviour. Missing or bad token → 400.

## Pages and fragments

| Id | Route | Collection |
|---|---|---|
| W1 | `GET|HEAD /admin` | All photos |
| W2 | `GET|HEAD /admin/unplaced` | Not in a gallery |
| W3 | `GET|HEAD /admin/g/{gid}` | one gallery; unknown gallery → 404 |
| W4 | `GET /admin/tiles/{iid}` | one tile fragment; unknown or tombstoned → 404 |

**AMENDED (tasks) C-12**: document titles W1 "All photos", W2 "Not in a gallery", W3 the gallery's title (planning default). The list's current row has `aria-current="page"`.

`?photo={iid}` on W1 to W3 is read by the script only (large view). The server ignores it.

The `X-Workspace` request header selects the response body on W1 to W3:

| Header | Body | Reads |
|---|---|---|
| absent | the full document: `AdminLayout` with the collections list, the middle region and the panel templates | W1/W2: 4; W3: 5 (research R-02) |
| `main` | the middle region only: heading row, upload-tray mount, grid (first chunk or the whole gallery), and `<template data-panel="default">` for this collection | 2 |
| `chrome` | the collections list and the collection-independent templates (photo panel, selection panel, checklist rows, tray, marks) | 3 |
| `more` (W1, W2 only; needs `?before={ticks}.{id}`) | tiles only, followed by `<span data-next="{cursor}">` when more exist | 1 |

Any other header value → 400. A fragment response has no `<html>`; it is `text/html`.

Chunks: `Library:PageSize` tiles (100), newest first (`createdAt` desc, `_id` desc). A malformed
`before` → 400.

## Writes

| Id | Route | Body | Success | Service (FR-027) |
|---|---|---|---|---|
| A1 | `POST /admin/api/photos/{iid}` | `{ "title"?: string, "caption"?: string }` | 200 `{ title, caption, message }` | `ImageService.UpdateTextAsync`; an absent field keeps the stored value |
| A2 | `POST /admin/api/photos/delete` (16 KiB) | `{ "ids": [iid…] }` 1 to cap | 200 `{ deleted, leftovers, failed, message }` | `ImageDeleteService.DeleteManyAsync` |
| A3 | `POST /admin/api/galleries` (16 KiB) | `{ "title": string, "ids"?: [iid…] }` | 201 `{ id, slug, title, added, message }` | no ids: `GalleryService.CreateAsync`; ids: `ImageService.BulkAddAsync(ids, null, title)` |
| A4 | `POST /admin/api/galleries/order` (16 KiB) | `{ "order": [gid…] }` | 200 `{ message }` | `GalleryService.ReorderHomeAsync` |
| A5 | `POST /admin/api/galleries/{gid}` (16 KiB) | `{ "title"?: string, "description"?: string }` | 200 `{ title, slug, description, message }` | `GalleryService.UpdateTextAsync` |
| A6 | `POST /admin/api/galleries/{gid}/visibility` | `{ "public": bool }` | 200 `{ public, message }` | see below |
| A7 | `POST /admin/api/galleries/{gid}/photos` (16 KiB) | `{ "ids": [iid…] }` 1 to cap | 200 `{ added, skipped, public, message }` | `ImageService.BulkAddAsync(ids, gid, null)` |
| A8 | `POST /admin/api/galleries/{gid}/photos/remove` (16 KiB) | `{ "ids": [iid…] }` 1 to cap | 200 `{ removed, public, listed, message }` | NEW `GalleryService.RemoveManyAsync` |
| A9 | `POST /admin/api/galleries/{gid}/order` (64 KiB) | `{ "order": [iid…] }` | 200 `{ message }` | `GalleryService.ReorderAsync` |
| A10 | `POST /admin/api/galleries/{gid}/cover` | `{ "image": iid | null }` | 200 `{ cover, message }` | `GalleryService.SetCoverAsync` |
| A11 | `POST /admin/api/galleries/{gid}/delete` | `{}` | 200 `{ message }`; already gone → 200 with "That gallery was already deleted." | `GalleryService.DeleteAsync` |

"cap" is `Library:PageSize` (100), the bulk limit the services enforce today (FR-008). More ids →
422 with the service's reason.

A6, in this order (the G4 handler's order; [SAFETY] II.6):

1. Unknown gallery → 404.
2. `public: true` → `PublishAsync(gid, confirmed: true)`. The service checks emptiness first and
   answers 409 with `GalleryService.EmptyPublishReason`.
3. `public: false` on a gallery that is not public: an empty one answers 409 with the same reason
   whatever was sent; a non-empty one answers 200 with no write.
4. `public: false` on a public gallery → `UnpublishAsync`.

A8 `RemoveManyAsync(gid, ids)`: validates the count (1 to cap, else 422), opens ONE operation scope,
calls the existing `RemoveOneAsync(gid, iid, scope)` per id (an id that is not a member is
skipped), completes the scope once. Timing op `library.bulk.remove`. `listed` in the answer is
false when a public gallery became empty (the message then says visitors can no longer see it).

Messages (exact; `{n}` photo/photos pluralised, `{g}` the gallery title):

| Case | Message |
|---|---|
| A1, A5 | `Saved` |
| A7 private | `{n} added to {g}.` (+ ` {k} were already in it.` when skipped > 0) |
| A7 public | the same, then ` Visitors can see them now.` (`it` for one) |
| A7 nothing new | `Already in {g}.` |
| A3 no ids | `{g} created. It is private.` |
| A3 with ids | `{g} created with {n}. It is private.` |
| A8 | `{n} removed from {g}.` then, when a public gallery became empty, ` Visitors can no longer see {g} until it has a photo.` |
| A6 on | `{g} is now public. Visitors can see it.` |
| A6 off | `{g} is now private.` |
| A4 | `Gallery order saved.` |
| A9 | `Order saved.` |
| A10 | `{photo title} is now the cover of {g}.` / `{g} uses its first photo as the cover.` |
| A2 | `{n} deleted.` plus today's leftovers/failed sentences (**AMENDED (tasks) C-07**, exact: `{n} deleted.` ("1 photo deleted."), then when leftovers > 0 ` {k} files could not be removed and were logged.`, then when failed > 0 ` {f} could not be deleted. Try again.`; LibraryEndpoints.cs:555-557) |
| A11 | `{g} deleted. Its photos are still in All photos.` |

Refusal messages are the services' existing reasons, unchanged.

## Upload

Unchanged: U4 master check, U5 progress, U6 fail, U7 complete (still answers
`{ "state": "succeeded" }`; it does NOT join), U8 restart, U9 delete, the asset rules, the body
limits. Their paths stay under `/admin/upload/`.

Changed:

**U2 `GET /admin/upload/state`** gains fields:

```json
{ "batch": { "id": "…",
  "destination": null | { "id": "…", "title": "…", "public": true },
  "added": ["<imageId>", …],
  "limits": { "maxFileBytes": 20971520, "maxPhotos": 100 },
  "items": [ { …as today…, "state": "queued|processing|uploading|succeeded|joining|failed|interrupted" } ] } }
```

`confirming` is removed. **AMENDED (tasks) C-01**: the batch object keeps `"confirming": false`, always false (`UploadFlowTests.cs:200` reads it and passes unmodified, SC-012); only the item-state value `confirming` is gone. `{ "batch": null }` when there is no open batch, or when the open batch
has no items and no `added` ids. `joining` is never reported as `interrupted`.

**U3 `POST /admin/upload/items`** gains two optional fields:
`{ "files": […], "gallery": gid | null, "visibleToVisitors": bool }`.

1. Get or create the open batch.
2. Destination: when the batch has no items and no `added` ids, set `destination` to `gallery`
   (clearing `added`); an unknown gallery → 404 `gallery-gone`, nothing registered. Otherwise the
   stored destination stays, whatever was sent.
3. [SAFETY] II.5a: when the effective destination gallery is public and `visibleToVisitors` is not
   `true` → 409 `{ "error": "destination-public", "message": "{g} is public. Visitors will see
   these photos." }`, nothing registered.
4. Per-file checks, append and presign exactly as today.

The answer gains `"destination": null | { id, title, public }` (the effective one). A request with
neither new field behaves exactly as today (library only).

**U8, U9**: an item in `joining` → 409 `not-active`. `confirm-in-progress` no longer exists.

New:

**U12 `POST /admin/upload/items/{itemId}/join`**: `{ "attemptId": "…" }`.

1. The item must be in the open batch with that attempt (404 / 409 `stale-attempt` as the other
   item routes) and in state `succeeded` or `joining` (else 409 `not-active`).
2. [SAFETY] Claim: atomically set `joining`.
3. Publish the item (`JoinService.JoinAsync`; **AMENDED (tasks) C-02**: there is no `JoinService`; the method is `ConfirmService.JoinAsync(batchId, itemId, attemptId)`, built from the body of the private `ConfirmItemAsync`; `ConfirmService` keeps its name and its public statics, loses `ConfirmAsync`/`RunAsync`/`ConfirmResult`/`ConfirmStatus`): copy-if-match of the master and every size on the
   validated ETags, image upsert with `$setOnInsert`, staged prefix deleted. A staged file that is
   missing is accepted only when its destination exists. A refused copy: the item becomes `failed`
   "changed after validation", the public names this run created and the master copy are removed,
   no image is created → 409 `{ "error": "changed-after-validation", "message": … }`.
4. Pull the item from the batch and push its id to `added`.
5. Destination: when the batch has one, `GalleryService.AddAsync(gid, [itemId])`. Gallery gone →
   unset the batch's destination; the photo stays in the library.
6. 200 `{ "imageId": "…", "gallery": null | { "id", "title", "public" }, "galleryGone": bool }`.

Repeating U12 for an item already joined answers 404 (it is no longer in the batch); the page
treats that as done when the id is in `added`.

Events: `upload.join` (whole call), `s3.head`/`s3.copy`/`s3.delete`/`s3.list`,
`db.image.upsert`, `db.batch.update`, the gallery add's own events, and the line
`upload.joined {batch} {item} {gallery} {public} {duration_ms}`.

[SAFETY] II.5b: with no destination, U12 calls no gallery method and enqueues no edge work.
[SAFETY] II.5c: U12 never writes a gallery's visibility. [SAFETY] II.5d: U12 runs only from
`succeeded`/`joining`, states reachable only through U7's validation.

**U13 `POST /admin/upload/clear`**: `{ "batchId": "…" }`. Not the open batch → 404. Any item in
effective `queued`, `processing`, `uploading`, `succeeded` or `joining` → 409
`{ "error": "still-working", "message": "Wait for the photos that are still working, or remove them." }`.
Otherwise: delete the staging prefix of every remaining item (failed, interrupted), remove the
items, clear `added` and `destination`. 200 `{ "dropped": n, "message": "{n} were not added." }`
(message omitted when n is 0). Event: `upload.cleared {batch} {dropped}`.

Removed: U1 `GET /admin/upload`, U10 `POST /admin/upload/confirm`, U11
`POST /admin/upload/discard`.

## Removed routes (FR-004)

`/admin/upload`, `/admin/upload/confirm`, `/admin/upload/discard`, `/admin/library`,
`/admin/library/images/{iid}` (GET and POST), `/admin/library/add`, `/admin/library/delete`,
`/admin/library/images/{iid}/delete`, `/admin/galleries` (GET and POST), `/admin/galleries/order`,
`/admin/galleries/{gid}` (GET and POST), `/admin/galleries/{gid}/visibility`, `/order`, `/cover`,
`/remove`, `/delete`. None is mapped; each falls to the existing `/admin/{**rest}` catch-all and
answers what any unknown admin address answers. No redirect exists.

## Static assets

`/admin-assets/workspace.js?v={hash8}` joins the versioned assets (`AdminAssets`), with the same
cache rules as `upload.js`. `library.js` and `reorder.js` are deleted.
