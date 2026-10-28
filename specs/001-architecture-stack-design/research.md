# Research: Architecture and Stack Design (Phase 0)

The evidence lives in `research-notes/`: five area files, each claim with a fetched quote and a
`Fetch: OK` line, plus a `## Gaps` section. This file consolidates the decisions that
`docs/design.md` will record. Area files are cited as [cf] cloudflare.md, [st]
storage-datastore.md, [br] browser-encoding.md, [az] azure-dotnet.md, and [wp] web-platform.md.

## R-01: Edge on Cloudflare Free covers every Principle IV delivery rule

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

## R-02: Storage budget, where R2's free 10 GB is the binding constraint

- **Facts**:
  - The R2 free tier is 10 GB-month, 1M Class A operations, and 10M Class B operations, with
    free egress [st Q1].
  - PutObject and CopyObject are Class A; DeleteObject is free [st Q5].
- **Estimate**: These are design estimates, verified at implementation from logged byte counts.
  - The derivative ladder of 6 widths × 2 formats is about 2.2 MB per image (AVIF about 0.9 MB,
    WebP about 1.3 MB).
  - A master is about 8–15 MB.
  - Derivatives only: about 11 GB at 5,000 images, so the free tier holds about 4,000 images.
  - Derivatives plus retained masters: about 60–85 GB at 5,000 images, so the free tier holds
    about 700 images.
- **Decision (default, needs owner review, D-masters)**: Masters are kept only in the private
  staging area until their batch is confirmed. They are needed for Restart (FR-027). After
  confirm they are deleted, and the owner's local exports remain the source of truth, which is
  consistent with the accepted no-backup risk. This conflicts with III.1 as worded ("Masters
  MUST be stored privately"), so the decision is flagged `requires-amendment`.
- **Capacity default (D-capacity)**: The design states a free-tier capacity of about 4,000
  images. Above that, R2 bills $0.015 per GB-month; another 10 GB costs about $0.15/month. That
  breaks VIII.1 literally, so the decision is recorded as a known ceiling with two cheap
  reversals. One reversal is to amend VIII to allow metered storage under $1/month. The other is
  to drop the 2560 width or WebP (all current browsers decode AVIF [wp Q6]), which roughly
  halves storage but needs an amendment to IV.6.
- **Class A ops**: There are 13 PUTs (master plus 12 derivatives) and 12 cross-bucket copies on
  confirm, about 25 per image. 5,000 images is about 125k, within 1M.

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

## R-04: R2 layout uses two buckets

- **Decision**: `gallery-private` holds staging (masters and unconfirmed derivatives).
  `gallery-public` holds confirmed derivatives and is bound to a custom domain on the same
  Cloudflare zone. The custom domain gets the Cloudflare cache and Cache Rules; `r2.dev` does
  not, and is never used [st Q2].
- **Rationale**: R2 public access works per bucket. Staging must never be publicly reachable,
  even by a guessed key.
- **Backstop sweep**: A lifecycle rule on `gallery-private` deletes `staging/` objects after 7
  days, and incomplete multipart uploads abort after 7 days by default [st Q4]. This removes
  orphans without any app timer (FR-030).

## R-05: Upload path is direct from the browser to R2 with presigned PUTs

- **Decision**:
  1. The server issues presigned PUT URLs scoped to `staging/{batch}/{item}/{attempt}/…`, with a
     15-minute expiry.
  2. The browser uploads the master and the derivatives directly to R2. The bucket's CORS
     configuration allows only the admin origin [st Q3].
  3. The browser then calls `complete`, and the server validates the staged files.
- **Rationale**: 20 MB masters never pass through the B1's single vCPU and 1.75 GB of RAM
  [az Q1]. Presigned URLs also keep the R2 credentials server-side.
- **Alternatives**: Streaming uploads through the app was rejected because it would push
  every master and derivative through the B1 and its single vCPU.

## R-06: Server-side validation without decoding pixels (III.5–III.8)

- **Decision**: For each staged derivative, the server reads it with a ranged GET (Class B) and
  parses the container structure only:
  - **AVIF** (ISOBMFF): `ftyp` must be `avif`. Dimensions come from `ispe`. The file must contain
    no `infe` item of type `Exif`, and no `mime` item whose content type is `application/rdf+xml`
    (XMP). A `colr` box is allowed only with nclx, or with an sRGB ICC profile [az Q6].
  - **WebP** (RIFF): `RIFF…WEBP` with `VP8`, `VP8L`, or `VP8X`. If `VP8X` is present, the EXIF
    and XMP flags must be clear and the file must contain no `EXIF` or `XMP ` chunk. Dimensions
    come from the bitstream or the canvas header [az Q6].
- **Master**: The server reads the EXIF allowlist with MetadataExtractor, which is Apache-2.0
  and parses headers without decoding [az Q5]. It reads only the first 256 KB via a ranged GET.
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

## R-09: Observability

- **Decision**:
  - Log JSON to the console (built-in formatter).
  - App Service logs → File System, with a 35 MB quota and 7-day retention, which is free.
  - Evidence is pulled with `az webapp log download` or the log stream [az Q3].
  - Event names are `http.request` (route, status, duration_ms, cache) and `op.timing` (op,
    duration_ms, outcome).
  - `Server-Timing` is sent on public pages [wp Q7].
- **Alternatives**: Application Insights was rejected because of metered ingestion (VIII.3)
  and the clarification that chose free logs only.

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
| AWSSDK.S3 | R2's S3 API for presigning, copy, delete, and ranged GET. |
| MetadataExtractor | III.3 and III.7, reading EXIF without decoding, plus a second check on derivative metadata. |
| @jsquash/avif, @jsquash/webp | IV.6 encoding in the browser. Safari cannot encode WebP, and no browser encodes AVIF reliably. |
| thumbhash (about 1 KB, MIT) | IV.7 placeholder. |
| htmx | Admin interactivity (Platform Constraints). |

## Open gaps carried into the design (from research-notes)

- The exact B1 price for the chosen region.
- The list of Free-eligible Atlas regions on Azure.
- The Safari AVIF `toBlob` claim. It is not relied on, since jSquash is used everywhere.
- The ThumbHash data-URI byte size, to be measured.

All four are verified at provisioning or in the first build feature and are listed in design
section 10.
