# Implementation Plan: Public Site, Local Walking Skeleton

**Branch**: `006-public-site-local` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-public-site-local/spec.md`

## Summary

This feature builds the three public pages (home, gallery, photo) on real stored content, running
locally:

- Pages read local Mongo, and images come from the dev R2 bucket through `img-dev.zacal.me`.
- The 003 components are reused, with optional parameters only.
- A dev-only script prepares the owner's 93 photos into metadata-free AVIF/WebP ladders.
- A committed, editable manifest describes the galleries.
- A `seed` mode of the web app uploads the files and writes Mongo idempotently.

Storage code is tested against S3Proxy on every run and against real R2 on demand. Every datastore
and storage call is timed in the logs, and the success criteria are computed from those logs.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.403, the latest released; research R-01)

**Primary Dependencies**: MongoDB.Driver 3.13.0, AWSSDK.S3 4.0.104.8, MetadataExtractor 2.9.3
(Web); Testcontainers + Testcontainers.MongoDb 4.16.0 (Web.Tests). Each is justified in research
R-01.

**Storage**: MongoDB 8 in a local container (`gallery-mongo`). Cloudflare R2 `gallery-dev-public`,
served at `img-dev.zacal.me`. `gallery-dev-private` is not used. Tests use Testcontainers Mongo and
S3Proxy.

**Testing**: xUnit (template). Core unit tests; Web component tests through `HtmlRenderer`;
integration tests on containers; R2 tests on demand (`Category=R2`); live `verify.sh` and `p95.sh`;
browser measurements in `evidence/browser.md`.

**Target Platform**: macOS dev machine, Docker Desktop. Public routes are mapped in all
environments, and `/ui/` stays Development-only.

**Project Type**: server-rendered web app plus class library (D-22 layout, unchanged).

**Performance Goals**:
- p95 server time under 50 ms per page type, with output cache off.
- Home: 1 read. Gallery and photo: at most 2 reads.
- 0 layout shift.

**Constraints**:
- no `style=` attributes
- public pages work with JS off
- no credential or storage endpoint in the repo or in served HTML
- prepared files carry no metadata
- the server never decodes pixels
- `/ui/` output unchanged

**Scale/Scope**: 93 photos (about 1,100 derivative files, about 75 MB), about 11 galleries, 3
public routes, 1 seed command, 4 scripts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I.1–I.6 Owner, claim, CSRF | PASS | No sign-in or writes over HTTP. `site.claimedSubject` is created null with `$setOnInsert`. A reseed never writes the claim fields (I.4), and a test proves an existing claim survives a reseed. |
| II.1 Anonymous viewing | PASS | Public routes need no cookie. No `Set-Cookie` is sent (checked in verify.sh). |
| II.2 Private = unlisted | PASS | Absent from `homeCards`; `noindex` meta and header on the gallery and its photo pages (contracts/http.md). |
| II.3 Opaque ids | PASS | 128-bit random base32, never from titles or file names. Derivative keys are content hashes (R-06). |
| II.4 Renames keep links | PASS | `/g/{id}/{slug}` with a 301 on slug mismatch. |
| II.5 Nothing public by side effect | PASS | The manifest default is private. The generator writes explicit visibility. |
| III.1 Masters private, never served | PASS | Originals are never stored anywhere (FR-019). No route serves a file from `dev-pictures/`. |
| III.2, III.5–III.7, III.10 Validated derivatives, seeded content | PASS (constitution 4.1.0, III.10) | The owner's machine prepares the derivatives. The structural check from 003 must pass at preparation. Then the seed validates every file again with MetadataExtractor before upload: format, no metadata directory, width and height (`DerivativeValidator`, probe in `evidence/probe-metadataextractor.txt`). That is two independent readers. A failing photo stores nothing. The allowlist is read by the server extractor (R-05). The seed refuses any non-dev target (FR-032). |
| III.3 Allowlist camera fields | PASS | MetadataExtractor reads only the allowlisted tag ids; GPS is never read (R-05). A test proves it with a GPS fixture. |
| III.4 Orientation before strip | PASS | ffmpeg applies EXIF orientation in the intermediate; cwebp encodes only from it. Probed (R-04). |
| III.8 No pixel work on the server | PASS | Preparation is a dev-machine script. The seed reads only EXIF segments. |
| III.9 Private bucket, scoped credential, presign, r2.dev off | PASS | Bucket-scoped token (probed: 403 outside scope); user-secrets, never committed or served; secret scan. Presign is proven in the storage tests (PUT-only, 15 min), though no route uses it until 008. r2.dev is owner-verified (R-03 deviation). |
| IV.1–IV.4 Measurement | PASS | `http.request` with `db_reads`, `op.timing` per Mongo/S3 call, `Server-Timing` `app`+`db`, and `p95.sh` from the log alone (R-09). |
| IV.6–IV.8 Ladder, placeholder, dimensions | PASS | Prepared per R-04; rendered by the 003 Picture. |
| IV.9 One-read home | PASS | `site.homeCards` (R-06). |
| IV.10 Warm on publish; tiered cache | PASS (justified N/A) | No publish action, and no edge locally. Owned by 009 and hosting. |
| IV.11–IV.14 Predictive loading, Save-Data | PASS | Speculation Rules plus a loader of under 30 lines (R-08). |
| IV.15–IV.17 Thumb-first, view transitions, priority | PASS | SoloViewer thumb layer; `#p-{id}` transition rules; first row and solo image at high priority. |
| IV.18–IV.20 Edge-cacheable, early hints, bfcache | PASS (origin side) | `Cache-Control` with `s-maxage`, a preload `Link` for the primary image (IV.19 as clarified in 4.1.0: inlined CSS needs no hint), no `no-store` or `unload`. Edge behaviour is proven by hosting. |
| IV.21 Immutable assets; origin output cache | PASS | Objects stored with `immutable` (probed through img-dev: HIT); `AddOutputCache` 60 s. Always On is a hosting setting. |
| IV.22 No framework, inlined CSS, system fonts | PASS | Unchanged from 003. |
| V.1 Scope | PASS | Pages, seed, preparation. The severable seams are listed in the spec checklist. |
| V.2 Dependencies | PASS | 5 packages, each justified (R-01). Dev tools are not app dependencies. |
| V.3 Works without JS | PASS | Scripts are enhancement only. The no-JS walk-through is in `evidence/browser.md`. |
| VI. CLI scaffolding | PASS | `dotnet add package`, `dotnet user-secrets init`, `docker run` (quickstart Part A). No hand-written project files. |
| VII.2 Design stays current | PASS | D-15 and D-17 amended in this feature (Design updates below). |
| VIII. Cost | PASS | R2 dev use is about 75 MB, inside the free 10 GB. There is no other metered service locally. |

