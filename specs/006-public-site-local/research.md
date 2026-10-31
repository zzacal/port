# Research: 006 Public Site, Local Walking Skeleton

Every decision here was checked by a probe on 2026-10-31, unless it says otherwise. Probe output
is in `evidence/probe-s3.txt`. Probes ran in `/tmp` and were then deleted.

## R-01: Toolchain and package versions

- **SDK**: 10.0.403 is installed and pinned (`global.json`, `latestPatch`). It is the latest
  released .NET 10 SDK in the official release index. 11.0 is `go-live` (RC) and does not count.
- **Packages added in this feature**, latest stable on NuGet on 2026-10-31. Each is justified
  under Constitution V.2:

| Package | Version | Project | Requirement served |
|---|---|---|---|
| MongoDB.Driver | 3.13.0 | Gallery.Web | Datastore reads for every public page (FR-001–004); seed writes (FR-017). Platform constraint. |
| AWSSDK.S3 | 4.0.104.8 | Gallery.Web | R2 through its S3-compatible API: put, head, ranged get, copy, delete, presign (FR-017, FR-019, FR-022; D-07, D-08). R2 has no first-party .NET SDK; Cloudflare documents AWSSDK.S3. |
| MetadataExtractor | 2.9.3 | Gallery.Web | Allowlisted camera fields read from the original JPEG without decoding pixels (FR-013; III.3, III.7, III.8). Already in the 001 dependency table. |
| Testcontainers.MongoDb | 4.16.0 | Gallery.Web.Tests | Real Mongo for integration tests (Development Workflow: integration tests against real Mongo in containers). |
| Testcontainers | 4.16.0 | Gallery.Web.Tests | Generic container for the S3 emulator (R-02). Comes in with Testcontainers.MongoDb anyway; referenced directly because the emulator fixture uses `ContainerBuilder`. |

- **Rejected**: Mongo EF Core provider (extra layer for three read shapes); Minio .NET SDK
  (the server is the emulator, not the client); Serilog (the built-in JSON console logger already
  meets IV.1–2); `Microsoft.AspNetCore.Mvc.Testing` (in-process tests miss the bare-HTTP bugs the
  live `verify.sh` catches, and this feature's HTTP checks are header and redirect assertions that
  curl does well).

## R-02: S3 emulator for tests, and R2 client settings

- **Decision**: S3Proxy (`andrewgaul/s3proxy`, Apache 2.0) in `transient` mode, with
  `S3PROXY_AUTHORIZATION=aws-v2-or-v4`, pinned by digest in the test fixture. This replaces the
  owner's MinIO pick: MinIO's community edition was archived in April 2026.
- **Probe** (the same nine operations against three targets, AWSSDK.S3 4.0.104.8):

| Operation | S3Proxy | SeaweedFS | R2 dev |
|---|---|---|---|
| put with Cache-Control, head, head-missing 404 | pass | pass | pass |
| ranged get (206) | pass | pass | pass |
| presigned PUT via plain HttpClient | 200 | 200 | 200 |
| presigned PUT URL used for GET | **403** | **200** | **403** |
| copy private→public with metadata REPLACE | pass | pass | pass |
| list prefix, delete | pass | pass | pass |

- **Why S3Proxy**: SeaweedFS accepted a GET on a URL presigned for PUT. That is exactly the
  "one object, one method" property III.9 depends on, so an emulator that doesn't enforce it would
  let a broken presigner pass every test. S3Proxy and R2 both refuse it.
