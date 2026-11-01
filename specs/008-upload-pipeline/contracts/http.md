# HTTP Contract: 008 Upload Pipeline

These endpoints sit in the existing `/admin` group: owner policy plus `AdminAntiforgeryFilter`
(Constitution I.3, I.6; 007 contracts/http.md). Every non-GET request needs the
`X-CSRF-TOKEN` header, whose value the page renders in `data-csrf`. Every response gets the
existing admin `Cache-Control: no-store` and `X-Robots-Tag: noindex` (AdminHeaders). Every request
gets one `http.request` event (FR-037).

Shared rules:
- Ids in paths must match `OpaqueId.IsValid`, or the endpoint answers 404.
- An item request carries `attemptId` in its JSON body. The server answers 409
  `{"error":"stale-attempt"}` when it does not equal the item's current attempt, and 409
  `{"error":"not-active"}` when the item's effective state forbids the action. It answers 404 when
  the item is not in the OPEN batch (deleted, or the batch is closed), and 409
  `{"error":"confirm-in-progress"}` while `confirmStartedAt` is set (except `/confirm`).
- An unauthenticated request gets the 007 behaviour: 302 to `/admin/signin` for GET, 401/302 per
  the 007 contract for POST. A missing or bad CSRF token gets 400.
- Body limits (`RequestSizeLimitAttribute`; an oversize body answers 413) are derived in
  research R-15.
- Error JSON is `{"error": "<code>", "message": "<owner-facing sentence>"}`. Messages never echo a
  metadata value or a key.

## U1 `GET|HEAD /admin/upload`: upload page

200 `text/html`, rendered from `UploadPage.razor` (contracts/components.md). One `db.batch.read`.
Query `?confirmed={batchId}`: when that batch is `confirmed`, the page shows the result state
("{confirmedCount} photos added to your library", a link to `/admin/library`, and an empty drop
target). Any other value is ignored. Timing: SC-010 is read from this route's `http.request`.

## U2 `GET /admin/upload/state`: batch JSON

200 `application/json`:
```json
{ "batch": null }
```
or
```json
{ "batch": { "id": "…", "confirming": false,
  "limits": { "maxFileBytes": 20971520, "maxPhotos": 100 },
  "items": [ { "itemId": "…", "attemptId": "…", "fileName": "a.jpg", "fileSize": 123,
               "state": "queued|processing|uploading|succeeded|failed|interrupted",
               "percent": 0, "reason": null, "masterStored": false } ] } }
```
`state` is the EFFECTIVE state.

## U3 `POST /admin/upload/items`: register picked files (limit 128 KiB)

Request: `{ "files": [ { "name": "a.jpg", "size": 1234, "type": "image/jpeg" } ] }` (1–100 entries).

Server, in order:
1. Get or create the open batch (`db.batch.create` on insert; a DuplicateKey re-reads).
2. Per file: refuse `type != image/jpeg` ("not a JPEG"), `size <= 0 || size > MaxFileBytes`
   ("{x.x} MB, over the 20 MB limit"), and a name that is empty after sanitising.
3. Atomically append the accepted files up to the remaining capacity; the rest are refused with
   "batch is full (100 photos)".
4. For each accepted item, presign PUT `staging/{b}/{i}/{a}/master.jpg` with content type
   `image/jpeg` and expiry `PresignMinutes`.

200:
```json
{ "batchId": "…",
  "accepted": [ { "index": 0, "itemId": "…", "attemptId": "…",
                  "master": { "url": "https://…", "contentType": "image/jpeg" } } ],
  "refused":  [ { "index": 1, "name": "b.heic", "reason": "not a JPEG" } ] }
```
More than `MaxPhotosPerBatch` entries in one request answers 400. Events: `db.batch.update`, and
`s3.presign` per item (SC-006 is read from `s3.presign`).

## U4 `POST /admin/upload/items/{itemId}/master`: check the stored original (limit 4 KiB)

Request: `{ "attemptId": "…" }`. The item must be effective queued, processing or uploading.

Server: HEAD the master, then a ranged read of 256 KB (one 1 MB retry), then the research R-06
checks. It sets `master` and `heartbeatAt`, and computes `Ladder.For(width)`.

200:
```json
{ "width": 4000, "height": 6000,
  "sizes": [ { "w": 320, "avif": { "url": "…", "contentType": "image/avif" },
                         "webp": { "url": "…", "contentType": "image/webp" } } ] }
```
On failure the item becomes `failed` with a reason, the attempt prefix is deleted, and the server
answers 422 `{"error":"rejected","message":"<reason>"}`. Reasons: "not a JPEG", "unreadable JPEG
header", "{x.x} MB, over the 20 MB limit", "original not found". A missing object answers 422 and
leaves the item in its state (the page may retry the PUT). Events: `upload.master-check`, `s3.head`,
`s3.get-range`, `s3.presign` × n, `db.batch.update`.

## U5 `POST /admin/upload/items/{itemId}/progress`: heartbeat (limit 4 KiB)

Request: `{ "attemptId": "…", "state": "processing|uploading", "percent": 0 }`. It moves the stored
state forward only (queued→processing→uploading) and sets `heartbeatAt` and `percent`. It does
nothing to a `succeeded`/`failed` item (409 not-active). 204. The page sends it on every state
change and at least every 20 s while it works on the item (6× under the 2-minute threshold).

