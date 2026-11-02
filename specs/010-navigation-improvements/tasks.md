# Tasks: Navigation Improvements

**Input**: Design documents in `specs/010-navigation-improvements/`: spec.md, plan.md, research.md,
data-model.md, contracts/components.md, contracts/edge.md, contracts/log-events.md, quickstart.md,
decisions.md.

**Source of truth**: the contracts and the research decisions (R-xx). Implement exactly what they
say. If something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: requested by the spec (every SC is a measured check; FR-012/FR-013 require catalogue
states and byte-identity guards). Each batch writes its tests in the files it names, in the same
batch as the code they guard.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081 in their own window.** Never kill or
  restart it. Never run `pkill` or `killall` in any form, and never bind :5080 or :5081. Build and
  test into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s`
  - Parallel executors use their OWN output, `--artifacts-path artifacts/exec-<batch>`, plus a
    `--filter` on their own test classes. When a sibling's half-written file breaks your build,
    wait and retry. Never edit a file you do not own.
  - macOS has no `timeout` command. Use `--blame-hang-timeout`.
- **Existing tests must pass UNMODIFIED.** That means every file in `tests/` at the T001 head
  (T001 records the list and the test count). A failure there means your change is wrong. Fix
  the code, never the test, a fixture or a snapshot. The ONLY stated requirement changes are
  T004 (fixture refactor), T013, T025, T035 and the Batch D gate note, and only the parent makes
  them. Executors never touch those files or any file under `specs/*/evidence/`.
- **Repo paths in tests**: use `Render.RepoRoot`. Never count `..` levels from
  `AppContext.BaseDirectory`.
- **Never commit**. The parent commits after its own gate. Never commit `artifacts/`.
- **No new NuGet package, no new script file, no new `<script>` element.** The only new browser
  code is the 5-line key handler in T029, verbatim from contracts/components.md K7. No `style=`
  attributes.
- **No real Cloudflare call in any test.** `LibraryHost`'s `RecordingEdgeProvider` replaces the
  edge.
- **Razor optional parameters**: with every new parameter unset, a component's bytes must not
  change (except the stated Pager `tap` class). Never put a new `@if` beside existing markup:
  Razor trims the adjacent whitespace. Use a null-by-default `RenderFragment` property rendered
  as `@Fragment` (contracts/components.md, rules at the top).
- **File ownership**: each batch lists the files it owns. These belong to the parent and no
  executor edits them: `src/Gallery.Web/AppBuilder.cs`, `src/Gallery.Web/Program.cs`,
  every file under `specs/`, every existing file under `tests/` (executors only create new test
  files), `tests/Gallery.Web.Tests/Fixtures/PhotoEncoder.cs` once T004 creates it.
- **Logging**: no new event or op name (contracts/log-events.md). `nav` writes happen inside the
  existing `db.site.project` timer. `http.request` comes from the existing middleware.
- **[SAFETY]** marks a task that implements the II.2 private = unlisted guarantee or the
  single-writer rule for `nav`. These tasks are blocking and non-deferrable: never stubbed,
  skipped, weakened, or left as follow-ups. Each [SAFETY] guard gets a deliberate break in T046
  and must go red on its ASSERTION.
- **Tight assertions**: purge assertions compare the recorder's set for EQUALITY with the
  expected set (none missing, none extra). Never write an assertion that accepts two outcomes.
  Every "renders nothing" case asserts the element is absent, not just empty.
- **Call, never copy**: `SiteProjector.Cards`, `GalleryNav.Neighbours`, `PurgeSets.*`,
  `CardBuilder.Build`, `GalleryService.*`, the `TextLink` atom and `Pager` are CALLED. A second
  copy of any of them is a defect.
- Escalate (stop and report) on any design question. Executors never change the contracts.
  After two failed attempts on one task, stop and hand it back to the parent.
- Docker must be running (`docker info`; if not, `open -a Docker` and poll).

## Owner UI review rules (acceptance criteria for every UI task)

- **UI-1 Controls stay text links**: Previous, Next, the gallery link and the neighbour links
  are `TextLink`s in the caption style; no outline, fill or button class (FR-004).
- **UI-2 One tap rule**: every 44×44 tap box comes from `.text-link.tap` (K1). No other rule sets
  `min-height: 44px` on a link (grep in T045).
- **UI-3 Nothing above the photo**: the photo page adds nothing before `.stage` (FR-001).
- **UI-4 No hints for keys**: no text, `title`, `aria-keyshortcuts` or tooltip mentions the keys
  (clarify: silent).
- **UI-5 Missing directions render nothing**: no disabled link, no placeholder text.

---

## Phase 1: Setup (parent, before any dispatch)

- [X] T001 Run the baseline gate on the current head (`dotnet build … -warnaserror`,
  `dotnet test …`). Write `specs/010-navigation-improvements/evidence/baseline.txt`: head hash,
  warning count, test count per assembly, `git ls-files tests` output, and the byte-identity
  guards that pin current output (`PublicOutputUnchangedTests`, `UiPagesUnchangedTests`,
  `OrganismStatePageTests`). Every later checkpoint runs `git diff --stat <head> -- tests/` and
  accepts changes only to NEW files and to the files named in T004, T013, T025, T035.
- [X] T002 Write `specs/010-navigation-improvements/evidence/p95.sh` per
  contracts/log-events.md (six rows, n ≥ 100, `db_reads` rule per route, p95 < 50 ms, plain and
  JSON input as in `specs/009-library-galleries/evidence/p95.sh`, BSD-awk safe: lists passed
  space-separated on one line). Create the six synthetic logs in `evidence/p95-selftest/`
  (pass-plain, pass-json, home-2-reads, photo-p95-60, gallery-99-lines, empty) and save
  `bash p95.sh p95-selftest/*.log` to `evidence/p95-selftest.txt`. Each bad log must fail
  exactly its own row; the two good logs must pass every row.
- [X] T003 Write `tests/Gallery.Web.Tests/NavTimingEvidenceTests.cs`
  (`[Trait("Category","Evidence")]`, `[Collection("s3")]`, modelled on
  `LibraryTimingEvidenceTests`): a `LibraryHost` (output cache off) holding 3 public galleries
  (23, 6, 1 photos, created and filled through `GalleryService`, then published) and 1 private
  (2 photos); 10 warm-up then 100 GETs each of `/`, the 23-photo gallery page and its 12th
  photo page; assert 300 `http.request` lines with status 200; write the host's log messages
  one per line to the path in env `NAV_TIMINGS_OUT`, defaulting to
  `specs/010-navigation-improvements/evidence/nav-timings.log`. Uses only APIs that exist today.
  Run it on the UNCHANGED code with `NAV_TIMINGS_OUT=…/evidence/baseline-timings.log`, then run
  `p95.sh` on it into `evidence/p95-baseline.txt`. All rows must PASS (else stop: the baseline
  itself breaks SC-006 and the owner decides). `git add -f` both files (`*.log` is ignored).
- [X] T004 Move `BrowserSiteFixture`'s private photo-encoding code (`EncodePhoto`, `Named`,
  `Run`, the `Encoded` record, colours and ladder constants) into a new
  `tests/Gallery.Web.Tests/Fixtures/PhotoEncoder.cs` (internal static, same behaviour) and make
  `BrowserSiteFixture` call it. Test infrastructure, not a test: `BrowserTests` must pass
  unmodified and byte-for-byte the same derivative file names must result (compare the fixture's
  image-host file list before and after in one run). Log it as a new decisions.md row.
- [X] T005 Design record (VII.2, research R-14): edit `specs/001-architecture-stack-design/draft/`
  — `02d-decisions-delivery.md` D-21 (Pager centre slot and `Tap`; new molecules `GalleryLink`,
  `GalleryNeighbours`, `MembershipStatus`; `SiteHeader` with no nav; `PublicLayout` key handler)
  and D-17 (the key handler shares the predictive module, registered before the Save-Data
  return); `04-content-model.md` Gallery (`nav`, single writer, listed only); `05-lifecycle.md`
  the "Pages and caches" lines of add, remove, reorder, publish, unpublish, rename, gallery
  delete, home reorder and image delete (P-NAV and the counter rule; "Amended by feature 010");
  `07-traceability.md` II.2 row (the two nav guards and their tests). Run
  `bash specs/001-architecture-stack-design/evidence/assemble.sh` then `validate.sh`; 0
  failures. Annotate spec.md Assumptions: "Neighbours are stored per gallery by the projector
  (plan research R-01)". Commit (docs) with T001–T004.

**Checkpoint**: baseline recorded, p95 script proven on synthetic logs, design record updated.

---

## Phase 2: Foundational, Batch A: neighbours and purging (one executor, 7 tasks)

**Owns**: `src/Gallery.Web/Data/Documents.cs`, `src/Gallery.Web/Library/SiteProjector.cs`,
`src/Gallery.Web/Library/OperationResult.cs`, `src/Gallery.Web/Library/ImageDeleteService.cs`
(only the `RefreshAsync` call site), `src/Gallery.Web/Edge/PurgeSets.cs`, and the new test files
`NavProjectionTests.cs`, `NavPurgeTests.cs`, `NavSourceGuardTests.cs`.

- [X] T006 Add `GalleryLinkDoc` (`id`, `slug`, `title`), `GalleryNavDoc` (`prev`, `next`, both
  `[BsonIgnoreIfNull]`) and `GalleryDoc.Nav` (`nav`, `[BsonIgnoreIfNull]`) to
  `src/Gallery.Web/Data/Documents.cs`, beside `GalleryCardDoc` (data-model.md). No other field
  changes.
- [X] T007 [SAFETY] In `src/Gallery.Web/Library/SiteProjector.cs` add
  `record GalleryLinkTarget(string Id, string Slug)`, `record ProjectionResult(IReadOnlyList<GalleryCardDoc> Cards, IReadOnlyList<GalleryLinkTarget> NavChanged)`,
  a pure `static Dictionary<string, GalleryNavDoc?> Nav(IEnumerable<string> homeOrder, IEnumerable<GalleryDoc> galleries)`
  and `ProjectWithNavAsync()`. `Nav` CALLS `Cards(...)` for the listed sequence and
  `GalleryNav.Neighbours` over the card gallery ids; it returns an entry for EVERY gallery in
  `homeOrder` that exists: a `GalleryNavDoc` for a listed gallery with at least one neighbour,
  null otherwise (data-model rules 2–3). `ProjectWithNavAsync` does the existing read, cards and
  `$set`, then for each gallery whose computed nav differs from its stored `Nav` (BSON equality,
  null vs absent equal) writes `$set nav` or `$unset nav` with NO `updatedAt` change, all inside
  the existing `db.site.project` timer, and returns the changed galleries with their slugs.
  `ProjectAsync()` becomes `(await ProjectWithNavAsync()).Cards` with its signature unchanged.
- [X] T008 [SAFETY] Write `tests/Gallery.Web.Tests/NavProjectionTests.cs` (AdminHost or
  LibraryHost + Mongo): (a) pure `Nav` over A, B, C public non-empty, P private non-empty, E
  public empty, in homeOrder A, P, B, E, C → A{next B}, B{prev A, next C}, C{prev B}, P null, E
  null, and no nav anywhere names P or E; (b) the only listed gallery gets null; (c) through
  `GalleryService`: reorder home, publish P, unpublish B, rename B, delete C, remove B's last
  photo — after each, every stored `nav` equals `Nav(...)` of the stored state, and
  `NavChanged` equals exactly the galleries whose stored nav changed; (d) a second
  `ProjectWithNavAsync` with no change returns empty `NavChanged` and issues no gallery update
  (count `op.timing db.site.project` duration only; assert via `updatedAt` and the stored doc
  being unchanged); (e) a nav-only write leaves `updatedAt` unchanged; (f) a private gallery
  with a planted stale `nav` gets it unset by the next projection.
- [X] T009 In `src/Gallery.Web/Edge/PurgeSets.cs`: widen `Changed` per contracts/edge.md E1 (rules
  3 and 4) and add `PurgeSet Nav(IEnumerable<GalleryLinkTarget> navChanged, EdgeBases bases)`
  (gallery paths only). Update the XML comments. Nothing else in the file changes.
- [X] T010 In `src/Gallery.Web/Library/OperationResult.cs` (`OperationScope`): call
  `ProjectWithNavAsync` in `CompleteAsync` and `RefreshAsync`; keep a scope-level set of
  `NavChanged` from every projection (RefreshAsync adds to it); add `PurgeSets.Nav(set)` to the
  union before eviction and the single enqueue. For warm, read the NavChanged galleries missing
  from `after` with one `db.gallery.load` read (only when non-empty) and include them in the
  `after` values passed to `WarmFiles`. `RefreshAsync`'s public return type stays
  `IReadOnlyList<GalleryCardDoc>`; `ImageDeleteService` needs no change unless it calls the
  projector directly (check; if it does, route it through the scope).
- [X] T011 Write `tests/Gallery.Web.Tests/NavPurgeTests.cs`: (a) the E1 table of
  contracts/edge.md, row by row, against `PurgeSets.Changed`; (b) every row of the "Expected sets
  per owner action" table, through `LibraryHost` with the recorder: set up A, B, C, D listed and
  P private (each with 2 photos), run the action, `SettleAsync()`, and assert the recorder's page
  set EQUALS the union of the pre-010 sets for that action (computed by calling the existing
  `PurgeSets` methods on the snapshots) plus exactly the P-NAV pages in the table; (c) the
  "none" rows add nothing.
- [X] T012 [SAFETY] Write `tests/Gallery.Web.Tests/NavSourceGuardTests.cs` (source scan like
  `OwnerWriteGuardTests`): in every `src/**/*.cs` outside `bin/obj`, a line containing the
  literal `"nav"` or a match of `\.Nav\b` inside `Update\.(Set|Unset)` or a `Nav =` assignment
  fails unless the file is `Library/SiteProjector.cs` or `Data/Documents.cs`; the public
  endpoint's READ of `gallery.Nav` is allowed (no assignment). Collect every violation, assert
  once, message lists `path:line`.

**Batch A gate (parent)**: build `-warnaserror`; full test; then T013.

- [X] T013 (parent, stated requirement change) Run the full suite after Batch A. Exactly these
  four facts are expected to fail, each because the 009 expected set lacks pages 010 now purges
  (worked through E1 and E2 during analyze):
  1. `PurgeSetTests.P_MEMBERS_add_purges_gallery_page_added_photo_its_new_neighbour_and_home`
     (a b c → a b c e): old `/, /g/g1/s, …/c, …/e`; new adds `…/a, …/b` (rule 4). Rename to
     `P_MEMBERS_add_purges_every_photo_page_when_the_count_changes`.
  2. `GalleryServiceTests` line 144 (35 photos added to a 40-photo gallery): old
     `fresh + first[39]` photo pages + gallery page; new = all 75 photo pages + gallery page
     (rule 4).
  3. `GalleryServiceTests` line 275 (remove c from a b c): old gallery page + `…/b`, `…/c`; new
     adds `…/a` (rule 4).
  4. `GalleryServiceTests` line 242 (home reorder swapping the two listed galleries a, b): old
     `[Home]`; new `[Home, Gp(a, "alpha"), Gp(b, "beta")]` (P-NAV: both neighbours swap).
  These were checked and do NOT change: `Changed_is_added_removed_and_neighbour_changed_ids`,
  `P_MEMBERS_remove_…`, `P_MEMBERS_reorder_…`, `Union_…`, `P_DELETE_…`, `LibraryFlowTests:86`,
  `EdgeModeTests:248`, every `ImageDeleteTests`/`ImageServiceTests`/`LibraryEditTests` purge set,
  and the publish/unpublish/delete facts of `GalleryServiceTests` (each has at most one listed
  gallery, which has no `nav`). For each of the four: confirm the new set from E1/E2, edit ONLY
  the expected set (and the one rename), and add a decisions.md row with old and new sets. Any
  other failure is a defect in Batch A: send it back.
  DONE (decisions row 27): the four above plus two the analyze pass missed, both per the P-NAV
  table: `GalleryListTests.G3_…` (listed swap adds both gallery pages) and
  `BuilderTests.Removing_photos_…` (unlisting/relisting Coast adds `/g/{later}/later`).

---

## Phase 3: Foundational, Batch B: components (one executor, parallel with Batch A, 8 tasks)

**Owns**: `src/Gallery.Web/Components/Atoms/TextLink.razor(.css)`,
`Components/Molecules/Pager.razor(.css)`, `Components/Molecules/PagerStack.cs` (new),
`Components/Molecules/GalleryLink.razor(.css)` (new), `Components/Molecules/GalleryNeighbours.razor(.css)`
(new), `Components/Molecules/GalleryNeighbour.cs` (new),
`Components/Molecules/MembershipStatus.razor(.css)` (new),
`Components/Organisms/SoloViewer.razor`, `Components/Organisms/SiteHeader.razor`, and the new
test file `NavComponentTests.cs`. Implement contracts/components.md K1–K6 and K8 verbatim.

- [X] T014 [P] K1: `Tap` parameter and the `.text-link.tap` rule in
  `src/Gallery.Web/Components/Atoms/TextLink.razor` and `TextLink.razor.css`.
- [X] T015 K2: `PagerStack` enum (`None`, `Narrow`, `Always`) in
  `src/Gallery.Web/Components/Molecules/PagerStack.cs`; `Here` and `Stack` parameters, `Tap="true"`
  on both links, and the grid CSS in `Components/Molecules/Pager.razor(.css)`.
- [X] T016 [P] K3: `src/Gallery.Web/Components/Molecules/GalleryLink.razor(.css)` with
  `NarrowChars`, `WideChars`, `Length`, `StackFor`.
- [X] T017 K4: `Here` and `Stack` pass-through in
  `src/Gallery.Web/Components/Organisms/SoloViewer.razor`.
- [X] T018 [P] K5: `src/Gallery.Web/Components/Molecules/GalleryNeighbour.cs` and
  `GalleryNeighbours.razor(.css)`; renders nothing when both are null.
- [X] T019 [P] K8: `src/Gallery.Web/Components/Molecules/MembershipStatus.razor(.css)`.
- [X] T020 [P] K6: `src/Gallery.Web/Components/Organisms/SiteHeader.razor` renders no `<nav>`
  when `NavItems` is empty; `NavItems` defaults to `[]`, not `EditorRequired`; the non-empty
  output is byte-identical to today (check with `UiPagesUnchangedTests` filter
  `organisms/site-header`, which must stay green).
- [X] T021 Write `tests/Gallery.Web.Tests/NavComponentTests.cs` (`Render.Html`, decode before
  asserting copy): each K-item's exact markup and copy from contracts/components.md; TextLink
  without `Tap` renders `class="text-link"` exactly; Pager with neither `Here` nor `Stack`
  differs from a pre-010 render ONLY by ` tap` (assert by stripping ` tap` and comparing to the
  006 ui-baseline `molecules/pager.html` specimens); first/last/one-photo Pager states render no
  link for the missing side (absent, UI-5); `StackFor` thresholds at 26/27 and 60/61 characters;
  GalleryNeighbours with both null renders an empty string, with one side renders an empty
  `<span>` for the other; MembershipStatus all four table rows plus a 0-public, 2-others case,
  and its output starts with neither `<span` nor any wrapper element (contracts P3/K8);
  GalleryLink's anchor has `class="text-link tap"` (M5); SoloViewer passes `Here` and `Stack`
  through (its output contains the `here` div and the stack class when set, and with both unset
  differs from the 006 `organisms/solo-viewer.html` specimens only by ` tap`); PhotoPage
  rendered WITHOUT `GalleryHref` renders no `gallery-link` and does not throw (M3);
  SiteHeader with `[]` has no `<nav`; no `style=` anywhere (`Guards.AssertNoInlineStyle`);
  `TextLink.razor.css` is the only `.razor.css` under `src/` that sets `min-height`,
  `line-height: 44px` or `display` on `.text-link` (source scan, M1/UI-2).

**Batch B gate (parent)**: build; NavComponentTests green; every byte-identity failure in the
full run must be on the expected list in T025 and nowhere else.

---

## Phase 4: Catalogue states, Batch C (one executor, after Batch B, 3 tasks) — FR-012

**Owns**: `src/Gallery.Web/Ui/UiCatalog.cs`, new `src/Gallery.Web/Ui/Pages/GalleryLinkPage.razor`,
`GalleryNeighboursPage.razor`, `MembershipStatusPage.razor`, the three existing pages named in
T023 (append only), and the new test file `NavCataloguePageTests.cs`.

- [X] T022 [P] Create the three catalogue pages with exactly the specimen labels of
  contracts/components.md "Catalogue" (sample titles of 10, 30 and 70 characters; sample
  galleries "Coast", "alpine", "Birds"; hrefs `#`), and append their entries at the end of the
  Molecules block in `src/Gallery.Web/Ui/UiCatalog.cs`.
