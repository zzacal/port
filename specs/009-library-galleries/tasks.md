# Tasks: Library and Gallery Management

**Input**: Design documents in `specs/009-library-galleries/`: spec.md, plan.md, research.md,
data-model.md, contracts/http.md, contracts/edge.md, contracts/components.md,
contracts/log-events.md, contracts/removal-command.md, quickstart.md, decisions.md,
evidence/probe-results.txt.

**Source of truth**: the contracts and the research decisions (R-xx). Implement exactly what they
say. If something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: requested by the spec (every SC, FR-031, FR-036, FR-049). Each batch writes its tests in
the files it names, in the same batch as the code they guard.

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
    --contentRoot "$PWD/src/Gallery.Web" --urls https://localhost:5094` (5094/5095 are this
    feature's ports). Stop it with `kill <that pid>` only.
  - macOS has no `timeout` command. Use `--blame-hang-timeout`.
- **Existing tests must pass UNMODIFIED.** That means every file in `tests/` at 27eef20 (the list
  and the 779-test count are recorded by T001), including `UiPagesUnchangedTests`,
  `PublicOutputUnchangedTests`, `OrganismStatePageTests`, `PublicRoutesTests`, `BrowserTests`,
  `UploadPageTests`, `UploadSourceGuardTests` and `OwnerClaimTests` (the `OwnerClaimTests` class).
  A failure there means your change is wrong. Fix the code, never the test, a fixture or a
  snapshot. The ONLY stated requirement changes are listed in T047 (seed retirement) and T036
  (`AdminAccessTests`). Only the parent makes them. Executors never touch those files.
- **Repo paths in tests**: use `Render.RepoRoot`, which walks up to `Gallery.slnx`. Never count
  `..` levels from `AppContext.BaseDirectory`. A fixed count breaks under `artifacts/exec-*`.
- **Secrets**: never read, print, log or commit a credential. Never open `.r2-keys*` or the
  user-secrets file. `Edge:ApiToken` exists only as a test literal in tests and in the owner's
  user-secrets (OWNER-ONLY O-01). No log event, page, URL or exception message carries it.
- **Never commit** `artifacts/`, `dev-pictures/`, `.dev-prepared/`, `.r2-keys*`. Executors do not
  commit. The parent commits after its own gate.
- **No new NuGet package, no npm/node, no htmx** (V.2, research R-08). The only new browser code
  is `wwwroot/admin-assets/library.js` and `reorder.js`. No `style=` attributes. No `innerHTML`,
  `outerHTML`, `insertAdjacentHTML` or `document.write` in admin scripts. No `<script>` and no
  owner markup on any public page (FR-003, IV.22).
- **No real Cloudflare call in any test.** The recorder (`RecordingEdgeProvider`) or a fake
  `HttpMessageHandler` replaces the edge.
- **File ownership**: each batch lists the files it owns. These belong to the parent (high tier)
  and no executor edits them: `src/Gallery.Web/AppBuilder.cs`, `src/Gallery.Web/Program.cs`,
  `src/Gallery.Web/Admin/AdminEndpoints.cs`, `src/Gallery.Web/Site/PublicEndpoints.cs`,
  `src/Gallery.Web/Site/PublicCache.cs`, `src/Gallery.Web/Upload/AdminAssets.cs`, every file under
  `src/Gallery.Web/Seed/` (except the one-line delegate in T005), `seed/`, `scripts/`, and the
  test files named in T036 and T047. `src/Gallery.Web/Library/LibrarySetup.cs` and
  `src/Gallery.Web/Edge/EdgeSetup.cs` take ADDITIVE edits only (one registration line or one
  route call per change), and at most one running batch edits each at a time.
- **Logging, verbatim** (contracts/log-events.md; `evidence/p95.sh` parses these names):
  - Timing uses the existing `OpTimer.Time` or `OpTiming.Log` exactly:
    `op.timing {op} {duration_ms} {outcome}`, with `outcome` one of `ok`, `error`, `refused`,
    `stale`, `skipped`. `OpTimer.Time` writes `ok`/`error` itself; a service that refuses (a
    gate) or detects a stale reorder logs one extra `OpTiming.Log` line for the operation's db op
    with `refused` or `stale`, and `skipped` is used only by the edge worker.
  - op names used by 009 (no others): `db.library.page`, `db.library.count`, `db.galleries.list`,
    `db.site.order`, `db.image.update`, `db.image.tombstone`, `db.image.delete`,
    `db.images.shared`, `db.gallery.create`, `db.gallery.update`, `db.gallery.delete`,
    `db.gallery.load` (owner reads; `db.gallery.read` stays the public `ContentReader` op),
    `db.membership.repair`, `db.site.project`, `s3.delete` (existing
    `ImageStore`), `library.image.delete`, `library.bulk.add`, `library.bulk.delete`,
    `cache.evict`, `edge.purge.call`, `edge.warm.request`, `maintenance.remove-seeded`.
  - Events, exactly these templates:
    - `edge.purge {op} {mode} {count} {duration_ms} {outcome} {addresses}` (`mode` ∈ `log-only`,
      `dev-zone`, `recorder`; `count` ≤ 100; `addresses` space-joined; `outcome` ∈ `ok`,
      `skipped`, `failed`, `rate-limited`)
    - `edge.warm {op} {mode} {count} {duration_ms} {outcome}`
    - `edge.lag {op} {duration_ms}`
    - `edge.dropped {op} {count}`
    - `owner.action {action} {target} {count} {detail}` (`action` ∈ `image.delete`,
      `gallery.delete`, `gallery.publish`, `gallery.unpublish`, `seeded.remove`)
    - `library.delete_leftover {image} {bucket} {key} {error}` (`error` = exception type name
      only)
  - `http.request {method} {route} {status} {duration_ms} {db_reads}` comes from the existing
    middleware. Do not add a second one.
- **[SAFETY]** marks a task that implements a Constitution I.3/I.6/II.5/II.6/III.9 control, the
  shared-file delete rule, the removal command's target guard or owner-content filter, or the
  public-page isolation of FR-003 (research R-12 "[SAFETY] items"). These tasks are blocking and
  non-deferrable. They are never stubbed, skipped, weakened, or left as follow-ups. Each [SAFETY]
  guard gets a deliberate break in T060 and must go red on its ASSERTION.
- **Tight assertions**: assert the exact count, state, reason, stored value and address set the
  contract names. Purge assertions compare the recorder's set for EQUALITY with the expected set
  (SC-004: none missing, none extra). Never write an assertion that accepts two outcomes.
- **Bound controls, FALSE case**: every test of a control that shows persisted state (Public
  switch, `required`, `disabled`, selected radio) asserts the TRUE case renders the attribute AND
  the FALSE case renders NO such attribute at all: no `checked`, no `checked="False"`, no
  `disabled="False"` (FR-031).
- **Call, never copy**: `CardBuilder.Build`, `SiteProjector.ProjectAsync`,
  `MembershipRepair.RepairAsync`, `PurgeSets.*`, `GalleryService.*`,
  `ImageDeleteService.DeleteAsync`, `Indexes.EnsureAsync`, `OpaqueId`, `Slug.From`,
  `GalleryNav.Neighbours` and the 005 components are CALLED. A bulk path calls the single-item
  method for each id; a second copy of any of these is a defect.
- **No compatibility shims**: no redirect or alias for `seed`, no deprecation message beyond the
  contracted "Unknown command: seed", no kept-for-later seed code, no replacement markers.
- Escalate (stop and report) on any design question. Executors never change the contracts. After
  two failed attempts on one task, stop and hand it back to the parent.
- Docker must be running (`docker info`; if not, `open -a Docker` and poll).

## Owner UI review rules (acceptance criteria for every UI task: T030–T033, T037–T040, T048, T049, T051)

From speckit-orchestration "Post-implement UI review", applied to every owner page and its tests:

- **UI-1 Studio rows link to their public page**: every library tile, gallery card and builder
  tile whose public page exists has a `View ↗` link to it; items without a page show none
  (FR-005). Pages get the paths from the view model the endpoint builds; nothing is guessed.
- **UI-2 Controls sit under the title, not overlaid**: per-item controls are in a row inside the
  tile, under its title (`Tile.Caption`). Only the library `SelectMark` is drawn over the photo.
  Status text sits under the cell (FR-004).
- **UI-3 One primary action**: exactly one filled (`btn-primary`) button is visible per page
  state; a state that adds its own primary demotes the container's (contracts/components.md).
- **UI-4 Danger actions are outlined**: every delete uses `ButtonVariant.Danger` (outlined
  `btn-danger`), never `btn-primary` and never a solid red fill.
- **UI-5 Bound controls are tested in the FALSE case** (Rules above, FR-031).
- **UI-6 Bulk actions reuse the single-item gate**: bulk add and bulk delete call the single-item
  method per id and report skipped or failed items with the reason (FR-012, FR-013).
- **UI-7 Limits are enforced at selection time**: the selection cap (`data-limit`) and the field
  limits (`maxlength`, `required`, `pattern`) come from `LibraryOptions`, the same instance the
  server enforces; the server still refuses (FR-008, FR-010).
- **UI-8 No validation nags on bulk surfaces**: the library and builder show no required-field
  errors, warnings or missing-metadata prompts (FR-007). The only refusal text is the reason for
  a refused submit.

## Phase 1: Setup (high tier, parent, before any dispatch)

- [X] T001 Run the baseline gate (`dotnet build … -warnaserror`, `dotnet test …`) on 27eef20.
  Write to `specs/009-library-galleries/evidence/baseline.txt`: the head hash, the warning count,
  the test count per assembly (expected 779 in total), the list of every file under `tests/`
  (`git ls-files tests`), the byte-identity guards that pin pre-change output
  (`PublicOutputUnchangedTests`, `UiPagesUnchangedTests`, `OrganismStatePageTests`), and the
  seed-consumer inventory of T047 as found by the grep in T047 step 0. Every later checkpoint runs
  `git diff --stat 27eef20 -- tests/` and accepts changes only to NEW files and to the files named
  in T036 and T047.
