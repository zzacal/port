# Research: Architecture and Stack Design (Phase 0)

The evidence lives in `research-notes/`: six area files, each claim with a fetched quote and a
`Fetch: OK` line, plus a `## Gaps` section. This file consolidates the decisions that
`docs/design.md` will record. Area files are cited as [cf] cloudflare.md, [st]
storage-datastore.md, [br] browser-encoding.md, [az] azure-dotnet.md, and [wp] web-platform.md, and [asl] azure-storage-logs.md.

## R-01: Edge on Cloudflare Free covers every Principle IV delivery rule (HTML and images)

- **Decision**: Use Cloudflare Free with the following features:
  - Smart Tiered Cache.
  - Cache Rules (Free allows 10): cache anonymous HTML with an edge TTL override of 1 day, and
    bypass the cache when the owner session cookie is present.
  - Early Hints generated from the origin's `Link: rel=preload` headers.
  - Purge by URL for content changes. The limits are 800 URLs/s and 100 URLs per call.
  - Origin CA certificate (15-year) in Full (strict) mode.
- **Rationale**:
  - Every one of these is listed as available on Free [cf Q1–Q5].
  - Tag and prefix purge are now also on Free, but rate-limited to 5 requests/min [cf Q1]. URL
    purge is enough, because the app always knows the exact affected URLs.
  - The bounded edge TTL satisfies IV.18's "a failed purge self-heals".
- **Alternatives**:
  - Cache Reserve: paid only [cf Q8].
  - A Worker in front of the origin: the Free plan has 100k requests/day and 10 ms CPU [cf Q7].
    It is unnecessary because Cache Rules do the cookie bypass.

## R-02: Storage budget on Azure Blob, with masters kept (constitution 3.0.0)

> **AMENDED by constitution 4.0.0 (2026-10-30)**: image storage moved from Azure Blob to Cloudflare R2. This section is kept as the record of the 3.0.0 design; the current decision is in docs/design.md D-07, D-08, D-19 and research-notes/storage-datastore.md Q1–Q6, Q11, Q12.

- **Facts** [asl Q1, Q2, Q6, Q7]:
  - There is no free storage allowance. Prices are per GB-month on LRS in East US. These are
    indicative, because the pricing page renders with JavaScript; verify them in the calculator:
    - Hot: about $0.0184
    - Cool: about $0.010 (minimum stay 30 days)
    - Cold: about $0.0045
  - Write operations cost $0.065 (Hot) and $0.13 (Cool) per 10,000.
  - The first 100 GB/month of internet egress is free.
  - Lifecycle policies and deletes are free.
  - A same-account Copy Blob is billed as a write on the destination.
- **Estimate** at 5,000 images. These are design estimates, to be verified from logged byte
  counts.
  - Derivatives are about 2.2 MB per image, about 11 GB in Hot, so about $0.20/month.
  - Masters are about 8–15 MB per image, about 60–75 GB in Cool, so about $0.60–0.75/month.
  - Writes: 13 PUTs per image to staging (master in Cool, derivatives in Hot), plus 12 copies to
    public on confirm, about 25 writes per image. At 5,000 images uploaded over a year, that is
    about 125k writes, about $1 in total, or about $0.10/month amortized.
  - Egress: origin pulls from Cloudflare only on edge cache misses. At low-thousands of page
    views, that is well under 100 GB, so it is free.
  - Application Insights: under 5 GB/month, so it is free (R-09).
  - **Projected metered total: about $1/month**, against the $7 alert and the $10 cap (VIII.1).
- **Decision**:
  - One GPv2 LRS account in the App Service region, with two containers:
    - `public`: Hot, anonymous blob-level read.
    - `private`: Cool, no anonymous access. It holds `staging/` and `masters/`.
  - On confirm, the master is copied from `staging/` to `masters/{imageId}.jpg`, which stays in
    Cool permanently.
  - An Azure budget is set at $10, with an alert at $7 (VIII.1).
- **Cool minimum-stay charge**: a master deleted within 30 days is billed for the full 30 days,
  which is at most about $0.0015 per image. That is negligible and recorded.
- **Capacity**: there is no hard ceiling. At $10/month the budget would cover roughly 50,000+
  images, so the D-capacity concern from the R2 design is gone.
## R-03: Datastore on Atlas Free fits comfortably

- **Facts**:
  - The limits are 0.5 GB storage, 500 connections, 100 operations/s, and 10 GB in or out per 7
    days [st Q7].
  - Transactions and unique indexes are supported [st Q9].
  - The cluster pauses only after 30 days with zero connections [st Q10]. An Always On app holds
    a connection, so it never pauses.
  - No backup is included. A paid add-on exists at $3/month; it is not used, per the
    clarification [st Q8].