- [X] T023 Append the specimens listed for `PagerPage`, `SiteHeaderPage` and `TextLinkPage` in
  `src/Gallery.Web/Ui/Pages/` AFTER their existing specimens. Do not change an existing
  specimen. Do NOT touch `SoloViewerPage.razor`: `OrganismPageTests` counts its two thumb layers
  and must pass unmodified (SoloViewer's pass-through of `Here`/`Stack` is covered by T021).
- [X] T024 Write `tests/Gallery.Web.Tests/NavCataloguePageTests.cs`: each new page renders every
  label in order; `Guards.AssertNoInlineStyle` and `Guards.AssertNoScript` on each; the three
  appended pages contain their new labels after the old ones.

- [X] T025 (parent, stated requirement change) Snapshot regeneration, verify-before-replace (plan
  "Test changes" 1–2): copy `specs/005-admin-ui-components/evidence/baseline/` and
  `specs/006-public-site-local/evidence/ui-baseline/` aside; delete exactly the failing snapshots
  (expected: 005 `SoloViewerPage.html`, `PublicLayoutPage.html` only after Batch D, and 006
  `molecules/pager.html`, `molecules/text-link.html`, `organisms/solo-viewer.html`,
  `organisms/site-header.html`, `templates/public-layout.html` after Batch D, `index.html`, plus
  the three new slugs which have none); re-run to rewrite; diff old vs new with a committed
  script `evidence/snapshot-diff.sh` that strips ` tap`, the Pager `here`/stack markup, appended
  `<section class="specimen">` blocks, the removed `<nav class="nav" aria-label="Site">…</nav>`
  and new index entries, then collapses whitespace. Equal = keep; any residue = stop. Save the
  script output to `evidence/snapshot-diff.txt`. In `IndexTests` change 31 → 34 (the only
  assertion edit). Run once after Batch C and again after Batch D for the layout snapshots.

---

## Phase 5: User Stories 1–4 (public pages), Batch D (one executor, after Batches A and C, 7 tasks) 🎯 MVP

**Owns**: `src/Gallery.Web/Site/PublicEndpoints.cs`, `Site/Pages/PhotoPage.razor`,
`Site/Pages/GalleryPage.razor(.css)`, `Components/Templates/PublicLayout.razor`, and the new test
file `NavRoutesTests.cs`.

### User Story 1 — Know where I am and get back from a photo (P1)

Goal: every photo page names its gallery and position and links back to the photo's tile.
Independent test: open a photo directly; the row reads `← {Gallery} · N / total`; the link is
`/g/{id}/{slug}#p-{imageId}`.

- [X] T026 [US1] Photo route in `src/Gallery.Web/Site/PublicEndpoints.cs`: compute
  `Index = items.IndexOf(imageId) + 1`, `Total = items.Count`,
  `GalleryHref = $"/g/{id}/{slug}#p-{imageId}"`; pass them to `PhotoPage`. No new read; the
  gallery projection for the photo page is unchanged. In `src/Gallery.Web/Site/Pages/PhotoPage.razor`
  add the parameters as OPTIONAL (`GalleryHref` null by default; null means no `Here` and
  `Stack.None`, never a throw) and pass `Here` (a `GalleryLink`) and
  `Stack = GalleryLink.StackFor(...)` to `SoloViewer` (contracts P1). `PageComponentTests` render
  PhotoPage without these parameters and must pass unmodified.

### User Story 2 — Step through photos comfortably (P1)

Goal: 44×44 Previous/Next, Left/Right/Escape. Independent test: measure at 390 px; press keys.
(Tap areas come from Batch B; keys here; measured in Batch F.)

- [X] T027 [US2] Add the K7 key handler verbatim to the `Enhance` module in
  `src/Gallery.Web/Components/Templates/PublicLayout.razor` as its first statements, before
  `if (navigator.connection?.saveData) return;`. The module stays one `<script type="module">`
  under 30 lines (`PageComponentTests` must pass unmodified).

### User Story 3 — Keep going at the end of a gallery (P2)

Goal: listed galleries end with neighbour links in home order; private galleries end with
nothing. Independent test: bottom of a listed gallery links the next one by title.

- [X] T028 [SAFETY] [US3] Gallery route in `src/Gallery.Web/Site/PublicEndpoints.cs`: when
  `gallery.Visibility == "public"` and `gallery.Nav` is not null, map `Nav.Prev`/`Nav.Next` to
  `GalleryNeighbour($"/g/{n.Id}/{n.Slug}", n.Title)`; otherwise pass null for both, whatever the
  document holds. In `src/Gallery.Web/Site/Pages/GalleryPage.razor` add `Prev`/`Next` and render
  `<GalleryNeighbours Prev Next />` after the `ContactSheet` as a null-by-default fragment.
  The read stays `ContentReader.GalleryAsync(id, forPhoto: false)`, unchanged.

### User Story 4 — A header that doesn't repeat itself (P3)

- [X] T029 [US4] In `src/Gallery.Web/Components/Templates/PublicLayout.razor` render
  `<SiteHeader SiteName="Gallery" />` and delete the `_nav` field.

### Tests for US1–US4

- [X] T030 [SAFETY] [US3] Write `tests/Gallery.Web.Tests/NavRoutesTests.cs` (LibraryHost +
  Mongo + recorder; galleries built through `GalleryService`, never with a hand-written `nav`
  EXCEPT case (e)): (a) photo page of photo 4 of 23: the decoded HTML contains
  `← {title}` linked to `/g/{id}/{slug}#p-{imageId}` and ` · 4 / 23`; first and last photos show
  1 / 23 and 23 / 23 and no link for the missing side; a one-photo gallery shows 1 / 1 and
  neither Previous nor Next; (b) the photo and gallery routes each log `db_reads` ≤ 2 and home
  exactly 1 (`http.request` lines); (c) gallery pages of A, B, C (home order A, B, C) show
  `← A`/`C →` on B, only `B →` on A, only `← B` on C, with hrefs `/g/{id}/{slug}`; (d) a private
  gallery's page has no `gallery-neighbours` element and no `/g/` link outside its tiles, and no
  listed gallery's page links to it; (e) [SAFETY] write a stale `nav` naming A and C directly
  onto the private gallery's document, GET its page, assert no `gallery-neighbours` element;
  (f) header on `/`, a gallery, a photo and a 404 page: the set of header `href`s has no
  duplicates and is exactly `{"/"}`; (g) `Server-Timing` still present on all three routes;
  (h) SC-005 end to end (analyze M4): with A, B, C, D listed and P private, perform each owner
  action of contracts/edge.md "Expected sets" in sequence through `GalleryService` (reorder home,
  unpublish B, publish P, rename B, delete C, remove B's last photo); after each, `SettleAsync()`
  and then GET every listed gallery's page plus P's: each page's neighbour links (titles and
  hrefs, in order) equal the neighbours of the CURRENT listed home order, and a gallery that is
  not listed shows none. Zero stale links across the run.
- [X] T031 [US1] After rename of the gallery (through `GalleryService`), GET the photo page under
  the OLD slug: 301 to the new slug (existing behaviour) — and the new page's gallery link uses
  the new slug. Add to `NavRoutesTests.cs`.

**Batch D gate (parent)**: full test; T025 second run for the layout snapshots; then any
public-route test that asserts the header's "Galleries" link is a stated change: flip it to
assert-absent, rename the test to say so, and log it in decisions.md (expected: none outside the
snapshots; `TemplatePageTests.SiteHeader_page_renders_brand_and_nav` uses the catalogue page and
stays unmodified).

---

## Phase 6: User Story 5 — From a library photo to its galleries (P3), Batch E (one executor, parallel with Batch D, 3 tasks)

Goal: the library status links to the first gallery by title with "+N". Independent test: photos
in 0, 1 and 3 galleries.

**Owns**: `src/Gallery.Web/Library/LibraryReader.cs`, `src/Gallery.Web/Admin/Pages/LibraryPage.razor`,
and the new test file `LibraryMembershipTests.cs`.

- [X] T032 [US5] In `src/Gallery.Web/Library/LibraryReader.cs` add `GalleryRef? FirstByTitle` as
  the LAST positional parameter of `LibraryTile`: the holder with the least `Title` under
  `StringComparer.OrdinalIgnoreCase`, ties by `Id` ordinal, any visibility. No new read.
  `Status`/`StatusLine` stay.
- [X] T033 [US5] In `src/Gallery.Web/Admin/Pages/LibraryPage.razor` `RowFor`, replace the
  CONTENTS of `<span class="lib-status">` (`@tile.Status`) with the K8 `MembershipStatus` call,
  keeping the span itself in the page, exactly as contracts P3 shows. Nothing else in the row
  changes.
- [X] T034 [US5] Write `tests/Gallery.Web.Tests/LibraryMembershipTests.cs` (LibraryHost): photos
  in 0, 1 and 3 galleries (titles "coast", "Alpine", "birds"; "coast" first in home order, one
  public): 0 → plain `Not in a gallery` with no `<a` inside `lib-status`; 1 → link to
  `/admin/galleries/{id}` with the title; 3 → link to "Alpine" (case-insensitive first, though
  not first in home order) followed by ` +2` and ` · public in 1`; a private-only first gallery
  is still chosen. SC-009: every tile whose photo is in ≥ 1 gallery has exactly one
  `/admin/galleries/` link inside `lib-status`.

- [X] T035 (parent, stated requirement change) `LibraryPageTests` lines 172–173 and
  `LibraryFlowTests` line 100 read the plain status string for photos in galleries. Change them
  to read the decoded text content of `lib-status` (tags stripped) and update the expected
  strings to the new copy (`Alpha +1`, `Alpha +1 · public in 1`, and for the flow
  `Coast · public in 1`). The zero-gallery assertions stay as they are. Log in decisions.md.

---

## Phase 7: Browser verification, Batch F (one executor, after Batches D and E, 4 tasks)

**Owns**: new `tests/Gallery.Web.Tests/Fixtures/NavBrowserFixture.cs`, new
`tests/Gallery.Web.Tests/NavBrowserTests.cs`. `BrowserTests`/`BrowserSiteFixture` are not touched.

- [X] T036 Write `NavBrowserFixture` (research R-13): Mongo container, image host with hit
  counting and immutable caching, the real app via `AppBuilder.Build` (Development, the app's
  content root, output cache off, as `BrowserSiteFixture` does). Photos are encoded by
  `PhotoEncoder` (T004), 6 distinct sets reused cyclically for 23 image documents. Galleries:
  "September 6" (23 photos), "Coast" (6), "Solo" (1), all public, and "Private" (2). Cards and
  `nav` come from calling `CardBuilder.Build` per gallery and then the app's `SiteProjector`
  from its service provider; no hand-written card or nav. Expose paths and ids like
  `BrowserSiteFixture`, plus `RenameAsync` and `RemovePhotoAsync` that go through
  `GalleryService`.
- [X] T037 [US1] [US2] In `NavBrowserTests.cs`: SC-001 for EVERY photo of the 23-photo gallery
  at 1440×900 and 390×844: open the photo page, click the gallery link, wait for load, assert
  `#p-{id}`'s bounding box lies inside the viewport; repeat once per width with JS off; repeat
  for 3 photos pressing Escape instead of clicking; repeat for 1 photo after hovering the link
  for 1 s first (prerender risk R-01-a: on failure STOP and report, the fallback selector is a
  parent decision). Old slug after `RenameAsync`: following a stale gallery link still lands on
  the tile. After `RemovePhotoAsync`, the removed photo's anchor URL opens at scrollY 0 with
  status 200.
- [X] T038 [US2] In `NavBrowserTests.cs`: SC-003 tap boxes ≥ 44×44 for Previous, Next and the
  gallery link (M5), and the gallery neighbour links, at 360, 390, 600, 1024, 1440 px, no two boxes overlapping, centre
  text on one line and horizontally centred within 2 px for middle, first and last photos, with
  gallery titles of 10, 30 and 70 characters (set via `RenameAsync`); the photo's `.stage`
  height equals its pre-010 value (`0.8 * innerHeight`). Keys: Right/Left/Escape navigate;
  Alt+Left, Ctrl+Right, Meta+Right and Shift+Right are not intercepted (URL unchanged, or
  browser Back for Alt+Left); a key pressed while focus is in an injected `<input>` does nothing;
  Right on the last photo and Left on the first do nothing. FR-006: after Right, the next
  photo's main AVIF has hit count 1 (served from the prefetch, as `BrowserTests` checks with the
  same hit counter); with Save-Data emulated (same init script as
  `BrowserTests.Save_data_suppresses_neighbour_and_hover_fetches`) the keys still work. UI-4:
  no element's text, `title` or `aria-keyshortcuts` mentions an arrow key or Escape.
