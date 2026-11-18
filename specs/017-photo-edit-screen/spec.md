# Feature Specification: Focused Photo Edit Screen

**Feature Branch**: `017-photo-edit-screen`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "admin edit workflow improvement. clicking on image metadata edit should open a focused screen similar to the gallery edit where the user is presented with a focused view of just the item being edited. the view should just be of the one image (not a thumbnail) and the edit dialog"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit a photo on its own screen (Priority: P1)

In the library, the owner clicks a photo's edit icon. Instead of a side panel opening beside the
library grid, a screen for that one photo opens, the way a gallery's edit icon opens that
gallery's own screen. The screen shows the whole photo large (not a grid thumbnail) next to the
edit form with the same fields as today: title, caption, galleries, the View link to the public
photo page, and Delete photo. No other library photos are visible on it.

**Why this priority**: This is the request. The side panel shares the screen with a grid of
other photos, and the photo being edited appears only as a small tile somewhere in that grid.

**Independent Test**: Sign in, open the library, click a photo's edit icon. A screen opens with
that photo shown large, the edit form beside it, and no other photo on the page; change the title
and save; the new title shows on the same screen.

**Acceptance Scenarios**:

1. **Given** the library page, **When** the owner clicks a photo's edit icon, **Then** the
   browser goes to that photo's edit screen, which shows exactly one photo (the one being
   edited) at a large size, plus the edit form.
2. **Given** the photo's edit screen, **When** the owner changes the title, caption or galleries
   and saves, **Then** the changes are stored exactly as the side panel stores them today, and
   the owner stays on the same edit screen with the saved state shown.
3. **Given** the photo's edit screen, **When** a save is refused (for example an empty title),
   **Then** the same screen comes back with the reason shown and the owner's entries kept, and
   nothing is stored, as the side panel behaves today.
4. **Given** the photo's edit screen, **When** the owner confirms Delete photo, **Then** the
   photo is deleted as today and the owner lands on the library page they came from.
5. **Given** a portrait, a landscape and a small photo, **When** each one's edit screen is
   opened at a desktop width, **Then** the whole photo is visible without scrolling, uncropped,
   beside the form.

---

### User Story 2 - Get back to where I was in the library (Priority: P2)

From the edit screen, the owner returns to the library through the breadcrumb ("Library ›
{photo title}"), landing on the same library page they left, at the photo they edited.

**Why this priority**: The library is paged. Losing the place after each edit would make the
new screen slower than the panel it replaces when editing several photos in a row.

**Independent Test**: Go to library page 3, edit a photo, save, then click "Library" in the
breadcrumb: page 3 opens scrolled to that photo's tile.

**Acceptance Scenarios**:

1. **Given** the owner opened the edit screen from library page N, **When** they click the
   breadcrumb's "Library" link (before or after saving), **Then** library page N opens at that
   photo's tile.
2. **Given** the owner opened an edit screen directly (bookmark, typed address), **When** they
   click "Library", **Then** the first library page opens.

---

### Edge Cases

- The photo id is unknown, malformed or the photo was deleted: the edit screen answers not found,
  the same as other owner routes do for an unknown item.
- A signed-out visitor opens an edit screen address: the same refusal as every other owner page
  (redirect to sign-in), and the screen reveals nothing about the photo.
- A photo in no gallery: the screen shows the galleries field's "Not in a gallery" state and no
  View link, as the panel does today.
- A very wide panorama or a tall narrow portrait: the whole photo stays visible and uncropped;
  it is letterboxed inside its area rather than stretched.
- Phone width: the photo sits above the form, full width, with no horizontal scroll.
- A library address carrying the old "open this photo's panel" parameter (an old bookmark):
  shows the plain library page; no panel, no error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each library photo MUST have its own owner edit screen at a stable address that
  can be bookmarked and reloaded.
- **FR-002**: On every library tile, the edit icon MUST lead to that photo's edit screen. The
  gallery icon, which today opens the same panel, MUST lead to the same screen (its galleries
  field lives there).
- **FR-003**: The edit screen MUST show exactly one photo: the one being edited, rendered large
  from the largest stored display size suitable for the screen, never the grid thumbnail
  size. The whole photo MUST be visible, uncropped and not upscaled beyond its stored
  dimensions, within the visible area at desktop and phone widths.
- **FR-004**: The edit screen MUST present the same edit form the library side panel presents
  today, with the same fields, limits, validation, warnings and save, saved and refused states:
  title, caption, galleries (add existing, create private, remove; applied on Save), the View
  link to the public photo page when one exists, and the Delete photo confirmation.
- **FR-005**: Saving MUST go through the same rules and produce the same stored result, purges
  and timing events as today's panel save. Only where the owner lands afterwards changes: back
  on the photo's edit screen with the saved state shown.
- **FR-006**: A refused save MUST re-render the edit screen with the reason and the owner's
  entries kept and store nothing, as the panel does today.
- **FR-007**: Delete photo MUST behave as today and then return the owner to the library page
  they came from (the first page when unknown or when that page no longer exists).
- **FR-008**: The edit screen MUST show the admin breadcrumb "Library › {photo title}", with
  "Library" linking back to the library page the owner came from, at that photo's tile.
- **FR-009**: The library's side panel MUST be removed: the library page shows only its grid,
  selection bar, pager and dialogs. An old address that asked for the panel MUST render the
  plain library page.
- **FR-010**: Bulk actions on the library (select, Add to gallery, Delete) MUST be unchanged.
- **FR-011**: The edit screen MUST be owner-only, under the same sign-in and form-protection
  rules as every other owner page.
- **FR-012**: The edit screen MUST share the admin chrome: owner bar, breadcrumb row and the
  gap under it at the same position and height as every other admin page, and MUST join the
  measured chrome-consistency browser test.
- **FR-013**: The edit screen's request MUST be timed with the same request-timing and
  named-operation events as the other owner pages, and MUST read no more from the datastore
  than the library page reads today to show the open panel.
- **FR-014**: The design record's library description (which says edit and gallery toggle the
  side panel) MUST be amended in this feature to describe the edit screen.

