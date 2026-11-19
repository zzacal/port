# Feature Specification: Admin Workspace

**Feature Branch**: `020-admin-workspace`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "the admin overhaul to this new system including the /ui update"

"This new system" is the single-workspace admin the owner reviewed and accepted as a clickable
preview on 2026-11-18 (see Design source of truth).

## Design source of truth

`docs/ux-research/admin-preview-v2.html` is the visual and behavioural reference for this feature.
It was built from the jobs the admin does, not from its current pages, and the owner reviewed it
over two rounds. Where the preview and this spec disagree, this spec wins. The preview is a mock:
its uploads are simulated, its phone layout is rough, and it has no way to reorder without a mouse.
Those three gaps are requirements here (FR-023, FR-028 to FR-034, FR-036).

`docs/ux-research/admin-preview.html` is the earlier preview the owner rejected ("it feels like
we're fixing the existing admin system"). It is kept only as a record of what was not wanted.

Owner feedback this spec encodes:

- No two controls go to the same place.
- Upload is not a page. It is a function inside the photo view that the owner can hide or close.
- No rows of bare icon buttons. Every action is a word.
- Uploading straight into a gallery is wanted: it makes working gallery-first easy.
- The galleries control on a photo or a selection keeps the old ability to make a new gallery by
  typing its name.

## Clarifications

### Session 2026-11-18

- Q: The owner opens a public gallery and uploads photos into it. Do visitors see them straight
  away? → A: Yes. The upload area says visitors will see the photos before any file is picked.
  This needs a constitution amendment before planning (FR-046). Proposed wording for II.5, a MINOR
  change (6.2.2 → 6.3.0): "Only the owner decides what is visible; nothing becomes public as a side
  effect of upload, tagging, or editing. An upload the owner aims at a public gallery is the owner's
  decision, not a side effect: the destination, and that visitors will see the photos, MUST be shown
  before any file is picked." (Done: constitution 6.3.0 carries this as II.5 with four conditions,
  5a to 5d; where the wording differs, the constitution's is the binding one.) Accepted risk: with the answer below, a wrong file dropped into a
  public gallery is visible to visitors seconds after it finishes, with no confirm step in between.
  Detection is immediate (the photo appears in the open gallery marked New); reversal is one action
  (untick the gallery, or delete the photo).
- Q: When do uploaded photos join the library? → A: Each photo as it finishes, as the preview
  showed. A failed photo holds nothing up. There is no confirm step and no batch-wide discard. This
  reworks the upload batch rules built in feature 008 (design record D-11 and its lifecycle entries)
  and the tests of those rules; the checks each photo passes are unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Organise photos without leaving the screen (Priority: P1)

The owner opens the admin and sees one screen: a list of collections on the left (All photos, Not
in a gallery, then each gallery), the photos of the chosen collection in the middle, and details on
the right. Clicking a photo shows its title, caption and galleries on the right, and the owner edits
them there. Selecting several photos shows what can be done to all of them. To put photos in a
gallery the owner ticks the gallery on the right, types a new gallery name, or drags the photos onto
a gallery name on the left.

**Why this priority**: Naming photos and putting them in galleries is most of the work the admin
exists for. Today it takes three page loads per photo to retitle one (library, photo page, back).

**Independent Test**: With 12 photos and 3 galleries, retitle three photos, add four selected photos
to an existing gallery and to a new one typed by name, and drag two photos onto a gallery, all
without the page changing. Reload and check everything was kept.

**Acceptance Scenarios**:

1. **Given** the workspace on All photos, **When** the owner clicks a photo, **Then** the right side
   shows that photo's title, caption and a list of every gallery with the ones it is in ticked.
2. **Given** one photo selected, **When** the owner changes the title and leaves the field,
   **Then** the title is saved, the field says it was saved, and the photo's title in the grid
   changes without a page load.
3. **Given** one photo selected, **When** the owner shift-clicks a photo four places later,
   **Then** all five are selected and the right side says how many are selected.
4. **Given** several photos selected, some of them in a gallery and some not, **When** the owner
   looks at that gallery in the list on the right, **Then** it shows as partly ticked; ticking it
   adds the ones that were missing.
5. **Given** a selection, **When** the owner types a name no gallery has and presses Enter,
   **Then** a private gallery with that name is created holding the selection, it appears in the
   left list, and the typing field keeps the focus.
6. **Given** a selection, **When** the owner types the name of an existing gallery and presses
   Enter, **Then** the selection is added to that gallery and no duplicate gallery is made.
7. **Given** a selection, **When** the owner drags it onto a gallery name on the left, **Then** the
   photos are added to that gallery and its count goes up.
8. **Given** photos are added to a public gallery by any of these ways, **When** the action
   finishes, **Then** the result message says visitors can see them now.
9. **Given** the Not in a gallery collection, **When** the owner adds its last photo to a gallery,
   **Then** the collection is empty and says every photo is in a gallery.

---

### User Story 2 - Add photos wherever you are (Priority: P1)

The owner presses Add photos, or drops files on the window, in whichever collection is open. An
upload area opens at the top of the photos. It says where the photos will go. The owner can hide it
while it works and close it when done. Photos added while a gallery is open go into that gallery.

**Why this priority**: The owner asked for upload to stop being a page of its own and liked
uploading straight into a gallery. Today, getting new photos into a gallery means the upload page,
confirm, the library, select, a dialog, then the gallery.

**Independent Test**: Open a gallery, add four photos, hide the upload area while they process, show
it again, close it when they are done, and check the four photos are in the gallery and in All
photos. Repeat from All photos and check the photos are in no gallery.

**Acceptance Scenarios**:

1. **Given** any collection is open, **When** the owner presses Add photos, **Then** an upload area
   opens above the photos, names where the photos will go, and states the file limits before any
   file is picked.
2. **Given** any collection is open and the upload area is closed, **When** the owner drops photo
   files anywhere on the window, **Then** the upload area opens and takes them.
3. **Given** photos are uploading, **When** the owner presses Hide, **Then** the area shrinks to one
   line with its progress and a Show button, and the upload carries on.
4. **Given** photos are uploading into a gallery, **When** the owner opens another collection,
   **Then** the upload carries on, the upload area stays visible, and the photos still go to the
   gallery it named.
5. **Given** photos are uploading into a gallery, **When** one of them finishes, **Then** it appears
   at the end of that gallery marked New while the others are still working.
6. **Given** the gallery being uploaded into is public, **When** the upload area opens, **Then** it
   says visitors will see the photos, before any file is picked; each photo is visible to visitors
   once it finishes.
7. **Given** an upload from All photos or Not in a gallery has finished, **When** the owner looks,
   **Then** the new photos are in the library and in no gallery.
8. **Given** one photo failed, **When** the owner looks at the upload area, **Then** that photo
   shows why in words, with Retry and Remove, and every other photo has still been added.
9. **Given** an upload has finished, **When** the owner presses Done, **Then** the upload area
   closes and the New marks clear.
10. **Given** the upload area holds photos that failed or were interrupted, **When** the owner
    closes it, **Then** those photos are dropped, nothing of them is kept, and the result says how
    many were not added.
11. **Given** a file that is not allowed (wrong type, too large, too many), **When** it is picked or
   dropped, **Then** it is refused at once by name with the reason, and the allowed files carry on.

---

### User Story 3 - Shape a gallery and the galleries list (Priority: P1)

The owner makes a gallery by typing its name in the left list. Opening a gallery shows its photos
in their order; with no photo selected, the right side holds the gallery's title, description and
its Public switch. The owner drags photos to order them, chooses a cover with a worded button, and
drags gallery names in the left list to set the order visitors see.

**Why this priority**: Ordering, cover and publishing decide what visitors see. Today they are
spread over a galleries page and a builder page with four or five icons per item.

**Independent Test**: Create a gallery by name, add photos, reorder two, choose a cover, make it
public, move it to the top of the list, and check the public galleries listing and the gallery page
show that order, cover and position.

**Acceptance Scenarios**:

1. **Given** the left list, **When** the owner chooses New gallery, types a name and presses Enter,
   **Then** a private gallery is created and opened.
2. **Given** a gallery is open with nothing selected, **When** the owner edits the title or
   description and leaves the field, **Then** it is saved and the left list shows the new title.
3. **Given** a private gallery with photos, **When** the owner turns Public on, **Then** it is
   public at once and the result message says visitors can see it.
4. **Given** a private gallery with no photos, **When** the owner looks at its Public switch,
   **Then** it cannot be turned on and the reason is written beside it.
5. **Given** a gallery is open, **When** the owner drags a photo (or a selection) to another place,
   **Then** the order changes at once and visitors see the new order.
6. **Given** a gallery is open and one photo is selected, **When** the owner presses Use as gallery
   cover, **Then** that photo carries the Cover mark and is the gallery's cover for visitors.
7. **Given** the left list, **When** the owner drags a gallery name above another, **Then** the
   public galleries listing shows the galleries in that order.
8. **Given** a gallery is open with nothing selected, **When** the owner presses Delete gallery and
   confirms, **Then** the gallery is gone, its photos are still in All photos, and the owner is on
   All photos.
9. **Given** the owner unticks the open gallery for a selected photo, **When** the action finishes,
   **Then** the photo leaves the grid and stays in the library.

---

### User Story 4 - One consistent system, the old one gone (Priority: P1)

Everything in the admin follows the same few rules: one screen, actions as worded buttons in one
place, delete last, results reported in one spot. The old Upload, Library, Galleries, gallery
builder and photo edit pages no longer exist, and neither do the parts only they used. The owner's
component catalogue at `/ui` shows the new parts in every state and no longer shows removed ones.

**Why this priority**: The owner's stated bar is "it HAS to be consistent". Leaving the old pages
or their parts in place would keep two systems alive.

**Independent Test**: Walk every context of the workspace at phone and desktop widths and check the
rules below hold in each; request each old admin address and check it answers as an unknown one;
open `/ui` and check every new part has a page with all its states and no removed part has one.

**Acceptance Scenarios**:

1. **Given** any context of the workspace (a library collection, a gallery, one photo, several
   photos, a photo viewed large), **When** the owner looks for an action, **Then** it is a button
   with a word on it, on the right side or in the bar above the photos.
2. **Given** any context that offers a delete, **When** the owner looks at the right side,
   **Then** delete is its last control and asks for confirmation naming what will be deleted.
3. **Given** any action finishes or is refused, **When** the result is shown, **Then** it appears
   in the same place and style as every other result.
4. **Given** a signed-in owner, **When** they request an old admin page address, **Then** it answers
   exactly as an admin address that never existed.
5. **Given** the `/ui` catalogue, **When** the owner opens it, **Then** each part of the workspace
   has a page showing every state it can take, and no page exists for a removed part.
6. **Given** a visitor, **When** they load any public page, **Then** it is identical to what it was
   before this feature.

---

### User Story 5 - Look at a photo large and step through (Priority: P2)

The owner opens a photo large in the middle of the screen, sees the whole photo, and steps to the
previous or next one with buttons or the arrow keys while the details stay editable on the right.

**Why this priority**: Captioning and choosing covers need a proper look at the photo. Grid
thumbnails are enough for sorting but not for judging.

**Independent Test**: Open the third photo of a gallery large, step forward twice and back once
with the arrow keys, edit a title on the way, go back to the grid, and check the grid shows the
last photo viewed as selected with the new title.

**Acceptance Scenarios**:

1. **Given** a photo in the grid, **When** the owner double-clicks it, presses Enter on it, or
   presses Open large, **Then** the whole photo is shown as large as the middle area allows, with
   its position in the collection ("3 of 12").
2. **Given** a photo is shown large, **When** the owner presses Next or the right arrow key,
   **Then** the next photo of the same collection is shown and the right side shows its details.
3. **Given** the first or last photo is shown large, **When** the owner looks at Previous or Next,
   **Then** the one that has nowhere to go is unavailable.
4. **Given** a photo is shown large, **When** the owner goes back, **Then** the grid returns with
   that photo selected and in view.

---

### User Story 6 - Do all of it on a phone (Priority: P2)

On a phone the collections are reachable from the top of the screen, the photos fill the width, and
the details open as a panel the owner can dismiss. Every job in stories 1 to 3 can be finished by
touch alone.

**Why this priority**: The current admin works on a phone, so losing that would be a step back. It
is P2 because the owner does most admin work at a desk.

**Independent Test**: At 390 px wide with touch input only, retitle a photo, add two photos to a
gallery, upload a photo into a gallery, reorder a gallery and make it public.

**Acceptance Scenarios**:

1. **Given** a 390 px wide screen, **When** the workspace loads, **Then** nothing scrolls sideways
   and every control is at least 44 px in both directions.
2. **Given** a phone, **When** the owner taps a photo's select mark, **Then** the details panel
   opens and has a visible way to close it.
3. **Given** a phone and a gallery open, **When** the owner reorders photos by touch, **Then** the
   order changes as it does with a mouse.
4. **Given** a phone, **When** the owner opens a gallery with nothing selected, **Then** the
   gallery's settings are one tap away.

---

### Edge Cases

- A save-on-leave fails (connection lost, session ended): the field says it was not saved and why,
  keeps what the owner typed, and the grid keeps the old value.
- A title is cleared and the field is left: the save is refused with the reason and the old title
  comes back, as the same rule refuses it today.
- The owner selects more photos than one bulk action may hold: the selection stops at the limit and
  says so at selection time, not after pressing an action.
- A selection is dragged onto a gallery that already holds all of it: nothing changes and the
  result says so.
- The last photo of a public gallery is removed or deleted: the gallery stays public but leaves the
  public listing and its page answers not found, as today; the result message says visitors can no
  longer see it until a photo is added.
- The cover photo is removed from its gallery or deleted: the cover falls back to the first photo,
  as today.
- A gallery is deleted, or a photo removed, in another tab while this one shows it: the next action
  on it reports that it no longer exists and the lists refresh.
- The owner switches collection with photos selected: the selection is cleared.
- The owner reloads or closes the tab while photos are uploading: on return the upload area is open
  again with the same photos, the ones that stopped marked interrupted with Retry and Remove.
- The owner closes the upload area with failed or interrupted photos still in it: they are dropped
  with their uploaded files, and the result says how many were not added. Photos already added stay.
- The owner tries to close the upload area while photos are still working: closing is unavailable
  until they finish, fail or are removed; Hide is the way to get it out of the way.
- A photo is retried after others finished: it joins at the end, after the ones already added.
- The gallery an upload is aimed at is deleted while photos are still working: the remaining photos
  join the library only, and the upload area says so.
- The gallery an upload is aimed at is made public or private while photos are still working: each
  remaining photo follows the gallery's visibility at the moment it joins, and the upload area's
  notice changes to match.
- A wrong photo lands in a public gallery: it is visible to visitors until the owner unticks the
  gallery or deletes it; there is no confirm step to catch it first (accepted, see Clarifications).
- The library is empty: the middle says so and points at Add photos.
- A gallery is empty: the middle says how to fill it (Add photos here, or drag photos onto its name
  from All photos).
- The library holds several thousand photos: more photos load as the owner scrolls; select-all
  covers the photos loaded so far, up to the bulk limit.
- Scripting is off: the workspace says it needs scripting; nothing half-works.
- The gallery name typed for a new gallery breaks the naming rules (blank, too long): refused with
  the reason, nothing created.

## Requirements *(mandatory)*

### Functional Requirements

**The workspace**

- **FR-001**: After sign-in the owner MUST land on one workspace screen with three regions: a
  collections list, the photos of the open collection, and a details panel. The sign-in screen
  itself is unchanged.
- **FR-002**: The collections list MUST show, in this order: All photos, Not in a gallery, then
  every gallery in the order visitors see them on the galleries listing. Each row shows its photo
  count; a private gallery is marked Private in words. The list also holds New gallery, View site
  and Sign out.
- **FR-003**: Each collection, and each photo viewed large, MUST have its own address, so reload,
  back and forward keep the owner's place.
- **FR-004**: The old admin page addresses (upload, library, a library photo, galleries, a gallery)
  MUST answer exactly as an admin address that never existed. No redirect layer is added.
- **FR-005**: The photos region MUST show each photo whole (never cropped) with its title. All
  photos and Not in a gallery are newest first; a gallery is in its own order. The only control
  drawn on a photo is its select mark. State is shown with small worded marks (Cover, New), never
  with action controls.
- **FR-006**: Every photo in the library MUST be reachable in the workspace without page
  navigation; more photos load as the owner scrolls, starting with as many as one library page
  holds today.

**Selecting**

- **FR-007**: Clicking a photo selects only it; a modifier-click or the select mark adds or removes
  one; shift-click selects the range from the last selected photo; a select-all shortcut selects
  every loaded photo; Escape, or a click on empty space, clears. Opening another collection clears
  the selection.
- **FR-008**: A selection MUST NOT grow past the number of photos one bulk action accepts today.
  The limit is applied when selecting, with a message naming it.

**The details panel**

- **FR-009**: The details panel MUST show: for a library collection with nothing selected, the
  library's counts and short usage tips; for a gallery with nothing selected, that gallery's
  settings; for one photo, that photo; for several photos, the selection.
- **FR-010**: Every action in the workspace MUST be a control with a visible word. There MUST be no
  icon-only action control anywhere in the workspace. The select mark on a photo is the one
  exception.
- **FR-011**: No two controls in the same context may lead to the same place or do the same thing.
- **FR-012**: Where a context offers a delete, it MUST be the last control in the details panel,
  styled as the outlined danger action, and MUST ask for confirmation that names what is deleted
  and what happens to related things, with the same wording rules as today.
- **FR-013**: A photo's title and caption MUST be saved when the owner leaves the field, with a
  visible "saved" mark by the field. A refused or failed save MUST say why beside the field, keep
  what was typed, and leave the stored value unchanged. Limits and the rule that a title cannot be
  blank are the ones in force today.
- **FR-014**: The galleries control, for one photo or a selection, MUST list every gallery with a
  tick showing membership: ticked when every selected photo is in it, partly ticked when some are,
  unticked when none are. Ticking adds every selected photo that is missing; unticking removes
  every selected photo. Each change applies at once.
- **FR-015**: The galleries control MUST have a text field that narrows the list as the owner types
  and, when the text matches no gallery name, offers to create a gallery with that name. Enter (or
  choosing the offer) creates a private gallery holding the selection. When the text matches an
  existing gallery's name, Enter adds the selection to that gallery. After either, focus stays in
  the field. Naming rules are the ones gallery creation uses today.
- **FR-016**: Dragging the selection onto a gallery in the collections list MUST add it to that
  gallery, the same as ticking it.
- **FR-017**: For one photo, the panel MUST offer a way to see it on the public site when it is in
  at least one public gallery; otherwise that control is shown unavailable and says the photo is
  not on the site.
- **FR-018**: Every action's result, success or refusal, MUST be reported in one place with one
  style, in words. A result that changes what visitors see MUST say so.

**Galleries**

- **FR-019**: New gallery MUST let the owner type a name in the collections list and create the
  gallery with Enter; it is created private and opened. Escape cancels.
- **FR-020**: With a gallery open and nothing selected, the details panel MUST hold its title and
  description, saved when the owner leaves the field (as FR-013), and a Public switch that applies
  at once.
- **FR-021**: The Public switch MUST refuse to turn on for a gallery with no photos, with the
  reason shown beside it, by the same rule that refuses it today.
- **FR-022**: In an open gallery the owner MUST be able to move one photo or a selection to another
  position by dragging; the new order applies at once.
- **FR-023**: Every reorder (photos in a gallery, galleries in the list) MUST also be possible by
  touch and by keyboard alone, as it is today.
- **FR-024**: With one photo selected in an open gallery, the panel MUST offer Use as gallery
  cover. The cover photo carries a Cover mark in the grid; the control says so and is unavailable
  when the selected photo already is the cover. With no cover chosen, the first photo is the cover.
- **FR-025**: Dragging a gallery in the collections list MUST change the order of the public
  galleries listing at once.
- **FR-026**: Delete gallery MUST leave its photos in the library and return the owner to All
  photos.
- **FR-027**: Every change made through the workspace MUST go through the same rules, refusals,
  page refreshes for visitors and logging as today's equivalent action. This feature changes where
  the owner does things, not what the site allows.

**Adding photos**

- **FR-028**: Add photos MUST be available in every collection. Dropping photo files anywhere on
  the workspace MUST have the same effect.
- **FR-029**: The upload area MUST open above the photos, name the destination before any file is
  picked, state the file limits, and refuse a disallowed file at pick time by name with the reason.
- **FR-030**: Each photo MUST join the library as soon as it has passed every check, without
  waiting for the other photos and without a confirm step. A photo that fails or is interrupted
  MUST NOT hold up, undo or block any other photo.
- **FR-031**: Photos added while a gallery is open MUST also join that gallery as each one joins the
  library, at the gallery's end, in the order they finish (the order picked, except that a retried
  photo lands after those already added). Photos added from All photos or Not in a gallery join no
  gallery. When the destination gallery is public, each photo is visible to visitors once it joins,
  and the upload area MUST say that visitors will see the photos before any file is picked and for
  as long as it is open.
