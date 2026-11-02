# Tasks: Home Page

**Input**: Design documents in `specs/011-home-page/`: spec.md, plan.md, research.md,
data-model.md, contracts/routes.md, contracts/components.md, contracts/edge.md,
contracts/test-impact.md, quickstart.md.

**Source of truth**: the contracts and the research decisions (R-xx). Implement exactly what they
say. If something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: requested by the spec (SC-002..SC-007 are measured checks; FR-016 requires the measured
layout test). Each batch writes its tests in the files it names, in the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081 in their own window.** Never kill or
  restart it. Never run `pkill` or `killall` in any form, and never bind :5080 or :5081. Build and
  test into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s`
  - Parallel executors use their OWN output (`--artifacts-path artifacts/exec-<batch>`) and a
    `--filter` on their own test classes. When a sibling's half-written file breaks your build,
    wait and retry. Never edit a file you do not own.
  - macOS has no `timeout` command. Use `--blame-hang-timeout`.
- **Existing tests must pass UNMODIFIED**, except the stated changes in
  contracts/test-impact.md. Only the parent makes those changes (T002 and the "Parent stated
  change" steps after Batches B, C and D). A failure
  anywhere else means your change is wrong. Fix the code, never the test, a fixture or a snapshot.
  Executors never touch existing files under `tests/` or any file under `specs/`.
- **Repo paths in tests**: use `Render.RepoRoot`. Never count `..` levels.
- **Never commit**. The parent commits after its own gate. Never commit `artifacts/`.
- **No new NuGet package, no new script file, no new `<script>` element.** The only script edit is
  the speculation-rule string in T009, verbatim from contracts/components.md C4. No `style=`
  attributes.
- **Razor optional parameters** (`Nav.Home`, `PublicLayout.Fill`): with the new parameter unset,
  the component's bytes must not change. Never put a new `@if` beside existing markup. Compute
  the class string in `@code` (contracts C2, C4).
- **File ownership**: each batch lists the files it owns. These belong to the parent:
  - `src/Gallery.Web/AppBuilder.cs`, `src/Gallery.Web/Program.cs`
  - every file under `specs/`
  - every existing file under `tests/` (executors only create new test files)
- **Logging**: no new event or op name. `/` must emit no `db.*` op.timing. `/g` emits exactly the
  events the old `/` emitted.
- **[SAFETY]** marks a task that implements the II.2 private = unlisted guarantee (private
  galleries never listed; a private page's trail links no other gallery) or the IV.9 read budget.
  These tasks are blocking and non-deferrable. Each [SAFETY] guard gets a deliberate break in T025
  and must go red on its ASSERTION.
- **Tight assertions**: purge assertions compare sets for EQUALITY, and read counts use `==`.
  "No X" assertions check the ELEMENT is absent (`class="nav nav--home"`, `<a href="/g"`), never a
  bare substring that inline scripts could contain.
- **Call, never copy**: the listing handler is MOVED, not duplicated. `Trails.*`, `PurgeSets.*`,
  `ContactSheet`, `Nav` and `Breadcrumb` are CALLED. A second copy of any of them is a defect.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back to the parent.
- Docker must be running (`docker info`; if not, `open -a Docker` and poll).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [ ] T001 Record the baseline in `specs/011-home-page/evidence/baseline.txt`:
  - `git rev-parse HEAD`
  - the list of `tests/**/*.cs` files
  - the full-suite pass count from `dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s`
  - one capture of `/` timing lines from the existing `NavTimingEvidenceTests` log, as the no-regression read baseline for `/g` (SC-005)
  - SC-004 pre-change capture: on the T001 head, GET `/` from a `LibraryHost` seeded with three
    listed galleries, one private and one empty (fixed ids and titles, written as a reusable
    helper `HomeCaptureLibrary` in a new `tests/Gallery.Web.Tests/Fixtures/HomeCaptureLibrary.cs`).
    Save the ContactSheet region (`<div class="cs ` … its closing `</div>`) with scoped ids
    stripped to `specs/011-home-page/evidence/pre-011-listing.html`. Commit it before Batch A.
