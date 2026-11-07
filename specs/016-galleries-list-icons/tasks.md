# Tasks: Icon Controls on the Galleries List

**Input**: design documents in `specs/016-galleries-list-icons/`:
- spec.md (FR-001–FR-015, SC-001–SC-007)
- plan.md
- research.md (R-01–R-09)
- data-model.md
- contracts/components.md (G1–G6, J1)
- contracts/test-impact.md (A1–A10, B1–B5, C)
- quickstart.md

**Source of truth**: the contracts and the research decisions. Implement exactly what they say. If
something is missing or contradictory, stop and report a blocker. Do not invent anything.

**Tests**: required. The spec's SCs are measured checks. The constitution requires timing evidence
from logs and measured layout tests (IX.4). Each batch writes its tests in the NEW test files it
names, in the same batch as the code.

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
- **Existing tests must pass UNMODIFIED, except the test-impact (A) rows, which only the parent
  applies.**
  - Any other failure of an existing test means your change is wrong. Fix the code, never the
    test, a fixture or a baseline.
  - Executors never edit existing files under `tests/`, or any file under `specs/` (including
    `specs/006-public-site-local/evidence/ui-baseline/`).
  - `UiPagesUnchangedTests` writes a missing baseline on first run and fails. Delete what it
    wrote (check `git status`) before finishing; the parent regenerates baselines.
- **Never commit.** The parent commits after its own gate.
- **Forbidden:**
  - no new NuGet package, script file or `<script>` element;
  - no `style=` attribute and no new design token;
  - no literal `44px` in `.razor.css` (use `var(--tap)`);
  - no HTML injection sink in `library.js` (`innerHTML`, `outerHTML`, `insertAdjacentHTML`,
    `document.write`).
- **Byte identity**: `Tile`, `DragHandle`, `IconButton`, `ContactSheet`, `VisibilityToggle` and
  every public page are not edited. Pinned output changes only through the B rows.
- **Names, verbatim**:
  - type `GalleryActions`, with `VisibilityLabel(bool isPublic, bool hasPhotos)` as a public static
    method in `GalleryActions.cs`;
  - page `GalleryActionsPage`;
  - classes `gallery-actions`, `ga-vis`, `ga-view`, `ga-edit`, `gal-band`, `lib-grid`, `b-grid`;
  - catalogue slug `molecules/gallery-actions`;
  - every label in data-model.md, verbatim.
- **Icons**: copy `eye` and `eye-off` from
  `https://raw.githubusercontent.com/lucide-icons/lucide/1.70.0/icons/{name}.svg`. Take only the
  inner elements, with whitespace collapsed exactly as the 8 existing `Icons.Paths` values were
  made. No edits, no other icons, no other version.
- **[SAFETY]** marks the public-isolation guarantee:
  - publish goes through the unchanged G4 route, and an empty gallery is still refused (II.5–6);
  - public pages render byte-identical (FR-013).
  It is blocking and non-deferrable. T026 breaks it on purpose.
- **Tight assertions**:
  - compare lists and orders for EQUALITY;
  - "absent" means no element or attribute (`name="public"`, `class="gal-status"`), never a bare
    substring;
  - measure geometry in a real browser, never infer it from CSS text.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back.
- **Your final message must be ONLY the JSON report** (`tasks_completed`, `build_succeeded`,
  `tests_passed`, `decisions_made`, `blockers`).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [ ] T001 Record the baseline in `specs/016-galleries-list-icons/evidence/baseline.txt`:
  `git rev-parse HEAD`, the full-suite counts, and `ls tests/Gallery.Web.Tests/*.cs | wc -l`.
- [ ] T002 Copy `specs/015-tile-icon-controls/evidence/gate.sh` to
  `specs/016-galleries-list-icons/evidence/gate.sh`. Use output `/tmp/016-gate-$L.txt`, and add
  `specs/015-tile-icon-controls/evidence` to the restored dirs.
- [ ] T003 Freeze the pre-change snapshots by copying
  `specs/006-public-site-local/evidence/ui-baseline/` to
  `specs/016-galleries-list-icons/evidence/pre-016-ui-baseline/`. Then write
  `specs/016-galleries-list-icons/evidence/baseline-diff.py` from 015's:
  - for each regenerated file, strip exactly the B1, B3 and B4 changes and compare with the
    frozen copy;
  - B2 is new, so it is shown, not diffed;
  - print EQUAL or RESIDUE per file;
  - assert that each changed file DIFFERS from its frozen copy before stripping;
  - include a `--selftest` mode (intended edits give EQUAL; one stray edit gives RESIDUE).
  Save the selftest output to `evidence/baseline-diff-selftest.txt`.
