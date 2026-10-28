# Decisions Log: 001 Architecture and Stack Design

Each row records the decision, its rationale, and its pivot cost: how expensive it is to reverse.
The rows marked **OWNER** are defaults I chose because the question timed out twice. Please
review them first.

| # | Decision | Rationale | Pivot cost |
|---|---|---|---|
| 1 | **OWNER — D-masters**: masters are kept only until their batch is confirmed (needed for Restart), then deleted. | Keeping every master forever takes about 60–85 GB at 5,000 images, against R2's 10 GB free tier, so only about 700 images would fit. Local exports are already the recovery path (no-backup risk). **Conflicts with III.1** ("Masters MUST be stored privately"), so it needs an amendment before the first build plan. | Low: drop the delete step. But masters deleted in the meantime are gone, so they would have to be re-uploaded from your exports. |
| 2 | **OWNER — D-capacity**: the free tier holds about 4,000 images (derivatives at about 2.2 MB per image). The design states this ceiling instead of hiding it. | 6 widths × AVIF and WebP is about 2.2 MB per image (an estimate, to be measured). Past the ceiling, R2 bills about $0.15/month per extra 10 GB, which breaks VIII.1 literally. | Low: there are three ways out. Amend VIII to allow metered storage under $1/month. Drop WebP, since every current browser decodes AVIF (amend IV.6). Or drop the 2560 width (amend IV.6). |
| 3 | Upload in the browser with presigned PUT URLs straight to a private R2 bucket. | 20 MB masters never pass through the single-vCPU B1. | Medium: you would need a streaming upload endpoint. |
| 4 | Two R2 buckets, private staging and public derivatives. | R2 makes a whole bucket public, not a prefix, and staging must never be reachable. | Low before launch, high after: public URLs would change. |
| 5 | Content-hash derivative keys (`d/{sha256-16}.avif`). | Unguessable (II.3), immutable caching (IV.21), and identical files are stored once. | High after launch, because the URLs change. |
| 6 | Single-threaded jSquash WASM encoders in a Web Worker. | No COOP/COEP isolation is needed. A batch takes minutes, which is acceptable for one owner. | Low: add the isolation headers and switch to the multithreaded builds. |
| 7 | Server validation by hand-written AVIF and WebP box/chunk parsers, plus MetadataExtractor as a second check. | III.5 requires parsing the structure, never trusting the client. Two independent readers must both report no metadata. | Low. |
| 8 | sRGB output only; a P3 export is converted to sRGB. | jSquash has no ICC passthrough. Most web-bound exports are sRGB. | Medium: needs a custom encoder path. |
| 9 | ThumbHash placeholder rendered to a ≤32 px WebP data URI in the browser and inlined as a CSS background. | Public pages need no JavaScript (V.3), and the placeholder costs no extra request (IV.7). | Low. |
| 10 | Origin lock-down by the Cloudflare IP allowlist **plus** Authenticated Origin Pulls. | IP lists alone can be reached by anyone with their own Cloudflare account. AOP is free. | Low. |
| 11 | Edge HTML cache: Cache Rule with a 1-day edge TTL, bypassed when the owner session cookie is present, purged by URL. | URL purge is the fastest Free-plan purge, the app knows the exact URLs to purge, and the TTL bounds staleness after a failed purge. | Low. |
| 12 | Orphan cleanup: R2 lifecycle rule (staging older than 7 days) plus a best-effort app timer. | The backstop needs no running code. The timer covers explicit deletes that failed. | Low. |
| 13 | Interrupted = no heartbeat for 2 minutes, evaluated when the batch is read. | No timer is needed, and the result is deterministic. | Low. |
| 14 | Logs: JSON console logging to App Service file system logs, 7 days, 35 MB. | Free, per your clarification. | Low. |
| 15 | Image title defaults to the file name without its extension. | Required field, with no friction at upload. | Low. |