- [ ] T002 Test-impact verification pass (parent, by hand) over the WHOLE `tests/` tree.
  - Grep `"/"`, `HomePath`, `"Galleries"`, `HomePage`, `Page("/")`, and the shared helpers
    `GalleryListTests.Home`, `HomeRawAsync`, `SetEq(`, `AllPurged`.
  - Read each hit and confirm or correct its class (A/B/C/D) in contracts/test-impact.md.
  - Must cover the sites not yet re-read at plan time: BrowserTests 38/152/191; NavBrowserTests
    509/537/543/581; TemplatePageTests 36; AdminAccessTests 228/288/325/415; SiteProjectorTests 128;
    InvariantTests 53; PublicRoutesTests 208/339/525; LibraryEditTests 154–169; PublishRefusalTests 58.
  - Update test-impact.md with the final list before Batch B is dispatched.
- [ ] T003 Freeze the pre-011 snapshots into `specs/011-home-page/evidence/pre-011-specimens/`
  (copied from `git show HEAD:<path>`):
  - `006/tokens.html`
  - `006/molecules/nav.html`
  - `006/molecules/breadcrumb.html`
  - `006/organisms/site-header.html`
  - `006/templates/public-layout.html`
  - `005/PublicLayoutPage.html`

## Phase 2: Foundational (blocks every story)

**Batch A**: one executor. It owns `src/Gallery.Core/Design/Tokens.cs`, `src/Gallery.Web/Site/Trails.cs`,
`tests/Gallery.Core.Tests/TypeDisplayTests.cs` and `tests/Gallery.Web.Tests/TrailsTests.cs`.

- [ ] T004 [P] Add `TypeDisplay = 48` to `src/Gallery.Core/Design/Tokens.cs`, exactly per contracts/components.md C1
  (const, `ToCssRoot` placement, `All()` row). Test in `tests/Gallery.Core.Tests/TypeDisplayTests.cs`:
  - `--type-display:48px` appears in `ToCssRoot()`, immediately after `--type-brand:16px;`
  - `All()` has the row
  - reflection over `public const int Type*` confirms TypeDisplay is the strict maximum
- [ ] T005 [P] Create `src/Gallery.Web/Site/Trails.cs` verbatim from contracts/components.md C3. Do not
  wire any call site yet. Test in `tests/Gallery.Web.Tests/TrailsTests.cs`:
  - each static trail's (label, href) list equals the data-model.md table exactly
  - [SAFETY] `Under(new("T"))` and `Under(new("G", "/g/x/y#p-1"), new("P"))` yield hrefs that are a
    subset of `{"/", "/g", the given tail hrefs}`

**Checkpoint (parent)**: build `-warnaserror`, run the two new test classes and the full suite (still
unmodified), commit.

## Phase 3: User Story 1, home page and listing at `/g` (P1) 🎯 MVP

**Goal**: `/` shows only the Galleries link; `/g` is the old grid, unchanged.

**Independent test**: GET `/` has one `<a href="/g">Galleries</a>` inside `nav.nav--home` and no
ContactSheet. GET `/g` lists the same galleries in the same order as the pre-change `/`, with 1 read.

**Batch B**: one executor, after Batch A. It owns:
- `src/Gallery.Web/Site/PublicEndpoints.cs`
- `src/Gallery.Web/Site/Pages/HomePage.razor`
- `src/Gallery.Web/Site/Pages/ListingPage.razor`
- `src/Gallery.Web/Components/Molecules/Nav.razor` and `Nav.razor.css`
- `src/Gallery.Web/Components/Templates/PublicLayout.razor` and `PublicLayout.razor.css`
- `src/Gallery.Web/Edge/PurgeSets.cs`
- `src/Gallery.Web/Admin/Pages/GalleryPage.razor`
- `src/Gallery.Web/Ui/Pages/NavPage.razor`
- `tests/Gallery.Web.Tests/HomePageTests.cs` (new)

- [ ] T006 [US1] `git mv src/Gallery.Web/Site/Pages/HomePage.razor src/Gallery.Web/Site/Pages/ListingPage.razor`,
  then apply contracts/components.md C6 (Title "Galleries", `Crumbs="Trails.Listing"`, static
  field removed). Nothing else in the file changes.
