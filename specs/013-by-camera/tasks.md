# Tasks: By Camera

**Input**: design documents in `specs/013-by-camera/`: spec.md, plan.md, research.md (R-xx),
data-model.md, contracts/routes.md (C1-C3), contracts/components.md (K1-K5),
contracts/edge.md (E1-E4), contracts/test-impact.md (A-D), quickstart.md, decisions.md.

**Source of truth**: the contracts and the research decisions. Implement exactly what they say. If
something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: required. The spec's SCs are measured checks, and the constitution requires [SAFETY]
tests, the IX.4 measured layout test and log-verifiable timing. Each batch writes its tests in
the new files it names, in the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081.** Never kill or restart it. Never run
  `pkill` or `killall`, and never bind :5080 or :5081. Build and test into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s --filter <your classes>`
  - macOS has no `timeout`; use `--blame-hang-timeout`. Docker must be running (`docker info`; if
    it is not, `open -a Docker` and poll).
- **Existing tests must pass UNMODIFIED**, except the stated changes in contracts/test-impact.md
  (A) and (C). Only the parent makes those changes. Any other existing-test failure means your
  change is wrong: fix the code, never the test, a fixture or a snapshot. Executors never edit
  existing files under `tests/` or any file under `specs/`.
  - `LibraryHost` images have no camera (`SeedShapes`), so every existing purge-set and
    `SettleAsync` assertion must stay green as is. If one goes red, your camera code changed a
    camera-less path.
- **Repo paths in tests**: use `Render.RepoRoot`.
- **Never commit.** The parent commits after its own gate.
- **No new NuGet package, no new script file, no new `<script>` element, no `style=` attribute.**
  The only script edit is the speculation-rule string, verbatim from research R-07.
- **Keep existing public signatures.** `SiteProjector.ProjectAsync()`, `ProjectWithNavAsync()`,
  `PurgeSets.WarmFiles(...)` and `PhotoPage`'s existing parameters keep their shapes. New
  behaviour goes in new members or optional parameters with defaults that reproduce today's
  output.
- **Names, verbatim**:
  - types `CameraGroupDoc`, `CameraCardDoc`, `CameraSnapshot`, `CameraChange`, `CameraKey`;
  - collection `"cameras"`;
  - timers `db.cameras.load`, `db.cameras.write` (projection) and `db.cameras.list`,
    `db.camera.read` (reads, `isDbRead: true`);
  - paths `/c`, `/c/{slug}`, `/c/{slug}/{iid}`;
  - labels `Trails.CamerasLabel = "By Camera"`, `Trails.CamerasHref = "/c"`.

  No other new log event or op name.
- **Call, never copy**: `ContactSheet`, `PhotoPage`, `PublicLayout`, `Trails`,
  `ExifFormat.Camera`, `Slug.From`, `PurgeSets.Changed`, `NotFoundResult` and
  `StatusAndHeadersResult` are CALLED. A second copy of any of them is a defect.
- **File ownership**: each batch lists the files it owns. The parent owns `AppBuilder.cs`,
  `Program.cs`, everything under `specs/`, and every EXISTING file under `tests/`.
- **[SAFETY]** marks a task implementing II.2 / II.5 (only photos of listed galleries ever appear;
  private-only and unknown cameras are indistinguishable) or the IV.9 / FR-016 read budget. These
  are blocking and non-deferrable, and each gets a deliberate break in T033 that must turn its test
  red ON ITS ASSERTION.
- **Tight assertions**:
  - sets are compared for EQUALITY and read counts with `==`;
  - "absent" checks look for the element or attribute (`href="/g/`, `class="tile"`), never a bare
    substring an inline script could contain;
  - a 404-parity check compares status, the three headers and the body bytes.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back to the parent.
- **Your final message must be ONLY the JSON report** (tasks_completed, build_succeeded,
  tests_passed, decisions_made, blockers).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [X] T001 Record the baseline in `specs/013-by-camera/evidence/baseline.txt`:
  - `git rev-parse HEAD`
  - the full-suite pass, fail and skip counts from
    `dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s`
  - `ls tests/Gallery.Web.Tests/*.cs | wc -l`
