# Tasks: Upload Pipeline into the Library

**Input**: Design documents in `specs/008-upload-pipeline/`: spec.md, plan.md, research.md,
data-model.md, contracts/http.md, contracts/components.md, contracts/log-events.md, quickstart.md,
decisions.md, evidence/probe-results.txt.

**Source of truth**: the contracts and the research decisions (R-xx). Implement exactly what they
say. If something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: requested by the spec (FR-041 to FR-044, every SC). Each phase writes its tests in the
files it names. Tests are written in the same batch as the code they guard.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081 in their own window.** Never kill or
  restart it. Never run `pkill` or `killall` in any form, and never bind :5080 or :5081. Build and
  test into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s`
  - Parallel executors use their OWN output, `--artifacts-path artifacts/exec-<batch>`, plus a
    `--filter` on their own test classes. When a sibling's half-written file breaks your build,
    wait and retry. Never edit a file you do not own.
  - A live instance, when a task asks for one, runs the built dll directly so it has ONE pid:
    `ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/bin/Gallery.Web/debug/Gallery.Web.dll
    --urls http://localhost:5092` (5092/5093 are this feature's ports). Stop it with
    `kill <that pid>` only.
  - macOS has no `timeout` command. Use `--blame-hang-timeout`.
- **Existing tests must pass UNMODIFIED.** That means every file in `tests/` at 47de295 (the count
  is recorded by T001), including `UiPagesUnchangedTests`, `PublicOutputUnchangedTests`,
  `StorageTests`, `SeederTests`, `AdminAccessTests` and `OrganismComponentTests`. A failure there
  means your change is wrong. Fix the code, never the test or a snapshot. There is ONE stated
  requirement change: `AdminAccessTests.Owner_GetAdminUpload_returns_404`. Only the high tier
  edits it, in T035. Executors never touch it.
- **Repo paths in tests**: use `Render.RepoRoot`, which walks up to `Gallery.slnx`. Never count
  `..` levels from `AppContext.BaseDirectory`. A fixed count breaks under `artifacts/exec-*`.
- **Secrets**: never read, print, log or commit a credential. Never open `.r2-keys*` or the
  user-secrets file. Tests that need the real R2 secret read it through configuration
  (`R2Fixture`) and compare it without printing it. Error JSON and log events never carry a key, a
  presigned URL or a metadata value read from a file.
- **Never commit** `artifacts/`, `dev-pictures/`, `.dev-prepared/`, `.r2-keys*`. Executors do not
  commit. The parent commits after its own gate.
- **No new NuGet package, no npm/node in the build, no ImageSharp/SkiaSharp/System.Drawing**
  (V.2, III.8). The only new browser code is `upload.js`, `upload-worker.js` and the vendored files
  from T002. No `style=` attributes. No `innerHTML`, `outerHTML`, `insertAdjacentHTML` or
  `document.write` in admin scripts. No `<script>` on any public page.
- **File ownership**: each batch below lists the files it owns. `src/Gallery.Web/AppBuilder.cs`
  and `src/Gallery.Web/Admin/AdminEndpoints.cs` belong to T004 (high tier) and no executor edits
  them. `src/Gallery.Web/Upload/UploadSetup.cs` takes ADDITIVE edits only (one registration line
  or one route-group call per change), and at most one running batch edits it at a time
  (Batch A, then D, E and F in order).
- **Logging, verbatim** (contracts/log-events.md; `evidence/p95.sh` parses these names):
  - Timing uses the existing `OpTimer.Time` or `OpTiming.Log` exactly:
    `op.timing {op} {duration_ms} {outcome}`, with `outcome` one of `ok`, `error`, `rejected`.
  - op names used by 008 (no others): `s3.presign`, `s3.head`, `s3.get`, `s3.get-range`,
    `s3.list`, `s3.copy`, `s3.delete`, `db.batch.read`, `db.batch.create`, `db.batch.update`,
    `db.image.upsert`, `upload.master-check`, `upload.validate`, `upload.confirm.item`,
    `upload.confirm`, `browser.codec-init`, `browser.decode-orient`, `browser.resize.{w}`,
    `browser.encode.avif.{w}`, `browser.encode.webp.{w}`, `browser.placeholder`, `browser.total`.
  - Upload events use ONE logger category, `Gallery.Upload`, and exactly these templates:
    - `upload.rejected {batch} {item} {file} {reason}`. `file` is the staged name (`master.jpg`,
      `640.webp`, `placeholder`). `reason` is a fixed server string.
    - `upload.confirmed {batch} {count} {duration_ms}`
    - `upload.discarded {batch} {count}`
    - `upload.cleanup_failed {key} {error}`. `error` is the exception type name only.
    - `upload.timings_dropped {item} {count}`
  - `http.request {method} {route} {status} {duration_ms} {db_reads}` comes from the existing
    middleware. Do not add a second one.
- **[SAFETY]** marks a task that implements a Constitution I.3/I.6/II.5/III control or the
  seed-coexistence fix (plan.md "Safety-relevant work", items 1–9). These tasks are blocking and
  non-deferrable. They are never stubbed, skipped, weakened, or left as follow-ups. Each [SAFETY]
  guard gets a deliberate break in T054 and must go red on its ASSERTION.
- **Tight assertions**: assert the exact count, state, reason and stored value the contract names.
  Never write an assertion that accepts two outcomes (`a + b == n`, "matches a pattern" when the
  value is known).
- **Call, never copy**: `DerivativeValidator.Validate`, `CameraFields.Read`, `OpaqueId`,
  `ImageStore.PresignPutAsync` and the 005 components are CALLED. Duplicating their logic is a
  defect.
- Escalate (stop and report) on any design question. Executors never change the contracts. After
  two failed attempts on one task, stop and hand it back to the parent.
- Docker must be running (`docker info`; if not, `open -a Docker` and poll).

## Phase 1: Setup (high tier, parent, before any dispatch)

- [X] T001 Run the baseline gate (`dotnet build … -warnaserror`, `dotnet test …`) on 47de295.
  Write the build warning count, the test count per assembly, and the head hash to
  `specs/008-upload-pipeline/evidence/baseline.txt`. Also list there the test classes that pin
  pre-change output (`PublicOutputUnchangedTests`, `UiPagesUnchangedTests`) so later batches know
  which byte-identity guards apply.
- [X] T002 Write `scripts/vendor-encoders.sh` (research R-01). For each of `@jsquash/avif@2.1.1`,
  `@jsquash/webp@1.5.0` and `thumbhash@0.1.1` it must: curl the registry tarball, check
  `shasum -a 512` (base64) against the full `dist.integrity` string written into the script, and
  `tar -x` only the named members into
  `src/Gallery.Web/wwwroot/admin-assets/vendor/{jsquash-avif-2.1.1,jsquash-webp-1.5.0,thumbhash-0.1.1}/`.
  Write `VENDORED.md` (package, version, source URL, integrity, license, the MIT notice for
  thumbhash, the regenerate command). Exit non-zero on any mismatch. Run it and commit its output.
  Expected files are as in quickstart Part A.
- [X] T003 Copy the probe outputs into `tests/fixtures/upload/` (`gps-sips.avif`,
  `gps-cwebp.webp`, `gps-320.avif`, `gps-320.webp`, `gps-placeholder.webp`, `orient6-200.avif`,
  `orient6-200.webp`, `orient6-placeholder.webp`). Write `scripts/fixtures/upload-fixtures.sh`,
  which regenerates the generated bad cases from `tests/fixtures/gps-camera.jpg` with `cwebp`
  (dev tool, not an app dependency; decisions row 42):
  - `placeholder-exif.webp`: 32 px wide with `-metadata exif`;
  - `icc.webp`: `-metadata icc` from a JPEG that `sips --embedProfile` tagged with sRGB;
  - `fixture-6000x4000.jpg`: a synthetic large JPEG (`sips -z`) for the 2560 ladder.
  Run it and commit the outputs. Structural AVIF cases (second `av01` item, `iref`, `irot`,
  `colr prof`, `avis` brand) are built in test code by T018, not as files.