- [X] T039 [US3] [US4] In `NavBrowserTests.cs`: SC-004 with JS off: home → "September 6" →
  photo 1 → Next → gallery link (lands on photo 2's tile) → next gallery link → "Coast". SC-007:
  CLS 0 and no horizontal scroll on home, gallery and photo at 1440, 1024, 390 (same observer as
  `BrowserTests`). SC-008: header link hrefs on each page have no duplicate.

**Batch F gate (parent)**: run NavBrowserTests 3 times in a row; all green each time.

---

## Phase 8: Polish, guards, evidence (parent)

- [X] T040 Run `NavTimingEvidenceTests` on the finished code (default output path), then
  `p95.sh evidence/nav-timings.log > evidence/p95-output.txt`. All rows PASS and the `db_reads`
  distribution equals the baseline's. A miss is recorded, never tuned (decisions row with the
  measured values). `git add -f` the log.
- [X] T041 Grep-verify every [SAFETY] mechanism on the merged tree: the listed filter in
  `SiteProjector.Nav` (calls `Cards`), the `Visibility == "public"` check before `Nav` in the
  gallery route, `NavSourceGuardTests` present and green.
- [X] T042 Diff read of every executor change: no leftover scaffolding, no duplicated logic
  (`Cards`, `Neighbours`, tap rule), no `style=`, no new `<script`, no hint copy for keys (UI-4),
  no `min-height: 44px` outside `TextLink.razor.css` (UI-2).
