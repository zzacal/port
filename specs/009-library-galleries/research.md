# Research: 009 Library and Gallery Management

Phase 0 for `plan.md`. Each item: Decision, Rationale, Alternatives. Probe output is in
`evidence/probe-results.txt` (P1–P4). Decisions taken in the owner's place are rows 15+ in
`decisions.md`.

## R-01 Write model: ordered single-document updates, Gallery.items authoritative

**Decision**: No multi-document transactions. Every operation is a fixed sequence of
single-document updates that is safe to repeat:

1. Write the gallery document(s) (`items`, `pinnedCover`, `title`, `slug`, `visibility`). Array
   writes carry the prior value in the filter (`{_id, items: <array read>}`), so a concurrent
   change makes the update match 0 and the operation answers "changed in another tab".
2. Rewrite `galleryIds` on every image whose membership changed (`$addToSet` / `$pull`).
3. Recompute each affected gallery's `card` (one read of the cover image).
4. Recompute `site.homeCards` from `site.homeOrder` and the cards of public, non-empty galleries
   (`SiteProjector`, one read of the galleries in `homeOrder`, one write).

`Gallery.items` is the source of truth; `Image.galleryIds` is a derived index. `MembershipRepair`
(a function, also run at the start of image delete and by the removal command) rebuilds
`galleryIds` for a set of images from the galleries that hold them, so any interrupted sequence is
finished by repeating it (FR-017, FR-036, SC-009).

**Rationale**: P1 shows the dev Mongo and the Testcontainers fixture are standalone servers, where
transactions are unavailable. Atlas supports them, but the code must run identically locally. With
one owner the only concurrency is two tabs, which the array filter covers.

**Alternatives**: Transactions (would need a replica-set fixture and dev container change, and a
second code path for standalone). Storing membership only on images (the public gallery page
would need a query by `galleryIds` plus a sort key, breaking the D-13 shape the reader uses).

## R-02 Image delete: tombstone first, files last, record removed last

**Decision**: `ImageDeleteService.DeleteAsync(imageId)` runs, in order:

1. Set `deletingAt` on the image (tombstone). The library query excludes tombstoned images.
2. For each gallery whose `items` contains the image (query by `items`, not only `galleryIds`):
   `$pull` it, unset `pinnedCover` when it matched, recompute the card. Then recompute the site.
3. Storage: delete `masters/{id}.jpg` from the private bucket; for each derivative key (AVIF and
   WebP, every width) delete it from the public bucket unless another image without `deletingAt`
   references it (one query on the multikey indexes `derivatives.avif`, `derivatives.webp`).
   A missing object counts as deleted (S3 delete is idempotent; P3). A failure is caught per key,
   logged as `library.delete_leftover {image} {bucket} {key} {error}` (error = exception type name),
   counted as a leftover in the result, and never stops the sequence.
4. Delete the image document.
5. Enqueue purge: the deleted file URLs plus the affected pages (R-06).

A repeat of `DeleteAsync` on a tombstoned image resumes at step 2. Bulk delete calls this method
once per id, sequentially, and collects per-item results (FR-013, decisions row 11).

**Rationale**: FR-017 needs "finishable by repeating". Removing the record last means a crash
leaves a tombstone a repeat can find; removing gallery membership before the record means no
gallery ever lists a missing image. Shared content-hash names are protected (Clarification 3).

**Alternatives**: Delete the record first (a crash loses the key list, files become unfindable).
`DeleteObjects` multi-delete (P3 shows it works on S3Proxy; kept as the recorded alternative
because per-key deletes give per-key failure logging and bulk volume is ≤ 100 × 13 keys).

## R-03 Reorder and stale tabs

**Decision**: A reorder posts the complete ordered id list. The server reads the current array,
refuses with 409 and the Clarification 5 message unless the posted ids are exactly the current
set (same members, no duplicates), and writes with the current array in the filter. Text saves are
plain `$set` (last write wins).

**Rationale**: Clarification 5; protects FR-036 without versioning documents.

## R-04 Library query and paging