- [X] T004 **[SAFETY]** The shared-file seam (plan item 5). Create
  `src/Gallery.Web/Upload/UploadSetup.cs` with
  `public static IServiceCollection AddUpload(this IServiceCollection s, IConfiguration c)`, an
  empty body for now, and `public static RouteGroupBuilder MapUpload(this RouteGroupBuilder admin)`,
  which returns `admin` for now. Then edit, once:
  - `src/Gallery.Web/AppBuilder.cs`: `builder.Services.AddUpload(builder.Configuration);` plus
    `builder.Services.TryAddSingleton(TimeProvider.System);`. Extend the existing
    `UseStaticFiles` `OnPrepareResponse` so `/admin-assets/vendor/*` and any `/admin-assets/*`
    request with a `v` query get `public, max-age=31536000, immutable`, and other `/admin-assets/*`
    requests get `no-cache` (research R-12). `/samples` behaviour stays unchanged.
  - `src/Gallery.Web/Admin/AdminEndpoints.cs`: call `admin.MapUpload();` on the protected group,
    immediately BEFORE the H6 `{**rest}` catch-all. Upload routes therefore inherit
    `RequireAuthorization("owner")` and `AdminAntiforgeryFilter`.
  Gate: build green, all existing tests green unmodified. From now on no executor edits these two
  files.
- [X] T005 Edit `docs/roadmap.md`, section "008 Upload pipeline into the library". Replace the
  "Before `/speckit-plan`" spike bullet's status with: "Spike skipped by the owner (008
  decisions row 6). Deferred to the owner's post-build live tests (008 tasks.md, OWNER-ONLY O-02);
  the GPS fixtures and reader checks it named are built and tested inside 008 (FR-041). First
  Chrome data point: probe P4 (decisions row 36)." Keep the original text visible, struck through
  or quoted, so the history stays readable.

**Checkpoint**: build and the full existing suite green. Batches A, B and C may now run in
parallel.

## Phase 2: Foundational, Batch A (one executor, ≈8 tasks)