## U6 `POST /admin/upload/items/{itemId}/fail`: client-side failure (limit 4 KiB)

Request: `{ "attemptId": "…", "reason": "Out of memory while preparing sizes" }`. The reason is
truncated to 200 characters and has control characters stripped. Sets `failed`; staged files of the
attempt are kept until Restart or Delete (L-02). 204.

## U7 `POST /admin/upload/items/{itemId}/complete`: report done, validate (limit 16 KiB)

Request:
```json
{ "attemptId": "…",
  "placeholder": { "color": "#a1b2c3", "preview": "data:image/webp;base64,…" },
  "timings": [ { "op": "browser.decode-orient", "ms": 812.4 }, … ],
  "camera": { … }   // optional; IGNORED (FR-027, III.7)
}
```
The server first logs each accepted timing (research R-16). It then validates in this order:
1. Master re-check: same length, ETag and dimensions as recorded.
2. Every expected size: HEAD + GET, reader 1, reader 2, agreement, with sha256 computed.
3. Placeholder: decoded, both readers, size and edge limits, colour pattern.

Pass: 200 `{ "state": "succeeded" }`. The item gets `succeeded` + `validated` (L-01).

Fail: 422 `{ "error": "rejected", "message": "<reason naming the file, e.g. '640.webp: EXIF
chunk'>" }`. The item becomes `failed`, the attempt prefix is deleted, and NOTHING is written to the
public bucket (FR-026). An `upload.rejected` event is logged.

Events: `upload.validate` (whole photo; SC-007), `s3.head`/`s3.get`/`s3.get-range` per file,
`db.batch.update`, and `browser.*`.

## U8 `POST /admin/upload/items/{itemId}/restart` (limit 4 KiB)

Request: `{ "attemptId": "…" }` (the current attempt). The effective state must be failed or
interrupted. The server issues a new attempt id. It copies the master to the new attempt when
`master` is set (if-match), deletes the old attempt prefix, and resets
`state=queued, percent=0, stored=[], validated=null, heartbeatAt=now`.

200:
```json
{ "attemptId": "new", "needsFile": false, "fileName": "a.jpg",
  "sizes": [ …as U4… ] }
```
or, with no stored master:
```json
{ "attemptId": "new", "needsFile": true, "fileName": "a.jpg",
  "master": { "url": "…", "contentType": "image/jpeg" } }
```
Afterwards, a request that carries the OLD attemptId gets 409 stale-attempt, and a PUT to an old
presigned URL writes under the old attempt prefix, which no later step reads (FR-011, FR-019).

## U9 `POST /admin/upload/items/{itemId}/delete` (limit 4 KiB)

Request: `{ "attemptId": "…" }` or `{}`. Delete is allowed in any state and is idempotent for the
current batch. The server `$pull`s the item and deletes the `staging/{b}/{i}/` prefix (all
attempts). 204 (L-04).

## U10 `POST /admin/upload/confirm` (limit 4 KiB)

Request: `{ "batchId": "…" }`. Gate: the batch is open, has ≥1 item, and every effective state is
`succeeded`. Otherwise 409:
```json
{ "error": "not-ready", "message": "Restart or delete the 2 photos that aren't ready",
  "notReady": 2 }
```
Run (research R-09; idempotent, re-entrant after a crash): 200
`{ "batchId": "…", "count": 40, "redirect": "/admin/upload?confirmed=…" }`.

Events: `upload.confirm.item` per photo, `upload.confirm` (whole batch; SC-008), `s3.copy`,
`s3.list`, `s3.delete`, `db.image.upsert`, `db.batch.update`, `upload.confirmed`. No purge or warm
call (FR-033).

## U11 `POST /admin/upload/discard` (limit 4 KiB)

Request: `{ "batchId": "…" }`. Allowed on an open batch that is not confirming. The server deletes
the `staging/{batchId}/` prefix and sets `state: discarded`. 200 `{ "redirect": "/admin/upload" }`.
Event: `upload.discarded`.

## Catch-all

The existing `/admin/{**rest}` 404 stays. U1–U11 are mapped before it. `/admin/library` stays 404
(009).

## Static assets (outside `/admin`)

| Path | Cache-Control | Notes |
|---|---|---|
| `/admin-assets/upload.js?v={hash8}` | `public, max-age=31536000, immutable` | page script (research R-11, R-12) |
| `/admin-assets/upload-worker.js?v={hash8}` | same | module worker |
| `/admin-assets/vendor/{pkg}-{ver}/…` | same | jSquash/ThumbHash, version in the path (research R-01) |
| the same paths with no `v` | `no-cache` | |

These assets are not owner-gated. They hold no secret, and the FR-043 scan covers them.

## Request flow (one photo)

```text
page ─register─▶ U3 ─▶ master URL
page ─PUT master.jpg────────────▶ R2 staging            (Content-Type only; FR-044)
page ─U4 master check─▶ server ─HEAD+range─▶ staging ─▶ size URLs (ladder from the server-read width)
worker: decode+orient → resize → AVIF/WebP → placeholder
page ─PUT {w}.avif / {w}.webp ──▶ R2 staging            (≤4 parallel; U5 heartbeats)
page ─U7 complete + placeholder + timings─▶ server ─HEAD+GET each─▶ two readers ─▶ succeeded | failed+cleanup
owner ─Confirm─▶ U10 ─copy-if-match─▶ public/ + masters/ ; images upserted ; staging deleted
```
