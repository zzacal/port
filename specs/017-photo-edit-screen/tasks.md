# Tasks: Focused Photo Edit Screen

**Input**: design documents in `specs/017-photo-edit-screen/`:
- spec.md (FR-001–FR-014, SC-001–SC-006)
- plan.md
- research.md (R-01–R-11)
- data-model.md
- contracts/http.md (L6 new; L1, L2 changed)
- contracts/components.md (P1–P4, J1)
- contracts/test-impact.md (A1–A18, B1–B2, C, D1–D2)
- quickstart.md

**Source of truth**: the contracts and the research decisions. Implement exactly what they say. If
something is missing or contradictory, stop and report a blocker. Do not invent anything.

**Tests**: required. The SCs are measured checks; the constitution requires timing evidence from
logs (IV) and the measured chrome test (IX.4). Each batch writes its tests in the NEW test files
it names, in the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081.**
  - Never kill or restart it, never run `pkill`/`killall`, and never bind :5080 or :5081.
  - Build and test into your own output:
    - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts/exec-<batch>`
    - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts/exec-<batch> --blame-hang-timeout 120s --filter <your classes>`
  - macOS has no `timeout`; use `--blame-hang-timeout`.
  - Docker must be running (`docker info`). If it is not, run `open -a Docker` and poll.
  - If the build breaks in a file you do not own, a sibling is mid-edit. Wait about 60 s and
    retry; never edit that file.
- **Existing tests must pass UNMODIFIED, except the test-impact A, B and D rows, which only the
  parent applies.**
  - Any other failure of an existing test means your change is wrong. Fix the code, never the
    test, a fixture or a baseline.
  - Executors never edit existing files under `tests/`, or any file under `specs/`.
  - Expected red until the parent's checkpoint: exactly the tests named in test-impact A/B/D rows.
    Report them by name; do not touch them.
- **Never commit.** The parent commits after its own gate.
- **Forbidden:**
  - no new NuGet package, script file or design token; no `style=` attribute;
  - exactly one `<script>` on the edit screen (library.js);
  - no HTML injection sink in `library.js` (`innerHTML`, `outerHTML`, `insertAdjacentHTML`,
    `document.write`);
  - no edit to `SidePanel`, `TokenCombobox`, `DangerConfirm`, `PhotoActions`, `Picture`,
    `AdminLayout`, `ImageService`, `ImageDeleteService`, `GalleryService`, any public page, or
    anything under `src/Gallery.Web/Ui/`.
- **Names, verbatim**:
  - page `PhotoEditPage` (`src/Gallery.Web/Admin/Pages/PhotoEditPage.razor`, `.razor.css`); the
    public `Site/PhotoPage` is a different type, do not touch it;
  - model `PhotoEditPageModel` with the fields in data-model.md, in that order;
  - `LibraryReader.PhotoAsync(string iid)` returning `(LibraryTile? Photo, IReadOnlyList<GalleryRef> Galleries)`;
  - `Sizes.ForAside()` and `Sizes.AsideWidth = 320`;
  - route comment `// L6` on the `MapMethods("/library/images/{iid}", ["GET", "HEAD"], …)` line;
  - classes `pe`, `pe-stage`, `lib-result`, `lib-edit`; ids `lib-edit`, `lib-gf`, `lib-del-one`;
  - `PhotoEditPage.DeleteConsequence(int)` public static, its text unchanged.
- **Log events, verbatim** (already emitted by existing code; do not add or rename any):
  - `http.request {method} {path} {status} {duration_ms} {db_reads}` (middleware);
  - `op.timing db.library.page …` for the edit screen's one image read; `db.galleries.list` and
    `db.site.order` from `GalleriesAsync()`.
- **[SAFETY]** marks owner-only access and form protection (constitution I, I.6): L6 is mapped on
  the protected `/admin` group, and the edit form carries the antiforgery token. Blocking and
  non-deferrable. T022 breaks each on purpose.
- **Tight assertions**: compare lists and hrefs for EQUALITY; "absent" means no element or
  attribute, never a bare substring; measure geometry in a real browser, never infer it from CSS.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back.
- **Your final message must be ONLY the JSON report** (`tasks_completed`, `build_succeeded`,
  `tests_passed`, `decisions_made`, `blockers`).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [X] T001 Record the baseline in `specs/017-photo-edit-screen/evidence/baseline.txt`:
  `git rev-parse HEAD`, the full-suite counts (from the finished run's own output, never a
  partial file), and `ls tests/Gallery.Web.Tests/*.cs | wc -l`.