- [X] T002 Re-verify contracts/test-impact.md against the tree at T001 (parent, by hand):
  - grep `tests/` for `SpeculationRule`, `nav--home a`, `AnchorRx`, `"(Home|Galleries)"`,
    `ProjectionResult(`, `"cameras"` and `/c/`;
  - confirm every class A row, line for line, and that no class D assumption moved;
  - edit test-impact.md if anything differs before Batch B is dispatched.

## Phase 2: Foundational: the camera projection (blocks every story)

**Batch A**: one executor. It owns:
- `src/Gallery.Core/Images/CameraKey.cs` (new)
- `src/Gallery.Web/Data/Documents.cs` (additive)
- `src/Gallery.Web/Data/Indexes.cs` (additive)
- `src/Gallery.Web/Library/SiteProjector.cs`
- `tests/Gallery.Core.Tests/CameraKeyTests.cs` (new)
- `tests/Gallery.Web.Tests/CameraProjectionTests.cs` (new)
- `tests/Gallery.Web.Tests/CameraSourceGuardTests.cs` (new)

- [X] T003 [P] Create `src/Gallery.Core/Images/CameraKey.cs`, a pure static class:
  - `Normalise(string?)`: trim, collapse whitespace runs to one space, `ToLowerInvariant`; null or
    blank → `""`.
  - `Collapse(string?)`: the same without lower-casing; blank → null.
  - `Key(make, model)`: `Normalise(make) + "\u001f" + Normalise(model)`, or null when both are `""`.
  - `DisplayName(make, model)`: `ExifFormat.Camera(new Exif(Collapse(make), Collapse(model),
    null, null, null, null, null, null))`.

  Test in `tests/Gallery.Core.Tests/CameraKeyTests.cs`:
  - `Canon`/`CANON ` and `Canon  EOS R5`/`canon eos r5` give equal keys;
  - make-only and model-only keys;
  - both blank → null;
  - `DisplayName("Canon","Canon EOS R5")` is `"Canon EOS R5"`;
  - `DisplayName("NIKON CORPORATION","NIKON Z 8")` is `"NIKON Z 8"`.
- [X] T004 [P] Add `CameraGroupDoc` (`_id`, `slug`, `name`, `items`, `rank`, `card`) and
  `CameraCardDoc` (`slug`, `name`, `imageCount`, `cover` as `CoverImageDoc`) to
  `src/Gallery.Web/Data/Documents.cs`, with BSON element names exactly as in data-model.md and
  placed after the galleries section. In `src/Gallery.Web/Data/Indexes.cs` `EnsureAsync`, create on
  `cameras`: `slug_1` unique and `rank_1`, with name constants beside the existing ones.
