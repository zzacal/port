# Tasks: Galleries Combobox in the Photo Edit Panel

**Input**: design documents in `specs/014-gallery-combobox/`: spec.md, plan.md, research.md (R-xx),
data-model.md, contracts/http.md, contracts/components.md (K1–K3), contracts/test-impact.md
(A1–A3, B1, C), quickstart.md, decisions.md.

**Source of truth**: the contracts and the research decisions. Implement exactly what they say. If
something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: required. The spec's SCs are measured checks, and the constitution requires [SAFETY]
tests and log-verifiable timing. Each batch writes its tests in the NEW test files it names, in
the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081.** Never kill or restart it. Never run
  `pkill`/`killall`, and never bind :5080 or :5081. Build and test into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s --filter <your classes>`
  - macOS has no `timeout`; use `--blame-hang-timeout`. Docker must be running (`docker info`; if
    not, `open -a Docker` and poll).
- **Existing tests must pass UNMODIFIED**, except test-impact A1–A3 and B1, which only the parent
  does. Any other existing-test failure means your change is wrong: fix the code, never the test,
  a fixture or a baseline. Executors never edit existing files under `tests/`, any file under
  `specs/`, or `specs/006-public-site-local/evidence/ui-baseline/`.
- **Never commit.** The parent commits after its own gate.
- **No new NuGet package, no new script file, no new `<script>` element, no `style=` attribute, no
  new design token, no HTML injection sink** (`innerHTML`, `outerHTML`, `insertAdjacentHTML`,
  `document.write`) in `library.js`. Use `createElement` and `textContent` only.
- **Keep existing public signatures.** `GalleryService.RemoveAsync`, `AddOneAsync`, `CreateAsync`,
  `ImageService.UpdateTextAsync` and the existing `LibraryPageModel` positional parameters keep
  their shapes. New behaviour goes in new members or in optional trailing parameters with
  defaults that reproduce today's output.
- **Names, verbatim**:
  - types `TokenCombobox`, `ComboToken`, `ComboOption`, `ComboPending`, `ComboMatch`,
    `MembershipEdit`;
  - methods `TokenCombobox.Match`, `GalleryService.RemoveOneAsync`, `ImageService.SavePanelAsync`,
    `MembershipEdit.Normalise`;
  - form fields `add`, `create`, `leave`;
  - CSS classes `combobox`, `cb-*` (as in K1);
  - catalogue slug `molecules/token-combobox`;
  - op `library.image.memberships` with outcomes `ok`, `refused`, `notfound`;
  - scope op `image.memberships`;
  - status text `That gallery no longer exists.`

  No other new log event or op name.
- **The molecule knows nothing about galleries** (FR-020). No "gallery" word, route or
  `Gallery.Web.Library` type appears in `TokenCombobox.*`.
- **Call, never copy**: `AddOneAsync`, `CreateAsync`, `PullAsync`, `GalleryService.TitleProblem`,
  `UpdateTextAsync`, `Begin`/`CompleteAsync` and `TokenCombobox.Match` are CALLED. A
  second copy of the add gate, the title rule, the purge logic or the match rules in C# is a
  defect. `library.js` mirrors `Match` by design, pinned by T015's parity test.
  - The panel's public warning comes from `WarningFormat` and `ComboOption.Warning`
    (contracts/components.md K3), NOT from `NoteFor`. `NoteFor` stays only in the bulk picker,
    untouched (FR-018).
- **File ownership**: each batch lists the files it owns. The parent owns everything under
  `specs/`, every EXISTING file under `tests/`, and `AppBuilder.cs`/`Program.cs` (untouched by
  this feature).
- **[SAFETY]** marks a task implementing II.5 (nothing public as a side effect: created galleries
  are private, no visibility write), the validate-before-write guarantee (FR-014), or I.6 CSRF.
  These are blocking and non-deferrable. Each gets a deliberate break in T024 that must turn its
  test red ON ITS ASSERTION.
- **Tight assertions**:
  - sets and lists are compared for EQUALITY;
  - "nothing written" means the stored `items`, `galleryIds`, `homeOrder` and gallery count are
    compared before and after;
  - "absent" checks look for an element or attribute, never a bare substring.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back to the parent.
- **Your final message must be ONLY the JSON report** (tasks_completed, build_succeeded,
  tests_passed, decisions_made, blockers).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [X] T001 Record the baseline in `specs/014-gallery-combobox/evidence/baseline.txt`:
  `git rev-parse HEAD`, the full-suite pass/fail/skip counts from
  `dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s`, and
  `ls tests/Gallery.Web.Tests/*.cs | wc -l`.
- [X] T002 Copy `specs/013-by-camera/evidence/gate.sh` to
  `specs/014-gallery-combobox/evidence/gate.sh`. Change the output path to
  `/tmp/014-gate-$L.txt` and add `specs/013-by-camera/evidence` to the restored dirs.
- [X] T003 Re-verify contracts/test-impact.md against the tree at T001 (parent, by hand). Grep
  `tests/` for `lib-galleries`, `lib-in`, `In galleries`, `Not in a gallery`, `UiCatalog.Entries`,
  `hrefs.Count` and `TakeLast(`, and confirm every A/C row line for line. Edit test-impact.md
  before Batch A if anything differs.

## Phase 2: Foundational: the TokenCombobox molecule (blocks every story)

Batch A. Owns `src/Gallery.Web/Components/Molecules/TokenCombobox.cs`, `TokenCombobox.razor`,
`TokenCombobox.razor.css`, `src/Gallery.Web/Ui/Pages/TokenComboboxPage.razor`,
`src/Gallery.Web/Ui/UiCatalog.cs` (one inserted line), and the new
`tests/Gallery.Web.Tests/TokenComboboxTests.cs`.

- [X] T004 [P] Write `src/Gallery.Web/Components/Molecules/TokenCombobox.cs`: the records and enum,
  and `public static ComboMatch Match(...)`, exactly per contracts/components.md K1 "Value types".
  It is a pure function with no I/O.
- [X] T005 Write `src/Gallery.Web/Components/Molecules/TokenCombobox.razor` and
  `TokenCombobox.razor.css`, per K1 "Parameters", "Markup" and "CSS" (the three `<template>`s,
  the `cb-empty`/`cb-msg`/`cb-warn`/`cb-status` lines, the visually-hidden help text, the
  component's own `.vh` rule, no `placeholder`):
  - the header comment names `admin-assets/library.js` as the behaviour script a consumer page
    must load;
  - with `Query` set, the shown rows, their order, the create row, the message, the status,
    `aria-expanded` and the highlighted row (row 0 unless `Highlight` overrides it;
    `aria-selected`/`aria-activedescendant`) all come from `Match` (no second copy of the rules);
  - the Input atom's border/radius/focus values are copied as token references from
    `Components/Atoms/Input.razor.css` (same `var(--…)` names).
- [X] T006 Write `src/Gallery.Web/Ui/Pages/TokenComboboxPage.razor` with the 10 specimens of K2
  (labels verbatim, `Width="w-480"`). Insert the catalogue entry in
  `src/Gallery.Web/Ui/UiCatalog.cs` directly after the `VisibilityToggle` line.
- [X] T007 Write `tests/Gallery.Web.Tests/TokenComboboxTests.cs`:
  - `Match` theory rows:
    - empty and whitespace queries;
    - a case-insensitive substring;
    - taken ids excluded;
    - ranking: query `beach` over "Alpine beach", "Beach", "Beaches" gives Beach, Beaches,
      Alpine beach (exact, prefix, contains); within a rank, sorted by title;
    - the `maxShown` cut (12 matching options → 10);
    - an exact match hides create, including ` Coast ` with case and spaces differing;
    - a pending `Create` token counts as exact;
    - `q.Length > createMax` gives the message and no create;
    - `allowCreate=false` gives no create;
    - Status is `"No matches"`, `"1 option"` or `"{n} options"` (the create row counts), and
      `"Already added"` when nothing is shown and the text is an exact match.
  - Markup facts rendered with `Render.Html<TokenCombobox>`:
    - every option is rendered, all `hidden`, without `Query`;
    - with `Query`, exactly `Match.Shown` is visible, in its order;
    - `role="combobox"` with `aria-controls` pointing at the listbox id, and `role="option"`
      ids unique;
    - the text input has no `name`;
    - `Removed` ids become hidden inputs with `RemoveName`;
    - pending tokens carry `is-pending` and a hidden input named `AddName`/`CreateName`;
    - the × `aria-label` is `string.Format(RemoveLabel, Label)`;
    - no `placeholder` attribute anywhere; `cb-empty` shows `EmptyText` only when there are no
      tokens;
    - the input's `aria-describedby` points at the help text "Type to search. Backspace removes
      the last one.";
    - the three templates are present, and their hidden inputs carry `AddName`, `CreateName` and
      `RemoveName` (and `form` when `FormId` is set);
    - the root carries `data-add-name`, `data-create-name`, `data-remove-name` and
      `data-remove-label` with their values;
    - `cb-warn` lists `WarningFormat(option.Warning)` for each pending `Add` token whose option
      has a Warning;
    - `Guards.AssertNoInlineStyle` and `Guards.AssertNoScript` hold;
    - no `btn-primary` class.
  - FR-020 source guard: after deleting every `Gallery.Web.` namespace prefix,
    `TokenCombobox.cs`/`.razor`/`.razor.css` match no `galler(y|ies)` (case-insensitive) and no
    `Library`. (The prefix strip is needed because the namespace `Gallery.Web.Components.Molecules`
    itself contains "Gallery".)
  - Catalogue: the page renders 10 `specimen-label`s with the K2 labels, in order.

**Checkpoint (parent)**:
1. Apply test-impact A2: `IndexTests.cs:16` 36 → 37.
2. Run `UiPagesUnchangedTests` once. It writes B1
   (`ui-baseline/molecules/token-combobox.html`) and fails "missing".
3. Review B1 against K2 by eye.
4. Regenerate A3 (the index baseline): delete `ui-baseline/index.html`, run the test to write it,
   then `git diff` it. The ONLY change allowed is the one TokenCombobox `li`.
5. Run the full gate (`bash specs/014-gallery-combobox/evidence/gate.sh A`), then commit.

## Phase 3: User Story 1 + 2: add to existing and create from typed text (P1) 🎯 MVP

US1 and US2 share every file (the same L2 save, the same field), so they form one batch with
story-labelled tasks.

**Goal**: in the photo panel, type, pick an existing gallery or "+ text", Save, and the photo is
in it. New galleries are private.

**Independent test**: spec US1/US2 Independent Tests; quickstart "By hand" steps 1–4 and 6.

Batch B (server). Owns `src/Gallery.Web/Library/MembershipEdit.cs` (new),
`src/Gallery.Web/Library/ImageService.cs`, `src/Gallery.Web/Library/GalleryService.cs`,
`src/Gallery.Web/Library/LibraryEndpoints.cs`, and the new
`tests/Gallery.Web.Tests/LibraryMembershipEditTests.cs`.

- [X] T008 [P] [US1] Write `src/Gallery.Web/Library/MembershipEdit.cs`:
  - `public sealed record MembershipEdit(IReadOnlyList<string> Add, IReadOnlyList<string> Create, IReadOnlyList<string> Leave)`
    with `IsEmpty`;
  - `static MembershipEdit Normalise(add, create, leave)`, per data-model.md and research R-04:
    ordinal dedupe of ids; create titles trimmed, blank ones dropped, deduped
    `OrdinalIgnoreCase`; ids in both Add and Leave dropped from both.
- [X] T009 [US2] In `src/Gallery.Web/Library/GalleryService.cs`, add
  `public async Task<OperationResult> RemoveOneAsync(string gid, string iid, OperationScope scope)`
  by moving `RemoveAsync`'s body after the scope creation:
  - `RemoveOneAsync` loads the gallery, and returns NotFound / Ok with `Removed = 0` as today;
  - it calls `scope.Touch(snapshot, PurgeKind.Members)` only when it removes;
  - it calls `PullAsync` and the image `$pull`.

  `RemoveAsync` becomes `Begin("gallery.remove")` → `RemoveOneAsync` → `CompleteAsync` →
  return. Its output, log lines and purge set must be identical; `BuilderTests` stay green
  unmodified. This is used by US3, but it lives here because the batch owns the file.
- [X] T010 [US1] [SAFETY] In `src/Gallery.Web/Library/ImageService.cs`, add
  `public Task<OperationResult> SavePanelAsync(string iid, string? title, string? caption, MembershipEdit edit)`,
  exactly per contracts/http.md "Processing order" steps 1–7:
  - validate everything before any write: text, the image, every create title through
    `GalleryService.TitleProblem`, and every add/leave id with `OpaqueId.IsValid` plus ONE
    `db.gallery.load` find with `Filter.In`;
  - a missing gallery is `OperationResult.Refused(404, "That gallery no longer exists.")`;
  - an empty edit is exactly `UpdateTextAsync(iid, title, caption)`;
  - otherwise `UpdateTextAsync`, then `timer.Time("library.image.memberships", …)` around ONE
    `galleries.Begin("image.memberships")` scope, in the order leave → add → create.
  - Each create is `CreateAsync(title)`, then `AddOneAsync(newId, iid, scope)`.
  - The outcome is logged as `ok`, `refused` or `notfound`. A bad create title or text is
    `refused`; a missing or invalid gallery id or a missing photo is `notfound`, even though the
    gallery case returns `Refused(404, …)`. Both are logged with
    `OpTiming.Log(log, "library.image.memberships", 0, "refused"|"notfound")`. An empty edit
    logs no line.
  - No visibility write.
- [X] T011 [US1] In `src/Gallery.Web/Library/LibraryEndpoints.cs`:
  - add optional trailing params `IReadOnlyList<string>? EditAdd = null`, `EditCreate`,
    `EditLeave` to `LibraryPageModel`, and matching optional params to `RenderLibraryAsync`;
  - `SaveImage` reads the repeated `add`/`create`/`leave` with the existing `Ids` helper
    (create via `form["create"]`, keeping non-empty values), builds `MembershipEdit.Normalise`
    and calls `images.SavePanelAsync`;
  - keep the existing `switch`. Its `_ =>` branch re-renders with the panel open and passes
    `EditAdd/EditCreate/EditLeave` from the normalised edit only when the refusal is a 422.
    A 404 gallery refusal passes none, so the panel shows current memberships;
  - the routes and other handlers are unchanged.
- [X] T012 [US1] [SAFETY] Write `tests/Gallery.Web.Tests/LibraryMembershipEditTests.cs`
  (`[Collection("s3")]`, `LibraryHost`, modelled on `LibraryEditTests`):
  - `add=` an existing gallery → 303 `…&saved=1`; `items` and `galleryIds` gain the photo; the
    purge set EQUALS the bulk add's set for the same gallery and photo (compute it by running L3
    on a twin host or the same host before/after, whichever the existing tests use);
  - `create=Night walks` → one new gallery: `Visibility == "private"`, `Items == [iid]`, last in
    `homeOrder`; the create itself purges nothing (L-10), and the save's purge set EQUALS the
    one L3 produces for a bulk add into a new gallery (the new gallery page and its photo page).
    AMENDED at implement (decision 23): "empty purge set" contradicted calling `AddOneAsync`
    unchanged. [SAFETY II.5]
  - `create` + `add` + `title` in one POST → all applied; exactly one `library.image.memberships`
    `ok` line;
  - `create=` 201 chars → 422 with `"The title can be at most 200 characters."`; NOTHING
    written: title unchanged, gallery count, `homeOrder`, `items` and `galleryIds` all equal
    before and after. [SAFETY FR-014]
  - `add=` an unknown valid id plus `create=X` → 404, status `That gallery no longer exists.`,
    nothing written (same comparisons), X not created. The re-rendered panel's
    `ul.cb-tokens` hrefs EQUAL the photo's stored holders (title order), and no `is-pending`
    token is present. [SAFETY FR-014]
  - `add=not-an-id` → 404, same status, nothing written;
  - text refusal (blank title) with `add=` → 422, no membership change;
  - a POST with a session, `add=` and `create=` but NO antiforgery token → 400, and nothing is
    written. This follows the pattern of
    `LibraryAccessTests.With_a_session_and_no_antiforgery_token_every_POST_is_400_and_stores_nothing`.
    [SAFETY I.6]
  - an empty edit (title only) logs no `library.image.memberships` line;
  - `add` of an already-held gallery → 303, no duplicate in `items`.

Batch C (client and panel). Owns `src/Gallery.Web/Admin/Pages/LibraryPage.razor`,
`LibraryPage.razor.css`, `src/Gallery.Web/wwwroot/admin-assets/library.js`, and the new
`tests/Gallery.Web.Tests/GalleryFieldBrowserTests.cs`. It depends on Batch A (molecule) and
Batch B (model params).

- [ ] T013 [US1] In `src/Gallery.Web/Admin/Pages/LibraryPage.razor`, replace the `lib-in` div and
  the `GalleriesOf` fragment with the `<TokenCombobox …>` of K3, plus `TokensFor`/`GalleryOptions`
  exactly as K3 says. Remove the `.lib-in`, `.lib-galleries` and `.lib-none` rules from
  `LibraryPage.razor.css`. The bulk picker is untouched.
- [ ] T014 [US1] In `src/Gallery.Web/wwwroot/admin-assets/library.js`, add the section
  `// ── combobox (TokenCombobox molecule)` per K1 "Behaviour":
  - input filter with the first row highlighted;
  - keyboard and mousedown pick;
  - pick(option)/pick(new) by cloning the component's templates;
  - token remove with focus moving to the next ×;
  - `cb-empty`/`cb-warn` refresh;
  - status announcements, and "Not added" on blur. The rules
  mirror `TokenCombobox.Match`. Update the file's header comment to list the combobox. Add no
  injection sink.
- [ ] T015 [US1] Write `tests/Gallery.Web.Tests/GalleryFieldBrowserTests.cs`
  (`[Collection("library-browser")]`, `[Trait("Category","Browser")]`, `LibraryBrowserFixture`):
  - US1 keyboard only (SC-005): open `?open={id}`, focus the input with Tab, type `coa`, Enter
    (the first row is highlighted, so Enter takes it), then Tab to Save and Enter. The stored `items` hold the photo, and the reloaded panel
    shows the token as a link;
  - US1 mouse: click an option, and a pending token appears (`.cb-token.is-pending`). Its
    computed `border-style` is `dashed` and a saved token's is not, which proves the cloned
    template carries the component's CSS scope (analyze H1);
  - the public option is one line "Coast · Public"; picking it shows the `cb-warn` line
    "Coast is public: visitors will see this photo after you save.", and removing the pending
    token hides it;
  - announcements: `.cb-status` reads "2 options" after typing `coa` (Coastline + create), and
    "Added Coastline" after the pick;
  - the × hit box measures ≥ 44×44 (`getBoundingClientRect`), and a 60-character label is
    truncated (`scrollWidth > clientWidth`) with `title` equal to the full label;
  - Esc closes the list and does not close or submit the panel;
  - parity (decision 14): with options Alpine/Birds/Coast/Coastline/Desert/Harbour and token
    Coast, plus options "Alpine beach", "Beach" and "Beaches", for each query `"coa"`,
    `"COAST"`, `" desert "`, `"x"`, `"a"`, `"beach"`:
    - the visible row texts, in DOM order, EQUAL the strings from `TokenCombobox.Match`
      computed in the test;
    - the highlighted row is the first of them;
    - the `.cb-status` text EQUALS `Match(...).Status` (L6);
  - SC-003: seed 200 galleries, type one character with `page.Keyboard.TypeAsync`, and measure
    `performance.now()` from keydown to the next `requestAnimationFrame` in-page. It must be
    < 100 ms; take the median of 5 keys;
  - the panel's filled buttons are exactly `["Save"]` (reuse the `FilledButtonsJs` approach by
    copying the probe into this file, because the original is private).
- [ ] T016 [US2] Add to `GalleryFieldBrowserTests.cs`:
  - type `Night walks`, and the last row reads `+ Night walks`;
  - pick it and Save, and one private gallery "Night walks" exists holding the photo;
  - type ` coast ` with no `+` row;
  - type 201 chars: message shown, no `+` row;
  - pick `+ X`, type `X` again, and no second `+ X` is offered.

**Checkpoint (parent)**:
1. Apply test-impact A1 (`LibraryEditTests.cs:184-186` selector `lib-galleries` →
   `cb-tokens`).
2. Run the gate (C).
3. Run quickstart "By hand" 1–4 and 6 on a scratch database (not `gallery_dev`).
4. Commit.

## Phase 4: User Story 3: remove a gallery token (P2)

Batch D. Owns only additions to the two new test files. The code already landed in T009, T010
and T014.

- [ ] T017 [US3] Add to `LibraryMembershipEditTests.cs`:
  - `leave=` a holding gallery → the photo is out of `items` and `galleryIds`, and
    `pinnedCover` is cleared when it was the cover; the purge set EQUALS B5's for the same pair;
  - a public gallery whose last photo is removed stays `public` and drops out of `/g` after
    settle (II.6);
  - `leave` + `add` of the same id → no change (R-04).
- [ ] T018 [US3] Add to `GalleryFieldBrowserTests.cs`:
  - click × on a saved token, Save, and the gallery no longer holds the photo; the photo is still
    in the library;
  - Backspace in the empty input removes the last token;
  - remove a saved token then re-pick it, and no `leave` input remains and Save changes nothing;
  - the keyboard-only remove: Tab to ×, Enter.

## Phase 5: Polish and cross-cutting (parent unless marked)

- [X] T019 [P] Write `tests/Gallery.Web.Tests/MembershipTimingEvidenceTests.cs`
  (Category=Evidence, modelled on `LibraryTimingEvidenceTests`):
  - 100 L2 saves, each with one `create` and one `add`;
  - assert ≥ 100 `op.timing library.image.memberships … ok` lines;
  - write the host log to `specs/014-gallery-combobox/evidence/membership-timings.log`.

  Executor; owns that new file only.
- [X] T020 [P] Write `specs/014-gallery-combobox/evidence/p95.sh` (adapted from 013's):
  - rows: `library.image.memberships` p95 < 1000 ms, and
    `http.request POST /admin/library/images/{iid}` p95 < 1000 ms, with n ≥ 100 each (SC-002);
  - a missing or empty log FAILs;
  - tee the output to `p95-output.txt`.

  Self-test on an empty log (must fail) and an over-limit value.
- [X] T021 [P] VII.2 design-record update, scripted in
  `specs/014-gallery-combobox/evidence/design-record-edit.py`, which asserts each old string's
  count before writing (decisions 12, 16). In
  `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md`:
  - the D-21 admin molecules list gains `TokenCombobox` (feature 014, a generic multi-value
    combobox);
  - "A component is extracted only when it has a second consumer" gains "or when the owner
    designates it reusable (feature 014)";
  - the Library line "title and caption editing in the side panel" gains "and gallery
    membership through a TokenCombobox".

  Then run `assemble.sh` and `validate.sh`, teeing to `evidence/design-validate.txt` (0
  failures). `git show --stat` must list `docs/design.md`.
- [ ] T022 Fold every executor `decisions_made` into `specs/014-gallery-combobox/decisions.md`,
  plus one STATED-CHANGE row each for A1, A2, A3 and B1. The A3 row names its precedent:
  feature 010 regenerated `ui-baseline/index.html` as a stated change when it added catalogue
  entries (commits 201d72c, 158dc3f). This is the same case: a new entry, not a changed component.
- [ ] T023 Write `docs/roadmap.md` 014 entry (status, evidence paths, OWNER-ONLY check).
- [ ] T024 Write `specs/014-gallery-combobox/evidence/non-vacuity.sh` from speckit-orchestration
  `templates/non-vacuity.sh`, with one break per [SAFETY] guard:
  - `SavePanelAsync` creates before validating add ids (move step 5 after step 7) → T012
    "unknown add + create" red;
  - drop the create-title check (step 4) → T012 "201 chars" red;
  - `CreateAsync` sets `Visibility = "public"` → T012 private-create fact red (and the existing
    II.5 guard);
  - in `AdminAntiforgeryFilter.InvokeAsync`, skip validation when the form carries an `add`
    field (`httpContext.Request.HasFormContentType && (await httpContext.Request.ReadFormAsync()).ContainsKey("add")`)
    → T012's CSRF fact goes red on its 400 assertion. Every existing CSRF test (which sends no
    `add`) stays green, which proves the new test is the one guarding this route;
  - `Match` ignores taken ids → T007 and T015 parity red;
  - `library.js` drops the rank step (title order only) → T015 parity red on `"beach"`;
  - `library.js` pick uses `innerHTML` → `LibrarySourceGuardTests` red.

  Dry-run with `NV_DRYRUN=1`, then run it for real. Each break must fail on its assertion. Tee
  to `non-vacuity.txt`.
- [ ] T025 Final gate:
  1. `dotnet clean`, then build `-warnaserror`, then run the full suite including Evidence.
  2. `p95.sh` ALL PASS.
  3. `git checkout --` the earlier features' regenerated logs.
  4. Commit.
- [ ] T026 Merge to main and push (parent). Watch the deploy job go green. Then the production
  browser pass on port.zacal.me at desktop and phone widths, saved to
  `evidence/browser-prod.txt`:
  - `/ui/molecules/token-combobox` renders 10 specimens;
  - the library panel shows the field;
  - no horizontal scroll.

  Do not create galleries on production; the owner does that in O-01.

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 On port.zacal.me, open a photo's Edit panel, add it to an existing gallery and to a new
  one, then remove one token. Look over `/ui/molecules/token-combobox`.

## Dependencies and execution order

- T001 → T002 → T003 (parent) → Batch A (T004 ‖ start of T005; T005 → T006 → T007) → parent
  checkpoint (A2, A3, B1).
- Batch B (T008 ‖ T009; then T010 → T011 → T012) can be dispatched in parallel with Batch A:
  the two own disjoint files.
- Batch C (T013 → T014 → T015 → T016) needs both A and B → parent checkpoint (A1).
- Batch D (T017 ‖ T018) after C.
- Polish: T019, T020 and T021 in parallel; then T022, T023 → T024 (needs every guard) → T025 →
  T026.

## Parallel example

```text
Wave 1: Batch A (molecule + catalogue)  ‖  Batch B (MembershipEdit, RemoveOneAsync, SavePanelAsync, L2, HTTP tests)
Wave 2: Batch C (panel wiring, library.js, browser tests)
Wave 3: Batch D (US3 tests)  ‖  T019 (timing evidence) ‖ T020 (p95.sh) ‖ T021 (design record)
```

## Implementation strategy

- MVP = Phases 1–3: the reusable molecule in the catalogue, plus add-to-existing and
  create-from-text in the panel. It is deployable on its own. US3's removal code already
  exists after T009 and T014; Phase 4 only adds its tests.
- Severable seams:
  - token removal (cut: drop the × and `leave`; T009 still lands because it is harmless);
  - the catalogue page (cut: drop T006's page and catalogue line and A2, A3, B1; the molecule
    stays reusable).
- Run `/speckit-implement` in a fresh session seeded from these artifacts.
