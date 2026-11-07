# Test Impact: 016

The source-only greps are in research.md R-09. Every existing test not listed under (A) must pass
unmodified.

## (A) Stated changes to existing tests

| # | Test | Change |
|---|---|---|
| A1 | `GalleryListTests.Card_statuses_dimming_View_and_Edit_links_and_switch_states` | The status span, the text View/Edit links, the `.gal-row` parts and `SwitchOf` become `GalleryActions` assertions: the eye or eye-off and its label per state; the view link `/g/{id}/{slug}` or the "No public page yet" span; the edit href, last; the visibility `form="gal-vis-{gid}"`. The dimming assertions are unchanged. Rename: `Card_states_dimming_and_icon_row`. |
| A2 | `GalleryListTests` helpers `SwitchOf` (and any `.gal-status` helper) | Replaced by a `VisibilityOf(row)` helper. `CardOf` is unchanged. |
| A3 | `BuilderTests:357-361` | The list status "Public · hidden until it has a photo" plus an enabled, checked switch become the eye label "Public, hidden until it has a photo. Make private" on an enabled submit button with no `name="public"`. |
| A4 | `PublishRefusalTests:207-218` (list half) | The disabled switch plus the visible reason hint become the unavailable eye-off span labelled "Private. Add a photo before making this gallery public". The builder half is unchanged. |
| A5 | `LibraryBrowserTests:238` | `.gal-row .handle` becomes `.gallery-actions .handle`. |
| A6 | `LibraryBrowserTests:404-424` | The switch click and its checked state become a click on the visibility button, then an eye/eye-off check. Publish and unpublish are still asserted from storage. |
| A7 | `LibraryBrowserTests:496` | Row selector `.gal-row` becomes `.gallery-actions`. |
| A8 | `IconTests` | `Keys` +2, two SHA-256 pins, `IconPage` count `8+8+1` → `10+10+1`, `IconButtonPage` `5 + Keys.Length` (follows Keys). |
| A9 | `IndexTests` | Catalogue count 39 → 40. |

## (B) UI baselines

| # | File | Change |
|---|---|---|
| B1 | `index.html` | +1 `li` (Gallery actions). |
| B2 | `molecules/gallery-actions.html` | new. |
| B3 | `atoms/icon.html`, `atoms/icon-button.html` | eye and eye-off specimens only. |
| B4 | `organisms/gallery-builder.html` | `cs-m3` → `cs-m2`; the `sizes` medium term goes from 3 columns to 2; the `b-grid` wrapper `div`. |
| B5 | every other page | byte-identical. Review with `baseline-diff.py` (copied from 015). |

## (C) Unchanged by design

- **The ContactSheet `cs-m3` assertions** (`OrganismPageTests`, `OrganismStatePageTests`,
  `CameraPageComponentTests`, `PageComponentTests:99`, `TileContactSheetAdditionsTests`). They
  render the catalogue and the public pages, whose counts don't change.
- **The "Make public" absence checks** (`GalleryListTests:218`, `BuilderTests:142`,
  `LibraryFlowTests:68`, `LibraryComponentAdditionsTests:205`, `MoleculeComponentTests:310`,
  `LibraryBrowserTests:451-454`). They guard against a confirm-step button whose visible text is
  "Make public".
  - The new eye carries "Private. Make public" in `aria-label` and `title`. Those are attributes,
    so the HTML string checks would match them. The checks run on:
    - the builder page: no GalleryActions there, so no conflict;
    - the list HTML (`GalleryListTests:218`): this one conflicts.
  - Resolution: A10 below.
- `NewTabLinkTests`, `AdminNavConsistencyTests`, `LayoutConsistencyBrowserTests`,
  `LibrarySourceGuardTests`, `PublicOutputUnchangedTests`, `IconRegistryGuardTests`,
  `PhotoActionsTests` (its CSS guard reads `PhotoActions.razor.css` only).

| # | Test | Change |
|---|---|---|
| A10 | `GalleryListTests:218-219` | `Assert.DoesNotContain("Make public", html)` and `"Keep private"` narrowed to visible text (the page's text outside attributes), which is what the confirm-step guard means. The eye's name is an attribute. |

## New tests

- `GalleryActionsTests` (component):
  - the label rule for 4 states;
  - order, with edit last;
  - the form id, plus the `public` field only when private;
  - unavailable view and visibility;
  - no `aria-pressed`;
  - the CSS guard (nowrap, `var(--tap)`, no 44px);
  - the catalogue page.
- `GalleryListBrowserTests` (Chromium):
  - publish and unpublish by click, JS on and JS off, checked against storage;
  - the title band contrast is at least 4.5:1;
  - the row is a single 44 px row with every target ≥ 44 × 44;
  - the edit control is right-most in every card;
  - keyboard reorder still works.
- `GridWidthBrowserTests` (SC-007):
  - widths 600, 700, 760, 900, 1199, 1200, 1300, 1440 and 1920;
  - the library (panel closed and open), the builder and the list;
  - every tile is at least as wide as its row's `scrollWidth`, the row is a single line, and
    every target is ≥ 44 px.
- `GalleryListTimingEvidenceTests` (Category=Evidence): 100 GETs with 30 galleries; p95 and
  db_reads from logs.