- [ ] T007 [US1] Add `Nav.Home` to `src/Gallery.Web/Components/Molecules/Nav.razor` and append the CSS to
  `Nav.razor.css`, both verbatim from contracts C2. With `Home` unset the output is byte-identical:
  the existing `molecules/nav.html` Default specimen must still match after T014's regeneration
  strip.
- [ ] T008 [US1] Add `PublicLayout.Fill` (body class, CSS) to
  `src/Gallery.Web/Components/Templates/PublicLayout.razor` and `.razor.css` per contracts C4. Change the
  default trail to `Crumbs ?? Trails.Default` and delete the static `Home` field.
- [ ] T009 [US1] In the same `PublicLayout.razor`, replace the speculation-rule string with the exact string in
  contracts C4. Change nothing else in the script block.
- [ ] T010 [US1] Write a new `src/Gallery.Web/Site/Pages/HomePage.razor` verbatim from contracts C5.
- [ ] T011 [US1] [SAFETY] Edit `src/Gallery.Web/Site/PublicEndpoints.cs` per contracts/routes.md:
  - MOVE the `/` handler body to `MapMethods("/g", …)` with `ListingPage` and rename
    `ApplyHomeHeaders` → `ApplyListingHeaders`. Keep the II.6 `Cover is not null` filter and the
    single `HomeAsync()` read untouched.
  - Add a new `MapMethods("/", …)` that takes no `ContentReader` and renders `HomePage` through
    `StatusAndHeadersResult(200, {Cache-Control: HtmlPolicy}, …, policy)`.
  - Add `/g/` → `ShortPolicyRedirectResult("/g")`.
  - Attach `.CacheOutput(PolicyName)` to `/` and `/g`.
  - Gallery page crumbs become `Trails.Under(new(gallery.Title))`.
  - Update the class summary comment's route list.
- [ ] T012 [US1] Edit `src/Gallery.Web/Edge/PurgeSets.cs` per contracts/edge.md E1: `HomePath` →
  `ListingPath = "/g"` and `Home(..)` → `Listing(..)`, with all uses and doc comments updated. No
  condition changes.
  - Then `grep -rn HomePath src` must be empty.
  - Other `src` files that reference `PurgeSets.HomePath` (GalleryService, ImageDeleteService,
    EdgeWorker if any) are renamed in the same task. List them in your report.
- [ ] T013 [US1] Admin fallback per contracts C7 in `src/Gallery.Web/Admin/Pages/GalleryPage.razor`.
  Append two specimens to `src/Gallery.Web/Ui/Pages/NavPage.razor` per contracts C2. The Default
  specimen stays first and unchanged.
- [ ] T014 [US1] Write tests in a NEW `tests/Gallery.Web.Tests/HomePageTests.cs`, `LibraryHost` with
  listed, private and empty galleries:
  - (a) `/` is 200. It has exactly one `nav.nav--home` holding exactly one `<a href="/g">Galleries</a>`,
    no `contact-sheet` element, `body` class `public fill`, `Cache-Control` = `HtmlPolicy`, and
    `<title>Gallery</title>`.
  - (b) [SAFETY] `/` logs `http.request` with route `/` and **zero** `op.timing db.` lines for that
    request. `/g` logs exactly 1 `db.site.read` (`==`).
  - (c) [SAFETY] `/g` lists exactly the listed galleries in `homeOrder` (private and empty absent,
    asserted by `class="tile"` hrefs) and has `<title>Galleries</title>`.
  - (d) `/g/` gives 301 with `Location: /g` and `ShortPolicy`.
  - (e) A second GET of `/g` is served from the output cache (1 read total). A second GET of `/` adds
    no read either.
  - (f) [SAFETY] No set returned by any `PurgeSets` method, over the PurgeSetTests fixtures, ever
    contains `/` (contracts/edge.md E2). The `/g` membership is pinned by the parent's Class A
    edits to PurgeSetTests.
  - (g) A source guard: the `/` lambda in `PublicEndpoints.cs` takes no `ContentReader`.
  - (h) The speculation rule string is exact on `/`, `/g`, a gallery page and a photo page.
  - (i) `Nav` with `Home` unset renders the same bytes as before. Compare it to the FIRST
    specimen section extracted from
    `specs/011-home-page/evidence/pre-011-specimens/006/molecules/nav.html`, style-stripped, not
    to the whole file (T013 appends specimens to the live page).
  - (j) SC-004: build `HomeCaptureLibrary` (T001), GET `/g`, extract the ContactSheet region the
    same way, and assert it is byte-equal to `specs/011-home-page/evidence/pre-011-listing.html`.
  - If routing does not deliver `/g/` to the redirect, STOP and escalate (research R-03).

