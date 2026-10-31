# Tasks: Public Site, Local Walking Skeleton

**Input**: Design documents in `specs/006-public-site-local/`: plan.md, spec.md, research.md,
data-model.md, contracts/http.md, contracts/seed.md, contracts/components.md, quickstart.md,
decisions.md.
**Source of truth**: the contracts. Implement exactly what they say. If something is missing or
contradictory, stop and report a blocker. Do not invent.

## Rules for every executor

- **The owner runs `dotnet watch run` on :5080 in their own window.** Never kill or restart it,
  never `pkill` dotnet or Gallery.Web, and never bind :5080. Build and test into a separate
  output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts`
  - For a live server: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web
    --no-build --no-launch-profile --artifacts-path artifacts --urls http://localhost:5090`.
    Stop only the PID you started.
- **Existing tests must pass UNMODIFIED.** That covers every file in `tests/` before this feature,
  including `PublicOutputUnchangedTests`, `MoleculePageTests` and `OrganismPageTests`. A failure
  there means your change is wrong. Fix the code, never the test or a baseline snapshot.
- **Never print, log, commit or echo a secret.** Do not open `.r2-keys-copy` or read
  user-secrets values. Code reads them through configuration only. Error messages name the
  missing key, never a value.
- **Never commit** `dev-pictures/`, `.dev-prepared/`, `.r2-keys*`, or `artifacts/`. Check with
  `git status --short` before committing.
- Gate after every task: build with 0 warnings and 0 errors, and all tests green. Docker Desktop
  must be running for integration tests (`docker info`). If it isn't, run `open -a Docker` and
  wait.
- No `style=` attributes. No `<script>` except the two in PublicLayout when `Enhance=true`. No
  NuGet package beyond the five in quickstart Part A. No media query other than `min-width: 600px`
  or `min-width: 1200px`.
- Logging uses the existing template exactly: `log.LogInformation("op.timing {op} {duration_ms}
  {outcome}", ...)`, with `duration_ms` rounded to 2 decimals and `outcome` `ok` or `error`.
- Blocking, non-deferrable tasks are marked **[SAFETY]**. They implement Constitution III.3/III.9
  or II.2 controls and may never be skipped, stubbed, or left as follow-ups.
- Escalate (stop and report) on any design question. Executors never change the contracts.

## Phase 1: Setup (high tier, done before dispatch)

- [ ] T001 Add packages with the CLI exactly as in quickstart Part A:
  - MongoDB.Driver 3.13.0, AWSSDK.S3 4.0.104.8 and MetadataExtractor 2.9.3 to `src/Gallery.Web`
  - Testcontainers 4.16.0 and Testcontainers.MongoDb 4.16.0 to `tests/Gallery.Web.Tests`

  Run `dotnet user-secrets init --project src/Gallery.Web`. Add `/.dev-prepared/` to
  `.gitignore` if absent. Build and test green.
- [ ] T002 Create `scripts/dev-mongo.sh`. It is idempotent: it starts the `gallery-mongo`
  container if it's stopped, and otherwise runs `docker run -d --name gallery-mongo -p
  27017:27017 -v gallery-mongo:/data/db mongo:8`. It then waits until `docker exec gallery-mongo
  mongosh --quiet --eval 'db.runCommand({ping:1}).ok'` prints 1, giving up after 30 s.
- [ ] T003 Create `scripts/r2-keys-to-user-secrets.sh` (research R-10). It reads `.r2-keys-copy`
  with the same parsing as the plan's probe: the value on the first non-blank line after "Access
  Key ID" and after "Secret Access Key", and the endpoint matched by
  `https://[a-f0-9]{32}\.r2\.cloudflarestorage\.com`. It runs `dotnet user-secrets set --project
  src/Gallery.Web` for `Storage:AccessKeyId`, `Storage:SecretAccessKey` and `Storage:ServiceUrl`,
  passing values as arguments and never echoing them. It prints only `set 3 secrets; delete
  .r2-keys-copy when done`. It exits 1 with a message if the file or a value is missing. The high
  tier runs it once and confirms `dotnet user-secrets list --project src/Gallery.Web | cut -d= -f1`
  shows the three keys.
- [ ] T004 Add the non-secret settings to `src/Gallery.Web/appsettings.Development.json`, keeping
  the existing `Logging` block. `Mongo`: `ConnectionString` `mongodb://localhost:27017`,
  `Database` `gallery_dev`. `Storage`: `PublicBucket` `gallery-dev-public`, `PrivateBucket`
  `gallery-dev-private`, `ImageBaseUrl` `https://img-dev.zacal.me`. `Site`: `OutputCache` `true`.

