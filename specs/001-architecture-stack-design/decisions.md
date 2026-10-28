# Decisions Log: 001 Architecture and Stack Design

Each row records the decision, its rationale, and its pivot cost: how expensive it is to reverse.
**OWNER** marks decisions taken in the owner's hosting interview.

| # | Decision | Rationale | Pivot cost |
|---|---|---|---|
| 1 | **OWNER**: Azure Blob Storage for the public derivatives (Hot tier). | Keeps everything with one vendor alongside the app. This replaces the constitution's earlier R2 default (amended in 3.0.0). | Medium before launch: change the storage abstraction's implementation. High after launch: every image would have to be copied. |
| 2 | **OWNER**: Originals kept permanently, private, in the same account, in the Cool tier. | Cheaper than Hot for files that are rarely read. The 30-day minimum stay is harmless because the files are kept forever. | Low: a lifecycle rule can retier them to Cold, which also removes the D-masters conflict with III.1. |
| 3 | **OWNER**: Google sign-in, with a 7-day session extended while in use. | Keeps the existing claim-once model. Signing in weekly is a reasonable balance. | Low: one setting. |
| 4 | **OWNER**: Application Insights for logs, 30-day retention, daily cap of 150 MB. | Searchable and charted. Expected usage is about 2% of the 5 GB free monthly allowance. The cap guarantees it stays free (VIII.4). | Low: change the exporter. |
| 5 | **OWNER**: Images reach visitors through Cloudflare Cloud Connector, with a Worker as fallback. | Cloudflare Free cannot override the Host header, which Azure Blob requires. Cloud Connector is free; the Worker is free up to 100k requests/day. | Low: swap Cloud Connector for the Worker. Image URLs don't change. |
| 6 | **OWNER**: Metered spend capped at $10/month with an alert at $7 (constitution VIII.1, 3.0.0). | The projected spend is about $1/month at 5,000 images, which leaves a large margin. | Low: requires an amendment. |
| 7 | Upload goes from the browser directly to the private container, using a per-blob user delegation SAS (write-only, 15 minutes). | 20 MB masters never pass through the single-vCPU B1. Managed identity means no storage keys exist anywhere. | Medium: you would need a streaming upload endpoint. |
| 8 | Two containers, `public` (anonymous read) and `private`. | Anonymous access is set per container, and staging must never be readable. The raw Blob URL of `public` is also reachable, which is acceptable: names are unguessable hashes, and the budget alert covers egress abuse. | Low before launch, high after. |
| 9 | Content-hash derivative names (`public/{sha256-16}.avif`). | Unguessable (II.3), immutable caching (IV.21), and identical files are stored once. | High after launch, because the URLs change. |
| 10 | Single-threaded jSquash WASM encoders in a Web Worker. | No COOP/COEP isolation is needed. A batch takes minutes, which is acceptable for one owner. | Low. |
| 11 | Server validation by hand-written AVIF and WebP box/chunk parsers, plus MetadataExtractor as a second check. | III.5 requires parsing the structure, never trusting the client. Two independent readers must both report no metadata. | Low. |
| 12 | sRGB output only; a P3 export is converted to sRGB. | jSquash has no ICC passthrough. | Medium. |
| 13 | ThumbHash placeholder rendered in the browser to a ≤32 px WebP data URI and inlined as a CSS background. | Public pages need no JavaScript (V.3), and the placeholder costs no extra request (IV.7). | Low. |
| 14 | Origin lock-down by the Cloudflare IP allowlist **plus** Authenticated Origin Pulls. | IP lists alone can be reached by anyone with their own Cloudflare account. AOP is free. | Low. |
| 15 | Edge HTML cache: 1-day TTL, bypassed when the owner session cookie is present, purged by URL. | URL purge is the fastest Free-plan purge, and the TTL bounds staleness after a failed purge. | Low. |
| 16 | Orphan cleanup: Blob lifecycle rule (staging older than 7 days) plus a best-effort app timer. | The backstop needs no running code, and lifecycle rules are free. | Low. |
| 17 | Interrupted = no heartbeat for 2 minutes, evaluated when the batch is read. | No timer is needed, and the result is deterministic. | Low. |
| 18 | Telemetry sampling is OFF. | The time-bound success criteria need every event, and the volume is tiny. | Low. |
| 19 | Image title defaults to the file name without its extension. | Required field, with no friction at upload. | Low. |

## Superseded

- The R2-era D-masters default ("delete masters after confirm") is superseded by row 2. Masters
  are now kept, so III.1 needs no amendment.
- The R2-era D-capacity ceiling (about 4,000 images free) is superseded by rows 1 and 6. The cost
  now scales at about $0.0002 per image per month, with no hard ceiling under the cap.
