# Tasks: Icon Controls on Library Photos

**Input**: design documents in `specs/015-tile-icon-controls/`: spec.md, plan.md, research.md
(R-01–R-10), data-model.md, contracts/components.md (K1–K8, J1), contracts/test-impact.md
(A1–A14, B1–B7, C), quickstart.md, decisions.md.

**Source of truth**: the contracts and the research decisions. Implement exactly what they say. If
something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: required. The spec's SCs are measured checks, and the constitution requires timing
evidence from logs and measured layout tests (IX.4). Each batch writes its tests in the NEW test
files it names, in the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081.** Never kill or restart it, never run
  `pkill`/`killall`, and never bind :5080 or :5081. Build and test into your own output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts/exec-<batch>`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts/exec-<batch> --blame-hang-timeout 120s --filter <your classes>`
  - macOS has no `timeout`; use `--blame-hang-timeout`.
  - Docker must be running (`docker info`; if it is not, `open -a Docker` and poll).
  - If the build breaks in a file you do not own, a sibling is mid-edit. Wait about 60 s and
    retry; never edit it.
- **Existing tests must pass UNMODIFIED**, except the test-impact (A) rows, which only the parent
  applies. Any other existing-test failure means your change is wrong. Fix the code, never the
  test, a fixture or a baseline.
  - Executors never edit existing files under `tests/`, or any file under `specs/` (including
    `specs/006-public-site-local/evidence/ui-baseline/`).
  - `UiPagesUnchangedTests` writes a missing baseline on first run and fails. Delete what it
    wrote (check `git status`) before finishing; the parent regenerates baselines.
- **Never commit.** The parent commits after its own gate.
- **No new NuGet package, no new script file, no new `<script>` element, no `style=` attribute,
  no new design token, no literal `44px` in `.razor.css` (use `var(--tap)`).** No HTML injection
  sink in `library.js` (`innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`).
- **Byte identity**: every new component parameter defaults to today's output, byte for byte.
  Never put an `@if` next to existing markup in a pinned component (`Tile`, `DragHandle`,
  `CoverMarker`, `GalleryBuilder`). Use an expression that renders null by default:
  `private RenderFragment? X => cond ? @<…/> : null;` placed as `@X`. If a newline is eaten,
  restore it with a static `MarkupString`.
- **Keep every hook**: the builder's `b-ctl`, `handle`, `cover-marker`, `role="status"`,
  `form`/`name`/`value` attributes and `aria-label`s stay exactly as they are (reorder.js and
  existing tests key on them).
- **Names, verbatim**:
  - types `Icons`, `Icon`, `IconButton`, `PhotoActions` (with `Membership`, `MembershipOf`,
    `IconOf` and `CountLabel` as K4), `IconPage`, `IconButtonPage`, `PhotoActionsPage`;
  - parameters `TitleOnPhoto`, `Id` (Tile), `Icon` (DragHandle, CoverMarker);
  - classes `icon`, `icon-button`, `is-active`, `is-unavailable`, `on-photo`, `photo-actions`,
    `pa-group`, `pa-view`, `pa-edit`;
  - attribute `data-icon`;
  - catalogue slugs `atoms/icon`, `atoms/icon-button`, `molecules/photo-actions`;
  - the labels in K4/K6, verbatim.
- **Icons**: copy the 8 Lucide 1.70.0 paths from
  `https://raw.githubusercontent.com/lucide-icons/lucide/1.70.0/icons/{name}.svg`, taking only
  the inner elements with whitespace collapsed. Copy them exactly, with no edits, no other icons,
  and no other version.
- **[SAFETY]** marks the public-isolation guarantee: public tiles render byte-identical (FR-004,
  constitution II/IV.22). It is blocking and non-deferrable, and T029 breaks it on purpose.
- **Tight assertions**:
  - lists and orders are compared for EQUALITY;
  - "absent" means no element or attribute (`class="photo-actions"`, `data-icon="…"`), never a
    bare substring;
  - geometry is measured in a real browser, never inferred from CSS text.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back.