## Phase 2: Foundational (blocks every user story)

- [ ] T005 [P] Create `src/Gallery.Core/Ids/OpaqueId.cs`:
  - `NewId()` returns 16 bytes from `RandomNumberGenerator`, encoded as RFC 4648 base32 in
    lowercase with no padding (26 chars)
  - `IsValid(string?)` checks exactly 26 chars of `[a-z2-7]`

  Tests in `tests/Gallery.Core.Tests/OpaqueIdTests.cs`:
  - 1,000 ids are all valid and distinct
  - IsValid rejects null, 25 or 27 chars, uppercase, and the characters `0`, `1`, `8`, `9`
- [ ] T006 [P] Create `src/Gallery.Core/Text/Slug.cs` with `From(string title)`:
  - lowercase; map each char to ASCII by removing diacritics (`NormalizationForm.FormD`, drop
    `NonSpacingMark`)
  - collapse runs of chars outside `[a-z0-9]` to one `-`, then trim `-`
  - cut to 60 chars, then trim a trailing `-` again
  - empty result becomes `"gallery"`

  Tests in `tests/Gallery.Core.Tests/SlugTests.cs`: `"September 5"` → `september-5`;
  `"Café — Noir!"` → `cafe-noir`; `"!!!"` → `gallery`; a 100-char title gives at most 60 chars
  with no trailing `-`.
- [ ] T007 [P] Create `src/Gallery.Core/Content/GalleryNav.cs` with
  `static (string? Prev, string? Next) Neighbours(IReadOnlyList<string> items, string current)`.
  It returns nulls at the ends and throws `ArgumentException` when `current` is not in `items`.
  Tests in `tests/Gallery.Core.Tests/GalleryNavTests.cs` cover first, middle, last, a single item,
  and an item that isn't present.
- [ ] T008 Create `src/Gallery.Web/Observability/OpTimer.cs` and extend
  `src/Gallery.Web/Observability/RequestTimingMiddleware.cs` (research R-09).
  - OpTimer is a scoped service:
    - `Task<T> Time<T>(string op, Func<Task<T>> body, bool isDbRead = false)` logs `op.timing`
      with `outcome` `ok` or `error` and rethrows on error.
    - When `isDbRead` is true it increments `DbReads` and records the duration.
    - `ServerTimingDb()` returns `db;dur=x` entries.
  - In the middleware, resolve OpTimer from `context.RequestServices`. Append the db entries to
    the existing `Server-Timing` header in `OnStarting`, after `app;dur=`. Add a `{db_reads}`
    field at the END of the existing template:
    `"http.request {method} {route} {status} {duration_ms} {db_reads}"`.
  - Register OpTimer as scoped in `Program.cs`.
  - New tests in `tests/Gallery.Web.Tests/OpTimerTests.cs`:
    - a failing body logs `outcome` error and rethrows
    - two db reads give `DbReads == 2` and two `db;dur=` entries

  Existing 003/005 verify scripts grep `"http.request "`, which still matches.
- [ ] T009 Create `src/Gallery.Web/Storage/StorageOptions.cs` (`ServiceUrl`, `AccessKeyId`,
  `SecretAccessKey`, `PublicBucket`, `PrivateBucket`, `ImageBaseUrl`) and
  `src/Gallery.Web/Storage/S3ClientFactory.cs`. It is the ONLY place an `AmazonS3Client` is
  built, using the research R-02 settings verbatim:
  - `ForcePathStyle = true`, `AuthenticationRegion = "auto"`
  - `RequestChecksumCalculation = WHEN_REQUIRED`, `ResponseChecksumValidation = WHEN_REQUIRED`
  - `public bool IsHttps` from the ServiceUrl scheme

  `Validate()` returns the names of missing settings, never values.
- [ ] T010 Create `src/Gallery.Web/Storage/ImageStore.cs` over the factory's client:
  - `Task<bool> ExistsAsync(bucket, key)` (HEAD; false on 404)
  - `PutAsync(bucket, key, Stream, contentType, cacheControl)` with `DisableDefaultChecksumValidation
    = true` and `DisablePayloadSigning = IsHttps`
  - `Task<byte[]> GetRangeAsync(bucket, key, from, to)`
  - `CopyAsync(srcBucket, srcKey, dstBucket, dstKey, contentType, cacheControl)` with
    `MetadataDirective.REPLACE`
  - `DeleteAsync(bucket, key)`
  - `Task<string> PresignPutAsync(bucket, key, contentType, TimeSpan expiry)`: it throws
    `ArgumentOutOfRangeException` if expiry is over 15 minutes (III.9), and sets `Protocol` from
    the scheme
  - `string PublicUrl(string key)` = `ImageBaseUrl.TrimEnd('/') + "/" + key`

  Every S3 call is wrapped in `OpTimer.Time` with op names `s3.head`, `s3.put`, `s3.get-range`,
  `s3.copy`, `s3.delete` and `s3.presign`. ImageStore is a singleton, so it takes an
  `ILogger<ImageStore>` and logs `op.timing` itself through a static helper shared with OpTimer
  (`OpTiming.Log(logger, op, ms, outcome)` in `OpTimer.cs`). It does not take the scoped OpTimer.