**Owns**: `src/Gallery.Web/Data/Documents.cs` (ImageDoc's two properties only),
`src/Gallery.Core/Images/Ladder.cs`, `src/Gallery.Web/Upload/{UploadOptions,StagingKeys,EffectiveState,BatchStore}.cs`,
`src/Gallery.Web/Data/BatchDocuments.cs`, `src/Gallery.Web/Upload/UploadSetup.cs` (additive),
`tests/Gallery.Core.Tests/LadderTests.cs`,
`tests/Gallery.Web.Tests/{UploadSeedCoexistenceTests,EffectiveStateTests,BatchStoreTests}.cs`,
`tests/Gallery.Web.Tests/Fixtures/{UploadHost,MutableTimeProvider}.cs`.

- [X] T006 **[SAFETY]** In `src/Gallery.Web/Data/Documents.cs`, change `ImageDoc.SeedKey` and
  `ImageDoc.SourceHash` to `string?` with NO initialiser (default null) and add
  `[BsonIgnoreIfNull]` to both (research R-10, decisions row 28, plan item 7). Do not touch
  `GalleryDoc.SeedKey` (decisions row 43). Every existing writer sets these explicitly, so existing
  tests stay green. Fix any nullable warning at the consumer with a null check, never with `!`.
- [X] T007 **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadSeedCoexistenceTests.cs`
  (FR-035, plan item 7):
  - `ImageDoc_without_seed_fields_serialises_no_seedKey`: `new ImageDoc{…}.ToBsonDocument()` has
    no `seedKey` and no `sourceHash` element.
  - `Seed_run_leaves_an_uploaded_image_untouched`: use the same class shape, fixtures and
    `Seeder` construction as `SeederWithS3Tests` in `SeederTests.cs`. Insert an `ImageDoc` with no
    seed fields (as confirm will write it) into a fresh DB, run a real `Seeder.RunAsync`, then
    assert that the document still exists and that its `ToJson()` is identical to before. Do not
    copy seeder logic into the test. If the seeder cannot be driven this way, escalate.
- [X] T008 [P] Create `src/Gallery.Core/Images/Ladder.cs`: `public static class Ladder` with
  `Widths = [320,640,960,1280,1920,2560]`, `IReadOnlyList<int> For(int uprightWidth)` and
  `int HeightFor(int w, int W, int H) => (int)Math.Round((double)w * H / W)` (data-model "Ladder
  rule"). Write `tests/Gallery.Core.Tests/LadderTests.cs` covering 200 → [200], 320 → [320],
  1000 → [320,640,960], 2560 → all six, 6000 → all six, 0 or a negative width → ArgumentOutOfRange,
  and HeightFor(640, 6000, 4000) = 427.
- [X] T009 Create `src/Gallery.Web/Upload/UploadOptions.cs`, config section `Upload`, with the keys
  and defaults of data-model "Settings". Startup validation: `PresignMinutes` must be in 1..15 and
  every other value > 0, or startup throws with the key name. Create
  `src/Gallery.Web/Upload/StagingKeys.cs`:
  - `Master(b,i,a)`, `Size(b,i,a,w,ext)`, `AttemptPrefix(b,i,a)`, `ItemPrefix(b,i)` and
    `BatchPrefix(b)` per data-model "Staged keys". Every key is built from stored ids; all ids
    must pass `OpaqueId.IsValid`, or the method throws.
  Register `UploadOptions` in `AddUpload`. (The best-effort delete helper is Batch B's, T014.)
- [X] T010 Create `src/Gallery.Web/Data/BatchDocuments.cs` (`BatchDoc`, `BatchItemDoc`,
  `MasterDoc`, `ValidatedDoc`, `ValidatedSizeDoc`, `PlaceholderDoc`) with the BSON names of
  data-model.md exactly. Create `src/Gallery.Web/Upload/EffectiveState.cs` with
  `static string Of(BatchItemDoc item, DateTimeOffset now, TimeSpan timeout)`: it returns
  `interrupted` per data-model "Effective state", otherwise the stored state. Write
  `tests/Gallery.Web.Tests/EffectiveStateTests.cs`: queued, processing and uploading at 119 s stay
  as stored and at 121 s become `interrupted`; succeeded and failed at 10 min stay as stored.
- [X] T011 Create `src/Gallery.Web/Upload/BatchStore.cs` (research R-08), scoped,
  `(IMongoDatabase, OpTimer, TimeProvider, UploadOptions)`. Every Mongo call goes through
  `OpTimer.Time` with `db.batch.read` (isDbRead: true), `db.batch.create` or `db.batch.update`.
  - The lazy unique partial index `batches_open_unique` is created on first use, never at startup.
  - `GetOpenAsync()`.
  - `GetOrCreateOpenAsync()`: find, insert on miss, re-read on DuplicateKey.
  - `AppendAsync(batchId, items)`: the `items.{100−k}` `$exists:false` filter plus
    `confirmStartedAt: null`, then re-read and split into accepted and refused.
  - `UpdateItemAsync(batchId, itemId, attemptId, update)`: a positional filter on itemId AND
    attemptId; returns matched or not.
  - `PullItemAsync`, `TryStartConfirmAsync`, `MarkConfirmedAsync(count)`, `MarkDiscardedAsync`,
    `GetByIdAsync`.
  Register it scoped in `AddUpload`.
- [X] T012 Write `tests/Gallery.Web.Tests/BatchStoreTests.cs` (`[Collection("mongo")]`, real
  Mongo):
  - 50 concurrent `GetOrCreateOpenAsync` calls on a fresh DB give exactly 1 document with
    `state:"open"`, and all 50 results share its `_id` (FR-007, US5 AS-3).
  - 50 concurrent `AppendAsync` calls of 3 items each against one batch leave exactly 100 items,
    and the refused count sums to exactly 50.
  - `UpdateItemAsync` with a stale attemptId matches nothing and changes nothing.
  - `AppendAsync` after `TryStartConfirmAsync` refuses every item.
  - A second open insert throws DuplicateKey (the index exists after first use).
- [X] T013 Create `tests/Gallery.Web.Tests/Fixtures/MutableTimeProvider.cs` (a `TimeProvider`
  whose `GetUtcNow()` returns a settable value; no NuGet, decisions row 41). Create
  `tests/Gallery.Web.Tests/Fixtures/UploadHost.cs`, a wrapper over `AdminHost.CreateAsync(mongo,
  fakeGoogle, overrides, configure)`:
  - Storage overrides point at `S3ProxyFixture.StorageOptions` (service URL, `local` /
    `localsecret`, buckets `pub` / `priv`).
  - `configure` replaces `TimeProvider` with a `MutableTimeProvider` exposed as `Clock`.
  - It signs in `owner-1`, then caches the CSRF token from `TokenAsync("/admin")`.
  - Helpers: `PostJsonAsync(path, object body, bool csrf = true)` (sends `X-CSRF-TOKEN`),
    `GetJsonAsync(path)`, `PutToPresignedAsync(url, byte[] bytes, string contentType)` (a plain
    `HttpClient` that sends exactly one header, `Content-Type`), and `ListAsync(bucket, prefix)`
    through the S3 SDK.
  - It exposes `LogLines`.
  Prove it once: a test in `BatchStoreTests.cs` builds an `UploadHost`, GETs `/admin`, gets 200,
  and the token is non-empty. This proves the fixture before anything builds on it.

## Phase 3: Foundational, Batch B: storage (one executor, parallel with A and C)

**Owns**: `src/Gallery.Web/Storage/ImageStore.cs` (additive methods only; existing methods stay
byte-unchanged), `tests/Gallery.Web.Tests/UploadStorageTests.cs`. `StorageTests.cs` stays
unmodified: the new facts go in a new file with the same abstract/emulator/R2 pattern
(decisions row 37).

- [X] T014 Add to `src/Gallery.Web/Storage/ImageStore.cs`, each method logging `op.timing` like
  the existing ones:
  - `HeadAsync(bucket,key)` → `(bool exists, long length, string? etag)` (op `s3.head`);
  - `GetAsync(bucket,key, long maxBytes)` → `(byte[] bytes, string etag)` (op `s3.get`). It
    throws when the length exceeds `maxBytes`.
  - `ListAsync(bucket,prefix)` → keys, paginated (op `s3.list`);
  - `CopyIfMatchAsync(srcBucket,srcKey,dstBucket,dstKey, etag, contentType, cacheControl)` →
    `CopyOutcome { Copied, PreconditionFailed }` (op `s3.copy`, outcome `ok` or `rejected`). It
    sets `ETagToMatch` and `MetadataDirective=REPLACE`.
  - `static Task TryDeleteAsync(Func<Task> delete, string key, ILogger log)`, which logs
    `upload.cleanup_failed {key} {error}` on any exception and never rethrows (FR-036). It is
    the ONLY place that template is logged.
  - `DeletePrefixAsync(bucket,prefix, ILogger)`, which lists and then deletes each key through
    `TryDeleteAsync`, and never throws.
  `PresignPutAsync` is NOT changed (research R-03).
- [X] T015 **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadStorageTests.cs`: an abstract
  `UploadStorageTests` plus `EmulatorUploadStorageTests` (`[Collection("s3")]`) and
  `R2UploadStorageTests` (`[Trait("Category","R2")]`, `IClassFixture<R2Fixture>`, skipping
  exactly like `R2StorageTests`). Fact `Presign_signs_content_type_and_host_only` (FR-044, plan
  item 3) parses the URL query and asserts:
  - `X-Amz-SignedHeaders` equals `content-type;host` exactly;
  - `X-Amz-Expires` ≤ 900;
  - no query key starts with `x-amz-checksum`, `x-amz-sdk-checksum` or `x-amz-meta-`.
  A PUT with only `Content-Type` answers 200.
- [X] T016 **[SAFETY]** In the same file (FR-042, SC-011), for both the emulator and R2:
  - GET on the PUT URL → 403;
  - PUT of the same URL with the path changed to another key → 403;
  - PUT with a different content type → 403;
  - PUT 3 s after a 1-second expiry → 403;
  - after each refused attempt, `HeadAsync` shows the target key absent or unchanged.
- [X] T017 In the same file:
  - `CopyIfMatchAsync` with the current ETag copies and sets the destination
    `Cache-Control: public, max-age=31536000, immutable`;
  - after a re-PUT of the source, the same call returns `PreconditionFailed` and the destination
    stays absent (probe P3, decisions row 27);
  - `GetAsync` ETag equals `HeadAsync` ETag;
  - `ListAsync` returns exactly the keys put under a prefix;
  - `DeletePrefixAsync` leaves zero keys.

## Phase 4: User Story 2, nothing that carries metadata reaches visitors (P1): readers, Batch C

**Goal**: the two readers and the combined validator, proven on real and hostile files.
**Independent test**: unit tests over fixture bytes, no browser and no host.
**Owns**: `src/Gallery.Web/Upload/{ContainerParser,MasterCheck,UploadValidator}.cs`,
`tests/Gallery.Web.Tests/Fixtures/UploadFixtures.cs`,
`tests/Gallery.Web.Tests/{ContainerParserTests,MasterCheckTests,UploadValidatorTests}.cs`.
Runs in parallel with A and B.

- [X] T018 [US2] Create `tests/Gallery.Web.Tests/Fixtures/UploadFixtures.cs`:
  - `Bytes(name)` loads `tests/fixtures/upload/*` and `tests/fixtures/*` via `Render.RepoRoot`;
  - a minimal ISO-BMFF box writer (`Box(type, payload)`, `FullBox`) and helpers that take the real
    `orient6-200.avif` and return mutated copies: an added second `infe(av01)`, an added `iref`,
    an added `irot` / `imir` / `clap` property, `colr` changed to `prof`, the `ftyp` brand changed
    to `avis`, an added `infe(mime)`, an unknown top-level box, and a truncated file;
  - a RIFF writer for WebP cases: an added `ICCP`, `EXIF` or `XMP ` chunk, VP8X flags set, `ANIM`,
    an unknown chunk, a wrong RIFF length.
- [X] T019 [US2] **[SAFETY]** Create `src/Gallery.Web/Upload/ContainerParser.cs` (reader 1;
  research R-05, decisions row 29; plan item 1). It is static and pure, with
  `ParseResult Parse(ReadOnlySpan<byte> file)` → `(bool Ok, string? Format, int Width, int Height,
  string? Reason)`. It applies the AVIF and WebP allowlists EXACTLY as R-05 lists them, handles
  box size 1 (largesize) and 0 (to end), and bounds-checks every length so no input throws
  (malformed input → `Ok=false`, reason `"malformed {box}"`). Reasons are fixed strings naming the
  box or chunk (e.g. `"AVIF item Exif"`, `"WebP EXIF chunk"`, `"VP8X EXIF flag"`), never a value
  read from the file.
- [X] T020 [US2] **[SAFETY]** Write `tests/Gallery.Web.Tests/ContainerParserTests.cs`:
  - accepts `orient6-200.avif` (avif 200×400), `orient6-200.webp`, `gps-320.avif` and
    `gps-320.webp` (jSquash output, no metadata), and both placeholders;
  - `Reader1_alone_rejects_gps_avif` (`gps-sips.avif`, reason contains `Exif`) and
    `Reader1_alone_rejects_gps_webp` (`gps-cwebp.webp`, reason names EXIF), for FR-041;
  - one rejecting fact per T018 mutation, with its exact reason;
  - `icc.webp` rejected;
  - a fuzz loop: 2 000 random truncations or byte flips of the valid files never throw.
- [X] T021 [US2] Create `src/Gallery.Web/Upload/MasterCheck.cs` (research R-06). It is pure over
  bytes: `MasterResult Check(byte[] prefix, long length, long maxBytes)` →
  `(Ok, UprightWidth, UprightHeight, Exif? Camera, NeedsMoreBytes, Reason)`:
  - File Type must be JPEG, or the reason is "not a JPEG";
  - a missing SOF or a reader exception gives `NeedsMoreBytes` (the caller retries once with
    1 MB), and after the retry the reason is "unreadable JPEG header";
  - `length > maxBytes` gives "{x.x} MB, over the 20 MB limit";
  - orientation 5–8 swaps width and height;
  - `CameraFields.Read` is CALLED inside try/catch, so a throw means `Camera=null`
    (decisions row 17).
  No pixel decode.
- [X] T022 [US2] Write `tests/Gallery.Web.Tests/MasterCheckTests.cs`:
  - `orientation-6.jpg` → 200×400;
  - `gps-camera.jpg` → 400×200 with camera fields, and no GPS value anywhere in the result's
    serialised JSON;
  - `camera-subifd.jpg` → camera fields present;
  - `not-a-jpeg.jpg` → "not a JPEG";
  - a 200-byte prefix of `fixture-6000x4000.jpg` → `NeedsMoreBytes`;
  - the full prefix → 6000×4000;
  - length 20 971 521 → the over-limit reason.
- [X] T023 [US2] **[SAFETY]** Create `src/Gallery.Web/Upload/UploadValidator.cs` (research R-07,
  plan item 1). `SizeVerdict ValidateSize(byte[] bytes, string storedName, int expectedWidth,
  int masterW, int masterH)` runs BOTH readers every time and returns
  `(Ok, Reader1Ok, Reader2Ok, Format, Width, Height, Sha256Hex16, Reason)`:
  - reader 1 is `ContainerParser.Parse`;
  - reader 2 is `DerivativeValidator.Validate(stream, ext, expectedWidth, masterW/(double)masterH)`,
    CALLED unchanged;
  - `Ok` requires both readers, reader 1's format equal to the stored extension, reader 1's width
    equal to `expectedWidth`, and `|h − Ladder.HeightFor| ≤ 2`;
  - `Reason` is `"{storedName}: {first failing reader's reason}"`.
  `PlaceholderVerdict ValidatePlaceholder(string color, string dataUri, UploadOptions o)` checks:
  - `color` matches `^#[0-9a-f]{6}$`;
  - the URI prefix is exactly `data:image/webp;base64,`;
  - the decoded bytes are ≤ `PlaceholderMaxBytes`;
  - reader 1 says WebP with 1 ≤ each side and long edge ≤ `PlaceholderMaxEdge`;
  - reader 2 is called with reader 1's width and aspect.
  No IO in this class.
- [X] T024 [US2] **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadValidatorTests.cs` (FR-041,
  SC-002):
  - `Reader2_alone_rejects_gps_avif` and `Reader2_alone_rejects_gps_webp` call
    `DerivativeValidator.Validate` directly;
  - `Combined_rejects_gps_avif` and `Combined_rejects_gps_webp` assert `Ok=false`,
    `Reader1Ok=false` AND `Reader2Ok=false`;
  - accepts `gps-320.avif` at width 320 against a 400×200 master;
  - wrong width (a 320 file stored as `640.webp`), wrong aspect (a master of 400×400), a
    disallowed format (a JPEG stored as `.avif`) and the embedded thumbnail (second `av01`) are
    each rejected;
  - the placeholder: `orient6-placeholder.webp` accepted; `placeholder-exif.webp`, a 64 px WebP,
    a non-WebP data URI, an over-1536-byte URI and colour `red` are each rejected.

**Checkpoint (parent)**: merge A+B+C; delete `artifacts/exec-*`; full build and full suite
green; grep the tree for each [SAFETY] mechanism in T006, T019 and T023 (`BsonIgnoreIfNull` on
both ImageDoc properties, the `iref` and `Exif` rejections, the `DerivativeValidator.Validate`
call in UploadValidator).

## Phase 5: User Story 1, the owner uploads a batch (P1) 🎯 MVP: server path, Batch D

**Goal**: U2–U5 and U7 work end to end over HTTP with committed fixture bytes; rejections clean
up.
**Independent test**: `UploadFlowTests` registers, PUTs, checks the master, PUTs sizes and
completes, with no browser.
**Owns**: `src/Gallery.Web/Upload/UploadEndpoints.cs`, `UploadSetup.cs` (additive: call
`MapUploadEndpoints`), `tests/Gallery.Web.Tests/UploadFlowTests.cs`.

- [X] T025 [US1] Create `src/Gallery.Web/Upload/UploadEndpoints.cs` with
  `MapUploadEndpoints(RouteGroupBuilder admin)` (called from `UploadSetup.MapUpload`). Implement
  the shared rules of contracts/http.md:
  - `OpaqueId.IsValid` on path ids, or 404;
  - an item outside the OPEN batch → 404;
  - 409 `stale-attempt`, `not-active` and `confirm-in-progress` with the exact error JSON shape;
  - per-endpoint `.WithMetadata(new RequestSizeLimitAttribute(n))` at 128 KiB, 16 KiB and 4 KiB
    (research R-15);
  - messages never echo a key or a URL.
  Implement U2 `GET /admin/upload/state` with the EFFECTIVE state from `EffectiveState.Of` and
  `TimeProvider`.
- [X] T026 [US1] Implement U3 `POST /admin/upload/items` exactly per contracts/http.md (steps 1–4,
  refusal strings verbatim). More than `MaxPhotosPerBatch` entries → 400. Name sanitising
  (data-model `fileName` rule) strips path separators and control chars. Each accepted item gets
  `PresignPutAsync(priv, StagingKeys.Master(…), "image/jpeg", PresignMinutes)`.
- [X] T027 [US1] Implement U4 `POST /admin/upload/items/{itemId}/master`:
  - `HeadAsync`, then `GetRangeAsync` of `MasterHeaderBytes`, a single 1 MB retry on
    `NeedsMoreBytes`, then `MasterCheck.Check`, all inside `OpTimer.Time("upload.master-check")`;
  - on success: set `master` and `heartbeatAt`, and presign each `Ladder.For(width)` × {avif, webp};
  - on rejection: `failed` + reason, `DeletePrefixAsync` of the attempt prefix, 422, and one
    `upload.rejected … master.jpg {reason}`;
  - a missing object → 422 "original not found", with the item state unchanged.
- [X] T028 [US1] **[SAFETY]** Implement U5 `progress` (forward-only, `heartbeatAt`, 409 on a
  terminal or effective-interrupted item) and U7 `complete` (contracts/http.md U7; FR-022–FR-029;
  plan items 2 and 8):
  1. Log the accepted browser timings with `OpTiming.Log(op, ms, "ok")` (name regex and range of
     R-16). Log `upload.timings_dropped` for the rest.
  2. Run the whole validation inside `OpTimer.Time("upload.validate")`: master re-check (same
     length, ETag and dimensions); every expected size present (`HeadAsync`), fetched with
     `GetAsync(max 8 MiB)` (contracts/http.md U7) and checked by `UploadValidator.ValidateSize`; then the placeholder.
  3. Pass: store `validated` with dimensions FROM `master` and camera FROM `MasterCheck` on the
     stored master prefix (the request's `camera` and any width or height field are never read),
     the size keys `{sha256-16hex}.{ext}`, the ETags and byte counts; set `succeeded`.
  4. Fail: `failed` + reason, `DeletePrefixAsync` of the attempt prefix, 422, `upload.rejected`
     with the failing file name; ZERO writes to the public bucket on this path.
- [X] T029 [US1] **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadFlowTests.cs`
  (`[Collection("s3")]` + MongoFixture, `UploadHost`):
  - **Happy path**: register `gps-camera.jpg`, PUT the master, U4 returns exactly the 320 size,
    PUT `gps-320.avif` and `gps-320.webp`, then U7 with `gps-placeholder.webp` → 200
    `succeeded`. The stored `validated` has 400×200, the camera fields, and no `gps` substring in
    the batch document's JSON.
  - `Complete_ignores_page_camera_and_dimensions` (plan item 8, FR-022, FR-027): U7 also sends
    `camera:{make:"Evil"}` and `width:9999` → the stored camera make equals the master's, and the
    stored width is 400.
  - **Rejections** (US2 AS-1/2/3/6, FR-026): one test per case:
    - GPS AVIF stored as `320.avif`: master `gps-camera.jpg` (400 wide → ladder [320]), and the
      size is `gps-320.avif` with an `Exif` item added by the T018 helper (`gps-sips.avif` itself
      is 400 wide, which is not a ladder width, so it is used only in the reader unit tests);
    - GPS WebP stored as `320.webp`: `gps-320.webp` with an `EXIF` chunk and the VP8X EXIF flag
      added by the T018 RIFF helper;
    - wrong width;
    - a missing size;
    - `placeholder-exif.webp`.
    Each asserts 422, item `failed` with the reason naming the file, `ListAsync(priv, attempt
    prefix)` empty, `ListAsync(pub, "")` identical to before, and one `upload.rejected` line.
  - A stale attemptId on U4, U5 or U7 → 409 `stale-attempt`; an unknown or deleted itemId → 404
    (FR-020).
  - U4 presigns ONLY the server's ladder: for the 400-wide master the response has exactly one
    size (320) and two URLs; for `fixture-6000x4000.jpg` exactly six widths (FR-020, FR-023).
  - Timing and log lines (FR-037–FR-040), asserted on `LogLines` of the happy path:
    - one `http.request POST /admin/upload/items/{itemId}/complete 200` line;
    - one `op.timing upload.validate … ok` and one `op.timing upload.master-check … ok` per U4/U7;
      at least one each of `s3.presign`, `s3.head`, `s3.get`, `s3.get-range`, `db.batch.update`;
    - U7 sent with `timings` `[{op:"browser.total",ms:812.4},{op:"browser.encode.avif.320",ms:5},
      {op:"evil",ms:1},{op:"browser.total",ms:-1}]` → exactly the two valid `op.timing browser.*`
      lines and one `upload.timings_dropped {item} 2`;
    - each rejection case logs one `upload.rejected` whose `file` is the failing name and whose
      line contains no value read from the file (no coordinate, camera make or model string), no
      key and no URL (FR-040).

## Phase 6: User Story 1 (cont.): confirm and the page, Batch E

**Owns**: `src/Gallery.Web/Upload/{ConfirmService,AdminAssets,UploadPageModel}.cs`,
`UploadEndpoints.cs` (U1, U10 additions only), `UploadSetup.cs` (additive),
`src/Gallery.Web/Admin/Pages/UploadPage.razor` (+ `.razor.css` if needed),
`src/Gallery.Web/Components/Organisms/UploadDropzone.razor`,
`src/Gallery.Web/Components/Molecules/BatchItem.razor`,
`tests/Gallery.Web.Tests/{UploadConfirmTests,UploadComponentAdditionsTests,UploadPageTests}.cs`.

- [X] T030 [US1] **[SAFETY]** Create `src/Gallery.Web/Upload/ConfirmService.cs` and map U10
  (research R-09, contracts/http.md U10, FR-030–FR-033, plan item 6):
  - Gate with `TryStartConfirmAsync`; otherwise 409 `not-ready` with `notReady` = the count of
    non-succeeded items (zero items → 409 as well).
  - Per item, inside `OpTimer.Time("upload.confirm.item")`, with at most 8 storage operations in
    flight:
    - `CopyIfMatchAsync` of each size to `pub/{key}` with the validated ETag, the content type and
      `public, max-age=31536000, immutable`;
    - the master to `priv/masters/{itemId}.jpg` with if-match;
    - a `PreconditionFailed` follows contracts/http.md U10 "Copy refused by if-match" exactly: that
      item `failed` ("changed after validation"), its item prefix deleted, NO image for it; the
      other items continue and are `$pull`ed once their image exists; at the end
      `confirmStartedAt` is cleared and the answer is 409 with `added`/`failed` counts;
    - upsert `ImageDoc{_id=itemId}` with `$setOnInsert` per data-model "Library image" (no seed
      fields, `galleryIds: []`) through `OpTimer.Time("db.image.upsert")`;
    - `DeletePrefixAsync` of the item prefix.
  - Then `MarkConfirmedAsync(count)` and `upload.confirmed {batch} {count} {duration_ms}`, all
    inside `OpTimer.Time("upload.confirm")`.
  - No purge or warm call anywhere (FR-033). 200 `{batchId,count,redirect}`.
  - Crash seam for tests: `ConfirmService` takes an optional `Func<string, Task>? afterItem`
    (constructor parameter, default null; DI never sets it). It runs after each item's upsert. A
    test passes one that throws after the first item to simulate a crash.
- [X] T031 [US1] **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadConfirmTests.cs`:
  - Confirm with one item still uploading, and with zero items → 409 `not-ready`, `notReady`
    exact.
  - Happy confirm of 2 items: 2 `images` documents with `_id` = itemId, the title from the file
    name, `galleryIds` empty, no `seedKey` element; each `pub` key exists with the immutable
    `Cache-Control`; `priv/masters/{id}.jpg` exists; `ListAsync(priv, "staging/{batchId}/")` is
    empty (SC-003); the batch is `confirmed` with `confirmedCount` 2.
  - `Confirm_twice_after_a_simulated_crash`: build `ConfirmService` with an `afterItem` that throws
    after item 1, run it (it throws, the batch stays open with `confirmStartedAt` set), then call
    U10 → 200, exactly 2 images, no duplicate public keys (FR-032). A further U10 on the confirmed
    batch → 200 with count 2 and still 2 images.
  - `Reput_after_validation_publishes_nothing` (plan item 6): 2 items; re-PUT item 2's `320.avif`
    through the still-valid presigned URL after `succeeded` → U10 answers 409
    `changed-after-validation` with `added:1, failed:1`. Item 2's public keys are absent, it has no
    image, it is `failed` with "changed after validation", and its item prefix is empty. Item 1 has
    its image. The batch is open with only item 2 and no `confirmStartedAt`, and U9 delete on item 2
    then works (no lock-out).
  - Timing: the happy confirm logs one `op.timing upload.confirm … ok`, one
    `upload.confirm.item` per photo, `s3.copy` lines, a `db.image.upsert` per photo, and one
    `upload.confirmed {batch} 2 {duration_ms}` (FR-038, FR-040).
  - U10 with a random batchId → 404; U11 on a confirming batch → 409 `confirm-in-progress`.
  - `Public_pages_byte_identical_around_confirm` (SC-004): seed public content with the
    `PublicRoutesTests` shape and GET `/` and every gallery page before and after a confirm → the
    bodies are byte-identical.
  - `Seed_run_after_confirm_leaves_uploaded_images` (FR-035): a real `Seeder.RunAsync` after
    confirm, with the `SeederWithS3Tests` setup → both images unchanged (`ToJson()` equal).
- [X] T032 [P] [US1] Component additions (contracts/components.md C1, C2):
  - `UploadDropzone.MaxPhotos` as an EXPRESSION (`@PhotosMarkup`) inside the existing span;
  - `BatchItem` root `@attributes="RootAttrs"`;
  - no new `@if` beside existing markup.
  Write `tests/Gallery.Web.Tests/UploadComponentAdditionsTests.cs`:
  - MaxPhotos=100 renders the exact FR-002 line;
  - the default renders byte-identical to `Render.Html<UploadDropzone>(new{})`;
  - `ItemId="abc"` renders `data-item="abc"`, and the default renders no `data-item`.
  Then run `UiPagesUnchangedTests` and `OrganismComponentTests` UNMODIFIED, and both must be green.
- [X] T033 [US1] Create `src/Gallery.Web/Upload/AdminAssets.cs` (a singleton computing
  `sha256-8hex` of `upload.js` and `upload-worker.js` from the web root at startup; a missing file
  gives the version `dev`), `src/Gallery.Web/Upload/UploadPageModel.cs`, and
  `src/Gallery.Web/Admin/Pages/UploadPage.razor` exactly per contracts/components.md C3:
  - `<noscript>`, `upload-batch hidden` with the data attributes, the hidden form, the
    dropzone, the ContactSheet of BatchItems in effective state, the footer mapping, and the
    templates;
  - the limits come from the SAME `UploadOptions` instance.
  Map U1 `GET|HEAD /admin/upload` with `?confirmed=` (contracts/http.md U1) using one
  `db.batch.read`. `upload.js` and `upload-worker.js` may be empty placeholder files until
  Batch G; T033 creates them if absent.
- [X] T034 [US1] Write `tests/Gallery.Web.Tests/UploadPageTests.cs`:
  - owner GET `/admin/upload` → 200 with `no-store`, the `JPEG only · up to 20 MB each · up to 100
    photos per batch` line, the noscript text, `upload-batch` carrying `hidden`, one
    `<script type="module" src="/admin-assets/upload.js?v=`, all 6 `data-tile` templates and 4
    `data-footer` templates;
  - no `X-Amz-` and no `/priv/` or `staging/` substring (FR-021);
  - with `Upload:MaxFileBytes=10485760` → "up to 10 MB" and `data-max-bytes="10485760"`
    (FR-002 single source);
  - a seeded 100-item batch renders 100 `data-item` tiles in one `db.batch.read`, and the
    `http.request` line is present;
  - `?confirmed={id}` of a confirmed batch shows "2 photos added to your library" and the
    `/admin/library` link, while a random id shows the empty page;
  - `GET /admin-assets/upload.js?v=x` → `immutable`; without `v` → `no-cache`;
    `/admin-assets/vendor/jsquash-avif-2.1.1/avif_enc.wasm` → `application/wasm` + `immutable`.
- [X] T035 (high tier, parent) Requirement change, FR-001: in
  `tests/Gallery.Web.Tests/AdminAccessTests.cs`, rename `Owner_GetAdminUpload_returns_404` to
  `Owner_GetAdminUpload_returns_200_and_library_still_404`. Assert 200 for `/admin/upload` and
  404 for `/admin/library`. Leave every other assertion in that file untouched. State this in the
  commit body and in decisions row 38.

**Checkpoint (parent)**: full suite green. Grep for: `ETagToMatch` in ConfirmService; no
`Purge`/`Warm` in `src/Gallery.Web/Upload/`; `$setOnInsert` without `seedKey`; no write to
`PublicBucket` in `UploadEndpoints.cs`.

## Phase 7: User Stories 3, 4, 5 and access, Batch F (one executor, after Batch E)

**Owns**: `UploadEndpoints.cs` (U6, U8, U9, U11 additions),
`tests/Gallery.Web.Tests/{UploadIntakeTests,UploadLifecycleTests,UploadResumeTests,UploadAccessTests}.cs`.

- [X] T036 [US3] Write `tests/Gallery.Web.Tests/UploadIntakeTests.cs` (US3 AS-4/5, FR-005):
  - register with a `type:"image/png"` entry, a `size:20971521` entry and an empty name → each is
    refused with the exact contract string while the valid entries are accepted in the same call;
  - a batch of 95 plus 10 → 5 accepted and 5 "batch is full (100 photos)";
  - 101 entries in one request → 400;
  - a 200 KB body → 413;
  - a staged master that is a PNG renamed (`not-a-jpeg.jpg` PUT as image/jpeg) → U4 422 "not a
    JPEG", item `failed`, attempt prefix empty;
  - a staged master over `MaxFileBytes` (host override `Upload:MaxFileBytes=1000`) → 422 with the
    size reason.
- [X] T037 [US4] Implement in `UploadEndpoints.cs`, exactly per contracts/http.md, through
  `BatchStore` and `DeletePrefixAsync` (FR-011–FR-013, FR-036):
  - U6 `fail` (reason ≤ 200 chars, control chars stripped);
  - U8 `restart` (new attemptId; master `CopyIfMatchAsync` to the new attempt when `master` is
    set; old attempt prefix deleted; reset fields);
  - U9 `delete` (`$pull`, item prefix deleted, idempotent);
  - U11 `discard` (batch prefix deleted, `discarded`, `upload.discarded {batch} {count}`).
  All four answer 409 `confirm-in-progress` while confirming.
- [X] T038 [US4] Write `tests/Gallery.Web.Tests/UploadLifecycleTests.cs`:
  - U6 → `failed` with the reason, other items untouched (FR-017);
  - restart with a stored master → `needsFile:false`, sizes returned, the master present under
    the new attempt, the old attempt prefix empty, and the old attemptId on U5 → 409; a PUT to an
    old size URL lands under the old prefix and is never read (complete still validates the new
    attempt);
  - restart without a master → `needsFile:true` with the fileName;
  - delete in each stored state → the item gone and the item prefix empty;
  - discard → `discarded`, the batch prefix empty (SC-003), `images` count unchanged, one
    `upload.discarded {batch} {count}` line with the exact count, and the next GET of
    `/admin/upload/state` → `{"batch":null}`;
  - restart and delete with a stale attemptId → 409 `stale-attempt`;
  - `ImageStore.TryDeleteAsync` with a throwing delegate logs exactly one
    `upload.cleanup_failed {key} {error}` with the exception type name and does not throw
    (decisions row 44).
- [X] T039 [US5] Write `tests/Gallery.Web.Tests/UploadResumeTests.cs` (SC-005, FR-008, FR-010):
  - with a batch of items in queued, processing, uploading and succeeded, advance `Clock` by 121 s
    → U2 and the page show `interrupted` for exactly the first three and `succeeded` for the
    fourth;
  - U5 on an interrupted item → 409 `not-active`;
  - Restart works on it;
  - two registers from two `UploadHost` clients (same session) → one batch id;
  - while a batch is open, `/` and a gallery page are byte-identical to a no-batch run (US5 AS-4).
- [X] T040 [US1] **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadAccessTests.cs` (FR-001, I.3,
  I.6, plan item 5). It is a theory over every U2–U11 route and method:
  - without a session, GET → 302 `/admin/signin` and POST → 401;
  - with a session and NO `X-CSRF-TOKEN` → 400 for every POST, with the batch document unchanged;
  - with a session and a token, on a fresh batch with one registered item → exactly the
    contract status: U2 200, U3 200, U4 422 (no master stored), U5 204, U6 204, U7 422, U8 409
    `not-active` (the item is still queued), U9 204, U10 409 `not-ready`, U11 200. Each theory case
    runs on its own fresh batch;
  - every response carries `Cache-Control: no-store`.

## Phase 8: User Story 1 (cont.): browser worker and page script, Batch G

- [X] T041 (high tier, parent, ≤ 2 minutes) **S3Proxy CORS preflight probe**, the FIRST step of
  the browser e2e work (decisions row 35, research R-14). Start a throwaway S3Proxy container
  (same pinned image as `S3ProxyFixture`) with `S3PROXY_CORS_ALLOW_ORIGINS=http://localhost:5092`,
  `S3PROXY_CORS_ALLOW_METHODS=PUT`, `S3PROXY_CORS_ALLOW_HEADERS=content-type`. Then
  `curl -i -X OPTIONS` an object URL with `Origin`, `Access-Control-Request-Method: PUT` and
  `Access-Control-Request-Headers: content-type`. Also try `S3PROXY_CORS_ALLOW_ALL=true`. In the
  same probe, confirm Chrome keeps the `__Host-gallery` cookie on `http://localhost:<port>` (with
  `ForceHttpsSchemeFilter`). Stop only that container by id. Record the result as part P9 in
  `evidence/probe-results.txt`:
  - preflight 200/204 with a matching `Access-Control-Allow-Origin` → the fixture uses those env
    values and the browser test also proves the single-header PUT under CORS;
  - otherwise → apply decisions row 35's fallback (that one fixture's Chrome gets
    `--disable-web-security --user-data-dir=<temp>`), append a DEFAULT row to decisions.md, and
    note that FR-044 then rests on T015 plus OWNER-ONLY O-03.
  If the cookie is dropped, use `https://localhost` with the dev cert in the fixture and record
  that as well.

