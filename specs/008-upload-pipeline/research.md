# Research: 008 Upload Pipeline into the Library

Phase 0 of `/speckit-plan`. Every decision below either cites an observed probe result
(`evidence/probe-results.txt`, code in `evidence/probes/`) or a line of the current source. The
owner was away. Each choice an owner would normally make is a DEFAULT row in `decisions.md`, from
row 21 on.

Format per entry: **Decision**, **Rationale**, **Alternatives considered**.

---

## R-01 Vendoring the jSquash encoders without a JS build toolchain

**Decision**: Ship the codec files only, downloaded from pinned npm tarballs by a committed script
`scripts/vendor-encoders.sh` (curl + `shasum -a 512` check against the registry's `dist.integrity`
+ `tar -x` of named members). Commit the output under
`src/Gallery.Web/wwwroot/admin-assets/vendor/`:

| Package (npm) | Version | License | Files kept | Bytes |
|---|---|---|---|---|
| `@jsquash/avif` | 2.1.1 | Apache-2.0 | `codec/enc/avif_enc.js`, `codec/enc/avif_enc.wasm`, `LICENSE` | 39 621 + 3 485 872 |
| `@jsquash/webp` | 1.5.0 | Apache-2.0 (codec BSD-3, `codec/LICENSE.codec.md`) | `codec/enc/webp_enc_simd.js`, `codec/enc/webp_enc_simd.wasm`, `LICENSE`, `codec/LICENSE.codec.md` | 38 646 + 345 584 |
| `thumbhash` | 0.1.1 | MIT | `thumbhash.js` (MIT notice copied into `VENDORED.md`, as the tarball has no LICENSE file) | 10 255 |

Integrity strings recorded (from `npm view <pkg>@<ver> dist.integrity`, 2026-11-01):
`@jsquash/avif@2.1.1 sha512-LMRxd0fM…Ti5VRNA==`, `@jsquash/webp@1.5.0 sha512-KggLoj2M…ZtKFw==`,
`thumbhash@0.1.1 sha512-kH5pKeII…VuCJcg==` (full strings go into the script). A `VENDORED.md` in the
vendor folder names each package, its version, its source URL and integrity, its license, and the
command that regenerates the folder. Building the app never runs npm or node. The script needs curl,
shasum and tar, which ship with macOS.

Each vendor folder name carries the version (`jsquash-avif-2.1.1/`), so the folder is
content-addressed by version. Files under `admin-assets/vendor/` get
`Cache-Control: public, max-age=31536000, immutable`, so the 3.5 MB AVIF wasm downloads once.

**Rationale**:
- Probe P4: the codec-level ES modules load in a module Worker with no bundler and no import map.
  The emscripten glue finds its `.wasm` beside itself (`new URL("avif_enc.wasm", import.meta.url)`).
  Real headless Chrome 156 encoded AVIF and WebP, without cross-origin isolation, from these exact
  files.
- The packages' top-level `encode.js` wrappers are NOT used. They import the bare specifier
  `wasm-feature-detect`, which needs a bundler or an import map. The AVIF wrapper also selects the
  multithreaded build whenever threads are available, and D-09 rejected that build (it needs
  COOP/COEP). The worker calls the emscripten factory directly, passing the full options object
  copied from each package's `meta.js` defaults (AVIF quality 50 speed 6; WebP quality 75 method 4).
- WebP: the SIMD build only. Two other APIs the pipeline needs already require these browser
  versions or later: module workers and 2D `OffscreenCanvas` (Safari 16.4, Chrome 69/80, Firefox
  105), and every one of those versions has wasm SIMD. A non-SIMD fallback could never be
  selected.
- The decode and resize codecs (`@jsquash/jpeg`, `@jsquash/resize`) are not needed.
  `createImageBitmap` decodes, and `OffscreenCanvas.drawImage` resizes (P4).