**Parent stated change (after Batch B)**: apply contracts/test-impact.md Classes A, B and D to the
existing tests, exactly as verified in T002. Log old and new values per file in decisions.md.
Then build `-warnaserror`, run the full suite, and commit.

## Phase 4: User Story 2, Home-rooted breadcrumbs (P1)

**Goal**: every public trail starts with "Home"; Galleries links to `/g`.

**Independent test**: crumbs on `/`, `/g`, a gallery, a photo and the 404 page equal the
data-model.md table.

**Batch C**: one executor, after Batch B. It owns:
- `src/Gallery.Web/Site/Pages/PhotoPage.razor`
- `src/Gallery.Web/Site/Pages/NotFoundPage.razor`
- `src/Gallery.Web/Ui/Pages/BreadcrumbPage.razor`
- `src/Gallery.Web/Ui/Pages/SiteHeaderPage.razor`
- `tests/Gallery.Web.Tests/TrailRoutesTests.cs` (new)
- `tests/Gallery.Web.Tests/TrailSourceGuardTests.cs` (new)

- [ ] T015 [US2] `src/Gallery.Web/Site/Pages/PhotoPage.razor`: `Crumbs` becomes
  `GalleryHref is null ? null : Trails.Under(new(GalleryTitle, GalleryHref), new(Image.Title))`.
- [ ] T016 [US2] `src/Gallery.Web/Site/Pages/NotFoundPage.razor`: `Crumbs="Trails.NotFound"`, and delete the static
  field and its comment.
- [ ] T017 [US2] Update the catalogue specimens to the Home root in
  `src/Gallery.Web/Ui/Pages/BreadcrumbPage.razor` and `SiteHeaderPage.razor`. Replace the
  `new("Galleries", "#")` root with `new("Home", "#"), new("Galleries", "#")` in every trail
  except the "Admin gallery" specimen, which stays (admin roots are unchanged). The SiteHeader
  "Home" specimen becomes `[new("Home")]`.
- [ ] T018 [US2] [SAFETY] Write `tests/Gallery.Web.Tests/TrailRoutesTests.cs` (`LibraryHost`):
  - parse `nav.breadcrumb` (label, href) on `/`, `/g`, a listed gallery, its photo and the 404
    page, and assert equality with the data-model.md table
  - for a PRIVATE gallery's page and photo page, assert every crumb href is in
    `{"/", "/g", "/g/{id}/{slug}#p-{imageId}"}`
  - assert Escape still targets the gallery crumb: the photo page has exactly one
    `.breadcrumb a[href*="#p-"]`
- [ ] T019 [US2] Write `tests/Gallery.Web.Tests/TrailSourceGuardTests.cs` per contracts C3. Scan
  `src/Gallery.Web/Site/**/*.cs|*.razor` and `Components/Templates/PublicLayout.razor`. The string
  literals `"Home"` and `"Galleries"` appear only in `Site/Trails.cs`. HomePage and ListingPage
  use `Trails.ListingLabel` per C5 and C6. Collect every violation
  before one final assert.

**Parent stated change (after Batch C)**: apply contracts/test-impact.md Class C, then run the gate
and commit. The gate must include `NavBrowserTests.Tap_boxes_stacking_and_centring` (all title
lengths and widths) as a must-pass test. With three ancestor links the 320 px trail is the risk. A
failure goes back to the Breadcrumb CSS owner as a defect and is escalated; the test's 44 px,
overlap and one-line rules are never relaxed (analyze M1).