- [ ] T004 Write `tests/Gallery.Web.Tests/GalleryListTimingEvidenceTests.cs` (Category=Evidence,
  modelled on 015's `TileTimingEvidenceTests`):
  - 30 galleries: 24 with photos (half of them public), 6 empty (3 of those public, published
    before their photos were removed);
  - 100 GETs of `/admin/galleries`;
  - assert at least 100 `http.request GET /admin/galleries 200` lines;
  - write the host log to the path in env `LIST_TIMING_LOG`, default
    `specs/016-galleries-list-icons/evidence/list-timings.log`.
  Run it NOW on the unchanged tree, with
  `LIST_TIMING_LOG=specs/016-galleries-list-icons/evidence/pre-timings.log` (the SC-005 pre-change
  capture). Commit both files.
- [ ] T005 Copy 015's `p95.sh` and `p95-selftest.sh` to `specs/016-galleries-list-icons/evidence/`:
  - one route row, `GET /admin/galleries`: p95 ≤ pre × 1.10 + 1 ms, and `db_reads` EQUAL on every
    line;
  - n ≥ 100;
  - output to `p95-output.txt`; run the selftest into `p95-selftest.txt`.
  Run `p95.sh pre-timings.log pre-timings.log` once: ALL PASS.
- [ ] T006 Write `specs/016-galleries-list-icons/evidence/test-impact-edit.py` from 015's:
  - rows A1–A10 as count-asserting (file, old, new, count) replacements;
  - a `DRYRUN=1` mode and a row filter by id;
  - A1 and A6 rewrite whole facts, so the script carries their full new bodies (written from
    data-model.md and G2/G4).
  Dry-run it now: every row must match its count. If anything differs, fix test-impact.md first.

## Phase 2: Foundational: the icons and the GalleryActions molecule (blocks US1–US3)

Batch A owns:
- `src/Gallery.Web/Components/Atoms/Icons.cs`;
- the new `src/Gallery.Web/Components/Molecules/GalleryActions.razor`, `GalleryActions.razor.css`
  and `GalleryActions.cs`;
- the new `src/Gallery.Web/Ui/Pages/GalleryActionsPage.razor`;
- `src/Gallery.Web/Ui/UiCatalog.cs` (one inserted line);
- the new `tests/Gallery.Web.Tests/GalleryActionsTests.cs`.

- [ ] T007 Add `eye` and `eye-off` to `Icons.Paths` in
  `src/Gallery.Web/Components/Atoms/Icons.cs` (G1), from the fetched Lucide 1.70.0 files. Put the
  two SHA-256 values in the JSON report's `decisions_made`; the parent pins them through A8.
- [ ] T008 Write `src/Gallery.Web/Components/Molecules/GalleryActions.cs`, the pure rules:
  - `VisibilityLabel(isPublic, hasPhotos)` returns the four names in data-model.md, verbatim;
  - the empty-private name is built as `$"Private. {GalleryService.EmptyPublishReason}"`;
  - an icon rule (public → `eye`, private → `eye-off`);
  - `CanToggle(isPublic, hasPhotos)` is false only for private and empty.
- [ ] T009 Write `GalleryActions.razor` and `GalleryActions.razor.css` per G2 and G2a:
  - the parameters are those in data-model.md;
  - the order is the DragHandle with `Icon="true"`, `ga-vis`, `ga-view`, `ga-edit`;
  - `Name="public" Value="on"` only when private with photos;
  - no `Toggle`;
  - the CSS mirrors `.photo-actions`: flex, `nowrap`, gap 4px, height `var(--tap)`, centred.
  For each RenderFragment, use expressions that render null rather than `@if` blocks.
- [ ] T010 [P] Write `src/Gallery.Web/Ui/Pages/GalleryActionsPage.razor` (G6):
  - 5 specimens, each a `w-320` Tile with `TitleOnPhoto="true"` and a GalleryActions caption;
  - the labels are "Public", "Private", "Public, no photos", "Private, no photos", "Long title";
  - use a catalogue sample image as on TilePage;
  - the empty specimens use an empty Tile. A blank cell is acceptable on the catalogue page; the
    real band for empty cards is G3's.
  In `src/Gallery.Web/Ui/UiCatalog.cs`, insert the entry directly after the `VisibilityToggle`
  line.
- [ ] T011 Write `tests/Gallery.Web.Tests/GalleryActionsTests.cs`:
  - the label rule over the 4 states (EQUALITY with the data-model strings);
  - child order EQUALS [handle, ga-vis, ga-view, ga-edit] for every state, with edit last;
  - visibility:
    - `form` equals VisibilityForm;
    - `name="public" value="on"` is present only for private with photos and absent (no
      attribute) for both public states;
    - the empty-private control is a `span role="img"`, with no `form`;
    - no `aria-pressed` anywhere;
  - view: with ViewHref it is a link with `target="_blank" rel="noopener"`; without it, an
    unavailable span named "No public page";
  - edit: an `href` equal to EditHref;
  - the DragHandle keeps `class="handle"` plus Position/Total;
  - `Guards.AssertNoInlineStyle` and `Guards.AssertNoScript`;
  - CSS guard on `GalleryActions.razor.css`: `nowrap`, `var(--tap)`, no `44px`, no
    `flex-wrap: wrap`;
  - GalleryActionsPage renders 5 specimens in order.

**Checkpoint (parent)**:
1. Apply test-impact A8 (IconTests: Keys, the two hashes from the report, counts) and A9
   (IndexTests 39 → 40).
2. Regenerate B1 and B3, and write B2.
3. Run `baseline-diff.py`: EQUAL. Look over B2 in a browser.
4. Run gate A, then commit.

## Phase 3: US1 + US2: the list cards with the icon row and publishing (P1) 🎯 MVP

US1 (the icon row, edit last) and US2 (publish/unpublish) change the same files
(`GalleriesPage`, its CSS, `library.js`), so they form one batch with story-labelled tasks.

Batch B owns:
- `src/Gallery.Web/Admin/Pages/GalleriesPage.razor` and `GalleriesPage.razor.css`;
- `src/Gallery.Web/Library/LibraryEndpoints.cs` (`GalleryRow.Status` only);
- `src/Gallery.Web/wwwroot/admin-assets/library.js` (J1 only);
- the new `tests/Gallery.Web.Tests/GalleryListBrowserTests.cs`.

- [ ] T012 [US1] In `src/Gallery.Web/Admin/Pages/GalleriesPage.razor`, apply G3:
  - a cover card is `Tile TitleOnPhoto="true"` with a GalleryActions caption;
  - an empty card is `.gal-tile.tile-empty`, with `<span class="gal-band">` holding the title
    inside `.cell` (replacing the `Title` under it), followed by GalleryActions;
  - remove `RowFor`'s `.gal-row`, `.gal-status`, `ViewFor`, and the `VisibilityToggle` use;
  - keep `DisabledReasonFor` (GalleryPage uses it);
  - pass every value from the page: VisibilityForm `gal-vis-{id}`; ViewHref `/g/{id}/{slug}`
    only when HasPhotos; EditHref `/admin/galleries/{id}`; DragLabel `Move {title}`;
    Position/Total;
  - update the header comment (UI-2: the icon row).
- [ ] T013 [US1] In `GalleriesPage.razor.css`:
  - remove `.gal-row` and `.gal-status`;
  - add `.gal-tile .cell` `position: relative`, plus `.gal-band`, an absolutely positioned
    bottom band with the values of 015's `.on-photo` (copy them from `Tile.razor.css`): one line
    with an ellipsis, and the full title in a `title` attribute on the band span.
- [ ] T014 [US1] In `src/Gallery.Web/Library/LibraryEndpoints.cs`, remove `GalleryRow.Status` and
  its doc comment (R-04). Grep `src/` for any other `.Status` consumer of GalleryRow first; R-09
  found none.
- [ ] T015 [US2] [SAFETY] In `src/Gallery.Web/wwwroot/admin-assets/library.js`, apply J1:
  - remove the `gal-vis-` submit branch in the `change` handler, keeping the builder switch's
    return-early path, so a non-gal-vis switch still submits nothing;
  - remove the header comment's gal-vis line.
  Leave the visibility route, the forms and `from=list` untouched.
- [ ] T016 [US1] [US2] Write `tests/Gallery.Web.Tests/GalleryListBrowserTests.cs`
  (`[Collection("library-browser")]`, `LibraryBrowserFixture`):
  - US1, at 390, 900 and 1440:
    - every card's `.gallery-actions` children in order EQUAL
      [handle, ga-vis, ga-view, ga-edit];
    - the edit control's right edge is the row's largest;
    - the row is one line (every child shares one top), and every target is ≥ 44 × 44;
    - no `a.text-link` and no `.btn` in any row (SC-002);
    - every row's height is EQUAL across cards (SC-001);
    - the band is inside the cell on cover and empty cards, and its contrast is ≥ 4.5:1 over
      white, black and mid-grey covers (SC-004, with 015's luminance helper copied into this
      file);
  - US2 [SAFETY]:
    - with JS on and JS off (a context with `JavaScriptEnabled=false`):
      - click the eye-off on a private gallery with photos → storage public, and the card shows
        `eye`;
      - click the eye → storage private;
    - the empty-private control is not a button, and clicking it changes nothing in storage;
    - the SC-006 names match storage for public, private, empty-public and empty-private;
  - US3:
    - a keyboard lift, move and drop on the move icon saves the order (from storage);
    - a mouse drag of the 3rd card to the front saves it.

**Checkpoint (parent)**:
1. Apply A1–A7 and A10 with `test-impact-edit.py`.
2. Run the B-row check (B5: only B1–B3 so far).
3. Hand-walk quickstart steps 1–4 at 390 and 1440.
4. Run gate B, then commit.

## Phase 4: US3 + FR-015: reorder kept, and every photo as wide as its row (P2)

US3's reorder is verified in T016, since its hooks are G3's. This phase is FR-015/SC-007: the grid
columns across the three owner grids.

Batch C owns:
- `src/Gallery.Web/Admin/Pages/LibraryPage.razor` and `LibraryPage.razor.css` (the grid wrapper and
  the counts only);
- `src/Gallery.Web/Components/Organisms/GalleryBuilder.razor` and `GalleryBuilder.razor.css` (the
  grid wrapper and the counts only);
- `GalleriesPage.razor`: the one `ContactSheet` line and the `GridSizes` line, applied AFTER
  Batch B lands (the parent sequences it);
- the new `tests/Gallery.Web.Tests/GridWidthBrowserTests.cs`.

- [ ] T017 [US3] Library, per G5:
  - in `LibraryPage.razor`, set `ContactSheet Large="4" Medium="2" Small="1"`;
  - change `GridSizes` to `Sizes.ForGrid(4, 2, 1)`;
  - wrap the ContactSheet in `<div class="lib-grid">`;
  - in `LibraryPage.razor.css`, give `.lib-grid` `container-type: inline-size`. Inside
    `@media (min-width: 1200px)`, add `@container (max-width: 799.98px)` with
    `::deep .cs { grid-template-columns: repeat(2, minmax(0, 1fr)); }`. Confirm that the scoped
    selector reaches the ContactSheet's `.cs` (check the computed style in the browser test).
- [ ] T018 [US3] Builder, per G5: the same change in `GalleryBuilder.razor` and
  `GalleryBuilder.razor.css`, with `b-grid` and `991.98px`. Keep every builder hook
  (`reorder-grid` must still find `.cs`; check that `reorder.js`'s `querySelector('.cs')` still
  resolves inside the wrapper).
- [ ] T019 [US3] Galleries list: `ContactSheet Large="3" Medium="2" Small="1"` and
  `Sizes.ForGrid(3, 2, 1)` in `GalleriesPage.razor`. No container query (R-07).
- [ ] T020 [US3] Write `tests/Gallery.Web.Tests/GridWidthBrowserTests.cs` (SC-007):
  - widths 600, 700, 760, 900, 1199, 1200, 1300, 1440 and 1920;
  - grids:
    - library (panel closed; panel open via `?open={id}`);
    - builder;
    - list;
  - for every tile, tile width ≥ its row's `scrollWidth`, the row is one line, and every target
    is ≥ 44 × 44;
  - the column count EQUALS the expected table (data-model.md "Grid columns") at each width;
  - at 390, one column on all three;
  - a public listing page at 900 keeps its D-20 columns (3), so public grids are untouched.

**Checkpoint (parent)**:
1. Regenerate B4 (`organisms/gallery-builder.html`) and run `baseline-diff.py`: EQUAL.
2. Run `PublicOutputUnchangedTests` green, unmodified.
3. Hand-walk quickstart steps 5–7.
4. Run gate C, then commit.

## Phase 5: Polish and cross-cutting (parent unless marked)

- [ ] T021 [P] Check `IconRegistryGuardTests`: it passes unmodified, and the used-name set
  includes `eye` and `eye-off`. If it misses them (a new call shape), escalate; don't edit it.
- [ ] T022 [P] VII.2 design-record update, scripted in
  `specs/016-galleries-list-icons/evidence/design-record-edit.py` (counts asserted), in
  `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md`:
  - D-20: the admin grids get medium 2 columns (library/builder 4/2/1, list 3/2/1), plus the panel
    rule (2 columns when the grid is too narrow for 4 rows of icons; feature 016);
  - D-21: `GalleryActions` (the gallery card's move/visibility/view/edit icon row);
  - the D-23 Gallery list line: "Each card has a drag handle, a Public switch, and a View link"
    becomes "Each card has its title on the cover and one icon row: move, visibility (eye /
    crossed-out eye), view, edit (feature 016)".
  Then run `assemble.sh` and `validate.sh`, teeing to `evidence/design-validate.txt` (0 failures).
- [ ] T023 Write `specs/016-galleries-list-icons/decisions.md` with `add-decisions.py`, copied
  from 015:
  - every executor `decisions_made`;
  - one STATED-CHANGE row per A1–A10 and B1–B4;
  - the Q1 and Q2 owner answers.
- [ ] T024 Write the 016 entry in `docs/roadmap.md`: its status, the evidence paths, the
  OWNER-ONLY review, and the note "fixes 015's sub-44 px builder icons at tablet and panel widths".
- [ ] T025 Run the evidence test (default path), then
  `p95.sh pre-timings.log list-timings.log`. A miss is recorded, not tuned (owner rule).
- [ ] T026 Write `specs/016-galleries-list-icons/evidence/non-vacuity.sh` from 015's, with one
  break per guard:
  - [SAFETY] the private-with-photos eye omits `name="public"` → GalleryListBrowserTests publish
    red;
  - [SAFETY] the empty-private control renders as a button → GalleryActionsTests red;
  - the edit control is moved before view → GalleryActionsTests order red, plus browser
    right-most red;
  - `VisibilityLabel` swaps the public/private names → SC-006 red;
  - `GalleryActions.razor.css` `flex-wrap: wrap` plus `width: 100px` → the GalleryListBrowserTests
    one-line fact red;
  - library medium back to 3 → GridWidthBrowserTests red at 600;
  - the builder container query removed → GridWidthBrowserTests red at 1200;
  - an extra unused key in `Icons.Paths` → IconRegistryGuardTests red.
  Run `NV_DRYRUN=1`, then the real run. Each break must fail on its own assertion. Tee to
  `non-vacuity.txt`.
- [ ] T027 Final gate (`evidence/gate-final.txt`):
  1. `dotnet clean`, then a `-warnaserror` build and the full suite, including Evidence and
     Browser.
  2. `p95.sh` result recorded; `baseline-diff.py` EQUAL.
  3. `git checkout --` the earlier features' regenerated logs.
  4. Commit.
- [ ] T028 Merge to main (`--no-ff`, with a body as in 015's) and push. Watch the deploy job go
  green. Then save the production check to `evidence/browser-prod.txt`:
  - `/admin/galleries` and `/ui/molecules/gallery-actions` answer 302 to sign-in;
  - public `/g` is 200 and unchanged.
  Do not change data on production.
- [ ] T029 Create a GitHub issue for the open OWNER item, as with 015 (#4).

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 On port.zacal.me, check that `/admin/galleries` cards show the icon row with edit last.
  Toggle one gallery's eye twice. Resize to tablet width. Open a library photo's panel at about
  1300 px and check that every photo shows its full icon row.

## Dependencies and execution order

- Setup: T001 → T002 → T003 → T004 → T005 → T006 (parent).
- Batch A: T007 → T008 → T009 → T011, with T010 ‖ T009, then the parent checkpoint (A8, A9, B1–B3).
- Batch B (needs A): T012 → T013, T014, T015 → T016, then checkpoint B (A1–A7, A10).
- Batch C (needs A): T017 ‖ T018, then T019 (after B lands, in the same file as T012) → T020, then
  checkpoint C (B4).
  - B and C can run in parallel. Their files are disjoint apart from T019, which the parent
    applies after B.
- Polish:
  - T021 and T022 in parallel;
  - T023, T024 and T025 next;
  - T026 needs every guard, then T027 → T028 → T029.

## Parallel example

```text
Wave 1: Batch A (eye/eye-off, GalleryActions + page + tests)
Wave 2: Batch B (GalleriesPage cards, J1, list browser tests)  ‖  Batch C (library/builder grids + GridWidth tests)
Wave 3: T019 (list grid line), checkpoint C; T021 ‖ T022; then evidence, non-vacuity, final gate
```

## Implementation strategy

- The MVP is Phases 1–3: the list cards with icons, edit last, and publishing as before. It is
  deployable on its own.
  - The list then uses 3 columns at medium width until Phase 4, so its 136–188 px tiles at
    600–760 px would squeeze the row. Ship Phase 4 with it unless the owner says otherwise.
- Severable seams:
  - the title on the cover (T012/T013 band; keep `Title` under the cell);
  - removing the status words (keep `GalleryRow.Status` as small text under the row);
  - the container-query panel rule (T017/T018 query only; the medium 2-column change stays). Cut
    it and the library-with-panel and builder grids squeeze below 1440 px, as today.
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
