# Feature Specification: Icon Controls on Library Photos

**Feature Branch**: `015-tile-icon-controls`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "overhaul the controls on each picture. the title should be directly on the picture at the bottom. below the picture the control bar should be icon buttons instead of words to save space. the gallery icon should just be an icon. The icon should indicate if the picture is (not in a gallery), (in 1 galler), or (in 2 or more galleries). the view button should be after the gallery button. The edit button should be last. The edit button has two modes. Enabled and active. The button is enabled by default and when clicked or tapped, it goes into active mode. Active mode opens the edit dialog. Interacting with the edit button again will turn the button to inactive and enabled. It also closes the edit dialog. Only one photo can be in active edit at a time. Research a performant icon system and designs. Use free ones if available. I also have gemini.google.com if and only if we need to invoke it create icons. Icon system needs to be in our component library for me to review"

## Clarifications

### Session 2026-11-06

- Q1: What does the gallery icon do when activated? → A: it opens the photo's edit panel with the
  Galleries field in view (FR-007). Like the edit icon, it toggles: activating it on the photo
  whose panel is open closes the panel.
- Q2: Which tiles change? → B: the library's photo tiles AND the gallery builder's photo tiles
  (FR-021, User Story 4). The galleries list cards keep their current look.
- Owner direction on the gallery icon's words: "simplify only icon and total count". The hover
  text and accessible name give only the count ("Not in a gallery", "In 1 gallery", "In 3
  galleries"); no gallery title and no public count (FR-006).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A compact photo tile: title on the photo, icons below (Priority: P1)

The owner scrolls the library. Each photo shows its title in one line across the bottom of the
photo itself, readable over light and dark photos. Under the photo, one row of three icon buttons
replaces today's words ("Not in a gallery" / gallery link, "Edit", "View ↗"), in this order:
gallery, view, edit. The gallery icon alone tells the owner whether the photo is in no gallery,
one gallery, or two or more. Each icon names itself on hover and to assistive technology.

**Why this priority**: This is the request: less space per tile and the membership state at a
glance.

**Independent Test**: Open the library with photos in zero, one and three galleries. Every tile
shows its title on the photo and exactly three icons below it in the fixed order, and the three
gallery states show three distinct icons.

**Acceptance Scenarios**:

1. **Given** a photo in no gallery, **When** the library renders, **Then** its gallery icon is the
   "in no gallery" icon and its accessible name says it is in no gallery.
2. **Given** a photo in exactly one gallery, **When** the library renders, **Then** its gallery icon
   is the "in one gallery" icon and its accessible name is "In 1 gallery".
3. **Given** a photo in three galleries, **When** the library renders, **Then** its gallery icon is
   the "in two or more galleries" icon and its accessible name is "In 3 galleries".
4. **Given** any photo, **When** the owner activates its gallery icon, **Then** the edit panel opens
   for that photo with the Galleries field in view, and that photo's edit icon is active.
5. **Given** any photo, **When** the library renders, **Then** the title sits inside the photo's
   own rectangle along its bottom edge (not the empty letterbox area of the cell), on one line,
   truncated with an ellipsis when long, and the full title is available on hover.
6. **Given** a photo with no public page, **When** the library renders, **Then** the view icon is
   shown in its place but unavailable (not a link), so the edit icon stays in the same position on
   every tile.

---

### User Story 2 - The edit button toggles the edit panel (Priority: P1)

The owner clicks or taps a tile's edit icon. It turns active (visibly pressed) and the edit panel
opens for that photo. Clicking the same icon again turns it back to its normal state and closes the
panel. Clicking another tile's edit icon moves the active state and the panel to that photo; at
most one tile is ever active.

**Why this priority**: The owner asked for the two modes and the toggle; it replaces a link that
could only open.

**Independent Test**: Click edit on photo A (panel opens on A, A active), click edit on B (panel on
B, only B active), click edit on B again (panel closed, no tile active).

**Acceptance Scenarios**:

1. **Given** no panel open, **When** the owner activates photo A's edit icon, **Then** the panel
   opens for A and A's edit icon is in the active state; no other tile's is.
2. **Given** the panel open for A, **When** the owner activates A's edit icon again, **Then** the
   panel closes, the page stays on the same library page and scroll position region, and A's icon
   returns to its normal state.
3. **Given** the panel open for A, **When** the owner activates B's edit icon, **Then** the panel
   shows B and only B's icon is active.
4. **Given** the panel open for A after a save ("Saved"), **When** the library reloads, **Then** A's
   icon is still active.
5. **Given** scripting is off, **When** the owner uses the edit icons, **Then** the same open, move
   and close behaviour works through ordinary navigation.

---

### User Story 3 - Review the icon system in the component library (Priority: P1)

The owner opens the component library and finds the icon system: every icon the site uses, each
with its name and size, the icon button in its states (normal, hover/focus, active, unavailable),
the three gallery-membership icons side by side, and the new tile with title-on-photo and its
control row in each state. The page notes where the icons come from and their licence.

**Why this priority**: The owner asked to review the icon system there before it is used.

**Independent Test**: Open the catalogue index; it lists the icon pages; each renders every icon
and state with its label, and the source and licence are named.

**Acceptance Scenarios**:

1. **Given** the catalogue, **When** the owner opens the icon page, **Then** every icon used by the
   site appears with its name, and the source set and its licence are stated.
2. **Given** the icon button page, **When** it renders, **Then** it shows the normal, active and
   unavailable states, and the focus look is visible on keyboard focus.
3. **Given** the library tile page, **When** it renders, **Then** it shows a tile in each gallery
   state, with a long title, without a public page, and with edit active.

---

### User Story 4 - The same tile in the gallery builder (Priority: P2)

The owner opens a gallery's builder. Each photo there also shows its title on the photo and a row of
icon buttons under it, replacing today's words. The builder's own controls (move, cover, remove,
view) become icons in the same style. There is no gallery icon and no edit toggle there, because the
builder has no photo edit panel.

**Why this priority**: The owner wants one look across both photo grids; the library is the core
request.

**Independent Test**: Open a builder with three photos (one pinned cover). Every tile shows its
title on the photo and one icon row in a fixed order; each icon names its action on hover and to
assistive technology; drag, keyboard move, cover and remove still work as before.

**Acceptance Scenarios**:

1. **Given** a builder tile, **When** it renders, **Then** the title is on the photo (as FR-001) and
   the controls are icon buttons in this order: move, cover, remove, view.
2. **Given** the pinned cover photo, **When** it renders, **Then** its cover icon is in the active
   state and is named "Unpin cover"; other photos' cover icons are named "Make {title} the cover".
3. **Given** the first photo while no cover is pinned, **When** it renders, **Then** its cover icon
   shows the automatic-cover state, named "Cover: first photo".
4. **Given** keyboard or drag reordering, **When** the owner moves a photo, **Then** it behaves
   exactly as today (only the handle's look changes).

---

### Edge Cases

- A very short or very wide photo (panorama): the title band still sits on the photo's bottom
  edge and stays readable; if the photo is too short for the band, the title still shows on one
  line over the bottom of the photo.
- Light, dark and busy photos: the title stays readable on all of them.
- An open panel for a photo that is not on the current page (`open` naming a photo elsewhere): no
  tile on this page is active; the panel still closes from its own controls.
- The select mark (top-left on the photo) and the title band (bottom) never overlap, even on a
  very short photo.
- Phone width: three icons fit on one row under the smallest tile with 44×44 targets.
- A photo whose edit is active and which is then deleted: the panel closes as today and no tile
  stays active.
- Keyboard: Tab reaches gallery, view, edit in that order on each tile; the active edit icon
  announces its pressed state.
- Builder: four icons (move, cover, remove, view) fit on one row under the smallest builder tile at
  phone width with 44×44 targets; a lifted photo still shows its "Lifted. Position N of M" help.
- Gallery icon activated while the photo's panel is open: the panel closes (same toggle as edit).

## Requirements *(mandatory)*

### Functional Requirements

**Title on the photo**

- **FR-001**: On library and builder tiles, the photo's title MUST be drawn inside the photo's own rectangle,
  along its bottom edge, on one line, truncated with an ellipsis when it does not fit, with the
  full title available on hover.
- **FR-002**: The title MUST stay readable over any photo (light, dark or busy), meeting text
  contrast of at least 4.5:1 against its immediate background.
- **FR-003**: The title band MUST NOT cover the select mark, and MUST NOT change the tile's
  height or the photo's size or position.
- **FR-004**: Public pages MUST be unchanged: public tiles keep their title under the photo,
  byte for byte.

**Control row**

- **FR-005**: Under each library photo there MUST be one row of exactly three icon buttons, in
  this order: gallery, view, edit. They MUST replace the current status text, "Edit" and
  "View ↗" words.
- **FR-006**: The gallery icon MUST show one of three distinct icons: in no gallery, in exactly one
  gallery, in two or more galleries. Its accessible name and hover text MUST give only the count,
  in words: "Not in a gallery", "In 1 gallery", "In {n} galleries". No gallery title and no public
  count appear on the tile (owner: "simplify only icon and total count").
- **FR-007**: Activating the gallery icon MUST open that photo's edit panel with the Galleries field
  in view, exactly as activating the edit icon does (same open state, same active edit icon). On
  the photo whose panel is already open, it MUST close the panel, as the edit icon does.
- **FR-008**: The view icon MUST open the photo's public page in a new tab when the photo has one
  (as the "View ↗" link does today) and MUST render as an unavailable, non-link icon with the
  accessible name "No public page" when it has none, so the row never changes length (IX.3).
- **FR-009**: Every icon button MUST have a visible hover text and an accessible name, a 44×44
  minimum hit area, and a visible keyboard focus indicator. Icons MUST NOT be the only carrier of
  meaning to assistive technology.
- **FR-010**: No icon MAY be a filled (primary) style at any time; the active state is a ring, not
  a fill, so the panel's Save stays the one filled action when the panel is open (009 UI-3: one
  filled action per state; amended at plan, research R-05).

**Edit toggle**

- **FR-011**: The edit icon MUST have two visible states: normal (enabled) and active (pressed).
  It is active exactly when the edit panel is open for that photo.
- **FR-012**: Activating a normal edit icon MUST open the edit panel for that photo and make that
  icon active; any other tile's active edit icon MUST return to normal. At most one tile is active.
- **FR-013**: Activating the active edit icon MUST close the panel and return the icon to normal,
  keeping the owner on the same library page.
- **FR-014**: The open/close state MUST survive a reload and work with scripting disabled
  (ordinary navigation), and the active icon MUST expose its pressed or current state to assistive
  technology.
- **FR-015**: Closing the panel this way MUST NOT save or discard anything beyond what leaving the
  panel does today (unsaved edits are lost exactly as when navigating away now).

**Icon system and component library**

- **FR-016**: Icons MUST come from one free, permissively licensed icon set, chosen after
  comparing candidates on licence, coverage of the needed icons, visual fit with the site, and
  delivery cost. The choice, the candidates and the licence MUST be recorded. An icon is made
  (with the owner's image tool) only when no free set has a usable icon for a needed meaning; any
  made icon MUST match the chosen set's grid and stroke and be recorded as such.
- **FR-017**: Icons MUST add no network request and no third-party runtime dependency to any page
  that shows them, MUST scale with no blur at any pixel ratio, and MUST take their colour from the
  surrounding text colour so they follow the site's colours.
- **FR-018**: Only the icons the site uses are shipped; adding an icon is a deliberate step, not a
  whole-set download.
- **FR-019**: The component library MUST gain: an icon page listing every shipped icon with its
  name and stating the source set and licence; an icon-button page showing normal, active and
  unavailable states; the library tile states of User Story 3; and a builder tile in each cover
  state.
- **FR-020**: The icon and the icon button MUST be reusable components with no knowledge of
  galleries or photos (labels and the icon name are parameters).

**Scope**

- **FR-021**: This feature changes the library's photo tiles and the gallery builder's photo tiles.
  The galleries list cards (`/admin/galleries`) keep their current look.
- **FR-022**: On builder tiles the title MUST be on the photo as in FR-001 to FR-003, and the
  builder's controls MUST become icon buttons, in this order: move, cover, remove, view. Each
  keeps its current action, form target and accessible name ("Move {title}", "Make {title} the
  cover" / "Unpin cover", "Remove {title} from this gallery", view in a new tab). The cover icon
  shows three states: not the cover, the automatic cover (first photo, no pin; named "Cover: first
  photo"), and the pinned cover (active, named "Unpin cover").
- **FR-023**: Drag and keyboard reordering, cover pinning, removal, and the builder's lifted-photo
  announcement MUST behave exactly as today.
- **FR-024**: The bulk selection, the select mark, the edit panel's contents, and every server
  operation MUST be unchanged.

**Observability**

- **FR-025**: The library and builder pages' existing request timing MUST still cover them, and
  neither page MUST make more datastore reads than today (the membership state comes from data the
  library page already reads).

### Key Entities

- **Icon**: a named glyph from the chosen set (or a made one), drawn in the current text colour.
- **Icon button**: an icon with an accessible name, a hover text, a 44×44 hit area, and a state:
  normal, active, or unavailable.
- **Gallery membership state**: none, one, or many, derived from the number of galleries holding
  the photo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A library or builder tile's control area under the photo is no taller than one 44 px
  row at every width (today it is a wrapping row of words plus the title line).
- **SC-002**: In a browser check over photos in 0, 1 and 3 galleries, 100% of tiles show the
  matching membership icon and accessible name, and the icons appear in the order gallery, view,
  edit on every tile.
- **SC-003**: Over a toggle sequence (open A, open B, close B), exactly 0 or 1 tile is active after
  each step and the panel state matches it, with scripting on and off.
- **SC-004**: The title on the photo measures at least 4.5:1 contrast against its band over a
  white, a black and a mid-grey photo.
- **SC-005**: Pages that show icons make zero extra network requests for them, and the library
  page's server time and read count stay within 10% of the pre-change capture, from logs.
- **SC-006**: Every icon, the icon button states and the new tile states are visible in the
  component library for owner review, with the icon source and licence named.

## Assumptions

- Surface replaced: `LibraryPage.razor` `RowFor` (status via `MembershipStatus`, `Edit` TextLink,
  `View ↗` TextLink) and the `Tile`'s title-below on library tiles (`CssClass="lib-tile"`). The
  edit panel opens today through the `?open={iid}` address, which already allows only one open
  photo; the toggle uses the same address (open) and the page address without it (close).
- `MembershipStatus` (feature 010) has only the library as a consumer; replacing it on the tile
  leaves it orphaned. Default: remove it and its catalogue page (severable: keep it).
- Default (superseded by the owner's direction "simplify only icon and total count", FR-006): the
  tile no longer shows "· public in N" or the first gallery's title anywhere, not even in hover
  text. Which galleries hold the photo, and which are public, is shown in the edit panel's
  Galleries field (feature 014).
- Builder surface replaced: `GalleryBuilder.razor`'s `b-ctl` caption (`DragHandle`, `CoverMarker`,
  "Remove" button, "View ↗" link) and the title below the photo. `DragHandle` and `CoverMarker`
  keep their behaviour and gain an icon look; they are not rewritten. The builder has its own
  gallery side panel, unchanged.
- Default: the view icon for a photo with no public page is shown unavailable rather than omitted,
  to keep the edit icon in a fixed position (constitution IX.3).
- The icon system is admin-first; public pages gain no icons in this feature.
- Icon delivery, the set comparison, and whether Gemini is needed are planning decisions; the
  owner's stated rule is free sets first, Gemini only for a meaning no free set covers.
- Test impact (to be listed in the plan): tests pin `lib-row`, `lib-status`, `MembershipStatus`
  and the Edit/View words in about 10 files (`LibraryPageTests`, `LibraryMembershipTests`,
  `LibraryFlowTests`, `LibraryBrowserTests`, `NavComponentTests`, `NavCataloguePageTests`,
  `LibraryComponentAdditionsTests`, `LibraryEditTests`, `GalleryListTests`, `BuilderTests`), plus
  the builder's component and catalogue snapshots (`GalleryBuilder`, `DragHandle`, `CoverMarker`
  pages in `ui-baseline`), which this feature changes by design.
- Severable seams (beyond the literal request):
  - removing the orphaned `MembershipStatus` component (cut: leave it unused);
  - the unavailable view icon (cut: omit it and let the row be two icons long);
  - the tile catalogue states (US3 scenario 3; cut: icon and icon-button pages only).
  - the builder tiles (User Story 4, owner chose it in clarify Q2; cut: library only, roughly
    two thirds of the work).