**Alternatives considered**: npm + a bundler (esbuild/vite) in the repo: rejected, since it adds a
JS toolchain to the app build (owner instruction). Loading from a CDN (unpkg/jsdelivr): rejected by
the spec Assumptions ("serve from the app itself, never a CDN") and by IV.21. Committing whole
tarballs: rejected, because the multithreaded build (3.5 MB) and the decoders would ship as dead
weight. Building the WASM from source (emsdk): rejected as unnecessary; Squoosh's builds are what
D-09 chose.

## R-02 Browser pipeline shape (D-09 as built)

**Decision**: One module Worker (`admin-assets/upload-worker.js`) processes one photo at a time.
For each photo it runs:
1. `createImageBitmap(file, {imageOrientation:'from-image'})`.
2. For each expected width (ladder widths ≤ upright width, or `[width]` when the photo is narrower
   than 320): `OffscreenCanvas(w, round(H*w/W))`, then `getContext('2d', {colorSpace:'srgb'})`,
   `imageSmoothingQuality='high'`, `drawImage`, and `getImageData(…, {colorSpace:'srgb'})`. The
   result is passed to the AVIF and WebP encoders.
3. Placeholder: downscale so the long edge is ≤100 px (ThumbHash's input limit), run
   `rgbaToThumbHash`, take the dominant colour from `thumbHashToAverageRGBA`, and render the preview
   with `thumbHashToRGBA` (≤32 px long edge). Encode the preview as WebP at quality 40 and send it as
   a `data:image/webp;base64,…` URI.
4. `bitmap.close()`.

Encoded buffers are transferred to the main thread, which PUTs them to their presigned URLs, up to
4 at a time. The worker starts the next photo while the main thread uploads the previous one.

**Rationale**: Probe P4 measured these values:
- orientation-6 (400×200, EXIF 6) decoded upright as 200×400.
- The 6000×4000 source produced all 6 widths, with heights matching `round(H*w/W)`.
- The main thread had 0 stalls over 200 ms.
- Placeholders were 110–130 bytes, well under the 1 KB ceiling (design section 10 measurement,
  first data point).
- For the 6000×4000 JPEG in desktop Chrome, a 2560 AVIF took 0.9 s and the whole photo about
  2.4 s. That is far inside SC-009's 60 s for Chrome. Safari and iOS are not measured
  (decisions row 6).

The `getImageData` sRGB colour space is the P3→sRGB conversion D-09 asks for.

**Alternatives considered**:
- `@jsquash/resize` (Lanczos in WASM, about 600 KB): rejected, because canvas resampling at
  `high` quality is enough for the thumbnail sizes, and V.2 asks for the fewest third-party
  pieces.
- A pool of several workers: rejected, because each AVIF encode already uses one core and holds a
  full-resolution bitmap. Two concurrent 60 MP decodes are the iOS memory risk E-05 names.
- ThumbHash from the 320 px bitmap, as D-09 words it: ThumbHash's encoder requires input of at
  most 100×100, so D-09's wording is updated (see "Design record updates" in plan.md).

## R-03 Presigned PUT headers and the CORS rule (FR-044)

**Decision**: Keep `ImageStore.PresignPutAsync` as it is. Add a [SAFETY] test that parses each
issued URL and asserts `X-Amz-SignedHeaders == "content-type;host"`, `X-Amz-Expires <= 900`, and
no `x-amz-checksum-*`, `x-amz-sdk-checksum-algorithm` or `x-amz-meta-*` query keys. The test must
fail when `Headers.CacheControl` or `Metadata` is set on the request (non-vacuity break). The
browser PUT sends exactly one header, `Content-Type`.

**Rationale**: Probe P1 (AWSSDK.S3 4.0.104.8):
- The default request signs `content-type;host` and nothing else, with the factory's
  `WHEN_REQUIRED` checksum setting or without it.
- Adding cache-control or metadata adds them to SignedHeaders, which would make the browser send a
  header R2's CORS rule refuses. The owner's R2 CORS rule allows `content-type` only (decisions
  row 7).
- On S3Proxy: a wrong content type gives 403, a missing content type 403, an extra header 403,
  use after expiry 403, and GET on the PUT URL 403.

So the emulator also enforces the content type, operation, expiry and key. That is FR-042's
"different operation, location, after expiry" set, plus content type.

**Alternatives considered**: Presigned POST policies (could cap size): rejected by D-08 ("no
multipart"/"POST policies unnecessary"). An oversize original is caught on the server's master
check instead (FR-022). Signing cache-control at upload time: rejected, because the cache header
is set by the server-side copy at confirm (decisions row 20).

## R-04 Upload request flow and endpoint shape

**Decision**: JSON endpoints under the existing `/admin` group, so they inherit the owner policy
and `AdminAntiforgeryFilter`. The browser sends the token in the `X-CSRF-TOKEN` header, taken from
a `data-csrf` attribute the page renders. The flow per photo is:

1. `POST /admin/upload/items`: register up to N picked files `{name,size,type}`. The server
   gets or creates the open batch, enforces type `image/jpeg`, size ≤ 20 MB and the 100-photo
   count atomically, and returns each file's `itemId`, `attemptId` and the master's presigned URL,
   or a refusal reason.
2. Browser PUTs `master.jpg`.
3. `POST /admin/upload/items/{itemId}/master`: the server checks the stored master's header (R-06)
   and records its upright width, height, ETag and length. It returns the expected size set and
   one presigned URL per size. A bad master rejects the photo right away.
4. Browser encodes (it can start before step 3 returns), then PUTs each size.
5. `POST /admin/upload/items/{itemId}/complete` with the placeholder and browser timings. The server
   validates (R-05/R-06/R-07); the item becomes ready, or it fails with all of its files deleted.

`POST …/progress` (heartbeat + state), `…/fail` (client-side error), `…/restart` and `…/delete` act
on one item. `POST /admin/upload/confirm` and `POST /admin/upload/discard` act on the batch.
`GET /admin/upload/state` is the JSON read the script uses after a reload. Full shapes are in
`contracts/http.md`.

Issuing the size permissions only after the master check is how the server knows "a size the photo
should not have" (FR-020). It never trusts a width list from the page.

**Rationale**: FR-020 needs the source width before size permissions are issued. FR-022 needs a
server-read header. Splitting master and sizes gives both, and lets a bad master fail before any
encoding is uploaded. Every request carries `attemptId`. The server matches it against the stored
current attempt, so stale attempts are refused with 409 (FR-011, FR-019).

**Alternatives considered**: One "permissions for everything" call before the master is stored:
rejected, because the server could not know the expected widths and would have to trust the page.
htmx form posts: rejected, because the upload flow is script-only anyway (worker + PUTs) and htmx
would be a third-party script with no requirement behind it (V.2). See R-11.

## R-05 Reader 1: hand-written container parser (server code)

**Decision**: Add a new `Gallery.Web.Upload.ContainerParser` (static, pure, about 250 lines). It ports
`scripts/check-metadata.sh` and adds dimensions and an allowlist:
- **WebP**: requires `RIFF….WEBP` with a RIFF length equal to file length − 8. Chunk allowlist
  `VP8 `, `VP8L`, `VP8X`, `ALPH`; anything else (`ICCP`, `EXIF`, `XMP `, `ANIM`, `ANMF`, unknown)
  rejects. In VP8X, the ICC (0x20), EXIF (0x08), XMP (0x04) and animation (0x02) flags must be
  clear. Dimensions come from the VP8 frame header (14-bit), VP8L (14-bit+1) or VP8X canvas
  (24-bit+1).
- **AVIF**: requires `ftyp` first, with major brand or a compatible brand `avif`. `avis`
  (sequence) rejects. Top-level box allowlist `ftyp meta mdat free skip`. Size 1 (64-bit largesize)
  and size 0 (to end) are handled, since the sips fixture uses largesize (P4). In `meta`, `iinf`
  must hold exactly one item of type `av01` (one image; a second `av01` is an embedded
  thumbnail), and any `Exif` or `mime` item rejects. `iref` is rejected (thumbnail/aux links).
  The `ipco` property allowlist is `ispe pixi av1C colr pasp`. `colr` must be `nclx`; `prof`/`rICC`
  ICC rejects. `irot`/`imir`/`clap` reject, because orientation must be in pixels (III.4) and
  `ispe` would then disagree with the displayed shape. Dimensions come from `ispe`.
- Output: `(ok, format, width, height, reason)`. Reasons are fixed strings naming the box or chunk,
  never a value read from the file (FR-040).

**Rationale**: D-10 names hand-written parsers as the first reader. Probe P4 shows real jSquash
output is exactly `ftyp meta{hdlr pitm iloc iinf{infe(av01)} iprp{ipco{ispe pixi av1C colr(nclx)}
ipma}} mdat` for AVIF and a single `VP8 ` chunk for WebP, so the allowlists accept all real encoder
output. The GPS fixtures fail on `infe(Exif)` and on the `VP8X` EXIF flag plus the EXIF chunk.
Rejecting ICC is stricter than III.5 allows. It matches reader 2's behaviour (decisions row 9),
and P4 shows jSquash in Chrome emits none.

**Alternatives considered**: Shelling out to `scripts/check-metadata.sh`: rejected, because it is
bash, depends on dev-machine tools, and has no dimension checks. An item/property denylist instead
of an allowlist: rejected, because new metadata box types would pass silently (III rationale:
"allowlist is the only approach that stays safe").

## R-06 Master check: header only, from a prefix

**Decision**: `HEAD` the staged master (length ≤ 20 971 520 bytes, ETag). Then make a ranged read
of the first 262 144 bytes and run `ImageMetadataReader.ReadMetadata` on it:
- Require the File Type directory to be JPEG and a `JpegDirectory` with SOF width and height.
- Read IFD0 `Orientation`; values 5–8 swap width and height to give the upright shape.
- When SOF is missing or the reader throws, retry once with the first 1 048 576 bytes, then reject
  with "unreadable JPEG header".
- Camera fields come from `CameraFields.Read` (called, not copied) on the same bytes, inside a
  try/catch, so a throw means "no camera details" (decisions row 17).

The step runs at `…/master` (to fix the expected size set) and again at `…/complete` (FR-022: "when
the page reports a photo complete"). The second read must match the recorded length, ETag and
dimensions, or the photo is rejected.

**Rationale**: Probe P6:
- orientation-6 gives SOF 400×200 and Orientation=6 from 600 bytes.
- The 6000×4000 JPEG gives SOF from a 256 KB prefix.
- A 200-byte prefix throws `IOException` on some files and silently omits the JPEG directory on
  others, so both cases need handling.
- not-a-jpeg throws `ImageProcessingException`.

No pixels are decoded (III.8).

**Alternatives considered**: Trusting the page's reported dimensions: prohibited by III.5 and
FR-022. Downloading the whole master: 20 MB per photo would break SC-007's 2 s, and the header is
enough.

## R-07 Reader 2 and the "both readers agree" rule

**Decision**: Call `Gallery.Web.Seed.DerivativeValidator.Validate(stream, ext, width, aspect)`
unchanged for every size and the placeholder. A size passes only when:
- reader 1 passes;
- reader 2 passes;
- reader 1's format equals the stored name's format, and reader 1's width equals the stored
  width;
- reader 2 is called with the stored width and the master's upright aspect, so it checks its own
  dimensions independently. Both use the same height rule: `|h − round(w·H/W)| ≤ 2`.

The placeholder is validated as WebP and ≤ 1 536 decoded bytes, with long edge ≤ 32 and both sides
≥ 1. Reader 2 gets reader 1's width and aspect for it, since ThumbHash's preview aspect is
approximate (P4: 32×18 for a 2:1 source). Its own metadata-directory allowlist is the independent
check.

**Rationale**: Reuse by calling (orchestration rule; spec Assumptions). P4 shows reader 2's
directory names for jSquash output are all in its allowlist.

**Alternatives considered**: Moving `DerivativeValidator` out of `Seed/`: rejected for 008, because
a move is a refactor with no requirement behind it. It stays callable where it is.

## R-08 Batch persistence (D-11 as built)

**Decision**: A new `batches` collection with documents `BatchDoc { _id, state, items[],
createdAt, updatedAt, confirmStartedAt? }`. Items are embedded, as in the design record, and are
updated with positional filters on `items.itemId` + `items.attemptId`, so a stale attempt matches
nothing. The data model is in `data-model.md`.
- **Single open batch**: unique partial index `{state:1}` with `partialFilterExpression
  {state:"open"}`, created lazily on the batch store's first use (startup must not touch Mongo).
  Get-or-create does a find; if there is none it inserts, and on `DuplicateKey` it re-reads.
- **Count limit, atomic**: register pushes `$each` the accepted items with the filter
  `{_id, state:"open", confirmStartedAt:null, "items.{100−k}": {$exists:false}}`. On no match it
  re-reads and refuses the overflow ("batch is full"). Two tabs racing cannot exceed 100.
- **Interrupted on read**: a pure function `EffectiveState(item, now)` returns `interrupted` when
  the stored state is queued, processing or uploading and `now − heartbeatAt > 2 min`. It is applied
  on every read (page, state JSON, confirm gate) and on every write that requires an active item
  (progress, complete), so a waking laptop's late report gets 409. No timer. Time comes from the
  built-in `TimeProvider` so tests can advance it.
- **Interrupted is persisted lazily**: there is none. The stored state stays as it was. Restart
  writes `queued` with a new attemptId.

**Rationale**: D-11. Embedding matches the design-record content model and keeps the page at one
datastore read (SC-010). 100 items × about 3 KB is about 300 KB per document, far below Mongo's
16 MB.

**Alternatives considered**: An `items` collection: rejected, because the page would need two
reads and the design record embeds items. A background sweeper: rejected by D-11 (and see R-13).

## R-09 Confirm: idempotent, copy-if-match, no purge

**Decision**: `POST /admin/upload/confirm`:
1. A conditional update sets `confirmStartedAt` only if the batch is open, has ≥1 item, and every
   item's effective state is ready. Otherwise it answers 409 with the counts. While
   `confirmStartedAt` is set, every other batch mutation (register, item actions, discard) answers
   409 "confirm in progress". A repeated confirm is allowed.
2. For each item, in order:
   - Copy each size from staging to `public/{sha256-16hex}.{ext}` with
     `CopyObjectRequest.ETagToMatch = validated ETag`, content type, and
     `Cache-Control: public, max-age=31536000, immutable`.
   - Copy the master to `private/masters/{itemId}.jpg`, also with `ETagToMatch`.
   - Upsert `ImageDoc {_id: itemId}` with `$setOnInsert` (title = file name without extension,
     validated fields, `createdAt`, `galleryIds: []`, no `seedKey`).
   - Delete the item's staged files (best effort).
   Storage operations run up to 8 at a time.
3. Set `state: "confirmed"`.

A crash at any point leaves the batch open with `confirmStartedAt` set, and Confirm runs again to
the same result. When a copy is refused by if-match, that item fails ("changed after validation")
and loses its staged files, the confirmed items are pulled out of the batch, and the batch returns
to plain open with only the failed items, so the owner is never locked out (contracts/http.md U10;
decisions row 45, added by /speckit-analyze). The sha256 for each public name is computed at validation time (the server
already holds the bytes) and stored on the item.

**Rationale**: Probe P3: copy with if-match succeeds while the object is unchanged and returns 412
once it has been re-PUT, and the destination stays absent. This closes the window in which a
still-valid 15-minute presigned URL could swap a validated size for an unvalidated one before
confirm (III.6). Copies with the same content-hash name are idempotent, and the `_id` upsert is
idempotent (FR-032). No purge or warm runs, because the images join no gallery (FR-033, II.5).

**Alternatives considered**:
- Re-validating every size at confirm: rejected, because it doubles the reads and cannot beat 60 s
  for 40 photos on R2. If-match gives the same guarantee for one header.
- A background confirm job (Channel): rejected, because the synchronous request finishes inside
  SC-008 at this scale. A job would add a polling state for no requirement.
- Allowing Discard after a confirm has started: rejected, because some images may already be in
  the library and a discard would contradict them (decisions row 23).

## R-10 Seed coexistence: `seedKey` must be absent, not empty (FR-035)

**Decision**: Change `ImageDoc.SeedKey` and `SourceHash` to `string?` with `[BsonIgnoreIfNull]`.
Upload never sets them. Add a [SAFETY] test: create an uploaded image, run the seeder, and the
uploaded image is unchanged.

**Rationale**: Finding P7. Today `SeedKey` defaults to `""`, which serialises as `"seedKey": ""`.
`Seeder` step 5e deletes images matching `Exists("seedKey") && seedKey ∉ manifest`, so every
uploaded image would be deleted by the next seed run. The spec assumed "uploaded images carry
none". The code makes that false unless it is changed.

**Alternatives considered**: An explicit `origin: "upload"` field and a changed seeder filter:
rejected, because it touches seeder logic that 006 tests pin. Making the field absent satisfies
the existing filter unchanged.

## R-11 Page and script structure (V.3, D-21, D-23)

**Decision**:
- `GET /admin/upload` renders `UploadPage.razor` through `AdminLayout` (`Current=AdminPage.Upload`).
  The page holds:
  - an `<upload-batch>` custom element carrying `hidden`, `data-csrf`, `data-max-mb`,
    `data-max-photos` and `data-state-url`;
  - `UploadDropzone` with the new optional `MaxPhotos` parameter;
  - a `ContactSheet` (ChildContent) of `BatchItem`s, one per stored item, in its effective state;
  - a `BatchFooter`;
  - a `<noscript>` notice: "Uploading needs JavaScript. Turn it on to add photos." (FR-006).
- One page script, `admin-assets/upload.js` (a module of about 400 lines, no framework), removes
  `hidden`, makes the whole document a drop target, sniffs the first 3 bytes for `FF D8 FF` and
  checks size and remaining count at pick time (FR-004), drives the worker, PUTs, and calls the
  JSON endpoints.
- **Markup comes from one source.** The server renders one `<template data-tile="{state}">` per
  `BatchItemState`, made by the real `BatchItem` component with sentinel values (`\u2063name\u2063`
  and similar). It renders the same for the footer states and a refusal row. The script clones a
  template and replaces sentinels by walking text nodes and attributes. It never builds HTML
  strings, so a file name with `<` can't inject markup.
- **Optional parameters on existing components**, all null by default with output unchanged: on
  `BatchItem`, `data-item` rendered only when `ItemId` is non-empty; on `UploadDropzone`,
  `MaxPhotos`, appended to the limits line as an expression. `UiPagesUnchangedTests` and
  `PublicOutputUnchangedTests` must pass unmodified.
- After confirm the script navigates to `/admin/upload?confirmed={batchId}`. The server renders "N
  photos added to your library" from that confirmed batch, a link to `/admin/library` (404 until
  009, decisions row 1), and an empty drop target.
- Tiles show the file name in every pre-confirm state (BatchItem `Preview = null`). Staged sizes are
  private and have no viewable URL (decisions row 24).

**Rationale**: V.3 allows admin pages to require JS. Platform Constraints name htmx and web
components; the custom element is the web component. htmx is not added, since no requirement needs
it (V.2). Templates keep the 005 contract markup as the only markup, so the `/ui/` pages and the
live page cannot drift.

**Alternatives considered**: Building tile DOM in JS: rejected, because it duplicates the 005
markup and drifts. Fetching server-rendered fragments per change: rejected, because it costs about
400 extra requests and full re-renders for a 100-photo batch.

## R-12 Script and asset caching

**Decision**: Admin scripts live at `/admin-assets/*.js`, outside `/admin`, so they escape the
admin `no-store` and are not owner-gated. They contain no secrets; FR-043's test scans them. They
are referenced as `?v={sha256-8hex}`, computed once at startup by an `AdminAssets` singleton. The
static-file hook sets `immutable` on versioned and `vendor/` URLs and `no-cache` on bare URLs.
`.js`/`.mjs`/`.wasm` content types are already correct (P5).

**Rationale**: Orchestration lesson: an asset served without a cache policy is cached
heuristically, so new markup gets paired with an old script. Content-hashed URLs fix this at the
source.

**Alternatives considered**: Serving under `/admin/…`: rejected, because `no-store` would
re-download 3.9 MB of wasm on every page load.

## R-13 Staged-file cleanup

**Decision**: Explicit deletes on Delete, Restart (old attempt's sizes), Discard, rejection and
confirm (FR-036), via `ListObjectsV2` on the item or batch prefix, then `DeleteObject` per key. A
failed delete is logged as `upload.cleanup_failed` and does not fail the request. The 7-day R2
lifecycle rule is the backstop; it remains an owner prerequisite before the live run (spec
Assumptions 2). D-11's "best-effort server timer sweep" is NOT built.

**Rationale**: FR-036 names the lifecycle rule as the backstop and puts the timer out of scope.
D-11's timer clause is removed in the design record (VII.2).

**Alternatives considered**: A hosted-service sweeper: rejected (D-11 alternative; no FR).

## R-14 Test strategy: emulator, browser, and what only the owner can run

**Decision**:
- **Server path, no browser** (most tests): `AppBuilder.Build` host + `MongoFixture` +
  `S3ProxyFixture` + `AdminHost`'s owner session. Tests PUT committed fixture bytes to the
  presigned URLs with `HttpClient`. The fixtures are real jSquash output from P4 and the GPS
  fixtures `gps-sips.avif`/`gps-cwebp.webp`, copied into `tests/fixtures/upload/`.
- **Readers**: unit tests per rejection class (SC-002 list), each asserting reader 1 alone, reader 2
  alone, and the combined validator (FR-041).
- **Presign**: the signed-headers test (R-03), plus FR-042's other-operation, other-key and expiry
  checks. They go in a new `UploadStorageTests` file with `[Fact]` pairs for the emulator and R2,
  following the existing `StorageTests` pattern; `StorageTests.cs` stays unmodified (decisions
  row 37).
- **Browser E2E** (one Playwright test class, `UploadBrowserTests`): the real page, worker and vendored
  encoders upload two fixture JPEGs to the emulator, then confirm. It runs in Chrome via
  `Channel="chrome"`.
  - Before writing it, a 2-minute probe must settle two things. First, the owner `__Host-` cookie
    over the fixture origin; if http on localhost does not carry it, use `https://localhost` with
    the dev certificate. Second, S3Proxy CORS: P2 left it unverified. If the emulator cannot answer
    preflights, launch this one fixture's browser with `--disable-web-security`, and record that
    FR-044 is then proven by the signed-headers test plus the owner's live R2 run, not by this
    test.
- **Owner post-build live run** (not automated; quickstart Part C): real R2 with the CORS rule,
  Safari macOS and iOS (the skipped spike, decisions row 6), SC-007 on R2, SC-009, and the size and
  colour-profile measurements.

**Rationale**: The emulator enforces method, key, content type and expiry (P1), so the server path
is testable without R2. Browser-only behaviour (worker, orientation, no main-thread blocking) needs
one real-browser test (orchestration: "browser-only success criteria get automated Playwright
tests").

**Alternatives considered**: An end-to-end test only, with no server-path tests: rejected, because
it is slow and gives poor failure locality for the [SAFETY] rejections.

## R-15 Request body limits, derived from the batch limit

**Decision**: Every upload endpoint states its own limit with
`.WithMetadata(new RequestSizeLimitAttribute(n))` (P8: honoured, oversize → 413):
- `POST /admin/upload/items`: 128 KiB. Derivation: 100 files × (file name ≤ 255 chars × up to 3
  UTF-8 bytes = 765, plus about 120 bytes of JSON per entry) ≈ 88.5 KB, rounded up to a power of
  two. The handler also refuses more than `MaxPhotosPerBatch` entries.
- `…/complete`: 16 KiB. Derivation: a placeholder of ≤ 1 536 bytes is ≤ 2 048 base64 characters,
  plus ≤ 40 timing entries × about 64 bytes ≈ 2.6 KB, plus framing.
- All other upload POSTs: 4 KiB. They carry `attemptId`, a state, a percent, or a reason of at
  most 200 characters.

No limit is sized from the 20 MB per-file limit, because no image bytes pass through the app
(FR-018). The 30 MB Kestrel default is not relied on.

**Rationale**: Orchestration rule: when per-item work becomes a batch, derive the body limits from
the batch limit. The derivation is written here so a later change to `MaxPhotosPerBatch` updates
it.

## R-16 Timing events (IV.1, IV.2, IV.4; FR-037–FR-040)

**Decision**: Existing `http.request` covers every new route. The following events are logged with
the existing `op.timing {op} {duration_ms} {outcome}` template via `OpTimer`/`OpTiming`:
- **Storage**: `s3.presign`, `s3.head`, `s3.get`, `s3.get-range`, `s3.list`, `s3.copy`,
  `s3.delete`. `s3.get` and `s3.list` are new ImageStore methods.
- **Datastore**: `db.batch.read`, `db.batch.create`, `db.batch.update`, `db.image.upsert`.
- **Server work**: `upload.master-check`, `upload.validate` (one per photo), `upload.confirm.item`,
  `upload.confirm`.
- **Browser timings**, posted with `complete` and logged one event each with `outcome` `ok`:
  `browser.codec-init`, `browser.decode-orient`, `browser.resize.{w}`,
  `browser.encode.{avif|webp}.{w}`, `browser.placeholder`, `browser.total`. The server accepts only
  names matching `^browser\.(codec-init|decode-orient|resize\.\d{2,4}|encode\.(avif|webp)\.\d{2,4}|placeholder|total)$`
  and durations 0–600 000 ms; others are dropped and counted.

Separate structured events:
- `upload.rejected {batch} {item} {file} {reason}`
- `upload.confirmed {batch} {count} {duration_ms}`
- `upload.discarded {batch} {count}`
- `upload.cleanup_failed {key} {error}`

None of them logs a metadata value.

Decode and orientation are ONE event, `browser.decode-orient`. `createImageBitmap(…,
{imageOrientation:'from-image'})` performs both in one platform call, so they cannot be timed
apart without hand-parsing EXIF in JS and rotating on a canvas. That would add code solely to split
a timer. FR-039 is met by reporting the combined operation under a name that says so (decisions row
25).

**Rationale**: SC-006..SC-010 must be read from logs alone (IV.4). `evidence/p95.sh` (a task)
computes them from captured logs.

## R-17 Package and dependency summary (V.2)

| Dependency | Kind | New? | Justification |
|---|---|---|---|
| AWSSDK.S3 4.0.104.8 | NuGet | existing | presign, head, ranged get, list, copy-if-match, delete (D-07, D-08) |
| MetadataExtractor 2.9.3 | NuGet | existing | reader 2 and the master header/camera read (D-10, III.7) |
| MongoDB.Driver 3.13.0 | NuGet | existing | `batches`, `images` |
| Microsoft.Playwright 1.64.1 | NuGet (tests) | existing | the one browser E2E test (R-14) |
| Testcontainers 4.16.0 | NuGet (tests) | existing | S3Proxy + Mongo fixtures |
| @jsquash/avif 2.1.1 (codec files) | vendored static asset | NEW | AVIF encode in the browser (D-09, FR-014); no browser encodes AVIF natively |
| @jsquash/webp 1.5.0 (SIMD encoder) | vendored static asset | NEW | WebP encode in the browser (D-09); Safari can't encode WebP from canvas |
| thumbhash 0.1.1 | vendored static asset | NEW | placeholder colour and preview (D-09, FR-016, IV.7) |

No NuGet package is added. No npm or node runs in the app build.

## R-18 Owner prerequisites before the live run (not before tasks or implement)

1. The 7-day lifecycle rule on `gallery-dev-private` under `staging/` (spec Assumptions 2).
2. Optional: masters to Infrequent Access after 30 days (D-07).
3. A Safari and iOS device run of quickstart Part C (decisions row 6).

The existing CORS rule is used exactly as registered: origin `https://localhost:5081`, PUT, header
`content-type`.