- **FR-032**: Each photo in the upload area MUST show its state in words (waiting, preparing,
  uploading, added, failed with the reason, interrupted), with Retry and Remove on failed and
  interrupted photos.
- **FR-033**: The upload area MUST have Hide and Show while it works, and a control that closes it
  when the owner is done. It MUST stay in place and keep its destination when the owner opens
  another collection; files added while it is open go to that same destination. Closing is
  unavailable while any photo is still working. Closing with failed or interrupted photos drops
  them and their uploaded files and reports how many were not added. There is no batch-wide
  discard: a photo that has joined the library is removed only by the ordinary delete.
- **FR-034**: Everything the upload pipeline guarantees for a single photo MUST hold unchanged: what
  files are accepted, the checks every photo passes before it is stored where visitors can receive
  it, the rejection of the whole photo when any of its sizes fails a check, the removal of location
  and other embedded data, the limits, and recovery of an upload interrupted by a reload or a
  closed tab. What changes is only the moment of joining (per photo, FR-030) and the removal of the
  confirm and discard steps. Joining MUST be safe to repeat: a join interrupted partway and tried
  again finishes without duplicating the photo.
- **FR-035**: Photos added in the current upload MUST carry a New mark in the grid until the upload
  area is closed.

**Viewing large**

- **FR-036**: The owner MUST be able to open a photo large by double-click, by Enter on the focused
  photo, or by a worded button in the panel. The whole photo is shown as large as the photos region
  allows, with its position in the collection, Previous and Next buttons, and the left and right
  arrow keys. Going back returns to the grid with that photo selected and in view. The details
  panel stays usable throughout.