- [X] T002 **Output-cache check, the FIRST implement task** (plan Risks R-07, decisions row 23,
  research R-07). Write `tests/Gallery.Web.Tests/OutputCacheEvictionTests.cs`
  (`[Collection("mongo")]`). The host is `AdminHost.CreateAsync` with the override
  `Site:OutputCache=true`. It inserts one public gallery with two images directly, with the
  document shape `PublicRoutesTests` uses (inline helper in this file):
  - `Home_second_GET_is_served_from_the_output_cache`: GET `/` twice; exactly ONE
    `op.timing db.site.read` line in `LogLines`.
  - `Gallery_and_photo_second_GET_is_served_from_the_output_cache`: the same for
    `/g/{gid}/{slug}` (one `db.gallery.read`) and the photo page.
  - `Evicting_a_page_tag_drops_only_that_page`: after both pages are cached,
    `IOutputCacheStore.EvictByTagAsync(PublicCache.PageTag("/g/{gid}/{slug}"))`; the gallery page
    is re-read (a second `db.gallery.read`) and `/` is not (still one `db.site.read`).
  Write the first two facts and run them BEFORE changing any source (the third needs
  `PageTag`, so it is added after the source change below). Record the red/green result as part
  P2b in
  `evidence/probe-results.txt`: red or green, and the assertion message.
  - If the first two facts are red (the 006 cache never applies), edit
    `src/Gallery.Web/Site/PublicEndpoints.cs` to add `.CacheOutput(PublicCache.PolicyName)` to
    the `/`, `/g/{id}/{slug}` and `/g/{id}/{slug}/{imageId}` routes when `Site:OutputCache` is
    true. Do not add it to the `/g/{id}` short route or to any 301/404 response.
  - In every case, add `public static string PageTag(string path) => $"page:{path}"` to
    `src/Gallery.Web/Site/PublicCache.cs`, and have `StatusAndHeadersResult` add
    `PageTag(httpContext.Request.Path)` beside the existing `public-page` tag.
  - Public markup does not change. `PublicRoutesTests`, `PublicOutputUnchangedTests`,
    `AdminAccessTests` and `BrowserTests` stay green UNMODIFIED.
  - Append a DEFAULT row to decisions.md stating which branch was taken.
- [X] T003 **[SAFETY]** The shared-file seam (owner gate inheritance, I.3/I.6). Create:
  - `src/Gallery.Web/Library/LibrarySetup.cs` with `public static IServiceCollection
    AddLibrary(this IServiceCollection s, IConfiguration c)` (empty body) and
    `public static RouteGroupBuilder MapLibrary(this RouteGroupBuilder admin)` (returns `admin`);
  - `src/Gallery.Web/Edge/EdgeSetup.cs` with `AddEdge(this IServiceCollection s, IConfiguration c)`
    (empty body).
  Then edit, once:
  - `src/Gallery.Web/AppBuilder.cs`: `builder.Services.AddLibrary(builder.Configuration);` and
    `builder.Services.AddEdge(builder.Configuration);` beside `AddUpload`.
  - `src/Gallery.Web/Admin/AdminEndpoints.cs`: `admin.MapLibrary();` on the protected group,
    immediately after `admin.MapUpload();` and BEFORE the H6 `{**rest}` catch-all, so every 009
    route inherits `RequireAuthorization("owner")` and `AdminAntiforgeryFilter`.
  - `src/Gallery.Web/Upload/AdminAssets.cs` (additive): `public string UrlFor(string relPath)`,
    which returns `/{relPath}?v={sha256-8hex}` from a hash computed once at startup for
    `admin-assets/library.js` and `admin-assets/reorder.js` (missing file → `dev`), with the
    existing method for upload.js. Existing members stay byte-unchanged.
  Gate: build green, all 779 existing tests green unmodified. From now on no executor edits these
  files.

**Checkpoint**: build and the full existing suite green. Batches A and B may now run in parallel.

## Phase 2: Foundational, Batch A: data and projections (one executor, 8 tasks)

**Owns**: `src/Gallery.Web/Data/Documents.cs` (the two properties named in T004 only),
`src/Gallery.Web/Data/Indexes.cs`, `src/Gallery.Web/Seed/Seeder.cs` (the one-line delegate in T005
only), `src/Gallery.Web/Upload/ConfirmService.cs` (the index call and the title cap only),
`src/Gallery.Web/Library/{LibraryOptions,CardBuilder,SiteProjector,MembershipRepair}.cs`,
`src/Gallery.Web/Library/LibrarySetup.cs` (additive),
`tests/Gallery.Web.Tests/{IndexesTests,CardBuilderTests,SiteProjectorTests,MembershipRepairTests}.cs`,
`tests/Gallery.Web.Tests/Fixtures/{SeedShapes,InvariantChecker}.cs`.

- [X] T004 **[SAFETY]** In `src/Gallery.Web/Data/Documents.cs` (FR-048, decisions row 13):
  - `GalleryDoc.SeedKey` becomes `string?` with NO initialiser and `[BsonIgnoreIfNull]`;
  - `ImageDoc` gains `[BsonElement("deletingAt")] [BsonIgnoreIfNull] public DateTime? DeletingAt`.
  Every existing writer sets `SeedKey` explicitly, so existing tests stay green. Fix any nullable
  warning at the consumer with a null check, never with `!`.
- [X] T005 Create `src/Gallery.Web/Data/Indexes.cs` with `public static Task
  EnsureAsync(IMongoDatabase db, CancellationToken ct = default)` (research R-11, data-model
  "Indexes"):
  - MOVE the body of `Seeder.EnsureSeedKeyIndexesAsync` here unchanged (the partial unique
    `seedKey_1` on `images` and `galleries`, with the legacy plain-index upgrade on codes 85/86).
    `Seeder.EnsureSeedKeyIndexesAsync` becomes the one line `=> Indexes.EnsureAsync(db, ct);`
    until T047 deletes the seeder.
  - Add the 009 indexes: `images {createdAt:-1, _id:-1}` (name `library_page`),
    `images {"derivatives.avif":1}` and `images {"derivatives.webp":1}`.
  - Move the once-per-process `Lazy` cache from `ConfirmService` into `Indexes` as
    `Indexes.EnsureOnceAsync(IMongoDatabase db)` (keyed by database name, retried after a
    failure exactly as today). `ConfirmService` calls it where it now calls the seeder method;
    `GalleryService`, `ImageService` and `ImageDeleteService` call it before their first write,
    and the removal command calls `EnsureAsync` (data-model "Indexes").
- [X] T006 **[SAFETY]** Write `tests/Gallery.Web.Tests/IndexesTests.cs` (`[Collection("mongo")]`):
  - after `EnsureAsync`, two images AND two galleries without `seedKey` insert without
    DuplicateKey (the 008 orchestration rule: test with TWO documents);
  - a pre-existing plain unique `seedKey_1` on each collection is replaced by the partial form
    (`partialFilterExpression` equals `{seedKey:{$exists:true}}`);
  - the three 009 indexes exist with their keys; a second `EnsureAsync` call is a no-op;
  - `new GalleryDoc{…}.ToBsonDocument()` without a seed key has no `seedKey` element, and
    `new ImageDoc{…}` without `DeletingAt` has no `deletingAt` element.
- [X] T007 Create `src/Gallery.Web/Library/LibraryOptions.cs`, section `Library`: `PageSize` 100,
  `TitleMax` 200, `CaptionMax` 2000, `DescriptionMax` 5000 (research R-09). Startup validation:
  every value > 0, or startup throws naming the key. Register it in `AddLibrary` as one
  singleton. `ConfirmService` takes `LibraryOptions` and caps upload titles at `TitleMax` instead
  of the constant (the constant stays as the default's documented value; `UploadConfirmTests`
  stay green unmodified).
- [X] T008 Create `src/Gallery.Web/Library/CardBuilder.cs`: `public static GalleryCardDoc
  Build(GalleryDoc g, ImageDoc? cover)`. Cover = `pinnedCover` when set, else `items[0]`, else
  none; `imageCount` = `items.Count`; title and slug from the gallery. It produces exactly the
  shape `GalleryCardDoc` already defines (the shape the public pages read). Create
  `src/Gallery.Web/Library/SiteProjector.cs` (scoped, `IMongoDatabase`, `OpTimer`):
  `ProjectAsync()` reads `site.homeOrder` and the galleries in it, then `$set`s ONLY `homeCards`
  (public AND `imageCount ≥ 1`, in `homeOrder` order) and `updatedAt`, with upsert on
  `_id:"site"`, all inside `OpTimer.Time("db.site.project")`. It never names `claimedSubject`,
  `claimedAt` or `claimedIp` (`OwnerWriteGuardTests` scans for them). Register it scoped.
- [X] T009 Write `tests/Gallery.Web.Tests/CardBuilderTests.cs` (pinned cover, first-item cover,
  empty gallery: no cover and count 0, pinned cover removed → falls back) and
  `tests/Gallery.Web.Tests/SiteProjectorTests.cs` (`[Collection("mongo")]`): private and empty
  galleries are excluded, `homeOrder` order is kept, a site document carrying a claim keeps
  `claimedSubject`, `claimedAt` and `claimedIp` byte-identical after `ProjectAsync`, and GET `/`
  on an `AdminHost` over that database answers with `db_reads` 1 in its `http.request` line
  (SC-007).
- [X] T010 Create `src/Gallery.Web/Library/MembershipRepair.cs` (scoped):
  `RepairAsync(IReadOnlyCollection<string> imageIds)` sets each image's `galleryIds` to the ids
  of the galleries whose `items` contain it (one `galleries.find({items:{$in:ids}})`, then one
  update per image), inside `OpTimer.Time("db.membership.repair")` (research R-01). Write
  `tests/Gallery.Web.Tests/MembershipRepairTests.cs`: stale extra ids are removed, missing ids are
  added, an image in no gallery gets `[]`, and a second run changes nothing.
- [X] T011 Create `tests/Gallery.Web.Tests/Fixtures/SeedShapes.cs`: builders for seed-shaped
  `ImageDoc` and `GalleryDoc` (non-empty `seedKey`, `sourceHash`, derivative keys, card) with the
  field set the retired seeder wrote (read `Seed/Seeder.cs` for the field list; do not call it),
  and for upload-shaped images (no `seedKey`). Create
  `tests/Gallery.Web.Tests/Fixtures/InvariantChecker.cs`: `AssertAsync(IMongoDatabase db)` checks
  every data-model invariant (no duplicate `items`; every item is an existing, non-tombstoned
  image; `pinnedCover` absent or a member; `galleryIds` equals membership for every image; `card`
  equals `CardBuilder.Build`; `homeCards` equals what `SiteProjector` would write) and fails with
  the gallery or image id. Prove it can fail: a fact in `MembershipRepairTests.cs` breaks each
  invariant once on a fresh database and asserts `AssertAsync` throws naming the broken id.

## Phase 3: Foundational, Batch B: edge purge and warm (one executor, parallel with A, 8 tasks)

**Owns**: `src/Gallery.Web/Edge/*.cs` (EdgeSetup.cs additive), `src/Gallery.Web/Library/PageCacheEvictor.cs`,
`tests/Gallery.Web.Tests/{PurgeSetTests,EdgeWorkerTests,CloudflareEdgeProviderTests,PageCacheEvictorTests}.cs`,
`tests/Gallery.Web.Tests/Fixtures/RecordingEdgeProvider.cs`. It registers `PageCacheEvictor` in
`EdgeSetup.cs`, not in `LibrarySetup.cs` (Batch A owns that file).

