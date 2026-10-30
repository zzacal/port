# Tasks: Admin UI Components

**Input**: Design documents in `specs/005-admin-ui-components/`
**Source of truth**: `contracts/admin-components.md`. Implement exactly what it says, with the
markup, classes, copy and states it gives. If something is missing or contradictory, stop and
report a blocker. Do not invent.

## Rules for every executor

- Read `contracts/admin-components.md` (the global rules G1–G8 plus your components) and the
  existing public components named in your task. Copy their style: the `@code` block at the
  bottom, `[Parameter, EditorRequired]` on required parameters, and a one-line `@*` comment at the
  top.
- **Existing tests must pass UNMODIFIED.** That covers every file in `tests/` that existed before
  this feature. The only allowed edit is the 14 → 32 count in `IndexTests.cs` (T026), which is a
  stated requirement change. A failure in `PublicOutputUnchangedTests` means your Tile or
  ContactSheet change altered public markup: fix the component, never the snapshot.
- Gate after every task:
  `dotnet build Gallery.slnx -warnaserror` (0 warnings) and `dotnet test --no-build` (all green).
- Never add a `style=` attribute, a `<script>`, a NuGet package, or a media query other than
  `min-width: 600px` / `min-width: 1200px`.
- Bool attributes follow contract G3. Every false case of `checked`/`disabled`/`open`/`aria-*` gets
  a test asserting the attribute is ABSENT, or `"false"` for aria-pressed/aria-busy.
- Escalate (stop and report) on any design question. Executors never change the contract.

## Phase 1: Setup

- [X] T001 Capture the public output baseline before any change:
  `specs/005-admin-ui-components/evidence/baseline/*.html` (5 pages), with the byte-identity
  guard in `tests/Gallery.Web.Tests/PublicOutputUnchangedTests.cs`. Done by the high tier and
  green on the unchanged tree.
- [X] T002 Add `ColorDanger "#b3261e"`, `ColorSuccess "#1e7a3c"` and `ColorSurface "#f4f4f4"` to
  `src/Gallery.Core/Design/Tokens.cs`:
  - append `--color-danger`, `--color-success` and `--color-surface` to `ToCssRoot()` after
    `--color-rule`
  - append three `("Color", …)` rows to `All()` after ColorRule
  - add swatch rules `.swatch-ColorDanger/.swatch-ColorSuccess/.swatch-ColorSurface` in
    `src/Gallery.Web/Ui/Pages/TokensPage.razor.css`, matching the existing swatch rules
  - add tests in a NEW file `tests/Gallery.Core.Tests/AdminTokensTests.cs`, asserting the three
    `--color-*:value` strings and the three names in `All()`

  `TokensTests.cs` is not edited.
- [X] T003 Create `src/Gallery.Web/Components/Admin/AdminTypes.cs` with the enums and the
  `Refusal` record exactly as in the contract under "Shared enums" (namespace
  `Gallery.Web.Components.Admin`). Add these lines to both `src/Gallery.Web/Components/_Imports.razor`
  and `src/Gallery.Web/Ui/Pages/_Imports.razor`:
  - `@using Gallery.Web.Components.Admin`
  - `@using Gallery.Web.Components.Admin.Atoms`
  - `@using Gallery.Web.Components.Admin.Molecules`
  - `@using Gallery.Web.Components.Admin.Organisms`
  - `@using Gallery.Web.Components.Admin.Templates`

  Create the four folders. If an empty namespace would break the build, add the `@using` lines as
  each folder gets its first component.

## Phase 2: Foundational (blocks US2/US3)

- [X] T004 Apply the "Public additions" section of the contract to
  `src/Gallery.Web/Components/Molecules/Tile.razor` and `Tile.razor.css`:
  - `Overlay`, `CssClass` and `Caption` parameters
  - `Href` becomes optional, with a `div` root when it is empty
  - `.cell{position:relative}`, `.overlay`

  Also apply the section to `src/Gallery.Web/Components/Organisms/ContactSheet.razor`: add
  `ChildContent` and remove `[EditorRequired]` from `Items`. When CssClass is null the root
  class attribute must be exactly `tile`, with no trailing space.

  Acceptance:
  - `PublicOutputUnchangedTests` passes UNMODIFIED, along with all existing tests
  - a new test file `tests/Gallery.Web.Tests/AdminGridAdditionsTests.cs` asserts:
    - Tile with Overlay renders `<div class="overlay">`
    - Tile with no Href renders a `div` root with no `href`
    - Tile with CssClass "is-selected" renders `class="tile is-selected"`
    - ContactSheet with ChildContent renders that content inside `div.cs`