## Phase 5: User Story 3, big, centred links (P2)

**Goal**: the link is display-size and centred under the header at every width, and the chrome
matches every other page.

**Independent test**: Playwright on `/` at 320, 390, 768, 1280 and 2560 px. Centre offsets ≤ 2 px,
there is no scrollbar, the tap height is ≥ 44, and the font size is 48 px.

**Batch D**: one executor, after Batch C. It owns `tests/Gallery.Web.Tests/HomeBrowserTests.cs` (new).

- [ ] T020 [US3] Write `tests/Gallery.Web.Tests/HomeBrowserTests.cs` on `NavBrowserFixture`, collection
  `nav-browser`. Centre-y is measured on the `a` inside `nav.nav--home`. At each width 320, 390, 768, 1280 and 2560 (height 900, and a 390×640 phone
  case):
  - (a) the `nav.nav--home a` rect centre-x is within 2 px of `innerWidth/2`
  - (b) its centre-y is within 2 px of the midpoint between `header.site-header` bottom and
    `innerHeight`
  - (c) `document.documentElement.scrollHeight <= innerHeight` and `scrollWidth <= innerWidth`
  - (d) `getComputedStyle(a).fontSize === '48px'`, which is the max of every element's computed
    font size on `/`, `/g`, a gallery and a photo page
  - (e) the link rect height is ≥ 44
  - (f) a no-JS context walks `/` → click Galleries → URL is `/g` and the grid is present
  - Park the pointer at (0,0) before each `GotoAsync` (speculation-rule pitfall).
  - Collect failures and assert once.

**Parent stated change (after Batch D)**: edit `tests/Gallery.Web.Tests/LayoutConsistencyBrowserTests.cs`
per contracts/test-impact.md T-IX. Add listing `/g` after home. Every page is compared on all seven
measures, with no exception. Run it at all five widths, then commit.

## Phase 6: Polish and cross-cutting (parent unless marked)

- [ ] T021 Regenerate the snapshots. Snapshots are regenerated on the merged tree by the parent, never by an executor.
  - Delete the five `006/ui-baseline` files and `005/baseline/PublicLayoutPage.html` named in
    test-impact.md, run the two snapshot test classes to rewrite them, and delete strays.
  - Write `specs/011-home-page/evidence/snapshot-diff.sh` from 010's `snapshot-diff-review2.sh`
    pattern (strip scoped ids, blank `<style>`, collapse whitespace). It strips exactly the four
    intended changes in test-impact.md.
  - Self-test it first, saved as `snapshot-diff-selftest.sh` and its output:
    - a temp copy with only the intended edits must report EQUAL
    - a stray edit to `tokens.html` must report RESIDUE
  - Run it on the real regeneration and tee the output to `snapshot-diff.txt`. It must report EQUAL.
- [ ] T022 [P] Write `specs/011-home-page/evidence/p95.sh` and `p95-selftest.sh` from 010's.
  - Routes `/ /g /g/{id}/{slug} /g/{id}/{slug}/{imageId}`, max reads `0 1 2 2`, compared with `==`
    for the first two.
  - The self-test covers a plain log, an empty log (must fail), an over-limit value (fails its row
    only) and an under-sampled route.
  - Add a test-hosted capture writing `evidence/home-timings.log` (the `NavTimingEvidenceTests`
    pattern; parent-owned edit to add `/` and `/g`, Class A).
  - Run it and tee to `p95-output.txt`. SC-006: `/` p95 < 50 ms.
- [ ] T023 [P] VII.2 design-record update in `specs/001-architecture-stack-design/draft/`:
  - `02c` D-13: the listing is the single read
  - `02d`:
    - D-15: pages list
    - D-16: purge set says `/g`, `/` never purged
    - D-20: the home usage becomes the listing usage
    - D-21: `/` = `PublicLayout` Fill + `Nav` Home; `/g` = listing; amended-by-011 note on Nav and the Home trail
  - D-23 (`02d`): "over the home page's first cover" → "over the first cover in the galleries
    listing (the same `homeCards` read)", and "Gallery list: the home grid" → "Gallery list: the
    listing grid"
  - `05` lifecycle "Pages and caches": home page → galleries listing
  - `06` edge cases E-16
  - `07` traceability II.2, II.6, IV.9 rows (IV.9 adds "home page: no read")
  
  Each edit is a scripted replacement that asserts each old string occurs exactly once. Then run
  `bash specs/001-architecture-stack-design/evidence/assemble.sh` and `validate.sh`, and tee the
  output to `specs/011-home-page/evidence/design-validate.txt`.
  `grep -n "home page" docs/design.md` is reviewed hit by hit.