- [X] T012 Create `src/Gallery.Web/Edge/EdgeOptions.cs` (section `Edge`: `ZoneId`, `ApiToken`,
  `SiteBaseUrl` default `https://localhost:5081`, `PurgePages` false, `MaxUrlsPerCall` 100,
  `MaxUrlsPerSecond` 800, `QueueCapacity` 1000; `ToString()` not overridden), `EdgeWork.cs`
  (record: `Op`, `Pages`, `Files`, `WarmFiles`, `EnqueuedAt`), `IEdgeCache.cs`
  (`void Enqueue(EdgeWork work)`) and `IEdgeProvider.cs` (`string Mode`,
  `Task<EdgeOutcome> PurgeAsync(IReadOnlyList<string> urls, CancellationToken)`,
  `Task<EdgeOutcome> WarmAsync(IReadOnlyList<string> urls, CancellationToken)`). In `AddEdge`:
  bind and register the options; register `CloudflareEdgeProvider` when both `ZoneId` and
  `ApiToken` are non-empty, else `LogOnlyEdgeProvider` (FR-042, decisions row 6).
- [X] T013 Create `src/Gallery.Web/Edge/PurgeSets.cs`, pure and static, exactly per
  contracts/edge.md: `HasPage`, `Listed`, `Home`, `Changed` (using `GalleryNav.Neighbours`),
  `Pages`, and one method per set (`Members`, `Visibility`, `Rename`, `Description`, `Order`,
  `GalleryDelete`, `Text`, `Delete`) from BEFORE/AFTER snapshots
  (`GallerySnapshot(Id, Slug, Visibility, Items)`). Addresses are absolute: pages on
  `Edge:SiteBaseUrl`, files on `Storage:ImageBaseUrl`; de-duplicated and ordinally sorted.
  Add `WarmFiles(purgedPages, after, homeCards, avifKeysOf)` (FR-039, contracts/edge.md "Warm"):
  for each purged page that still exists AFTER the change, every AVIF width URL of its first-row
  images (home: the first 3 `homeCards` covers; gallery page: the first 4 `items`; photo page: the
  photo itself); never a file in the deleted set. `avifKeysOf` is a lookup the caller fills from
  the image documents it already read.
- [X] T014 Write `tests/Gallery.Web.Tests/PurgeSetTests.cs` (pure, SC-004): one fact per
  contracts/edge.md row, each asserting set EQUALITY, plus: a gallery emptied by the change is
  still purged; a private gallery with photos is purged and `/` is not; rename to the same slug
  gives one slug's paths only; a private-only gallery move gives an empty P-ORDER; P-TEXT adds `/`
  only when the image is the cover of a listed gallery (decisions row 24); P-DELETE files are
  exactly the keys passed as deleted. `WarmFiles`: a purged gallery page of 6 photos gives the
  AVIF URLs of exactly its first 4; `/` gives the first 3 covers; a page that 404s after the change
  gives none; a deleted key is never returned.
- [X] T015 Create `src/Gallery.Web/Edge/EdgeQueue.cs` (implements `IEdgeCache`; bounded
  `Channel<EdgeWork>` of `QueueCapacity`; a full queue logs `edge.dropped {op} {count}` and never
  blocks or throws; `Task<bool> WhenIdleAsync(TimeSpan timeout)` completes when the queue is empty
  and no work item is in flight, for the removal command and the evidence test) and `src/Gallery.Web/Edge/EdgeWorker.cs` (`BackgroundService`): per work item
  it splits pages and files into calls of at most `MaxUrlsPerCall`, spaces calls so no more than
  `MaxUrlsPerSecond` URLs leave per rolling second, purges, then warms, logging `edge.purge` per
  call, `edge.warm` per warm batch and `edge.lag` after the warm (templates verbatim). Page URLs
  are logged with outcome `skipped` and are neither purged nor warmed unless `PurgePages` is true
  (decisions row 21). A provider exception is logged as outcome `failed` and never stops the
  worker. Create `src/Gallery.Web/Edge/LogOnlyEdgeProvider.cs` (`Mode` = `log-only`, returns
  `skipped`, logs every address). Register queue and worker in `AddEdge`.
- [X] T016 **[SAFETY]** Create `src/Gallery.Web/Edge/CloudflareEdgeProvider.cs` (III.9-style
  credential handling, FR-043, research R-06): a named `HttpClient` from `IHttpClientFactory`;
  `POST https://api.cloudflare.com/client/v4/zones/{ZoneId}/purge_cache` with JSON
  `{"files":[…]}` and the header `Authorization: Bearer {ApiToken}`; one retry after 2 s on HTTP
  429 or error code 1134, then outcome `rate-limited`; any other non-success → `failed`. Warm is
  an anonymous `GET` per file URL (no cookie, no Authorization header), timed as
  `edge.warm.request`. Every call is timed as `edge.purge.call`. The token goes ONLY into that
  header: never into a URL, a log message, an exception message or `ToString()`.
- [X] T017 Create `tests/Gallery.Web.Tests/Fixtures/RecordingEdgeProvider.cs` (`Mode` =
  `recorder`; records every purge and warm call's URL list; `Gate` is an optional
  `TaskCompletionSource` that blocks calls until released; `AllPurged()` returns the union) and
  write `tests/Gallery.Web.Tests/EdgeWorkerTests.cs`: 250 file URLs → exactly 3 purge calls of
  100, 100 and 50; with `MaxUrlsPerSecond=100` the third call starts ≥ 2 s after the first;
  `PurgePages=false` → page URLs only in a `skipped` `edge.purge` line, never in a recorder
  call; `PurgePages=true` → the page URLs are purged by the provider AND each is warmed with an
  anonymous GET (IV.10; the hosting feature turns this on); a throwing provider → one `failed`
  line and the next work item still runs; a queue of
  capacity 1 with a blocked gate → `edge.dropped`; one `edge.lag` per work item; `WhenIdleAsync`
  returns false while the gate is closed and true after it opens; the log-only
  provider logs every address with outcome `skipped`.
- [X] T018 **[SAFETY]** Write `tests/Gallery.Web.Tests/CloudflareEdgeProviderTests.cs` with a fake
  `HttpMessageHandler` and the test token `test-edge-token-0123456789abcdef`: the request URL is
  exactly the zone purge URL; the body is `{"files":[…]}`; the `Authorization` header is exactly
  `Bearer test-edge-token-0123456789abcdef`; 429 then 200 → two requests and outcome `ok`; 429
  twice → outcome `rate-limited`; warm requests carry no `Authorization` and no `Cookie`; the token
  appears in no captured log line and in no request URL.
- [X] T019 Create `src/Gallery.Web/Library/PageCacheEvictor.cs` (scoped):
  `EvictAsync(IEnumerable<string> pagePaths)` calls `IOutputCacheStore.EvictByTagAsync
  (PublicCache.PageTag(path))` for each distinct path, inside `OpTimer.Time("cache.evict")`; it
  takes page PATHS (no host). Register it in `AddEdge`. Write
  `tests/Gallery.Web.Tests/PageCacheEvictorTests.cs` (`Site:OutputCache=true`, same shape as
  T002): evicting a gallery path re-reads only that page; evicting an unknown path is a no-op.

**Checkpoint (parent)**: merge A+B; delete `artifacts/exec-*`; full build and full suite green;
`git diff --stat 27eef20 -- tests/` shows only new files. Grep for each [SAFETY] mechanism:
`BsonIgnoreIfNull` on `GalleryDoc.SeedKey`; `partialFilterExpression`/`Exists("seedKey")` in
`Indexes.cs`; `Bearer` only in `CloudflareEdgeProvider.cs` and no `ApiToken` inside any `Log*(`
call; `admin.MapLibrary()` before the catch-all.

## Phase 4: User Stories 1 and 2 (P1) 🎯 MVP: write services, Batch C (one executor, 8 tasks)

**Goal**: every owner operation exists as a service method with its gate, its purge set and its
invariants, proven without pages.
**Independent test**: service-level tests over `LibraryHost`, with `InvariantChecker` and the
recorder after every operation.
**Owns**: `tests/Gallery.Web.Tests/Fixtures/LibraryHost.cs`,
`src/Gallery.Web/Library/{GalleryService,ImageService,ImageDeleteService,IObjectDeleter,OperationResult}.cs`,
`LibrarySetup.cs` (additive),
`tests/Gallery.Web.Tests/{GalleryServiceTests,ImageServiceTests,ImageDeleteTests,LibraryStorageTests}.cs`.

- [ ] T020 [US1] Create `tests/Gallery.Web.Tests/Fixtures/LibraryHost.cs`, a wrapper over
  `AdminHost.CreateAsync(mongo, fakeGoogle, overrides, configure)` (research R-12):
  - storage overrides from `S3ProxyFixture.StorageOptions`; `Site:OutputCache=false` unless the
    caller overrides it;
  - `configure` replaces `IEdgeProvider` with one `RecordingEdgeProvider`, exposed as `Recorder`;
  - signs in `owner-1` and caches the CSRF token from `TokenAsync("/admin")` (calls
    `AdminHost.MintSession`/`SendAsync`; no reflection, no copies);
  - helpers: `InsertUploadedImageAsync(createdAt)` (an upload-shaped `ImageDoc` from
    `SeedShapes`, its derivative files and `masters/{id}.jpg` PUT to S3Proxy), `InsertSeededAsync`,
    `PostFormAsync(path, fields, csrf: true)`, `PostBareAsync(path)` (no body, no content type),
    `Scope()` for services, `Db`, `ListAsync(bucket)`, `LogLines`.
  Prove it once in `GalleryServiceTests.cs`: GET `/admin` → 200, the token is non-empty, and
  `IEdgeProvider` resolves to the recorder.
- [ ] T021 [US1] Create `src/Gallery.Web/Library/OperationResult.cs` (data-model "Value objects")
  and, in the same file, `OperationScope`: it collects the BEFORE and AFTER `GallerySnapshot` of
  every gallery an operation touches, the deleted public keys and the counts; one scope per
  request; `CompleteAsync()` runs card rebuilds, `SiteProjector.ProjectAsync`, eviction and ONE
  `Enqueue` exactly once, so a bulk loop of single-item calls flushes once,
  and `src/Gallery.Web/Library/GalleryService.cs` part 1 (scoped). Every write follows
  contracts/http.md "Every write": the ordered single-document updates of research R-01, then
  `CardBuilder.Build` for each affected gallery, `SiteProjector.ProjectAsync`,
  `PageCacheEvictor.EvictAsync(page paths)`, then `IEdgeCache.Enqueue` (never awaited further). The enqueued `EdgeWork`
  carries `PurgeSets.WarmFiles(...)` computed from the AFTER state it already read:
  - `CreateAsync(title)`: trimmed title 1..`TitleMax`, `OpaqueId.NewId()`, `Slug.From`,
    visibility `private`, empty `items`, NO `seedKey`, appended to `homeOrder`; no purge (L-10).
  - `AddOneAsync(gid, iid, OperationScope)`: THE single-item add gate (unknown or tombstoned
    image → skipped, already a member → skipped, else appended and `$addToSet galleryIds`).
  - `AddAsync(gid, ids)`: calls `AddOneAsync` once per id in the given order inside one
    `OperationScope`, then card, projection, eviction and ONE `EdgeWork` (P-MEMBERS) per request
    (contracts/edge.md "A bulk operation enqueues ONE EdgeWork"; decisions row 11).
  - `RemoveAsync(gid, iid)` (clears the pin when it matched; idempotent), `ReorderAsync(gid, ids)`
    (exact-set check, then a write filtered on the array read → otherwise `Stale`),
    `SetCoverAsync(gid, iid?)` (non-member → `Refused` 422; null unpins).