- **Your final message must be ONLY the JSON report** (`tasks_completed`, `build_succeeded`,
  `tests_passed`, `decisions_made`, `blockers`).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [X] T001 Record the baseline in `specs/015-tile-icon-controls/evidence/baseline.txt`:
  `git rev-parse HEAD`, the full-suite counts, and `ls tests/Gallery.Web.Tests/*.cs | wc -l`.
- [X] T002 Copy `specs/014-gallery-combobox/evidence/gate.sh` to
  `specs/015-tile-icon-controls/evidence/gate.sh`. Use output `/tmp/015-gate-$L.txt`, and add
  `specs/014-gallery-combobox/evidence` to the restored dirs.
- [X] T003 Freeze the pre-change snapshots: copy `specs/006-public-site-local/evidence/ui-baseline/`
  to `specs/015-tile-icon-controls/evidence/pre-015-ui-baseline/`. Then write
  `specs/015-tile-icon-controls/evidence/baseline-diff.py`:
  - for each regenerated file, strip exactly the intended change (test-impact B1–B5) and compare
    with the frozen copy;
  - print EQUAL or RESIDUE per file;
  - assert each regenerated file DIFFERS from its frozen copy before stripping;
  - include a `--selftest` mode: apply only the intended edits to a temp copy (must say EQUAL),
    then add one stray edit to a page that must not change (must say RESIDUE);
  - tolerate `b-xxxxxxxxxx` scope attributes.
  Run `--selftest` and save the output to `evidence/baseline-diff-selftest.txt`.
- [X] T004 Write `tests/Gallery.Web.Tests/TileTimingEvidenceTests.cs`
  (Category=Evidence, modelled on `MembershipTimingEvidenceTests`):
  - 100 photos, one gallery "Coast" holding 40 of them;
  - 100 GETs of `/admin/library`, 100 GETs of `/admin/galleries/{gid}`;
  - assert at least 100 `http.request GET` lines for each route template. Grep a real line first
    for the builder's template;
  - write the host log to the path in env `TILE_TIMING_LOG`, default
    `specs/015-tile-icon-controls/evidence/tile-timings.log`.
  Run it NOW on the unchanged tree with
  `TILE_TIMING_LOG=specs/015-tile-icon-controls/evidence/pre-timings.log` (SC-005 pre-change
  capture), and commit both files.
- [X] T005 Write `specs/015-tile-icon-controls/evidence/p95.sh`:
  - rows: library and builder GET p95 ≤ pre p95 × 1.10 + 1 ms; every post line's `db_reads`
    EQUALS the pre value (equality);
  - n ≥ 100 per route in both logs; a missing or empty log FAILs;
  - tee the output to `p95-output.txt`.
  Write `p95-selftest.sh` (pass; slow; extra read; n = 99; empty) and run it into
  `p95-selftest.txt`. Run `p95.sh pre-timings.log pre-timings.log` once and expect ALL PASS.
- [X] T006 Write `specs/015-tile-icon-controls/evidence/test-impact-edit.py` from 014's: rows
  A1–A14 as count-asserting (file, old, new, count) replacements, `DRYRUN=1` mode, and a row
  filter by id. Dry-run it now (every row must match its count), and fix test-impact.md first if
  anything differs. A3 and A12 rewrite whole facts: give them their full new bodies in the script.

## Phase 2: Foundational: icon atoms and the tile's title-on-photo (blocks every story)

Batch A owns:
- `src/Gallery.Web/Components/Atoms/Icons.cs`, `Icon.razor`, `Icon.razor.css`,
  `IconButton.razor`, `IconButton.razor.css`;
- `src/Gallery.Web/Components/Molecules/Tile.razor` and `Tile.razor.css`;
- `src/Gallery.Web/Ui/Pages/IconPage.razor`, `IconButtonPage.razor`, `TilePage.razor`
  (append only);
- `src/Gallery.Web/Ui/UiCatalog.cs` (two inserted lines);
- the new `tests/Gallery.Web.Tests/IconTests.cs` and `tests/Gallery.Web.Tests/TileOnPhotoTests.cs`.