**Phone**

- **FR-037**: At 390 px wide, with touch only, every job in user stories 1 to 3 MUST be completable.
  The collections are reachable from the top of the screen; the details panel can always be
  dismissed by a visible control; nothing scrolls sideways; every control is at least 44 px in both
  directions.

**One system**

- **FR-038**: The workspace MUST be drawn by one layout that owns its frame in every context. A
  measured browser check MUST cover each context (library collection, gallery, one photo, several
  photos, photo large, upload area open) at phone and desktop widths and fail when the frame differs
  between them.
- **FR-039**: The old Upload, Library, photo edit, Galleries and gallery builder pages, their page
  scripts, and every component that no longer has a user MUST be removed. No request may reach
  dormant behaviour of the old pages, and an automated check MUST fail if a removed page or
  component comes back.
- **FR-040**: Public pages MUST be unchanged: same output, no admin script or markup, same reads.
- **FR-041**: The workspace is owner-only, and every change it makes, including saves on leaving a
  field and drag actions, MUST carry the same forgery protection as today's admin forms.

**The `/ui` catalogue**

- **FR-042**: The catalogue MUST have a page for every new part of the workspace showing every
  state it can take, including at least: the collections list (a collection open, a private
  gallery, a drag target, naming a new gallery); a photo in the grid (plain, selected, cover, new,
  drop position); the details panel in each of its four contexts; a field saving, saved and
  refused; the galleries control (ticked, partly ticked, narrowed, offering a new gallery); the
  upload area (empty, working, hidden, one failed, finished); the result message; the large view
  (first, middle, last); and the workspace layout at phone and desktop widths.