**Batch G owns**: `src/Gallery.Web/wwwroot/admin-assets/{upload.js,upload-worker.js}`,
`tests/Gallery.Web.Tests/Fixtures/UploadBrowserFixture.cs`,
`tests/Gallery.Web.Tests/UploadBrowserTests.cs`. It does not edit `BrowserSiteFixture.cs` or
`S3ProxyFixture.cs`.

- [X] T042 [US1] Write `src/Gallery.Web/wwwroot/admin-assets/upload-worker.js` exactly per
  contracts/components.md C5 and research R-02:
  - relative imports of the vendored factories, with options from each package's `meta.js`
    defaults (AVIF quality 50 speed 6; WebP quality 75 method 4; preview WebP quality 40);
  - `createImageBitmap(file,{imageOrientation:'from-image'})`, then sRGB `OffscreenCanvas`
    resizes at the widths in the message ONLY, then the placeholder via ThumbHash at ≤ 100 px
    input;
  - timings named exactly as listed in the Rules;
  - `bitmap.close()` in `finally`;
  - any error (incl. `RangeError`) → `{ok:false, reason}`.
- [X] T043 [US1] Write `src/Gallery.Web/wwwroot/admin-assets/upload.js` exactly per
  contracts/components.md C4 (steps 1–8):
  - pick-time refusal by the first 3 bytes, the size, and the remaining capacity, with the exact
    strings;
  - template cloning with sentinel replacement through `textContent`/`setAttribute` only;
  - one worker, sequential items, ≤ 4 PUTs in flight;
  - storage PUTs with `credentials:"omit"` and exactly one header `Content-Type`;
  - U5 at every state change and every 20 s;
  - Restart, Delete, Discard and Confirm per steps 5–7;
  - `X-CSRF-TOKEN` and `credentials:"same-origin"` on every app call.
  Header comment: how footer counts are replaced by structure (C3 Templates).