- [X] T007 [P] Write `src/Gallery.Web/Components/Atoms/Icons.cs`:
  - the 8 paths and the `Source` and `License` constants, exactly per K1 and the R-01 table;
  - the ISC notice in the file header.
- [X] T008 Write `Icon.razor` and `Icon.razor.css` (K1: exact markup, unknown name throws), then
  `IconButton.razor` and `IconButton.razor.css` (K2: link, button and unavailable forms;
  `aria-current`/`aria-pressed` rules; the CSS values). Use `var(--tap)` and existing colour
  tokens only.
- [X] T009 [SAFETY] In `src/Gallery.Web/Components/Molecules/Tile.razor` and `Tile.razor.css`, add
  `TitleOnPhoto` and `Id` per K3:
  - the band is a sibling AFTER `.overlay` inside Picture's frame, with z-index 1;
  - with both at their defaults the output is byte-identical (`PublicOutputUnchangedTests` and
    every `UiPagesUnchangedTests` slug stay green, unmodified).
- [X] T010 [P] Write `src/Gallery.Web/Ui/Pages/IconPage.razor` and `IconButtonPage.razor`
  with the K8 specimens (labels verbatim). In `src/Gallery.Web/Ui/UiCatalog.cs`, insert the Icon
  and IconButton entries directly after the `SelectMark` line. In `TilePage.razor`, append the
  three "Title on photo, …" specimens after the existing ones.
- [X] T011 Write `tests/Gallery.Web.Tests/IconTests.cs`:
  - K1 markup for each icon:
    - `data-icon`, `viewBox="0 0 24 24"`, `stroke="currentColor"`;
    - `fill` is `none`, and `currentColor` when Filled;
    - `aria-hidden="true"`, `focusable="false"`;
  - an unknown name throws;
  - each `Icons.Paths` value equals the inner markup of the Lucide 1.70.0 file. Pin a SHA-256 of
    each value in the test, computed once from the fetched files (no network at test time);
  - K2 for every form:
    - link vs button vs span;
    - `aria-label` == `title` == Label;
    - Active link gives `aria-current="true"` and `is-active`;
    - a toggle button gives `aria-pressed="true"`/`"false"` and never `True`/`False` or a bare
      attribute;
    - Target `_blank` gives `rel="noopener"`;
    - unavailable gives `role="img"` with no `href` and no `tabindex`;
  - `Guards.AssertNoInlineStyle` and `Guards.AssertNoScript` hold on every render;
  - no `btn-primary`;
  - `IconPage` renders every key with its name, plus the Source and License text; `IconButtonPage`
    renders the K8 labels in order.
- [X] T012 Write `tests/Gallery.Web.Tests/TileOnPhotoTests.cs`:
  - with `TitleOnPhoto`:
    - the `.on-photo` `Title` is inside the cell;
    - it comes after the `.overlay` (when an Overlay is passed) and is NOT inside it;
    - there is no title line under the cell;
    - the `Caption` still renders after the cell;
  - `Id` renders on the root;
  - defaults equal omitted parameters byte for byte, for an image tile and an empty tile.

**Checkpoint (parent)**:
1. Apply test-impact A13 (`IndexTests` 37 → 39), A15 (`MoleculePageTests` TilePage cells 3 → 6) and A14 (`NewTabLinkTests`: icon counts as ↗;
   the amended test passes on the pre-change markup too).
2. Run `UiPagesUnchangedTests` once; it writes B7 (`atoms/icon.html`, `atoms/icon-button.html`).
3. Regenerate B1 (`index.html`) and B2 (`molecules/tile.html`), then run `baseline-diff.py`:
   EQUAL for both.
4. Look over B7 in a browser.
5. Run gate A, then commit.

## Phase 3: User Story 1 + 2: library tile with icon row and the edit toggle (P1) 🎯 MVP

US1 (icon row) and US2 (toggle) change the same files (`LibraryPage`, the same hrefs), so they
form one batch with story-labelled tasks.

**Goal**: every library tile has its title on the photo and gallery/view/edit icons. Edit and
gallery toggle the panel, and one photo is active at a time.

**Independent test**: spec US1 and US2 Independent Tests; quickstart "By hand" 1–3 and 5.

