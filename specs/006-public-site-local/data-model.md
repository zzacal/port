# Data Model: 006 Public Site, Local Walking Skeleton

This follows the design content model (`docs/design.md` §4) for the read path. It adds `seedKey`,
which keeps seeded ids stable (research R-06). Upload-batch collections arrive with 008 and are
not created here.

## Mongo collections (database `gallery_dev`; tests use `gallery_test_<guid>`)

### `site` (one document, `_id: "site"`)

| Field | Type | Rule |
|---|---|---|
| _id | string | Always `"site"`. |
| claimedSubject, claimedAt, claimedIp | null | Set to `null` only when the seed CREATES the document (`$setOnInsert`). The seed never writes them on an existing document, so a reseed never reopens a claim (I.4). Owned by 007. |
| homeOrder | string[] | Every gallery id, public and private, in manifest order. |
| homeCards | GalleryCard[] | Cards for the public galleries in `homeOrder` order. This is the only thing the home page reads (IV.9). |
| updatedAt | datetime | Set by the seed. The seed updates only `homeOrder`, `homeCards` and `updatedAt` (`$set`), never replacing the document. |

### `galleries`

| Field | Type | Rule |
|---|---|---|
| _id | string | 26-char lowercase base32 of 128 random bits (II.3). Stable across reseeds through `seedKey`. |
| seedKey | string | The manifest `key`. Unique index. |
| slug | string | Derived from the title (R-06). The URL is `/g/{_id}/{slug}`. |
| title | string | 1–200 chars. |
| description | string? | 0–5000 chars, plain text. |
| visibility | `"public"` \| `"private"` | From the manifest. The manifest default is `private` (II.5). |
| items | string[] | Image ids in manifest order. |
| pinnedCover | string? | Must be in `items`. The seed rejects the manifest otherwise. |
| card | GalleryCard | Recomputed by the seed. |
| updatedAt | datetime | |

### `images`

| Field | Type | Rule |
|---|---|---|
| _id | string | 26-char base32, random. Stable through `seedKey`. |
| seedKey | string | Source file name, e.g. `DSCF2703.jpg`. Unique index. |
| sourceHash | string | sha256-16 of the original's bytes. Reuses preparation across renames. Not secret, and reveals nothing beyond identity. |
| title | string | 1–200 chars. Defaults to the file name without its extension (D-13). |
| caption | string? | 0–2000 chars. |
| width, height | int | Pixel size of the largest prepared WebP, read back after encoding (IV.8). The design model says "source after orientation". Only the aspect ratio matters for layout, and the largest derivative has the same ratio up to rounding, so this follows 003's samples. |
| placeholder | { color: `#rrggbb`, preview: data URI } | Preview is WebP, at most 32 px on the long edge (IV.7). |
| camera | { make, model, lens, focalMm, focal35Mm, fNumber, exposure, iso }? | Allowlist only (R-05). Each field is optional. Null when no field is present. |
| derivatives | { w, avif, webp, bytesAvif, bytesWebp }[] | `avif`/`webp` are object keys (`{sha256-16}.avif`), never URLs. URLs are built at render time from `Storage:ImageBaseUrl` (FR-005). |
| galleryIds | string[] | Back-references, kept consistent by the seed. |
| createdAt | datetime | Set on first seed and kept on reseed. |

**Never stored**: GPS or any location field, original file paths, original EXIF beyond the
allowlist (FR-013, SC-011).

### GalleryCard (embedded)

`{ galleryId, slug, title, imageCount, cover: CoverImage? }`, where `CoverImage` is
`{ imageId, width, height, placeholder, derivatives }`. The cover is `pinnedCover` if set,
otherwise `items[0]`. It is null for an empty gallery, and the card renders an empty cell (E-16).

## Mapping to the component model (Gallery.Core, unchanged records)

- `images` document → `ImageView(Id, Title, Width, Height, Variants, PlaceholderDataUri,
  DominantColor, Exif)`. `Variant(Width, AvifUrl, WebpUrl)` URLs are `ImageBaseUrl + "/" + key`.
- Caption is not on `ImageView`. It is passed separately to SoloViewer's new optional `Caption`
  parameter (contracts/components.md). The 003 record is unchanged.
- Home card → `ContactSheetItem(ImageView?, Href, Title)`. See contracts/components.md for the
  empty-cover change.

## Read shapes (FR-004, SC-004)

| Page | Reads | Projection |
|---|---|---|
| `/` | `site` by `_id` (1) | `homeCards` |
| `/g/{id}/{slug}` | `galleries` by `_id` (1); `images` `_id ∈ items` (2) | gallery: all except `card`; images: `_id, title, width, height, placeholder, derivatives` |
| `/g/{id}/{slug}/{imageId}` | `galleries` by `_id` (1); `images` `_id ∈ {prev, cur, next}` (2) | gallery: `slug, title, visibility, items`; images: full for current, `_id, derivatives` for neighbours |

The second read is skipped when the first returns not found or a redirect is due, so those
responses log 1 read.

## Seed-side files (not in Mongo)

### `.dev-prepared/<sourceHash>/` (gitignored)

| File | Content |
|---|---|
| `photo.json` | `{ sourceHash, sourceName, width, height, color, preview, derivatives:[{w, avif, webp, bytesAvif, bytesWebp}] }` |
| `{sha256-16}.avif`, `{sha256-16}.webp` | Derivatives, metadata-free (FR-012). |

### `seed/manifest.json` (committed). Schema: contracts/seed.md

## State transitions

None in this feature. Content changes only by reseeding, which is a full reconcile:

1. Upsert by `seedKey`.
2. Delete only documents that HAVE a `seedKey` not in the manifest. Documents without a
   `seedKey`, such as ones created later through 009's UI, are never touched.
3. Recompute every card and `homeCards`.
