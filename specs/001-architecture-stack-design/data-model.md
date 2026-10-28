# Data Model: Gallery (designed in 001, built in later features)

This model is a design input to `docs/design.md` section 4. The collections are MongoDB
documents (Platform Constraints). Storage keys refer to R2. All ids are opaque: 128-bit random
values, base32-encoded, 26 characters, never sequential and never derived from titles
(Constitution II.3).

## Owner (collection `site`, single document `_id: "site"`)

| Field | Type | Rule |
|---|---|---|
| claimedSubject | string? | Google `sub`. Null means the site is unclaimed (I.2). |
| claimedAt | datetime? | |
| claimedIp | string? | Taken from CF-Connecting-IP (I.5, PC.7). |
| homeOrder | id[] | Gallery ids in the owner's manual order on the home page. |
| homeCards | GalleryCard[] | Denormalized cards for the public galleries in `homeOrder`, so the home page is one read (IV.9). |

The claim is a single conditional update, `{_id:"site", claimedSubject:null}` → set. A matched
count of 1 means the caller won the claim, and 0 means it lost the race (I.1). No web route ever
writes this document after the claim (I.4).

## Image (collection `images`)

| Field | Type | Rule |
|---|---|---|
| _id | id | Public identity of the image. |
| title | string | Required. 1–200 characters. Defaults to the file name without its extension. |
| caption | string? | 0–2000 characters, plain text. Shown only in the solo view. |
| width, height | int | Pixel size of the source after orientation is applied. Written into markup (IV.8). |
| placeholder | { color: "#rrggbb", preview: data URI } | Preview is a WebP of at most 32 px on the long edge and at most about 1 KB. It is inlined in the markup (IV.7). |
| camera | { make, model, lens, focalMm, fNumber, exposure, iso }? | Extracted by the server from the master, allowlisted fields only (III.3, III.7). |
| derivatives | { w: int, avif: key, webp: key, bytesAvif, bytesWebp }[] | Ladder widths at or below the source width (IV.6). Keys are content-addressed. |
| createdAt | datetime | |
| galleryIds | id[] | Back-references, so that deleting an image can find every gallery that must be fixed and purged. |

An Image exists only after its batch is confirmed. It has no state field (FR-017).

## Gallery (collection `galleries`)

| Field | Type | Rule |
|---|---|---|
| _id | id | Stable identity (II.4). |
| slug | string | Generated from the title. Cosmetic only: the URL is `/g/{id}/{slug}`, and a wrong slug redirects with 301 to the current one (II.4). |
| title | string | 1–200 characters. |
| description | string? | 0–5000 characters, plain text. |
| visibility | `public` \| `private` | Defaults to `private` (II.5). |
| items | id[] | Image ids in the owner's order. Membership and position are the same thing. |
| pinnedCover | id? | Must be an element of `items`. Cleared if that image leaves the gallery. |
| card | GalleryCard | Recomputed whenever `items`, `pinnedCover`, or a member image changes. |
| updatedAt | datetime | |

A GalleryCard is `{ galleryId, slug, title, coverImageId?, coverPlaceholder, coverWidth,
coverHeight, coverDerivatives, imageCount }`. The cover is `pinnedCover` if set, otherwise
`items[0]`. When the gallery has no items, the cover is null and the card renders empty.

## Upload batch (collection `batches`, at most one with `state: open`)

| Field | Type | Rule |
|---|---|---|
| _id | id | |
| state | `open` \| `confirmed` \| `discarded` | A unique partial index on `{state:"open"}` enforces a single open batch (FR-029). |
| items | BatchItem[] | |
| createdAt, updatedAt | datetime | |

BatchItem is defined as follows:

| Field | Type | Rule |
|---|---|---|
| itemId | id | Becomes the Image `_id` when the batch is confirmed. |
| fileName | string | Shown when the owner is asked to pick the file again (FR-027). |
| fileSize | int | 20 MB at most (FR-014). |
| state | `queued` \| `processing` \| `uploading` \| `succeeded` \| `failed` \| `interrupted` | |
| reason | string? | Set when the state is `failed`. |
| masterStored | bool | |
| derivativesStored | key[] | |
| heartbeatAt | datetime | Updated by the client while it works on the item. |
| attemptId | id | A new value on each Restart. Uploads that carry a stale attemptId are refused. |
| validated | { width, height, placeholder, camera, derivatives }? | Set by the server when the item reaches `succeeded`. |

Item state transitions:

```
queued → processing → uploading → succeeded
   any non-terminal state → failed (with reason)
   processing | uploading → interrupted   (no heartbeat for 2 minutes, checked when the batch is read)
   failed | interrupted → queued           (Restart: new attemptId; staged files of the old attempt deleted)
   any state → removed                     (Delete: staged files deleted, item dropped from the batch)
```

Batch state transitions:

```
open → confirmed   only when the batch has at least one item and every item is succeeded (FR-028)
open → discarded   always allowed; all staged keys are deleted (FR-029)
```

On confirm, each item's derivatives are published to the public bucket, its Image document is
created, and the batch is marked confirmed. A confirm that crashes partway through can be re-run.

## R2 key layout

There are two buckets. Only the public bucket is attached to a custom domain; R2 exposes a
whole bucket at a time, not a prefix.

```
gallery-private:  staging/{batchId}/{itemId}/{attemptId}/master.jpg         (see D-masters)
                  staging/{batchId}/{itemId}/{attemptId}/{w}.{avif|webp}
gallery-public:   d/{sha256-16hex}.{avif|webp}                              immutable (IV.21)
```

Derivatives are named by their content hash, so their addresses are unguessable and
content-addressed (II.3, FR-011). On confirm, each derivative is copied across buckets into the
public bucket and its staged copy is deleted. The confirm step is idempotent per item.
A lifecycle rule on the private bucket deletes any `staging/` object older than 7 days. This is the
backstop sweep for orphaned files (FR-030).