- [ ] T011 Create the test fixtures in `tests/Gallery.Web.Tests/Fixtures/`:
  - `MongoFixture.cs`: Testcontainers `MongoDbBuilder().WithImage("mongo:8")`, one per test
    collection `"mongo"`, with `NewDatabase()` returning a fresh `gallery_test_<guid>` database.
  - `S3ProxyFixture.cs`: `ContainerBuilder()`, image
    `andrewgaul/s3proxy@sha256:87662b2a5afcdfa5f478a1c61650bae4c87bbd15f6ff2823236bcfd66ef7fe04`,
    port 80, env `S3PROXY_AUTHORIZATION=aws-v2-or-v4`, `S3PROXY_IDENTITY=local`,
    `S3PROXY_CREDENTIAL=localsecret`, `JCLOUDS_PROVIDER=transient`, wait until the HTTP port
    answers. It creates buckets `pub` and `priv`, and exposes a `StorageOptions` (ServiceUrl
    `http://localhost:{mapped}`, ImageBaseUrl `https://img.test`). One per collection `"s3"`.
  - `R2Fixture.cs`: builds `StorageOptions` from user-secrets (`new
    ConfigurationBuilder().AddUserSecrets(typeof(Gallery.Web.Storage.StorageOptions).Assembly)`)
    plus the dev bucket names. `Enabled` is true only when env `GALLERY_R2_TESTS=1`.
  - R2 tests carry `[Trait("Category","R2")]`. The xunit in the csproj is 2.9.3, which has no
    dynamic skip, so each R2 test method starts with `if (!fixture.Enabled) return;`. Its method
    name ends in `_R2`, so a vacuous pass is visible in the test list. The high tier runs them
    for real with `GALLERY_R2_TESTS=1` (T012).