Batch B owns:
- `src/Gallery.Web/Components/Molecules/PhotoActions.razor`, `PhotoActions.razor.css`,
  `PhotoActions.cs` (new);
- `src/Gallery.Web/Ui/Pages/PhotoActionsPage.razor` (new) and `src/Gallery.Web/Ui/UiCatalog.cs`
  (one changed line);
- deletes `src/Gallery.Web/Components/Molecules/MembershipStatus.razor` and
  `src/Gallery.Web/Ui/Pages/MembershipStatusPage.razor`;
- `src/Gallery.Web/Admin/Pages/LibraryPage.razor` and `LibraryPage.razor.css`;
- `src/Gallery.Web/Components/Templates/AdminLayout.razor.css`;
- `src/Gallery.Web/wwwroot/admin-assets/library.js`;
- the new `tests/Gallery.Web.Tests/PhotoActionsTests.cs` and
  `tests/Gallery.Web.Tests/TileIconBrowserTests.cs`.

It runs in parallel with Batch C (disjoint files).

- [ ] T013 [P] [US1] Write `src/Gallery.Web/Components/Molecules/PhotoActions.cs`,
  `PhotoActions.razor` and `PhotoActions.razor.css` exactly per K4: the order, the labels, the
  unavailable view, one row that never wraps.
- [ ] T014 [US1] In `src/Gallery.Web/Ui/UiCatalog.cs`, replace the `MembershipStatus` line in
  place with the `PhotoActions` entry (K8). Write `src/Gallery.Web/Ui/Pages/PhotoActionsPage.razor`
  with the six K8 specimens. Delete `MembershipStatus.razor` and `MembershipStatusPage.razor`.
  Grep `src/` for `MembershipStatus`: there must be 0 hits.
- [ ] T015 [US1] [US2] In `src/Gallery.Web/Admin/Pages/LibraryPage.razor` and `.razor.css`, apply
  K5:
  - add `TitleOnPhoto` and `Id="t-{id}"` on each tile;
  - `RowFor` becomes `PhotoActions` with the open and close hrefs and `Active`;
  - remove `.lib-row`, `.lib-status` and the `MembershipStatus` use;
  - keep the panel's own `ViewFor`.
- [ ] T016 [US2] In `src/Gallery.Web/Components/Templates/AdminLayout.razor.css`, add K7 (sticky
  aside at ≥ 1200 px only). In `src/Gallery.Web/wwwroot/admin-assets/library.js`, add J1 (≤ 8
  lines, its own section, header comment updated, no sink).
- [ ] T017 [US1] Write `tests/Gallery.Web.Tests/PhotoActionsTests.cs`:
  - `MembershipOf`, `IconOf` and `CountLabel` for 0, 1, 2 and 37;
  - the markup order `pa-group`, `pa-view`, `pa-edit`, with exactly 3 children;
  - labels verbatim, including "View {title} (opens in a new tab)", "Edit {title}" and
    "Close the editor for {title}";
  - the view link has `target="_blank" rel="noopener"`, and an unavailable view is a span with no
    `href`;
  - `Active` puts `aria-current="true"` on BOTH group and edit, and on neither when false;
  - FR-020 source guard: `PhotoActions.*` contains no `/admin`, no `Gallery.Web.Library` and no
    `open=`;
  - over the HTTP host, for a library with photos in 0, 1 and 3 galleries:
    - each tile's `.pa-group` `data-icon` and `aria-label` (SC-002);
    - with `?open={b}`, exactly one tile has `aria-current`, and its edit href is
      `?page=1#t-{b}`; every other edit href is `?page=1&open={x}#t-{x}`.