- [X] T002 Copy `specs/016-galleries-list-icons/evidence/gate.sh` to
  `specs/017-photo-edit-screen/evidence/gate.sh`. Output `/tmp/017-gate-$L.txt`; add
  `specs/016-galleries-list-icons/evidence` to the dirs restored in its `trap … EXIT`.
- [X] T003 Write `tests/Gallery.Web.Tests/PhotoEditTimingEvidenceTests.cs` (Category=Evidence,
  modelled on 016's `GalleryListTimingEvidenceTests`) with one fact `Pre_library_with_panel`:
  - 100 images (`InsertUploadedImageAsync`), 3 galleries holding the first 10;
  - 100 GETs of `/admin/library?page=1&open={id}` for an id on page 1, each 200;
  - assert ≥ 100 `http.request GET /admin/library 200` lines;
  - write the host log to env `PHOTO_TIMING_LOG`, default
    `specs/017-photo-edit-screen/evidence/pre-timings.log`.
  Run it NOW on the unchanged tree. Commit the test file and the log.
- [X] T004 Copy 016's `p95.sh` and `p95-selftest.sh` to `specs/017-photo-edit-screen/evidence/`
  and adapt the rows:
  - pre route = lines matching `^http.request GET /admin/library 200 `;
  - post route = lines matching `^http.request GET /admin/library/images/[0-9a-z]{26} 200 `;
  - p95 post ≤ pre × 1.10 + 1 ms; every post `db_reads` ≤ the single pre value; n ≥ 100 each.
  Run the selftest into `p95-selftest.txt`.
- [X] T005 Write `specs/017-photo-edit-screen/evidence/test-impact-edit.py` from 016's:
  - rows A1–A18, B1–B2 and D1–D2 as count-asserting (file, old, new, count) replacements; A13,
    A15, A16, A17 and A10's rewrite carry their full new fact bodies, written from
    test-impact.md and contracts/http.md;
  - `DRYRUN=1` mode and a row filter by id.
  First re-run the test-impact grep (test-impact.md header) and add any new hit as the next row
  "(found at implement)". Dry-run: every row must match its count; if not, fix test-impact.md
  first.

## Phase 2: Foundational (Batch A, executor; blocks US1)

Batch A owns: `src/Gallery.Core/Images/Sizes.cs`, `src/Gallery.Web/Library/LibraryReader.cs`,
`tests/Gallery.Core.Tests/SizesTests.cs` (append only), the new
`tests/Gallery.Web.Tests/PhotoReaderTests.cs`.

- [ ] T006 [P] Add `ForAside()` and `AsideWidth` to `src/Gallery.Core/Images/Sizes.cs` exactly as
  contracts/components.md P2. Append one fact to `tests/Gallery.Core.Tests/SizesTests.cs`
  asserting the exact string
  `(min-width: 1200px) calc(100vw - 448px), (min-width: 600px) calc(100vw - 64px), calc(100vw - 32px)`.
- [ ] T007 In `src/Gallery.Web/Library/LibraryReader.cs` (R-04):
  - move the `ToTile` lambda into a private static method
    `Tile(ImageDoc d, IReadOnlyDictionary<string, GalleryRef> byId, IReadOnlyDictionary<string, int> rank, Func<…> url)`
    (match the existing url delegate's type) and call it from `PageAsync`, output unchanged;
  - add `PhotoAsync(string iid)`: one `timer.Time("db.library.page", …)` find with the `Live`
    filter and `Id == iid`, same projection as the page read; null → return `(null, [])` with no
    further read; else `GalleriesAsync()` and build the tile with `Tile(...)`;
  - leave `PageAsync(int page, string? open = null)` as it is (Batch B removes `open`).
- [ ] T008 Write `tests/Gallery.Web.Tests/PhotoReaderTests.cs` (`LibraryHost`, run the reader via
  `h.RunAsync<LibraryReader, …>`):
  - known photo in 2 galleries: tile Galleries EQUAL the holders in home order, ViewHref equals
    `PageAsync`'s tile for the same photo (record equality of the two `LibraryTile`s);
  - unknown id and tombstoned id: Photo null, and exactly 1 `op.timing db.library.page` line and
    0 `db.galleries.list` lines were logged by that call;
  - known id: exactly 3 `op.timing` db lines (`db.library.page`, `db.galleries.list`,
    `db.site.order`).

**Checkpoint (parent)**: read the diff; run gate A (full suite green, no test changes); commit.

## Phase 3: US1 + US2 — the edit screen, return to the library page (P1) 🎯 MVP

US2 (return to page N at the tile) lives in the same files as US1 (the page, its route, the L2
landing), so one batch implements both with story-labelled tasks.

Batch B owns: `src/Gallery.Web/Library/LibraryEndpoints.cs`, `LibraryReader.cs` (the `open`
removal only), the new `src/Gallery.Web/Admin/Pages/PhotoEditPage.razor` and `.razor.css`,
`src/Gallery.Web/Admin/Pages/LibraryPage.razor` and `.razor.css`,
`src/Gallery.Web/wwwroot/admin-assets/library.js`, the new
`tests/Gallery.Web.Tests/PhotoEditPageTests.cs` and `PhotoEditBrowserTests.cs`.

- [ ] T009 [US1] [SAFETY] In `src/Gallery.Web/Library/LibraryEndpoints.cs` (contracts/http.md L6):
  - add `PhotoEditPageModel` (data-model.md) beside `LibraryPageModel`;
  - map `admin.MapMethods("/library/images/{iid}", ["GET", "HEAD"], PhotoEditScreen); // L6` on
    the same `admin` group as L1–L5, directly after the L1 line;
  - `PhotoEditScreen`: `OpaqueId.IsValid` else `Results.NotFound()`; `page = PageOf(query page)`;
    status from `StatusFromQuery` (only `saved=1` → "Saved"); call `RenderPhotoEditAsync`;
  - `RenderPhotoEditAsync(ctx, reader, options, antiforgery, assets, iid, page, statusCode,
    status, saved, editTitle, editCaption, editAdd, editCreate, editLeave)`: `PhotoAsync`; null →
    `Results.NotFound()`; else render `PhotoEditPage` with `Token(ctx, antiforgery)`,
    `assets.UrlFor(AdminAssets.LibraryScriptPath)`, `ReturnPage = page`, edit values defaulting to
    the stored title and caption.
- [ ] T010 [US1] [US2] L2 `SaveImage` (contracts/http.md L2): success →
  `SeeOther($"{LibraryPath}/images/{iid}?page={page}&saved=1")`; NotFound unchanged; every other
  outcome → `RenderPhotoEditAsync(..., iid, page, r.StatusCode, r.Reason, saved: false, title,
  caption, keep ? edit.Add : null, keep ? edit.Create : null, keep ? edit.Leave : null)`.
- [ ] T011 [US1] [US2] Write `src/Gallery.Web/Admin/Pages/PhotoEditPage.razor` and
  `PhotoEditPage.razor.css` exactly per contracts/components.md P1: move the panel body,
  `TokensFor`, `GalleryOptions`, `DangerFor`, `DeleteConsequence`, `ViewFor` from
  `LibraryPage.razor:121-183` verbatim (renaming `Data.Galleries` → `Model.Galleries`,
  `Data.Page` → `Model.ReturnPage`, `open` → `Model.Photo`); breadcrumb
  `[new("Library", $"/admin/library?page={Model.ReturnPage}#t-{id}"), new(title)]`; `Picture`
  with `Sizes.ForAside()`, `PictureFit.Contain`, `PicturePriority.High`, `ShowThumbLayer="true"`.
  Header comment cites 017 P1.
- [ ] T012 [US1] Library page (P3, R-06): in `LibraryPage.razor` remove everything P3 lists and
  set the tile href; in `LibraryEndpoints.cs` trim `LibraryPageModel` (data-model.md) and
  `RenderLibraryAsync`'s `open`/`saved`/`edit*` parameters and every caller's arguments for them
  (L1 no longer reads `open`; L3/L4/L2-NotFound pass none); in `LibraryReader.cs` remove
  `PageAsync`'s `open` parameter, the off-page read and `LibraryPageData.Open` with its doc
  comment.
- [ ] T013 [P] [US1] In `src/Gallery.Web/Admin/Pages/LibraryPage.razor.css`, remove the 016 G5 block (P4). Keep the `.lib-grid`
  wrapper in the markup.
- [ ] T014 [P] [US1] In `src/Gallery.Web/wwwroot/admin-assets/library.js`, apply J1 (remove the final "panel into view" section and the
  "Photo panel (feature 015 J1)" header paragraph; reword the combobox header line). Nothing else.
- [ ] T015 [US1] [US2] [SAFETY] Write `tests/Gallery.Web.Tests/PhotoEditPageTests.cs` (HTTP,
  `LibraryHost`):
  - L6 200 for a photo in 2 galleries: exactly one `<picture` in `main` and no `class="tile`,
    `class="cs"` or `name="ids"` in the document; `sizes` EQUALS `Sizes.ForAside()`;
    `fetchpriority="high"`; no `<a` wrapping the picture;
  - breadcrumb: hrefs EQUAL `["/admin/library?page=3#t-{id}"]` for `?page=3`, current item the
    title; `?page=x` and no page → `page=1`;
  - `form#lib-edit` action `/admin/library/images/{id}`, hidden `page` value 3, the antiforgery
    field present with a non-empty value [SAFETY]; `form#lib-del-one` action
    `/admin/library/images/{id}/delete` with `page` 3;
  - `saved=1` → status line "Saved" (first `role="status"`) and `sp-status` Saved; without it, no
    status line;
  - 404 for malformed, unknown and tombstoned ids; HEAD 200 with no body for a known id;
  - reads: the `http.request GET /admin/library/images/{id} 200` line's `db_reads` = 3;
  - signed-out GET answers the same challenge status and `Location` as signed-out
    `/admin/library` [SAFETY];
  - `/admin/library?open={id}`: no `side-panel`, no `lib-del-one`, and every tile's `pa-edit` and
    `pa-group` href EQUALS `/admin/library/images/{tileId}?page=1`;
  - a 422 refusal (blank title) re-renders the edit screen: status 422, reason as the status
    line, the typed caption kept, and the photo still shown;
  - `Guards.AssertNoInlineStyle`; exactly one `<script` on the page.
- [ ] T016 [US1] [US2] Write `tests/Gallery.Web.Tests/PhotoEditBrowserTests.cs`
  (`[Collection("library-browser")]`, `LibraryBrowserFixture`). Fixture images are
  `SeedShapes.UploadedImage` (stored 1280 × 960, 4:3). For the other shapes, insert a
  `SeedShapes.UploadedImage(...)` with `Width`/`Height` overridden (portrait 960 × 1280, small
  400 × 300) and measure the photo's `.frame` box (the box `Picture` sizes from the stored
  dimensions), not the bitmap. If the frame does not follow the stored dimensions, escalate; do
  not change `Picture`.
  - SC-002 at 1440 × 900, landscape and portrait: the frame is inside the viewport (top ≥ 0,
    bottom ≤ 900, `scrollY` 0), its aspect equals the stored aspect within 1%, and its area ≥ 3 ×
    the same photo's library tile `.cell` area measured at 1440 on `/admin/library`;
  - small (400 × 300) at 1440: frame width ≤ 400 (not upscaled);
  - SC-002 at 390 × 844 (landscape and portrait): the frame is wholly inside the viewport on load,
    and `document.documentElement.scrollWidth ≤ innerWidth`;
  - the `img`'s `currentSrc` width descriptor at 1440 × 900 (DPR 1) is ≥ 960;
  - SC-004: on a site with `extraOverrides` `Library:PageSize = 4` and 10 images, open
    `/admin/library?page=2`, click a tile's pencil, edit the title, Save, click "Library" in the
    breadcrumb: path `/admin/library`, query `page=2`, and that tile is in the viewport;
  - SC-001: the folder icon on page 1 navigates to the same URL as that tile's pencil;
  - JS off: open the edit screen, change the caption, Save → stored, page shows "Saved";
  - delete from the edit screen of a page-2 photo (confirm) lands on `/admin/library` with query
    `page=2` and the status "1 deleted".

**Checkpoint (parent)**:
1. Run `test-impact-edit.py` for A1–A18, B1–B2, D1–D2.
2. Run the B rows' tests and `AdminLayoutConsistencyBrowserTests` (IX.4) green.
3. Hand-walk quickstart steps 1–9 at 390 and 1440.
4. Gate B; commit.

## Phase 4: Polish and cross-cutting (parent unless marked)

- [ ] T017 Replace `PhotoEditTimingEvidenceTests.Pre_library_with_panel` with
  `Post_photo_edit_screen`: same data, 100 GETs of `/admin/library/images/{id}?page=1`, assert ≥
  100 `http.request GET /admin/library/images/` lines, default log
  `specs/017-photo-edit-screen/evidence/photo-timings.log`. Run it, then
  `p95.sh pre-timings.log photo-timings.log | tee evidence/p95-output.txt`. A miss is recorded,
  not tuned.
- [X] T018 [P] VII.2: write `specs/017-photo-edit-screen/evidence/design-record-edit.py` (each
  old string's count asserted before writing) for
  `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md` per research R-10: D-20
  drops the "library below 800 px" clause (the builder clause stays); the D-23 Library line
  replaces "where edit and gallery toggle the side panel (feature 015)" with the R-10 text. Run
  `assemble.sh` and `validate.sh`, tee to `evidence/design-validate.txt`; then
  `grep -c 'toggle the side panel' docs/design.md` = 0 and `git show --stat` includes
  `docs/design.md`.
- [ ] T019 [P] Write `specs/017-photo-edit-screen/decisions.md` with 016's `add-decisions.py`:
  every executor `decisions_made`, one STATED-CHANGE row per A/B/D row, the plan's spec
  corrections (SC-002, FR-007), and the three spec defaults (gallery icon opens the screen, stay
  after save, panel removed).
- [ ] T020 Write the 017 entry in `docs/roadmap.md`: status, evidence paths, the OWNER-ONLY item.
- [ ] T021 Re-run the test-impact grep against the finished tree: no `?open=` producer left in
  `src/` (`grep -rnE '[?&]open=' src --include=*.cs --include=*.razor --include=*.js` → no hits;
  DangerConfirm's `open="@Open"` attribute is not a match), and no reference to
  `LibraryPageData.Open` or `PageAsync(…, open)`.
- [ ] T022 Write `specs/017-photo-edit-screen/evidence/non-vacuity.sh` from 016's, one break per
  guard, each failing on its own assertion:
  - [SAFETY] L6 mapped on `app` instead of the `admin` group → PhotoEditPageTests signed-out
    fact and LibraryAccessTests L6 red;
  - [SAFETY] the antiforgery hidden input dropped from `lib-edit` → PhotoEditPageTests token
    fact red;
  - `sizes` set to `Sizes.ForGrid(4, 2, 1)` → PhotoEditPageTests sizes fact and the browser
    `currentSrc` fact red;
  - `.pe-stage` max-height removed → SC-002 1440 portrait fact red;
  - L2 success back to `/admin/library?…&open=` → LibraryEditTests (A1) red;
  - breadcrumb href without `page` → SC-004 browser fact red;
  - `PhotoAsync` without the `Live` filter → tombstoned-404 fact red;
  - the photo page dropped from the layout list → no break needed; instead break
    `PhotoEditPage.razor.css` with `.pe { margin-top: 40px }` → AdminLayoutConsistencyBrowserTests red.
  Run `NV_DRYRUN=1`, then the real run; tee to `non-vacuity.txt`.
- [ ] T023 Final gate (`evidence/gate-final.txt`): `dotnet clean`, `-warnaserror` build, full suite
  including Evidence and Browser; `p95.sh` recorded; restore earlier features' regenerated logs;
  commit.
- [ ] T024 Merge to main (`--no-ff`, body as in 016's) and push. Watch the deploy job go green.
  Signed out, `curl -sI https://port.zacal.me/admin/library/images/{any-id}` answers 302 to
  sign-in; save to `evidence/browser-prod.txt`. Do not change production data.
- [ ] T025 OWNER step, then parent: the owner signs in to the automation browser on
  port.zacal.me. Then open one library photo's edit screen at 390 and 1440: no horizontal scroll,
  0 broken images, photo wholly visible, breadcrumb back to the same library page. Append to
  `evidence/browser-prod.txt`. No saves on production.

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 On port.zacal.me, edit a photo from library page 2: the screen shows only that photo,
  Save keeps you there, "Library" takes you back to page 2.

## Dependencies and execution order

- Setup (parent): T001 → T002 → T003 → T004 → T005.
- Batch A: T006 ‖ T007 → T008; checkpoint A.
- Batch B (needs A): T009 → T010 → T011 → T012, with T013 ‖ T014 any time; then T015 → T016;
  checkpoint B (test-impact rows).
- Polish: T017, T018 ‖ T019, T020, T021 → T022 → T023 → T024 → T025.

## Parallel example

```text
Wave 1: Batch A — T006 (Sizes) ‖ T007 (reader), then T008
Wave 2: Batch B — T009–T012 in order; T013 ‖ T014 alongside; then T015, T016
Wave 3: T017 evidence; T018 design record ‖ T019 decisions; non-vacuity; final gate; merge
```

## Implementation strategy

- MVP = Phases 1–3: the edit screen with return-to-page and the panel removed. It is the whole
  feature; Phase 4 is evidence, design record and release.
- Severable seams:
  - US2 return-to-page (T010/T011 `page` plumbing): cut → the breadcrumb goes to library page 1.
  - Panel-only CSS/JS removal (T013/T014, D1/D2): cut → dormant code remains; not recommended.
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