- [X] T043 Write `specs/010-navigation-improvements/evidence/verify.sh` (contract-invariant
  greps, both directions: the tap rule exists once; the key handler precedes the Save-Data line
  in `PublicLayout.razor`; `PublicLayout.razor` has no `NavItem`; `SiteHeader` still renders
  `<Nav` for non-empty items; catalogue has 34 entries including the three new slugs;
  `ProjectAsync` still exists; `PurgeSets.Nav` is called from `OperationResult.cs`). Run it,
  `tee` to `evidence/verify-output.txt`.
- [X] T044 Live walkthrough (Workflow rule): run the built dll on :5094 against the owner's dev
  Mongo and R2 read-only (`ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/bin/Gallery.Web/debug/Gallery.Web.dll --contentRoot "$PWD/src/Gallery.Web" --urls https://localhost:5094`);
  curl `/`, one gallery, one photo: 200s, header has one link, photo row shows the gallery link
  and counter. Stop only that pid. Record in `evidence/live-walkthrough.md`.
- [X] T045 Full gate: `dotnet clean`, build `-warnaserror`, full test, `git diff --stat <T001 head> -- tests/`
  shows only new files and the stated-change files (T004, T013, T025, T035, Batch D gate).
- [X] T046 Write `specs/010-navigation-improvements/evidence/non-vacuity.sh` from
  `~/.hermes/skills/software-development/speckit-orchestration/templates/non-vacuity.sh`, one
  `brk` per guard, each dry-run against a temp copy first; break must hit the test's data path:
  (1) [SAFETY] `SiteProjector.Nav` uses all `homeOrder` galleries instead of `Cards` →
  `NavProjectionTests` red; (2) [SAFETY] the gallery route drops the public check →
  `NavRoutesTests` case (e) red; (3) [SAFETY] add `Builders<GalleryDoc>.Update.Unset(g => g.Nav)`
  to `GalleryService.cs` → `NavSourceGuardTests` red; (4) `CompleteAsync` omits `PurgeSets.Nav` →
  `NavPurgeTests` red; (5) `Changed` drops rule 4 → `NavPurgeTests` red; (6) key handler moved
  after the Save-Data return → `NavBrowserTests` Save-Data key case red. Each red run must fail on
  its assertion, not an exception. `tee` to `evidence/non-vacuity.txt`; tree restored and
  touched.