- **FR-043**: The catalogue pages and index entries of removed components MUST be removed, so the
  index lists exactly the components that exist. The catalogue stays owner-only.

**Evidence and records**

- **FR-044**: Every read and write the workspace makes MUST emit a named timing event with duration
  and outcome, so each time-bound success criterion below is checked from logs alone.
- **FR-045**: The design record MUST be updated in this feature: the admin layout decision (the
  "site with a frame" direction) is replaced by the workspace, the component list is brought up to
  date, and the upload batch lifecycle entries are rewritten for per-photo joining with no confirm or
  discard step.
- **FR-046**: (amendment DONE: constitution 6.3.0, II.5a to 5d) BLOCKING, before planning: the constitution's rule that nothing becomes public as a
  side effect of upload (II.5) MUST be amended as worded in Clarifications, so that an upload the
  owner aims at a public gallery, announced before files are picked, is the owner's decision.
- **FR-047**: Requirements in earlier specs that this feature supersedes MUST be annotated in
  place, not silently edited.

### Key Entities

- **Collection**: what the photos region is showing. One of: All photos; Not in a gallery (photos
  that are in no gallery); one gallery. Not stored; the two library collections are views.
- **Photo**: unchanged (title, caption, the galleries it is in). Gains nothing stored.
- **Gallery**: unchanged (title, description, public or private, ordered photos, chosen cover,
  position in the listing).