No violations. Constitution 4.1.0 (III.10, IV.19 clarification) was amended before this
re-check, on 2026-10-31, after `/speckit-analyze` finding C1.

**Post-design re-check**: unchanged, all PASS. Phase 1 added `seedKey` (not in the design content
model) and the TransitionId mechanism. Neither weakens a principle.

## Design updates (Constitution VII.2, applied in this feature)

1. D-15 Choice: the public HTML cache header and the 60 s origin output cache (research R-07).
2. D-17 Choice: `view-transition-name` through a per-page style block (inline style is banned);
   hover preload on photo tiles only; the preload `Link` for the primary image only (R-07, R-08).
3. Decisions log rows are in `decisions.md` (this feature), including the S3Proxy choice and the
   r2.dev owner check.

Assembled and validated: `docs/design.md`, validate.sh 0 failures.

## Project Structure

### Documentation (this feature)

```text
specs/006-public-site-local/
├── plan.md  research.md  data-model.md  quickstart.md  decisions.md
├── contracts/http.md  contracts/seed.md  contracts/components.md
├── checklists/requirements.md
├── evidence/probe-s3.txt     # plan-time probe output (R-02, R-03)
├── evidence/                 # verify.sh, p95.sh, dev-run.log, browser.md (implement)
└── tasks.md                  # /speckit-tasks
```