- [X] T047 Fold every executor `decisions_made` into decisions.md (accepted/REJECTED rows), mark
  tasks `[X]`, update `docs/roadmap.md` status for 010, commit docs then code with the
  verification line (build, test count, evidence files).

---

## Phase 9: Owner review 1 (contracts/owner-review-1.md; decisions rows 41-46)

- [X] T048 Parent: B1 `Crumb`, `Breadcrumb`; B3 `PhotoCount`; B6 `PageHeading`; delete
  `GalleryLink`/`PagerStack`; B4 Pager CSS; SoloViewer/Pager lose `Stack`; OwnerBar and
  AdminLayout lose `PrimaryAction`, both layouts gain `Crumbs`; catalogue pages and entries (36).
- [X] T049 Public wiring (B2, B3, B5): PublicLayout crumbs + K7 Escape selector; PhotoPage and
  GalleryPage crumbs; endpoints pass them.
- [X] T050 Admin wiring (B6): Galleries and Library `PageHeading`; builder crumbs, no "Back to
  galleries"; no page passes `ViewHref`; `library.js` New gallery opener via `.page-heading`.
- [X] T051 Tests: 010 test files updated to the amendment; new guards: breadcrumb markup and
  roots-without-crumbs (component + route), owner bar byte-identical across Library, Galleries,
  builder and Upload except `aria-current`, Pager whole words in the browser at 320-1440 px on the
  photo page and the admin library pager, Escape via the crumb, SC-001 via the crumb.