- [X] T005 [SAFETY] In `src/Gallery.Web/Library/SiteProjector.cs`, add the pure
  `public static List<CameraGroupDoc> Cameras(IReadOnlyList<GalleryDoc> listed, IReadOnlyList<ImageDoc> images, IReadOnlyDictionary<string,string> storedSlugs)`.
  Follow data-model.md "Visibility", research R-02/R-03 and the `slug` rule exactly:
  - an image counts only when its id is in some `listed` gallery's `items`, `DeletingAt` is
    null, and `CameraKey.Key` is not null;
  - each id appears at most once per camera;
  - items are ordered `CreatedAt` desc, then `_id` ordinal;
  - name comes from the first item;
  - rank is by count desc, then name `OrdinalIgnoreCase`, then key ordinal;
  - slug: keep the stored slug while it is unique, otherwise `Slug.From(name)`, then `-2`, `-3`…;
    keys are processed in ordinal key order;
  - the card cover is built from `items[0]` exactly the way `CardBuilder` builds a gallery cover
    (call its helper if it has one; otherwise map the same five fields).

  "Listed" is computed by the caller with the same rule as `Cards` (public and card
  `ImageCount >= 1`). Expose `public static IReadOnlyList<GalleryDoc> Listed(order, galleries)` (public: the test project has no InternalsVisibleTo, and the parent's InvariantChecker rule calls it)
  and use it in both places, so `Cards` behaviour is byte-identical.
- [X] T006 [SAFETY] Extend `ProjectWithNavAsync` in `src/Gallery.Web/Library/SiteProjector.cs`
  per data-model.md "Write", after the nav loop and inside the same `db.site.project` timer:
  - one `images` find for the union of listed items, with the R-05 projection, timed as
    `db.cameras.load`, plus one `cameras` find, timed under the same name;
  - compute `Cameras(...)`;
  - delete the keys that left, and `ReplaceOne` with upsert each document whose BSON differs;
    all writes go inside ONE `db.cameras.write` timer, which runs only when there is at least one
    write.

  `ProjectionResult` gains a third positional member,
  `IReadOnlyList<(CameraGroupDoc? Before, CameraGroupDoc? After)> Cameras`, with one entry per
  changed, added or removed key. `ProjectAsync()` is unchanged.
- [X] T007 [SAFETY] Write `tests/Gallery.Web.Tests/CameraProjectionTests.cs`.

  Pure `Cameras(...)` facts:
  - private-only photo excluded;
  - photo in no gallery excluded;
  - photo in a private and a public gallery included once;
  - photo in two public galleries included once;
  - tombstoned photo excluded;
  - no make or model excluded;
  - case and space variants form one camera;
  - order, rank and cover per R-03;
  - slug stability: an existing stored slug is kept when a second key with the same slug appears,
    and the new key gets `-2`;
  - determinism: the same input twice gives BSON-equal output.

  `LibraryHost` integration facts (images inserted with
  `InsertUploadedImageAsync(shape: i => i.Camera = new CameraDoc { Make, Model })`; galleries
  through `GalleryService`):
  - after publish, the `cameras` collection holds exactly the expected documents;
  - after unpublish, the camera is gone;
  - a second projection with no change makes zero `db.cameras.write` lines;
  - exactly one `op.timing db.site.project ` line per projection still holds.
- [X] T008 [SAFETY] Write `tests/Gallery.Web.Tests/CameraSourceGuardTests.cs`, modelled on
  `NavSourceGuardTests`:
  - in `src/**/*.cs` outside bin and obj, the literal `"cameras"` may appear only in
    `Library/SiteProjector.cs`, `Data/ContentReader.cs`, `Data/Indexes.cs` and
    `Data/Documents.cs`;
  - any `InsertOne`, `ReplaceOne`, `UpdateOne`, `UpdateMany`, `DeleteOne` or `DeleteMany` on a
    `CameraGroupDoc` collection is allowed only in `SiteProjector.cs`;
  - a non-vacuity fact: `SiteProjector.cs` does contain a `ReplaceOneAsync`.

**Checkpoint (parent)**:
- build `-warnaserror`;
- run the full suite, which must still be unmodified and green;
- **parent stated change, test-impact (C)**: add the `InvariantChecker` camera rule in
  `tests/Gallery.Web.Tests/Fixtures/InvariantChecker.cs` (stored `cameras` sorted by `rank` ==
  `SiteProjector.Cameras(Listed(...), images, storedSlugs)`, BSON-equal), then re-run the full
  suite;
- commit.

## Phase 3: User Story 1, browse the cameras and open one (P1) 🎯 MVP

**Goal**: the home page has a "By Camera" link; `/c` lists cameras; `/c/{slug}` shows a camera's
grid. Both look and load like the gallery pages.

**Independent test**: seed photos from two cameras into public galleries. `/` has two links in
order; `/c` has exactly 2 tiles; each `/c/{slug}` shows exactly that camera's photos, each once.

**Batch B**: one executor, after the Phase 2 checkpoint. It owns:
- `src/Gallery.Web/Site/Trails.cs`
- `src/Gallery.Web/Data/ContentReader.cs`
- `src/Gallery.Web/Site/ViewMapper.cs`
- `src/Gallery.Web/Site/PublicEndpoints.cs`
- `src/Gallery.Web/Site/Pages/CameraListingPage.razor` (new)
- `src/Gallery.Web/Site/Pages/CameraPage.razor` (new)
- `src/Gallery.Web/Site/Pages/HomePage.razor`
- `src/Gallery.Web/Components/Templates/PublicLayout.razor`
- `tests/Gallery.Web.Tests/CameraRoutesTests.cs` (new)
- `tests/Gallery.Web.Tests/CameraPageComponentTests.cs` (new)

- [X] T009 [P] [US1] Add `CamerasLabel`, `CamerasHref`, `Cameras` and `UnderCameras(params Crumb[])` to
  `src/Gallery.Web/Site/Trails.cs`, exactly per contracts/routes.md "Breadcrumbs". Existing
  members are unchanged.
- [X] T010 [P] [US1] [SAFETY] Add `CamerasAsync()` and `CameraAsync(slug)` to
  `src/Gallery.Web/Data/ContentReader.cs`, per the routes.md reader table: one find each, the
  projection and sort as listed, `isDbRead: true`.
- [X] T011 [P] [US1] Add `ToCameraCard(CameraCardDoc, Func<string,string> url)` to
  `src/Gallery.Web/Site/ViewMapper.cs`, the same shape as `ToCard`, with href `/c/{slug}` and
  title = name.
- [X] T012 [US1] Create `src/Gallery.Web/Site/Pages/CameraListingPage.razor` (K1) and
  `src/Gallery.Web/Site/Pages/CameraPage.razor` (K2), per contracts/components.md.
- [X] T013 [US1] [SAFETY] Add C1 `/c` and C2 `/c/{slug}` to `src/Gallery.Web/Site/PublicEndpoints.cs`,
  per contracts/routes.md:
  - `/c/` → 301 inside C1;
  - slug regex check, then `CameraAsync`, then `ImagesAsync(items, full: false)`;
  - tiles exactly as the gallery page builds them, with href `/c/{slug}/{iid}`;
  - `Link` preload helpers modelled on `ApplyListingHeaders` and `ApplyGalleryHeaders` (no
    `X-Robots-Tag`);
  - every refusal goes through `NotFoundResult()`;
  - `.CacheOutput` attached when OutputCache is on;
  - update the class summary's route list.
- [X] T014 [US1] `src/Gallery.Web/Site/Pages/HomePage.razor`: `Links` per K4.
  `src/Gallery.Web/Components/Templates/PublicLayout.razor`: replace the speculation-rule string
  with the research R-07 string verbatim (K5). Change nothing else in the script.
- [X] T015 [US1] Write `tests/Gallery.Web.Tests/CameraPageComponentTests.cs` (`Render.Html<…>`):
  - K1 with 0 cards has no `contact-sheet` and has crumbs Home → `/`, then "By Camera" current;
  - K1 with 2 cards has 2 `class="tile"` links to `/c/{slug}`, and the first has
    `fetchpriority="high"`;
  - K2 has tiles with `/c/{slug}/{iid}` hrefs, `id="p-{iid}"` and a `view-transition-name` rule,
    no `gallery-desc`, no `GalleryNeighbours`, and no `<meta name="robots"`.
- [X] T016 [US1] [SAFETY] Write `tests/Gallery.Web.Tests/CameraRoutesTests.cs` (`LibraryHost`; two
  cameras A and B across two public galleries, plus camera C only in a private gallery), part 1:
  - `/` has exactly 2 anchors in `nav.nav--home`: `/g` "Galleries", then `/c` "By Camera";
  - `/c` is 200 with exactly the tiles for A and B, in rank order, and no C;
  - `/c/{a}` shows exactly A's visible photos, each once (one photo sits in both galleries);
  - `/c/` → 301 `/c` with ShortPolicy;
  - read budget `==`: `/c` 1 (`db.cameras.list`), `/c/{slug}` 2, `/` still 0. Every page has
    `Server-Timing`, and with output cache on, a second GET adds no read.
- [X] T017 [US1] [SAFETY] Same file, part 2 (FR-009/FR-010):
  - `/c/{C's slug}`, `/c/no-such-camera` and `/c/BAD!` answer 404 with identical status,
    `Cache-Control`, `X-Robots-Tag` and body bytes;
  - `/c` and `/c/{a}` contain no `href="/g/` (no gallery is linked or named);
  - neither page carries `X-Robots-Tag`.
- [X] T018 [US1] Same file, part 3, repeating the test-impact (B) scans for `/c`, `/c/{a}` and
  (after Batch D) `/c/{a}/{iid}`. Leave a `// T029 adds the photo route` marker in the path array:
  - with the owner session and anonymous: 200, no `/admin-assets/`, no `owner-bar`, no
    `lib-`/`gal-`/`b-ctl` class tokens (copy the assertion helper's logic into a private method;
    you may not edit `LibrarySourceGuardTests`);
  - the speculation rule string is exact on every page.

**Parent stated change (after Batch B)**: apply test-impact (A):
- `HomePageTests.cs` lines 20-21 and 113-114;
- `HomeBrowserTests.cs` lines 64-111;
- `TrailSourceGuardTests.cs` lines 14 and 42.

Log old and new values in decisions.md. Then build `-warnaserror`, run the full suite, and commit.

## Phase 4: User Story 2, private photos stay out and pages stay fresh (P1)

**Goal**: every owner action listed in FR-011 updates the stored cameras and purges exactly the
affected camera pages, in the same single edge work item.

**Independent test**: the edge.md worked-examples table, as pure facts plus `LibraryHost` runs,
each comparing the purged set for equality.

**Batch C**: one executor, after Batch B. It owns:
- `src/Gallery.Web/Edge/PurgeSets.cs`
- `src/Gallery.Web/Library/OperationResult.cs`
- `tests/Gallery.Web.Tests/CameraPurgeSetTests.cs` (new)
- `tests/Gallery.Web.Tests/CameraFreshnessTests.cs` (new)
- `tests/Gallery.Web.Tests/CameraTimingEvidenceTests.cs` (new)

- [ ] T019 [P] [US2] In `src/Gallery.Web/Edge/PurgeSets.cs`, add the following, per contracts/edge.md
  E1 and E2. Existing methods stay byte-for-byte.
  - `CameraSnapshot(Key, Slug, Items)` with `From(CameraGroupDoc)`;
  - the three path helpers;
  - `Cameras(changes, cardsChanged, bases)` (P-CAM);
  - `CameraText(iid, after, bases)` (P-CAMTEXT).
- [ ] T020 [US2] In `src/Gallery.Web/Edge/PurgeSets.cs`, give `WarmFiles` a new optional last
  parameter `IReadOnlyList<CameraCardDoc>? cameraCards = null`:
  - `/c` warms the first 3 card covers' AVIF keys;
  - `/c/{slug}` and `/c/{slug}/{iid}` are skipped by the `parts[0] != "g"` branch, as today;
  - the scope supplies their ids (E4).

  With `cameraCards` null, the output is identical to today's for every input.
- [ ] T021 [US2] [SAFETY] Wire the camera sets into `src/Gallery.Web/Library/OperationResult.cs`,
  per contracts/edge.md E3 and E4:
  - keep the camera BEFORE of each key from its first appearance in any projection result in the
    scope, and its AFTER from the last;
  - `cardsChanged` is true when any pair's `card` or `rank` differs, or a side is null;
  - add `PurgeSets.Cameras` and, for every `TextImages` id, `PurgeSets.CameraText` over the
    scope's latest AFTER cameras. Those come from one `db.cameras.load`-timed `cameras` find in
    `CompleteAsync`, only when `TextImages` is non-empty and no projection result already holds
    that camera;
  - extend the warm-id loop with the `/c/{slug}` (first 4) and `/c/{slug}/{iid}` shapes from the
    AFTER snapshots;
  - pass the AFTER camera cards sorted by rank to `WarmFiles`.

  It must stay ONE `Enqueue` per scope.
- [ ] T022 [P] [US2] Write `tests/Gallery.Web.Tests/CameraPurgeSetTests.cs` with pure facts:
  - every row of the edge.md worked-examples table (set equality);
  - a slug change purges both slugs;
  - `CameraText` never contains `/c`;
  - [SAFETY] no camera set ever contains a `/g/` path or `/`;
  - `WarmFiles` with `cameraCards` null equals today's result on the existing `PurgeSetTests`
    inputs (call the same fixtures through a copied helper).
- [ ] T023 [US2] [SAFETY] Write `tests/Gallery.Web.Tests/CameraFreshnessTests.cs` (`LibraryHost`,
  images with cameras). For each action:
  - publish
  - unpublish
  - add to a public gallery
  - remove
  - reorder inside a gallery
  - set cover
  - retitle a photo
  - delete a photo
  - delete a gallery
  - make private the only listed holder of a photo that also sits in another private gallery

  assert three things:
  1. the stored `cameras` (via `InvariantChecker` in `SettleAsync`);
  2. `SettleAsync()`'s purged set equals the expected union of the existing gallery set and the
     camera part from edge.md, computed by hand in the test;
  3. the next anonymous GET of `/c` and the affected `/c/{slug}` shows the change (tile present
     or absent, count, title text).

  The last action must remove the photo from its camera (II.2, US2 scenario 3).
- [ ] T024 [US2] Write `tests/Gallery.Web.Tests/CameraTimingEvidenceTests.cs`:
  - one run that publishes, retitles and unpublishes camera photos, then GETs `/c`, `/c/{slug}`
    and (after Batch D) a camera photo page;
  - with output cache OFF, drive 100 GETs (200s) each of `/g`, one gallery page, one photo page,
    `/c`, `/c/{slug}` and (after Batch D) `/c/{slug}/{iid}`, so the committed `evidence/p95.sh`
    (T030, needs n >= 100 per route and its mirror) can read the log;
  - assert an `op.timing` line exists for each of `db.cameras.load`, `db.cameras.write`,
    `db.cameras.list` and `db.camera.read`, and an `http.request` line for each camera route
    template;
  - write the captured lines to `specs/013-by-camera/evidence/camera-timings.log` (the
    `NavTimingEvidenceTests` pattern).

**Checkpoint (parent)**: build `-warnaserror`, run the full suite (existing tests unmodified,
apart from the Phase 3 stated changes), commit.

## Phase 5: User Story 3, step through a camera's photos (P2)

**Goal**: a camera tile opens a photo view that steps within the camera, with the camera
breadcrumb and N / total.

**Independent test**: open `/c/{slug}`, open the first tile, and step Next to the last. Each page
is the next item of `camera.items`, the first has no Previous and the last has no Next, and
Escape lands on `/c/{slug}#p-{iid}`.

**Batch D**: one executor, after Batch C. It owns:
- `src/Gallery.Web/Site/Pages/PhotoPage.razor`
- `src/Gallery.Web/Site/PublicEndpoints.cs`
- `tests/Gallery.Web.Tests/CameraPhotoRoutesTests.cs` (new)
- `tests/Gallery.Web.Tests/CameraBrowserTests.cs` (new)

- [ ] T025 [US3] `src/Gallery.Web/Site/Pages/PhotoPage.razor`: add the optional `Crumbs` parameter (K3).
  The `Crumbs` private property becomes `CrumbsParam ?? (old expression)`. Rename the private
  property if needed; the public parameter is named `Crumbs`. With it unset, the output is
  byte-identical: `PageComponentTests` and `NavComponentTests` stay green unmodified.
- [ ] T026 [US3] [SAFETY] Add C3 `/c/{slug}/{iid}` to `src/Gallery.Web/Site/PublicEndpoints.cs`
  per contracts/routes.md:
  - `OpaqueId.IsValid` is checked before any read;
  - `CameraAsync`; `iid` must be in `items`;
  - neighbours come from `GalleryNav.Neighbours(items, iid)` (call it);
  - `ImagesAsync(≤3 ids, full: true)`, dropping any doc with `DeletingAt` set;
  - prev/next hrefs `/c/{slug}/{id}`, and `PrefetchUrls` built exactly as on the photo page
    (extract a private helper both routes call rather than copying);
  - `PhotoPage` gets `GalleryTitle` = camera name (used in `<title>`),
    `Crumbs = Trails.UnderCameras(new(name, $"/c/{slug}#p-{iid}"), new(title))`,
    `Index` and `Total` over `items`, and `NoIndex` false;
  - `ApplyPhotoHeaders` without noindex.
- [ ] T027 [US3] Write `tests/Gallery.Web.Tests/CameraPhotoRoutesTests.cs` (`LibraryHost`):
  - crumbs (label, href) are Home `/`, By Camera `/c`, {camera} `/c/{slug}#p-{iid}`, {title};
  - `PhotoCount` is `N / total`;
  - prev/next follow `items`, with none at the ends;
  - there is exactly one `.breadcrumb a[href*="#p-"]`;
  - the `MetadataBar` shows the same fields as the photo's gallery photo page (compare the
    element bytes);
  - [SAFETY] reads `== 2`;
  - [SAFETY] 404 parity (status, headers, body bytes) for: an invalid iid; a valid iid not in
    the camera; a valid iid of a private-only photo; an iid of another camera; an unknown slug;
  - [SAFETY] no `href="/g/` anywhere on the page.