- **Decision**: One cluster in a Free-eligible Azure region nearest the App Service region. The
  exact region list was not captured [st Gaps]; it is confirmed at provisioning time.
- **Sizing**: An image document is about 3 KB including a placeholder of at most 1 KB, so 5,000
  images is about 15 MB, which is 3% of the 0.5 GB quota.

## R-04: Image delivery via Cloudflare Cloud Connector (Worker fallback)

> **AMENDED by constitution 4.0.0 (2026-10-30)**: image storage moved from Azure Blob to Cloudflare R2. This section is kept as the record of the 3.0.0 design; the current decision is in docs/design.md D-07, D-08, D-19 and research-notes/storage-datastore.md Q1–Q6, Q11, Q12.

- **Facts**:
  - Azure Blob rejects requests whose Host header is not its own endpoint or a registered custom
    domain, and Azure custom domains don't support HTTPS without Azure CDN [asl Q4].
  - Cloudflare's Origin Rules Host-header override is Enterprise-only [asl Q4].
  - Cloud Connector is free on all plans, in beta, and supports `<account>.blob.core.windows.net`
    origins [asl Q4].
- **Decision**:
  - `img.<domain>` is a proxied Cloudflare hostname. A Cloud Connector rule routes it to
    `https://<account>.blob.core.windows.net/public/`.
  - A Cache Rule sets the edge TTL to 1 year and respects the origin's
    `Cache-Control: public, max-age=31536000, immutable`, which is set as blob metadata at
    upload (IV.21).
  - Tiered Cache applies [cf Q2].
- **Fallback (owner-approved)**: if Cloud Connector is not offered on the zone, a Worker (about
  20 lines) proxies `img.<domain>/*` to the blob URL. The Workers free plan allows 100k
  requests/day [cf Q7]. Every request on a Worker route invokes the Worker, cache hits included,
  so the limit covers all image requests. At about 20 images per page view, that is about 5,000
  page views/day, far above the expected traffic. The Worker MUST use the Cache API so that
  cache hits do not pull from Blob.
- **Public container exposure**: anonymous read must be enabled for the edge to fetch, so the
  raw `blob.core.windows.net` URL is also publicly readable. This is acceptable: derivative
  names are unguessable content hashes (II.3), derivatives are "unlisted, not secret" (FR-012),
  and egress beyond 100 GB is the only exposure, which the budget alert covers. The app never
  emits raw blob URLs.
- **Alternatives**:
  - Azure Front Door: about $35/month or more, which breaks the cap.
  - Direct Blob URLs: no edge cache, and the egress is billed.
  - Making the public container private and signing every URL: this breaks immutable caching.

## R-05: Upload path is direct from the browser to Blob with SAS

> **AMENDED by constitution 4.0.0 (2026-10-30)**: image storage moved from Azure Blob to Cloudflare R2. This section is kept as the record of the 3.0.0 design; the current decision is in docs/design.md D-07, D-08, D-19 and research-notes/storage-datastore.md Q1–Q6, Q11, Q12.

- **Decision**:
  1. The server issues a **user delegation SAS** per blob. It is signed through managed identity
     (no account key exists in config), is write-only, scoped to
     `private/staging/{batch}/{item}/{attempt}/{name}`, and expires after 15 minutes [asl Q5,
     Q10].
  2. The browser PUTs each file with a single Put Blob request. 20 MB is far below the 5,000 MiB
     single-request limit [asl Q5].
  3. Blob-service CORS allows only the admin origin, for PUT and OPTIONS [asl Q5].
  4. The browser then calls `complete`, and the server validates the staged files.
- **Staging tiers**: derivatives are staged in the `private` container. Each upload sets the
  per-blob tier: Hot for derivatives, so they can be copied immediately without a Cool minimum
  charge, and Cool for the master.
- **Rationale**: 20 MB masters never pass through the B1. The managed identity removes stored
  storage keys.
- **Backstop sweep**: a lifecycle policy deletes `private/staging/` blobs 7 days after creation.
  Lifecycle runs daily and is free [asl Q6] (FR-030).
- **Alternatives**: streaming uploads through the app was rejected because of the origin's CPU
  and memory.

## R-06: Server-side validation without decoding pixels (III.5–III.8)