**Decision**: `LibraryReader.PageAsync(page)`: `images.find({deletingAt: {$exists:false}})
.sort({createdAt:-1, _id:-1}).skip((page-1)*100).limit(100)` with a grid projection, plus
`countDocuments` with the same filter, plus one read of every gallery (`_id, slug, title,
visibility, items` count via `$size` projection) and one read of `site.homeOrder`. A page past the
end renders the last page. New index `images {createdAt:-1, _id:-1}`. Four reads; at 5,000 images
`skip` costs at most 4,900 index entries.

Status line per tile (FR-007) and View link (FR-005) are computed in memory from the gallery list:
"Not in a gallery" / "In N gallery|galleries" / "In N galleries · public in M"; View opens
`/g/{gid}/{slug}/{imageId}` for the first gallery in `homeOrder` holding the image (every gallery
that holds the image has a page).

**Rationale**: SC-002 (≤ 500 ms p95 for 100 tiles). Uploaded and seeded images both carry
`createdAt` (008 confirm, 006 seeder).

**Alternatives**: Range paging on `createdAt` (no page numbers; Previous needs reverse queries).

## R-05 Storage deletes

**Decision**: Reuse `ImageStore.DeleteAsync` (op `s3.delete`), at most 8 in flight (the 008 confirm
limit), with the per-key catch of R-02 (it needs the outcome, which `TryDeleteAsync` does not
return; `TryDeleteAsync` and its `upload.cleanup_failed` template stay upload's). No new storage
method.

**Rationale**: P3 confirms delete of a missing key is not an error on S3Proxy; R2 behaves the same
(S3 semantics). The R2 on-demand suite (`GALLERY_R2_TESTS=1`) gains one delete-missing-key case.

## R-06 Purge and warm: `IEdgeCache`, background queue, three modes

**Decision**:
- `PurgeSet` (pure, `Gallery.Core`-free logic in `Gallery.Web/Edge/PurgeSets.cs`) computes the
  page and file addresses for each operation from BEFORE and AFTER snapshots of the affected
  galleries (`id, slug, visibility, items`). Rules (FR-038, widened by decisions row 7):
  - a gallery "has a page" when its `items` is non-empty, public or private; take the union of
    before and after, so a gallery emptied by the change is still purged;
  - "listed" = public and non-empty; the home page is purged when any affected gallery is listed
    before or after;
  - photo pages purged on a membership/order change = added and removed images plus every image
    whose (previous, next) neighbour pair differs between before and after;
  - rename: every page path under the old slug and the new slug;
  - addresses are absolute: pages on `Edge:SiteBaseUrl`, files on `Storage:ImageBaseUrl`.
- `IEdgeCache.Enqueue(EdgeWork)` writes to a bounded `Channel<EdgeWork>` (capacity 1,000, built
  in). `EdgeWorker : BackgroundService` drains it, splits addresses into calls of at most 100,
  spaces calls so at most 800 URLs/s leave (P4), purges, then warms. The owner's request never
  awaits it (FR-040, SC-003).
- `IEdgeProvider` has three implementations, chosen at startup:
  - `LogOnlyEdgeProvider` when `Edge:ZoneId` or `Edge:ApiToken` is missing (default, decisions
    row 6): logs `edge.purge` / `edge.warm` with `mode=log-only`, `outcome=skipped` and the
    addresses.
  - `CloudflareEdgeProvider` when both are set: `POST
    https://api.cloudflare.com/client/v4/zones/{zone}/purge_cache` with `{"files":[...]}` and
    `Authorization: Bearer` (P4, permission Cache Purge only, FR-043). Only file URLs on the image
    hostname are sent; page URLs are logged `outcome=skipped reason=no-hosting` until the hosting
    feature sets `Edge:PurgePages=true`. One retry after 2 s on HTTP 429 or error code 1134, then
    log the failure. Warm GETs the purged image files anonymously.
  - `RecordingEdgeProvider` (tests only, `tests/.../Fixtures`): captures every call, can hold a
    gate to prove the request does not wait.
- Warm scope: each purged page (when page warming is enabled) and every AVIF width of the images in
  the first row of each purged gallery page (4) and of the home page (3). With page purge disabled,
  warm covers the image files only.

**Rationale**: Spec FR-038–FR-043, decisions rows 6 and 10. `System.Threading.Channels` and
`BackgroundService` are built in (V.2). Pure address computation makes SC-004 a unit-test matrix.

**Alternatives**: Purge inline in the request (violates FR-040). Tag or prefix purge (5 requests/min
on Free, D-16). Hangfire/queues (packages, no need).

## R-07 Local output cache: per-page tags, evicted in the request

**Decision**: Every cached public response gets the tag `page:{path}` (path without query) in
addition to the 006 `public-page` tag. After an operation saves, the endpoint calls
`IOutputCacheStore.EvictByTagAsync` for every page path in its purge set (old slug paths included),
before returning (FR-044, in-memory and fast, so it may sit in the request).

**Risk carried in from P2**: the probe could not run (approval prompt denied). The framework
documents that output caching applies only to endpoints with a policy (`CacheOutput()` or a base
policy); the 006 public endpoints add a tag to `IOutputCacheFeature` at execution time but never
attach the policy, so the 60 s cache may not be active at all. The first implement task is a test
with `Site:OutputCache=true` that asserts a second GET of `/` is served from the cache (a datastore
read counter stays at 1) and that eviction drops exactly the tagged page. If the cache is inactive,
the fix is `.CacheOutput(PublicCache.PolicyName)` on the three public page routes when
`Site:OutputCache` is true; the public markup does not change. Logged as decisions row 23.

## R-08 Owner pages: server-rendered forms, small admin scripts, no htmx

**Decision**:
- Every action is a real form POST answered with `303 See Other` back to the page (PRG), carrying
  a short status in the query (`?saved=1`, `?added=35&skipped=5`, `?deleted=3&leftovers=1`), which
  the page renders as the status text. A refusal re-renders the page with `422` (invalid field),
  `409` (stale reorder) or `409` (empty publish) and the reason, and changes nothing.
- The library side panel opens through `?open={imageId}` (the tile link), so it is
  server-rendered and needs no script.
- Publish confirmation is a server step: a publish POST without `confirm=1` renders the page with
  the 005 `VisibilityToggle` Warning state; "Make public" posts `confirm=1`. The empty refusal is
  checked first, so it applies whatever the request carries (FR-030, SC-006).
- Two plain ES module scripts under `wwwroot/admin-assets/` (008 R-12 caching):
  `library.js` (selection bar count, Clear, Select all on this page, the gallery picker dialog and
  its public-gallery note, delete confirm wiring, Public switch auto-submit) and `reorder.js`
  (`<reorder-grid>` custom element: pointer drag with a ghost slot, keyboard lift/arrows/drop/Escape
  with a live-region announcement, then submits the hidden order form). References to elements
  declared later are resolved at use time (orchestration pitfall).
- No htmx: no requirement needs partial swaps (V.2). Admin pages may require scripts (V.3); every
  save still works as a form without them except drag and bulk selection.

**Rationale**: Mirrors 008's pattern (plain modules, no build step). PRG makes bare and repeated
POSTs predictable; a bare POST with no fields keeps stored values (FR-010).

## R-09 Field limits from server settings

**Decision**: `LibraryOptions` (`Library:PageSize` 100, `TitleMax` 200, `CaptionMax` 2000,
`DescriptionMax` 5000), validated at startup like `UploadOptions`. Pages pass them to the `Input`
atom's `MaxLength`, plus new optional `Required` and `Pattern` (`.*\S.*`) parameters, so the
browser blocks an empty, blank or over-long value before submit. The server applies the same
options: trimmed title 1..max, caption/description 0..max, otherwise 422 with the reason and the
stored value kept. The upload title rule (`ConfirmService.MaxTitleChars`) reads the same option.

**Rationale**: Owner rule "limits enforced at selection, sourced from server config"; FR-010.

## R-10 Components: optional parameters only, catalogue pages untouched

**Decision**: The 005 `GalleryBuilder` already puts the drag handle and cover control in a row
under the title (005 decisions row 4), and the `/ui/` ContactSheet gallery-list specimen already
does too, so FR-004 needs no catalogue change. The owner pages compose the existing components; new
behaviour arrives as optional parameters whose default output is byte-identical
(`references/razor-optional-params.md`), covered by component tests in a NEW file
`LibraryComponentAdditionsTests.cs`. `UiPagesUnchangedTests`, `PublicOutputUnchangedTests` and the
catalogue page tests stay unmodified. Details in `contracts/components.md`.

**Rationale**: `UiPagesUnchangedTests` compares every catalogue page byte for byte; appending a
specimen would force a test edit.

## R-11 Seed retirement and the removal command

**Decision**:
- Delete `Seed/Seeder.cs`, `Seed/SeedCommand.cs`, `Seed/Manifest.cs`, `Seed/PhotoJson.cs`,
  `seed/manifest.json`, `scripts/seed-manifest.sh`, `scripts/prepare-photos.sh`. `git mv`
  `Seed/DerivativeValidator.cs` and `Seed/CameraFields.cs` to `Upload/` (namespace
  `Gallery.Web.Upload`). `Seeder.EnsureSeedKeyIndexesAsync` moves to `Data/Indexes.cs`
  (`Indexes.EnsureAsync`, which also creates the 009 indexes); `ConfirmService` calls it.
- `Program.cs`: `seed` is gone; `remove-seeded` runs `Maintenance/RemoveSeededCommand`. It builds
  the same configuration as the old seed (appsettings + Development + env + user-secrets), applies
  the same target guard (`Maintenance/DevTargetGuard`, exit 4 naming each failed check), then
  deletes every gallery with a non-empty `seedKey` through `GalleryService.DeleteAsync` and every
  image with a non-empty `seedKey` through `ImageDeleteService.DeleteAsync`, and prints counts. A
  second run prints zeros. It never selects a document without `seedKey` (filter
  `{seedKey: {$exists:true, $nin:[null,""]}}`, the 006/008 safety rule).
- Its purge goes through the configured provider (log-only unless the owner configured it). The
  running app's output cache expires within 60 s; the command says so.
- `GalleryDoc.SeedKey` becomes `string?` with `[BsonIgnoreIfNull]` (FR-048).

**Rationale**: Clarification 1, FR-045–FR-048. Reusing the delete services is the "same rules as an
image delete" requirement, and keeps shared files of an uploaded twin.

**Constitution**: III.10 is a permission ("MAY be seeded ... only if"), and III.2 / IV.6 name seeded
content only as an allowed alternative source. Removing the seed uses no permission and breaks no
MUST, so no amendment is required. The design-record row for III.10 is now `n/a` (committed). A
later PATCH amendment could delete the dead permission text; noted for the owner, not a blocker.

## R-12 Tests and fixtures

**Decision**:
- `LibraryHost` (new fixture): `AdminHost` + `S3ProxyFixture` + signed-in owner + CSRF token +
  `RecordingEdgeProvider` registered through the `configure` hook, with helpers that insert
  upload-shaped images (no `seedKey`, files PUT to S3Proxy) and seed-shaped ones (for the removal
  command). Reuses `AdminHost.MintSession` / `SendAsync` (no reflection, no copies; 008 rows 53, 67).
- `LibraryBrowserFixture` (new, modelled on `UploadBrowserFixture`) for the Playwright tests:
  drag reorder, keyboard reorder, selection bar and Select all, publish confirmation, and a geometry
  check that per-tile controls sit below the cell inside the tile box.
- Tests that used the retired seeder as a fixture are rewritten to insert seed-shaped documents and
  call `Indexes.EnsureAsync` (stated requirement change; assertions kept). `SeederTests`,
  `SeedCommandTests`, `ManifestTests` are deleted with the code they test.
- [SAFETY] items: owner gate and CSRF on every new route; the publish refusal; the shared-file rule;
  the removal command's target guard and its never-touch-owner-content filter; no purge credential
  in responses or logs; public pages carry no owner markup or scripts.

## R-13 Timing instrumentation

**Decision**: Every new route gets `http.request` from the existing middleware. New `op.timing`
names and events are fixed in `contracts/log-events.md`. `evidence/p95.sh` reads SC-002, SC-003 and
SC-005 from a captured log and fails on an empty log or zero matching events (tested on synthetic
logs first).
