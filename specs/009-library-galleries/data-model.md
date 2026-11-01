# Data Model: 009 Library and Gallery Management

Collections are the existing D-13 ones (`images`, `galleries`, `site`); `batches` (008) is
untouched. Field names are the BSON names already in `src/Gallery.Web/Data/Documents.cs`. Changes
are marked NEW or CHANGED. No migration: the site is pre-production, and the removal command
(R-11) clears seeded content.

## Image (`images`)

| Field | Type | 009 |
|---|---|---|
| `_id` | 26-char base32 | unchanged |
| `seedKey` | string, absent for uploads | read only by the removal command (`$exists:true, $nin:[null,""]`) |
| `sourceHash` | string, absent for uploads | unchanged |
| `title` | string | edited: trimmed, 1..`Library:TitleMax` (200), not whitespace-only |
| `caption` | string or absent | edited: 0..`Library:CaptionMax` (2000), plain text; an empty save stores null |
| `width`, `height`, `placeholder`, `camera`, `derivatives[]` | | unchanged; `derivatives[].avif/webp` are the public object keys |
| `galleryIds` | string[] | DERIVED index of the galleries whose `items` hold this image (R-01) |
| `createdAt` | date | library sort key |
| `deletingAt` | date, absent | NEW tombstone set at the start of a delete (R-02); tombstoned images are hidden from the library and are not "remaining" references |

Indexes: NEW `{createdAt:-1, _id:-1}` (library page); NEW `{"derivatives.avif":1}` and
`{"derivatives.webp":1}` (shared-file check); existing partial unique `seedKey_1`. All created by
`Indexes.EnsureAsync` (moved from the seeder, called once per process before the first owner write,
as 008 confirm already does).

## Gallery (`galleries`)

| Field | Type | 009 |
|---|---|---|
| `_id` | 26-char base32 | `OpaqueId.NewId()` on create |
| `seedKey` | CHANGED `string?`, `[BsonIgnoreIfNull]` | absent on owner galleries (FR-048) |
| `slug` | string | `Slug.From(title)`; recomputed on rename |
| `title` | string | 1..200, trimmed, not whitespace-only |
| `description` | string or absent | 0..5000, plain text |
| `visibility` | `"public"` or `"private"` | private on create; changed only by publish / unpublish |
| `items` | string[] | AUTHORITATIVE ordered membership; no duplicates |
| `pinnedCover` | string or absent | always a member of `items`; cleared when that image leaves |
| `card` | GalleryCard | recomputed after every change to items, pin, title or a member's title |
| `updatedAt` | date | set on every write |

Invariants (checked by `InvariantChecker` in tests after every operation; SC-009):
- `items` has no duplicates; every id in `items` names an existing, non-tombstoned image after the
  operation completes.
- `pinnedCover` is absent or an element of `items`.
- For every image, `galleryIds` equals the set of galleries whose `items` contain it.
- `card` equals `CardBuilder.Build(gallery, coverImage)`.

### GalleryCard (`card`, and each element of `site.homeCards`)

Unchanged shape: `galleryId, slug, title, imageCount, cover{imageId,width,height,placeholder,
derivatives}`. Cover = `pinnedCover` when set, else `items[0]`, else no cover. Built by one
function, `CardBuilder.Build`, used by every write path (no copies; orchestration rule).

## Site (`site`, `_id: "site"`)

| Field | 009 |
|---|---|
| `homeOrder` | every gallery id, public and private; create appends; delete removes; reorder replaces after the exact-set check |
| `homeCards` | recomputed by `SiteProjector` after every operation: cards of galleries in `homeOrder` order that are public AND have `imageCount ≥ 1` (II.6) |
| `claimedSubject`, `claimedAt`, `claimedIp` | NEVER written by 009 code (`OwnerWriteGuardTests` scans for these names; the projector updates only `homeOrder`, `homeCards`, `updatedAt` with `$set`) |

The home page stays one read of `site.homeCards` (IV.9, SC-007).

## State transitions

### Gallery visibility (II.5, II.6)

```text
            create (private, empty)
                    │
   private ──publish(confirm=1, items ≥ 1)──▶ public
      ▲                                         │
      └──────────────── unpublish ──────────────┘
publish with items = 0  → refused 409 "Add a photo before making this gallery public", no write
public, last photo removed/deleted → stays public, card has no cover, absent from homeCards,
                                     pages 404 (E-16); a later add brings it back with no publish
```

The publish write is conditional: `updateOne({_id, "items.0": {$exists:true}}, {$set:{visibility:
"public"}})`; matched 0 → refusal (closes the race with a remove in another tab).

### Image delete (R-02)

```text
live ──set deletingAt──▶ tombstoned ──pull from galleries, cards, site──▶ ──delete files──▶
     ──delete document──▶ gone
```

Repeating `DeleteAsync` on a tombstoned image resumes from "pull from galleries".

## Value objects (not stored)

- **LibraryPage**: page number, total pages, total count, 100 `LibraryTile`s
  (`ImageView`, status text, view href or null, gallery count, public-gallery count).
- **GalleryRef**: id, slug, title, visibility, photo count (from `items` length); the picker and
  status lines use it.
- **OperationResult**: `Ok`, `Refused(reason, status)`, `Stale`, plus counts (`added`, `skipped`,
  `deleted`, `leftovers`) for the PRG status query.
- **EdgeWork**: operation name, page addresses, file addresses, warm image URLs, enqueue time.
- **Settings** (`LibraryOptions`, section `Library`): `PageSize` 100, `TitleMax` 200,
  `CaptionMax` 2000, `DescriptionMax` 5000. (`EdgeOptions`, section `Edge`): `ZoneId`, `ApiToken`
  (user-secrets only), `SiteBaseUrl` (default `https://localhost:5081`), `PurgePages` (false),
  `MaxUrlsPerCall` 100, `MaxUrlsPerSecond` 800, `QueueCapacity` 1000.
  User-secrets key names pinned for the owner: `Edge:ZoneId`, `Edge:ApiToken`.