- [ ] T024 [P] Write `specs/011-home-page/evidence/verify.sh`, a read-only live check against a
  `--no-launch-profile --urls http://127.0.0.1:5092` instance started from `artifacts/`, which
  stops only its own PID. Checks:
  - `/` 200 and has `nav--home`
  - `/` has no `contact-sheet`
  - `/g` 200 with tiles
  - `/g/` 301 → `/g`
  - breadcrumb roots on all five page types
  - `Server-Timing` on `/` and `/g`
  
  Prefix every negative grep with a file-exists check. Run it against the pre-011 commit in a
  throwaway worktree first: every new-mechanism check must FAIL there. Then run it on the feature
  tree and tee to `verify-output.txt`.
- [ ] T025 Non-vacuity. Copy `templates/non-vacuity.sh` (speckit-orchestration skill) to
  `specs/011-home-page/evidence/non-vacuity.sh` with one `brk` per [SAFETY] guard:
  - `/` handler given a `HomeAsync()` call → T014(b) red
  - listing II.6 filter removed → T014(c) red
  - `ListingPath` set to `/` → T014(f) red
  - `Trails.Under` given a `new("Other", "/g/zz/other")` crumb → T005 and T018 red
  - a `"Galleries"` literal added to `NotFoundPage.razor` → T019 red
  - `nav--home` font-size changed to `var(--type-brand)` → T020(d) red
  
  Dry-run with `NV_DRYRUN=1`, then do the real run. Each red run must fail on its assertion. Tee
  the output to `non-vacuity.txt`.
- [ ] T026 Create `specs/011-home-page/decisions.md`. Rows:
  - DEFAULT rows for: Nav reuse (R-02), listing title, vertical centring, admin empty-gallery View → `/g` (R-10), the T-IX measure-7 exclusion, and the blocked routing probe (R-03)
  - one STATED-CHANGE row per test file edited in T002/Class A–D
  - every executor `decisions_made`, as accepted or REJECTED rows
- [ ] T027 Update `docs/roadmap.md` with an 011 entry: status, evidence paths and OWNER-ONLY checks.
  Then do a full clean build (`dotnet clean` then `dotnet build -warnaserror`) and run the full
  suite. `git checkout --` any evidence logs the suite regenerates, then commit.

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 Quickstart Part C on your watch (press Ctrl+R first), at desktop and phone widths.
- [ ] O-02 Real Chrome: on `/`, hover Galleries, then check DevTools > Application > Speculative loads
  shows `/g` prerendered (R-04).

## Dependencies and execution order

- T001–T003 (parent) come before Batch A.
- T004 and T005 run in parallel within Batch A.
- The order is Batch A → Batch B → parent A/B/D edits → Batch C → parent C edits → Batch D →
  parent T-IX edit → Polish.
  - Batches are sequential because B, C and D all render through `PublicLayout` and the trail
    builder: they share files and depend on each other's output.
- Polish: T021 comes first, because snapshots need the final markup. T022, T023 and T024 can run
  in parallel. T025 needs all guards present. T026 and T027 come last.

## Parallel example

```text
Batch A: T004 (Tokens.cs + TypeDisplayTests.cs) ‖ T005 (Trails.cs + TrailsTests.cs)
Polish:  T022 (p95.sh) ‖ T023 (design drafts) ‖ T024 (verify.sh)
```

## Implementation strategy

- MVP = Phases 1–3 (US1). `/` is home and `/g` is the listing. Trails are already Home-rooted on
  `/`, `/g` and the gallery page through Batch B's call sites.
- US2 finishes the photo and 404 trails and their guards. US3 proves the look by measurement.
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