- [ ] T022 [US1] **[SAFETY]** `GalleryService` part 2 (II.5, II.6, FR-029–FR-033):
  - `PublishAsync(gid, confirmed)` in the contracts/http.md G4 order: load → `items` empty →
    `Refused(409, "Add a photo before making this gallery public")` FIRST, whatever else was sent
    → not confirmed → `NeedsConfirm` with no write → `updateOne({_id, "items.0":{$exists:true}},
    {$set:{visibility:"public"}})` → matched 0 → the same refusal. Test seam: an optional
    constructor parameter `Func<string, Task>? beforePublishWrite` (default null; DI never sets
    it) runs just before the conditional write.
  - `UnpublishAsync(gid)` writes at once. Both log `owner.action gallery.publish|unpublish`.
  - `UpdateTextAsync(gid, title?, description?)`: null keeps the stored value; title trimmed
    1..`TitleMax`; description 0..`DescriptionMax`; a slug change gives P-RENAME (both slugs),
    else P-DESC when the description changed, else nothing.
  - `DeleteAsync(gid)` (L-17): removes it from `homeOrder`, `$pull`s it from every member image's
    `galleryIds`, deletes the document, projects, P-GDELETE, `owner.action gallery.delete`;
    a missing gallery → `NotFound` (the endpoint answers 303 `deleted=0`).
  - No method other than `PublishAsync` writes `visibility:"public"` (T049 scans for it).
- [ ] T023 [US2] **[SAFETY]** Create `src/Gallery.Web/Library/ImageService.cs` (scoped):
  - `UpdateTextAsync(iid, title?, caption?)`: null keeps the stored value; title trimmed, 1..
    `TitleMax`, whitespace-only → `Refused(422, reason)`; caption 0..`CaptionMax`, an empty caption
    stores null; the cards of galleries holding it are rebuilt; P-TEXT (FR-038).
  - `BulkAddAsync(ids, gid | newTitle)`: `ids.Count` 1..`PageSize` else `Refused(422)`; `new`
    calls `GalleryService.CreateAsync` first; then ONE call to `GalleryService.AddAsync` (which
    applies `AddOneAsync` per id). Result: added, skipped, gallery title (FR-012, UI-6). Timed as
    `library.bulk.add`.
  It never writes `visibility` (II.5).
- [ ] T024 [US5] **[SAFETY]** Create `src/Gallery.Web/Library/IObjectDeleter.cs` (`Task
  DeleteAsync(string bucket, string key)`, default implementation calls the existing
  `ImageStore.DeleteAsync`; registered scoped) and `src/Gallery.Web/Library/ImageDeleteService.cs`
  exactly per research R-02:
  1. `$set deletingAt` (op `db.image.tombstone`); a missing image → `NotFound`;
  2. for each gallery whose `items` contains the id (query by `items`): `$pull`, unset a
     matching `pinnedCover`, rebuild the card; then `SiteProjector.ProjectAsync`;
  3. delete `masters/{id}.jpg` from the private bucket; for each derivative key (AVIF and WebP,
     every width) delete it from the public bucket UNLESS another image without `deletingAt`
     references it (op `db.images.shared`); at most 8 deletes in flight; a missing object counts
     as deleted; each failure logs `library.delete_leftover {image} {bucket} {key} {error}`, is
     counted as a leftover and never stops the sequence;
  4. delete the document (op `db.image.delete`);
  5. P-DELETE with exactly the public keys actually deleted; `owner.action image.delete`.
  Run 1–5 inside `OpTimer.Time("library.image.delete")`. A repeat on a tombstoned image resumes at
  step 2. `DeleteManyAsync(ids)` (1..`PageSize`) calls `DeleteAsync` once per id, sequentially,
  inside one `OperationScope` and enqueues ONE `EdgeWork` (timed `library.bulk.delete`). Test seam:
  optional constructor parameter `Func<string, Task>? afterGalleries` (default null) runs after
  step 2.
- [ ] T025 [US1] Write `tests/Gallery.Web.Tests/GalleryServiceTests.cs` (`[Collection("s3")]` +
  MongoFixture, `LibraryHost`). After every operation `InvariantChecker.AssertAsync` passes and the
  recorder's set EQUALS the contracts/edge.md set:
  - create: private, last in `homeOrder`, the stored BSON has no `seedKey` element, zero recorder
    calls, and no gallery's visibility changed (II.5);
  - add 40 then 40 with 5 overlapping: `added`/`skipped` exact, order = given order;
  - reorder with one id missing → `Stale`, `items` unchanged; cover of a non-member → 422;
  - remove the pinned cover → pin cleared, card cover = `items[0]`;
  - rename → new slug, both slugs' pages in the set; same-slug rename → one slug only;
  - delete gallery → images keep their other galleries and stay in `images`.
- [ ] T026 [US5] **[SAFETY]** Write `tests/Gallery.Web.Tests/ImageDeleteTests.cs` (SC-008,
  FR-014–FR-017):
  - `Shared_public_file_is_kept`: images X and Y share one derivative key; delete X → the shared
    key still exists, X's other keys and `masters/{X}.jpg` are gone, the P-DELETE file set is
    exactly X's unshared keys;
  - `Missing_master_counts_as_deleted` (a seed-shaped image with no master): result `deleted`,
    zero leftovers;
  - `One_failed_storage_delete_of_three`: an `IObjectDeleter` that throws for one key (replaced
    through `configure`) → all 3 images gone from `images` and every gallery, leftovers 1, one
    `library.delete_leftover` line with the exception type name;
  - `Crash_after_galleries_then_repeat`: `afterGalleries` throws once → the image is tombstoned,
    in no gallery, hidden from the library query; a second `DeleteAsync` finishes it;
  - `Pinned_cover_of_a_public_gallery_and_member_of_a_private_one`: both galleries lose it, the
    public pin falls back, `homeCards` shows the new cover, the set covers both galleries' pages
    and `/`.
- [ ] T027 [US2] Write `tests/Gallery.Web.Tests/ImageServiceTests.cs`: title/caption saved and
  trimmed; whitespace-only and 201-char titles and a 2001-char caption → 422 with the stored values
  unchanged; null fields keep values; P-TEXT set exact for an image in a public gallery (incl. `/`
  when it is the cover), and ZERO recorder calls for an image in no gallery with a page (US2 AS-4);
  a bulk add of `PageSize+1` ids → 422 with nothing written; a bulk add to a new gallery creates
  it private. Also write `tests/Gallery.Web.Tests/LibraryStorageTests.cs` (abstract plus
  `EmulatorLibraryStorageTests` `[Collection("s3")]` and `R2LibraryStorageTests`
  `[Trait("Category","R2")]`, skipping exactly like `R2StorageTests`): deleting a missing key does
  not throw (research R-05, probe P3).

**Checkpoint (parent)**: full suite green; grep: `"items.0"` with `$exists` in GalleryService;
`db.images.shared` check before every public delete in ImageDeleteService; `AddOneAsync` called
from `AddAsync` (no second add path); `DeleteAsync` called from `DeleteManyAsync`.

## Phase 5: User Stories 1 and 2 (cont.): library HTTP and page, Batch D (one executor, 8 tasks)

**Goal**: L1–L5 and G2–G4/B2–B6 answer per contracts/http.md; the library page works.
**Independent test**: HTTP tests through `LibraryHost`, no browser.
**Owns**: `src/Gallery.Web/Library/{LibraryReader,LibraryEndpoints}.cs`, `LibrarySetup.cs`
(additive: `admin.MapLibraryEndpoints()` inside `MapLibrary`),
`src/Gallery.Web/Admin/Pages/LibraryPage.razor` (+ `.razor.css`),
`src/Gallery.Web/Components/Atoms/{Switch,Input}.razor`,
`src/Gallery.Web/Components/Molecules/SelectionBar.razor`,
`tests/Gallery.Web.Tests/{LibraryComponentAdditionsTests,LibraryPageTests,LibraryEditTests,LibraryFlowTests,LibraryAccessTests}.cs`.

- [ ] T028 [US1] Create `src/Gallery.Web/Library/LibraryReader.cs` (research R-04):
  `PageAsync(page)` with the four timed reads (`db.library.page`, `db.library.count`,
  `db.galleries.list`, `db.site.order`), excluding tombstoned images, sorted
  `{createdAt:-1,_id:-1}`, `PageSize` per page, a page past the end → the last page. Each tile
  carries the status line (FR-007 copy), the gallery and public-gallery counts, and the View href
  `/g/{gid}/{slug}/{iid}` of the first gallery in `homeOrder` holding it (or null). `GalleryRef`
  list in home order for the picker.
- [ ] T029 [US1] Create `src/Gallery.Web/Library/LibraryEndpoints.cs` with every POST route of
  contracts/http.md (L2–L5, G2–G4, B2–B6) and GET L1, calling the Batch C services only:
  - `OpaqueId.IsValid` on `{gid}`/`{iid}` or 404;
  - forms read only when `HasFormContentType`; a bare POST is "no fields" (never
    `ReadFormAsync` on a bodiless request);
  - success → 303 with the contracted status query; refusal → the page re-rendered with the
    contracted status code and the reason in `role="status"`, nothing stored;
  - `ids` limited to `Library:PageSize` (422 above it; decisions row 30);
  - G4 with `from=list|builder` redirects to that page.
  G1 and B1 GET are added by Batch E; until then G4/B-route refusals re-render through a minimal
  `role="status"` page in this file, replaced by E.
- [ ] T030 [P] [US1] Component additions K1, K2, K5 (contracts/components.md), as attribute or
  RenderFragment EXPRESSIONS, never new `@if` blocks beside existing markup:
  `Switch.FormId`/`Value`; `Input.Required`/`Pattern`; `SelectionBar.Render`/`SelectAllLabel`/
  `Limit`. Write `tests/Gallery.Web.Tests/LibraryComponentAdditionsTests.cs` (HTML-decoded,
  attribute regexes): each default render is byte-identical to the render with the parameter
  omitted; each set parameter renders its attribute; FALSE cases (UI-5): `Required=false` → no
  `required` attribute at all, `Checked=false` → no `checked` attribute at all (and no
  `checked="False"`), `Render=true Count=0` → the bar carries `hidden`. Then run
  `UiPagesUnchangedTests` and `AtomComponentTests`/`MoleculeComponentTests` UNMODIFIED: green.