### Source Code (repository root)

```text
.gitignore                                   # + /.dev-prepared/ (dev-pictures/ and .r2-keys* already ignored)
seed/manifest.json                           # generated once, owner-edited, committed
scripts/
├── prepare-photos.sh                        # R-04, contracts/seed.md
├── seed-manifest.sh                         # R-11
├── r2-keys-to-user-secrets.sh               # R-10, one-time
└── dev-mongo.sh                             # local Mongo container
src/Gallery.Core/
├── Ids/OpaqueId.cs                          # 128-bit base32 generate/validate (II.3)
├── Text/Slug.cs                             # title → slug (R-06)
└── Content/GalleryNav.cs                    # prev/next/neighbour ids from items (pure)
src/Gallery.Web/
├── Program.cs                               # options, Mongo, S3, output cache, public routes, `seed` mode
├── Observability/OpTimer.cs                 # op.timing + Server-Timing db entries + db_reads counter
├── Observability/RequestTimingMiddleware.cs # + db_reads field (existing file, additive)
├── Storage/StorageOptions.cs  Storage/S3ClientFactory.cs  Storage/ImageStore.cs   # R-02 settings in one place
├── Data/MongoOptions.cs  Data/Documents.cs  Data/ContentReader.cs                 # read shapes (data-model)
├── AppBuilder.cs                            # shared host setup; tests host the real app with config overrides
├── Seed/Manifest.cs  Seed/CameraFields.cs  Seed/DerivativeValidator.cs  Seed/Seeder.cs  Seed/SeedCommand.cs
├── Site/PublicEndpoints.cs  Site/PublicCache.cs  Site/ViewMapper.cs
├── Site/Pages/{HomePage,GalleryPage,PhotoPage,NotFoundPage}.razor (+ GalleryPage.razor.css)
├── Components/Atoms/Picture.razor           # + TransitionId (optional)
├── Components/Molecules/Tile.razor          # + TransitionId, Preload*, EmptyTitle (optional)
├── Components/Organisms/ContactSheet.razor  ContactSheetItem.cs  SoloViewer.razor(.css)
└── Components/Templates/PublicLayout.razor(.css)   # + Enhance, HeadExtra; @view-transition
tests/Gallery.Core.Tests/{OpaqueIdTests,SlugTests,GalleryNavTests}.cs
tests/fixtures/{orientation-6,gps-camera,not-a-jpeg}.jpg   # tiny committed fixtures (scripts/fixtures/make-exif-fixtures.py)
tests/Gallery.Web.Tests/
├── Fixtures/{MongoFixture,S3ProxyFixture,R2Fixture}.cs   # one container per collection
├── UiPagesUnchangedTests.cs                 # all /ui/ pages vs pre-006 baseline (SC-010)
├── OpTimerTests.cs  ViewMapperTests.cs  ManifestTests.cs  DerivativeValidatorTests.cs
├── PublicRoutesTests.cs                     # live host: routes, headers, read counts, visibility, redirects
├── StorageTests.cs                          # emulator always; [Trait("Category","R2")] variants on demand
├── R2ScopeTests.cs                          # Category=R2: 403 outside scope (FR-021)
├── CameraFieldsTests.cs                     # GPS fixture → no location (SC-011)
├── SeederTests.cs                           # idempotency, reconcile, cards, claim preserved, target guard (SC-006)
├── PageComponentTests.cs                    # pages, noindex, empty card, caption, no style=, script contents
└── ComponentAdditionsTests.cs               # new optional params; unset = unchanged
```

**Structure Decision**: This keeps the D-22 four-project layout. Storage, data and seed code live in
Gallery.Web as folders. A separate project was rejected in D-22, since each has one consumer. Pure
helpers (ids, slug, navigation) go to Core with no packages. The seed is a mode of the web app,
not a fifth project, so it shares configuration, user-secrets and the storage client with no
duplication.

## Complexity Tracking

No entries. The earlier III.2/III.5 deviation is resolved by constitution 4.1.0 rule III.10:
seeded content passes the same two-reader validation as uploads.