- [X] T044 [US1] Create `tests/Gallery.Web.Tests/Fixtures/UploadBrowserFixture.cs`
  (`[CollectionDefinition("upload-browser")]`), following references/browser-verification.md:
  - its own Mongo database (MongoFixture container) and its own S3Proxy container configured per
    T041's result;
  - the real app via `AppBuilder.Build` on `http://localhost:<free port>` with
    `--environment Development --contentRoot src/Gallery.Web --applicationName Gallery.Web`, the
    storage overrides, FakeGoogle, and `LogLines` capture;
  - an owner session minted with `AdminHost`'s approach and injected into the browser context as
    the `__Host-gallery` cookie;
  - headless Chrome via `Channel="chrome"`, with the T041 flags if the fallback applies;
  - a fixture guard test: GET `/admin/upload` in the browser shows `upload-batch` without
    `hidden` after load.
- [X] T045 [US1] Write `tests/Gallery.Web.Tests/UploadBrowserTests.cs`
  (`[Trait("Category","Browser")]`, `[Collection("upload-browser")]`):
  - `Worker_path_two_photos_reach_ready_and_confirm` (US1 AS-2/4/6, FR-014): set the file input to
    `orientation-6.jpg` and `gps-camera.jpg`; wait (≤ 60 s) for 2 tiles in `state-succeeded`;
    click "Add 2 photos to library" → the URL contains `?confirmed=` and the page shows "2 photos
    added". In Mongo, the orientation-6 image has width 200 and height 400, and every public file
    parses through `ContainerParser` upright (h > w). `LogLines` has `op.timing browser.total`
    for each photo.
  - `Storage_PUTs_carry_only_content_type` (FR-044): record every request to the S3 origin
    (`page.Request`), and each PUT's `AllHeadersAsync()` has no header beyond `content-type` and
    browser-added fetch headers (`origin`, `referer`, `user-agent`, `accept*`, `sec-*`,
    `content-length`); no `x-amz-*`, no `cache-control`.
  - `Pick_time_refusals` (FR-004): with host `Upload:MaxFileBytes` small, picking
    `not-a-jpeg.jpg` and an over-limit JPEG lists both with their exact reasons, adds 0 tiles, and
    sends 0 requests to S3.
  - `Drop_on_document_adds_tiles` (FR-003): dispatch `dragover` + `drop` with a `DataTransfer`
    built from `orientation-6.jpg` on `document.body` (not the dropzone) → one tile appears.
  - `Worker_failure_fails_only_that_photo` (FR-017, US4 AS-1): pick a JPEG whose bytes after the
    SOI marker are garbage (`FF D8 FF` + random; written by the test) plus `gps-camera.jpg` → the
    garbage tile becomes `state-failed` with a reason and Restart/Delete, the other reaches
    `state-succeeded`, and the footer shows the Blocked copy.
  - `Restart_without_master_asks_for_the_file` (FR-011, E-04): pick a file while
    `page.RouteAsync` aborts every PUT to the S3 origin → after the retry the tile shows
    `state-failed` with "upload failed" (C4 step 4). Remove the route and reload the page (it no
    longer holds the file), then press Restart → the tile shows "Pick {name} again". Choosing a
    file with the same name continues the item to `state-succeeded`; in a second run a different
    name adds a new tile instead.
  - `No_js_shows_notice_and_no_control` (FR-006): a `JavaScriptEnabled=false` context shows the
    notice text and `upload-batch` is not visible.
  - `Main_thread_stays_responsive` (US1 AS-3): a `longtask` PerformanceObserver installed by init
    script records no task > 200 ms while `fixture-6000x4000.jpg` encodes.
  Run the class 3 times; it must be green all 3 times.