- [ ] T031 [US1] Create `src/Gallery.Web/Admin/Pages/LibraryPage.razor` exactly per
  contracts/components.md C1 (hidden forms, ContactSheet 4/3/1, `SelectMark` overlay, caption row
  with status, Edit and View, SelectionBar with `Limit=@PageSize`, the gallery picker dialog with
  the public note, the bulk `DangerConfirm`, the side panel with bound Title/Caption inputs whose
  `maxlength` come from `LibraryOptions`, the danger area, Pager, empty state, the
  `AdminAssets.UrlFor("admin-assets/library.js")` module script). With `?open`, the panel's Save
  is Primary and the bar's "Add to gallery…" is Secondary. Acceptance criteria UI-1, UI-2,
  UI-3, UI-4, UI-7 and UI-8.
- [ ] T032 [US1] Write `tests/Gallery.Web.Tests/LibraryPageTests.cs`:
  - FR-002: the owner bar's Library link carries `aria-current="page"` and Galleries does not;
  - 101 uploaded images: page 1 holds 100 newest first, page 2 holds 1, `?page=9` renders page 2;
  - empty library: "No photos yet." with the `/admin/upload` link;
  - status lines: "Not in a gallery", "In 2 galleries", "In 2 galleries · public in 1";
  - UI-1: a tile in a gallery with photos has `View ↗` to `/g/{gid}/{slug}/{iid}` of the first
    gallery in home order; an image in no gallery has no View link;
  - UI-2: the only element inside the tile's photo cell is the `pos-tl` SelectMark; status, Edit
    and View are inside `.lib-row` in the caption;
  - UI-3: outside `<dialog>` elements, exactly one `btn-primary`: without `?open` it is the
    selection bar's "Add to gallery…"; with `?open` it is the panel's Save and "Add to gallery…"
    renders `btn-secondary` (contracts/components.md C1); UI-4: `Delete…` and
    `Delete photo` carry `btn-danger` and not `btn-primary`;
  - UI-7: with override `Library:TitleMax=50` and `Library:PageSize=20` the title input has
    `maxlength="50"`, `required`, `pattern`, and the bar `data-limit="20"`;
  - UI-8: the body contains none of "required", "missing", "No caption", "needs", "warning"
    (case-insensitive, outside attribute names) with a fresh, untitled-looking upload;
  - FR-011: the picker lists galleries in home order with "· Public|Private · n photos"; public
    options carry "Visitors will see these photos once they're added."; private ones do not.
- [ ] T033 [US2] Write `tests/Gallery.Web.Tests/LibraryEditTests.cs` (US2, FR-009/FR-010):
  - L2 title+caption → 303 `saved=1`; GET `?open={iid}` shows the stored values in the inputs
    (title `value`, caption textarea body) and "Saved";
  - blank, whitespace-only and 201-char titles and a 2001-char caption → 422 with the reason in
    `role="status"`, stored values unchanged;
  - `PostBareAsync` on L2 → 303 and BOTH stored values unchanged (the bare-POST class);
  - the recorder set for an image in a public gallery equals P-TEXT; for an image in no gallery
    with a page, zero calls;
  - FR-044 end to end, on a host with `Site:OutputCache=true`: GET the image's photo page and `/`
    (both cached), save a new title through L2, GET both again → the photo page shows the new title
    (a second `db.gallery.read`), while `/` is still served from the cache (one `db.site.read`)
    because the image is not a cover;
  - the panel's gallery list links each holding gallery's builder; delete copy "It is in 2
    galleries. This removes it from every gallery and deletes the original file. It can't be
    undone." ("It is in no gallery." for 0).
- [ ] T034 [US1] Write `tests/Gallery.Web.Tests/LibraryFlowTests.cs` (US1 path, SC-001):
  40 uploaded images → (1) L3 `gallery=new newTitle=Coast` with the 40 ids → 303 to the builder
  and `?added=40`; (2) B3 reorder moving the 4th first; (3) G4 `public=on` without `confirm` → 200
  with the publish warning copy and visibility still private; (4) G4 with `confirm=1` → 303. That
  is at most 6 owner actions after the library GET (SC-001). Then: GET `/` lists Coast with the
  moved photo as cover; library tiles say "In 1 gallery · public in 1"; the recorder holds exactly
  P-MEMBERS ∪ P-VIS for these steps; L3 of the same 40 plus 5 new → "5 added · 40 were already
  in Coast" via the 303 query and the rendered status. InvariantChecker after each step.
- [ ] T035 [US1] **[SAFETY]** Write `tests/Gallery.Web.Tests/LibraryAccessTests.cs` (FR-001, I.3,
  I.6): a theory over all 15 routes of contracts/http.md (L1–L5, G1–G4, B1–B6; G1/B1 skipped with
  a reason until Batch E lands, then enabled by E in T040): without a session GET → 302
  `/admin/signin` and POST → 401; with a session and NO antiforgery token every POST → 400 with a
  collection dump of `images`, `galleries` and `site` identical before and after; every response
  carries `Cache-Control: no-store` and `noindex`.

**Parent task after Batch D**:

- [ ] T036 [US1] (high tier, parent) Stated requirement change, FR-001 (contracts/http.md "Changed
  existing test"): in `tests/Gallery.Web.Tests/AdminAccessTests.cs`, rename
  `Owner_GetAdminUpload_returns_200_and_library_still_404` to
  `Owner_GetAdminUpload_and_library_return_200`; assert 200 for `/admin/upload` and
  `/admin/library`, and 404 for `/admin/not-built`. Leave every other assertion in the file
  untouched, and state the change in the commit body and in decisions.md.

**Checkpoint (parent)**: full suite green; `git diff --stat 27eef20 -- tests/` lists only new files
and `AdminAccessTests.cs`; grep `HasFormContentType` before every form read in
`LibraryEndpoints.cs`.

## Phase 6: User Stories 3, 4 and 5: gallery list, builder and delete, Batch E (one executor, 7 tasks)

**Goal**: the gallery list and builder pages with visibility, ordering, covers, rename and delete;
image delete over HTTP.
**Independent test**: HTTP tests through `LibraryHost`.
**Owns**: `src/Gallery.Web/Admin/Pages/{GalleriesPage,GalleryPage}.razor` (+ `.razor.css`),
`src/Gallery.Web/Components/Molecules/{VisibilityToggle,CoverMarker}.razor`,
`src/Gallery.Web/Components/Organisms/GalleryBuilder.razor`, `LibraryEndpoints.cs` (G1, B1 GET
and the page re-renders only), `LibraryComponentAdditionsTests.cs` (appended facts only),
`LibraryAccessTests.cs` (enabling the G1/B1 cases only),
`tests/Gallery.Web.Tests/{GalleryListTests,PublishRefusalTests,BuilderTests,LibraryDeleteTests}.cs`.
Runs in parallel with Batch G (disjoint files).

- [ ] T037 [US4] Component additions K3, K4, K6 (contracts/components.md) as expressions, never
  new `@if` blocks: `VisibilityToggle.DisabledReason` (and the K1 `FormId`/`Value="on"`
  pass-through), `CoverMarker.ImageId`/`UnpinFormId`, and the `GalleryBuilder` K6 parameters.
  Append to `LibraryComponentAdditionsTests.cs`: each default render byte-identical; K6 renders,
  inside `.b-ctl` under each title, in order DragHandle, CoverMarker, `Remove` (Quiet,
  `name="image" value="{id}" form=RemoveFormId`) and `View ↗` (only with `PhotoHrefBase`), and no
  `pos-` overlay class anywhere in a tile (UI-2); FALSE cases (UI-5): `Public=false` → the switch
  input has NO `checked` attribute, `DisabledReason=null` → NO `disabled` attribute, and the
  reason text is absent; `DisabledReason` set → `disabled` and the exact reason. Run
  `UiPagesUnchangedTests`, `OrganismComponentTests` and `OrganismStatePageTests` UNMODIFIED: green.
- [ ] T038 [US3] Create `src/Gallery.Web/Admin/Pages/GalleriesPage.razor` exactly per
  contracts/components.md C2 and map G1 GET. Acceptance criteria UI-1 (View only when the gallery
  has photos; Edit to the builder), UI-2 (status, handle, switch, View, Edit in `.gal-row` under
  the title; the owner bar marks Galleries `aria-current="page"`, FR-002), UI-3 ("New gallery"
  is the one primary; Secondary while a publish warning is shown;
  the dialog's Create is the primary only while the dialog is open), UI-5.
- [ ] T039 [US4] Create `src/Gallery.Web/Admin/Pages/GalleryPage.razor` exactly per
  contracts/components.md C3 and map B1 GET (404 for an unknown gallery). Acceptance criteria
  UI-1 (`View gallery` and per-tile `View ↗` when it has photos, else "Back to galleries"), UI-2,
  UI-3 (Save is the primary; demoted in the PublishWarning state), UI-4 (`Delete gallery…` is
  Danger, Remove is Quiet with no confirmation), UI-7 (title/description limits from
  `LibraryOptions`), UI-8.
- [ ] T040 [US3] Write `tests/Gallery.Web.Tests/GalleryListTests.cs` (US3, FR-018–FR-020,
  FR-029–FR-032): G3 reorder stores public and private galleries alike; the recorder holds `/`
  only when the listed relative order changed (decisions row 32); a stale G3 → 409 with "The
  gallery list changed in another tab. Reload to see the current order."; G2 → 303 to the builder,
  last in `homeOrder`, zero recorder calls; publish from the list redirects to
  `/admin/galleries?visibility=public`; unpublish → gone from `/` at once, `homeOrder`
  unchanged (FR-032), the gallery page now
  has the noindex marker, P-VIS recorded; one `owner.action gallery.publish {gid} …` and one
  `owner.action gallery.unpublish {gid} …` line (FR-050); card statuses (4 variants) and dimmed
  `is-private`;
  UI-5 on the rendered list: the public card's switch has `checked`, the private card's switch
  has no `checked` attribute, the empty card's switch has `disabled` and "Add a photo before
  making this gallery public" while a non-empty card's has no `disabled` attribute. Enable the
  G1/B1 cases in `LibraryAccessTests.cs`.
- [ ] T041 [US3] **[SAFETY]** Write `tests/Gallery.Web.Tests/PublishRefusalTests.cs` (II.6,
  SC-006, FR-030): on an empty private gallery, G4 with `public=on&confirm=1&from=list`, with
  `public=on` only, and as a bare POST (`PostBareAsync`, then also with only the CSRF header) each
  answer 409 with the exact reason in `role="status"`; after each the stored visibility is
  `private`, `/` is byte-identical to before, and the recorder has zero calls. Race: a gallery with
  one photo whose `beforePublishWrite` seam empties `items` → 409, still private. The empty card
  in G1 and the empty builder render the switch `disabled` with the reason.
- [ ] T042 [US4] Write `tests/Gallery.Web.Tests/BuilderTests.cs` (US4, FR-021–FR-028), each step
  followed by `InvariantChecker` and an exact recorder set: no pin, moving a photo first makes it
  the home-card cover; Make cover pins (the control shows "Cover" and "Unpin"), Unpin returns to
  "Cover · first photo"; removing the pinned cover clears the pin and keeps the image in the
  library; removing the last photo of a public gallery → still public, absent from `/`, its page
  404, its list status "Public · hidden until it has a photo", and adding a photo brings it back at
  its position with no publish; rename Coast → Coastline: the old address answers 301 to the new
  one and P-RENAME holds both; B2 bare POST keeps title and description; delete gallery → 303
  `deleted=1`, page 404, images still in the library and their other galleries, one `owner.action
  gallery.delete {gid}` line (FR-050); the empty builder
  shows "No photos in this gallery yet." with the library link.
- [ ] T043 [US5] Write `tests/Gallery.Web.Tests/LibraryDeleteTests.cs` (US5, FR-013/FR-014): L5 →
  303 `deleted=1&leftovers=0`, the image gone everywhere, one `owner.action image.delete {iid}`
  line (FR-050); L4 of 3 with one failing key (the T026
  `IObjectDeleter` override) → 303 `deleted=3&leftovers=1` and the rendered status "3 deleted · 1
  files could not be removed and were logged"; exactly 3 `op.timing library.image.delete` lines
  (the bulk path calls the single path, UI-6); `PageSize+1` ids → 422 and nothing deleted; L5 on
  an already-deleted id → 303 `deleted=0`; the bulk confirm copy matches contracts/components.md.

## Phase 7: User Story 6 (P3): the seeded content is removed, Batch G (one executor, 3 tasks)

**Goal**: the one-time removal command, behind the seed's target guard.
**Owns**: `src/Gallery.Web/Maintenance/{DevTargetGuard,RemoveSeededCommand}.cs`,
`tests/Gallery.Web.Tests/RemoveSeededTests.cs`. It does NOT edit `Program.cs` or anything under
`Seed/` (T047 wires and deletes). Runs after Batch C, in parallel with Batch E.

- [ ] T044 [US6] **[SAFETY]** Create `src/Gallery.Web/Maintenance/DevTargetGuard.cs`:
  `static IReadOnlyList<string> Check(string environment, StorageOptions s, MongoOptions m)` with
  the three checks of contracts/removal-command.md, phrased as `SeedCommand` phrases them today
  (read `Seed/SeedCommand.cs` lines 95–105). Until T047 deletes `SeedCommand`, the two copies
  coexist; T047 removes the old one, and T052's source guard keeps it from coming back.
  An empty list means pass.
- [ ] T045 [US6] **[SAFETY]** Create `src/Gallery.Web/Maintenance/RemoveSeededCommand.cs` exactly
  per contracts/removal-command.md: `public static Task<int> RunAsync(string[] args, TextWriter
  output, IDictionary<string,string?>? overrides = null)`; configuration sources in the contracted
  order with overrides last; the environment is the configuration key `ASPNETCORE_ENVIRONMENT`
  (default `Production`), as `SeedCommand` reads it; exit 2 (`Missing required setting: {key}`,
  never a value), 4
  (`Target guard failed: {check}` per check, nothing touched), 5 (`Stopped: {error type}. Run the
  command again to finish.`), 0 with the seven summary lines verbatim. It builds a service
  host with `Host.CreateApplicationBuilder` and the same registrations the app uses (`AddLibrary`,
  `AddEdge`, Mongo, storage, `OpTimer`, and the existing `AddPublicOutputCache()` so
  `PageCacheEvictor` resolves; its evictions are no-ops in this separate process, which is why the
  output says the running app drops its cache within 60 s). The guard runs BEFORE the host is
  built or started. It starts the host (so `EdgeWorker` runs), CALLS `Indexes.EnsureAsync`,
  `GalleryService.DeleteAsync` and `ImageDeleteService.DeleteAsync` inside a scope, then awaits
  `EdgeQueue.WhenIdleAsync(30 s)` before stopping the host, so the purge goes through the
  configured provider (contracts/removal-command.md). The post-guard body is a separate
  `public static Task<RemovalSummary> RemoveAsync(IServiceProvider services)` (the seven counts),
  which `RunAsync` calls; tests that prove owner content survives but not the guard call it with a
  test host's services.
  The selection filter is exactly `{seedKey:{$exists:true, $nin:[null,""]}}` on both collections.
  Timed as `maintenance.remove-seeded`; logs `owner.action seeded.remove`.
- [ ] T046 [US6] **[SAFETY]** Write `tests/Gallery.Web.Tests/RemoveSeededTests.cs`
  (`[Collection("s3")]` + MongoFixture; each fact opens its own database
  `new MongoClient(mongo.ConnectionString).GetDatabase($"gallery_rs_{Guid.NewGuid():N}_dev")`
  (MongoFixture stays unmodified), buckets `gallery-dev-*` created on S3Proxy; `ASPNETCORE_ENVIRONMENT` passed through overrides, never by mutating the process
  environment):
  - one fact per guard check plus all three failing → exit 4 with exactly the failing checks named,
    and dumps of `images`, `galleries`, `site` and both buckets identical before and after;
  - a missing `Mongo:ConnectionString` → exit 2 naming the key;
  - `Removes_seeded_and_never_touches_owner_content`: 3 seed-shaped images, 2 seed-shaped
    galleries, 2 uploaded images (one sharing a derivative key with a seeded image), 1 owner
    gallery holding a seeded and an uploaded image, a doc with `seedKey: ""`, and a site claim →
    exit 0; zero docs match the filter; the uploaded images and the `seedKey: ""` doc are
    byte-identical (`ToJson()`); the owner gallery lost only the seeded member; `homeCards`
    recomputed; the shared key still exists and is counted as kept; `claimedSubject`, `claimedAt`
    and `claimedIp` unchanged (I.4; the check moved here from the retired
    `OwnerClaimSeedTests.Reseed_keeps_claim`, decisions row 27); the seven output lines match;
  - a second run prints zeros for every count (SC-010, FR-047);
  - the first run logs one `owner.action seeded.remove` line with the counts and one
    `op.timing maintenance.remove-seeded … ok` (FR-049, FR-050).

## Phase 8: Seed retirement and the stated test changes (high tier, parent, after Batches E and G)

- [ ] T047 [US6] (high tier, parent ONLY) **Retire the seed** (FR-045, research R-11, decisions rows 26
  and 27, orchestration "Removal/deletion features"). Step 0: re-run the inventory grep and
  compare it with the list below; any extra hit is a blocker until classified.
  `grep -rn -E "Seeder|SeedCommand|Manifest\b|PhotoJson|seed-manifest|prepare-photos|Gallery\.Web\.Seed|EnsureSeedKeyIndexesAsync|\"seed\"" src tests scripts .gitignore`
  Inventory at 27eef20 (every consumer of the seed):
  - **Delete with `git rm`**: `src/Gallery.Web/Seed/Seeder.cs`, `src/Gallery.Web/Seed/SeedCommand.cs`,
    `src/Gallery.Web/Seed/Manifest.cs`, `src/Gallery.Web/Seed/PhotoJson.cs`, `seed/manifest.json`
    (the `seed/` directory goes with it), `scripts/seed-manifest.sh`, `scripts/prepare-photos.sh`.
  - **Move with `git mv`, content unchanged except the namespace line** (`Gallery.Web.Seed` →
    `Gallery.Web.Upload`): `src/Gallery.Web/Seed/DerivativeValidator.cs` →
    `src/Gallery.Web/Upload/DerivativeValidator.cs`, `src/Gallery.Web/Seed/CameraFields.cs` →
    `src/Gallery.Web/Upload/CameraFields.cs`. Then drop `using Gallery.Web.Seed;` from
    `src/Gallery.Web/Upload/MasterCheck.cs` and `src/Gallery.Web/Upload/UploadValidator.cs`. The
    `Seed/` folder is then empty and gone.
  - **`src/Gallery.Web/Program.cs`** (CRLF; edit in place with `perl -0pi`, never `write_file`):
    the `seed` branch is replaced by `remove-seeded` → `RemoveSeededCommand.RunAsync(args,
    Console.Out)`, and a first argument `seed` prints `Unknown command: seed` and exits 64.
    `AppBuilder.cs` has no seed reference (checked). No other `src/` file references the seed:
    `src/Gallery.Web/Ui/SampleCatalog.cs` has its OWN private `Manifest` record for
    `/ui` samples, which stays (trap: a bare `Manifest` grep hits it).
  - **Deleted test files (stated requirement change)**: `tests/Gallery.Web.Tests/SeederTests.cs`
    (class `SeederWithS3Tests`, 9 facts), `tests/Gallery.Web.Tests/SeedCommandTests.cs` (7 facts),
    `tests/Gallery.Web.Tests/ManifestTests.cs` (15 facts).
  - **Deleted test class (stated requirement change)**: `OwnerClaimSeedTests` (the 007 [SAFETY]
    fact `Reseed_keeps_claim`) at the end of `tests/Gallery.Web.Tests/OwnerClaimTests.cs`, plus
    that file's now-unused `using Gallery.Web.Seed;`. The `OwnerClaimTests` class above it stays
    byte-identical. Its I.4 check lives on in T046.
  - **Rewritten facts, assertions kept (stated requirement change)**:
    - `tests/Gallery.Web.Tests/UploadSeedCoexistenceTests.cs`:
      `Seed_run_leaves_an_uploaded_image_untouched` → `Removal_command_leaves_an_uploaded_image_untouched`
      (seed-shaped docs from `SeedShapes`, then `RemoveSeededCommand.RemoveAsync` over a
      service provider built on the test's database and S3Proxy, as T046's tests do; the uploaded image's `ToJson()` is identical);
      `Seed_run_replaces_legacy_seedKey_index_and_keeps_two_uploads` →
      `Indexes_replace_legacy_seedKey_index_and_keep_two_uploads` (`Indexes.EnsureAsync`
      instead of `SeedAsync`; every index assertion unchanged). The serialisation fact stays
      byte-identical. Drop `using Gallery.Web.Seed;` and the class comment's Seeder sentence.
    - `tests/Gallery.Web.Tests/UploadConfirmTests.cs`: `Seed_run_after_confirm_leaves_uploaded_images`
      → `Removal_command_after_confirm_leaves_uploaded_images` (the control image gets
      `seedKey: "seed-control"`, because the removal filter keeps `""`; it calls
      `RemoveSeededCommand.RemoveAsync` with the host's services, and `summary.ImagesDeleted == 1`
      becomes `SeededImagesRemoved == 1`); `Confirm_after_a_seed_run_adds_both_images`
      → `Confirm_after_seeded_content_adds_both_images` (`SeedShapes` docs plus
      `Indexes.EnsureAsync` before the batch). Every other fact in the file stays byte-identical.
  - **Namespace-only edits forced by the move** (`using Gallery.Web.Seed;` →
    `using Gallery.Web.Upload;` or removed when already imported):
    `tests/Gallery.Web.Tests/CameraFieldsTests.cs` (plus the path in
    `Source_CameraFields_does_not_contain_Gps`: `"Seed"` → `"Upload"`, assertion unchanged),
    `tests/Gallery.Web.Tests/DerivativeValidatorTests.cs`,
    `tests/Gallery.Web.Tests/UploadValidatorTests.cs`.
  - **Allow-list narrowed**: `tests/Gallery.Web.Tests/OwnerWriteGuardTests.cs` loses the
    `Gallery.Web/Seed/Seeder.cs` allowance and its comment mention (the file is gone); the
    assertion stays and is now stricter.
  - **Comments and docs**: `.gitignore` line 491's comment says the folder stays ignored after
    the retirement (the `/.dev-prepared/` and `/dev-pictures/` patterns stay).
    `tests/Gallery.Web.Tests/Fixtures/BrowserSiteFixture.cs` line 141 mentions
    `prepare-photos.sh` only as encoding provenance in a comment; it stays unmodified. Prior
    specs are annotated in place, not edited: add "(Retired by feature 009: the seed command,
    manifest and scripts no longer exist; see specs/009-library-galleries.)" under the headings of
    `specs/006-public-site-local/quickstart.md` Part B and `specs/006-public-site-local/contracts/seed.md`,
    and after the 007 spec edge case "Re-running the dev seed after a claim". No README exists.
    `docs/design.md` already marks III.10 retired (committed). `docs/roadmap.md` is NOT edited.
  Verification, all in this task: the step-0 grep returns only `SampleCatalog.cs`'s private record
  and the `.gitignore` patterns; `dotnet run --project src/Gallery.Web --no-launch-profile -- seed`
  exits 64; `git diff --stat 27eef20 -- tests/` lists exactly the files above plus `AdminAccessTests.cs`
  and the new files; full build `-warnaserror` and full suite green; the expected test count is the
  T001 count minus the deleted test cases (9 + 7 + 15 methods plus `Reseed_keeps_claim`, counted
  per class from the T001 run with theories expanded) plus the new ones. Record every deleted or
  moved file in
  decisions.md in one DEFAULT row and in the commit body.

## Phase 9: Scripts and browser, Batch F (one executor, 4 tasks, after Batch E)

**Owns**: `src/Gallery.Web/wwwroot/admin-assets/{library.js,reorder.js}`,
`tests/Gallery.Web.Tests/Fixtures/LibraryBrowserFixture.cs`,
`tests/Gallery.Web.Tests/LibraryBrowserTests.cs`. It does not edit `BrowserSiteFixture.cs`,
`UploadBrowserFixture.cs` or `S3ProxyFixture.cs`.

- [ ] T048 [US4] Write `src/Gallery.Web/wwwroot/admin-assets/reorder.js` exactly per
  contracts/components.md C4: `<reorder-grid data-form>`; pointer drag on `.handle` with the
  `.drop-slot` ghost; keyboard Space/Enter lift (`aria-pressed="true"`), arrows, Space/Enter drop,
  Escape restores and submits nothing; a polite live region "Position p of n"; on drop it rewrites
  the form's `order` inputs and submits. The form is resolved at use time, never cached in
  `connectedCallback`. No `innerHTML`.
- [ ] T049 [US1] Write `src/Gallery.Web/wwwroot/admin-assets/library.js` exactly per
  contracts/components.md C4: selection count and bar `hidden`; Clear; "Select all on this page"
  ticks only this page's `ids` boxes; a tick that would exceed `data-limit` is refused at selection
  time (box stays unticked, a status line says the limit; UI-7); the picker dialog and the bulk
  delete confirm (ticked ids copied into `lib-delete`); a visibility switch change submits its
  form. Elements declared later are resolved at use time. No `innerHTML`.
- [ ] T050 [US1] Create `tests/Gallery.Web.Tests/Fixtures/LibraryBrowserFixture.cs`
  (`[CollectionDefinition("library-browser")]`), modelled on `UploadBrowserFixture` and
  references/browser-verification.md: its own Mongo database and S3Proxy container; the real app
  via `AppBuilder.Build` with `--environment Development --contentRoot src/Gallery.Web
  --applicationName Gallery.Web`, `Site:OutputCache=false`, the recorder edge provider, FakeGoogle
  and `LogLines`; an owner session cookie injected into the context; headless Chrome
  (`Channel="chrome"`). Fixture guard test: `/admin/library` has a non-trivial stylesheet, the grid's
  computed `display` is `grid`, and `library.js` loaded (the bar element is upgraded).
- [ ] T051 [US3] Write `tests/Gallery.Web.Tests/LibraryBrowserTests.cs`
  (`[Trait("Category","Browser")]`, `[Collection("library-browser")]`), asserting outcomes:
  - drag a builder tile to the front → while the pointer is down a `.drop-slot` is in the grid at
    the landing index, and after the drop the stored `items` order changed (FR-022); the same in
    the gallery list (FR-019);
  - keyboard: focus a handle, Space, ArrowRight ×2, Space → stored order moved by 2, and the live
    region read "Position 3 of n" before the drop; Escape after a move → order unchanged and no
    POST sent;
  - select 3 tiles → "3 selected"; Select all on this page → count = tiles on the page; with
    host `Library:PageSize=5` and 7 images, page 1 shows 5 tiles, Select all gives "5 selected"
    and the bar's `data-limit` is "5" (UI-7; the page never holds more than the limit);
  - the picker shows the public note for a public gallery;
  - publish: toggling the switch of a non-empty private gallery shows the warning; "Make public"
    → public; the switch of a private gallery reads `input.checked === false` after load (UI-5);
  - UI-2 geometry at 1440, 1024 and 390 px: each tile's control row's bounding box lies below the
    photo cell's bottom edge and inside the tile's box; only the select mark intersects the cell;
  - UI-3/UI-4: per state (no selection, selection, side panel open with a selection, picker open,
    publish warning) exactly one visible button OUTSIDE an open modal `<dialog>` has a
    non-transparent computed background, and when a modal dialog is open exactly one inside it does; every delete button
    has a transparent background and a coloured border;
  - no `/admin-assets/` script on `/`, a gallery page or a photo page (FR-003).
  Run the class 3 times; it must be green all 3 times.

## Phase 10: Polish, guards, evidence (high tier unless noted)

**Batch H** (one executor, 4 tasks, after T047, parallel with Batch F; owns only the four test files below):

- [ ] T052 [P] **[SAFETY]** Write `tests/Gallery.Web.Tests/LibrarySourceGuardTests.cs`:
  - (FR-003, IV.22) GET `/`, a gallery page and a photo page (with an owner cookie and without)
    contain no `/admin-assets/`, no `owner-bar`, no `lib-`/`gal-`/`b-ctl` class;
  - (C4) `library.js` and `reorder.js` contain none of `innerHTML`, `outerHTML`,
    `insertAdjacentHTML`, `document.write`;
  - (II.5) in `src/Gallery.Web/Library/` and `src/Gallery.Web/Maintenance/`, the string
    `"public"` assigned to `visibility` appears only inside `GalleryService.PublishAsync`;
  - (FR-045) no file under `src/` or `scripts/` references `Seeder`, `SeedCommand`, `PhotoJson`,
    `seed-manifest` or `prepare-photos`, and `src/Gallery.Web/Seed/` does not exist (T047 has
    landed before this batch).
- [ ] T053 [P] Write `tests/Gallery.Web.Tests/EdgeModeTests.cs` (SC-012, SC-003, FR-040,
  FR-042): a host with NO `Edge:*` settings starts, and every 009 POST succeeds with its contracted
  303; `edge.purge … log-only … skipped` lines carry the addresses. With the recorder `Gate`
  closed, an L3, a G4 and an L4 each return their 303 while the recorder has received ZERO calls;
  opening the gate delivers them.
- [ ] T054 [P] **[SAFETY]** Write `tests/Gallery.Web.Tests/EdgeSecretTests.cs` (SC-011, FR-043):
  a host with `Edge:ZoneId=zone-test` and `Edge:ApiToken=test-edge-token-0123456789abcdef`, and the
  named Cloudflare `HttpClient`'s primary handler replaced through `configure` by a recording fake;
  GET every owner page (L1, G1, B1, `?open=`) and run publish, rename and delete; the token appears
  in no response body or header and in no `LogLines` entry, AND (non-vacuity) the fake saw it in the
  `Authorization` header at least once. Failure messages name the route or event, never the value.
- [ ] T055 [P] Write `tests/Gallery.Web.Tests/InvariantTests.cs` (SC-007, SC-009): one scripted
  sequence over HTTP (create, bulk add, reorder, pin, unpin, remove, publish, rename, unpublish,
  bulk delete, gallery delete, publish refusal, stale reorder) on `LibraryHost`; after EVERY step
  `InvariantChecker.AssertAsync` passes and a fresh GET `/` logs `http.request … / 200 … 1`
  (`db_reads` = 1).

**Parent**:

- [ ] T056 Write `specs/009-library-galleries/evidence/p95.sh` (contracts/log-events.md). Input:
  the plain message lines of a test log or the JSON console log (`jq -r '.Message'`). Per SC it
  prints n, p95 or max, and PASS/FAIL: SC-002 `http.request GET /admin/library 200` p95 ≤ 500;
  SC-003 `http.request POST` on every 009 route p95 ≤ 500 (one row per route); SC-005 `edge.lag`
  max ≤ 60000. n=0 is a FAIL; an empty or unreadable log is a FAIL. Test it on synthetic logs in
  `evidence/p95-selftest/` (plain, JSON, wrong format, empty), which must give PASS, PASS, FAIL and
  FAIL. Commit those logs. It can be written while Batch A runs.
- [ ] T057 Timing evidence. Write `tests/Gallery.Web.Tests/LibraryTimingEvidenceTests.cs`
  (`[Trait("Category","Evidence")]`): a `LibraryHost` with 100 uploaded images (and a second set of
  100 for the bulk delete) GETs `/admin/library` 30 times, then runs 30 of each save (L2, B2, B3,
  B4, B5, G3, G4) plus 5 bulk adds and 5 bulk deletes of 100, waits on `EdgeQueue.WhenIdleAsync(60 s)`,
  asserts at least one `op.timing` line for every op name in the Rules except
  `maintenance.remove-seeded` (T046) and `edge.purge.call`/`edge.warm.request` (dev-zone only;
  T018) (FR-049), and writes `LogLines` (one message per line, in order) to
  `specs/009-library-galleries/evidence/library-timings.log`. Then run `bash
  specs/009-library-galleries/evidence/p95.sh specs/009-library-galleries/evidence/library-timings.log
  | tee specs/009-library-galleries/evidence/p95-output.txt`. SC-002, SC-003 and SC-005 must PASS
  with non-zero n. A miss is reported with the measured p95 in decisions.md (row 22), never hidden.
- [ ] T058 Write `specs/009-library-galleries/evidence/verify.sh`, the **live-server curl
  walkthrough** (tee to `verify-output.txt`), modelled on 008's `evidence/verify.sh`:
  1. The build and test gates.
  2. A throwaway S3Proxy container (stopped by id at exit) with buckets `gallery-dev-public-verify`
     and `gallery-dev-private-verify`, and a fresh Mongo database `gallery_verify009_$(date +%s)_dev`
     on the dev Mongo container. Never touch the `gallery` database or R2.
  3. The built dll on `https://localhost:5094` (one pid, `kill` only it), Development, env
     overrides for Mongo and Storage, no `Edge:*` (log-only), and `Authentication:Google:Authority`
     pointed at `specs/008-upload-pipeline/evidence/fake-oidc.cs` run on 5095 (its own pid), with
     the dummy client `verify-client`/`verify-secret`.
  4. Sign in with curl's cookie jar; confirm 2 photos through the upload API with the committed
     fixtures (as 008's verify does); then L1, L3 new gallery, B3 reorder, G4 without and with
     `confirm=1`, GET `/` lists it, B2 rename → old URL 301, L2 save, a BARE L2 POST keeps the
     values, G4 on an empty gallery → 409, a POST without the token → 400, L5 delete.
  5. Run `remove-seeded` against the verify database after inserting two seed-shaped documents
     with `mongosh`: exit 0 with the counts, then a second run of zeros; then with
     `Mongo:Database=gallery_verify` (no `_dev`) → exit 4 and nothing changed.
  6. Check the captured server log (`evidence/verify-run.log`): one `http.request` per call and
     `edge.purge … log-only … skipped` after each public change. `pkill` appears nowhere.
- [ ] T059 Run `bash specs/009-library-galleries/evidence/verify.sh | tee
  specs/009-library-galleries/evidence/verify-output.txt` on the merged tree. Force-add
  `evidence/*.log` (`git add -f`) after grepping them for 32+-character tokens and redacting.
  Write `evidence/live-walkthrough.md` with the automated observations and a pointer to OWNER-ONLY
  O-03 for the real-browser part. Fold every executor `decisions_made` entry into decisions.md as
  numbered rows (REJECTED where it contradicts a contract, such as a kept seed alias). Run
  `dotnet clean`, build `-warnaserror` (0 warnings) and the full suite; record the new test count in
  `evidence/baseline.txt`. Do not edit `docs/roadmap.md`; put the roadmap status notes in the
  completion report.
- [ ] T060 **[SAFETY]** **Non-vacuity, final task** (high tier). Copy the orchestration template to
  `specs/009-library-galleries/evidence/non-vacuity.sh`, add one `brk` line per guard below, run it
  on a clean committed tree, and tee the output to `evidence/non-vacuity.txt`. Every line must be
  `PASS` (red on an ASSERTION, not an exception), and the restored build must be clean:
  - (a) GalleryService.PublishAsync: remove the empty check → `PublishRefusalTests` (full form).
  - (b) GalleryService.PublishAsync: drop the `"items.0"` filter → the `beforePublishWrite` race
    fact.
  - (c) ImageDeleteService: skip the `db.images.shared` check → `Shared_public_file_is_kept`.
  - (d) ImageDeleteService: delete the record before step 2 → `Crash_after_galleries_then_repeat`.
  - (e) DevTargetGuard: always return an empty list → the RemoveSeededTests environment fact.
  - (f) RemoveSeededCommand: filter `{seedKey:{$exists:true}}` only → the `seedKey: ""` survival
    assertion; (f2) filter `{}` → `Removes_seeded_and_never_touches_owner_content`.
  - (g) AdminEndpoints: map `MapLibrary` on `app.MapGroup("/admin")` → `LibraryAccessTests`.
  - (h) CloudflareEdgeProvider: log the token in the failure line → `CloudflareEdgeProviderTests`
    and `EdgeSecretTests`.
  - (i) GalleryDoc.SeedKey: initialiser `= string.Empty` → the IndexesTests serialisation fact and
    the GalleryServiceTests create fact.
  - (j) GalleryService.CreateAsync: visibility `public` → the create fact (II.5) and
    `LibrarySourceGuardTests`.
  - (k) GalleryService.AddAsync: append without calling `AddOneAsync` → the 40+5 overlap fact.
  - (l) Switch: render `checked="@(Checked ? "checked" : "False")"` → the K1/UI-5 FALSE fact.
  - (m) PublicLayout: add a `<script src="/admin-assets/library.js">` → `LibrarySourceGuardTests`.
  - (n) `p95.sh` on `library-timings.log` reports non-zero n for every row. A zero means a format
    mismatch, not a fast app.

## OWNER-ONLY: post-build live checks (NOT part of the automated run)

These need the owner, the Cloudflare dashboard, the dev database and buckets, or a real browser.
`/speckit-implement` does NOT run them, does not wait for them, and marks the feature implemented
when T001–T060 are done. Their results go into decisions.md and the roadmap status line when the
owner runs them.

- [ ] O-01 Purge credentials and a dev-zone purge run: create a Cloudflare API token limited to
  Cache Purge on the dev image hostname's zone, then `dotnet user-secrets set Edge:ZoneId …` and
  `dotnet user-secrets set Edge:ApiToken …` in `src/Gallery.Web` (key names pinned, decisions row
  21). Verify presence with `dotnet user-secrets list | sed -E 's/=.*/=<set>/'` (never print values).
  Ctrl+R the watch window, delete a photo that is in a gallery, and check the log shows
  `edge.purge … dev-zone … ok` for its deleted file URLs and `edge.warm … dev-zone …` after it; page
  URLs stay `skipped` until hosting. Save the redacted lines as `evidence/dev-zone-purge.txt`.
- [ ] O-02 One-time removal against the dev database and buckets (quickstart Part C step 8):
  `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-launch-profile --
  remove-seeded`; expect the counts, then a second run of zeros, and the home page showing only
  the owner's galleries. Save the output as `evidence/remove-seeded-dev.txt`.
- [ ] O-03 Live browser walkthrough (quickstart Part C steps 1–7) on `https://localhost:5081` in
  the owner's watch window after Ctrl+R, in Chrome and Safari: upload, library, add to a new
  gallery, drag and keyboard reorder, cover pin/unpin, remove, publish with the warning, rename
  with the redirect, title edit visible at once, delete. Check the owner UI review rules UI-1–UI-8
  by eye. Capture the server log and run `p95.sh` on it; record the R2 bulk-delete p95
  (decisions row 22).
- [ ] O-04 Real R2 delete facts: `GALLERY_R2_TESTS=1 dotnet test --artifacts-path artifacts
  --filter Category=R2` (includes `R2LibraryStorageTests` from T027). Save the redacted output as
  `evidence/r2-tests.txt`.

## Requirement coverage (for /speckit-analyze and the parent's checkpoints)

| Requirements | Tasks |
|---|---|
| FR-001 | T003, T029, T035, T036 |
| FR-002, FR-004, FR-005 | T031, T032, T037–T039, T051 |
| FR-003 | T051, T052 |
| FR-006–FR-008 | T028, T030–T032, T049, T051 |
| FR-009, FR-010 | T007, T023, T030, T031, T033 |
| FR-011, FR-012 | T021, T023, T032, T034 |
| FR-013 | T024, T043 |
| FR-014–FR-017 | T024, T026, T033, T043 |
| FR-018–FR-020 | T021, T038, T040, T048, T051 |
| FR-021–FR-028 | T021, T022, T037, T039, T042, T048, T051 |
| FR-029–FR-034 | T022, T037, T040, T041, T052 |
| FR-035, FR-036 | T008–T011, T025, T055 |
| FR-037 | T021, T040, T042 |
| FR-038, FR-039 | T013–T015, T025–T027, T034 |
| FR-040–FR-042 | T012, T015–T017, T053 |
| FR-043 | T016, T018, T054 |
| FR-044 | T002, T019 |
| FR-045 | T047, T052 |
| FR-046–FR-048 | T004, T006, T044–T046, T058 |
| FR-049, FR-050 | Rules, T015, T024, T057 |
| SC-001 | T034 |
| SC-002, SC-003, SC-005 | T053, T056, T057, O-03 |
| SC-004 | T014, T025–T027 |
| SC-006 | T041 |
| SC-007, SC-009 | T009, T055 |
| SC-008 | T026 |
| SC-010 | T046, O-02 |
| SC-011 | T018, T054 |
| SC-012 | T053 |

## Dependencies & Execution Order

- Phase 1 (T001–T003, parent; T002 first after the baseline) → Batches A and B in parallel →
  parent checkpoint.
- Batch C (T020–T027) needs A and B. Batch D (T028–T035) needs C. T036 (parent) follows D.
- Batch E (T037–T043) needs D and T036. Batch G (T044–T046) needs C and runs in parallel with D or
  E (disjoint files, its own artifacts path).
- T047 (parent) needs E and G (the removal command exists; no batch is mid-edit on Upload/).
- Batch F (T048–T051) needs E. Batch H (T052–T055) needs T047 and runs in parallel with F.
- Polish: T056 any time after Phase 1. T057 after F and H. T058–T059 after T047, F and H.
  **T060 is last.**
- OWNER-ONLY O-01…O-04 have no position in this order. Nothing above depends on them.

## Parallel opportunities

- Batches A and B: two executors, `--artifacts-path artifacts/exec-{a,b}`, each with a filter on
  its own classes. T013–T014 (pure) inside B are independent of the rest of B.
- Batch G alongside D or E; Batches F and H together after E.
- Parent work while executors run: T056 (p95.sh and self-test), T058 (verify.sh), and the T060
  break list against the merged tree.

## Executor batches (≈6–8 tasks, verify between batches)

| Batch | Tasks | Count | Runs with |
|---|---|---|---|
| A data + projections | T004–T011 | 8 | B |
| B edge | T012–T019 | 8 | A |
| C write services | T020–T027 | 8 | — |
| D library HTTP + page | T028–T035 | 8 | G |
| E list, builder, delete | T037–T043 | 7 | G |
| G removal command | T044–T046 | 3 | D or E |
| F scripts + browser | T048–T051 | 4 | H |
| H guards + modes | T052–T055 | 4 | F |

## Implementation Strategy

MVP = Phase 1 + Batches A–D: the owner can select photos in the library, put them in a gallery,
order them, publish it and edit titles, with every purge set, the access sweep and the invariants
green. Batch E completes the gallery list, the builder and delete (US3–US5). Batch G and T047
retire the seed (US6). Batch F proves the browser behaviour and the UI review rules; Batch H the
public isolation, the purge modes and the secret scan. Polish produces the evidence. The feature
is accepted after T060. The OWNER-ONLY items follow at the owner's convenience.