- **Selection**: the photos the owner has marked in the open collection. Lives only in the
  workspace; cleared on changing collection.
- **Upload set**: the photos currently in the upload area with their states and one destination
  (the library, or the library and one gallery). It replaces today's upload batch: it has a
  destination, each photo leaves it for the library on its own, and it has no confirmed or
  discarded state. It ends when the owner closes the upload area.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Retitling 10 photos takes 0 page loads (today: 30).
- **SC-002**: Getting 10 new photos into an existing gallery takes 0 page loads after opening the
  gallery (today: at least 4 pages and a dialog).
- **SC-003**: Adding 5 selected photos to two galleries, one of them new, takes no dialog and no
  page load.
- **SC-004**: Selecting a photo shows its details within 100 ms, measured in a browser, with no
  wait on the server.
- **SC-005**: Opening the workspace or a collection costs the server no more than today's library
  page for the same number of photos (95th percentile within 10% plus 1 ms), read from timing
  events against a capture taken before the change.
- **SC-006**: Each save on leaving a field, each tick, each reorder and each cover change costs the
  server no more than today's equivalent save (95th percentile within 10% plus 1 ms), read from
  timing events against the same capture.
- **SC-007**: Across every workspace context there are 0 icon-only action controls, delete is the
  last panel control wherever it exists, and the frame measures the same in every context at 390 px
  and 1440 px.