- **Client settings**, set once in the single place the client is built. All are proven by the R2
  run:
  - `ForcePathStyle = true`, `AuthenticationRegion = "auto"`.
  - `RequestChecksumCalculation = WHEN_REQUIRED` and `ResponseChecksumValidation = WHEN_REQUIRED`.
    Recent AWS SDKs send integrity checksums by default, which R2 rejects.
  - Uploads: `DisableDefaultChecksumValidation = true`, and `DisablePayloadSigning = true` **only
    when the endpoint is HTTPS**. The SDK refuses unsigned payloads over HTTP ("When
    DisablePayloadSigning is true, the request must be sent over HTTPS"), so the emulator, on
    plain HTTP, runs signed, and R2 runs unsigned as Cloudflare documents.
  - Presigned URLs: `Protocol` follows the endpoint scheme. The default is HTTPS, which breaks
    against the HTTP emulator.
  - `CopyObject` with `MetadataDirective = REPLACE` worked on R2 with SDK 4.0.104.8. The
    "x-amz-tagging-directive not implemented" error reported for older SDKs did not occur. It stays
    in the on-demand R2 test, because copy is the confirm step in 008.

## R-03: R2 token scope and delivery (III.9 evidence)

Probed with the owner's dev token using `curl --aws-sigv4`. Output is in `evidence/probe-s3.txt`.

- ListBuckets (account scope): **403**. List and create on another bucket: **403**. List on both
  dev buckets: **200**. So the token is limited to Object Read and Write on the two buckets.
- `img-dev.zacal.me` serves an object put with `Cache-Control: public, max-age=31536000,
  immutable`: the first request is a `MISS`, the second a `HIT`, and the header is passed through.
  The object metadata is enough; there is no cache rule to add for the dev hostname.
- The r2.dev setting can't be read with a bucket-scoped token (the Cloudflare API answers
  "Authentication error"), and an r2.dev address contains a random id that can't be guessed. The
  owner confirmed it disabled in the dashboard on 2026-10-30.
  - **Deviation from FR-021**: the automated check covers the token-scope half (403 on account
    scope, on another bucket, and on bucket creation; 200 on the two dev buckets). The r2.dev half
    is an owner-verified dashboard check, recorded in `decisions.md` and repeated in the
    quickstart.
  - Automating it would need a second Cloudflare API token with account read access to R2 bucket
    settings. That is a broader credential than the one III.9 limits, so it is not worth adding to
    check one setting. The spec's FR-021 is updated to say so.

## R-04: Preparing the owner's photos (FR-011–FR-013)

- **Survey** of `dev-pictures/` (93 JPEGs, read-only): 58 Fujifilm X100VI and 35 Leica Q3 / Q3 43.
  None is over 20 MB. 5 are portrait, 0 square, all wider than 2560 px, so every photo gets the
  full ladder. 21 carry GPS. None has an EXIF Orientation tag (the camera exports are already
  upright). Capture dates are 2026-09-05 to 2026-10-25, on 9 distinct days.
- **Orientation probe**: a synthetic JPEG with `Orientation=6` was made with an inserted APP1.
  - ffmpeg 8.1 **applies** EXIF orientation on decode (400×200 became 100×200 at width 100).
  - cwebp **ignores** it (it stayed 100×50).
  - **Decision**: decode each original once with ffmpeg to an upright, metadata-free intermediate
    PNG at the largest ladder width, then encode every AVIF, WebP and preview from that
    intermediate. Orientation is then correct for both encoders (III.4), and the 50 MP decode
    happens once per photo, not 13 times.
- **Encoders**: the same as 003 (`scripts/build-samples.sh`). AVIF uses ffmpeg `libsvtav1
  -crf 32 -preset 6 -map_metadata -1`; WebP uses `cwebp -q 78`; the preview uses `cwebp -q 40`,
  32 px on the long edge; the dominant colour is a 1×1 ffmpeg scale.
- **Timing probe**: one 38 MP X100VI photo took 0.8 s to decode to the intermediate and 2.4 s for
  all 12 derivatives. That is about 3.2 s per photo, so about 5 minutes for 93 photos on one core.
  The script runs 4 photos in parallel, so expect about 1.5–2 minutes cold. Ladder output for that
  photo was 804 KB, which is under the 2.2 MB design estimate.
- **Metadata**: `scripts/check-metadata.sh` (003) passed on the probe's 12 files. Preparation fails
  if any prepared file fails the check (FR-012).
- **Location**: the camera allowlist is read from the original by the seed command with
  MetadataExtractor (R-05), so no GPS value is ever read into a variable. Prepared files have no
  metadata, so location can't travel with them.
- **Repeatability (FR-018, SC-006)**: preparation writes one `prepared/<source-sha256-16>/` folder
  per photo, keyed by a hash of the original's bytes, with a `photo.json` describing its outputs.
  An unchanged original is skipped. A renamed file with the same bytes is reused. The prepared
  root is `.dev-prepared/`, which is gitignored.
- **Tools**: ffmpeg, cwebp, sips, shasum, jq. All are already installed and used by 003. They are
  dev-only and not app dependencies.

## R-05: Camera allowlist from the original (FR-013)

- MetadataExtractor `ImageMetadataReader.ReadMetadata(stream)` reads JPEG segment headers without
  decoding pixels (III.8). It is read from a stream, never with a full-file pixel decode.
- **Allowlist**: Make and Model (IFD0); LensModel (SubIFD); FocalLength; FocalLengthIn35mmFilm;
  FNumber; ExposureTime; ISOSpeedRatings. These map one to one onto the existing
  `Gallery.Core.Images.Exif` record and `ExifFormat`, which already format them.
