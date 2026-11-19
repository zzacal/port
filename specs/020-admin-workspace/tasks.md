# Tasks: Admin Workspace

**Input**: design documents in `specs/020-admin-workspace/`:
- spec.md (US1–US6, FR-001–FR-047, SC-001–SC-013, Clarifications)
- plan.md, research.md (R-01–R-15), data-model.md, decisions.md, quickstart.md
- contracts/http.md (W1–W4, A1–A11, U2/U3 changed, U12, U13, removed routes)
- contracts/components.md (K1–K12, removed list, catalogue, scripts C12–C14)
- contracts/log-events.md
- contracts/test-impact.md, **including its "Fact-level expansion" section (rows TI-01…TI-70)**
- Constitution `.specify/memory/constitution.md` v6.3.0 (binding)

**Source of truth**: the contracts, the research decisions, and the corrections C-01…C-17 below
(they override the contract text they name). Implement exactly that. If something is missing or
contradicts the code, stop and report a blocker. Do not invent a name, a route, a message or a field.

**Tests**: required. Every batch writes its tests in the NEW test files it names, in the same
batch as the code. SCs are measured (IV.1, IX.4); every [SAFETY] guard gets a deliberate break in
T068.

**Build order** is research R-15: every checkpoint leaves the full suite green (after the PARENT
applies that checkpoint's test-impact rows) and the admin usable.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081.** Never kill or restart it, never run
  `pkill`/`killall`, never bind :5080 or :5081. Build and test into your own output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts/exec-<batch>`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts/exec-<batch> --blame-hang-timeout 120s --filter <your classes>`
  - macOS has no `timeout`; use `--blame-hang-timeout`. Docker must be running (`docker info`; if
    not, `open -a Docker` and poll).
  - If the build breaks in a file you do not own, a sibling is mid-edit: wait about 60 s and
    retry; never edit that file.
  - Resolve repo paths with the existing walk-up helpers (`Render.RepoRoot`, `Render.WebRoot`),
    never a fixed `..` count.
- **Existing tests must pass UNMODIFIED, except the rows of contracts/test-impact.md (TI-nn),
  which only the PARENT applies, at the checkpoint the row names.**
  - Any other failure of an existing test means your change is wrong. Fix the code, never the
    test, a fixture, or a baseline. Never edit a test to make it green.
  - Executors never edit existing files under `tests/` (fixtures included), any file under
    `specs/`, `specs/006-public-site-local/evidence/ui-baseline/`, or any evidence script.
  - Expected red before the PARENT's checkpoint: exactly the facts the checkpoint's TI rows name.
    Report them by name; do not touch them. A strict snapshot test writes a missing baseline file
    and fails once: delete what it wrote and report it.
- **Never commit.** The PARENT commits after its own gate.
- **PARENT-only files**: `src/Gallery.Web/Program.cs`, `src/Gallery.Web/AppBuilder.cs`,
  `src/Gallery.Web/Admin/AdminEndpoints.cs`, `src/Gallery.Web/Library/LibrarySetup.cs`,
  `src/Gallery.Web/Upload/UploadSetup.cs` (bootstrap, DI and route-group wiring). If your code
  needs wiring, write the code and say exactly which line the PARENT must add.
- **Forbidden**:
  - no new NuGet package, no new design token (decisions row 22), no `style=` attribute, no
    colour or size literal except the photo-overlay values `Tile` and `SelectMark` already use;
  - no icon-only control anywhere in the workspace; every action is a `Button` with visible text
    (FR-010); the select mark is the one glyph on a photo; no `Icon`/`IconButton`/`Icons` use;
  - exactly one `<script>` element on a workspace page: `<script type="module" src="/admin-assets/workspace.js?v={hash8}">`,
    rendered by `WorkspacePage`, never by `AdminLayout` (C-06); `upload.js` is loaded by dynamic
    `import()` only;
  - the four string-to-markup sinks `innerHTML`, `outerHTML`, `insertAdjacentHTML`,
    `document.write` in no admin script; `DOMParser` only inside the function `swap` of
    `workspace.js`. Script-built markup is cloned from server-rendered `<template>`s and filled
    through `textContent`, `value`, `checked`, `setAttribute` only;
  - new CSS classes start with `ws-`; never reuse another element's root class.
- **Call, never copy.** The A-routes call `ImageService.UpdateTextAsync`,
  `ImageDeleteService.DeleteManyAsync`, `GalleryService.CreateAsync`, `ImageService.BulkAddAsync`,
  `GalleryService.ReorderHomeAsync`, `GalleryService.UpdateTextAsync`, `GalleryService.LoadAsync`,
  `GalleryService.PublishAsync`, `GalleryService.UnpublishAsync`, `GalleryService.ReorderAsync`,
  `GalleryService.SetCoverAsync`, `GalleryService.DeleteAsync`; `RemoveManyAsync` calls
  `GalleryService.Begin` and `RemoveOneAsync`; U12 calls `GalleryService.AddAsync`; title rules are
  `GalleryService.TitleProblem`; JSON bodies are read with `UploadEndpoints.ReadAsync<T>` and
  errors written with `UploadEndpoints.Error` (made internal by T013). A second copy of a rule,
  a purge, a projection, a message or a tile mapping is a defect. Keep existing public method
  signatures; add siblings.
- **No service method is deleted.** `GalleryService.SaveAsync`, `GalleryService.RemoveAsync`,
  `ImageService.SavePanelAsync`, `MembershipEdit` stay (their service-level tests stay U).
- **Names, verbatim** (contracts): folders `src/Gallery.Web/Workspace/`; types `WorkspaceApi`,
  `WorkspaceEndpoints`, `WorkspaceReader`, `Messages`, `WorkspacePage`, `Mark`, `PhotoCell`,
  `GalleryChecklist`, `UploadRow`, `UploadRowState`, `Toast`, `CollectionList`, `UploadTray`,
  `TrayState`, `PhotoStage`; methods `GalleryService.RemoveManyAsync(string gid, IReadOnlyList<string> ids)`,
  `GalleryChecklist.Match`, `Sizes.ForFill()`, `ConfirmService.JoinAsync` (C-02); custom elements
  `<admin-workspace>`, `<upload-tray>`; header `X-Workspace: main|chrome|more`; data attributes
  `data-csrf`, `data-cap`, `data-upload-src`, `data-worker-src`, `data-id`, `data-title`,
  `data-caption`, `data-galleries`, `data-consequence` (C-10), `data-gid`, `data-slug`,
  `data-public`, `data-gq`, `data-member`, `data-mk`, `data-name`, `data-panel`, `data-next`;
  item state `joining` (`ItemStates.Joining`); error codes `destination-public`, `gallery-gone`,
  `not-active`, `stale-attempt`, `changed-after-validation`, `still-working`, `bad-request`.
- **Log events, verbatim** (contracts/log-events.md; evidence scripts parse them; add no other):
  - `http.request {method} {route} {status} {duration_ms} {db_reads}` (existing middleware; C-08);
  - `op.timing {op} {duration_ms} {outcome}` with ops `db.library.count` (now one aggregate of both
    counts), `db.library.page`, `db.gallery.load`, `db.galleries.list`, `db.site.order` (reads,
    `isDbRead: true`), `library.bulk.remove` (A8), `upload.join` (U12, whole call);
  - `upload.joined {batch} {item} {gallery} {public} {duration_ms}` (`gallery` = id or `-`,
    `public` = `true`/`false`/`-`); `upload.join-refused {batch} {item} {reason}` (reason
    `changed after validation`); `upload.destination-refused {batch} {gallery}`;
    `upload.cleared {batch} {dropped}`;
  - removed with their routes: `upload.confirm`, `upload.confirm.item`, `upload.confirmed`,
    `upload.discarded`.
- **[SAFETY]** marks a task implementing a constitution security, consent or public-isolation
  control: I.3 owner-only and I.6 CSRF on every new route; II.5a to II.5d (aimed upload); II.6 (A6
  branch order); III.6 (copy-if-match publishes nothing); the join claim; removed routes answer as
  unknown; no admin markup or script on public pages; the script-sink and `DOMParser` guards.
  [SAFETY] tasks are blocking and non-deferrable: a batch with one undone is not done.
- **Tight assertions**: compare lists, hrefs and JSON fields for EQUALITY; "absent" means no
  element or attribute (`class="ws-mark"`), never a bare substring; "no X in the panel" excludes
  `<template>` content unless the template is the subject; measure geometry in a real browser;
  never accept two outcomes in one assertion.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back.
- **Your final message must be ONLY the JSON report** (`tasks_completed`, `build_succeeded`,
  `tests_passed`, `decisions_made`, `blockers`).

## Corrections to the contracts (binding; the PARENT folds them into the contract files in T002)

- **C-01** U2 keeps `"confirming": false` in the batch object, always false
  (contracts/http.md:117 says `confirming` is removed; `UploadFlowTests.cs:200` reads it and must
  pass unmodified, SC-012). Only the item state value `confirming` is gone (it never existed).
- **C-02** There is no `JoinService`. `src/Gallery.Web/Upload/ConfirmService.cs` keeps the class
  name `ConfirmService` and its public statics (`MasterKey`, `TitleFrom`, the constants), loses
  `ConfirmAsync`/`RunAsync`/`ConfirmResult`/`ConfirmStatus`, and gains
  `JoinAsync(string batchId, string itemId, string attemptId)` built from the body of the private
  `ConfirmItemAsync` (plan.md:131, research R-06, contracts/http.md:144 say `JoinService`; U tests
  call `ConfirmService.MasterKey`: ImageDeleteTests.cs:59, RemoveSeededTests.cs:101,
  LibraryDeleteTests.cs:59, Fixtures/LibraryHost.cs:233).
- **C-03** Catalogue insertion points (contracts/components.md:144 says "after the last entry of
  their level"; `NavCataloguePageTests.cs:61` pins `TakeLast(5)` of the molecules): `Mark` after
  `SelectMark`; `PhotoCell`, `GalleryChecklist`, `UploadRow`, `Toast` (in that order) after
  `DangerConfirm`; `CollectionList`, `UploadTray`, `PhotoStage` after `SignInCard`.
- **C-04** `ContactSheetPage` loses its specimens 4–6 at removal (they render `SelectionBar`,
  `DragHandle`, `BatchItem`, `BatchFooter`: ContactSheetPage.razor:27-59) and gains one appended
  specimen "Workspace fill grid" at T027. research R-11 (lines 211-212) said it only gains one.
  `PublicOutputUnchangedTests` passes unmodified after the PARENT trims the 005 baseline (TI-57).
- **C-05** `AdminLayout` changes in two steps. T029 adds the workspace mode additively (old
  parameters kept, default render byte-identical) because the old pages still need `Current`,
  `Crumbs`, `Aside`, `ShowOwnerBar`, `SignOut` until removal. T052 makes it final (K1).
- **C-06** The workspace's `<script>` is rendered by `WorkspacePage`, not `AdminLayout`
  (`OrganismComponentTests.AdminLayout_has_no_script_element` stays U).
- **C-07** A2 message, exact: `{n} deleted.` (n pluralised, "1 photo deleted.") then, when
  leftovers > 0, ` {k} files could not be removed and were logged.`, then, when failed > 0,
  ` {f} could not be deleted. Try again.` (today's sentences, LibraryEndpoints.cs:555-557).
- **C-08** The request line is `http.request {method} {route} {status} {duration_ms} {db_reads}`
  with the route TEMPLATE (RequestTimingMiddleware.cs:46); contracts/log-events.md:3 omits method
  and `db_reads`.
- **C-09** `templates/admin-layout` shows the three regions only. Bare mode cannot be shown on the
  same page (AdminLayoutPage renders the layout as the whole document); it is covered by
  `WorkspaceComponentTests` and the sign-in capture (T005, T053).
- **C-10** Each `PhotoCell` root also carries `data-consequence` = `Messages.DeleteConsequence(GalleryIds.Count)`
  (the photo delete confirm text depends on the gallery count, PhotoEditPage.razor:95, and the
  script never composes text). The selection confirm text is the server's bulk sentence in the
  template; the script sets only the count.
- **C-11** `LibraryAccessTests` is retired; `WorkspaceAccessTests` (new) carries its three
  guarantees for W1–W4 and A1–A11 (TI-35).
- **C-12** Document titles (planning default, not in the contracts): W1 "All photos", W2 "Not in a
  gallery", W3 the gallery's title. The list's current row has `aria-current="page"`.
- **C-13** Appended catalogue specimen labels: `SidePanelPage` "Without a save row";
  `ContactSheetPage` "Workspace fill grid".
- **C-14** A-routes read their JSON with `UploadEndpoints.ReadAsync<T>(ctx)` inside the handler
  (no `[FromBody]` binding), so the antiforgery filter answers 400 first and a bare POST with the
  CSRF header answers 400 `bad-request`, never 500.
- **C-15** Until Phase 5 the workspace page renders neither `Add photos` nor the tray mount; the
  old upload page is still the way to upload (R-15: admin usable at every step).
- **C-16** `LibraryReader.Live` and `LibraryReader.TileFields` become `internal static` (T030) so
  `WorkspaceReader` reuses them; at removal (T049) `PageAsync`, `PhotoAsync`, `Tile`, `StatusLine`,
  `LibraryTile`, `LibraryPageData` are deleted with their last callers (TI-61).
  `LibraryReader.GalleriesAsync` and `GalleryRef` stay.
- **C-17** p95 rows read per-source pre logs (`evidence/pre-timings/*.log`) because L2 plain saves
  and L2 membership saves share one route template; `evidence/pre-timings.log` is their
  concatenation for quickstart's command.
- **C-18** `Sizes.ForFill()`'s exact string is not in the contracts. The PARENT fixes it in T002
  from K1's frame (list 232 px, panel 320 px, the existing `--space-*` gaps, columns of at least
  168 px, two columns below 600 px) and writes it into contracts/components.md K5; T018 uses it
  verbatim and is blocked until then.
- **C-21** (found at gate B) The workspace frame switches at `Tokens.BreakLarge` (1200 px), not 900 px: `TokenMediaAgreementTests` requires every media query width to equal a token breakpoint, and decisions row 22 forbids a new token. Between 600 and 1199 px the workspace uses the one-column arrangement of US6.
- **C-22** (found at C1b) The request line's `db_reads` counts only `isDbRead` ops, and `GalleryService.LoadAsync`'s `db.gallery.load` is not one, so W3 full logs 4 and W3 `main` logs 1 (R-02 says 5 and 2). The op list per response equals R-02 (`WorkspacePageTests` asserts both); `p95.sh` keys W3 rows on 4/1. `AdminNavConsistencyTests` is retired at CP-C, not CP-E: its loop GETs `/admin`, which becomes the workspace at CP-C. TI-41 asserts the module `src` prefix only: `OwnerClaimTests`' host has no web root, so the version is `dev`; the 8-hex version is pinned by `WorkspacePageTests`.
- **C-19** Labels not in the contracts (planning defaults, PARENT may reword in T002 before
  Batch B): the naming input's label "New gallery name"; the photo cell's select-mark label
  "Select {title}".

## Format: `[ID] [P?] [Story] Description`

Tags after the id: `[P]` parallel (disjoint files, no dependency on an unfinished task),
`[USn]` story, `[SAFETY]`, `PARENT` (the parent does it, not an executor), `OWNER` (only the owner
can do it). Checkpoints (CP-0 … CP-H) are PARENT gates: read the diff, apply the named TI rows
with `evidence/test-impact-edit.py`, regenerate the named snapshots with a diff against the frozen
copy, run `evidence/gate.sh <label>`, grep each [SAFETY] mechanism, commit.

---

## Phase 1: Setup (PARENT, before any code change)

- [X] T001 PARENT Record `specs/020-admin-workspace/evidence/baseline.txt`: `git rev-parse HEAD`,
  the full-suite `Passed!/Failed!` lines from a finished run (never a partial file),
  `ls tests/Gallery.Web.Tests/*.cs | wc -l` (144). Accept: file has the three values.
- [X] T002 PARENT Fold corrections C-01…C-17 into `contracts/http.md`, `contracts/components.md`,
  `contracts/log-events.md`, `plan.md`, `research.md` (R-06, R-11) as "AMENDED (tasks)" notes in
  place, and add PLAN-CORRECTION rows 23+ to `decisions.md` (one per correction, with pivot cost).
  Re-run `bash specs/020-admin-workspace/evidence/test-impact-hits.sh > …/test-impact-hits.txt`;
  any hit outside the TI rows becomes the next TI row "(found at T002)". Accept:
  `grep -c 'AMENDED (tasks)' contracts/*.md plan.md research.md` ≥ 10; decisions.md has rows 23–39.
- [X] T003 PARENT Write `specs/020-admin-workspace/evidence/gate.sh` from
  `specs/017-photo-edit-screen/evidence/gate.sh`: output `/tmp/020-gate-$L.txt`; the `restore`
  list adds `specs/017-photo-edit-screen/evidence`, `specs/018-local-dev-signin/evidence`,
  `specs/019-selection-bar-layering/evidence`, `specs/005-admin-ui-components/evidence/baseline`
  is NOT restored; export `LIST_TIMING_LOG`, `PHOTO_TIMING_LOG`, `TILE_TIMING_LOG`,
  `WORKSPACE_TIMING_LOG`, `PUBLIC_CAPTURE_DIR` to `/tmp/020-gate-*` paths so a gate never
  overwrites committed captures; print failure messages (`grep -A3 'Error Message'`) for failed
  facts. Accept: `bash …/gate.sh setup` exits 0 and `git status --short specs/` is empty after it.
- [X] T004 PARENT Write `tests/Gallery.Web.Tests/Fixtures/PublicCapture.cs` and
  `tests/Gallery.Web.Tests/PublicCaptureEvidenceTests.cs` (`[Trait("Category","Evidence")]`):
  build `HomeCaptureLibrary.BuildAsync` on a `LibraryHost`, GET every public page type (`/`, `/g`,
  each listed gallery page, one photo page per listed gallery, `/c`, every camera page and one
  camera photo page if the fixture has camera data, an unknown `/g/{id}/x` 404 page), replace each
  gallery id with `G0…G4` (creation order) and strip nothing else, write each body to
  `{PUBLIC_CAPTURE_DIR ?? specs/020-admin-workspace/evidence/pre-public}/{label}.html` plus
  `index.txt` (label, path, status). Accept: two runs into two dirs are `diff -r`-identical.
- [X] T005 PARENT Write and run `specs/020-admin-workspace/evidence/capture-pre.sh` (quickstart Part A)
  on the UNCHANGED tree:
  1. run the five timing tests (`LibraryTimingEvidenceTests`, `GalleryListTimingEvidenceTests`,
     `PhotoEditTimingEvidenceTests`, `MembershipTimingEvidenceTests`, `TileTimingEvidenceTests`,
     `Category=Evidence`), with `LIST_TIMING_LOG`/`PHOTO_TIMING_LOG`/`TILE_TIMING_LOG` pointed into
     `evidence/pre-timings/`; copy `specs/009-library-galleries/evidence/library-timings.log` and
     `specs/014-gallery-combobox/evidence/membership-timings.log` there, then `git checkout` those
     two committed files; write `evidence/pre-timings/{library,list,photo,membership,tile}.log`,
     `evidence/pre-timings.log` = `cat library.log tile.log` (C-17), and
     `evidence/pre-timings-counts.txt` (route template → n per file);
  2. run `PublicCaptureEvidenceTests` twice (`pre-public/` and `/tmp/020-pre-public-2`), `diff -r`
     must be empty;
  3. freeze copies: `specs/006-public-site-local/evidence/ui-baseline/` → `evidence/pre-ui-baseline/`;
     `specs/005-admin-ui-components/evidence/baseline/ContactSheetPage.html` →
     `evidence/pre-005-ContactSheetPage.html`;
  4. sign-in layout capture: a TEMPORARY test (not committed) renders `Render.Html<AdminLayout>`
     with `Title="Sign in · Gallery"`, `ShowOwnerBar=false`, ChildContent `<p>x</p>`, and writes
     `evidence/pre-admin-layout-bare.html`; run twice, `cmp`, delete the temporary test.
  Commit the script, the test files of T004 and every capture before T011. Accept: counts file
  shows `GET /admin/library` n ≥ 100 (tile.log), each B2/B3/B4/B5 POST n ≥ 30 (library.log), L2
  POST n ≥ 100 (membership.log); `git status` clean after commit.
- [X] T006 [P] PARENT Copy `specs/017-photo-edit-screen/evidence/p95.sh`, `p95-selftest.sh` and
  `p95-selftest/` to `specs/020-admin-workspace/evidence/` and rewrite the rows (C-08, C-17; route
  templates as logged — grep one real line of each first):
  - SC-005 reads (post `workspace-timings.log`, n ≥ 100 per row): `GET /admin` with `db_reads` 4
    (W1 full) and `GET /admin/g/{gid}` with `db_reads` 5 (W3 full) vs pre `GET /admin/library 200`
    (tile.log); `GET /admin` with `db_reads` 2 and `GET /admin/g/{gid}` with `db_reads` 2 (`main`
    fragments) vs the same; info row (no pass/fail): W3 vs pre builder `GET /admin/galleries/{gid}`.
    Pass: post p95 ≤ pre p95 × 1.10 + 1 ms (nearest-rank).
  - Reads per response equal R-02: every W1/W2 line `db_reads` ∈ {4, 2, 1}, every W3 line ∈ {5, 2},
    every `GET /admin/tiles/{iid}` line = 1 (equality, never `<=`).
  - SC-006 writes (post n ≥ 30): A1 `POST /admin/api/photos/{iid}` vs library.log
    `POST /admin/library/images/{iid}`; A5 vs `POST /admin/galleries/{gid}`; A7
    `POST /admin/api/galleries/{gid}/photos` vs membership.log `POST /admin/library/images/{iid}`;
    A8 vs `POST /admin/galleries/{gid}/remove`; A9 vs `POST /admin/galleries/{gid}/order`; A10 vs
    `POST /admin/galleries/{gid}/cover`. Pre n is taken as captured and printed.
  - SC-013: nine `upload.joined` lines; every `op.timing upload.join` with outcome ok ≤ 2000 ms; no
    `upload.joined` line for the forced-failure item id (printed by the evidence test).
  Regenerate the self-test logs with `p95-selftest.sh` (one passing log, one per failing row, an
  empty log, a JSON log, an under-sampled log) into `p95-selftest.txt`. Accept: self-test expected
  pass/fail per log holds.
- [X] T007 [P] PARENT Write `specs/020-admin-workspace/evidence/compare-public.sh`: runs
  `PublicCaptureEvidenceTests` with `PUBLIC_CAPTURE_DIR=/tmp/020-post-public`, then
  `diff -r evidence/pre-public /tmp/020-post-public`, prints `EQUAL n files` or each differing file;
  exit = number of differing files. Self-test into `compare-public-selftest.txt`: against itself
  0; with one byte changed in a temp copy 1. Accept: both self-test rows hold.
- [X] T008 [P] PARENT Write `specs/020-admin-workspace/evidence/non-vacuity.sh` from
  `specs/017-photo-edit-screen/evidence/non-vacuity.sh` (same `brk` engine, `NV_DRYRUN`,
  `NV_ONLY`, clean-tree check over its FILES) with the break table of T068 as `brk` lines.
  `NV_DRYRUN=1` now reports "matched nothing" for code not yet written; re-dry-run after every
  checkpoint. Accept: script parses (`bash -n`), dry-run lists every break id.
- [X] T009 [P] PARENT Write `specs/020-admin-workspace/evidence/test-impact-edit.py` from 017's:
  one count-asserting (file, old, new, count, row) entry per TI row, row filter by id or by
  checkpoint (`--cp CP-D`), `DRYRUN=1`, abort without writing on any count mismatch, print `OK row
  file xN`, tee to `evidence/test-impact-edit-<cp>.txt`. Rewrites carry the full new fact bodies
  written from contracts/test-impact.md TI rows and contracts/http.md. Accept: `DRYRUN=1` on the
  unchanged tree matches every row's count.
- [X] T010 PARENT TI-01: move `LibraryHtml` to `tests/Gallery.Web.Tests/Fixtures/LibraryHtml.cs`
  and `GalleryHtml` to `tests/Gallery.Web.Tests/Fixtures/GalleryHtml.cs` verbatim; add
  `LibraryHost.PostJsonAsync`/`GetJsonAsync` and `UploadSite.OpenWorkspaceAsync` as TI-01 says.
  Accept: gate green with the same `Passed!` counts as T001.

**CP-0 (PARENT)**: gate `setup`; commit "020: setup captures and evidence scripts".

---

## Phase 2: Foundational A — write routes beside the old pages (R-15 step 2)

Batch A owns: `src/Gallery.Web/Library/GalleryService.cs` (append only),
`src/Gallery.Web/Workspace/Messages.cs`, `src/Gallery.Web/Workspace/WorkspaceApi.cs`,
`src/Gallery.Web/Upload/UploadEndpoints.cs` (two `private` → `internal` only),
`src/Gallery.Web/Admin/Pages/PhotoEditPage.razor` (one body line only), new
`tests/Gallery.Web.Tests/WorkspaceApiTests.cs`, new `tests/Gallery.Web.Tests/WorkspaceAccessTests.cs`.

- [X] T011 [P] Add `GalleryService.RemoveManyAsync(string gid, IReadOnlyList<string> ids)` in
  `src/Gallery.Web/Library/GalleryService.cs` (A8, contracts/http.md; data-model.md validation
  table): timed `library.bulk.remove`; ids count 0 or > `LibraryOptions.PageSize` →
  `OperationResult.Refused(422, …)` with the same two sentences `BulkAddAsync` uses ("Select at
  least one photo." / "Select at most {PageSize} photos."); unknown gallery → `NotFound()`;
  otherwise ONE `Begin("gallery.remove.bulk")`, `LoadAsync` once, `RemoveOneAsync(gid, iid, scope,
  loaded)` per distinct id (non-members skipped), ONE `scope.CompleteAsync()`; result carries the
  removed count. Depends: none. Accept: T016 facts `RemoveMany_*` green.
- [X] T012 [P] Write `src/Gallery.Web/Workspace/Messages.cs` (static class `Messages`) with one
  method per row of the contracts/http.md Messages table, `{n}` pluralised photo/photos and
  "it"/"them" for 1/many, A2 per C-07, the U13 and U3 messages, the selection-cap sentence "You
  can select up to {cap} photos at a time.", the empty-collection sentences of components.md K12,
  and `DeleteConsequence(int galleries)` with the body of `PhotoEditPage.DeleteConsequence`
  moved here verbatim; `PhotoEditPage.DeleteConsequence` becomes `=> Messages.DeleteConsequence(galleries);`
  (`src/Gallery.Web/Admin/Pages/PhotoEditPage.razor:95`). Depends: none. Accept: T016
  `Messages_*` facts pin every string; `PhotoEditPageTests`, `LibraryEditTests` stay green.
- [X] T013 [P] In `src/Gallery.Web/Upload/UploadEndpoints.cs` change only `private static … ReadAsync<T>`
  (line ~818) and `private static IResult Error(…)` (line ~838) to `internal static`. No other
  edit. Depends: none. Accept: `git diff` shows 2 changed lines.
- [X] T014 [SAFETY] Write `src/Gallery.Web/Workspace/WorkspaceApi.cs`:
  `public static RouteGroupBuilder MapWorkspaceApi(this RouteGroupBuilder admin)` mapping A1–A11
  exactly as contracts/http.md "Writes" (paths, `RequestSizeLimitAttribute` 4 KiB default, 16 KiB
  for A2/A3/A4/A5/A7/A8, 64 KiB for A9), each handler: `OpaqueId.IsValid` on `{gid}`/`{iid}` else
  404; body via `UploadEndpoints.ReadAsync<T>` (C-14) → 400 `bad-request` when absent/invalid; ONE
  call to the service in the "Service" column; `OperationResult.StatusCode` → error JSON
  `{ error, message }` with the service's reason as `message`; success JSON exactly the listed
  fields plus `message` from `Messages`. A3 answers 201. A11 on a gone gallery answers 200 "That
  gallery was already deleted.". **A6 [SAFETY II.6]** in the G4 order (LibraryEndpoints.cs:279-319):
  unknown → 404; `public: true` → `PublishAsync(gid, confirmed: true, loaded: gallery)`;
  `public: false` on a non-public gallery → 409 `EmptyPublishReason` when it has no items, else
  200 with no write; `public: false` on a public gallery → `UnpublishAsync`. The routes go on the
  `admin` group passed in (owner policy + `AdminAntiforgeryFilter`, [SAFETY I.3, I.6]); never on
  `app`, never `AllowAnonymous`. Depends: T011, T012, T013. Accept: compiles; PARENT wiring T015.
- [X] T015 PARENT In `src/Gallery.Web/Admin/AdminEndpoints.cs` `MapAdmin`, add
  `admin.MapWorkspaceApi();` after `admin.MapLibrary();` and before the H6 catch-all. Accept:
  `grep -n 'MapWorkspaceApi' src/Gallery.Web/Admin/AdminEndpoints.cs` shows one line above
  `{**rest}`.
- [X] T016 [SAFETY] Write `tests/Gallery.Web.Tests/WorkspaceApiTests.cs` (`LibraryHost`,
  `PostJsonAsync`): per A-route the status, JSON shape and exact message; the carried facts of
  TI-62 (A3 G2-rules, A4 G3 stale 409 and purge rule, A6 publish/unpublish purges and owner.action
  lines, A5 rename + old-slug redirect, A9/A10 home-card cover, A8 pin cleared and public-last-photo
  `listed:false`, A11 photos kept) with the old stored/purge assertions copied verbatim from the
  named old facts (they pin fixed sets; never compute an expectation by calling an old route);
  body limits (A9 at 64 KiB + 1 → 413, A2 at 16 KiB + 1 →
  413); 101 ids → 422 for A2/A3/A7/A8; `RemoveManyAsync` flushes once (one `edge.purge` batch,
  one `db.site.project` line) for 3 ids; **A6 [SAFETY II.6]**: empty private gallery → 409
  `EmptyPublishReason` for `{ public: true }` AND `{ public: false }`, store and recorder unchanged;
  `Messages_*` facts for every message row. Depends: T014, T015. Accept: `--filter
  FullyQualifiedName~WorkspaceApiTests` green.
- [X] T017 [SAFETY] Write `tests/Gallery.Web.Tests/WorkspaceAccessTests.cs` [I.3, I.6] with a
  route table of A1–A11 (W1–W4 rows are added in T035): (1) no session: POST 401 with a valid
  token, dump of `images`/`galleries`/`site` unchanged, `no-store` + `noindex`; (2) session, no
  token: JSON POST 400, bare POST 400, wrong token 400, dump unchanged, `Recorder.AllPurged()`
  empty; (3) session + CSRF header + empty body: 400 `bad-request` on every A-route, never 500.
  Depends: T015. Accept: green; each theory has 11 rows.

**CP-A (PARENT)**: no TI rows. Gate `A`; grep `MapWorkspaceApi` is inside the `admin` group and
`AllowAnonymous` appears nowhere in `src/Gallery.Web/Workspace/`; commit.

---

## Phase 3: Foundational B — components and catalogue (R-15 step 3)

Batch B owns: `src/Gallery.Core/Images/Sizes.cs` (append), `tests/Gallery.Core.Tests/SizesTests.cs`
(append one fact), `src/Gallery.Web/Components/Organisms/SidePanel.razor`,
`src/Gallery.Web/Components/Organisms/ContactSheet.razor(.css)`, every NEW component file below,
`src/Gallery.Web/Components/ComponentTypes.cs` (append enums), `src/Gallery.Web/Ui/UiCatalog.cs`,
the 8 new `src/Gallery.Web/Ui/Pages/*Page.razor`, `SidePanelPage.razor` and `ContactSheetPage.razor`
(append one specimen each), new `tests/Gallery.Web.Tests/WorkspaceComponentTests.cs`,
`tests/Gallery.Web.Tests/GalleryChecklistTests.cs`. Optional parameters next to existing markup
follow speckit-orchestration `references/razor-optional-params.md` (no `@if` beside existing
markup); default renders must be byte-identical.

- [X] T018 [P] Add `Sizes.ForFill()` to `src/Gallery.Core/Images/Sizes.cs` (K5): the `sizes`
  string for `repeat(auto-fill, minmax(168px, 1fr))` beside a 232 px list and 320 px panel at
  ≥ 1200 px (C-21) and two columns below 600 px: the exact string fixed by C-18 (blocked until T002 has
  written it into contracts/components.md K5; escalate, never invent it). Append one fact to
  `tests/Gallery.Core.Tests/SizesTests.cs` asserting that exact string. Accept: Core tests green.
- [X] T019 [P] K2/K3: `SidePanel` gains `[Parameter] public bool Save { get; set; } = true;`
  (false renders no `sp-save` row); `ContactSheet` gains `Fill` (true → class `cs cs-fill`, no
  `cs-l*/cs-m*/cs-s*`; CSS `.cs-fill { grid-template-columns: repeat(auto-fill, minmax(168px, 1fr)) }`
  and two columns below 600 px in `ContactSheet.razor.css`). Accept: T028 byte-identity facts for
  both defaults; `PublicOutputUnchangedTests`, `UiPagesUnchangedTests` green.
- [X] T020 [P] K4 `Mark` (`Components/Atoms/Mark.razor(.css)`, root `ws-mark`, param `Text`) and
  K8 `Toast` (`Components/Molecules/Toast.razor(.css)`, root `ws-toast`, `role="status"`, params
  `Text`, `Failed`; fixed bottom centre). Accept: T028 facts.
- [X] T021 K5 `PhotoCell` (`Components/Molecules/PhotoCell.razor(.css)`): markup exactly as
  components.md K5 plus `data-consequence` (C-10); one `Tile` with no `Href`, `Overlay` =
  `SelectMark` (label "Select {title}"), `Mark` "Cover"/"New" when set, `Sizes.ForFill()`,
  `Priority`; state classes `is-selected` (with `aria-selected="true"`) and `ws-drop`. Depends:
  T018, T020. Accept: T028 facts for the five states.
- [X] T022 [P] K6 `GalleryChecklist` (`Components/Molecules/GalleryChecklist.razor(.css)` +
  `GalleryChecklist.cs` holding `public static ChecklistMatch Match(string query, IReadOnlyList<string> titles)`:
  trim; case-insensitive contains narrows; case-insensitive whole-title equality = existing;
  ordering mirrors `TokenCombobox.Match`'s `OrdinalIgnoreCase` rule; **call** `GalleryService.TitleProblem`
  for the too-long/blank create offer, never re-implement it). Markup per K6 with `TitleMax`.
  Accept: `GalleryChecklistTests` (T028) cover every K6 state and every `Match` rule the retired
  `TokenComboboxTests` pinned (list them in the file's summary).
- [X] T023 [P] K7 `UploadRow` (`Components/Molecules/UploadRow.razor(.css)`) + enum
  `UploadRowState { Waiting, Preparing, Uploading, Adding, Added, Failed, Interrupted }` appended
  to `ComponentTypes.cs`; words and buttons per K7 (`Retry`, `Remove` are `Button`s with text).
  Accept: T028 facts per state.
- [X] T024 K9 `CollectionList` (`Components/Organisms/CollectionList.razor(.css)`, root
  `ws-collections`): markup order per K9; the sign-out `<form method="post" action="/admin/signout">`
  with the antiforgery hidden input from `Csrf` and a `Button` "Sign out"; "View site ↗" with
  `target="_blank" rel="noopener"`; `aria-current="page"` on the current row; states `ws-over`,
  `ws-before`, naming input (label "New gallery name"). Depends: T020. Accept: T028 facts incl.
  `CollectionList_sign_out_form_carries_the_token`.
- [X] T025 [SAFETY] K10 `UploadTray` (`Components/Organisms/UploadTray.razor(.css)`, root `ws-tray`)
  + enum `TrayState` in `ComponentTypes.cs`: heading lines, controls and body per K10.
  **[SAFETY II.5a]**: when `DestinationPublic`, the sentence "{destination} is public. Visitors
  will see these photos as soon as each one is added." renders as plain text in a `<p class="ws-notice">`
  directly after the heading, in EVERY state including Hidden, never inside `<details>`, a
  tooltip or a `title` attribute. `Done` is `disabled` while any row is Waiting, Preparing,
  Uploading or Adding. Depends: T023. Accept: T028 `UploadTray_public_notice_is_plain_text_in_every_state`
  (5 states × public) and the K10 state facts.
- [X] T026 [P] K11 `PhotoStage` (`Components/Organisms/PhotoStage.razor(.css)`, root `ws-stage`):
  bar with `Button` "← {BackLabel}", "{Position} of {Total}", `Previous`, `Next` (unavailable at
  the ends), empty frame `ws-stage-frame`. Accept: T028 facts first/middle/last.
- [X] T027 Catalogue: 8 pages `src/Gallery.Web/Ui/Pages/{Mark,PhotoCell,GalleryChecklist,UploadRow,Toast,CollectionList,UploadTray,PhotoStage}Page.razor`,
  each showing every state of components.md (PhotoCell, CollectionList, UploadTray also a 390 px
  specimen); 8 `UiCatalog.Entries` lines at the C-03 positions with slugs `atoms/mark`,
  `molecules/photo-cell`, `molecules/gallery-checklist`, `molecules/upload-row`,
  `molecules/toast`, `organisms/collection-list`, `organisms/upload-tray`, `organisms/photo-stage`;
  append one specimen to `SidePanelPage.razor` ("Without a save row", `Save="false"`) and to
  `ContactSheetPage.razor` ("Workspace fill grid", `Fill="true"` with `PhotoCell`s) after the last
  existing specimen (C-13). No `<script>`, no `style=`. Depends: T019–T026. Accept:
  `AllPagesTests` green; `UiPagesUnchangedTests` red ONLY for the 8 new slugs (missing baseline,
  delete what it wrote), `index`, `organisms/side-panel`, `organisms/contact-sheet`;
  `IndexTests` and `OrganismStatePageTests` SidePanelPage row red (TI-50, TI-56).
- [X] T028 Write `tests/Gallery.Web.Tests/WorkspaceComponentTests.cs` and
  `tests/Gallery.Web.Tests/GalleryChecklistTests.cs` (`Render.Html<T>`): every K4–K11 state as a
  named fact; K2/K3 default renders byte-identical to the render with the new parameter at its
  default; the state-label list of each new catalogue page (exact, in order); no `<script`, no
  `style=`, no element with `aria-label` and no text (icon-only) in any new component. Depends:
  T018–T027. Accept: green.

**CP-B (PARENT)**: apply TI-50 (40 → 48) and TI-56 (CP-B part). Snapshots (TI-52 CP-B): run
`UiPagesUnchangedTests` once to write the 8 new baselines and read each; delete and regenerate
`index.html`, `organisms/side-panel.html`, `organisms/contact-sheet.html`; diff each against
`evidence/pre-ui-baseline/` — index differs only by 8 inserted `<li>`s at the C-03 positions, the
two organisms only by one appended specimen section. `PublicOutputUnchangedTests` must be green
untouched. Gate `B`; commit.

---

## Phase 4: US1 + US3 — the workspace at `/admin` (R-15 step 4) 🎯 MVP

**Goal**: one screen to organise photos and shape galleries, old pages still reachable.
**Independent test**: spec US1 and US3 independent tests at 1440 px.

### Batch C1 — server

Batch C1 owns: `src/Gallery.Web/Components/Templates/AdminLayout.razor(.css)`,
`src/Gallery.Web/Library/LibraryReader.cs` (two `private` → `internal`),
new `src/Gallery.Web/Workspace/{WorkspaceModels.cs,WorkspaceReader.cs,WorkspaceEndpoints.cs}`,
new `src/Gallery.Web/Admin/Pages/WorkspacePage.razor(.css)`, `src/Gallery.Web/Upload/AdminAssets.cs`,
new `tests/Gallery.Web.Tests/WorkspacePageTests.cs`, `WorkspaceAccessTests.cs` (append rows).

- [X] T029 [US1] AdminLayout additive mode (C-05) in `src/Gallery.Web/Components/Templates/AdminLayout.razor(.css)`:
  new parameters `List`, `Panel` (RenderFragment), `Csrf` (FormToken), `Cap` (int),
  `UploadSrc`, `WorkerSrc` (string?); when `List` is not null render K1's
  `<admin-workspace class="ws" data-csrf data-cap data-upload-src data-worker-src>` with
  `<nav class="ws-list">`, `<main class="ws-main">`, `<aside class="ws-panel">` and NO owner bar,
  breadcrumb or `<script>`; the frame CSS for ≥ 1200 px (C-21) (list 232 px, panel 320 px) lives in
  `AdminLayout.razor.css` only (IX.2). When `List` is null the output is byte-identical to today.
  Accept: `UiPagesUnchangedTests` (templates/admin-layout) and every `OrganismComponentTests`
  AdminLayout fact green unmodified; T035 facts.
- [X] T030 [P] [US1] Make `LibraryReader.Live` and `LibraryReader.TileFields` `internal static`
  (C-16) in `src/Gallery.Web/Library/LibraryReader.cs`; nothing else. Accept: 2 changed lines.
- [X] T031 [US1] `src/Gallery.Web/Workspace/WorkspaceModels.cs` + `WorkspaceReader.cs` (scoped;
  research R-02, R-03, data-model "Read models"): `CountsAsync()` = ONE aggregate timed
  `db.library.count` returning total live and live-with-empty-`galleryIds`; `ChunkAsync(bool
  unplaced, string? before)` = ONE `db.library.page` keyset find on `Live` (+ `galleryIds` empty
  for unplaced), sort `createdAt` desc, `_id` desc, `Limit(PageSize + 1)`, projection
  `TileFields`, cursor `{createdAt.Ticks}.{id}` (malformed → caller answers 400); `MembersAsync(GalleryDoc g)`
  = ONE `db.library.page` find of the gallery's live items, returned in `g.Items` order;
  `TileAsync(string iid)` = ONE `db.library.page` find; galleries via **`LibraryReader.GalleriesAsync()`**
  (called, two reads); the gallery via `GalleryService.LoadAsync` (`db.gallery.load`). Views via
  `ViewMapper.ToView`. Depends: T030. Accept: T035 read-count facts.
- [X] T032 [US1] [US3] `src/Gallery.Web/Admin/Pages/WorkspacePage.razor(.css)` per K12 MINUS the
  `Add photos` button and the `<upload-tray>` mount (C-15) and minus `PhotoStage` (Phase 7):
  `AdminLayout` with `CollectionList`; main = `PageHeading` (C-12 title, counts, "· Public|Private",
  "View on site ↗" for a public gallery with photos; actions `Clear selection` hidden until a
  selection exists), `ContactSheet Fill` of `PhotoCell`s (first 4 `PicturePriority.High`) or the
  empty sentence from `Messages`, "Show more" `Button` + sentinel `<span data-next>` when more
  exist; four panel `<template data-panel>`s (`library`, `gallery`, `photo`, `selection`) each a
  `SidePanel Save="false"` with the K12 contents, delete LAST in the danger slot; the empty-gallery
  `Switch` disabled with `GalleryService.EmptyPublishReason` as its hint; `<noscript>`The admin
  needs JavaScript.`</noscript>`; ONE `<script type="module" src="{AdminAssets.UrlFor(WorkspaceScriptPath)}">`
  (C-06). No frame CSS in `WorkspacePage.razor.css` (IX.2). Depends: T029, T031. Accept: T035.
- [X] T033 [SAFETY] [US1] `src/Gallery.Web/Workspace/WorkspaceEndpoints.cs`:
  `public static RouteGroupBuilder MapWorkspace(this RouteGroupBuilder admin)` mapping W1 `GET|HEAD /`,
  W2 `GET|HEAD /unplaced`, W3 `GET|HEAD /g/{gid}`, W4 `GET /tiles/{iid}` on the passed `admin`
  group [SAFETY I.3, I.6]; `X-Workspace` absent → full document, `main`/`chrome`/`more` → the
  fragment of contracts/http.md (reads exactly R-02), any other value → 400; `more` without a
  valid `before` → 400; unknown/invalid gallery or tile → 404; fragments are `text/html` with no
  `<html>`; antiforgery tokens from `IAntiforgery.GetAndStoreTokens` for `data-csrf` and the
  sign-out form. `src/Gallery.Web/Upload/AdminAssets.cs` gains `WorkspaceScriptPath =
  "admin-assets/workspace.js"` versioned like the others (an empty placeholder
  `wwwroot/admin-assets/workspace.js` with one comment line is created here; T039 fills it).
  Depends: T032. Accept: compiles; PARENT wiring T034.
- [X] T034 [US1] PARENT Wiring: in `AdminEndpoints.MapAdmin` replace the H4 `admin.MapMethods("/", …ShellPage…)`
  block with `admin.MapWorkspace();` (before the catch-all) and delete
  `src/Gallery.Web/Admin/Pages/ShellPage.razor`; in `LibrarySetup.AddLibrary` add
  `s.AddScoped<WorkspaceReader>();`. Accept: `GET /admin` signed in answers the workspace;
  `grep -rn ShellPage src` empty.
- [X] T035 [SAFETY] [US1] Write `tests/Gallery.Web.Tests/WorkspacePageTests.cs` (`LibraryHost`)
  and append W1–W4 rows to `WorkspaceAccessTests` [I.3]: W1/W2/W3 full and each fragment by header
  (status, `text/html`, no `<html` in fragments, 400 for a bad header, 400 for a malformed
  `before`); reads per response from the `http.request` line's `db_reads` EQUAL R-02 (4, 4, 5, 2,
  3, 1, 1); W3 members in gallery order; W1 chunk = 100 newest first with `data-next` when 101
  exist (`Library:PageSize` override 4 + 5 photos is enough); unplaced lists only photos with no
  gallery; tombstoned photos in no collection; 404s (bad id, unknown gallery, unknown/tombstoned
  tile after one `db.library.page` and zero `db.galleries.list` lines, TI-61); HEAD 200 no body;
  exactly one `<script` and it is the module of C-06 (TI-41 shape); `Guards.AssertNoInlineStyle`;
  no icon-only control (no `<button>`/`<a>` whose text is empty, anywhere, templates included —
  the `SelectMark` checkbox excepted) (SC-007); in every panel template the `DangerConfirm` is the
  last control; `Photo_cells_carry_the_delete_consequence_for_their_gallery_count` (TI-10);
  `Selection_template_delete_is_the_last_control_with_the_bulk_copy` (TI-11);
  `Empty_gallery_default_panel_has_the_switch_disabled_with_the_reason` (TI-15);
  `Collections_follow_home_order_with_counts_and_Private`; `Workspace_holds_no_storage_url_or_key`;
  the three `AdminAssets` facts for `workspace.js` (TI-62). Depends: T034. Accept: green.

**CP-C (PARENT)**: apply TI-40, TI-41. Gate `C`; check `BatchStoreTests.UploadHost_signs_in_and_caches_a_csrf_token`
and every `OwnerSessionTests`/`OwnerClaimTests`/`LocalSignInTests` fact green; grep `MapWorkspace`
inside the `admin` group; commit.

### Batch C2 — the page script

Batch C2 owns: `src/Gallery.Web/wwwroot/admin-assets/workspace.js`, new
`tests/Gallery.Web.Tests/WorkspaceBrowserTests.cs`.

- [X] T036 [SAFETY] [US1] `workspace.js` part 1 (C12): register `<admin-workspace>`;
  `swap(region, html)` is the ONLY use of `DOMParser` (`new DOMParser().parseFromString(text,
  'text/html')`, then `region.replaceChildren(...doc.body.children)`); navigation by
  `fetch(href, { headers: { 'X-Workspace': 'main' } })` + `swap` into `.ws-main`, current mark,
  `pushState`, `popstate`; failed fetch → refusal toast, page unchanged; chrome refresh helper
  (`X-Workspace: chrome`) after writes that change a gallery's existence, title, order,
  visibility or count; "Show more" and the sentinel's `IntersectionObserver` fetch `more`; every
  write sends `X-CSRF-TOKEN` from `data-csrf` and `credentials: 'same-origin'`; every result line
  is the server's `message` in the `Toast` (template clone). No sink of the four. Depends: T035.
- [X] T037 [US1] `workspace.js` part 2: selection per FR-007/FR-008 and C12 (click, Ctrl/⌘,
  select mark, Shift range, Ctrl/⌘+A to `data-cap`, Escape, empty-space click, cap message from the
  template text); roving `tabindex`, arrow keys, Space; panel fill from the cell `data-*` by
  cloning the context template (R-04), no request; save on `change` for title/caption (A1) and
  gallery title/description (A5) with saving/Saved/refusal under the field, the typed text kept
  and the cell's `data-title` unchanged on refusal. Depends: T036.
- [X] T038 [US1] [US3] `workspace.js` part 3: checklist (tick → A7, untick → A8, indeterminate
  for partial, narrowing by the JS mirror of `GalleryChecklist.Match`, Enter/offer → A3 with ids or
  A7, focus back to the field); drag (cells → gallery row A7; cells within a gallery A9; gallery
  rows A4) with `ws-over`/`ws-before`/`ws-drop`; `Move earlier`/`Move later` (A9 bodies) and
  `Move up`/`Move down` (A4 bodies) (R-07); cover (A10); Public switch (A6, refusal puts it back);
  `DangerConfirm` then A2/A11 (A11 navigates to All photos); New gallery naming (A3 no ids, then
  navigate; Escape/blur cancels). Depends: T037.
- [X] T039 [US1] [US3] Write `tests/Gallery.Web.Tests/WorkspaceBrowserTests.cs`
  (`[Collection("library-browser")]`, `LibraryBrowserFixture`, 1440 × 900, `page.Mouse.MoveAsync(0,0)`
  before each `GotoAsync`) with the TI-70 LibraryBrowserTests mapping facts and: US1 independent
  test (12 photos, 3 galleries: retitle 3, add 4 to an existing and a typed new gallery, drag 2,
  0 document loads counted by `performance.getEntriesByType('navigation').length` staying 1, then
  reload and check stored) (SC-001, SC-003); SC-004: click → filled panel ≤ 100 ms by
  `performance.now()` around the click, median of 5, no request in between; shift range; cap at
  `Library:PageSize` override 4; save on leave + refusal (blank title keeps typed text);
  partly-ticked state; `Typing_a_new_name_…_keeps_focus`; `Typing_an_existing_name_…_without_a_duplicate`;
  `Checklist_rows_equal_GalleryChecklist_Match_for_each_query` (parity); US3 independent test
  (create by name, add, reorder by drag AND by Move later, cover, Public on, Move up; then the
  VISITOR view: anonymous context loads `/g` and the gallery page and asserts that order, cover
  and position) (visitor-visible US3 criteria); empty-gallery Public refusal text beside the
  switch; delete gallery lands on All photos with photos kept; untick the open gallery removes the
  cell (US3-9); last photo of a public gallery removed → toast says visitors can no longer see it
  and anonymous `/g/{gid}/{slug}` answers 404; back/forward/reload keep the collection;
  `Swapped_tile_is_styled` (R-01: computed `object-fit`/size of an `img` inside a swapped-in cell
  equals the full-load value; if unstyled, escalate, do not move styles into script);
  `Only_the_select_mark_is_drawn_on_a_photo`; `One_primary_button_per_context_…`;
  `Public_pages_load_no_admin_assets_even_for_the_owner`. Depends: T036–T038. Accept: green.

**CP-C2 (PARENT)**: no TI rows. Gate `C2`; `grep -c 'DOMParser' src/Gallery.Web/wwwroot/admin-assets/workspace.js` = 1;
hand-walk quickstart Live walk 1–7 locally at 1440; commit.

---

## Phase 5: US2 — upload into the workspace; the upload page goes (R-15 step 5)

**Goal**: per-photo joining with an optional destination, from a tray in every collection.
**Independent test**: spec US2 independent test.

### Batch D1 — server pipeline

Batch D1 owns: `src/Gallery.Web/Data/BatchDocuments.cs`, `src/Gallery.Web/Upload/BatchStore.cs`,
`src/Gallery.Web/Upload/EffectiveState.cs` (read-only check), `src/Gallery.Web/Upload/ConfirmService.cs`,
`src/Gallery.Web/Upload/UploadEndpoints.cs`, deletes `src/Gallery.Web/Upload/UploadPageModel.cs`
and `src/Gallery.Web/Admin/Pages/UploadPage.razor`, new `tests/Gallery.Web.Tests/UploadJoinTests.cs`.

- [X] T040 [US2] data-model.md "Upload set": `BatchDoc` gains `[BsonElement("destination")]
  [BsonIgnoreIfNull] string? Destination` and `[BsonElement("added")] List<string> Added = []`;
  `ItemStates.Joining = "joining"`; `ConfirmStartedAt`/`ConfirmedAt`/`DiscardedAt`/`ConfirmedCount`
  stay as properties (old documents) but nothing writes them. Accept: compiles.
- [X] T041 [SAFETY] [US2] `BatchStore` (`src/Gallery.Web/Upload/BatchStore.cs`): add
  `TryClaimJoinAsync(batchId, itemId, attemptId)` — ONE atomic `UpdateOne` whose filter requires
  the open batch, the item id, the attempt and `state ∈ {succeeded, joining}` and sets `joining`
  [SAFETY claim, II.5d]; `CompleteJoinAsync(batchId, itemId, imageId)` (pull the item, `$push`
  `added` only if absent); `SetDestinationIfEmptyAsync(batchId, gid?)` (sets/unsets `destination`
  and clears `added` only when `items` and `added` are empty); `UnsetDestinationAsync(batchId)`;
  `ClearAsync(batchId)` (removes failed/interrupted items, clears `added` and `destination`).
  Remove `TryStartConfirmAsync`, `ClearConfirmStartedAsync`, `MarkConfirmedAsync`,
  `MarkDiscardedAsync` (TI-32). `AppendAsync` keeps its `ConfirmStartedAt` check unchanged. All
  timed `db.batch.update`. Depends: T040.
- [X] T042 [SAFETY] [US2] `ConfirmService.JoinAsync` (C-02, contracts/http.md U12 steps 3–5) in
  `src/Gallery.Web/Upload/ConfirmService.cs`: the per-item body of `ConfirmItemAsync` moved, not
  copied; copy-if-match on the validated ETags for the master and every size [SAFETY III.6]; a
  refused copy → item `failed` "changed after validation", the public names this run created and
  the master copy removed, NO image, log `upload.join-refused`, result `ChangedAfterValidation`;
  a missing staged file accepted only when its destination already exists; image upsert
  `$setOnInsert`; staged prefix deleted; `CompleteJoinAsync`; then, ONLY when the batch has a
  `destination`, `GalleryService.AddAsync(gid, [imageId])` (gallery gone → `UnsetDestinationAsync`,
  `galleryGone: true`) [SAFETY II.5b: no destination → no GalleryService call and no
  `IEdgeCache.Enqueue`]; never any visibility write and no `PublishAsync`/`UnpublishAsync` call
  [SAFETY II.5c]; the whole call timed `upload.join`; success log line `upload.joined …`. Keep the
  existing `_afterItem` crash seam on the join path. Delete `ConfirmAsync`, `RunAsync`,
  `ConfirmResult`, `ConfirmStatus`. The file must not contain the words `Purge` or `Warm`
  (`UploadSourceGuardTests`). Depends: T041.
- [X] T043 [SAFETY] [US2] `UploadEndpoints` (`src/Gallery.Web/Upload/UploadEndpoints.cs`,
  contracts/http.md Upload): U2 adds `destination` (`{ id, title, public }` or null), `added`,
  `joining` passes through as a state, `confirming` stays false (C-01), `{ batch: null }` also when
  the open batch has no items and no `added`; U3 reads optional `gallery`, `visibleToVisitors`:
  step 2 via `SetDestinationIfEmptyAsync` (unknown gallery → 404 `gallery-gone`, nothing
  registered), **step 3 [SAFETY II.5a]**: effective destination public and `visibleToVisitors !=
  true` → 409 `destination-public` with `Messages`' sentence, nothing registered, log
  `upload.destination-refused`; answer gains `destination`; a request with neither field behaves
  byte-for-byte as today. U8/U9 on a `joining` item → 409 `not-active` [SAFETY claim]; remove the
  `ConfirmInProgress` branches. New U12 `POST /upload/items/{itemId}/join` (`SmallBodyLimit`,
  404/409 `stale-attempt`/409 `not-active` as the other item routes, `TryClaimJoinAsync`, then
  `ConfirmService.JoinAsync`, answer per http.md); new U13 `POST /upload/clear` (`SmallBodyLimit`,
  effective states per http.md → 409 `still-working`, else delete each remaining item's staging
  prefix, `ClearAsync`, answer + `upload.cleared`). Remove U1 `Page`, U10 `Confirm`, U11 `Discard`
  and their mappings; delete `UploadPageModel.cs` and `Admin/Pages/UploadPage.razor`. Confirm
  `EffectiveState.Of` leaves `joining` as stored (it only ages queued/processing/uploading);
  if not, escalate. Depends: T042.
- [X] T044 [SAFETY] [US2] Write `tests/Gallery.Web.Tests/UploadJoinTests.cs` (`UploadHost`; the
  13-row mapping of contracts/test-impact.md "UploadConfirmTests → UploadJoinTests", same seams,
  same stored-field and object assertions per item) plus:
  `Register_into_a_public_gallery_without_the_flag_is_409_destination_public` [SAFETY II.5a]
  (nothing registered, no staging key, log line);
  `Register_into_a_public_gallery_with_the_flag_names_the_destination`;
  `Destination_is_set_only_on_an_empty_set_and_the_stored_one_wins`;
  `Join_with_no_destination_calls_no_gallery_method_and_enqueues_no_edge_work` [SAFETY II.5b]
  (galleries collection dump and `Recorder.AllPurged()` unchanged, no `owner.action gallery.` line);
  `Public_pages_byte_identical_around_a_join_with_no_destination` [SAFETY II.5b];
  `Join_never_changes_visibility` [SAFETY II.5c] (private stays private, an EMPTY private
  destination stays private and gains the photo, a public stays public);
  `Join_of_an_item_that_is_not_validated_is_409_not_active` [SAFETY II.5d] (queued, processing,
  uploading, failed, interrupted);
  `Delete_and_restart_of_a_joining_item_are_409_not_active` [SAFETY claim];
  `Fifty_concurrent_claims_of_one_item_let_exactly_one_join` [SAFETY claim] (exactly one image,
  one `upload.joined`);
  `Reput_after_validation_publishes_nothing` through U12 [SAFETY III.6];
  `Delete_works_on_the_item_a_refused_join_copy_left_failed`,
  `Restart_of_the_item_a_refused_join_copy_left_failed_asks_for_the_file` (TI-21);
  `Gallery_deleted_mid_upload_joins_the_library_only`; `Added_survives_a_reload_in_U2`;
  `Clear_refuses_while_working_and_drops_failed_and_interrupted` (staging prefixes gone, `added`
  and `destination` cleared, message "{n} were not added.");
  `Overlapping_joins_of_one_item_converge` (second call 404, one image);
  `Joining_is_never_reported_interrupted` (clock +10 min). Depends: T043. Accept: green.

### Batch D2 — the tray

Batch D2 owns: `src/Gallery.Web/wwwroot/admin-assets/upload.js`, `workspace.js` (tray hooks
only), `src/Gallery.Web/Admin/Pages/WorkspacePage.razor(.css)` (Add photos, tray mount, tray
templates), new `tests/Gallery.Web.Tests/WorkspaceUploadBrowserTests.cs`,
`tests/Gallery.Web.Tests/WorkspaceLayoutBrowserTests.cs`.

- [X] T045 [SAFETY] [US2] `WorkspacePage`: `Add photos` (`Button` Primary) in the heading actions;
  the `<upload-tray>` mount at the top of `.ws-main`; the `UploadTray`/`UploadRow` `<template>`s in
  the `chrome` templates; `data-upload-src`/`data-worker-src` from `AdminAssets`
  (`ScriptUrl`, `WorkerUrl`); no new attribute for the destination: the tray reads the open gallery's title and
  `data-public` from its `CollectionList` row, so it can show the [SAFETY II.5a] notice before any
  file is picked. Accept: T035 facts still green; T047.
- [X] T046 [SAFETY] [US2] `upload.js` rework (C13): register `<upload-tray>`; keep register, PUT,
  master check, worker, size PUTs (≤ 4), heartbeats, U7, U6, U8, U9, reconcile from U2, at-pick
  refusals; U3 sends `gallery` (the open collection when it is a gallery, from the tray's stored
  destination after the first register) and `visibleToVisitors: true` ONLY while the tray shows
  the public notice [SAFETY II.5a]; after U7 succeeds and for every `succeeded`/`joining` item on
  reconcile → U12; on success dispatch a `CustomEvent` to `workspace.js`, which fetches W4 and
  places the cell (first in All photos; first in Not in a gallery when no destination; last in the
  destination gallery when open) and refreshes the chrome at most once a second; Hide/Show; `Done`
  (unavailable while working) / `Close` → U13, then remove the tray and the New marks; drop
  anywhere on the window opens the tray; the tray element is moved across `swap`, never
  re-created; remove footer/confirm/discard code and templates. Four sinks banned. Loaded by
  dynamic `import()` from `workspace.js` on Add photos, a drop, or a U2 batch. Depends: T044, T045.
- [X] T047 [SAFETY] [US2] Write `tests/Gallery.Web.Tests/WorkspaceUploadBrowserTests.cs`
  (`UploadBrowserFixture`, `OpenWorkspaceAsync`) with the TI-70 UploadBrowserTests mapping and:
  US2 independent test (gallery: four photos, Hide, Show, Done; all four in the gallery and in All
  photos; repeat from All photos → in no gallery); `Public_destination_notice_is_visible_before_picking_and_while_working`
  [SAFETY II.5a] (the `ws-notice` sentence is visible — bounding box > 0, not inside `details` —
  before `setInputFiles`, while Working and while Hidden); `Each_photo_appears_as_it_finishes_marked_New`;
  `Upload_carries_on_across_a_collection_switch`; `Reload_mid_upload_reopens_the_tray_with_interrupted_rows`
  (TI-33); `Done_with_failed_photos_drops_them_and_says_how_many`;
  `One_of_ten_forced_to_fail_the_other_nine_join_within_2_s` (SC-013 shape: for each joined item,
  U7 response end → U12 response end ≤ 2000 ms from `performance.getEntriesByType('resource')`);
  `Visitors_see_a_photo_joined_into_a_public_gallery` (anonymous context loads the public gallery
  page after a join and finds the photo; US2-6 visitor-visible); `Done_clears_the_New_marks`.
  Depends: T046.
- [X] T048 [US2] Write `tests/Gallery.Web.Tests/WorkspaceLayoutBrowserTests.cs` (IX.4, FR-038)
  at 1440 × 900: frame geometry (`.ws-list`, `.ws-main`, `.ws-panel` boxes; the gap above
  `.ws-main`'s first box) equal across library collection, gallery, one photo, several photos,
  upload area open; expected values measured in the browser, compared for equality within 0.5 px;
  rows for "photo large" (Phase 7) and 390 px (Phase 8) are added later. Depends: T046.

**CP-D (PARENT)**: apply TI-20, TI-21, TI-30, TI-31, TI-32, TI-33, TI-34 (upload part), TI-62
(`UploadPageTests`), TI-63 (`UploadTimingEvidenceTests`), TI-70 (`UploadBrowserTests`,
`LayoutConsistencyBrowserTests` admin fact); retire `AdminNavConsistencyTests` (TI-62) because it
loads `/admin/upload`. Gate `D`; `git diff <T001 commit> -- tests/Gallery.Web.Tests/UploadFlowTests.cs
tests/Gallery.Web.Tests/UploadIntakeTests.cs tests/Gallery.Web.Tests/UploadValidatorTests.cs
tests/Gallery.Web.Tests/UploadStorageTests.cs` is empty (SC-012); grep the four [SAFETY]
mechanisms (`destination-public` branch, `TryClaimJoinAsync` filter, `JoinAsync` destination
guard, `not-active` on `joining`); hand-walk Live walk 8, 10, 11; commit.

---

## Phase 6: US4 — one system, the old one gone (R-15 step 6)

**Goal**: remove every old page, route, script and component; guards that fail if any returns.
**Independent test**: spec US4 independent test.

Batch E owns every file it deletes, `src/Gallery.Web/Components/Templates/AdminLayout.razor(.css)`,
`src/Gallery.Web/Admin/Pages/SignInPage.razor`, `src/Gallery.Web/Components/ComponentTypes.cs`,
`src/Gallery.Web/Upload/AdminAssets.cs`, `src/Gallery.Web/Library/LibraryReader.cs`,
`src/Gallery.Web/Ui/UiCatalog.cs`, `src/Gallery.Web/Ui/Pages/{AdminLayoutPage,ContactSheetPage}.razor`,
`src/Gallery.Web/Workspace/Messages.cs` (no change expected), new
`tests/Gallery.Web.Tests/WorkspaceRemovalGuardTests.cs`.

- [X] T049 [US4] Delete the old pages and routes: `src/Gallery.Web/Admin/Pages/{LibraryPage,PhotoEditPage,GalleriesPage,GalleryPage}.razor`
  with their `.razor.css`; `src/Gallery.Web/Library/LibraryEndpoints.cs` (L1–L6, G1–G4, B1–B6);
  `src/Gallery.Web/wwwroot/admin-assets/library.js`, `reorder.js`; `AdminAssets.LibraryScriptPath`,
  `ReorderScriptPath` and their `_versions` entries. `LibraryReader` per C-16 (delete `PageAsync`,
  `PhotoAsync`, `Tile`, `StatusLine`, `Plural`, `LibraryTile`, `LibraryPageData`). Do NOT touch
  `Site/Pages/GalleryPage.razor` (the public page). Report the wiring line for T051. Depends: CP-D.
- [X] T050 [US4] Delete the 13 components and their catalogue pages: `Components/Organisms/{OwnerBar,BatchFooter,GalleryBuilder}.razor(.css)`,
  `Components/Molecules/{SelectionBar,PhotoActions,GalleryActions,BatchItem,DragHandle,CoverMarker,TokenCombobox,VisibilityToggle}.razor(.css)`
  plus `PhotoActions.cs`, `GalleryActions.cs`, `TokenCombobox.cs`, `Components/Atoms/{Icon,IconButton}.razor(.css)`,
  `Components/Atoms/Icons.cs`; `Ui/Pages/{OwnerBar,BatchFooter,GalleryBuilder,SelectionBar,PhotoActions,GalleryActions,BatchItem,DragHandle,CoverMarker,TokenCombobox,VisibilityToggle,Icon,IconButton}Page.razor`
  (+ `.razor.css` where present); their 13 `UiCatalog.Entries` lines; enums `BatchItemState`,
  `BatchState`, `BuilderState`, `CoverState`, `HandleState`, `AdminPage` from `ComponentTypes.cs`;
  `ContactSheetPage` specimens 4–6 (C-04), keeping the three public ones and the fill specimen;
  `MembershipEdit`/services untouched. Depends: T049.
- [X] T051 [US4] PARENT Wiring: remove `admin.MapLibrary();` from `AdminEndpoints.MapAdmin` and
  `MapLibrary` (only the endpoint mapping; `AddLibrary` stays) from `LibrarySetup.cs`; ensure
  `MapWorkspace`, `MapWorkspaceApi`, `MapUpload` precede the catch-all. Accept: build green;
  `grep -rn 'MapLibraryEndpoints\|LibraryEndpoints' src` empty.
- [X] T052 [US4] AdminLayout final (K1, C-05): parameters `Title`, `Bare`, `List`, `ChildContent`,
  `Panel`, `Csrf`, `Cap`, `UploadSrc`, `WorkerSrc`; remove `Current`, `Crumbs`, `Aside`,
  `ShowOwnerBar`, `SignOut` and the owner-bar/breadcrumb render; `Bare=true` renders today's
  bar-less document byte-identical to `evidence/pre-admin-layout-bare.html` for the same inputs;
  `SignInPage.razor` passes `Bare="true"`; delete CSS only the old frame used; rewrite
  `Ui/Pages/AdminLayoutPage.razor` to the three regions with sample content and a current row
  (C-09). Depends: T050.
- [X] T053 [SAFETY] [US4] Write `tests/Gallery.Web.Tests/WorkspaceRemovalGuardTests.cs` (SC-009,
  FR-004, FR-039): every removed address of contracts/http.md "Removed routes" (with real ids),
  GET and POST with a valid token, answers EXACTLY what `GET/POST /admin/never-existed-{n}`
  answers (status, `no-store`, `noindex`, body bytes) [SAFETY]; a source scan of `src/` (`*.cs`,
  `*.razor`, `*.js`, `*.css`, excluding `bin`/`obj`/`vendor`) finds none of the removed type
  names, file names, `library.js`, `reorder.js`, or route literals `"/library`, `"/galleries`,
  `"/upload/confirm"`, `"/upload/discard"` (excluding this guard's own list) and the removed
  files do not exist; `DOMParser` occurs exactly once in `workspace.js`, inside `function swap`
  [SAFETY]; none of the four sinks in any `wwwroot/admin-assets/*.js` outside `vendor/` [SAFETY];
  visibility write scan: in `src/Gallery.Web/{Library,Maintenance,Workspace,Upload}` the literal
  `"public"` is written only inside `GalleryService.PublishAsync` (same regexes as
  `LibrarySourceGuardTests`, called via a shared helper if one exists, else the same rule
  restated) [SAFETY II.5c]; `AdminLayout_Bare_equals_the_pre_change_sign_in_layout` (reads
  `evidence/pre-admin-layout-bare.html`, FR-001) — add this one to `WorkspaceComponentTests`.
  Each guard collects every violation before one assert. Depends: T049–T052.

**CP-E (PARENT)**: apply every TI row marked CP-E (TI-10…TI-19, TI-34 library part, TI-35, TI-36,
TI-38, TI-42, TI-50 (48 → 35), TI-51, TI-53…TI-56, TI-59…TI-63, TI-70) with
`test-impact-edit.py --cp CP-E`, delete the retired files, then snapshots (TI-52 CP-E): delete the
13 baselines; regenerate `index.html`, `templates/admin-layout.html`,
`organisms/contact-sheet.html`; trim `specs/005-admin-ui-components/evidence/baseline/ContactSheetPage.html`
(TI-57); diff each against `evidence/pre-ui-baseline/` / `evidence/pre-005-ContactSheetPage.html`
— only the stated removals/additions. Gate `E`; run `NV_DRYRUN=1 non-vacuity.sh`; commit.
- [X] T054 [US4] PARENT Constitution PATCH 6.3.1: IX.1's example "owner bar" → "collections list"
  (constitution.md:296), Sync Impact Report rewritten, the 6.3.0 deferred TODO (line 23) removed,
  version and date bumped. Accept: `grep -c 'owner bar' .specify/memory/constitution.md` = 0.

---

## Phase 7: US5 — large view (severable seam, P2)

Severable: cut → photos are judged from grid thumbnails and the panel preview; `PhotoStage`
(component, catalogue page, baseline) is then removed as unused (FR-039).

Batch F owns: `workspace.js` (large view only), `WorkspacePage.razor` (the hidden stage),
`WorkspaceBrowserTests.cs` (append), `WorkspaceLayoutBrowserTests.cs` (append one context).

- [X] T055 [US5] `WorkspacePage` renders a hidden `PhotoStage` in main and `Open large` in the
  photo template; `workspace.js` large view (R-08, FR-036): double-click, Enter on a focused cell,
  or `Open large`; clone the cell's `<picture>` into `.ws-stage-frame` and set `sizes` to the
  stage width; "{n} of {total}"; Previous/Next and ←/→; unavailable at the ends; `?photo={id}` by
  `pushState`; Escape/back returns to the grid with the cell selected and scrolled into view; a
  direct load with `?photo=` loads chunks until found (R-08). Depends: CP-E.
- [X] T056 [US5] Append to `WorkspaceBrowserTests`: US5 independent test (third photo large, →→←
  with an edit on the way, back → the last viewed is selected with the new title);
  `Large_view_shows_the_whole_photo_as_large_as_main_allows` (frame inside main, aspect equal to
  stored within 1%); `Large_view_reload_reopens_the_same_photo`; ends unavailable. Append the
  "photo large" context to `WorkspaceLayoutBrowserTests` at 1440. Depends: T055.

**CP-F (PARENT)**: gate `F`; commit.

---

## Phase 8: US6 — phone (severable seam, P2)

Severable: cut → the admin is desktop-only until a later feature (spec Assumptions); the 390 px
rows of FR-038/SC-007/SC-008 are then recorded as not delivered.

Batch G owns: `AdminLayout.razor.css` (below-1200 px rules only, C-21), `WorkspacePage.razor(.css)`
(`Gallery settings`, panel `Done`), `workspace.js` (sheet only), new
`tests/Gallery.Web.Tests/WorkspacePhoneBrowserTests.cs`, `WorkspaceLayoutBrowserTests.cs` (append).

- [X] T057 [US6] Below 1200 px (C-21) (K1, K12, C12): one column, the collections list a horizontal strip
  at the top (scrolls inside itself, never the page), the panel a bottom sheet shown while
  `admin-workspace` has `ws-sheet`; `Gallery settings` (gallery only, shown below 1200 px) and a
  `Done` in each panel heading row below 1200 px; `workspace.js` sets `ws-sheet` while a selection
  exists or `Gallery settings` is on, removes it on `Done`. Every control ≥ 44 × 44 px (the
  existing `--tap` token, `Tokens.Tap`; no new token). Frame CSS only in `AdminLayout.razor.css`. Depends: CP-F (or CP-E
  if Phase 7 is cut).
- [X] T058 [US6] Write `tests/Gallery.Web.Tests/WorkspacePhoneBrowserTests.cs` at 390 × 844 with
  `HasTouch` and taps only: SC-008 each P1 independent test (US1 retitle + add two to a gallery,
  US2 upload one photo into a gallery, US3 reorder with Move later + Public on) with
  `scrollWidth ≤ innerWidth` and every visible control's box ≥ 44 × 44; the sheet opens on a
  select-mark tap and closes with `Done`; gallery settings one tap away. Append 390 × 844 rows of
  every context to `WorkspaceLayoutBrowserTests` (SC-007). Depends: T057.

**CP-G (PARENT)**: gate `G`; hand-walk Live walk 15; commit.

---

## Phase 9: Polish, evidence, records, release

- [X] T059 [P] Write `tests/Gallery.Web.Tests/WorkspaceTimingEvidenceTests.cs`
  (`Category=Evidence`, `LibraryHost`, 100 photos, 3 galleries, one holding 30): 100 GETs each of
  W1 full, W1 `main`, W3 full, W3 `main`, W4; 30 POSTs each of A1, A5, A7 (one id), A8 (one id,
  re-added between), A9, A10; then an `UploadHost` part: ten registered photos staged with
  `UploadFixtures`, one forced to fail validation, nine U7 + U12; print the failed item id as a
  line `evidence.forced-failure {item}`; write the log to `WORKSPACE_TIMING_LOG` (default
  `specs/020-admin-workspace/evidence/workspace-timings.log`). Accept: the line counts p95.sh needs.
- [X] T060 PARENT SC-005, SC-006, SC-013 "same as before": run T059 alone, then
  `bash evidence/p95.sh evidence/pre-timings evidence/workspace-timings.log | tee evidence/p95-output.txt`.
  A miss is recorded, never tuned: decisions row with measured values, cause from the op.timing
  breakdown, and for W3 the R-02 fallback as the recommended fix. Depends: T059.
- [X] T061 PARENT SC-011 "same as before": `bash evidence/compare-public.sh | tee evidence/public-compare.txt`
  → `EQUAL`; plus a browser check (anonymous, 390 and 1440) of `/`, `/g`, one gallery, one photo
  page: no request to `/admin-assets/`, no element with a `ws-` class, 0 broken images
  (`evidence/browser-public.txt`). Depends: CP-G.
- [X] T062 PARENT SC-012 "same as before": `git diff --exit-code <T001 commit> -- tests/Gallery.Web.Tests/UploadFlowTests.cs
  tests/Gallery.Web.Tests/UploadIntakeTests.cs tests/Gallery.Web.Tests/UploadValidatorTests.cs
  tests/Gallery.Web.Tests/UploadStorageTests.cs tests/Gallery.Web.Tests/UploadSeedCoexistenceTests.cs`
  and the GPS/width/missing-size facts green in the final gate; FR-001 "sign-in unchanged":
  `WorkspaceComponentTests.AdminLayout_Bare_equals_the_pre_change_sign_in_layout` green. Save both
  outputs to `evidence/unchanged-checks.txt`.
- [X] T063 [P] PARENT VII.2 (research R-14): write `specs/020-admin-workspace/evidence/design-record-edit.py`
  (each old string's count asserted before any write; abort on mismatch) editing
  `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md` (D-23 "superseded by D-27";
  new D-27 "Admin workspace" with every field the validator wants, alternatives = D-23's direction
  and the rejected first preview; D-21 component list), `02b-decisions-media.md` (D-11 per-photo
  joining, destination, no confirm/discard), `05-lifecycle.md` (L-01…L-06), `06-edge-cases.md`
  (E-08), `03-diagrams.md` ("Owner upload to library"), `07-traceability.md` (II.5 row: mechanism
  destination + notice + join; tests `UploadJoinTests`, `WorkspaceUploadBrowserTests`). Run
  `uv run --no-project python …/design-record-edit.py`, `bash specs/001-architecture-stack-design/evidence/assemble.sh`,
  `bash specs/001-architecture-stack-design/evidence/validate.sh | tee evidence/design-validate.txt`
  (0 failures); grep the assembled `docs/design.md` for "owner bar", "Batch confirmed", "confirm"
  and review each hit; check `docs/architecture.html` for admin page names (none today) and edit
  if any. Accept: D-27 present; D-23 reads "superseded by D-27"; `git show --stat` of the commit
  includes `docs/design.md`.
- [X] T064 [P] PARENT FR-047: annotate superseded requirements in place ("SUPERSEDED by 020
  (2026-11-…): …", original text kept) in `specs/008-upload-pipeline/spec.md` (FR-013, FR-030,
  FR-031, FR-032, FR-033, FR-034, Clarification 1), and the page-level requirements of
  `specs/009-library-galleries/spec.md`, `specs/010-navigation-improvements/spec.md` (admin bar,
  breadcrumb), `specs/014-gallery-combobox/spec.md`, `specs/015-tile-icon-controls/spec.md`,
  `specs/016-galleries-list-icons/spec.md`, `specs/017-photo-edit-screen/spec.md`,
  `specs/019-selection-bar-layering/spec.md`. Accept: `grep -c 'SUPERSEDED by 020'` ≥ 1 in each
  of the eight files, 7 in 008.
- [X] T065 [P] PARENT Fold every executor `decisions_made` into `decisions.md` (one row each, kind
  DEFAULT/PLAN/IMPLEMENT/STATED-CHANGE; one STATED-CHANGE row per TI row with its checkpoint), and
  write the 020 entry in `docs/roadmap.md` (status, evidence paths, owner-only items, which
  severable phases shipped).
- [X] T066 PARENT Re-run the test-impact grep on the finished tree: `bash evidence/test-impact-hits.sh`
  hits only `WorkspaceRemovalGuardTests`, `AdminAccessTests` I1/I5 rows and TI-34's 404 fact;
  `grep -rnE 'library\.js|reorder\.js|ShellPage|UploadPageModel' src tests --include=*.cs --include=*.razor --include=*.js`
  hits only the removal guard. Save to `evidence/t066-grep.txt`.
- [X] T067 PARENT Live-server check (quickstart Live walk step 9; invisible to in-process tests):
  run the app locally with the development sign-in, then with the browser over CDP: register 100
  photos in one U3 (body near `RegisterBodyLimit`), A9 reorder of a 300-photo gallery (~9 KB, under
  64 KiB), A2/A7/A8 with 100 ids (under 16 KiB); no 413; plus one deliberate 64 KiB + 1 A9 → 413.
  Save statuses and body sizes to `evidence/live-limits.txt`. Never bind :5080/:5081 if the owner's
  watcher holds them; use another port.
- [X] T068 [SAFETY] PARENT Non-vacuity: write these `brk` lines into `evidence/non-vacuity.sh`
  (T008), dry-run, then run it (`| tee evidence/non-vacuity.txt`). One deliberate break per
  [SAFETY] guard; each named test must go red ON ITS ASSERTION; restore + `touch`. Depends: CP-G.
  1. T043 II.5a: delete the `destination-public` branch in `UploadEndpoints` Register →
     `UploadJoinTests.Register_into_a_public_gallery_without_the_flag_is_409_destination_public` red.
  2. T025 II.5a notice: render the `ws-notice` paragraph only when `State == TrayState.Empty` →
     `WorkspaceComponentTests.UploadTray_public_notice_is_plain_text_in_every_state` and
     `WorkspaceUploadBrowserTests.Public_destination_notice_is_visible_before_picking_and_while_working` red.
  3. T046 II.5a flag: `upload.js` always sends `visibleToVisitors: true` and the tray never shows the
     notice for a public destination (drop the `ws-notice` clone) → the same browser fact red.
  4. T042 II.5b: on the no-destination path call `GalleryService.AddAsync` with the first id of
     `site.homeOrder` → `UploadJoinTests.Join_with_no_destination_calls_no_gallery_method_and_enqueues_no_edge_work`
     and `Public_pages_byte_identical_around_a_join_with_no_destination` red.
  5. T042 II.5b edge: after the join enqueue `new EdgeWork("upload.join", [], ["/g"], [], now)` →
     `Join_with_no_destination_…_enqueues_no_edge_work` red.
  6. T042 II.5c: after `AddAsync` call `PublishAsync(gid, confirmed: true)` →
     `UploadJoinTests.Join_never_changes_visibility` red.
  7. T053 II.5c scan: add `.Set(g => g.Visibility, "public")` to a method in
     `Upload/ConfirmService.cs` → `WorkspaceRemovalGuardTests` visibility-scan fact red.
  8. T041 II.5d: widen the claim filter to accept `uploading` →
     `UploadJoinTests.Join_of_an_item_that_is_not_validated_is_409_not_active` red.
  9. T043 claim: let U9 delete accept `joining` →
     `UploadJoinTests.Delete_and_restart_of_a_joining_item_are_409_not_active` red.
  10. T041 claim atomicity: replace the conditional `UpdateOne` with a read then an unconditional
      write → `Fifty_concurrent_claims_of_one_item_let_exactly_one_join` red (run 15× if flaky;
      never retry into green).
  11. T042 III.6: drop the ETag precondition from the join's copy → `UploadJoinTests.Reput_after_validation_publishes_nothing` red.
  12. T014 II.6: in A6 answer 200 for `public: false` on an empty private gallery →
      `WorkspaceApiTests` A6 empty-private fact red.
  13. T014 I.3: add `.AllowAnonymous()` to the A1 mapping → `WorkspaceAccessTests` no-session A1 row red.
  14. T014 I.6: map A7 on a group without `AdminAntiforgeryFilter` (or `.DisableAntiforgery()`-style
      bypass) → `WorkspaceAccessTests` no-token A7 row red.
  15. T033 I.3: `.AllowAnonymous()` on W4 → `WorkspaceAccessTests` W4 row red.
  16. T043 I.3/I.6: `.AllowAnonymous()` on U12 → `UploadAccessTests` U12 row red.
  17. T053 removed routes: map `GET /admin/library` to `Results.Ok()` →
      `WorkspaceRemovalGuardTests` unknown-address fact red.
  18. T036/T053 parser: add a second `new DOMParser()` outside `swap` in `workspace.js` →
      `WorkspaceRemovalGuardTests` DOMParser fact red.
  19. T053 sinks: add `el.innerHTML = ''` to `upload.js` → `WorkspaceRemovalGuardTests` sink fact
      and `UploadSourceGuardTests.Upload_script_has_no_html_injection_sink` red.
  20. TI-36 public isolation: add `<span class="ws-mark">x</span>` to `PublicLayout.razor` →
      `LibrarySourceGuardTests.Public_pages_carry_no_owner_markup_…` red and `compare-public.sh`
      exit 1.
  21. T043 joining never ages: make `EffectiveState.Of` treat `joining` as active →
      `UploadJoinTests.Joining_is_never_reported_interrupted` red.
  Accept: `evidence/non-vacuity.txt` shows 21 PASS rows, none "on an EXCEPTION".

- [X] T069 PARENT Final gate `evidence/gate.sh final` after `dotnet clean`: `-warnaserror` build,
  full suite incl. `Category=Evidence` and Browser; `p95-output.txt` recorded; earlier features'
  logs restored; `tee evidence/gate-final.txt`; commit.
- [X] T070 OWNER Live walk-through on the local stack with the development sign-in, quickstart
  "Live walk" steps 1–15 at 1440 × 900 and 390 × 844; the owner reports done or lists defects
  (each defect becomes a task before T071).
- [X] T070a OWNER walk-through, first pass (2026-11-19): three defects reported, tasks T074 to T076.
- [X] T074 [US6] (owner, T070) Below 1200 px the collections list is an expandable menu, not a
  sideways-scrolling strip: one `Button` at the top of the workspace showing the current collection's
  name (with an `aria-hidden` "▾"), `aria-expanded`/`aria-controls`; open, it shows, full width and in
  this order, All photos, Not in a gallery, the Galleries heading and each gallery (Private mark,
  count), New gallery, then View site ↗ and Sign out at the bottom. Choosing a collection navigates and
  closes it; Escape, the button again, or a tap outside closes it. Every row ≥ 44 × 44. From 1200 px
  the list is unchanged. Frame CSS stays in `AdminLayout.razor.css`.
- [X] T075 [US6] (owner, T070) Below 1200 px a tap on a photo's select mark toggles that photo in the
  selection and does NOT open the sheet; several photos can be selected this way. While a selection
  exists and the sheet is closed, a bar fixed at the bottom shows "{n} selected" and one Primary
  `Button` "Add to gallery", which opens the sheet with the selection panel (or the photo panel for
  one photo); the bar is hidden while the sheet is open. A tap on the photo itself (not the mark)
  keeps today's behaviour (select only it, open the sheet). From 1200 px nothing changes.
- [X] T076 [US1] (owner, T070) From 1200 px the three regions start their content at the same
  distance below the top of the window, with space above it: the list, main (so `Add photos` and the
  heading row) and the panel get the same top padding (`--space-m`). The measured layout test asserts
  the first box of each of the three regions has the same top, equal to that padding, in every context.
- [ ] T071 PARENT Merge to `main` (`--no-ff`, body listing the stories, the TI rows, the
  severable phases shipped), push to `origin`, watch the deploy job green. Signed out, `curl -sI`
  `https://port.zacal.me/admin`, `/admin/library`, `/admin/galleries`, `/admin/upload`: each 302
  to `/admin/signin` (unknown addresses inside the owner group answer like every admin address);
  anonymous browser pass of the public pages at 390 and 1440 (no `/admin-assets/` request, 0
  broken images, no sideways scroll) → `evidence/browser-prod.txt`.
- [ ] T072 OWNER Sign in to the automation browser on port.zacal.me (the production check of
  owner pages needs the owner session; speckit-sessions Tasks phase).
- [ ] T073 PARENT Production check (after T072): open `/admin` at 390 and 1440: no sideways scroll,
  0 broken images, collections list and photos render, `/admin/library` answers like
  `/admin/never-existed`; open one photo's panel and one gallery's settings; NO writes on
  production. Append to `evidence/browser-prod.txt`.

---

## Dependencies and execution order

- Setup (PARENT): T001 → T002 → T003 → T004 → T005 (captures BEFORE any code) → T006 ‖ T007 ‖
  T008 ‖ T009 → T010 → CP-0.
- Phase 2 (Batch A): T011 ‖ T012 ‖ T013 → T014 → T015 (PARENT) → T016 ‖ T017 → CP-A.
- Phase 3 (Batch B): T018 ‖ T019 ‖ T020 ‖ T022 ‖ T023 ‖ T026 → T021, T024, T025 → T027 → T028 → CP-B.
- Phase 4 (Batch C1): T029 ‖ T030 → T031 → T032 → T033 → T034 (PARENT) → T035 → CP-C;
  (Batch C2): T036 → T037 → T038 → T039 → CP-C2.
- Phase 5 (Batch D1): T040 → T041 → T042 → T043 → T044; (Batch D2, after T044): T045 → T046 →
  T047 ‖ T048 → CP-D.
- Phase 6 (Batch E): T049 → T050 → T051 (PARENT) → T052 → T053 → CP-E → T054 (PARENT).
- Phase 7 (Batch F, severable): T055 → T056 → CP-F.
- Phase 8 (Batch G, severable): T057 → T058 → CP-G.
- Polish: T059 → T060; T061, T062, T066 after CP-G; T063 ‖ T064 ‖ T065 any time after CP-E (no
  source files; can run while Batches F/G work); T067; T068 (non-vacuity) → T069 → T070 (OWNER) →
  T071 → T072 (OWNER) → T073.

Note: T008's non-vacuity dry-run is repeated at every checkpoint, so a break whose code lands in a
later batch reports "matched nothing" until then.

## Parallel examples

```text
Phase 2: T011 (GalleryService) ‖ T012 (Messages) ‖ T013 (UploadEndpoints visibility) → T014 → T016 ‖ T017
Phase 3: T018 Sizes ‖ T019 SidePanel+ContactSheet ‖ T020 Mark+Toast ‖ T022 Checklist ‖ T023 UploadRow ‖ T026 PhotoStage
         then T021 PhotoCell, T024 CollectionList, T025 UploadTray (disjoint files), then T027, T028
Phase 4: T029 AdminLayout ‖ T030 LibraryReader visibility
Polish:  T063 design record ‖ T064 annotations ‖ T065 decisions/roadmap (alongside Batches F and G)
```

## Implementation strategy

- MVP = Phases 1–4 (CP-C2): the workspace organises photos and shapes galleries at `/admin`
  while the old upload page still uploads. Phase 5 makes upload part of the workspace; Phase 6
  completes the owner's "one system" bar (US4) and is required before merge.
- Severable seams (spec Assumptions): Phase 7 large view and Phase 8 phone layout. Cutting either
  is an owner decision recorded in decisions.md; FR-038's 390 px rows and SC-008 then go to the
  roadmap as not delivered, and an unused `PhotoStage` is removed.
- Every checkpoint leaves the suite green after its TI rows and the admin usable (R-15).
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
