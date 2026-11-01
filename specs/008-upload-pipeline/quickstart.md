# Quickstart: 008 Upload Pipeline (validation guide)

Run everything from the repo root. Do not stop or restart the owner's `dotnet watch` (ports
5080/5081). Build and test into `artifacts/`. macOS has no `timeout`; use
`--blame-hang-timeout`. Contracts: `contracts/http.md`, `contracts/components.md`,
`contracts/log-events.md`. Shapes: `data-model.md`.

## Part A: setup (CLI only, Constitution VI)

No NuGet package is added (research R-17). The only setup step is vendoring the browser encoders
from pinned npm tarballs. This is a committed script, and its output is committed:

```bash
bash scripts/vendor-encoders.sh            # curl + shasum -a 512 + tar; writes src/Gallery.Web/wwwroot/admin-assets/vendor/
git status --short src/Gallery.Web/wwwroot/admin-assets/vendor   # 3 folders + VENDORED.md
```

Expected: `jsquash-avif-2.1.1/{avif_enc.js,avif_enc.wasm,LICENSE}`,
`jsquash-webp-1.5.0/{webp_enc_simd.js,webp_enc_simd.wasm,LICENSE,LICENSE.codec.md}`,
`thumbhash-0.1.1/thumbhash.js`, and `VENDORED.md`. The script exits non-zero on any integrity
mismatch.

Docker must be up for Mongo and S3Proxy:

```bash
docker info >/dev/null 2>&1 || open -a Docker    # then poll `docker info` until it answers
```

## Part B: automated validation

```bash
dotnet build --artifacts-path artifacts -warnaserror
dotnet test  --artifacts-path artifacts --blame-hang-timeout 120s
```

Expected: all tests green. Existing tests are UNMODIFIED, including `UiPagesUnchangedTests`,
`PublicOutputUnchangedTests` and `StorageTests`. The new suites cover:

| Scenario | Proves | Test file (planned) |
|---|---|---|
| GPS AVIF and GPS WebP each rejected by reader 1 alone, by reader 2 alone, and by the combined validator | FR-041, SC-002, III rationale | `ContainerParserTests`, `UploadValidatorTests` |
| embedded thumbnail (second `av01` item / `iref`), wrong width, wrong aspect, disallowed format, ICC, metadata-bearing placeholder | SC-002 | same |
| photo rejected, then staging prefix empty and public bucket unchanged | FR-026, SC-002 | `UploadFlowTests` |
| presigned URL signs `content-type;host` only; other operation, other key and expiry refused (emulator always; R2 when keys are present) | FR-042, FR-044, SC-011 | `StorageTests` (new facts) |
| over-20 MB, non-JPEG and 101st photo refused by the server | FR-005 | `UploadIntakeTests` |
| 50 concurrent register calls give exactly one open batch and ≤ 100 items | FR-007 | `UploadIntakeTests` |
| stale attemptId gives 409; Restart deletes the old attempt and reuses the master | FR-011 | `UploadLifecycleTests` |
| time advanced 121 s makes queued, processing and uploading items interrupted, while ready stays ready | FR-010, SC-005 | `UploadLifecycleTests` |
| confirm refused with any not-ready item or with zero items | FR-030 | `UploadConfirmTests` |
| confirm run twice (crash simulated mid-way) gives exactly one image per photo | FR-032 | `UploadConfirmTests` |
| staged object re-PUT after validation makes confirm fail that copy (412) and publish nothing | III.6 (research R-09) | `UploadConfirmTests` |
| home and every gallery page byte-identical before and after confirm | SC-004, FR-033 | `UploadConfirmTests` |
| seed run after an upload leaves the uploaded image untouched | FR-035 | `UploadSeedCoexistenceTests` |
| upload page with scripts off shows the notice and no visible dropzone | FR-006 | `UploadBrowserTests` |
| no credential value in `/admin/upload`, `/admin/upload/state` or any `/admin-assets/*` | FR-043, SC-012 | `UploadSecretScanTests` |
| real Chrome worker: 2 fixture JPEGs (one EXIF-6) dropped, reach Ready, confirmed, stored sizes upright | US-1, FR-014 | `UploadBrowserTests` |

Evidence scripts (committed in `evidence/`):

```bash
bash specs/008-upload-pipeline/evidence/verify.sh | tee specs/008-upload-pipeline/evidence/verify-output.txt
bash specs/008-upload-pipeline/evidence/non-vacuity.sh | tee specs/008-upload-pipeline/evidence/non-vacuity.txt
bash specs/008-upload-pipeline/evidence/p95.sh specs/008-upload-pipeline/evidence/<run>.log
```

`verify.sh` runs a live server on a free port (`--no-launch-profile --urls http://127.0.0.1:<p>`,
`ASPNETCORE_ENVIRONMENT=Development`, emulator storage). It drives the U3→U10 flow with curl and
committed fixture files, and checks:
- the presigned URL query (`X-Amz-SignedHeaders=content-type%3Bhost`);
- the 413 on an oversize register body;
- `no-store` on admin JSON and `immutable` on versioned `/admin-assets`;
- one `http.request` per call.
It stops only the PID it started.

## Part C: owner post-build live run (not automated; decisions rows 6, 7)

Prerequisites: the R2 keys in user-secrets (already present), the dev cert trusted, and the
lifecycle rule on `gallery-dev-private` `staging/` (spec Assumptions 2).

1. Run the app on `https://localhost:5081` (the only CORS-allowed origin) and sign in.
2. In Chrome, Safari macOS and iOS Safari, open `/admin/upload` and drop:
   - 40 real exports;
   - one 18–20 MB JPEG;
   - one EXIF-rotated photo;
   - one HEIC and one renamed PNG (to see the refusals).
3. Expected:
   - the refusals are listed;
   - every tile reaches Ready and the page scrolls while encoding;
   - "Add 40 photos to library" leads to "40 photos added to your library";
   - `/` and every gallery page are unchanged.
4. Capture the server log to `evidence/live-run.log` (redacted copy committed), then run `p95.sh`
   on it for SC-006, SC-007 (R2), SC-008, SC-009 (`browser.total`, per browser) and SC-010.
5. Record in `decisions.md`:
   - each browser's 20 MB time and whether iOS ran out of memory (reopens D-09 if so);
   - real placeholder and size bytes against the about 1 KB and 2.2 MB estimates (design section 10);
   - whether Safari or iOS output carried a colour profile, which reader 2 would reject
     (decisions row 9).
6. Listing check (SC-003): after confirm and after a discard, `staging/{batchId}/` lists zero
   objects (`GALLERY_R2_TESTS=1` storage tests or an `aws s3 ls`-style listing with the dev token).
7. Run `GALLERY_R2_TESTS=1 dotnet test --artifacts-path artifacts --filter Category=R2` and save the
   redacted output as `evidence/r2-tests.txt`. This includes the signed-headers, misuse and
   copy-if-match facts against R2.