- **Decision**: For each staged derivative, the server reads it with a ranged blob download [asl Q7] and
  parses the container structure only:
  - **AVIF** (ISOBMFF): `ftyp` must be `avif`. Dimensions come from `ispe`. The file must contain
    no `infe` item of type `Exif`, and no `mime` item whose content type is `application/rdf+xml`
    (XMP). A `colr` box is allowed only with nclx, or with an sRGB ICC profile [az Q6].
  - **WebP** (RIFF): `RIFF…WEBP` with `VP8`, `VP8L`, or `VP8X`. If `VP8X` is present, the EXIF
    and XMP flags must be clear and the file must contain no `EXIF` or `XMP ` chunk. Dimensions
    come from the bitstream or the canvas header [az Q6].
- **Master**: The server reads the EXIF allowlist with MetadataExtractor, which is Apache-2.0
  and parses headers without decoding [az Q5]. It reads only the first 256 KB via a ranged download.
  A JPEG with its EXIF segment beyond 256 KB is read in full; it is still never decoded.
- **Library**: Hand-written parsers of about 200 lines each cover AVIF and WebP, because only
  "find boxes or chunks" is needed. MetadataExtractor also reads AVIF and WebP, so the
  validator uses it as a second check (it must report zero Exif and XMP directories).
- **Test (constitution III rationale)**: A fixture AVIF and a fixture WebP, each with GPS
  injected, must be rejected.

## R-07: In-browser derivative pipeline

- **Decision**: Run in a dedicated Web Worker:
  1. Decode with `createImageBitmap(file, {imageOrientation:'from-image'})`. Orientation is
     applied, and this is Baseline [br Q1].
  2. Resize each width with `createImageBitmap(bitmap, {resizeWidth, resizeQuality:'high'})`,
     never creating a full-size canvas [br Q2, Q5].
  3. Read pixels through an OffscreenCanvas sized to the target width.
  4. Encode with `@jsquash/avif` and `@jsquash/webp`. These are Apache-2.0; the AVIF build is
     about 1.1 MB gzipped [br Q4].
  5. Use the single-threaded builds. Multithreaded builds need COOP/COEP cross-origin
     isolation, which is not worth the risk on an admin page. The page is served same-origin,
     so this can be revisited later.
- **WebP**: Encode it in WASM too, because Safari cannot encode WebP from a canvas [br Q3].
- **Metadata**: Canvas and ImageData carry no EXIF, XMP, or ICC, so the outputs are clean by
  construction [br Q3, Q7]. Server validation is still authoritative.
- **Color**: Output is sRGB. The design assumes sRGB exports; a P3 export is converted to sRGB
  and loses wide gamut [br Q7]. This is recorded as a decision with low pivot cost.
- **Placeholder**:
  - The browser computes a ThumbHash from the 320 px bitmap and also renders it to a ≤32 px
    WebP data URI, stored on the image record.
  - The dominant color is the ThumbHash's average color.
  - The public page inlines the data URI as a CSS background, so no JavaScript is needed
    [br Q6].
- **Timings**: Decode, each encode, and the total are posted with `complete` and logged
  server-side as `op.timing` events (IV.2, FR-024).
- **Memory**: The worker processes one image at a time and closes each bitmap after use. A 24 MP
  decode is about 96 MB, so desktop browsers are fine. iOS Safari is limited to 16–67 MP of
  canvas area [br Q2], so mobile is "best effort, refused per file".

## R-08: Origin on App Service B1

- **Facts**: The plan is 1 vCPU, 1.75 GB RAM, and about $13/month on Linux (East US; to be
  re-checked in the pricing calculator) [az Q1, Gaps]. Access restrictions allow up to 512 IP
  rules, and filtering is by IP only [az Q2].
- **Decision**:
  - Allowlist Cloudflare's published IPv4 and IPv6 ranges in the access restrictions.
  - Also enable Authenticated Origin Pulls, which is mTLS and free on all plans [cf Q6]. The IP
    lists are public, so an attacker who sets up their own Cloudflare account could still reach
    an IP-only origin. AOP closes that gap.
  - The forwarded-headers middleware reads `CF-Connecting-IP` and trusts only Cloudflare's
    ranges as known networks [az Q7].
- **Background work**: One `BackgroundService` timer runs best-effort orphan cleanup for
  explicit deletes that failed. Always On keeps it alive, and restarts are tolerated because the
  sweep is idempotent [az Q4].
- **Identity to Azure**: the system-assigned managed identity holds the Storage Blob Data
  Contributor role on the account. The app uses `DefaultAzureCredential` [asl Q10], so no
  storage key or connection string is stored anywhere.

## R-09: Observability in Application Insights

