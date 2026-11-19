# Feature Specification: Selection bar stays on top of the photos

**Feature Branch**: `019-selection-bar-layering`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "in the library, when images are selected (ticked), the control bar is floating and falls visually beneath the images. looks bad."

## Clarifications

### Session 2026-11-18

- Q: Should the bar's look change too (docked, or a card with a shadow aligned to the grid)? → A: No.
  "For now I just don't want it to be behind the images." The defect is that photos draw over the bar
  and take the mouse clicks meant for its buttons. The bar's look, offset and width stay as they are.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The selection bar is never behind the photos (Priority: P1)

The owner ticks one or more photos in the library and scrolls. The bar with the selected count and its
actions (Clear, Select all on this page, Add to gallery…, Delete…) stays pinned near the bottom of the
window. Today, wherever a photo scrolls past the bar, the photo, its title strip and its select circle
draw on top of the bar, and a mouse click there lands on the photo instead of the bar's button. After
this feature the bar is always in front of every photo and every one of its controls can be clicked,
at every width and scroll position.

**Why this priority**: This is the reported defect; the bar is how every bulk action starts.

**Independent Test**: Sign in locally, open the library, tick two photos, scroll so photos pass behind
the bar, then click each bar control at a spot with a photo underneath: each click reaches the bar.

**Acceptance Scenarios**:

1. **Given** two photos are ticked, **When** the owner scrolls so a row of photos passes behind the bar,
   **Then** the bar, its count and every action are drawn in front of the photos.
2. **Given** the same state, **When** the owner clicks "Clear", "Select all on this page",
   "Add to gallery…" or "Delete…" at a spot where a photo lies underneath, **Then** that control acts,
   and the photo's link and select circle do not.
3. **Given** the owner unticks every photo, **When** the selection count reaches 0, **Then** the bar
   disappears as today.

### Edge Cases

- At phone width the bar wraps to two rows (88 px today); both rows must be in front of the photos.
- The tile's select circle and title strip sit on the highest layer inside a tile; the bar must still
  be above them.
- Open dialogs (the "Add to gallery" picker, the bulk delete confirm) must still cover the bar, as the
  existing check that the bar is unreachable behind the open confirm requires.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While one or more photos are ticked in the library, the selection bar MUST be drawn in
  front of every photo and every part of a tile (picture, placeholder, title strip, select circle,
  icon row) at every width and scroll position.
- **FR-002**: A mouse click anywhere on a bar control MUST reach that control, never a tile underneath.
- **FR-003**: The upload page's batch footer is pinned the same way over photo previews; it MUST also be
  drawn in front of the previews and its controls MUST receive their clicks.
- **FR-004**: Modal dialogs opened from the bar (gallery picker, bulk delete confirm) MUST still cover
  the bar and keep it unreachable while open.
- **FR-005**: Nothing else about the bar changes: its look, position, width, wording and selection
  behaviour (count, Clear, Select all on this page, the selection cap, show at 1 / hide at 0, the two
  actions) stay as today.
- **FR-006**: A browser check MUST guard the fix. With photos ticked and scrolled behind the bar, it
  samples points across the full bar at 390, 800 and 1440 px, and every sample must land on the bar or
  one of its controls. It MUST fail on today's code.
- **FR-007**: Existing tests MUST pass unmodified.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With 2 photos ticked and photos behind the bar, 100% of sampled points across the bar
  land on the bar at 390, 800 and 1440 px (today photos take 33 of 40 samples at 1440 px and 35 of 40 at
  800 px).
- **SC-002**: On the upload page with a batch in progress, 100% of sampled points across the batch
  footer land on the footer.
- **SC-003**: The owner can click every bar control with photos behind it, at any width, first try.

## Assumptions

- **Observed cause** (measured 2026-11-18, `evidence/observed-before.txt`, `evidence/before-1440.png`):
  the bar is pinned to the window bottom but is not set to draw above anything, while each tile's
  picture, title strip and select circle are. Those draw over the bar wherever they meet it, and the
  topmost element also receives the click. Measured: 33/40 samples across the bar hit a photo at
  1440 px, 35/40 at 800 px, 0/40 at 390 px (that scroll position put a gap under the bar).
- Blast radius: 2 pinned bottom bars in the admin site, the library selection bar
  (`Components/Molecules/SelectionBar.razor.css:9`) and the upload batch footer
  (`Components/Organisms/BatchFooter.razor.css:3`); the second is inferred from identical rules, not
  observed live. The side panel (`AdminLayout.razor.css:78`) sits beside the grid, not over photos.
- The existing check at `tests/Gallery.Web.Tests/LibraryBrowserTests.cs:588-590` samples only the
  centre of one button, with a dialog open; it cannot see this defect, which is why FR-006 adds one.
- Constitution IX: the fix goes in the shared bar components, not in the library page.
- Out of scope (owner, clarification): the bar's floating look and its being wider than the photo grid.
- Severable seams: the batch footer fix (FR-003; cut it and the upload page keeps the overlap) and the
  regression check (FR-006; cut it and the overlap can return unnoticed).
- No timing instrumentation is added: presentation only, no server work.
- Feature 018 is fully merged into `main`, so this branch starts from `main`.