- [X] T052 Parent: stated changes to pre-010 tests (owner bar specimens, builder "Back to
  galleries", primary-button location), snapshot regeneration with snapshot-diff, design record
  D-23/D-21/D-17 edits + assemble/validate, verify.sh, non-vacuity (break 6 selector), full gate,
  commit.

## OWNER-ONLY: post-build live checks (not part of the automated run)

- [ ] O-01 Quickstart Part C on the owner's own `dotnet watch` (Ctrl+R first; step 2 fills `nav`
  for existing galleries).
- [ ] O-03 Prerender landing (R-01-a, decisions row 38): in your own Chrome (not automated;
  Chrome disables prerender under DevTools automation), open a photo of a listed gallery, hover
  the `← {Gallery}` link for a second, click it. Expected: the gallery opens scrolled to that
  photo's tile. DevTools > Application > Speculative loads shows whether the page was
  prerendered. If it opens at the top, the fallback selector in research R-03 is the fix.
- [ ] O-02 Phone check in Safari/iOS of tap areas and the long-title stacking (backburnered with
  008 O-02 if the owner prefers).

---

## Requirement coverage

| Req | Tasks |
|---|---|
| FR-001 | T016, T017, T021, T026, T030a |
| FR-002 | T026, T037 |
| FR-003 | T016, T026, T030a, T009 (stale counters) |
| FR-004 | T014, T015, T021, T038 |
| FR-005 | T027, T038 |
| FR-006 | T027, T038 |
| FR-007 | T007, T018, T028, T030c |
| FR-008 | T007, T008, T028, T030d–e |
| FR-009 | T009, T010, T011, T013, T030h |
| FR-010 | T020, T029, T030f, T039 |
| FR-011 | T019, T032, T033, T034 |
| FR-012 | T022, T023, T024 |
| FR-013 | T025, T035 |
| FR-014 | T037 (JS off), T039 |
| FR-015 | T003, T030b, T040 |
| FR-016 | T030g, T040 |
| SC-001 | T037 |
| SC-002 | T030a |
| SC-003 | T038 |
| SC-004 | T039 |
| SC-005 | T011, T030h |
| SC-006 | T002, T003, T040 |
| SC-007 | T039 |
| SC-008 | T030f, T039 |
| SC-009 | T034 |