- [ ] T018 [US1] [US2] Write `tests/Gallery.Web.Tests/TileIconBrowserTests.cs`
  (`[Collection("library-browser")]`, `[Trait("Category","Browser")]`, `LibraryBrowserFixture`;
  park the mouse at (0,0) before each navigation). At 1440, 800 and 390 px:
  - the title band's rect is inside the photo's `.pic .main img` rect, along its bottom edge
    (the bottoms match within 1 px), on landscape, portrait, square and 3:1 panorama photos;
  - the band does not overlap the select mark's rect;
  - `.title` has `scrollWidth > clientWidth` for a 60-character title, and `title` equals the
    text;
  - SC-004: render the band over pure white, pure black and #808080 photos. Plant three images
    whose placeholder data URI is a 1×1 PNG of that colour (wait for no main image: block the
    image host route), compute `getComputedStyle` colour and band background, alpha-composite
    over the photo colour, and assert contrast ≥ 4.5;
  - SC-001: `.photo-actions` height == `var(--tap)` (44) at every width; each icon button
    measures ≥ 44×44; the 3 buttons sit on one row (equal `top`);
  - tab order on one tile: `pa-group`, `pa-view` (skipped when unavailable), `pa-edit`;
  - SC-003 with JS on:
    - click A's pencil: the URL has `open=A`, the panel is visible, and exactly one
      `[aria-current="true"].pa-edit` (A's);
    - click B's folder: the panel shows B's Galleries field in the viewport, and only B is active;
    - click B's pencil: no `open`, no panel, 0 active;
  - SC-003 with JS off (a new context, `JavaScriptEnabled = false`): the same three steps;
  - at 1440 px after scrolling to the 40th tile and opening it, the panel's top is within the
    viewport (sticky); at 390 px after opening tile 40 (JS on), the `.combobox` top is within the
    viewport (J1);
  - the filled-button probe (copy `FilledButtonsJs`) gives `[]` at rest and `["Save"]` with a
    panel open and A active (FR-010).

**Checkpoint (parent)**:
1. Apply A1–A5, A11 and A12 with `test-impact-edit.py`.
2. Regenerate B1 (`index.html`: MembershipStatus → PhotoActions); write B7
   `molecules/photo-actions.html`; delete B6 `molecules/membership-status.html`.
3. Run `baseline-diff.py`, then gate B, then the quickstart by hand 1–3 and 5 on a scratch
   database (not `gallery_dev`), at 1440 and 390 px in the CDP browser.
4. Commit.

## Phase 4: User Story 4: the same tile in the gallery builder (P2)

**Goal**: builder tiles get the title on the photo and move/cover/remove/view icons, with every
behaviour unchanged.

**Independent test**: spec US4; quickstart "By hand" 4.

Batch C owns:
- `src/Gallery.Web/Components/Molecules/DragHandle.razor`, `DragHandle.razor.css`,
  `CoverMarker.razor`, `CoverMarker.razor.css`;
- `src/Gallery.Web/Components/Organisms/GalleryBuilder.razor` and `GalleryBuilder.razor.css`;
- `src/Gallery.Web/Ui/Pages/DragHandlePage.razor` and `CoverMarkerPage.razor` (append only);
- the new `tests/Gallery.Web.Tests/BuilderIconTests.cs` and
  `tests/Gallery.Web.Tests/BuilderIconBrowserTests.cs`.

It depends on Batch A (Icon, IconButton, Tile.TitleOnPhoto) and runs in parallel with Batch B.

- [ ] T019 [P] [US4] In `src/Gallery.Web/Components/Molecules/DragHandle.razor` and `.razor.css`,
  add the `Icon` mode per K6. The default output is byte-identical (the galleries list and the
  `drag-handle` baseline).
- [ ] T020 [P] [US4] In `src/Gallery.Web/Components/Molecules/CoverMarker.razor` and `.razor.css`,
  add the `Icon` mode per K6 (three states, the same form/name/value and the same `Type` rule).
  The default output is byte-identical (the K4 default tests stay green).
- [ ] T021 [US4] In `src/Gallery.Web/Components/Organisms/GalleryBuilder.razor` and `.razor.css`,
  apply K6:
  - `TitleOnPhoto` on the tiles;
  - `Icon="true"` on DragHandle and CoverMarker;
  - IconButton remove and view in `RemoveFor`/`ViewFor`;
  - the `b-ctl` CSS;
  - the order move, cover, remove, view.
  In `DragHandlePage.razor` append "Icon"; in `CoverMarkerPage.razor` append "Icon, unpinned",
  "Icon, automatic" and "Icon, pinned".
- [ ] T022 [US4] Write `tests/Gallery.Web.Tests/BuilderIconTests.cs`:
  - DragHandle Icon mode: same `button`, the `handle` class, `aria-label`, `aria-pressed` for
    Rest/Lifted, `data-icon="grip-vertical"`, no `&#x2807;`;
  - CoverMarker Icon mode for the three states: element, label, `aria-pressed`, Filled,
    `form`/`name`/`value`, and the same `type` as the word mode for each `ImageId`/`UnpinFormId`
    combination;
  - GalleryBuilder with the K6 parameters:
    - per tile, the `b-ctl` children in order (handle, cover-marker, `image-minus` button,
      `square-arrow-out-up-right` link);
    - the remove button's `type`/`form`/`name`/`value`/`aria-label`;
    - the view href and `target`;
    - the title in `.on-photo`.
- [ ] T023 [US4] Write `tests/Gallery.Web.Tests/BuilderIconBrowserTests.cs`
  (`library-browser` collection), at 1440 and 390 px:
  - the `b-ctl` height == 44 and the 4 icons are on one row;
  - the title band is inside the photo;
  - keyboard lift and move with the icon handle reorders and announces "Lifted. Position…";
  - pinning by clicking the star gives a filled star with `aria-pressed="true"`, and the stored
    `pinnedCover` changes;
  - unpinning returns to the automatic state;
  - remove takes the photo out (stored `items`);
  - the filled-button probe gives `["Save"]` (the builder at rest).

**Checkpoint (parent)**:
1. Apply A6–A10.
2. Regenerate B3 (`drag-handle.html`), B4 (`cover-marker.html`) and B5 (`gallery-builder.html`),
   then run `baseline-diff.py`: EQUAL for B3 and B4. For B5, review by eye and record it as a
   stated change.
3. Run gate C, then quickstart 4 on the scratch database.
4. Commit.

## Phase 5: Polish and cross-cutting (parent unless marked)

- [ ] T024 [P] Write `tests/Gallery.Web.Tests/IconRegistryGuardTests.cs` (executor; owns only that
  file). It scans `src/**/*.razor` and `*.cs` (excluding bin/obj) for `Icon="…"` and
  `Name="…"` on `<Icon`, plus `IconOf` results, and asserts the set of used names EQUALS the key
  set of `Icons.Paths` (FR-018: nothing unused and nothing unknown). It runs after B and C land.
- [X] T025 [P] VII.2 design-record update, scripted in
  `specs/015-tile-icon-controls/evidence/design-record-edit.py` (counts asserted). In
  `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md`:
  - the D-21 atoms list gains `Icon`, `IconButton` (feature 015: Lucide 1.70.0 icons, ISC,
    vendored as path strings, inline SVG in the text colour; an icon is added only when a page
    uses it);
  - D-21's `MembershipStatus` mention becomes `PhotoActions` (the library tile's
    gallery/view/edit icon row, replacing MembershipStatus);
  - the D-23 Library line gains "photo titles on the photo and a gallery/view/edit icon row,
    where edit and gallery toggle the side panel (feature 015)";
  - the D-23 Gallery builder line gains "per-tile controls as icons (feature 015)".
  Then run `assemble.sh` and `validate.sh`, tee to `evidence/design-validate.txt` (0 failures),
  and check that `git show --stat` lists `docs/design.md`.