- **SC-008**: Each P1 story's independent test passes at 390 px with touch only, with no sideways
  scroll and no control under 44 px.
- **SC-009**: 100% of the old admin addresses answer as unknown, and a source scan finds 0 removed
  pages or components.
- **SC-010**: Every component that exists has a catalogue page; 0 catalogue pages exist for removed
  ones; each state listed in FR-042 is present.
- **SC-011**: Every public page is byte-for-byte what it was before the change, checked against a
  capture taken before the change.
- **SC-012**: The existing automated checks of what a single photo must pass (including refusal of
  a photo carrying location data, and rejection of the whole photo when one size fails) pass without
  being edited. Checks of the confirm and discard steps are replaced, as a stated requirement
  change.
- **SC-013**: With one photo of ten forced to fail, the other nine are in the library (and in the
  destination gallery) without any owner action, and each was added within 2 seconds of its upload
  finishing, read from timing events.

## Assumptions

**Defaults chosen for the owner (each can be overturned in clarify)**

- Saving as you go. Title, caption, gallery title and description save when the field is left, and
  the Public switch applies at once. This reverses the owner's earlier choice that Public waits for
  a Save button (feature 009 owner feedback). It was flagged when the preview was shown and follows
  from there being no page to leave. Reversal is cheap: a Save button in the panel.
