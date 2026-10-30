## 6. Edge Cases

### E-01: Claim race — two sign-ins on an unclaimed site
**Outcome**: Exactly one caller wins. The other receives no session and the site remains owned by the winner (Constitution I.1).
**Mechanism**: D-04 executes a single conditional update `{claimedSubject: null}` → set; MongoDB's atomic matched-count of 1 means the caller won, 0 means it lost. No second update is ever issued (data-model Owner).

### E-02: Stranger claims the site before the owner
**Outcome**: The stranger becomes owner. Detection is the claim log event (claimedAt, claimedIp). Recovery is an out-of-band datastore reset (Constitution I.4).
**Mechanism**: D-04 writes claimedSubject, claimedAt, and claimedIp atomically. No web route can overwrite a set claimedSubject (data-model Owner). The owner's subsequent sign-in fails the conditional update and receives no session.

### E-03: Tab closed or connection lost mid-batch
**Outcome**: When the owner returns, the open batch is still there. Images that had succeeded remain succeeded; images that were processing or uploading for more than two minutes are shown as interrupted with Restart and Delete. Nothing has entered the library.
**Mechanism**: D-11 persists the batch on the server (FR-017). D-11 (decisions #17) evaluates the heartbeat field: no heartbeat for 2 minutes transitions the item to interrupted when the batch is next read. L-02 captures the failed-or-interrupted state; L-05 has not fired, so no image is in the library.

### E-04: Restart when master never reached the server
**Outcome**: The owner is prompted to pick that same named file again. If they pick a different file, it is treated as a new image with a new attemptId.
**Mechanism**: D-11 stores masterStored and fileName on each BatchItem (data-model BatchItem). On Restart, D-11 checks masterStored; if false, the UI names the original fileName and requires re-selection. A new attemptId is issued (FR-027); any staged files from the prior attempt are deleted.

### E-05: Browser out of memory or format encode failure
**Outcome**: That image is marked failed with the reason; it shows Restart and Delete. The rest of the batch continues unaffected.
**Mechanism**: D-09 catches per-image errors in the Web Worker and posts a failed state with the reason string to the main thread (FR-026). D-11 records the reason on the BatchItem; other items continue in their own Worker tasks.

### E-06: Derivative reaches server with embedded metadata, wrong dimensions, or disallowed format
**Outcome**: The server rejects the whole image (FR-016). Every staged file for that image is discarded, and the item shows as failed with the rejection reason and offers Restart and Delete.
**Mechanism**: D-10 runs hand-written AVIF and WebP structural parsers plus MetadataExtractor; any metadata present, dimension mismatch, or disallowed format causes rejection. Staged objects for the item are deleted. The BatchItem transitions to failed with the reason (data-model item state machine).

### E-07: Owner tries to confirm a batch with failed or interrupted images
**Outcome**: Confirmation is refused — by the page and by the server — and the page states how many images still need Restart or Delete.
**Mechanism**: D-11 enforces FR-028: the Confirm action is disabled client-side until every remaining item is succeeded, and the server independently rejects a confirm request if any item is not in the succeeded state.

### E-08: Owner tries to start a new upload while a batch is unconfirmed
**Outcome**: The owner is redirected to the existing open batch, not given a new one.
**Mechanism**: D-11 enforces FR-029 via the unique partial index on `{state:"open"}` in the batches collection (data-model Upload batch). The server returns the open batch's URL; no second batch document can be inserted while one is open.

### E-09: Selected file is not a JPEG, is over 20 MB, or is a corrupt JPEG
**Outcome**: The file is rejected with the reason before it joins the batch; nothing is stored and no upload begins.
**Mechanism**: D-08 (FR-014) refuses the file in the browser before any processing starts, then enforces the same rules server-side. The rejected file never appears as a BatchItem; the batch and storage are unaffected.

### E-10: JPEG carries EXIF orientation, GPS, maker notes, or embedded thumbnail
**Outcome**: Orientation is applied before encoding. Nothing beyond the camera-allowlist fields survives in any derivative or in the stored metadata.
**Mechanism**: D-09 reads and applies EXIF orientation in the Web Worker before the resize-and-encode ladder; the encoded derivatives carry no EXIF (Constitution III). D-10 runs structural parsers plus MetadataExtractor on every uploaded file; any metadata outside the allowlist causes rejection (Constitution III.5–III.8). The server extracts allowlisted camera fields from the master by ranged download without decoding pixels (D-10, decisions #11).

### E-11: Source image is smaller than the smallest ladder width
**Outcome**: No upscaling occurs; only widths at or below the source width are produced.
**Mechanism**: D-09 checks each ladder step against the source dimensions and skips any width that would require upscaling (Constitution IV.6; decisions #10). The derivatives array on the Image document contains only the widths that were actually generated (data-model Image).

### E-12: Edge purge fails after a visibility change
**Outcome**: Stale HTML is served for at most one day. After the TTL expires the edge re-fetches from the origin.
**Mechanism**: D-16 sets a 1-day edge TTL on HTML pages (decisions #15). A failed purge leaves at most one day of staleness before natural expiry (Constitution II.2, IV.18). L-11 (Gallery made public) and L-12 (Gallery made private) trigger purge-and-warm; even when purge fails the bounded TTL is the safety net.

### E-13: Datastore is lost
**Outcome**: All gallery structure, ordering, image titles, captions, gallery descriptions, gallery visibility, and the claim record are permanently lost (accepted risk). The owner rebuilds from local exports by re-uploading images and re-creating galleries.
**Mechanism**: D-05 (Atlas Free, no backup mechanism) and FR-019 record this as an accepted risk. Image masters remain in R2 `private/masters/` (D-07, decisions #2); derivatives remain in `public/`. Nothing in the datastore is recoverable from the stack (data-model Owner, Image, Gallery).

### E-14: Cover image removed from gallery or deleted
**Outcome**: If it was the pinned cover, the pin is cleared. The cover falls back to the first image in the gallery's order, or to an empty card when no images remain. The denormalized GalleryCard and the home page are refreshed immediately.
**Mechanism**: D-13 defines the cover rule (pinnedCover cleared if the image leaves items; fallback to items[0] or null; data-model Gallery). L-08 (Image removed from gallery) and L-09 (Image deleted) recompute the Gallery card and trigger D-16 purge of the home page and gallery page (Constitution IV.9).

### E-15: Gallery reordered with no pinned cover and the first image changes
**Outcome**: The cover changes to the new first image. The home card is refreshed.
**Mechanism**: D-13 derives the cover from items[0] when pinnedCover is null (data-model Gallery). L-14 (Gallery reordered) recomputes the GalleryCard and triggers a D-16 purge of the home page and the gallery page.

### E-16: Gallery has no images
**Outcome**: The home card renders empty (no cover, no dimensions) and the gallery page shows no images. Neither crashes nor leaves broken markup.
**Mechanism**: D-13 specifies that when items is empty the cover is null and the GalleryCard's cover fields are null (data-model GalleryCard). D-15 server-renders the page with a null-safe cover path and no `<picture>` element when coverImageId is absent.

### E-17: Owner deletes an image by mistake
**Outcome**: There is no trash and no undo through the site. The image is permanently gone. Recovery is re-uploading from local exports. Its gallery memberships, positions, title, and caption are lost.
**Mechanism**: D-13 and FR-019 record this as an accepted risk (no backups). L-09 (Image deleted) hard-deletes the master from `private/masters/`, all derivatives from `public/`, and all gallery memberships, then triggers D-16 edge purge of derivative URLs and every public page that referenced the image (FR-013).

### E-18: Edge purge fails after an image is deleted
**Outcome**: The edge serves cached derivatives until their immutable-cache TTL expires (up to one year). Storage no longer has the files, so once the edge cache entry expires the files are gone everywhere.
**Mechanism**: D-07 sets `Cache-Control: public, max-age=31536000, immutable` on all `public/` derivatives (data-model Storage layout, Constitution IV.21). L-09 triggers D-16 URL purge; if the purge fails, the derivative is unreachable from storage but may remain in the edge cache until the TTL expires. This bound is stated as the browser-cache persistence caveat in the accepted-risk register (FR-013).

### E-19: Free-tier limit reached, ingestion cap hit, or $7 alert fires
**Outcome**: Atlas at 0.5 GB stops accepting writes, returning errors to the app; the Application Insights 150 MB daily cap stops ingesting logs for the rest of that day; the Azure budget alert at $7 sends a notification to the owner and no automatic action halts the service.
**Mechanism**: D-05 (Atlas Free, 0.5 GB limit) — writes fail with a quota error when the limit is reached; D-12 (decisions #4) — the 150 MB daily cap is configured in Application Insights (D-18, Constitution VIII.4); D-18 (decisions #6, Constitution VIII.1) — the Azure budget notifies at $7 and at $10. An Azure budget does not stop resources, so the $10 cap is enforced by the owner acting on the alerts, and exceeding it requires an amendment (spec FR-020, FR-006).