- [ ] T012 [SAFETY] Create `tests/Gallery.Web.Tests/StorageTests.cs` as an abstract base with two
  subclasses, `EmulatorStorageTests` (S3ProxyFixture) and `R2StorageTests` (R2Fixture,
  Category=R2, keys under `test/{guid}/`, deleted in cleanup). The same test methods run on both
  targets (FR-022):
  - put then head: exists, `Cache-Control` equals `public, max-age=31536000, immutable`
  - head on a missing key returns false
  - a ranged get returns exactly the requested bytes
  - copy with REPLACE carries the new Cache-Control
  - delete
  - **[SAFETY] presign**: a PUT through `HttpClient` to the URL returns 200; a GET to the same URL
    returns 403; a PUT to the URL with the key changed returns 403
  - **[SAFETY]** PresignPutAsync with 16 minutes throws

  Run the R2 variant once with `GALLERY_R2_TESTS=1 dotnet test --filter Category=R2` (high tier
  runs it; it needs the owner's secrets) and save the output to
  `specs/006-public-site-local/evidence/r2-tests.txt`. Run it through `sed -E
  's/[A-Za-z0-9]{32,}/<redacted>/g'` first.
- [ ] T013 [SAFETY] Create `tests/Gallery.Web.Tests/R2ScopeTests.cs` (Category=R2, FR-021). It
  uses the R2 credential through the factory:
  - `ListBucketsAsync` throws an AmazonS3Exception with status 403
  - `ListObjectsV2` on bucket `gallery-scope-probe-other` throws AmazonS3Exception with status
    403 or 404. Either means the bucket can't be reached; the test output records which, and the
    R-03 probe got 403.
  - `PutBucketAsync("gallery-scope-probe-new")` gives 403
  - ListObjectsV2 on both dev buckets succeeds

  Output is appended to `evidence/r2-tests.txt`.
- [ ] T014 Create `src/Gallery.Web/Data/MongoOptions.cs`, `src/Gallery.Web/Data/Documents.cs` and
  `src/Gallery.Web/Data/ContentReader.cs`.
  - `Documents.cs` has the BSON-mapped classes for `site`, `galleries` and `images`, with
    GalleryCard and CoverImage exactly as in data-model.md. Use explicit `[BsonElement]` camelCase
    names that match the tables. There is no GPS, location or path field anywhere.
  - ContentReader (scoped, takes `IMongoDatabase` and OpTimer) provides:
    - `Task<SiteDoc?> HomeAsync()`: op `db.site.read`, isDbRead, projection `homeCards`
    - `Task<GalleryDoc?> GalleryAsync(string id, bool forPhoto)`: op `db.gallery.read`; with
      `forPhoto`, the projection is `slug,title,visibility,items`; otherwise everything except
      `card`
    - `Task<IReadOnlyList<ImageDoc>> ImagesAsync(IEnumerable<string> ids, bool full)`: op
      `db.images.read`, `_id $in ids`, projection per data-model "Read shapes", returned in the
      order of `ids`

  Register `IMongoClient` as a singleton with `ServerSelectionTimeout = 2s` and the database from
  options in `AppBuilder.cs` (T015). Startup MUST NOT ping Mongo; the app boots without it.
- [ ] T015 Move the body of `src/Gallery.Web/Program.cs` into a new
  `src/Gallery.Web/AppBuilder.cs`: `public static class AppBuilder { public static WebApplication
  Build(string[] args, IDictionary<string,string?>? overrides = null) }`.
  - It does everything Program.cs does today, plus the T008, T009, T010 and T014 registrations.
  - It applies `overrides` with `builder.Configuration.AddInMemoryCollection(overrides)` LAST, so
    tests can set `Mongo:ConnectionString`, `Mongo:Database`, `Storage:*` and `Site:OutputCache`.
  - `Program.cs` becomes: if `args` starts with `seed`, run the seed command (wired in T030; until
    then `return 1`); otherwise `AppBuilder.Build(args).Run();`.
  - Acceptance: all existing tests pass unmodified, and `/ui/` still serves in Development.
- [ ] T016 Create `src/Gallery.Web/Site/ViewMapper.cs`:
  - `ImageView ToView(ImageDoc, Func<string,string> url)` maps derivatives to `Variant(w,
    url(avif), url(webp))`, the placeholder preview to `PlaceholderDataUri`, the color to
    `DominantColor`, and camera to the Core `Exif` (Make, Model, Lens, FocalLengthMm,
    FocalLength35Mm, FNumber, ExposureSeconds, Iso)
  - `ContactSheetItem ToCard(GalleryCardDoc, Func<string,string> url)` uses Href
    `/g/{galleryId}/{slug}`, Title = card title, and Image = the cover's view or null
  - `string PreloadLink(ImageView, string sizes)` builds the contracts/http.md `Link` value using
    `SrcSet.Webp` and `SrcSet.FallbackWebp`

  Unit tests in `tests/Gallery.Web.Tests/ViewMapperTests.cs`.

## Phase 3: User Story 1, browse the site from stored content (P1) 🎯 MVP

**Goal**: home → gallery → photo, rendered from Mongo, with images on the image hostname.
**Independent test**: integration tests seed Mongo directly with documents, and the live walk works
after the seed (US4).

- [ ] T017 [US1] Apply contracts/components.md "Changes to existing components" to `Picture.razor`,
  `Tile.razor`, `ContactSheet.razor`, `ContactSheetItem.cs` and `SoloViewer.razor` (+
  `SoloViewer.razor.css` `.solo-caption`).
  - Every new parameter is optional. When unset, the output is byte-identical.
  - Follow the whitespace rule already noted in Tile.razor: new markup goes on the same line as an
    existing element or inside unindented branches.
  - `ContactSheetItem` keeps its two-parameter positional constructor (add optional trailing
    parameters).
  - Tests in a NEW file `tests/Gallery.Web.Tests/ComponentAdditionsTests.cs`:
    - TransitionId renders `id="p-x"` on `.pic`, and unset renders no `id=`
    - Tile Preload* gives `data-preload-srcset`/`-sizes`
    - Tile with a null Image and an EmptyTitle gives `tile tile-empty`, an empty cell and the title
    - Tile with a null Image and no EmptyTitle throws
    - SoloViewer Caption renders `<p class="solo-caption">` between the h1 and the metadata, and
      none when null
    - SoloViewer PrefetchUrls gives `data-prefetch`
  - Acceptance: `PublicOutputUnchangedTests`, `MoleculePageTests` and `OrganismPageTests` pass
    unmodified. No `/ui/` page file is edited.
- [ ] T018 [US1] Apply the PublicLayout changes from contracts/components.md:
  - `Enhance` (default false) and `HeadExtra` (rendered after `<style>`, nothing when null)
  - `@view-transition { navigation: auto; }` appended to `PublicLayout.razor.css` (probe-confirmed:
    the scoped bundler emits it unchanged)
  - with `Enhance=true`, at the end of `<body>`:
    1. `<script type="speculationrules">{"prerender":[{"where":{"href_matches":"/g/*"},"eagerness":"moderate"}]}</script>`
    2. `<script type="module">` with the predictive loader in contracts/components.md, at most
       30 lines, no `unload`/`beforeunload`, first statement
       `if (navigator.connection?.saveData) return;` inside an IIFE

  Tests (append to ComponentAdditionsTests.cs):
  - Enhance=false output has no `<script` (the `/ui/` PublicLayout specimen stays unchanged)
  - Enhance=true has exactly 2 `<script`, contains `speculationrules`, `"eagerness":"moderate"` and
    `saveData`, and does not contain `unload`
  - HeadExtra renders inside `<head>`
- [ ] T019 [US1] Create the page components in `src/Gallery.Web/Site/Pages/`: `HomePage.razor`,
  `GalleryPage.razor` (+ `GalleryPage.razor.css`), `PhotoPage.razor` and `NotFoundPage.razor`,
  exactly as in contracts/components.md "New page components". Add `@using
  Gallery.Web.Components.*` through a new `src/Gallery.Web/Site/Pages/_Imports.razor` that copies
  the using lines from `src/Gallery.Web/Ui/Pages/_Imports.razor`, minus the Ui ones.
  - GalleryPage and PhotoPage render `HeadExtra` as a `<style>` with one
    `#p-{id}{view-transition-name:img-{id}}` rule per image that gets a `TransitionId` (all images
    on the page).
  - Tests in `tests/Gallery.Web.Tests/PageComponentTests.cs`:
    - home with 0 cards renders the layout only
    - GalleryPage shows the title as h1 and the description `<p class="gallery-desc">` only when
      set, with ContactSheet `cs-l4 cs-m3 cs-s1`, and no ContactSheet when there are no items
    - PhotoPage title is `"{photo} — {gallery}"`
    - the view-transition style has one rule per image
    - every page passes `Guards.AssertNoInlineStyle`
- [ ] T020 [US1] Create `src/Gallery.Web/Site/PublicCache.cs`:
  - `HtmlPolicy = "public, max-age=0, s-maxage=86400"` and `ShortPolicy = "public, max-age=0,
    s-maxage=300"`
  - output-cache policy name `"public-page"`, 60 s, varying by path
  - registered in Program.cs with `AddOutputCache`, and `UseOutputCache` placed after the timing
    middleware
  - applied to public routes only when `Site:OutputCache` is true
- [ ] T021 [US1] Create `src/Gallery.Web/Site/PublicEndpoints.cs` with `MapPublic(this
  WebApplication)` implementing contracts/http.md "Routes" for `/`, `/g/{id}`, `/g/{id}/{slug}` and
  `/g/{id}/{slug}/{imageId}`, GET and HEAD.
  - Malformed ids return 404 with no read (`OpaqueId.IsValid`).
  - Slug mismatch: 301 with `Results.Redirect(url, permanent: true)`.
  - The photo page reads the gallery (forPhoto), finds neighbours with `GalleryNav`, then reads
    `ImagesAsync([prev, cur, next] without nulls, full: true)`. Its PrefetchUrls are the
    neighbours' `SrcSet.FallbackWebp`.
  - Tile TransitionId = image id. Gallery page tiles get
    `PreloadSrcSet = SrcSet.Webp(img)` and `PreloadSizes = "100vw"`. Home cards get neither.
  - Headers per contracts/http.md: Cache-Control, `Link` from `ViewMapper.PreloadLink` for the
    primary image, and `X-Robots-Tag: noindex` where required.
  - A `MongoException`/`TimeoutException` during a read becomes 500 with the body "Something went
    wrong". The OpTimer error event is already logged.
  - Replace `app.MapGet("/", () => "Hello World!")` in `Program.cs` with `app.MapPublic()`.
    `/ui` mapping is unchanged.
- [ ] T022 [US1] Integration tests in `tests/Gallery.Web.Tests/PublicRoutesTests.cs`
  (MongoFixture). Host the real app with `AppBuilder.Build(["--urls", "http://127.0.0.1:0"],
  overrides)`, using overrides for the fixture's Mongo, a fresh database, `Storage:ImageBaseUrl`
  `https://img.test`, and `Site:OutputCache` `false`. Call `await app.StartAsync()` and take the
  base address from `app.Urls`, then use a plain `HttpClient` with `AllowAutoRedirect = false`.
  Stop the app in `DisposeAsync`. No Mvc.Testing package.

  Insert documents directly: 2 public galleries, 1 empty, 3 images, and home cards. Count datastore
  reads by the number of `db;dur=` entries in the `Server-Timing` header. Assert:
  - `/` lists the public cards in order, with 1 read
  - the gallery page has the images in order and `Server-Timing` with 2 `db;` entries
  - the photo page has prev/next correct at both ends, the caption, and `data-prefetch`
  - every `<img>`/`<source>` URL starts with ImageBaseUrl
  - `Cache-Control` and `Link` headers are present, and there is no `Set-Cookie`

## Phase 4: User Story 2, only public galleries are listed (P1)

**Independent test**: a private gallery is absent from `/`, renders by address, and is `noindex`
on the gallery and photo pages.

- [ ] T023 [SAFETY] [US2] Extend `PublicRoutesTests.cs` with a private gallery (II.2, FR-007,
  FR-008). Assert:
  - its id and title are absent from `/`
  - `/g/{id}/{slug}` gives 200 with `<meta name="robots" content="noindex">` and the
    `X-Robots-Tag: noindex` header
  - a photo page reached through it carries both
  - a public gallery's pages carry NEITHER (the negative case)

## Phase 5: User Story 3, links survive renames; bad addresses fail cleanly (P2)

- [ ] T024 [US3] Extend `PublicRoutesTests.cs` (FR-009, FR-010):
  - `/g/{id}/wrong-slug` gives 301 with `Location` `/g/{id}/{slug}`
  - `/g/{id}` gives 301
  - `/g/{id}/wrong/{imageId}` gives 301 to the current slug with the same imageId
  - an unknown valid id gives 404 with NotFoundPage, `noindex` and `db;` count 1
  - a malformed id (`ABC`, 25 chars, a `0`) gives 404 with zero `db;` entries
  - an imageId that is valid but not in items gives 404
  - redirect and 404 `Cache-Control` equals ShortPolicy

## Phase 6: User Story 4, the owner loads their own photos with one command (P2)

**Independent test**: prepare and seed on empty storage, run both again with no change, scan the
output for metadata.

- [ ] T025 [P] [SAFETY] [US4] Create `scripts/prepare-photos.sh` exactly per contracts/seed.md
  "scripts/prepare-photos.sh" and research R-04.
  - Encoder flags must match `scripts/build-samples.sh`.
  - The intermediate step is `ffmpeg -i src -vf "scale='min(2560,iw)':-2" -frames:v 1
    -map_metadata -1 tmp.png`.
  - The orientation check: create `scripts/fixtures/make-exif-fixtures.py` (python3 stdlib only)
    and run it once to write two committed fixtures under 20 KB each:
    - `tests/fixtures/orientation-6.jpg`: a 400×200 JPEG made first with `ffmpeg -f lavfi -i
      "color=c=red:s=400x200,drawbox=x=0:y=0:w=100:h=200:c=blue:t=fill" -frames:v 1`, with an APP1
      Exif inserted right after SOI. The big-endian TIFF has one IFD0 entry, `0x0112 SHORT 1 6`.
    - `tests/fixtures/gps-camera.jpg`, used by T027: the same base image. Its IFD0 has Make
      `"TESTCAM"`, Model `"T1"` and a GPSInfo pointer (`0x8825`) to a GPS IFD with
      GPSLatitudeRef `N`, GPSLatitude `37/1 46/1 0/1`, GPSLongitudeRef `W` and GPSLongitude
      `122/1 25/1 0/1`.
    - Verify each with `mdls -raw -name kMDItemOrientation` / `kMDItemLatitude` (non-null), and
      record the result in `evidence/prepare-check.txt`.
  - Run `prepare-photos.sh` with `PHOTOS_DIR=tests/fixtures PREPARED_DIR=/tmp/prep-test` (the
    script must honour these env overrides). Assert the largest WebP is portrait (height >
    width) and that `check-metadata.sh` passes.
  - Record the commands and results in `specs/006-public-site-local/evidence/prepare-check.txt`.
- [ ] T026 [P] [US4] Create `scripts/seed-manifest.sh` exactly per contracts/seed.md
  "scripts/seed-manifest.sh" (refuses to overwrite; groups by `mdls`
  `kMDItemContentCreationDate`; adds unlisted, coming-soon, a shared photo, a caption). The high
  tier runs it once against `dev-pictures/` and commits `seed/manifest.json`.
- [ ] T027 [SAFETY] [US4] Create `src/Gallery.Web/Seed/CameraFields.cs` (research R-05):
  - `static Exif? Read(Stream jpeg)` uses `ImageMetadataReader.ReadMetadata`
  - it reads ONLY ExifIfd0Directory Make/Model and ExifSubIfdDirectory LensModel, FocalLength,
    35mm, FNumber, ExposureTime and ISO
  - it never references `GpsDirectory`, and returns null if all fields are null

  Tests in `tests/Gallery.Web.Tests/CameraFieldsTests.cs` use the committed fixture
  `tests/fixtures/gps-camera.jpg` (from T025's fixture script; T027 depends on that script). As a
  precondition, assert that MetadataExtractor DOES see a GpsDirectory with a latitude in the
  fixture, so the test isn't vacuous. Then assert:
  - Make and Model are read
  - serializing the resulting `Exif` and the `ImageDoc` built from it to BSON/JSON contains no
    `gps`, `latitude`, `longitude` or `altitude` (case-insensitive)
  - a source grep test: `CameraFields.cs` does not contain `Gps`
- [ ] T028 [P] [US4] Create `src/Gallery.Web/Seed/Manifest.cs`: load and validate per
  contracts/seed.md. It returns all errors at once (duplicate key, visibility, cover not in photos,
  title, caption and description lengths). Tests in `tests/Gallery.Web.Tests/ManifestTests.cs`
  cover each rule plus the defaults: missing visibility gives private, and a null title gives the
  file name without its extension.
- [ ] T029 [US4] Create `src/Gallery.Web/Seed/Seeder.cs` (contracts/seed.md "Seed command",
  steps 2–6; data-model.md). Inputs: Manifest, the prepared root, the photos dir, ImageStore,
  IMongoDatabase, and a logger. It:
  - creates the unique indexes on `seedKey`
  - upserts by `seedKey`, keeping `_id`/`createdAt`, with new ids from `OpaqueId.NewId()`
  - HEADs before every PUT, uploading with `Cache-Control: public, max-age=31536000, immutable` and
    `Content-Type` `image/avif`/`image/webp`
  - deletes documents not in the manifest
  - recomputes `galleryIds`, every card (cover = pinned or `items[0]`, null if empty), `homeOrder`
    and `homeCards` (public only)
  - returns a `SeedSummary` record with every count in contracts/http.md

  It never uploads originals and never touches the private bucket. Tests in
  `tests/Gallery.Web.Tests/SeederTests.cs` (MongoFixture + S3ProxyFixture), with a tiny
  `.dev-prepared` fixture generated in the test (2 photo.json files with fake small byte files):
  - first run: counts and documents per data-model, with the private gallery absent from homeCards
  - **second run**: `uploaded 0`, and a dump of the 3 collections without `updatedAt` is equal
    (SC-006)
  - a gallery removed from the manifest is deleted and its images' galleryIds are updated
  - unmatched names are reported both ways
  - cover fallback, and an empty gallery card with a null cover
- [ ] T030 [US4] Create `src/Gallery.Web/Seed/SeedCommand.cs` and wire it in `Program.cs`:
  - if `args[0] == "seed"`, build the services (no web server), validate StorageOptions and
    MongoOptions (exit 2 naming the missing keys), load the manifest (exit 3 printing errors), run
    the Seeder, print the summary lines, and exit 0
  - options `--manifest` (default `seed/manifest.json`) and `--prepared` (default
    `.dev-prepared`), paths relative to the repo root found by walking up to `Gallery.slnx`
  - the last summary line reminds the owner to restart a running app to clear the output cache
- [ ] T031 [US4] High tier, live: run `bash scripts/dev-mongo.sh`, `bash
  scripts/prepare-photos.sh`, review `seed/manifest.json` (T026), then `dotnet run --project
  src/Gallery.Web --artifacts-path artifacts -- seed` twice. Save both summaries to
  `specs/006-public-site-local/evidence/seed-run.txt`. The second must show `uploaded 0` and
  `skipped-unchanged` for every photo in prepare's output.

## Phase 7: User Story 5, the site feels instant (P2)

- [ ] T032 [US5] Browser verification on the high tier's own :5090 instance after the seed. Record
  in `specs/006-public-site-local/evidence/browser.md` with screenshots in `evidence/shots/`:
  - CLS = 0 (PerformanceObserver `layout-shift`) on home, a gallery and a photo at 1440, 1024 and
    390 widths
  - hover on a gallery-page tile adds a `link[rel=preload][as=image]`
  - the photo page adds two `link[rel=prefetch]` after load, and none with Save-Data emulated
  - the `view-transition-name` computed on the tile picture and the solo picture match
  - a JS-disabled walk home → gallery → photo → next → back works (SC-009)
  - the empty "Coming soon" card renders without layout break

## Phase 8: Polish and evidence

- [ ] T033 Create `specs/006-public-site-local/evidence/verify.sh`, modelled on
  `specs/005-admin-ui-components/evidence/verify.sh`: build and test gates, its own :5090 server
  with log at `evidence/dev-run.log`, `pass`/`fail` lines. It checks:
  - SC-002: no private titles on `/`, and `noindex` on every private page found from the manifest
    keys via mongosh
  - SC-007: every img/source URL starts with `https://img-dev.zacal.me/`, and a sample of 5 HEADs
    shows `immutable`
  - **SC-008 secret scan**: `git ls-files -z | xargs -0 grep -lE
    'r2\.cloudflarestorage\.com|\.r2\.dev|[a-f0-9]{32}'` over committed text files, excluding
    `*.avif`, `*.webp` and `*.json` under `wwwroot/samples`, and served HTML of 5 pages. It
    prints file names only, never matches. It also checks `git check-ignore .r2-keys-copy
    dev-pictures/x .dev-prepared/x`
  - SC-011: `check-metadata.sh` over all of `.dev-prepared/*/*.{avif,webp}`, and a mongosh dump of
    `images` grepped case-insensitively for `gps|latitude|longitude`
  - SC-012: every `images` id appears on at least one gallery page
  - redirects and 404s per contracts/http.md
  - no `Set-Cookie` on public routes
  - `/ui/` still 200 in Development
  - a second seed run shows `uploaded 0` (SC-006)

  Output goes to `evidence/verify-output.txt`. All PASS is required.
- [ ] T034 Create `specs/006-public-site-local/evidence/p95.sh` (FR-031). It starts :5090 with
  `Site__OutputCache=false`, logging to `evidence/perf-run.log`, then warms up with 10 requests per
  route and sends 100 requests each to `/`, one public gallery page and one photo page. From the
  log alone (`"http.request ` lines; fields `duration_ms` and `db_reads` read from the JSON
  `State` object) it computes the p95 per route and the `db_reads` distribution.
  - PASS SC-003: p95 < 50 ms per route
  - PASS SC-004: home `db_reads=1` on 100%, and the others ≤ 2 on 100%

  Output goes to `evidence/p95-output.txt`. Add the logs with `git add -f` (`*.log` is ignored).
- [ ] T035 Clean rebuild (`dotnet clean` then `dotnet build -warnaserror`), run the full test
  suite, confirm the test counts went up from 69/156 and that no pre-existing test file shows in
  `git diff --stat main -- tests/` other than added files. Update `docs/roadmap.md` 006 as
  done-pending-review, and fold every executor `decisions_made` into `decisions.md` rows 16+.

## Dependencies & Execution Order

- Phase 1 (T001–T004) runs on the high tier, before dispatch.
- Phase 2: T005, T006 and T007 [P] are independent (Core). T008 → T009 → T010 → T011 → T012/T013.
  T014 depends on T008. T015 depends on T014. T016 depends on T014.
- US1: T017 → T018 → T019 → T020 → T021 → T022. Needs Phase 2.
- US2 (T023) and US3 (T024) need T021 and can run in parallel with each other (same test file:
  run them sequentially or have one executor do both).
- US4: T025, T026 and T028 [P with each other]; T027 needs T025 (fixture); T029 needs T010, T014, T027 and T028; T030
  needs T029; T031 needs T025, T026, T030 and T021 (to view).
- US5 (T032) needs T031.
- Polish: T033 and T034 need T031. T035 is last.

## Parallel opportunities

- Core helpers T005, T006 and T007 together.
- Once Phase 2 is done: the US1 chain (components and pages) in parallel with the US4 scripts
  T025 and T026 and the US4 code T028 (T027 follows T025, which makes its fixture). These touch
  disjoint files.
- T033 and T034 together.

## Implementation Strategy

1. **MVP = Phase 1, Phase 2, US1 and US2**: the site renders from Mongo, with visibility enforced.
   It can be shown with hand-inserted documents.
2. Add US4 to see the owner's photos.
3. Add US3 and US5, then the polish evidence.

Executor tiering: the high tier does Phase 1, T012/T013 R2 runs, T026's manifest review, T031,
T032 and T035. Cheap executors do T005–T011, T014–T025 and T027–T030, plus T033/T034 scripts with
high-tier review.