### Key Entities

- **Photo edit screen**: an owner view of one library photo: the photo at display size, its
  editable title and caption, its gallery memberships, its public page link and its delete
  action, plus the library page to return to.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From any library page, opening a photo for editing takes one click and shows that
  photo and no other.
- **SC-002**: On the edit screen at 1440 px and 390 px widths, the photo's rendered size is at
  least 3 times the area of its library tile at the same width, and the whole photo is inside
  the visible area at 1440 px with no scrolling.
- **SC-003**: Every behaviour the side panel's existing tests prove (save, refusal, galleries,
  delete, timing) passes on the edit screen, with only the address and landing place changed.
- **SC-004**: After saving, one click on the breadcrumb returns the owner to the library page
  and tile they started from, in 100% of the browser test runs.
- **SC-005**: The edit screen's 95th-percentile server time is within 10% (plus 1 ms) of
  today's library page with the panel open, read from the timing log.
- **SC-006**: The chrome-consistency test passes with the edit screen included, at narrow and
  wide widths.

## Assumptions

- Current surface (read 2026-11-17 on main after the 016 merge): the library's edit and gallery
  icons share one link that toggles a side panel on the library page itself
  (`src/Gallery.Web/Admin/Pages/LibraryPage.razor:113-119`, panel at `:127-140`); saving
  redirects back to the library with the panel open (`src/Gallery.Web/Library/LibraryEndpoints.cs:147`).
  The library page is the only page that uses the admin layout's side panel slot.
- "Similar to the gallery edit": the galleries list's edit icon opens the gallery's own screen
  (`/admin/galleries/{id}`) with breadcrumb "Galleries › {title}" and the form beside the
  content. The photo screen mirrors that: its own address, breadcrumb, content plus form.
- Defaults chosen (clarify can overturn): the gallery icon also goes to the edit screen
  (FR-002); after save the owner stays on the edit screen (FR-005); the side panel is removed
  rather than kept as a second way to edit (FR-009), with no redirect for old panel addresses.
- Layout default: photo on the left, form on the right at desktop widths; stacked (photo above
  form) on phones. The exact proportions are a plan decision within FR-003 and constitution IX.
- Out of scope: previous/next photo navigation on the edit screen; an edit link from the gallery
  builder's photo tiles; showing camera metadata on the edit screen; editing from the public
  photo page. Each would be a separate request.
- The existing save, delete and membership rules are reused unchanged; only the page they
  render on and the redirect targets change. About 42 references to the panel address across
  14 test files will move; the plan's test-impact pass sorts them.
- The photo is shown from the stored display derivatives, never the original upload
  (constitution III.1: no original is served).
- Planning inputs only: the existing .NET server-rendered admin pages, shared components
  (photo picture, side panel form, galleries combobox, danger confirm) and admin scripts.
- Branch `017-photo-edit-screen` is created from `main`, which already holds all of 016.
