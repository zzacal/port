# Research: Admin UI Components (005)

## R-01: Build on the feature 003 /ui infrastructure, unchanged in kind

**Decision**: Add admin entries to `UiCatalog` under four new levels (AdminAtoms, AdminMolecules,
AdminOrganisms, AdminTemplates) with slugs `admin/{atoms,molecules,organisms,templates}/<name>`.
Pages reuse `UiLayout` and `Specimen`. `AllPagesTests` covers every new page automatically
because it iterates the catalogue.
**Rationale**: FR-007 and FR-009. The routes, noindex, Development-only mapping and request timing
already work (feature 003, evidence/verify-output.txt), so the admin pages inherit FR-009 and
FR-011 with no new code path.
**Alternatives**: a separate `/ui/admin/` index was rejected: it means two catalogues and two
index tests (spec US1 wants one index). Putting admin entries under the existing levels was
rejected because public and admin would mix (spec FR-007 asks for grouping).

## R-02: Probe: boolean attribute rendering (run 2026-10-30)

A throwaway component rendered `checked`, `aria-pressed` and `open` from a C# bool through the
project's `HtmlRenderer` helper:
- true → `<x checked aria-pressed open>`
- false → `<x>`

**Decision**: Use `checked`/`disabled`/`open="@bool"` directly. For ARIA attributes, pass the
strings `"true"`/`"false"`, because a bare `aria-pressed` with no value is invalid. Recorded as
contract G3. Tests assert attribute absence for every false case (a false-case test is the one
that catches a `checked="False"` regression).

## R-03: Static rendering of dynamic states

**Decision**: States that are normally transient are passed in as parameters and drawn by CSS
classes: drag-over, dragging and drop slot, keyboard-lifted, uploading percent, busy and saving.
Progress uses the native `<progress>` element (determinate with `value`, indeterminate without).
The busy spinner is a CSS animation.
**Rationale**: Clarify answer 1-a (static, no scripts). Constitution V.3 allows scripts on admin
pages, but this feature adds none. Native `<progress>` and `<details>` give real semantics now and
keep working once scripts arrive (FR-005). A CSS animation is not a script, so
`Guards.AssertNoScript` still holds.
**Alternatives**: SVG progress rings (Uppy style) were rejected: more markup for the same
information, and a bar reads better under a letterboxed photo.

## R-04: Confirmation without scripts uses `<details>`

**Decision**: DangerConfirm is a `<details>` whose `<summary>` is the outlined danger trigger. The
body holds the consequence sentence and the real submit button.
**Rationale**: It works with no JS, keeps the control inside the item (P3), and names the target
and consequence. That combines Lychee's danger zone and Immich's naming from the reference board.
A modal `<dialog>` needs a script to open without a form round-trip.
**Alternatives**: a separate confirmation page was rejected: slower, and the owner loses context.
Type-the-name-to-confirm (Geist) was rejected for v1 as too heavy for single images. It is noted in
decisions.md as available for galleries later.

## R-05: Admin reuse of ContactSheet and Tile (FR-002)

**Decision**: Add optional `Overlay`, `CssClass` and `Caption` parameters to Tile, and make `Href`
optional (a `div` root when there is no link). Add optional `ChildContent` to ContactSheet so the
caller supplies cells such as Tiles, BatchItems or the drop slot. Without these parameters the
public output is byte-identical. It is guarded by the existing Tile and ContactSheet tests, which
must pass unmodified (SC-004).
**Rationale**: D-23 says admin grids reuse the public organisms. Grid columns and sizes stay single
sourced (D-20).
**Alternatives**: a separate AdminContactSheet was rejected because it would drift (the D-20
rationale).

## R-06: Scoped CSS across component boundaries

Markup inside a RenderFragment gets the scope of the component that authored it. The builder's
`.tl`/`.tr` overlay wrappers are therefore styled by GalleryBuilder's own CSS with no `::deep`.
Styling a child component's own element (Tile's `.cell` when `.is-selected`) needs `::deep` from
an ancestor in the caller's markup. Contract entries name each `::deep` use. Browser verification
(quickstart §3) measures these, because a wrong scope produces correct HTML with no visible
effect, which no HTML assertion can see.

## R-07: Gap-state designs (D-23 list)

Designed in contracts/admin-components.md and summarised in decisions.md, each with its source
pattern:
- claim and refusal: SignInCard, Immich/Flickr card over a photo
- processing and interrupted: BatchItem status strip, FilePond row plus Uppy marker
- keyboard reorder: DragHandle `aria-pressed` plus a live instruction line (no public example; WAI
  listbox-move convention)
- publish warning: inline GOV.UK warning-text shape under the switch, not a modal
- cover marker: a solid "Cover" label plus "Cover · first photo" for the automatic cover
- saving and saved: the Input foot line
- delete-image confirmation: DangerConfirm with consequence copy

## R-08: Tokens

**Decision**: Add ColorDanger `#b3261e`, ColorSuccess `#1e7a3c` and ColorSurface `#f4f4f4`.
**Rationale**: Danger and success text need contrast of at least 4.5:1 on white. Measured by
formula: #b3261e is 6.5:1 and #1e7a3c is 5.4:1, both pass AA. Surface is a neutral panel tint.
Values are owner-changeable defaults (spec Assumptions).

## R-09: Dependencies

None added. No htmx and no packages (static scope). AdminLayout's scripts arrive with the first
behaviour feature, and D-21 is amended to say so.