- Old admin addresses are dropped with no redirects. Only the owner's own bookmarks are affected.
- Reload and the back button keep the owner's place (each collection and large view has an address).
- Selecting more than the bulk limit is stopped at selection time; the limit stays what it is today
  (100).
- Large libraries load more photos on scroll; there are no numbered pages.
- The phone layout is collections across the top and details as a dismissable panel, as roughed out
  in the preview; planning designs it properly.
- A removed photo's or gallery's result message offers no undo. Deleting still asks first.
- The tick list and typed-name field replace the token field on the photo edit page (feature 014).
  The same create, add and remove rules apply.
- The sign-in screen, the claim, the session and the local development sign-in are untouched.

**Things included that the owner did not literally ask for, and what is lost if each is cut**

- Addresses per collection and large view: cut it and reload always returns to All photos.
- Keyboard and touch reordering: exists today; cut it and reordering needs a mouse.
- Phone layout (story 6): cut it and the admin is desktop-only until a later feature.
- Large view (story 5): cut it and photos are judged from grid thumbnails and the panel preview.
- Not in a gallery collection: cut it and unplaced photos are found by eye.
- Design record update: required by the constitution (VII.2), not cuttable.

**Size and impact (planning inputs only)**

- 50 of 144 test files reference the old admin page addresses, and 4 reference `/ui`. Admin page
  tests for removed pages are retired or rewritten as a stated requirement change. Tests of the
  rules underneath (gallery, photo and delete operations, the upload pipeline, public output,
  sign-in) are expected to pass unmodified.