## Phase 3: US1 + US2 atoms (P1)

- [ ] T005 [P] [US2] Button: `src/Gallery.Web/Components/Admin/Atoms/Button.razor` and `.razor.css`,
  per the contract.
- [ ] T006 [P] [US2] Input: `src/Gallery.Web/Components/Admin/Atoms/Input.razor` and `.razor.css`.
- [ ] T007 [P] [US2] Switch: `src/Gallery.Web/Components/Admin/Atoms/Switch.razor` and `.razor.css`.
- [ ] T008 [P] [US2] SelectMark: `src/Gallery.Web/Components/Admin/Atoms/SelectMark.razor` and
  `.razor.css`.
- [ ] T009 [US2] Add tests for T005–T008 in `tests/Gallery.Web.Tests/AdminAtomTests.cs`. They render
  each component directly with `Render.Html<T>` and cover:
  - Button: each variant class; Link renders `<a class="btn`; Summary renders `<summary class="btn`;
    Busy renders `aria-busy="true"`, `disabled` and `class="spinner"`; non-busy has no `aria-busy`
    and no `disabled`
  - Input: too long (Value 212 chars, MaxLength 200) renders `aria-invalid="true"` and
    "212 characters; the limit is 200."; a valid value has no `aria-invalid`; Saving gives
    "Saving…" and Saved gives "Saved"
  - Switch: checked versus absent; disabled versus absent; `role="switch"`
  - SelectMark: checked versus absent; the label text is visually hidden

## Phase 4: US2 molecules (P1)

- [ ] T010 [P] [US2] BatchItem, in `Components/Admin/Molecules/BatchItem.razor` and `.razor.css`
  (contract table for status per state).
- [ ] T011 [P] [US2] CoverMarker, in `Components/Admin/Molecules/CoverMarker.razor` and
  `.razor.css`.
- [ ] T012 [P] [US2] DragHandle, in `Components/Admin/Molecules/DragHandle.razor` and
  `.razor.css`.
- [ ] T013 [P] [US2] SelectionBar, in `Components/Admin/Molecules/SelectionBar.razor` and
  `.razor.css`.
- [ ] T014 [P] [US2] DangerConfirm, in `Components/Admin/Molecules/DangerConfirm.razor` and
  `.razor.css`.
- [ ] T015 [P] [US2] VisibilityToggle, in `Components/Admin/Molecules/VisibilityToggle.razor` and
  `.razor.css`.
- [ ] T016 [US2] Add tests in `tests/Gallery.Web.Tests/AdminMoleculeTests.cs`:
  - BatchItem: one test per state asserting the status text from the contract table; Restart and
    Delete appear ONLY for Failed and Interrupted (assert their absence for the other 4);
    Uploading renders `<progress` with `value="64"`; Processing renders `<progress` with no
    `value`; no Preview renders `fname-ph`
  - CoverMarker: the three states' texts
  - DragHandle: Lifted renders `aria-pressed="true"` plus "Position 3 of 12"; Rest renders
    `aria-pressed="false"` and no lift-help
  - SelectionBar: Count 0 renders an empty string; Count 3 renders "3 selected"
  - DangerConfirm: Open renders `open`, closed has no `open`; the consequence text is present
  - VisibilityToggle: Warning renders `role="alert"` and the title in the sentence; no Warning
    means no `publish-warning`

## Phase 5: US2/US3 organisms and template

- [ ] T017 [P] [US2] OwnerBar, in `Components/Admin/Organisms/OwnerBar.razor` and `.razor.css`.
- [ ] T018 [P] [US2] SidePanel, in `Components/Admin/Organisms/SidePanel.razor` and `.razor.css`.
- [ ] T019 [P] [US2] UploadDropzone, in `Components/Admin/Organisms/UploadDropzone.razor` and
  `.razor.css`.
- [ ] T020 [P] [US2] BatchFooter, in `Components/Admin/Organisms/BatchFooter.razor` and
  `.razor.css`.
- [ ] T021 [P] [US3] SignInCard, in `Components/Admin/Organisms/SignInCard.razor` and `.razor.css`.
- [ ] T022 [US3] GalleryBuilder, in `Components/Admin/Organisms/GalleryBuilder.razor` and
  `.razor.css`. Depends on T004, T011, T012, T015 and T018.
- [ ] T023 [US3] AdminLayout, in `Components/Admin/Templates/AdminLayout.razor` and `.razor.css`.
  Modelled on `Components/Templates/PublicLayout.razor`; depends on T017.
