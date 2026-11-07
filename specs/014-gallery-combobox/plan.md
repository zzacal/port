# Implementation Plan: Galleries Combobox in the Photo Edit Panel

**Branch**: `014-gallery-combobox` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/014-gallery-combobox/spec.md`

## Summary

The library side panel's read-only "In galleries" list becomes a token combobox, built as a reusable `TokenCombobox` molecule in the component library with its own `/ui` page (owner direction). Every gallery is
rendered as a hidden listbox option from data the page already reads, and `library.js` filters it
as the owner types, with a "+ {text}" row when the text matches no gallery title exactly. Picks and
removals become hidden `add`/`create`/`leave` fields on the panel's existing Save form. The
existing L2 route validates them all first, then applies them through the existing add, create and
(new sibling) remove gates in one operation scope. That scope gives one purge and one projection
for the membership changes; a save that also edits title or caption runs the existing text
scope first, so it purges twice.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), vanilla ES module `library.js`

**Primary Dependencies**: none new. Razor components via `RazorComponentResult`, MongoDB.Driver,
the existing Playwright test setup.

**Storage**: MongoDB `galleries`, `images`, `site.homeOrder`. No shape change (data-model.md).

**Testing**: xUnit with `LibraryHost` (Mongo + S3Proxy), `LibraryBrowserFixture` (Playwright,
headless Chrome), Category=Evidence timing test.

**Target Platform**: Azure App Service Linux behind Cloudflare (production), local dev.

**Project Type**: server-rendered web app (admin area).

**Performance Goals**: SC-002 save p95 < 1 s locally (from logs). SC-003 filter < 100 ms per
keystroke with 200 galleries (browser test, `performance.now()` around a typed key).

**Constraints**: no request per keystroke; no new script tag; no HTML injection sink in
`library.js` (existing guard); existing tests unmodified except test-impact (A).

**Scale/Scope**: single owner, tens to low hundreds of galleries.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I.6 CSRF on admin writes | PASS | Same L2 route; `AdminAntiforgeryFilter` on the `/admin` group (FR-016). |
| II.5 nothing public as a side effect | PASS [SAFETY] | Created galleries are private (`CreateAsync`). No new visibility write; the existing `LibrarySourceGuardTests` II.5 guard covers `Library/`. A non-vacuity task breaks it. |
| II.6 empty galleries not shown | PASS | Remove goes through the same pull path as B5. The projector already hides empty galleries. |
| II.2 / II.3 | N/A | No public page or address changes. |
| IV.1, IV.2, IV.4 timing | PASS | New `library.image.memberships` op plus existing db ops; evidence test and `p95.sh` (R-08). |
| IV purge on content change | PASS | One scope gives P-MEMBERS per touched gallery, identical to the bulk add/B5 sets (R-03). |
| V.1 traces to spec | PASS | |
| V.2 minimal deps | PASS | No package. |
| V.3 admin may need JS | PASS | Without JS, the panel shows saved tokens as links; Save still saves the text. |
| V.4 proportional effort | PASS | Extras beyond the literal request are listed below with benefit and cost. No endpoint and no count cap (R-04). The catalogue molecule is owner-requested (R-05). |
| VI official CLIs | N/A | No scaffolding. |
| VII.2 design is source of truth | DEVIATION-handled | Two departures from design.md: (1) the library panel gains gallery membership, beyond title and caption editing; (2) a molecule is extracted before it has a second consumer, by owner direction. Both get amendments in `specs/001-…/draft/02d-decisions-delivery.md` (D-21 molecule list and extraction rule, plus the Library line). Then `assemble.sh` and `validate.sh` run, as a task in this feature (decisions 12, 16). |
| VIII cost | N/A | |
| IX shared containers | PASS | The field lives inside the SidePanel's fields slot; chrome untouched. `AdminLayoutConsistencyBrowserTests` stays unmodified. |

Additions beyond the literal request (V.4):

| Addition | Benefit | Cost | Kept |
|---|---|---|---|
| Token removal (US3) | The tokens field would otherwise be half-editable, and removal would need a builder trip | ~30 lines JS, one service sibling | yes (owner kept it) |
| Public warning on pick | The only warning before a photo becomes visible | one line under the field (`WarningFormat`) | yes |
| Token links to builder | Keeps what the old list offered | none | yes |
| Reusable TokenCombobox molecule + /ui page | Owner-requested reuse; catalogue states visible at /ui | 1 molecule, 1 catalogue page, 3 stated test changes | yes (owner) |
| Count cap on add/create/leave | marginal (owner-only, CSRF) | | dropped |
| Duplicate-title refusal on create | marginal; G2/L3 allow duplicates | | dropped |

Post-design re-check: unchanged, all PASS or DEVIATION-handled.

## Project Structure

### Documentation (this feature)

```text
specs/014-gallery-combobox/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── decisions.md
├── contracts/
│   ├── http.md
│   ├── components.md
│   └── test-impact.md
├── evidence/            # p95.sh, membership-timings.log, gate.sh, non-vacuity.sh (tasks phase)
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Web/
├── Library/
│   ├── GalleryService.cs        # + RemoveOneAsync(gid, iid, scope); RemoveAsync delegates to it
│   ├── ImageService.cs          # + SavePanelAsync(iid, title, caption, MembershipEdit)
│   ├── MembershipEdit.cs        # new record + Normalise
│   └── LibraryEndpoints.cs      # SaveImage reads add/create/leave, calls SavePanelAsync;
│                                #   LibraryPageModel + EditAdd/EditCreate/EditLeave; RenderLibraryAsync passes them
├── Components/Molecules/
│   ├── TokenCombobox.razor      # new molecule (K1)
│   ├── TokenCombobox.razor.css
│   └── TokenCombobox.cs         # ComboToken, ComboOption, ComboPending, ComboMatch, Match()
├── Ui/
│   ├── UiCatalog.cs             # + entry after VisibilityToggle (K2)
│   └── Pages/TokenComboboxPage.razor   # 10 specimens (K2)
├── Admin/Pages/
│   ├── LibraryPage.razor        # <TokenCombobox …> replaces lib-in / GalleriesOf (K3)
│   └── LibraryPage.razor.css    # drop lib-in/lib-galleries/lib-none
└── wwwroot/admin-assets/library.js   # new "combobox (TokenCombobox molecule)" section

tests/Gallery.Web.Tests/
├── LibraryEditTests.cs, IndexTests.cs  # stated changes (test-impact A1, A2) only
├── TokenComboboxTests.cs               # new: Match() rules + molecule markup (HtmlRenderer)
├── LibraryMembershipEditTests.cs       # new: HTTP facts for add/create/leave, refusals, purge sets
├── GalleryFieldBrowserTests.cs         # new: keyboard/mouse flows, "+" rules, SC-003, SC-005
└── MembershipTimingEvidenceTests.cs    # new, Category=Evidence
```

**Structure Decision**: existing single web project. A generic molecule in the shared component
library (R-05, owner direction), consumed by the library page. The service logic sits in
`Library/` beside the gates it reuses.

## Complexity Tracking

None.