- [ ] T026 Fold every executor `decisions_made` into `decisions.md`, plus one STATED-CHANGE row
  per A1–A14 and B1–B7 (A3 and A12 as requirement changes from clarify; B5 as a stated
  builder-markup change).
- [ ] T027 Write the 015 entry in `docs/roadmap.md`: status, evidence paths, the OWNER-ONLY review.
- [ ] T028 Run the evidence test (default path), then `p95.sh pre-timings.log tile-timings.log`
  (ALL PASS). Record the library page's transfer size before and after in `evidence/weight.txt`
  (one curl each against a LibraryHost, gzip size); it is measured, not gated (R-09).
- [ ] T029 Write `specs/015-tile-icon-controls/evidence/non-vacuity.sh` from the 014 script, with
  one break per guard:
  - [SAFETY] Tile `TitleOnPhoto` defaults to true → `PublicOutputUnchangedTests` red;
  - `PhotoActions` sets `Active` on every tile → `PhotoActionsTests` (exactly one active) red;
  - the close href keeps `open=` → the `PhotoActionsTests` href fact red;
  - `IconOf(One)` returns `folders` → `PhotoActionsTests` red;
  - an unavailable view renders a link → `PhotoActionsTests` red;
  - the band background is removed (transparent) → `TileIconBrowserTests` contrast red;
  - `.photo-actions` `flex-wrap: wrap` plus a 4th dummy button → the row-height fact red;
  - an extra unused key in `Icons.Paths` → `IconRegistryGuardTests` red;
  - the view icon without `target` → `NewTabLinkTests` red;
  - the CoverMarker Icon pinned state drops `aria-pressed` → `BuilderIconTests` red.
  Run `NV_DRYRUN=1`, then the real run. Each break must fail on its assertion. Tee to
  `non-vacuity.txt`.