- The extractor reads only these tag ids, from the two directory types that hold them. It never
  enumerates directories and never touches `GpsDirectory`. A unit test with a GPS-tagged fixture
  asserts that the resulting record has no GPS values and that the serialized photo document
  contains no `GPS`/`Latitude` key.
- **Placement**: `Gallery.Web/Seed/CameraFields.cs`, a small adapter over MetadataExtractor that
  returns the Core `Exif` record. Core stays free of packages (D-22). This adapter becomes D-10's
  server-side extractor in 008.

## R-06: Content model on Mongo for the read path

- The documents follow the content model (design §4): one `site` document, then `galleries` and
  `images`.
- **Ids**: 128-bit random values, base32 (26 chars, RFC 4648 lowercase, no padding) (D-14, II.3).
  The seed keeps ids stable across runs: it reads the existing document for each manifest key
  first, and creates a new random id only when there is none. Ids never come from titles or file
  names.
- **Manifest keys**: galleries are keyed by a `key` field in the manifest (`sept-5`, `harbour`).
  Photos are keyed by file name. A `seedKey` field on each document records that key, so a rerun
  finds the existing id. `seedKey` has a unique index.
- **Read counts** (FR-004, SC-004):
  - Home: one `site` read, using `homeCards` (IV.9).
  - Gallery page: (1) the gallery by id, (2) `images` where `_id ∈ gallery.items`, projected to
    what the grid needs. That is 2.
  - Photo page: the same two reads. The gallery gives the order, prev/next and visibility; one
    `images` `$in` read over the gallery's items gives the current photo plus the prev/next
    neighbours' thumbnail URLs for the IV.13 prefetch. That is 2 regardless of gallery size,
    because the read is limited to the current, previous and next ids.
- **Slug**: a lowercase ASCII slug of the title, with runs of non-alphanumerics collapsed to `-`
  and trimmed to 60 characters, stored on the gallery. A request whose slug differs gets a 301 to
  the stored one (FR-009).
- **Indexes**: `galleries.seedKey` unique, `images.seedKey` unique. Reads by `_id` use the
  default index.
- **Visibility**: the seed computes `homeCards` from public galleries only, in `homeOrder`. Private
  galleries render by address with `noindex` (FR-007, FR-008). Photo pages inherit the gallery's
  visibility for `noindex`.
- **Mongo version**: `mongo:8` (8.3.13 locally). Atlas M0 runs 8.0, and this feature uses nothing
  beyond 6.0 features. Docker compose and the Testcontainers image pin the same major version
  (dotnet-scaffolding note).

## R-07: Page delivery headers (FR-028, FR-029)

- **Cache-Control on public HTML**: `public, max-age=0, s-maxage=86400`. Browsers revalidate, and
  the edge keeps it for one day (D-16; IV.18 bounded lifetime). There is no `no-store` (IV.20).
  The value comes from one constant, so the hosting feature can tune it.
- **Preload hints**: a `Link` response header with `rel=preload; as=image; imagesrcset=…;
  imagesizes=…; fetchpriority=high` for the primary image. The critical CSS is inlined, so there
  is no stylesheet to preload. That satisfies IV.19's intent at the origin, and the edge turns the
  header into 103 Early Hints when hosted [cf Q3]. This is recorded as a design refinement:
  "critical CSS" preload is not needed because nothing is fetched.
- **Output cache** (IV.21 "origin output caching"): built-in `AddOutputCache` for anonymous public
  pages, with a 60-second policy keyed by path. It is safe here because content changes only by
  reseeding. The seed command prints a reminder to restart the app; there is no cross-process
  invalidation. SC-003 and SC-004 are measured with output caching **off** (a config flag), so
  they measure real page generation and real reads.

## R-08: Predictive loading and view transitions (FR-025, FR-026)

- **Speculation Rules**: an inline `<script type="speculationrules">` in PublicLayout, with
  prerender at `eagerness: moderate` for `a[href^="/g/"]` (D-17). This is JSON, not script code,
  and it works with JS disabled in the sense that nothing breaks.
- **Hover/touch preload** (IV.12): a small inline module script (about 20 lines) in PublicLayout.
  On `pointerenter`/`touchstart` on a tile that carries `data-preload-srcset`, it adds `<link
  rel=preload as=image imagesrcset imagesizes>` from that tile's data attributes. It is skipped
  when `navigator.connection?.saveData`.
  - Only photo tiles on gallery pages carry the attributes: the destination shows the same photo
    at `sizes="100vw"`.
  - Home cards don't. Their destination is a grid whose first images aren't known from the card,
    and preloading the cover at the wrong size wastes bandwidth. Speculation Rules prerender the
    whole gallery page instead (IV.11), and browsers without them navigate normally.
