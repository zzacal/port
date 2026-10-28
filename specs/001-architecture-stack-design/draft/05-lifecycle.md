## 5. Lifecycle

### L-01: Image succeeded in batch
**Records**: The BatchItem state changes from `processing` or `uploading` to `succeeded`; the `validated` field is set with width, height, placeholder, camera metadata, and derivative keys (data-model BatchItem; D-11).
**Storage**: Staged derivatives and the staged master are already in `private/staging/` from the prior upload step; no new blob operations occur at this point (data-model Blob layout; D-07).
**Pages and caches**: No public page or edge cache is affected. The library and all public pages are unchanged until L-05 (FR-017; D-11).
**Visitor sees**: Nothing changes. The image is not yet in the library, and no public page or listing references it (FR-011; FR-017).

### L-02: Image failed or interrupted in batch
**Records**: The BatchItem state changes to `failed` (with a reason string) or `interrupted`; `heartbeatAt` stops advancing for interrupted items, which are detected as interrupted when the batch is next read (data-model BatchItem; D-11; decisions #17).
**Storage**: Staged blobs already written remain in `private/staging/` until the owner chooses Restart or Delete, or until the 7-day lifecycle sweep removes them (data-model; D-11; decisions #16).
**Pages and caches**: No public page or edge cache is affected. The library and all public pages are unchanged until L-05 (FR-017).
**Visitor sees**: Nothing changes. The image is not in the library (FR-017; FR-011).

### L-03: Image restarted
**Records**: The BatchItem state returns to `queued`; a new `attemptId` is generated; staged blobs from the previous attempt are deleted if the master already reached the server (`masterStored` is checked) and the owner is not re-using it; if the master was not stored, `masterStored` remains false and the owner is asked to pick the same named file again (data-model BatchItem; FR-027; D-11).
**Storage**: Previous-attempt staged derivatives are deleted from `private/staging/`. If `masterStored` is true, the staged master is reused; otherwise nothing is in storage until the next upload (FR-027; D-11; decisions #16).
**Pages and caches**: No public page or edge cache is affected (FR-017).
**Visitor sees**: Nothing changes. The batch remains open and unconfirmed (FR-017; FR-011).

### L-04: Image deleted from batch
**Records**: The BatchItem is removed from the batch's `items` array; the batch's `updatedAt` is refreshed (data-model BatchItem; FR-027).
**Storage**: All staged blobs for that item — master and all derivatives — are deleted from `private/staging/` immediately; the 7-day lifecycle sweep acts as a backstop if the explicit delete fails (data-model; FR-030; D-11; decisions #16).
**Pages and caches**: No public page or edge cache is affected (FR-017).
**Visitor sees**: Nothing changes. The item was never in the library (FR-017; FR-011).

### L-05: Batch confirmed
**Records**: Each BatchItem's `itemId` becomes the `_id` of a new Image document; the Image documents are written to the `images` collection with all validated fields; the batch `state` changes to `confirmed`; every Gallery whose `items` array contains an id from this batch has its `card` recomputed; `site.homeCards` is refreshed for any affected gallery in `homeOrder` (data-model; D-11; D-13; FR-017).
**Storage**: For each item: (1) staged derivatives are copied from `private/staging/` to `public/` with `Cache-Control: public, max-age=31536000, immutable`; (2) the staged master is copied to `private/masters/{imageId}.jpg` in Cool tier; (3) all staged blobs are deleted. These are same-account server-side copies and the confirm step is idempotent per item (data-model Blob layout; D-07; decisions #1, #2, #8, #9).
**Pages and caches**: If any affected gallery is public, the home page and that gallery's page are purged by URL at the edge (up to 100 URLs per call, 800/s) and then warmed. The purge is best-effort; a failed purge self-heals within the 1-day HTML edge TTL (D-16; decisions #15; R-01).
**Visitor sees**: Images from the confirmed batch appear in any public gallery they belong to immediately after purge and warm; visitors hitting a cached page see the new images within at most 1 day if purge fails (D-16; FR-017).

### L-06: Batch discarded
**Records**: The batch `state` changes to `discarded` (data-model; FR-029).
**Storage**: All staged blobs for every item in the batch — masters and derivatives — are deleted from `private/staging/` immediately; the 7-day lifecycle policy is the backstop for any that were not cleaned up (data-model; FR-029; FR-030; D-11; decisions #16).
**Pages and caches**: No public page or edge cache is affected. No Image documents were ever created for this batch (FR-017).
**Visitor sees**: Nothing changes. The library is unaffected (FR-017; FR-011).

### L-07: Image added to gallery
**Records**: The image's `_id` is appended to the Gallery `items` array at the owner's chosen position; the gallery's `card` is recomputed if the image becomes the new cover (first item or pinned cover); `galleryIds` on the Image document gains this gallery's id; `site.homeCards` is updated if the gallery is in `homeOrder` and is public (data-model Gallery, Image; D-13; FR-009).
**Storage**: No blob operations. The image's derivatives are already in `public/` from L-05 (D-07).
**Pages and caches**: If the gallery is public, its page and the home page are purged and re-warmed at the edge (D-16; decisions #15; R-01).
**Visitor sees**: If the gallery is public, the image appears in the gallery and on the home card after purge and warm, within at most 1 day if purge fails (D-16; FR-011).

### L-08: Image removed from gallery
**Records**: The image's `_id` is removed from the Gallery `items` array; if the image was the `pinnedCover`, the pin is cleared and the cover falls back to the new first item (or null if the gallery is empty); the gallery `card` is recomputed; `galleryIds` on the Image document loses this gallery's id; `site.homeCards` is updated if the gallery is public and in `homeOrder` (data-model Gallery, Image; D-13; FR-009).
**Storage**: No blob operations. Derivatives remain in `public/` because the image may still be in other galleries or the library (D-07; FR-013).
**Pages and caches**: If the gallery is public, its page and the home page are purged and re-warmed (D-16; decisions #15; R-01).
**Visitor sees**: If the gallery was public, the image disappears from the gallery page and the home card after purge and warm, within at most 1 day if purge fails (D-16; FR-011).

### L-09: Image deleted
**Records**: The Image document is removed from `images`; the image's `_id` is removed from `items` in every Gallery document in `galleryIds`; for each affected gallery, `pinnedCover` is cleared if it matched, the `card` is recomputed, and `site.homeCards` is updated for public galleries in `homeOrder` (data-model Image, Gallery; D-13; FR-013).
**Storage**: Hard delete: `private/masters/{imageId}.jpg` is deleted from the Cool container, and every `public/{hash}.{avif|webp}` derivative is deleted from the Hot container. These are immediate deletions triggered by the app (data-model; D-07; FR-013; decisions #1, #2).
**Pages and caches**: An immediate edge purge is issued for every derivative URL and for every public page that referenced the image (gallery pages and the home page for affected public galleries). The purge is best-effort at up to 100 URLs per call and 800/s (D-16; decisions #15; R-01; FR-013). Copies already held in visitor browser caches can persist for up to the immutable `max-age=31536000` lifetime because `immutable` blobs carry no `must-revalidate`; this is an accepted risk (FR-013; decisions #9; spec Assumptions).
**Visitor sees**: The image ceases to appear on public gallery pages and the home page after purge and warm. Visitors who have the derivative URLs cached in their browser may continue to load them for up to 1 year; this caveat is an accepted risk documented in the risk register (FR-013; spec Assumptions).

### L-10: Gallery created
**Records**: A new Gallery document is inserted with `visibility: "private"`, an empty `items` array, no `pinnedCover`, and a generated 128-bit base32 `_id` and slug. The gallery id is appended to the end of `site.homeOrder`, which holds every gallery so a private gallery keeps its position when published; `site.homeCards` is unchanged because the gallery is private (data-model Gallery, Owner; D-13; D-14; FR-009).
**Storage**: No blob operations (D-07).
**Pages and caches**: No public page or edge cache is affected. The gallery starts private (II.5; D-14).
**Visitor sees**: Nothing. The gallery is private and does not appear in any public listing, feed, or sitemap (FR-011; D-14).

### L-11: Gallery made public
**Records**: The Gallery `visibility` changes to `"public"`; `site.homeCards` gains the gallery's `card` at the position given by `site.homeOrder` (data-model Gallery, Owner; D-14; FR-009).
**Storage**: No blob operations. Derivatives for all member images are already in `public/` (D-07).
**Pages and caches**: The gallery page and the home page are purged and re-warmed at the edge (D-16; decisions #15; R-01). The gallery page changes from `noindex` to indexable. A failed purge is bounded by the 1-day HTML edge TTL (D-16; decisions #15).
**Visitor sees**: The gallery and its images appear on the home page and the gallery page after purge and warm, within at most 1 day if purge fails (D-16; FR-011).

### L-12: Gallery made private
**Records**: The Gallery `visibility` changes to `"private"`; its card is removed from `site.homeCards`; it keeps its position in `site.homeOrder` so republishing restores it in place (data-model Gallery, Owner; D-14; FR-009).
**Storage**: No blob operations. Derivatives remain in `public/` because the image addresses are permanent and unlisted (D-07; FR-012).
**Pages and caches**: The gallery page and the home page are purged by URL at the edge immediately. A failed purge means the gallery page and its home-card entry remain visible at the edge for up to the 1-day HTML TTL (D-16; decisions #15; R-01; II.2). The gallery page is re-served with `noindex` on the next origin hit.
**Visitor sees**: The gallery ceases to appear on the home page and its page is marked non-indexable after purge; with a failed purge, up-to-1-day edge staleness is the bound (II.2; D-16; FR-011).

### L-13: Gallery renamed
**Records**: The Gallery `title` is updated and a new `slug` is generated from the new title; the old slug is superseded. The `_id` is unchanged (data-model Gallery; D-14; FR-009).
**Storage**: No blob operations (D-07).
**Pages and caches**: The gallery page at the new URL (`/g/{id}/{new-slug}`) is served immediately; requests to the old URL (`/g/{id}/{old-slug}`) are served with a 301 redirect to the new URL by the app (D-14; decisions #15). Both the old and new gallery page URLs are purged and the new URL is re-warmed (D-16).
**Visitor sees**: Previously shared links (`/g/{id}/{old-slug}`) continue to resolve via 301 to the new URL. No content is lost (D-14; II.4; FR-009).

### L-14: Gallery reordered
**Records**: `site.homeOrder` is updated to reflect the new gallery positions (public and private alike); `site.homeCards` is recomputed in the new order for public galleries (data-model Owner; D-13; FR-009).
**Storage**: No blob operations (D-07).
**Pages and caches**: The home page is purged and re-warmed at the edge (D-16; decisions #15; R-01).
**Visitor sees**: The home page shows galleries in the new order after purge and warm, within at most 1 day if purge fails (D-16).

### L-15: Images reordered
**Records**: The Gallery `items` array is updated to reflect the new image order; if the gallery has no `pinnedCover`, the `card.coverImageId` changes to the new first item, and the `card` is recomputed; `site.homeCards` is updated for affected public galleries in `homeOrder` (data-model Gallery; D-13; FR-009).
**Storage**: No blob operations (D-07).
**Pages and caches**: If the gallery is public, the gallery page and the home page are purged and re-warmed at the edge (D-16; decisions #15; R-01).
**Visitor sees**: If the gallery is public, the gallery page shows images in the new order and the home card may show a new cover after purge and warm, within at most 1 day if purge fails (D-16; FR-009).

### L-16: Gallery cover changed
**Records**: The Gallery `pinnedCover` is set to the chosen image id (which must be an element of `items`); the `card` is recomputed with the new cover image's placeholder and derivatives; `site.homeCards` is updated if the gallery is public and in `homeOrder` (data-model Gallery; D-13; FR-009).
**Storage**: No blob operations. The new cover image's derivatives are already in `public/` (D-07).
**Pages and caches**: If the gallery is public, the gallery page and the home page are purged and re-warmed at the edge (D-16; decisions #15; R-01).
**Visitor sees**: If the gallery is public, the home card and the gallery page show the new cover image after purge and warm, within at most 1 day if purge fails (D-16; FR-009).

### L-17: Gallery deleted
**Records**: The Gallery document is removed from `galleries`; the gallery's `_id` is removed from `site.homeOrder`; the corresponding card is removed from `site.homeCards`; `galleryIds` on every member Image document is updated to remove this gallery's id (data-model Gallery, Image, Owner; D-13; D-14; FR-013).
**Storage**: No blob operations. Deleting a gallery MUST NOT delete the library images; their masters and derivatives remain in storage (FR-013; D-07).
**Pages and caches**: The gallery page and the home page are purged and re-warmed at the edge (D-16; decisions #15; R-01). The gallery page ceases to be served after purge.
**Visitor sees**: The gallery disappears from the home page and the gallery URL returns 404 after purge and warm, within at most 1 day if purge fails (D-16; FR-013). Member images remain in the library and in any other galleries they belong to.