## Dependencies & Execution Order

- Phase 1 (T001–T005) before any dispatch. T003 MUST run before any `src/` change.
- Batch A (T006–T012) and Batch B (T014–T021) run in parallel (disjoint files). T013 after A.
- Batch C (T022–T024) after B; T025 first run after C.
- Batch D (T026–T031) after A and C (it renders the components and reads `Nav`). Batch E
  (T032–T034) after B, in parallel with D. T025 second run and T035 after D and E.
- Batch F (T036–T039) after D and E.
- Phase 8 after F.

Story order inside Batch D: US1 (T026) → US2 (T027) → US3 (T028) → US4 (T029) → tests (T030,
T031). US5 (Batch E) depends only on Batch B.

## Parallel opportunities

- Batch A ∥ Batch B (own `artifacts/exec-a`, `artifacts/exec-b`).
- Batch D ∥ Batch E.
- Inside Batch B: T014, T016, T018, T019, T020 touch separate files.
- Parent work while executors run: T005 design record, T043 `verify.sh`, the `snapshot-diff.sh`
  script of T025, the `non-vacuity.sh` skeleton of T046.

## Implementation Strategy

MVP = US1 + US2 (the photo-page dead end the owner reported): Phase 1, Batches A–D. Batch A is
needed for the MVP only because Batch D also wires US3; if the photo page must ship alone, T026,
T027 and the photo cases of T030 depend only on Batch B. Then US3–US5 and the browser and
evidence work complete the feature. Run `/speckit-implement` in a fresh session seeded from these
artifacts.
