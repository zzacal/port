## 9. Accepted-Risk Register

### R-01: No claim token — unclaimed window on first deploy

**Failure**: From deploy until the owner's first sign-in at `/admin`, anyone with a Google account who reaches it first becomes owner (spec Assumptions).
**Detection**: The claim log event (Constitution I.5) records the identity, client IP, and timestamp; the real owner's subsequent sign-in is refused because the site is already claimed.
**Recovery**: Out-of-band datastore reset by the operator (Constitution I.4); no web-accessible reset path exists.
**Reversal cost**: Low — a claim token checked at claim time can be added later without changing the data model (spec Assumptions).

### R-02: No backups — datastore and master loss is permanent

**Failure**: Losing the datastore loses every gallery, its order, image titles and captions, gallery descriptions, visibility, and the claim; losing the storage account loses the masters (spec Assumptions).
**Detection**: Site content is absent when the owner accesses it; Azure service health notifications indicate resource outage.
**Recovery**: The owner's local exports are the only recovery path; rebuilding galleries is manual (spec Assumptions).
**Reversal cost**: Low — a scheduled datastore export into private storage can be added later for cents a month (spec Assumptions).

### R-03: Deletion not instant everywhere — browser cache persistence

**Failure**: A deleted image's derivatives can persist in visitors' browser caches for up to the immutable-cache lifetime, because derivative responses carry `Cache-Control: max-age=31536000, immutable` (spec Assumptions; FR-013; Constitution IV.21).
**Detection**: Undetectable from the server; a visitor with a cached copy can still view the derivative until the cache entry expires naturally.
**Recovery**: Hard delete removes the master and all derivatives from storage and purges the edge immediately; browser-cached copies have no server-side removal path (spec Assumptions; FR-013).
**Reversal cost**: Medium — removing immutable-cache headers from derivatives would require amending Constitution IV.21; the practical bound on staleness is one cache lifetime (1 year for derivatives).

### R-04: Unlisted is not secret — private derivative addresses are reachable

**Failure**: Anyone holding a derivative address of a library-only or private-gallery image can view it on `img.<domain>`, because the `public` bucket serves every object it holds (spec Assumptions; FR-012; D-19).
**Detection**: No server-side record of direct URL access exists; exposure of a derivative address cannot be detected retroactively.
**Recovery**: No recovery path within the system; derivative names are unguessable content hashes (decisions #9). Since constitution 4.0.0 there is no second address: the r2.dev URL is disabled and the R2 S3 endpoint needs a credential (III.9), so the only way in is `img.<domain>`, which the zone's security rules cover.
**Reversal cost**: High after launch — derivative addresses are content-hash URLs embedded in the edge cache and in any previously shared links; changing the addressing scheme requires re-generating all derivatives with new names (decisions #9).

### R-05: 30-day log retention — audit events and timing evidence expire

**Failure**: Timing evidence and audit events older than 30 days are gone, and the claim event in particular must be checked within that window; when the daily ingestion cap is hit, logs stop being ingested for the rest of that day (spec Assumptions; research R-09).
**Detection**: Application Insights daily cap metric in the Azure portal shows when ingestion has stopped for the day; log gaps appear as query results with no events in that interval (research R-09).
**Recovery**: No recovery of expired or dropped logs; the 150 MB daily cap and 5 GB monthly free allowance keep normal-traffic days from hitting the cap, so drops are rare (research R-09; decisions #4).
**Reversal cost**: Low — retention can be extended up to 90 days at no extra cost; the daily cap can be raised; the exporter can be switched to a longer-retention service at any time (decisions #4).

### R-06: Stored R2 credential — masters readable if it leaks

**Failure**: The app holds an R2 token with Object Read and Write on both buckets. Anyone who obtains it can read every master and staged upload, and can overwrite or delete objects, until the token is rotated (Constitution 4.0.0, III.9; decisions #34).
**Detection**: Unexpected Class B operations or storage changes on the Cloudflare R2 metrics page; masters or derivatives missing or altered.
**Recovery**: Roll the token in the Cloudflare dashboard and update the app's configuration; derivatives can be regenerated from masters only by re-uploading. The token cannot reach account settings, the zone, or other buckets (III.9).
**Reversal cost**: Low — R2 temporary credentials or a Worker binding can replace the stored token later without changing object keys or URLs.

### R-07: client-generated derivatives — metadata leak if admin page has a bug

**Failure**: A bug in the admin page could produce a derivative that leaks metadata (location, camera data, or other EXIF or XMP content) before it is uploaded (spec Assumptions; FR-016).
**Detection**: Server-side validation (FR-016) rejects any derivative whose AVIF or WebP structure contains an Exif or XMP item, or whose MetadataExtractor report is non-empty; the image is reported to the owner as failed with the reason (research R-06; decisions #11).
**Recovery**: The failed image shows Restart and Delete; no metadata-leaking derivative reaches the library because the server discards it before acceptance (FR-026; decisions #11).
**Reversal cost**: Low — the server validation is already the authoritative check; tightening the parser rules requires only a code change with no data migration (decisions #11).