- [ ] T028 [US3] Write `tests/Gallery.Web.Tests/CameraBrowserTests.cs` on `NavBrowserFixture`,
  collection `nav-browser`. The fixture's photos are all `TESTCAM T1`, so the slug is
  `testcam-t1`. Collect failures and assert once; park the pointer at (0,0) before each `GotoAsync`.
  - At 320 and 1440 px: `/c` → click the tile → `/c/testcam-t1` → click the first tile → photo.
    ArrowRight to the last photo, checking the URL each step; ArrowLeft once; Escape lands on
    `/c/testcam-t1#p-{iid}`.
  - JS disabled: the same walk by clicking Next links.
  - On `/c`, `/c/testcam-t1` and one camera photo page: no horizontal scroll, every `img` has
    `naturalWidth > 0`, there is no layout shift from images (a `PerformanceObserver`
    `layout-shift` sum of 0 after load), and the test-impact (B) browser scans (no
    `/admin-assets/` request; no key-hint text; no duplicate header link destination).
- [ ] T029 [US3] Add the camera photo route to the path arrays in `CameraRoutesTests` (T018 marker)
  and `CameraTimingEvidenceTests` (T024). Both files are new, so this is the Batch D executor's
  edit; run both classes.

**Parent stated change (after Batch D)**, test-impact (C):
- add `CameraPathAsync()` and `CameraPhotoPathAsync(int i)` helpers to
  `tests/Gallery.Web.Tests/Fixtures/NavBrowserFixture.cs` (additive);
