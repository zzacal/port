# Implementation Plan: Upload Pipeline into the Library

**Branch**: `008-upload-pipeline` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-upload-pipeline/spec.md` (44 FRs, 12 SCs; clarify
done with defaults while the owner was away)

## Summary

This feature adds the owner's upload page at `/admin/upload`. It works in five steps:

1. Dropped JPEGs are refused or accepted at pick time.
2. Each original goes straight to private R2 staging with a presigned PUT that signs only
   `content-type`.
3. The server reads the stored original's header to fix the upright size and the exact set of
   widths. Only then does it issue size permissions.
4. A module Web Worker decodes with orientation and encodes AVIF and WebP with vendored jSquash
   codec files (no JS build step), plus a ThumbHash placeholder. Each size is PUT to staging.
5. On "complete" the server validates every size and the placeholder with two independent readers:
   a new container parser, and the existing `DerivativeValidator`. Confirm copies each file from
   staging with copy-if-match into the public bucket under content-hash names, and the master into
   `masters/`. It then upserts one library image per photo and clears staging.

No NuGet package is added. The three vendored browser assets are justified in research R-01/R-17.

Probe findings that shaped the design (`evidence/probe-results.txt`):
- The SDK's presigned PUT already signs only `content-type;host` (P1).
- Real jSquash output passes both readers and carries no colour profile in Chrome (P4).
- Copy-if-match refuses an object re-PUT after validation (P3).
- Uploaded images would have been deleted by the next seed run, because `ImageDoc.SeedKey`
  serialises as `""` (P7). The fix is a [SAFETY] item.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK 10.0.403 `latestPatch`, runtime 10.0.14). Admin browser
code is plain ES modules (no TypeScript, no bundler).

**Primary Dependencies**: existing AWSSDK.S3 4.0.104.8, MetadataExtractor 2.9.3, MongoDB.Driver
3.13.0. New vendored static assets: `@jsquash/avif` 2.1.1 (single-thread encoder),
`@jsquash/webp` 1.5.0 (SIMD encoder), `thumbhash` 0.1.1 (research R-01).

**Storage**: MongoDB `batches` (new, unique partial index on open) and `images` (existing). R2
`gallery-dev-private` (`staging/`, `masters/`) and `gallery-dev-public` (content-hash names).
S3Proxy in tests.

**Testing**: xunit 2.9.3. Testcontainers Mongo + S3Proxy (existing fixtures), `AdminHost` owner
sessions, `HtmlRenderer` component tests, and one Playwright Chrome test for the worker path.
The R2 suite runs on demand (`GALLERY_R2_TESTS=1`).

**Target Platform**: owner's macOS machine. Kestrel HTTPS `https://localhost:5081`, the only
CORS-allowed origin. Desktop Chrome/Safari and iOS Safari as upload clients (Safari and iOS are
verified only in the owner's live run).

**Project Type**: web app (server-rendered minimal APIs + Razor components; one admin page with a
script and a worker)

**Performance Goals**: SC-006 presign p95 ≤ 200 ms; SC-007 validation p95 ≤ 2 s/photo; SC-008
40-photo confirm ≤ 60 s; SC-009 20 MB browser prep p95 ≤ 60 s (owner live run); SC-010 upload page
with 100 items p95 ≤ 500 ms. All are read from logs (`contracts/log-events.md`).

**Constraints**:
- No pixel work on the server (III.8).
- No credential reaches the browser (III.9).
- The presigned PUT signs only content-type (FR-044).
- Public pages are byte-identical (SC-004).
- Existing tests stay unmodified.
- The owner's `dotnet watch` on 5080/5081 is never touched.

**Scale/Scope**: one owner, ≤100 photos per batch, ≤20 MB each; 11 endpoints, 1 page, 2 scripts,
2 component additions.

## Constitution Check

*GATE: checked before Phase 0 and re-checked after Phase 1 (below). Constitution v4.2.0.*

| Principle | Status | How |
|---|---|---|
| I.1–I.2, I.4–I.5 | N/A | No ownership change. |
| I.3 others refused | PASS | Every route sits in the existing `/admin` group with `RequireAuthorization("owner")` (contracts/http.md). `/admin-assets/*` is public but static and secret-free (scanned, FR-043). |
| I.6 CSRF on admin state changes | PASS | Every U3–U11 POST passes `AdminAntiforgeryFilter`. The page sends `X-CSRF-TOKEN` (existing header name). A [SAFETY] test sends each POST without the token and expects 400. |
| II.1–II.4, II.6 | PASS | No public route changes. Uploaded images join no gallery. |
| II.5 nothing public as a side effect of upload | PASS | Images are created with `galleryIds: []`. SC-004 byte-identity test on home + every gallery page around a confirm. No purge or warm (FR-033). |
| III.1 masters private | PASS | `masters/{id}.jpg` in the private bucket; no route serves it. |
| III.2 only validated derivatives public | PASS | The only writes to the public bucket are confirm copies of sizes in `validated.sizes`, with if-match on the validated ETag (research R-09). The placeholder is validated too (FR-025). |
| III.3 allowlist camera fields | PASS | `CameraFields.Read` (called, not copied) on the master prefix. Page-sent camera values are ignored and a test proves it. |
| III.4 orientation applied to pixels | PASS | `createImageBitmap(..., {imageOrientation:'from-image'})` (P4: EXIF 6 → 200×400). The server takes upright dimensions from the header orientation. AVIF `irot`/`imir` are rejected. |
| III.5 untrusted, structure-parsed, two readers | PASS | Reader 1 is the new `ContainerParser` (allowlists), reader 2 is `DerivativeValidator`; both must agree on format and width (research R-05/R-07). [SAFETY]. |
| III.6 one bad derivative rejects all | PASS | `complete` fails the whole item and deletes the attempt prefix. Test asserts the public bucket is unchanged. [SAFETY]. |
| III.7 server extracts from the master | PASS | As III.3. |
| III.8 no server pixel work | PASS | Header-only reads, a 256 KB prefix, no image library. Guard: a source scan that `Gallery.Web` references no ImageSharp/SkiaSharp/System.Drawing and calls no decode API. |
| III.9 private bucket, scoped credential, presigned PUT ≤15 min | PASS | Existing `ImageStore` (throws above 15 min). New signed-headers test pins `content-type;host` (FR-044). Misuse tests for other operation, key and expiry on emulator + R2 (FR-042). Secret scan (FR-043). [SAFETY]. |
| III.10 | N/A | Seeding unchanged. Coexistence fix in research R-10. |
| IV.1 request timing | PASS | Existing middleware covers all new routes. |
| IV.2 named timing events incl. browser timings | PASS | `contracts/log-events.md`. Browser decode/orientation/encode/placeholder/total are posted with `complete` and logged as `op.timing`. Decode and orientation form ONE `browser.decode-orient` timing, because one platform call does both (decisions row 25). |
| IV.3 Server-Timing on public pages | PASS | Unchanged. |
| IV.4 time SCs from logs | PASS | `evidence/p95.sh` over captured logs: SC-006/007/008/010 automated, SC-009 in the owner live run. |
| IV.5 perceived-perf verification | N/A | No public page change. |
| IV.6 width ladder, no upscale | PASS | `Ladder.For` single source (server); the worker encodes only the widths the server returns. Sub-320 gets the own width (design record updated). |
| IV.7 placeholder | PASS | ThumbHash colour + ≤32 px WebP, validated (P4: 110–130 B). |
| IV.8 dimensions stored | PASS | Upright width and height from the master header. |
| IV.9–IV.20 | N/A | No public rendering, cache, or edge change. |
| IV.21 content-addressed immutable | PASS | `public/{sha256-16hex}.{ext}` copied with `public, max-age=31536000, immutable`. Admin assets are versioned by hash or folder version and served `immutable` (research R-12). |
| IV.22 no client framework on public pages | PASS | Scripts only on `/admin/upload`. A test asserts public pages carry no `<script src="/admin-assets`. |
| V.1 traces to spec | PASS | Every endpoint/component maps to FRs (contracts). |
| V.2 justified dependencies | PASS | No NuGet added. Three vendored browser assets, each tied to D-09/FR-014/FR-016 (research R-17). htmx is NOT added (no requirement). |
| V.3 public pages work without JS; admin may require it | PASS | `<noscript>` notice, control hidden without JS (FR-006). |
| VI official CLIs | PASS | No project or package scaffolding. Vendoring is a committed curl/shasum/tar script, not a hand-written manifest. `dotnet build/test` only. |
| VII.2 deviations update the design | DEVIATION, handled | D-09, D-10, D-11, content-model state machine, E-03, E-08 and E-11 edited in `specs/001-*/draft/` and reassembled in THIS plan step (`validate.sh`: 0 failures). See "Design record updates". |
| VII.3 decisions recorded | PASS | `decisions.md` rows 21+. |
| VIII cost | PASS | Storage at dev scale is a few hundred MB. Class A ops per 40-photo batch: about 40×(1+12+1) PUT + 40×14 copy + lists ≈ 1.2k, far inside the free 1M/month. No new metered service. |
| Platform: R2 two buckets, presigned uploads | PASS | As D-07/D-08. |
| Workflow: real Mongo + S3 emulator, live walkthrough | PASS | Testcontainers. Live walkthrough is quickstart Part C (owner, post-build, decisions row 6). |

No Complexity Tracking entries: no MUST is crossed.

**Post-Phase 1 re-check**: unchanged, all PASS or N/A, with one handled VII.2 deviation. The
Phase 1 design added two guards and no new risk:
- the copy-if-match publication guard (strengthens III.2/III.6);
- the `seedKey` absence fix (FR-035).

## Project Structure

### Documentation (this feature)

```text
specs/008-upload-pipeline/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md, decisions.md
├── contracts/http.md, contracts/components.md, contracts/log-events.md
├── checklists/requirements.md
├── evidence/probe-results.txt, evidence/probes/*        # Phase 0 probes (P1–P8)
│   └── probes/out/*                                      # real jSquash + GPS fixture bytes
└── tasks.md                                              # /speckit-tasks (not created here)
```

### Source Code

```text
scripts/vendor-encoders.sh                       # NEW pinned npm tarball → wwwroot vendor (R-01)

src/Gallery.Core/Images/Ladder.cs                # NEW ladder rule (pure)

src/Gallery.Web/
├── AppBuilder.cs                                # register Upload services; admin-assets cache hook (R-12)
├── Upload/                                      # NEW
│   ├── UploadOptions.cs                         # data-model "Settings"
│   ├── BatchStore.cs                            # batches collection, index, atomic ops (R-08)
│   ├── EffectiveState.cs                        # interrupted-on-read (FR-010)
│   ├── ContainerParser.cs                       # reader 1 (R-05)
│   ├── MasterCheck.cs                           # header-only master read (R-06)
│   ├── UploadValidator.cs                       # both readers + agreement + placeholder (R-07)
│   ├── ConfirmService.cs                        # copy-if-match, upsert, cleanup (R-09)
│   ├── StagingKeys.cs                           # key builder (no client keys)
│   └── UploadEndpoints.cs                       # U1–U11 mapped into the /admin group
├── Admin/AdminEndpoints.cs                      # call MapUpload(admin) before the catch-all
├── Admin/Pages/UploadPage.razor                 # NEW (contracts/components.md C3)
├── Data/Documents.cs                            # ImageDoc.SeedKey/SourceHash → string? + BsonIgnoreIfNull (R-10)
├── Data/BatchDocuments.cs                       # NEW BatchDoc, BatchItemDoc, ValidatedDoc
├── Storage/ImageStore.cs                        # + GetAsync, ListAsync, CopyIfMatchAsync, HeadAsync(etag,len)
├── Components/Organisms/UploadDropzone.razor    # + MaxPhotos (optional, C1)
├── Components/Molecules/BatchItem.razor         # + data-item when ItemId set (C2)
└── wwwroot/admin-assets/
    ├── upload.js, upload-worker.js              # NEW (C4, C5)
    └── vendor/{jsquash-avif-2.1.1,jsquash-webp-1.5.0,thumbhash-0.1.1}/, VENDORED.md

tests/fixtures/upload/                           # NEW committed bytes: jSquash sizes, placeholders,
                                                 # gps-sips.avif, gps-cwebp.webp, generated bad cases
tests/Gallery.Web.Tests/
├── ContainerParserTests.cs, UploadValidatorTests.cs, LadderTests (Core.Tests)
├── UploadIntakeTests.cs, UploadLifecycleTests.cs, UploadConfirmTests.cs, UploadFlowTests.cs
├── UploadSeedCoexistenceTests.cs, UploadSecretScanTests.cs, UploadComponentAdditionsTests.cs
├── UploadBrowserTests.cs                        # Playwright: worker path + no-JS notice
└── StorageTests.cs                              # + presign signed-headers, misuse, copy-if-match facts (emulator + R2)
```

**Structure Decision**: upload server code goes in a new `Upload/` folder beside `Admin/`, `Site/`
and `Seed/`, following the per-surface layout. The page lives in `Admin/Pages/` like 007's pages.
Components stay organised by atomic level, with optional parameters only. Browser code lives in
`wwwroot/admin-assets/`, outside `/admin`, so the admin `no-store` rule does not defeat asset
caching.

## Design record updates (VII.2, done in this plan step)

Edited in `specs/001-architecture-stack-design/draft/`, then `evidence/assemble.sh` and
`evidence/validate.sh` were re-run (0 failures). `docs/design.md` is in this commit.

- **D-09**: ThumbHash input downscaled to ≤100 px (ThumbHash limit; was "the 320 px bitmap");
  sub-320 own-width size; widths come from the server; vendored codec files with no JS build;
  decode+orientation is one timing.
- **D-10**: AVIF `colr` nclx only, so any ICC is rejected (stricter than III.5 allows, matching
  reader 2). Also: one `av01` item, no `iref`, a property allowlist; the WebP ICC flag/chunk is
  rejected. Reader 2 checks an allowlist of directories. The placeholder is validated. The master
  header supplies upright SOF dimensions, with a 1 MB retry. ETags are recorded and confirm uses
  copy-if-match.
- **D-11**: 100-item limit enforced atomically; picks join the open batch; `queued → interrupted`
  (Clarification 4); interrupted computed, never stored; confirm locks the batch; explicit deletes
  plus the 7-day rule, with the server timer sweep dropped (and recorded as an alternative).
- **Content model**: `queued --> interrupted` added to the item state machine; the `state` row
  notes it is computed.
- **E-03, E-08, E-11**: updated wording (waiting items too; the page shows the open batch instead
  of redirecting; the sub-320 own-width size).
- Not changed: L-05 already makes purge conditional on an affected public gallery, and 008 affects
  none.
- `docs/roadmap.md`: the 008 "Before /speckit-plan" spike line is superseded by decisions row 6 (it
  moves to the owner's live run). Annotating the roadmap is a named task in tasks.md, together with
  008's status line on completion.

## Safety-relevant work for /speckit-tasks ([SAFETY], blocking, non-deferrable)

1. Reader 1 and reader 2 each reject the GPS AVIF and the GPS WebP on their own (FR-041), plus
   every SC-002 fixture.
2. Whole-photo rejection: the attempt prefix is deleted and the public bucket is unchanged (FR-026).
3. The presigned URL signs exactly `content-type;host` (FR-044); misuse refused on emulator + R2
   (FR-042).
4. No credential value in the page, the state JSON or any admin asset (FR-043, read from
   user-secrets inside the test, never printed).
5. Every U3–U11 POST without CSRF answers 400; without the owner session it is refused.
6. Confirm publishes only validated bytes (copy-if-match 412 test) and creates no public change
   (SC-004).
7. `seedKey` absent on uploaded images; a seed run after an upload leaves them (FR-035).
8. The server ignores page-sent camera values and dimensions (FR-022, FR-027).
9. A final non-vacuity task: one deliberate break per guard above, each must go red on its
   assertion (template `non-vacuity.sh`).

Tasks phase rules to carry: fix the log templates verbatim from `contracts/log-events.md`; size
executor batches at 6–8 tasks; mark the `AppBuilder`/`AdminEndpoints` seam a high-tier setup task;
existing tests must pass unmodified; build/test with `--artifacts-path artifacts`; never touch ports
5080/5081 or kill dotnet.

## Complexity Tracking

None.