- **Next/prev prefetch** (IV.13): the photo page emits `data-prefetch` URLs for the previous and
  next photos' fallback WebP. The same script adds `<link rel=prefetch>` after `load`, with the
  same Save-Data guard.
- **View transitions** (IV.16): `@view-transition { navigation: auto; }` in the layout CSS, and
  `view-transition-name: img-<id>` on both the tile picture and the solo picture. The inline-style
  ban (D-21) rules out a per-image `style` attribute, so the name is emitted in a per-page
  `<style>` block of `#p-<id>{view-transition-name:img-<id>}` rules. Each Picture gets an `id`
  attribute. This adds an optional `TransitionId` parameter to Picture, Tile and SoloViewer. When
  unset, the output is byte-identical, which `PublicOutputUnchangedTests` guards.
- **Scripts on public pages**: until now the public layout had none, and a 003 test asserts that
  no script is emitted on component pages. The scripts live in PublicLayout behind a parameter that
  defaults off, so the `/ui/` PublicLayout specimen is unchanged (FR-006, SC-010), and real pages
  turn it on. V.3 is kept because every behaviour is an enhancement.

## R-09: Instrumentation and evidence (FR-030, FR-031)

- Existing: `http.request` middleware and `Server-Timing: app`.
- New: an `OpTimer` helper that logs `op.timing {op} {duration_ms} {outcome}`, the same template
  003 used, around every Mongo read (`db.site.read`, `db.gallery.read`, `db.images.read`) and
  every S3 call (`s3.head`, `s3.put`, and so on). It also appends a `Server-Timing` entry per
  datastore read (`db;dur=`).
- A request-scoped counter tags each `http.request` with `db_reads`, so SC-004 is one grep per
  event rather than a correlation. It is added as a structured field.
- `evidence/p95.sh` drives 100 requests per page type with output caching off, then computes the
  p95 of `duration_ms` per route and the `db_reads` distribution from `evidence/dev-run.log`.
  Field names were checked against the 003 middleware (`duration_ms`, `"http.request `).

## R-10: Configuration and secrets (FR-020)

- **Settings**: `Storage:ServiceUrl`, `Storage:AccessKeyId`, `Storage:SecretAccessKey`,
  `Storage:PublicBucket`, `Storage:PrivateBucket`, `Storage:ImageBaseUrl`, `Mongo:ConnectionString`,
  `Mongo:Database`. Non-secret defaults for local Mongo and the bucket names go in
  `appsettings.Development.json`. The three R2 values go in .NET user-secrets (`dotnet user-secrets
  init` on Gallery.Web, then `dotnet user-secrets set`), stored outside the repo in
  `~/.microsoft/usersecrets/`.
- A one-time `scripts/r2-keys-to-user-secrets.sh` reads the owner's `.r2-keys-copy` without
  printing values, and sets the three secrets. The owner then deletes the key file. The seed
  command is a mode of the web app (`dotnet run -- seed`), so it reads the same user-secrets with
  no second store.
- **Missing settings**: startup validation names the missing key and never prints a value (spec
  edge case). Missing storage settings fail only the seed command and storage tests. Pages still
  render without them, because they only build URLs from `ImageBaseUrl`.
- **Secret scan** (SC-008): `evidence/verify.sh` greps the committed tree (`git ls-files`) and
  every served page for the R2 account endpoint pattern, the `r2.dev` domain, and the access-key-id
  shape, and also checks that the key file is ignored. The scan never prints a matched value.

## R-11: Seed manifest and capture-date grouping (FR-015, FR-016)

- **Format**: JSON at `seed/manifest.json`, committed. JSON because .NET reads it with no package
  and jq can generate it. Shape is in `contracts/seed.md`.
- **Generation**: `scripts/seed-manifest.sh` groups the photos by capture date, read with `mdls`
  `kMDItemContentCreationDate`, the same source the survey used. It writes one public gallery per
  date in ascending date order, titled `"<Month> <day>"`, and adds the test cases:
  - "Unlisted": private, with the first 3 photos of the largest day.
  - "Coming soon": public, with no photos.
  - The first photo of the second gallery is also put in the first gallery.
  - The first photo gets a sample caption.
  It refuses to overwrite an existing manifest.
- **Reconciliation** (FR-017, FR-018): the seed reports manifest files missing from
  `dev-pictures/` and prepared photos absent from the manifest, by file name. It then deletes
  galleries and images whose `seedKey` is no longer in the manifest. Stored files are never deleted
  by the seed: content-addressed files are harmless, and the storage stays well under 10 GB.
