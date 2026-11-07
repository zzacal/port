# Feature Specification: Icon Controls on the Galleries List

**Feature Branch**: `016-galleries-list-icons`

**Created**: 2026-11-07

**Status**: Draft

**Input**: User description: "i was talking about the admin galleries list. it should have the same treatment. I like that edit action buttons are the right-most icon. update the galleries list so their action buttons inherit the icon style."

## Clarifications

### Session 2026-11-07

- Plan research (2026-11-07): FR-006 names extended to the two empty cases; FR-014 moved from the
  contact-sheet specimen (pinned byte-identical by the public-output guard) to the controls' own
  catalogue page.
- Q1: What replaces the list's Public switch? → A: an eye icon for public and a crossed-out eye
  for private. Hover text and accessible name: "Public. Make private" / "Private. Make public"
  (FR-006). The empty-private case keeps today's reason as its name.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Gallery cards with their actions as icons, edit last (Priority: P1)

The owner opens the galleries list. Each gallery card looks like the library and builder tiles that
feature 015 changed: the gallery's title sits on its cover photo's bottom edge, and under the
photo one row of icon controls replaces today's words. The edit control is the right-most icon on
every card. Each icon names itself on hover and to assistive technology, with the same look,
size, focus ring and states as the icons on the library and builder tiles.

**Why this priority**: This is the request: one look across all three owner grids, with edit in
the same place on every card.