## Phase 9: Polish, guards, evidence (high tier unless noted)

- [X] T046 **[SAFETY]** Write `tests/Gallery.Web.Tests/UploadSecretScanTests.cs` (FR-043, SC-012,
  plan item 4). With an owner `UploadHost` and a registered item:
  - collect the bodies of `/admin/upload`, `/admin/upload/state`, the U3 and U4 JSON responses,
    and every file under `wwwroot/admin-assets/` (recursively);
  - assert none contains the host's `Storage:SecretAccessKey` value (`localsecret`);
  - when `GALLERY_R2_TESTS=1`, also assert none contains the real R2 secret read through
    `R2Fixture`'s configuration. Compare in memory, and the failure message names only the file or
    route, never the value.
  It may run in parallel with Batch G (it owns only this file).
- [X] T047 [P] Write `tests/Gallery.Web.Tests/UploadSourceGuardTests.cs`:
  - (III.8) no `*.cs` under `src/Gallery.Web` references `SixLabors`, `SkiaSharp`,
    `System.Drawing` or `ImageDecoder`, and `Gallery.Web.csproj` gains no package;
  - (C3) `upload.js` and `upload-worker.js` contain none of `innerHTML`, `outerHTML`,
    `insertAdjacentHTML`, `document.write`;
  - (IV.22) GET `/` and a gallery and photo page of the seeded shape contain no `/admin-assets`;
  - (FR-033) no file under `src/Gallery.Web/Upload/` contains `Purge` or `Warm`.