- **Decision**:
  - Use workspace-based Application Insights, exporting through the Azure Monitor OpenTelemetry
    distro. New projects use `Microsoft.OpenTelemetry` [asl Q9].
  - Requests are auto-collected and become the `http.request` events.
  - `op.timing` events are `ILogger` structured logs, so their fields land in `customDimensions`
    and can be queried with KQL [asl Q9].
  - Sampling is explicitly OFF. It is not enabled by default [asl Q9], and the time-bound success
    criteria need every event.
  - Retention is 30 days, inside the 90 days included free [asl Q8].
  - A daily cap of 150 MB keeps usage inside the 5 GB/month free ingestion (about 4.5 GB/month
    worst case) (VIII.4).
  - `Server-Timing` is sent on public pages [wp Q7].
- **Evidence procedure**: a saved KQL query per success criterion (for example the p95 of
  `requests | where name startswith "GET /g/"`) is run with `az monitor app-insights query`.
  Its output is tee'd into `<feature>/evidence/`. The queries are committed as files.
- **Volume check**: at low-thousands of page views, most are served from the edge, so origin
  requests are maybe 20–50k/month, at about 1 KB each including dependencies. That is under
  100 MB/month, 2% of the free 5 GB.
- **When the cap is hit**: ingestion stops until the daily reset. Recorded as an accepted risk.
- **Alternatives**:
  - App Service file logs: free, but not searchable, and the owner chose Application Insights.
  - An external log service: adds a vendor.
## R-10: Perceived performance on the public site

- **Speculation Rules**: Use an inline rule set with `prerender` at `eagerness: moderate` on
  gallery and image links. Chromium suppresses it under Save-Data and Battery Saver [wp Q1],
  which satisfies IV.14 for IV.11.
- **Thumbnail warm-up (IV.12)**: About 20 lines of inline JS add a `<link rel=preload>` for the
  destination image's `srcset` on `pointerenter`/`touchstart`. It is skipped when
  `navigator.connection?.saveData` is true. This is progressive enhancement, and the page works
  without JS.
- **IV.13**: The solo view preloads the previous and next images after the `load` event, using
  the same Save-Data guard.
- **View transitions**: Use `@view-transition {navigation:auto}` with `view-transition-name:
  img-{id}` on both pages. This works in Chrome 126+ and Safari 18.2+, and other browsers do a
  normal navigation [wp Q2].
- **Image attributes**: The LCP image gets `fetchpriority=high`; the rest get `loading=lazy
  decoding=async` [wp Q5].
- **Picture markup**: `<picture>` lists the AVIF source, then WebP, then `<img>` [wp Q6].
- **bfcache**: Anonymous responses never send `no-store`, and pages have no `unload` handlers
  [wp Q3].
- **Immutable caching**: Derivatives use `max-age=31536000, immutable` [wp Q9].
- **Early Hints**: Paths have no extension, so they are eligible [cf Q3]. Safari honours only
  preconnect [wp Q8], which is acceptable.

## R-11: Identity and request forgery

- **Decision**:
  - Use the built-in OIDC handler for Google.
  - The session is a cookie (`__Host-` prefix, Secure, HttpOnly, SameSite=Lax).
  - The claim is the atomic conditional update in data-model.md.
  - Built-in antiforgery protects form posts [az Q8]. JSON admin endpoints (presign, complete)
    require the antiforgery header token.
- **CSRF on JSON**: The antiforgery header is required explicitly, because minimal APIs only
  enforce it automatically for form binding [az Q8].

## Dependency justification (V.2)

| Package | Requirement served |
|---|---|
| MongoDB.Driver | The datastore is a platform constraint. |
| ~~Azure.Storage.Blobs + Azure.Identity~~ AWSSDK.S3 (amended by constitution 4.0.0) | R2 through its S3-compatible API: presigned PUT, server-side copy, delete, and ranged GET. Justified again in the plan of the feature that adds it. |
| Microsoft.OpenTelemetry | The Application Insights export (Platform Constraints, Observability). |
| MetadataExtractor | III.3 and III.7, reading EXIF without decoding, plus a second check on derivative metadata. |
| @jsquash/avif, @jsquash/webp | IV.6 encoding in the browser. Safari cannot encode WebP, and no browser encodes AVIF reliably. |
| thumbhash (about 1 KB, MIT) | IV.7 placeholder. |
| htmx | Admin interactivity (Platform Constraints). |

## Open gaps carried into the design (from research-notes)

- The exact B1 price for the chosen region, and the exact Blob and Application Insights unit
  prices. The pricing pages render with JavaScript; confirm them in the Azure Pricing Calculator.
- ~~Whether Cloud Connector is offered on the owner's zone.~~ Not needed since constitution 4.0.0.
- The list of Free-eligible Atlas regions on Azure.
- The Safari AVIF `toBlob` claim. It is not relied on, since jSquash is used everywhere.
- The ThumbHash data-URI byte size, to be measured.

All of these are verified at provisioning or in the first build feature and are listed in design
section 10.