- The catalogue has 40 entries today (corrected at plan; the list that follows is corrected by
  plan decision 18: 13 entries go, the side panel, drop zone and page heading stay, the admin
  layout is rewritten in place). Components whose only users are removed pages, and so go:
  the owner bar, selection bar, photo icon row, gallery icon row, gallery builder, batch footer,
  batch item, upload drop zone, side panel, drag handle, cover marker, page heading, visibility
  toggle, token combobox, the icon and icon-button atoms, and the admin layout template (replaced).
  Kept because something else uses them: button, input, switch, the delete confirmation, breadcrumb
  and pager (public pages), and the sign-in card.
- Earlier requirements superseded, to be annotated in place: feature 008's confirm, discard and
  landing rules (its FR-013, FR-030 to FR-034 and Clarification 1, including "confirmed images join
  no gallery" and "confirm triggers no visitor page refresh"); the page-level requirements of features 009, 010 (admin bar and breadcrumb),
  014, 015, 016, 017 and 019.
- Design record entries affected: D-23 (admin layout, replaced), D-21 (component list), D-11 and
  the upload lifecycle entries L-01 to L-06 (per-photo joining, no confirm or discard), E-08
  (starting an upload while one is open).
- Uploading 100 photos into a public gallery means up to 100 changes to that gallery's public
  pages. Planning decides whether visitor page refreshes are grouped; visitors must see every added
  photo no later than shortly after the last one finishes.
- Cost of the per-photo answer: the confirm path, the batch states and their tests from feature 008
  are reworked. The owner chose this over keeping the pipeline as built.
- The workspace cannot be reviewed on the live site without the owner signed in; the local stack
  with the development sign-in is the review target.
- Stack is unchanged and is a planning input only: server-rendered pages with scripted admin
  interactivity, the existing datastore and image storage.