- [X] T048 Write `specs/008-upload-pipeline/evidence/p95.sh` (contracts/log-events.md). Input is
  either the plain message lines of the test log or the JSON console log (convert with
  `jq -r '.Message'`). For each SC row it prints n, p95 and PASS/FAIL:
  - SC-006 `op.timing s3.presign` ≤ 200;
  - SC-007 `op.timing upload.validate` ≤ 2000;
  - SC-008 `upload.confirmed` with count ≥ 40 → duration ≤ 60000;
  - SC-009 `op.timing browser.total` ≤ 60000 (reported as `n/a (owner live run)` when n=0, never
    as a pass);
  - SC-010 `http.request GET /admin/upload 200` ≤ 500.
  n=0 for SC-006/007/008/010 is a FAIL. An empty or unreadable log is a FAIL. Test it on three
  synthetic logs in `evidence/p95-selftest/` (plain, JSON, wrong format), which must give pass,
  pass and FAIL. Commit those logs. It can be written while Batch A runs.
- [X] T049 Timing evidence. Write `tests/Gallery.Web.Tests/UploadTimingEvidenceTests.cs`
  (`[Trait("Category","Evidence")]`):
  - one `UploadHost` registers 40 photos with the fixture master, drives U4/PUT/U7 for each with
    the committed 320 sizes, and confirms;
  - it seeds a 100-item batch in a second database and GETs `/admin/upload` 30 times;
  - it writes `LogLines` (one message per line, in log order) to
    `specs/008-upload-pipeline/evidence/upload-timings.log`.
  Then run `bash specs/008-upload-pipeline/evidence/p95.sh
  specs/008-upload-pipeline/evidence/upload-timings.log | tee
  specs/008-upload-pipeline/evidence/p95-output.txt`. SC-006, 007, 008 and 010 must PASS with
  non-zero n.
- [X] T050 Write `specs/008-upload-pipeline/evidence/verify.sh`, the **live-server curl
  walkthrough** (quickstart Part B; tee to `verify-output.txt`), modelled on 007's:
  1. The build and test gates.
  2. Start a throwaway S3Proxy container (stopped by id at exit) and use a fresh Mongo database
     name `gallery-verify-008-$(date +%s)` on the dev Mongo container. Never touch the `gallery`
     DB or R2.
  3. Run the built dll (one pid, `kill` only it) on `http://localhost:5092`, Development, with
     env overrides for Mongo and Storage and `Authentication:Google:Authority` pointed at a
     fake provider started from `specs/008-upload-pipeline/evidence/fake-oidc.cs`
     (`dotnet run fake-oidc.cs -- --urls http://127.0.0.1:5093`, its own pid; decisions row 40).
     The file has NO `#:package` line: it serves the same four endpoints as
     `tests/Gallery.Web.Tests/Fixtures/FakeGoogle.cs` (discovery, jwks, authorize redirect, token)
     and builds the RS256 id_token by hand with `System.Security.Cryptography.RSA` and base64url.
     The app gets `Authentication:Google:ClientId/ClientSecret` as the literal dummies
     `verify-client`/`verify-secret`, never the owner's.
  4. Sign in through the fake provider with curl's cookie jar, then drive U3 → PUT master → U4 →
     PUT sizes → U7 → U10 with the committed fixture files.
  5. Check: `X-Amz-SignedHeaders=content-type%3Bhost` in every presigned URL; 413 on a 200 KB
     register; 400 on a POST without `X-CSRF-TOKEN`; `no-store` on `/admin/upload/state`;
     `immutable` on `/admin-assets/upload.js?v=…`; `staging/{batch}/` empty after confirm (S3
     listing); one `http.request` line per call in the captured server log
     (`evidence/verify-run.log`).
  Print presigned URLs with their query values redacted. `pkill` appears nowhere in the script.
- [X] T051 Run `bash specs/008-upload-pipeline/evidence/verify.sh | tee
  specs/008-upload-pipeline/evidence/verify-output.txt` on the merged tree. Force-add
  `evidence/*.log` (`git add -f`) after grepping it for 32+-character tokens and redacting them.
  Write `evidence/live-walkthrough.md`: the automated walkthrough's observations, and a pointer to
  OWNER-ONLY O-02 for the real-browser and R2 part.