- [ ] T030 Final gate:
  1. `dotnet clean`, then build with `-warnaserror` and the full suite including Evidence and
     Browser.
  2. `p95.sh` ALL PASS; `baseline-diff.py` EQUAL.
  3. `git checkout --` the earlier features' regenerated logs.
  4. Commit.
- [ ] T031 Merge to main (`--no-ff`, body per 014's) and push. Watch the deploy job go green.
  Then the production check, saved to `evidence/browser-prod.txt`:
  - unauthenticated probes: `/admin/library` and `/ui/atoms/icon` answer 302 to sign-in, and
    public `/g` is unchanged (200);
  - the signed-in pass (library, builder and the three new /ui pages at 390 and 1440 px, no
    horizontal scroll) runs only if the automation browser has an owner session. Otherwise it
    folds into O-01.
  Do not change data on production.
- [ ] T032 Create GitHub issues for the open OWNER item(s), as with 014.

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 Review the icon system in the component library (`/ui/atoms/icon`,
  `/ui/atoms/icon-button`, `/ui/molecules/photo-actions`), then on port.zacal.me toggle edit on
  two photos, open a photo through its folder icon, and pin a cover in a builder.

## Dependencies and execution order

- T001 → T002 → T003 → T004 → T005 → T006 (parent).
- Batch A (T007 ‖ T009; T007 → T008 → T010 → T011; T009 → T012) → parent checkpoint (A13, A14,
  B1, B2, B7).
- Batch B (T013 → T014 → T015 → T016 → T017 → T018) ‖ Batch C (T019 ‖ T020 → T021 → T022 → T023).
  Both need A. Their files are disjoint (`UiCatalog.cs` is B's only).
- Checkpoint B (A1–A5, A11, A12, B1, B6, B7) and checkpoint C (A6–A10, B3–B5): whichever batch
  lands first is checkpointed first. Gate on the merged tree.
- Polish: T024 and T025 in parallel; T026, T027, T028 → T029 (needs every guard) → T030 → T031
  → T032.

## Parallel example

```text
Wave 1: Batch A (Icons, Icon, IconButton, Tile.TitleOnPhoto, Icon/IconButton pages)
Wave 2: Batch B (PhotoActions, LibraryPage, sticky aside, J1, library tests)  ‖  Batch C (DragHandle/CoverMarker icon mode, GalleryBuilder, builder tests)
Wave 3: T024 (registry guard) ‖ T025 (design record); then evidence, non-vacuity, final gate
```

## Implementation strategy

- MVP = Phases 1–3: the icon atoms in the catalogue plus the library tile with the toggle. It is
  deployable on its own.
- US3 (catalogue review) is delivered across the phases: the Icon and IconButton pages in A, the
  PhotoActions page in B, and the builder specimens in C.
- Severable seams:
  - US4 builder (cut Batch C, test-impact A6–A10 and B3–B5);
  - the sticky aside and J1 (cut T016; the narrow-screen field-in-view falls back to the panel at
    the end of the page);
  - the MembershipStatus deletion (keep the component unused).
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