- append the three rows to `tests/Gallery.Web.Tests/LayoutConsistencyBrowserTests.cs`. All pages
  are compared on every measure at every width, with no exception;
- run, then commit.

## Phase 6: Polish and cross-cutting (parent unless marked)

- [X] T030 [P] Write `specs/013-by-camera/evidence/p95.sh` and `p95-selftest.sh` from 011's:
  - routes `/g`, `/g/{id}/{slug}`, `/g/{id}/{slug}/{imageId}`, `/c`, `/c/{slug}`, `/c/{slug}/{iid}`;
  - read the `http.request` lines from `evidence/camera-timings.log`;
  - assert each camera route's p95 duration ≤ its mirror's × 1.10 plus 1 ms jitter, and its
    max `db_reads` `==` 1 / 2 / 2 (SC-004);
  - self-test on a plain log, an empty log (must fail), an over-limit value (fails its row only)
    and an under-sampled route;
  - tee the output to `p95-output.txt`.
- [X] T031 [P] VII.2 design-record update, scripted in
  `specs/013-by-camera/evidence/design-record-edit.py`, which asserts each old string's count
  before writing:
  - `draft/02c-decisions-content.md`: add D-26 "Camera Grouping" with Area, Status, Choice,
    Rationale citing II.2, II.5, IV.9 and VII.2, Alternatives (research R-01's three) and Pivot
    cost;
  - `draft/04-content-model.md`: add a `cameras` section (data-model.md table);
  - `draft/05-lifecycle.md`: L-07..L-17 "Pages and caches" lines gain "plus P-CAM";
  - `draft/07-traceability.md`: II.2 and IV.9 rows name the camera projection and the `/c` single
    read;
  - `draft/02d-decisions-delivery.md` D-21: the pages list gains `/c`, `/c/{slug}` and
    `/c/{slug}/{iid}`.

  Then run `assemble.sh` and `validate.sh`, teeing to `evidence/design-validate.txt` (0 failures),
  and `git show --stat` must list `docs/design.md`.
- [ ] T032 [P] Write `specs/013-by-camera/evidence/verify.sh`, a read-only live check on a
  `--no-launch-profile --urls http://127.0.0.1:5093` instance started from `artifacts/` against a
  seeded dev database, stopping only its own PID. Checks per quickstart.md §2 verify.sh. Run it on
  the T001 commit in a throwaway worktree first (every new-mechanism check must FAIL there), then on
  the feature tree; tee to `verify-output.txt`.
- [ ] T033 Write `specs/013-by-camera/evidence/non-vacuity.sh` from
  `templates/non-vacuity.sh`, with one break per [SAFETY] guard:
  - `Cameras()` ignores visibility (uses every gallery) → T007 red;
  - `Cameras()` ignores `DeletingAt` → T007 red;
  - a `"cameras"` write added to `ImageService.cs` → T008 red;
  - C2 returns the page for a private-only camera's slug lookup (drop the not-found branch) →
    T017 red;
  - an extra `ImagesAsync` call in C1 → T016 red;
  - `PurgeSets.Cameras` drops the before-slug → T022 red;
  - the scope skips `PurgeSets.Cameras` → T023 red;
  - C3 skips the `items.Contains` check → T027 red;
  - the `InvariantChecker` camera rule compared against an empty list → T023 red.

  Dry-run with `NV_DRYRUN=1`, then do the real run. Each break must fail on its assertion. Tee to
  `non-vacuity.txt`.
- [ ] T034 Fold every executor `decisions_made` into `specs/013-by-camera/decisions.md`
  (accepted or REJECTED rows), plus one STATED-CHANGE row per existing test file edited.
- [ ] T035 Update `docs/roadmap.md` with a 013 entry (status, evidence paths, OWNER-ONLY checks)
  and a coverage row for D-26. Then run `dotnet clean`, build `-warnaserror`, and run the full
  suite. `git checkout --` any evidence logs the suite regenerates, then commit.
- [ ] T036 Merge to main and push (parent). Watch the deploy job go green. Then the production
  pass from quickstart.md §3: run `verify-prod.sh` and a browser pass on port.zacal.me for `/c`,
  one camera and one camera photo page, saved to `evidence/browser-prod.txt`.

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 Look over `/c`, one camera page and stepping through its photos on the live site, at
  desktop and phone widths.

## Dependencies and execution order

- T001 and T002 (parent) → Batch A (T003 and T004 in parallel, then T005 → T006 → T007 and T008)
  → parent checkpoint and InvariantChecker rule.
- Batch B (T009, T010 and T011 in parallel; then T012 → T013 → T014 → T015-T018) → parent stated
  changes (A).
- Batch C (T019, then T020 → T021; T022 is parallel with T020/T021; then T023 and T024) →
  checkpoint.
- Batch D (T025 → T026 → T027 → T028 → T029) → parent stated change (C) for layout.
- Polish: T030, T031 and T032 in parallel; then T033 (needs every guard); then T034 → T035 → T036.
- The batches are sequential because B, C and D share `PublicEndpoints.cs`, the reader and the
  projection result, and each batch's tests read the previous batch's behaviour.

## Parallel example

```text
Batch A: T003 (CameraKey.cs + CameraKeyTests.cs) ‖ T004 (Documents.cs + Indexes.cs)
Batch B: T009 (Trails) ‖ T010 (ContentReader) ‖ T011 (ViewMapper)
Batch C: T019 (PurgeSets) → T020/T021 ‖ T022 (CameraPurgeSetTests)
Polish:  T030 (p95.sh) ‖ T031 (design record) ‖ T032 (verify.sh)
```

## Implementation strategy

- MVP = Phases 1-3: the camera list and grids, visible-only by construction (projection) and
  fresh for the next request on the origin. Edge freshness (US2) must land before deploy,
  because II.2's purge rule is a MUST. Do not ship the MVP to production without Phase 4.
- US3 adds the stepping photo view. It is the severable seam: if cut, T013's tiles would link to
  the photo's gallery page instead (a spec change, not an executor call).
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