- [ ] T024 [US3] Add tests in `tests/Gallery.Web.Tests/AdminOrganismTests.cs`:
  - OwnerBar: current page gets `aria-current="page"` (exactly 1)
  - SidePanel: Saving makes the primary busy; Saved shows "Saved"
  - UploadDropzone: `accept="image/jpeg"`, "up to 20 MB"; Refused lists both reasons
  - BatchFooter: InProgress and Blocked have the primary `disabled` plus their reason text; Ready
    has the primary without `disabled`
  - SignInCard: each state's h1
  - GalleryBuilder: Dragging renders exactly one `drop-slot`; Empty renders the library link and
    no `class="cs`; at rest, exactly one "Cover" pinned tag
  - AdminLayout: exactly one `<style`, no `<script`, noindex, `class="owner-bar"`

## Phase 6: US1 + US2 + US3 /ui pages

- [ ] T025 [US1] Extend `src/Gallery.Web/Ui/UiCatalog.cs`:
  - add `AdminAtoms, AdminMolecules, AdminOrganisms, AdminTemplates` to `UiLevel` after
    Templates
  - append the 18 entries in contract order, with slugs per `contracts/http.md`
  - in `src/Gallery.Web/Ui/Pages/Index.razor`, render the group heading through a display-name
    switch ("Admin atoms", "Admin molecules", "Admin organisms", "Admin templates"; public levels
    unchanged)
- [ ] T026 [US1] Update the expected count in `tests/Gallery.Web.Tests/IndexTests.cs` from 14 to
  32, and add the four admin headings to the heading loop. This is the ONE permitted edit to an
  existing test: a requirement change, stated in SC-004.
- [ ] T027 [P] [US2] Admin atom and molecule pages in `src/Gallery.Web/Ui/Pages/Admin/` (10 pages:
  ButtonPage, InputPage, SwitchPage, SelectMarkPage, BatchItemPage, CoverMarkerPage,
  DragHandlePage, SelectionBarPage, DangerConfirmPage, VisibilityTogglePage). Each uses UiLayout
  and Specimen and shows exactly the labelled states listed for it in the contract. Use
  `Render.Samples`-equivalent injection (`@inject SampleCatalog Samples`) and the sample text in
  data-model.md. Page-only CSS (rows, overlay placement in specimens) goes in `<Page>.razor.css`.
- [ ] T028 [P] [US3] Admin organism and template pages in `src/Gallery.Web/Ui/Pages/Admin/`
  (8 pages: OwnerBarPage, SidePanelPage, UploadDropzonePage, BatchFooterPage, GalleryBuilderPage,
  SignInCardPage, AdminContactSheetPage, AdminLayoutPage). AdminLayoutPage renders AdminLayout
  itself, like PublicLayoutPage. AdminContactSheetPage shows the three contract states.
- [ ] T029 [US2] Add tests in `tests/Gallery.Web.Tests/AdminPageTests.cs`: one test per admin page
  asserting every state label from the contract appears. `AllPagesTests` already covers no-style
  and no-script for every catalogue entry.

## Phase 7: Polish and verification (high tier)

- [ ] T030 Amend D-21 in `specs/001-architecture-stack-design/draft/02d-decisions-delivery.md`:
  - admin atoms become Button, Input, Switch, SelectMark (TagChip removed)
  - admin molecules and organisms per this feature; TagEditor removed
  - AdminLayout scripts arrive with the first behaviour feature

  Then reassemble with `assemble.sh`, run the design `validate.sh` (0 failures), and add a
  decisions.md row in feature 001.
- [ ] T031 Write `specs/005-admin-ui-components/evidence/verify.sh`, adapted from 003's, with the
  index at 32, all 33 pages, a 404 check, SC-008 (20 requests to an admin page give 20 events),
  and Production 404 for admin pages. Run it and tee to `verify-output.txt`.
- [ ] T032 Browser review per quickstart §3 at 1440/1024/390. Record the measurements in
  `evidence/browser.md` and the shots in `evidence/shots/`, and fix defects.
- [ ] T033 Write `specs/005-admin-ui-components/decisions.md`: every gap-state design choice, every
  default, and every executor decision. Then commit.

## Dependencies

- T001 → T002, T003 → T004 → (T005–T008 ∥) → T009 → (T010–T015 ∥) → T016 → (T017–T021 ∥) → T022 →
  T023 → T024 → T025 → T026 → (T027 ∥ T028) → T029 → T030–T033
- Parallel executors own disjoint files. Shared files (`_Imports.razor`, `UiCatalog.cs`) are
  touched only in T003 and T025.

## Implementation strategy

The atoms are the MVP: they let the owner judge the visual language (buttons, inputs, switch)
early. Molecules and organisms build on them, and each phase ends with the gates green.