**Independent Test**: Open the galleries list with a public gallery with photos, a private gallery
with photos, and an empty private gallery. Every card shows its title on the cover (or in the
empty card's blank cell). Under it is one row of icons with edit right-most. Nothing in the row is
a word link or a text button.

**Acceptance Scenarios**:

1. **Given** a gallery with photos, **When** the owner views its card, **Then** the row shows, left
   to right, move, visibility, view and edit, as icons of the same size and look as the builder's.
2. **Given** a gallery with no photos, **When** the owner views its card, **Then** the view icon
   shows as unavailable in its usual place, so edit stays right-most and in the same position as
   on every other card.
3. **Given** any card, **When** the owner hovers or focuses an icon, **Then** it shows its name
   ("Edit {title}", "View {title} (opens in a new tab)", "Move {title}", and "Public. Make
   private" / "Private. Make public" on the eye).
4. **Given** the edit icon, **When** the owner activates it, **Then** the gallery's builder opens,
   exactly as the "Edit" word link does today.

---

### User Story 2 - Publish and unpublish from the card, as today (Priority: P1)

The visibility control keeps today's behaviour: one activation publishes a private gallery that
has photos, or unpublishes a public one, at once, with no confirmation. An empty private gallery
cannot be published, and the card says why. Only the control's appearance changes.

**Why this priority**: Visibility is a public-isolation control (constitution II). Restyling it
must not change what it does or let a gallery become public by accident.

**Independent Test**: Activate the visibility control on a private gallery with photos: it becomes
public and the list shows it as public. Activate it again: it becomes private. On an empty private
gallery the control cannot be activated and its name gives the reason.

**Acceptance Scenarios**:

1. **Given** a private gallery with photos, **When** the owner activates its visibility control,
   **Then** the gallery becomes public in one step, and the card shows the public state.
2. **Given** a public gallery, **When** the owner activates its visibility control, **Then** it
   becomes private in one step.
3. **Given** an empty private gallery, **When** the owner tries to activate its visibility control,
   **Then** nothing changes, and the control's name says "Private. Add a photo before making this
   gallery public" (today's reason).
4. **Given** scripting is off, **When** the owner activates the visibility control, **Then** it
   still publishes or unpublishes.

---

### User Story 3 - Reorder and status unchanged (Priority: P2)

Dragging a card by its move icon, and keyboard lifting and moving, work exactly as today. The
gallery's status (public, private, hidden until it has a photo) stays visible on the card without
taking a word slot in the icon row.

**Why this priority**: Reorder decides the public listing order; it must keep working. The status
is how the owner tells galleries apart at a glance.

**Independent Test**: Drag the third card first and save; the stored order and the public listing
follow. Lift a card with the keyboard, move it and drop it; the position is announced as today.

**Acceptance Scenarios**:

1. **Given** the list, **When** the owner drags a card by its move icon or moves it by keyboard,
   **Then** the new order is saved, as today.
2. **Given** a private gallery, **When** the owner views the list, **Then** its card is still
   dimmed, and its state reads from the visibility icon and its name, without a separate status
   word.

---

### Edge Cases

- A public gallery that has become empty: visibility shows public, view is unavailable (there is no
  page to view), and its name says it is hidden until it has a photo.
- A long gallery title: it is cut off on one line on the cover and shown in full on hover, as on
  library tiles.
- Phone width: the icon row stays one row under each card.
- Exactly one gallery: the move icon is shown as today.
- A refused publish (the gallery emptied in another tab): the list shows today's refusal message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each galleries-list card MUST show the gallery's title on its cover photo's bottom
  edge, in the same band and with the same contrast as the library and builder tiles (feature 015).
  A card with no cover shows the title in the same place on its blank cell.
- **FR-002**: Under each card, one row MUST hold the card's controls as icons, in this order: move,
  visibility, view, edit. Edit MUST be the right-most control on every card.
- **FR-003**: Every control in the row MUST use the shared icon-control style from feature 015
  (size, tap area, colour, hover, focus ring, active ring, unavailable state). The page MUST NOT
  restyle it.
- **FR-004**: The edit icon MUST link to the gallery's builder, as the "Edit" link does today.
- **FR-005**: The view icon MUST open the gallery's public page in a new tab when the gallery has
  photos. Otherwise it MUST show as unavailable in the same position.
- **FR-006**: The visibility control MUST keep today's behaviour exactly: one activation publishes
  or unpublishes at once, with no confirmation, through the same form and server rule. An empty
  private gallery's control MUST be unavailable, and its name MUST give today's reason. The control
  MUST show an open eye for a public gallery and a crossed-out eye for a private one. Its name,
  shown on hover and given to assistive technology, MUST state the current state and what
  activating it does: "Public. Make private" or "Private. Make public". A public gallery with
  no photos is named "Public, hidden until it has a photo. Make private". An empty private
  gallery's control is unavailable and named "Private. Add a photo before making this gallery
  public" (today's reason, after the state).
- **FR-007**: The move control MUST keep today's drag and keyboard reorder behaviour, its hooks and
  its announcements.
- **FR-008**: The status words ("Public", "Private", "Public · hidden until it has a photo") MUST
  NOT take a slot in the icon row. The state MUST still be readable from the visibility icon and
  its name, and the private card stays dimmed.
- **FR-009**: Each icon MUST name itself on hover and to assistive technology. The view icon's name
  MUST say it opens in a new tab, and the "↗ means a new tab" rule MUST keep holding.
- **FR-010**: The row MUST be one row at every width and the same height on every card, whatever its
  parts (constitution IX.3).
- **FR-011**: Only icons the page actually uses MAY be added to the icon set, and they MUST come from
  the same free icon set at the same version as feature 015.
- **FR-012**: The galleries list MUST keep loading with the same number of data reads as today, and
  MUST meet the same timing budget rule as feature 015 (p95 within 10% + 1 ms of a pre-change
  capture), measured from logs.
- **FR-013**: Public pages MUST stay byte-identical.
- **FR-014**: The new card controls MUST have their own page in the component library, showing a
  public, a private, an empty-public and an empty-private card, so the owner can review them. The
  existing gallery-list specimen on the contact-sheet page stays as it is (it is pinned as a
  pre-admin reference).

### Key Entities

- **Gallery card**: one gallery on the list: cover (or blank cell), title, visibility (public or
  private), whether it has photos, and its position in the order. No new data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every card, at phone and desktop widths, the control row is one row of the same
  height, and edit is the right-most control.
- **SC-002**: Zero word links or text buttons remain in any card's control row.
- **SC-003**: Publishing, unpublishing, refusing an empty publish, and reordering by drag and by
  keyboard each give the same stored result as before the change, with scripting on and off.
- **SC-004**: The card title stays readable over white, black and mid-grey covers (contrast at least
  4.5:1).
- **SC-005**: The galleries list's server time at the 95th percentile stays within 10% (+1 ms) of
  the pre-change measurement, with the same number of data reads per request, read from logs.
- **SC-006**: Every visibility control shows the eye that matches the gallery's stored state, and
  its name matches that state (checked over public, private and empty-private galleries).

## Assumptions

- "Same treatment" means feature 015's library and builder tile treatment: title on the photo and
  one row of icon controls under it. Edit right-most matches both of 015's rows.
- Order default: move, visibility, view, edit, which mirrors the builder (move first, a state
  control, view, then the item's own action last). Overturnable in clarify.
- The status words leave the row (FR-008); the visibility icon carries the state. This follows 015's
  "minimum words" rule. Severable seam: keep the status as small text under the row.
- Visibility stays an immediate toggle with no confirmation, as today (009 owner decision). A
  restyled control that publishes on one click looks like any other icon, so its accessible name
  and hover text MUST state the current state and the action (FR-006).
- Reuse from 015: the IconButton and Icon atoms, the Tile's title-on-photo option, and DragHandle's
  icon mode, as they exist in `src/Gallery.Web/Components/`. The gallery list renders today in
  `src/Gallery.Web/Admin/Pages/GalleriesPage.razor:93-98` (`RowFor`, `ViewFor`). The empty card is
  drawn by the page (`CardFor`, line 87), not by Tile, so FR-001's blank-cell title needs a page or
  Tile change.
- The visibility switch today is the `VisibilityToggle` molecule (`Switch` with a hidden
  `gal-vis-{id}` form). Its other consumer is the builder's side panel, which stays a switch inside
  its Save form (feature 009 owner feedback: typed edits are never lost). Only the list's use changes.
- Severable seams beyond the literal request:
  - the title on the cover (FR-001): the request names the action buttons, and "same treatment"
    implies it. Cut: keep the title under the cover;
  - removing the status words (FR-008). Cut: keep them as text;
  - the catalogue page for the card controls (FR-014): needed for the component-library review.
- Open performance item: 015's SC-005 miss (issue #3) comes from the per-icon component. The list
  has at most a few dozen cards, so its rendering cost is far smaller, but FR-012 still needs a
  measured pre/post capture. Fixing #3 first would remove that risk for this page too.
- Stack (planning input only): .NET 10 Razor components, existing atoms; no new package.