- [X] T052 Fold every executor `decisions_made` entry into `decisions.md` as numbered rows.
  Reject any that contradict the contracts, keep backward-compatibility shims out, and note each
  rejection in the row.
- [X] T053 Update `docs/roadmap.md` 008 with a status line: what was delivered, the evidence
  paths, and what is not delivered until the OWNER-ONLY section runs (SC-009, Safari/iOS, R2
  copy-if-match, the lifecycle rule). Then run `dotnet clean`, build with `-warnaserror` (0
  warnings) and the full suite green. Record the new test count in `evidence/baseline.txt`.
- [X] T054 **[SAFETY]** **Non-vacuity, final task** (high tier; plan item 9). Copy the
  orchestration template to `specs/008-upload-pipeline/evidence/non-vacuity.sh`, add one `brk`
  line per guard below, run it on a clean committed tree, and tee the output to
  `evidence/non-vacuity.txt`. Every line must be `PASS` (red on an ASSERTION), and the restored
  build must be clean.
  - (a) ContainerParser: accept `infe` items of type `Exif` → `Reader1_alone_rejects_gps_avif`.
  - (b) ContainerParser: ignore the VP8X EXIF flag and the `EXIF` chunk →
    `Reader1_alone_rejects_gps_webp`.
  - (c) UploadValidator: set `Reader2Ok = true` without calling `DerivativeValidator` →
    `Combined_rejects_gps_webp`.
  - (d) U7: skip `DeletePrefixAsync` on rejection → the UploadFlowTests GPS-AVIF rejection.
  - (e) `PresignPutAsync`: add `Headers.CacheControl = "no-cache"` to the request →
    `Presign_signs_content_type_and_host_only`.
  - (f) UploadPage: render `data-k="@StorageOptions.SecretAccessKey"` → UploadSecretScanTests.
  - (g) AdminEndpoints: map upload on `app.MapGroup("/admin")` instead of the protected group →
    UploadAccessTests.
  - (h) ConfirmService: drop `ETagToMatch` → `Reput_after_validation_publishes_nothing`.
  - (h2) ConfirmService: on `PreconditionFailed`, upsert the image anyway →
    `Reput_after_validation_publishes_nothing` (the "no image for item 2" assertion).
  - (i) ConfirmService: push the item id into the first public gallery's `items` →
    `Public_pages_byte_identical_around_confirm`.
  - (j) ImageDoc: give `SeedKey` the initialiser `= string.Empty` →
    `Seed_run_after_confirm_leaves_uploaded_images` and the T007 serialisation fact.
  - (k) U7: use the request's `camera` when present → `Complete_ignores_page_camera_and_dimensions`.
  - (l) upload.js: add header `x-amz-meta-a: 1` to storage PUTs →
    `Storage_PUTs_carry_only_content_type`.
  - (m) `p95.sh` on `upload-timings.log` reports non-zero n for SC-006/007/008/010. A zero means
    a format mismatch, not a fast app.

## OWNER-ONLY: post-build live checks (NOT part of the automated run)

These need the owner, real devices, or the Cloudflare dashboard. `/speckit-implement` does NOT
run them, does not wait for them, and marks the feature implemented when T001–T054 are done. They
are tracked here so they stay visible. Their results go into decisions.md and the roadmap status
line when the owner runs them.

- [ ] O-01 Cloudflare dashboard: lifecycle rule on `gallery-dev-private`, deleting `staging/`
  objects 7 days after creation (spec Assumptions owner prerequisite 2, D-11 backstop). Optional:
  `masters/` to Infrequent Access after 30 days.
- [ ] O-02 The skipped roadmap spike (decisions row 6), as quickstart Part C steps 1–5. On
  `https://localhost:5081`, in Chrome, Safari macOS and iOS Safari: 40 real exports, an 18–20 MB
  JPEG, an EXIF-rotated photo, a HEIC and a renamed PNG. Capture `evidence/live-run.log` (commit a
  redacted copy) and run `p95.sh` on it for SC-006/007 (R2)/008/009/010. Record per browser: the
  20 MB time, iOS memory behaviour, real placeholder and size bytes against the estimates, and
  whether any colour profile appeared (decisions rows 9 and 29). A miss reopens D-09.
- [ ] O-03 Real R2 storage facts: `GALLERY_R2_TESTS=1 dotnet test --artifacts-path artifacts
  --filter Category=R2`, covering T015–T017 on R2, including copy-if-match 412 (decisions row 27).
  Save the redacted output as `evidence/r2-tests.txt`.
- [ ] O-04 SC-003 on R2 (quickstart Part C step 6): `staging/{batchId}/` lists zero objects after
  a confirm and after a discard.

## Requirement coverage (for /speckit-analyze and the parent's checkpoints)

| Requirements | Tasks |
|---|---|
| FR-001, FR-002, FR-006, FR-009, FR-034 | T004, T032–T035, T040, T045 |
| FR-003, FR-004, FR-014–FR-017 | T042–T045 |
| FR-005, FR-022, US3 | T021, T022, T026, T027, T036 |
| FR-007, FR-008, FR-010, US5 | T010–T012, T039 |
| FR-011–FR-013, FR-036, US4 | T014, T037, T038, T045 |
| FR-018–FR-021, FR-044 | T009, T015, T026, T027, T029, T034, T045 |
| FR-023–FR-029, FR-041, US2 | T018–T024, T028, T029 |
| FR-030–FR-033 | T030, T031, T047 |
| FR-035 | T006, T007, T031 |
| FR-037–FR-040 | Rules, T029, T031, T038, T048, T049 |
| FR-042 | T016, O-03 |
| FR-043 | T046 |
| SC-001 | T049 (40 photos, server path), T045 (browser path, 2 photos), O-02 (40 real exports) |
| SC-002 | T020, T024, T029 |
| SC-003 | T031, T038, T050, O-04 |
| SC-004 | T031, T039 |
| SC-005 | T039 |
| SC-006–SC-008, SC-010 | T048, T049 |
| SC-009 | T048 (script), O-02 (owner live run) |
| SC-011 | T016, O-03 |
| SC-012 | T046 |

## Dependencies & Execution Order

- Phase 1 (T001–T005, parent) → Batches A, B and C in parallel → parent checkpoint.
- Batch D (T025–T029) needs A, B and C. Batch E (T030–T034) needs D. T035 (parent) follows E.
- Batch F (T036–T040) needs E (it extends `UploadEndpoints.cs` after E). Batch G needs T041 (parent
  probe) and E, and can run in parallel with F because the files are disjoint. G has its own
  artifacts path.
- Polish: T048 any time after Phase 1. T046 and T047 after E. T049 after F. T050–T051 after F and
  G. T052–T053 after everything. **T054 is last.**
- OWNER-ONLY O-01…O-04 have no position in this order. Nothing above depends on them.

## Parallel opportunities

- Batches A, B and C: three executors, each with `--artifacts-path artifacts/exec-{a,b,c}` and a
  filter on its own classes. T008 inside A is independent of the rest of A.
- After E: Batch F and Batch G in parallel (disjoint files; G owns only `wwwroot/admin-assets/*.js`
  and two test files). T046 and T047 run alongside them.
- Parent work while executors run: T048 (p95.sh and its self-test), T050 (verify.sh and
  fake-oidc.cs), and the T054 break list against the merged tree.

## Executor batches (≈6–8 tasks, verify between batches)

| Batch | Tasks | Count | Runs with |
|---|---|---|---|
| A foundation | T006–T013 | 8 | B, C |
| B storage | T014–T017 | 4 | A, C |
| C readers | T018–T024 | 7 | A, B |
| D server path | T025–T029 | 5 | — |
| E confirm + page | T030–T034 | 5 | — |
| F lifecycle + access | T036–T040 | 5 | G |
| G browser | T042–T045 | 4 | F |

## Implementation Strategy

MVP = Phase 1 + Batches A–E: the owner can upload through the server path, confirm, and see the
page, and every [SAFETY] guard except access and secrets is green. Batch F completes recovery,
resume and the access sweep (all required: US2 and US1's safety rides on it). Batch G proves the
real browser path. Polish produces the evidence. The feature is accepted after T054. The
OWNER-ONLY items follow at the owner's convenience.
