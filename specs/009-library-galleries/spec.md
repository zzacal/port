# Feature Specification: Library and Gallery Management

**Feature Branch**: `009-library-galleries`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "009" (docs/roadmap.md, "009 Library and gallery management"): "The
library page (titles, captions, bulk actions, delete), the gallery list, and the gallery builder.
This includes visibility, ordering, covers, and purge plus warm on every public change. Purge calls
go to the dev zone for image URLs; page purge is exercised once hosting exists. The seeded sample
content is removed in this feature." Owns L-07 to L-17, E-12, E-14 to E-18, R-03 and R-04, the
D-23 Library, Gallery list and Gallery builder pages, the D-16 purge and warm, the II.6 publish
refusal, and the removal of the seeded sample content.

## Clarifications

### Session 2026-11-01

The owner is away for this run. Every question below was answered with the recommended option and
is logged in `decisions.md` (rows 1 to 5, source DEFAULT) for the owner to review.

- Q: How is the seeded sample content removed? → A: The seed command and its manifest are retired,
  so nothing can create seeded content any more. A one-time removal command, which refuses to run
  against anything but development storage and the development database, deletes every seeded
  image and seeded gallery and the public files only they use, and reports what it removed. The
  shared validation and camera readers that upload uses are kept. Uploaded images and galleries
  the owner made are never touched. (default taken; owner absent)
- Q: How does the library show a large number of photos? → A: Newest first, 100 photos per page,
  with Previous and Next. Selection and bulk actions work on the page being shown, so one bulk
  action covers at most 100 photos and the limit is visible before anything is pressed.
  (default taken; owner absent)
- Q: What happens to an image's files when it is deleted while another image uses an identical
  public file (the same file uploaded twice shares content-named files)? → A: A public file is
  deleted only when no remaining image references it; a shared file stays. The original (master)
  of the deleted image is always deleted. The edge purge covers only the files actually deleted.
  (default taken; owner absent)
- Q: Does making a gallery public ask for confirmation, and does making it private? → A: Making a
  gallery public asks once, naming the gallery and its photo count ("Make “Coast” public? It will
  be listed on the home page with its 24 photos, and anyone can see them."). Making it private
  happens at once with no question, because it only hides. An empty gallery's Public control is
  disabled with the reason shown, and the server refuses it as well (II.6).
  (default taken; owner absent)
- Q: What happens when the same gallery or the gallery order is changed from two tabs at once?
  → A: Order changes (photo order in a gallery, gallery order on the home page) are refused when
  the set of photos or galleries changed since the page was loaded, with "This gallery changed
  in another tab. Reload to see the current order." and nothing saved. Title, caption and
  description saves are last write wins. (default taken; owner absent)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The owner turns uploaded photos into a public gallery (Priority: P1)

The owner has just uploaded 40 photos and follows "Open the library" from the upload result. The
library shows every photo, newest first, as the same letterboxed contact sheet visitors see. They
press "Select all on this page", which marks the 40 new photos and the earlier ones too, so they
clear it and tick the 40 instead; a selection bar says "40 selected". They choose "Add to
gallery…", pick "New gallery", type "Coast" and confirm. They land in the Coast builder, which
shows the 40 photos in library order, the first one marked "Cover · first photo". They drag the
best photo to the front, switch Public on, confirm "Make “Coast” public?", and press View gallery.
The public Coast page shows the photos in their order, and the home page lists Coast.

**Why this priority**: Until this works, uploaded photos can never reach a visitor. It exercises
the library, bulk add, gallery creation, ordering, covers, publishing and the public refresh in
one path.

**Independent Test**: With an empty site plus a confirmed upload of 40 photos, follow the path
above; check that the home page and the gallery page show Coast with the chosen order and cover,
that the library tiles now say "In 1 gallery · public", and that the purge recorder holds the home
page and the Coast pages.

**Acceptance Scenarios**:

1. **Given** the owner is signed in, **When** they open the library, **Then** every image is
   shown newest first, 100 per page, each tile with its photo, its title and a plain status line
   under the title ("Not in a gallery", "In 2 galleries", "In 2 galleries · public in 1"), and no
   warning or required-field text anywhere on the page.
2. **Given** 40 tiles are selected, **When** the owner adds them to a new gallery called Coast,
   **Then** a private gallery Coast exists holding the 40 photos in library order, it is placed
   last in the home order, the owner lands on its builder, and no public page changes.
3. **Given** Coast holds photos, **When** the owner switches Public on and confirms, **Then** Coast
   appears on the home page at its home-order position, its page becomes indexable, and the home
   page and Coast's pages are purged and then warmed.
4. **Given** the selection includes 5 photos already in Coast, **When** the owner adds the
   selection to Coast, **Then** the other photos are added at the end and the result says
   "35 added · 5 were already in Coast".
5. **Given** the owner adds photos to a gallery that is already public, **When** they pick it in
   the gallery picker, **Then** the picker says the photos will be visible to visitors before they
   confirm (II.5), and after confirming the photos appear on the public page.

---

### User Story 2 - The owner edits titles and captions (Priority: P1)

In the library the owner opens a photo. A side panel shows its title, its caption, the galleries
it belongs to (each a link to that gallery's builder), a View link to its public photo page when
visitors can see it, and a separate Delete area. They change the title from "DSCF1197" to "Harbour
at dawn", add a caption, and press Save; the panel says "Saved", and the public photo page shows
the new title and caption.

**Why this priority**: Every uploaded photo is titled with its file name; without editing, the
public site shows camera file names.

**Independent Test**: Edit one image's title and caption; reload the library and check the fields
show the stored values; open the public photo page and check the new title and caption; check the
purge recorder holds that image's photo pages and the pages of its galleries.

**Acceptance Scenarios**:

1. **Given** an image is open in the side panel, **When** the owner saves a new title and caption,
   **Then** both are stored, the panel shows "Saved", and reloading the page shows the saved values
   in the fields.
2. **Given** the title field is emptied or longer than 200 characters, **When** the owner tries to
   save, **Then** the field itself prevents it with its limit shown, and a request that bypasses
   the field is refused by the server with the reason, keeping the stored title.
3. **Given** the image belongs to a public gallery, **When** its title changes, **Then** the gallery
   page and the image's photo page in every gallery it belongs to are purged and warmed.
4. **Given** the image belongs to no gallery with a page, **When** its title changes, **Then** no
   purge is issued.

---

### User Story 3 - The owner arranges the home page (Priority: P2)

The owner opens Galleries. It is the home grid with every gallery in home order, private ones
dimmed. Each card shows, under its title, a status ("Public", "Private", "Public · hidden until it
has a photo", "Private · no photos"), a drag handle, a Public switch, a View link (when the
gallery has a page) and an Edit link to its builder. They drag "Coast" to the front, and the home
page shows Coast first. They create a gallery with "New gallery", which asks for a title and opens
the empty builder.

**Why this priority**: Home order and visibility are the owner's main curation tools after
building galleries.

**Independent Test**: With three galleries (public, private, and public but empty), reorder them,
publish and unpublish one, create one; check home order, home listing, card statuses and purge
records after each step.

**Acceptance Scenarios**:

1. **Given** the gallery list is open, **When** the owner moves a gallery by dragging or with the
   keyboard (lift, arrow keys, drop, Escape to cancel), **Then** the new order is stored for public
   and private galleries alike, the home page lists public non-empty galleries in that order, and
   the home page is purged and warmed.
2. **Given** a private gallery with photos, **When** the owner switches it public and confirms,
   **Then** it appears on the home page at its stored position.
3. **Given** a public gallery, **When** the owner switches it private, **Then** it leaves the home
   page at once, keeps its position for later, its pages are marked non-indexable, and the home
   page and its pages are purged.
4. **Given** an empty gallery, **When** the owner looks at its card, **Then** the Public switch is
   disabled with "Add a photo before making this gallery public" beside it, and a publish request
   sent anyway is refused with that reason and changes nothing (II.6).
5. **Given** the owner presses New gallery and enters a title, **Then** a private, empty gallery is
   created at the end of the home order and the owner lands on its builder; no public page
   changes.

---

### User Story 4 - The owner builds a gallery (Priority: P2)

In the builder the owner sees the gallery grid as visitors see it. Under each photo's title is a
row with a drag handle, the cover control ("Cover" with Unpin, "Cover · first photo", or "Make
cover") and "Remove". The side panel holds title, description, the Public switch and View gallery,
and a separate danger area "Delete gallery…" that names the gallery and says its photos stay in
the library.

**Why this priority**: Ordering, covers and membership are what make a gallery look curated.

**Independent Test**: In a public gallery of 8 photos: reorder, pin a cover, unpin it, remove the
pinned cover, rename the gallery, then delete the gallery; after each step check the gallery page,
the home card cover, old-link redirects, image back-references and purge records.

**Acceptance Scenarios**:

1. **Given** no pinned cover, **When** the owner moves another photo to the front, **Then** that
   photo becomes the cover on the home card (E-15).
2. **Given** a photo, **When** the owner presses "Make cover", **Then** it is the cover wherever it
   sits in the order, its control shows "Cover" and "Unpin", and the home card shows it.
3. **Given** the pinned cover, **When** the owner removes it from the gallery, **Then** the pin is
   cleared, the cover falls back to the first photo, and the removed photo stays in the library
   (E-14, L-08).
4. **Given** a public gallery, **When** the owner removes its last photo, **Then** it stays public,
   leaves the home page, its page answers not found, its card in the list says "Public · hidden
   until it has a photo", and adding a photo later brings it back at its position with no
   republish (II.6, E-16).
5. **Given** a gallery titled Coast, **When** the owner renames it "Coastline", **Then** its address
   gains the new readable part, the old address redirects permanently to the new one, and both
   addresses are purged (II.4, L-13).
6. **Given** the owner deletes the gallery after confirming, **Then** it leaves the home order and
   home page, its page answers not found, its photos stay in the library and in their other
   galleries, and the home page and its pages are purged (L-17).

---

### User Story 5 - The owner deletes photos (Priority: P2)

The owner deletes one photo from its side panel, or several from the library selection. The
confirmation names the photo (or the count) and says it is removed from every gallery and the
original file is deleted, with no undo. After confirming, the photos are gone from every gallery,
covers fall back, the original files and every public file no other photo uses are deleted, and
the edge is told to forget those files and the affected pages.

**Why this priority**: Mistaken or unwanted uploads must be removable, and delete is the riskiest
action in the feature.

**Independent Test**: Delete an image that is the pinned cover of a public gallery and also in a
private one; check both galleries, the home card, the image record, the storage objects, and the
purge records for file addresses and pages. Delete a selection of 3 where one deletion fails in
storage; check the other two complete and the failure is reported.

**Acceptance Scenarios**:

1. **Given** the delete confirmation is open, **When** the owner confirms, **Then** the image leaves
   every gallery, each affected gallery's cover and card are recomputed, and the image no longer
   appears on any page (L-09).
2. **Given** the deletion is stored, **Then** the image's original and its public files are deleted
   from storage, except a public file another remaining image references, and the deleted file
   addresses plus every affected page are purged (R-03, E-18, Clarification 3).
3. **Given** a bulk delete of 3 photos in which one storage deletion fails, **When** it runs,
   **Then** all 3 images are gone from every gallery and the library, the failed file is logged
   with its reason as an unlisted leftover, and the result says "3 deleted" with a note of the
   leftover file.
4. **Given** a seeded image with no original in storage, **When** it is deleted, **Then** the
   missing original is treated as already gone, not as a failure.

---

### User Story 6 - The seeded sample content is gone (Priority: P3)

The owner runs the one-time removal command on their machine. It refuses unless the target is the
development storage and the development database. It removes every seeded image and seeded
gallery, recomputes the home order and home page, deletes the public files only seeded images
used, and prints what it removed. The site then shows only the owner's uploaded photos and the
galleries they built.

**Why this priority**: The roadmap requires it, and it can run last because seeded and uploaded
content coexist safely until then.

**Independent Test**: With seeded content plus 2 uploaded photos and 1 owner gallery that holds a
seeded photo and an uploaded one, run the command; check no seeded records remain, the uploaded
photos and owner gallery remain, the owner gallery lost only the seeded photo, and running the
command again reports nothing to remove.

**Acceptance Scenarios**:

1. **Given** a target that is not development, **When** the command runs, **Then** it stops
   without changing anything and says which check failed.
2. **Given** seeded and owner content, **When** the command runs, **Then** every seeded record is
   removed, owner content is untouched except that seeded photos leave owner galleries through the
   same rules as an image delete, and the summary lists counts.
3. **Given** the seed command was retired, **When** anyone tries to run it, **Then** it no longer
   exists.

### Edge Cases

- The owner opens a library page past the last page (after deletes): the last existing page is
  shown.
- The library is empty: the page says "No photos yet" with a link to Upload.
- A gallery has no photos: its builder says "No photos in this gallery yet." with a link to the
  library; its list card shows an empty tile with its title and no View link.
- The pinned cover is deleted from the library: the pin is cleared in every gallery that pinned
  it and each falls back to its first photo (E-14).
- An image belongs to several galleries, some public, some private: its delete or title change
  purges the pages of every gallery that has a page, public or private (private pages are
  reachable by link and cached too).
- The purge provider is unreachable, refuses, or rate-limits: the owner's change is already saved
  and stays; the failure is logged with its outcome, and the stale copy expires within the cached
  lifetime (one day for pages, E-12; the immutable lifetime for files, E-18 and R-03).
- No purge credentials are configured: every action works; purge and warm are logged as skipped
  with the addresses they would have covered.
- A bulk add or delete of 100 photos produces more than 100 addresses to purge: the purge is split
  into groups of at most 100.
- A publish request arrives with no form body or for a gallery emptied in another tab: refused as
  empty, nothing changed (II.6).
- A reorder request names photos or galleries that are not exactly the current set (another tab
  added or removed one): refused, nothing saved (Clarification 5).
- A bare save request with no form fields: stored values are kept, never cleared.
- A title made only of spaces: refused as empty.
- A rename whose new title produces the same readable address part: no redirect is needed and only
  the one address is purged.
- A deleted image is still held in a visitor's browser cache: it can show there until that copy
  expires (R-03); its address was never listed anywhere while it was not in a public gallery
  (R-04).
- A delete that crashes partway: re-running it finishes the job and never leaves an image listed
  in a gallery after its record is gone.

## Requirements *(mandatory)*

### Functional Requirements

**Access and layout**

- **FR-001**: The library, gallery list and gallery builder MUST be available only to the signed-in
  owner, behind the existing owner check, and every state-changing request MUST pass the existing
  forgery protection (I.3, I.6). Unknown admin addresses MUST still answer not found.
- **FR-002**: Each page MUST render as the visitor would see the corresponding public grid with the
  owner layer on top (D-23): the library and builder use the gallery grid (4 / 3 / 1 columns), the
  gallery list uses the home grid (3 / 3 / 1). The owner bar's Library and Galleries links MUST
  reach these pages and mark the current one.
- **FR-003**: Owner controls on public pages are forbidden: no public page gains owner markup or
  scripts, and public pages keep working with scripting disabled (IV.22, V.3, D-23).
- **FR-004**: Per-item controls MUST sit inside that item's tile, in a row under its title, never
  in a separate list and never as corner overlays on the photo cell. The only mark drawn over a
  photo is the library's selection mark. Status text MUST sit under the cell so grid rows stay the
  same height in every state.
- **FR-005**: Every library tile, gallery card and builder tile whose public page exists MUST link
  to it (a View link). A page exists for a gallery that holds at least one photo, public or
  private; a library image's View link opens its photo page in the first gallery, in home order,
  that holds it and has a page. Items with no public page MUST show no View link.

**Library**

- **FR-006**: The library MUST list every image, newest first, 100 per page, with Previous and Next
  (Clarification 2). An empty library says "No photos yet" with a link to Upload.
- **FR-007**: Each library tile MUST show the photo, its title and one plain status line: "Not in a
  gallery", "In N gallery/galleries", or "In N galleries · public in M". The page MUST NOT show
  warnings, required-field errors or prompts about missing titles, captions or other metadata.
- **FR-008**: Each tile MUST carry a selection mark; selecting any tile shows a selection bar with
  the count, Clear, "Select all on this page", "Add to gallery…" (primary) and "Delete…" (danger).
  Selection is limited to the page shown, so one bulk action covers at most 100 photos.
- **FR-009**: Opening a tile MUST show the side panel with the title (required, 1 to 200
  characters), the caption (optional, 0 to 2000 characters, plain text), the galleries holding the
  image (each linking to its builder), the View link when one exists, and a separate delete area.
- **FR-010**: The title and caption fields MUST show the stored values, and their limits MUST come
  from the same settings the server enforces, applied as the owner types (the save is blocked and
  the limit shown before submitting). The server MUST refuse an invalid save with the reason and
  keep the stored value; a title of only spaces counts as empty. A save request with no form
  fields MUST keep the stored values.
- **FR-011**: "Add to gallery…" MUST offer every gallery in home order, each labelled Public or
  Private with its photo count, plus "New gallery" with a title field. When the chosen gallery is
  public, the picker MUST say the photos will be visible to visitors before the owner confirms
  (II.5).
- **FR-012**: Adding to a gallery MUST append the selected photos at the end in library order,
  skip photos already in it, and report "N added · M were already in {gallery}" (L-07). Bulk add
  MUST apply the same rules as adding one photo.
- **FR-013**: Bulk delete MUST confirm once, naming the count and the consequence, then delete each
  photo through the same single-photo delete path; a photo that fails is reported with its reason
  and the others still complete.

**Image delete**

- **FR-014**: Deleting an image MUST be confirmed in a danger area that names the image and the
  number of galleries holding it and says "This removes it from every gallery and deletes the
  original file. It can't be undone." There is no trash and no undo (E-17).
- **FR-015**: On confirm the image MUST leave every gallery that holds it, each such gallery's pin
  is cleared if it was the image, each card and the home listing are recomputed (L-09, E-14), and
  then the image record is removed.
- **FR-016**: After the records change, the image's original MUST be deleted from private storage
  and each of its public files deleted unless another remaining image references the same file
  (Clarification 3). An original or file already missing counts as deleted. A storage failure MUST
  be logged with the file and reason and leave an unlisted leftover, never a broken page.
- **FR-017**: A delete that stopped partway MUST be finishable by repeating it, and no gallery may
  list an image whose record is gone.

**Gallery list**

- **FR-018**: The gallery list MUST show every gallery, public and private, in home order, private
  ones dimmed. Each card shows its cover (or an empty tile with the title), and under the title a
  status ("Public", "Private", "Public · hidden until it has a photo", "Private · no photos"), a
  drag handle, the Public switch, the View link when the gallery has a page, and an Edit link to
  its builder.
- **FR-019**: The owner MUST be able to reorder galleries by dragging (a ghost slot shows where the
  card will land) and by keyboard from the drag handle (lift, arrow keys, drop, Escape cancels,
  with the position announced). The new order MUST be stored for public and private galleries
  alike (L-14).
- **FR-020**: "New gallery" (the page's one primary action) MUST ask for a title (1 to 200
  characters), create a private empty gallery at the end of the home order, and open its builder
  (L-10). Creating a gallery changes no public page.

**Gallery builder**

- **FR-021**: The builder MUST show the gallery's photos in their order in the gallery grid. Under
  each photo's title is a row with the drag handle, the cover control and Remove (FR-004).
- **FR-022**: The owner MUST be able to reorder photos by dragging (with a ghost slot) and by
  keyboard, as in FR-019 (L-15). With no pinned cover, the first photo is the cover (E-15).
- **FR-023**: The cover control MUST show "Cover" with Unpin on the pinned photo, "Cover · first
  photo" on the first photo when nothing is pinned, and "Make cover" elsewhere. Make cover pins the
  photo; Unpin returns to the first photo (L-16, cover rule).
- **FR-024**: Remove MUST take the photo out of this gallery only, without confirmation, keep it in
  the library, clear the pin if it was the cover, and recompute the card (L-08, E-14).
- **FR-025**: The side panel MUST hold the title (1 to 200 characters), the description (0 to 5000
  characters, plain text), the Public switch and View gallery (when the page exists), with limits
  applied as in FR-010, and a separate danger area "Delete gallery…" that names the gallery and
  says "Its N photos stay in your library. This can't be undone."
- **FR-026**: Renaming MUST give the gallery a new readable address part while its identity stays,
  and the old address MUST redirect permanently to the new one (II.4, L-13).
- **FR-027**: An empty builder MUST say "No photos in this gallery yet." with a link to the library.
- **FR-028**: Deleting a gallery MUST remove it from the home order and home listing, remove it
  from every member image's gallery list, keep every image in the library and in its other
  galleries, and make its page answer not found (L-17).

**Visibility**

- **FR-029**: Making a gallery public MUST ask once, naming the gallery and its photo count;
  making it private MUST happen at once (Clarification 4). Both are available from the gallery
  list and the builder.
- **FR-030**: Making an empty gallery public MUST be refused by the server with "Add a photo before
  making this gallery public", changing nothing, whatever the request carries (II.6, L-11). The
  Public switch of an empty gallery MUST be disabled with that reason shown beside it.
- **FR-031**: Each Public switch MUST show the stored visibility. Automated checks MUST cover both
  directions: a public gallery renders the switch on, and a private gallery renders it with no
  "on" marker at all (no attribute that a browser would read as on).
- **FR-032**: Making a gallery private MUST remove it from the home listing at once, keep its home
  position, and mark its pages non-indexable (L-12, II.2).
- **FR-033**: A public gallery that becomes empty MUST stay public, be left out of the home page
  and every listing, and answer not found, until it holds a photo again, when it reappears at its
  position without republishing (II.6, E-16). Its visibility is never changed on the owner's
  behalf (II.5).
- **FR-034**: Nothing becomes visible to visitors except by the owner making a gallery public or
  adding photos to a gallery that is already public. Uploading, editing titles and captions,
  creating galleries and reordering MUST NOT change any visibility (II.5).

**Stored relationships**

- **FR-035**: After every operation, each affected gallery's card and the home listing MUST
  reflect the change before the response: the home listing holds exactly the public galleries with
  at least one photo, in home order, and the home page stays a single datastore read (IV.9).
- **FR-036**: After every operation, including bulk ones and ones that failed partway, each image's
  list of galleries MUST match the galleries that hold it, and every pinned cover MUST be a member
  of its gallery.
- **FR-037**: A reorder MUST be refused, saving nothing, when the photos or galleries it names are
  not exactly the current set, with "This gallery changed in another tab. Reload to see the current
  order." (or the gallery-list equivalent). Title, caption and description saves are last write
  wins (Clarification 5).

**Purge and warm**

- **FR-038**: Every change that alters what an existing public page shows MUST purge exactly the
  addresses of the affected pages and then warm them (IV.10, IV.18, D-16). Pages exist for the home
  page, and for every gallery with at least one photo (public or private) and its photo pages. The
  affected pages per change are:
  - add or remove photos, reorder photos, change cover (L-07, L-08, L-15, L-16): the gallery page
    and the photo pages whose content or neighbours change, plus the home page when the gallery is
    public;
  - publish or unpublish (L-11, L-12): the home page and the gallery's pages;
  - rename (L-13): the old and new gallery addresses and its photo pages, plus the home page when
    public;
  - gallery order (L-14): the home page;
  - gallery delete (L-17): the gallery's pages, plus the home page when it was public;
  - description change: the gallery page;
  - image title or caption change: its photo page in every gallery with a page, those gallery
    pages, and the home page when it is the cover of a public gallery;
  - image delete (L-09): the pages of every affected gallery with a page, the home page when any of
    them is public, and every public file address actually deleted (E-18).
- **FR-039**: Warming MUST request the purged pages as an anonymous visitor would, and the image
  files those pages show first, after the purge.
- **FR-040**: Purge and warm MUST run after the change is saved and MUST NOT delay, fail or undo the
  owner's action. A failed purge or warm is logged with its outcome and the addresses, and the
  bounded cached lifetime is the fallback (E-12, E-18). There is no owner-facing alert.
- **FR-041**: Purge requests MUST carry at most 100 addresses each and stay within the provider's
  rate limit (D-16).
- **FR-042**: The purge mechanism MUST be replaceable and MUST work in three modes: unconfigured
  (no outside call; logs each address that would have been purged or warmed), development zone
  (purges image file addresses on the development zone; page addresses stay log-only until hosting
  exists), and a test recorder that captures each call. With no purge settings the app starts and
  every owner action succeeds.
- **FR-043**: Purge credentials MUST come from configuration outside source control, MUST be
  limited to cache purge, and MUST never appear in a response, a page or a log.
- **FR-044**: The app's own short-lived page cache MUST drop every affected page on the same
  change, so the owner sees the change on the local site at once (IV.18; the local stand-in for the
  edge page purge).

**Seeded content removal**

- **FR-045**: The seed command, its manifest and its seed-only code MUST be retired so nothing can
  create seeded content (Clarification 1). The validation and camera readers that upload uses MUST
  be kept.
- **FR-046**: A one-time removal command MUST refuse to run unless the environment is development,
  the public bucket is a development bucket and the database is a development database (the same
  checks the seed used), and say which check failed.
- **FR-047**: The removal command MUST remove every seeded image (through the same rules as an image
  delete, FR-015 to FR-017) and every seeded gallery (as a gallery delete, FR-028), recompute the
  home order and home listing, report counts of what it removed, change nothing on a second run,
  and never touch an uploaded image or a gallery the owner created.
- **FR-048**: Galleries the owner creates MUST carry no seed marker, so nothing treats them as
  seeded.

**Observability**

- **FR-049**: Every new request MUST emit the request timing event, and each datastore operation,
  storage delete, purge call and warm request MUST emit a named timing event with duration and
  outcome; purge and warm events also carry the address count and the mode (IV.1, IV.2, IV.4).
- **FR-050**: Image deletes, gallery deletes, visibility changes and removal-command runs MUST be
  logged as owner-action events with the ids and counts involved.

### Key Entities

- **Library image** (existing): title, optional caption, size, placeholder, camera details, public
  files, created time, and the list of galleries holding it. Uploaded images carry no seed marker.
  This feature edits title and caption and deletes images.
- **Gallery** (existing): stable id, readable address part from the title, title, optional
  description, visibility (public or private, private by default), ordered photo list, optional
  pinned cover (always a member), and its denormalized card. Owner-created galleries carry no seed
  marker.
- **Gallery card** (existing): gallery id, address part, title, cover photo details or none, and
  photo count; recomputed on every change to members, order, pin, title or a member image.
- **Site** (existing): the home order (every gallery, public or private) and the home listing (the
  cards of public galleries with at least one photo, in home order).
- **Purge request** (new, not stored): a change's affected page addresses and file addresses, the
  mode, and the outcome, recorded only in logs (and by the test recorder).
- **Seeded record** (existing, removed by this feature): images and galleries carrying the seed
  marker.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The owner can take the 40 newest library photos into a new public gallery in at most
  6 actions after the library opens (select page, clear unwanted, Add to gallery, New gallery and
  title, Public, confirm), not counting individual tile ticks.
- **SC-002**: A library page of 100 photos is produced by the server in at most 500 ms at the 95th
  percentile, from the request timing events.
- **SC-003**: Each owner save (title, caption, description, reorder, cover, remove, publish, and a
  bulk add or delete of up to 100 photos) responds in at most 500 ms at the 95th percentile, from
  the request timing events, and no purge or warm timing event falls inside a save's request.
- **SC-004**: For every change listed in FR-038, the test recorder captures exactly the expected
  page and file addresses: none missing, none extra.
- **SC-005**: Purge and warm for a change finish, or log their failure, within 60 seconds of the
  change, from the timing events.
- **SC-006**: Zero empty galleries are listed or answer with a page, and zero attempts to make an
  empty gallery public succeed, including requests sent without the page.
- **SC-007**: The home page is exactly one datastore read after every operation in the test suite.
- **SC-008**: After any image delete, zero public files still referenced by a remaining image are
  deleted, and zero files of the deleted image remain except logged failures.
- **SC-009**: After every operation in the test suite, zero mismatches exist between gallery
  membership and each image's gallery list, and zero pinned covers are non-members.
- **SC-010**: After the removal command, zero seeded records remain, and the counts of uploaded
  images and owner galleries are unchanged.
- **SC-011**: Zero purge credential values appear in any served response or log line.
- **SC-012**: With no purge settings configured, 100% of owner actions succeed.

## Assumptions

- **Planning inputs only** (from the constitution and design record, not requirements): .NET 10
  minimal APIs with server-rendered Razor components, htmx and admin web components for the owner
  pages (Platform Constraints, D-21); MongoDB with the existing `images`, `galleries` and `site`
  collections (D-13, content model); Cloudflare URL purge on the zone, at most 100 URLs per call
  and 800 URLs/s, with derivatives on the image hostname purged like pages (D-16); R2 through the
  existing storage abstraction (D-07). The app stays .NET.
- **Reuse, not rebuild** (read before writing this spec):
  - `src/Gallery.Web/Data/Documents.cs`: `ImageDoc` (title, caption, derivatives, `galleryIds`,
    nullable `seedKey`), `GalleryDoc` (visibility, `items`, `pinnedCover`, `card`, but `SeedKey`
    still a non-null string; 008 decisions row 43 left that to 009, and FR-048 needs it nullable
    and omitted), `GalleryCardDoc`/`CoverImageDoc`, and `SiteDoc` (`homeOrder`, `homeCards`). The
    card shape the public pages read is reused unchanged.
  - `src/Gallery.Web/Data/ContentReader.cs`: the timed public reads (home one read, gallery, images
    in order). Owner writes need a new writer; the reader is not changed.
  - `src/Gallery.Web/Site`: the public pages already honour II.6 (home filters cards without a
    cover; gallery and short routes answer not found when `items` is empty; photo route answers not
    found for a non-member). `PublicCache` tags every public page with one output-cache tag, so
    FR-044 needs per-page eviction (a tag per page or per gallery). The seed tells the owner to
    restart the app to clear that cache; this feature removes that need.
  - `src/Gallery.Web/Admin/AdminEndpoints.cs`: the `/admin` group with the owner policy and
    `AdminAntiforgeryFilter` (form field or `X-CSRF-TOKEN` header) and the catch-all 404; the new
    routes go before the catch-all as `MapUpload()` does. `AdminAccessTests` asserts 404 for an
    owner on `/admin/library` (renamed in 008 T035); building the page is a stated requirement
    change for that one assertion, which moves to another unbuilt path.
  - `src/Gallery.Web/Upload`: uploaded images land in `images` with no `seedKey`, `galleryIds: []`
    and the file-name title; the upload result links to `/admin/library` (008 FR-034,
    `UploadPageTests`). `ConfirmService` and `Seeder.EnsureSeedKeyIndexesAsync` keep the partial
    unique `seedKey` index (008 decisions row 60), which already covers galleries without a seed key.
  - `src/Gallery.Web/Seed`: `Seeder`, `SeedCommand`, `Manifest`, `PhotoJson`, `seed/manifest.json`
    and `scripts/seed-manifest.sh` are seed-only and retire (FR-045); `DerivativeValidator` and
    `CameraFields` are used by upload and stay (they may move namespace). `SeederTests`,
    `SeedCommandTests`, `ManifestTests` and the seed parts of `UploadSeedCoexistenceTests` assert
    the retired command; removing them is a stated requirement change. Public-site and browser
    tests that use the seeder as a fixture need a replacement fixture that writes the same
    documents, so they keep passing unmodified in their assertions. The removal command reuses the
    seed's target guard (Development environment, `gallery-dev-` public bucket, `_dev` database).
  - `src/Gallery.Web/Storage/ImageStore.cs`: delete, delete-prefix, head and list with
    `op.timing`; deleting a missing key is already harmless.
  - 005 components (`specs/005-admin-ui-components/contracts/admin-components.md`): `SelectMark`,
    `SelectionBar` (gains "Select all on this page"), `DangerConfirm` (its image and gallery copy is
    used as written), `VisibilityToggle` (its Warning state is the publish confirmation of
    Clarification 4; a disabled-with-reason state for empty galleries is new), `DragHandle`
    (keyboard lift states), `CoverMarker` (Pinned, Automatic, Unpinned), `GalleryBuilder`,
    `SidePanel` (Saving and Saved), `OwnerBar` (Library and Galleries links, `ViewHref`), and
    `Tile` with `Overlay`, `CssClass`, `Caption` and `EmptyTitle`. The 005 contract places the
    builder's DragHandle and CoverMarker as corner overlays on the cell; FR-004 moves them into the
    row under the title (the owner's standing rule for letterboxed grids), using `Tile.Caption`.
    That changes the `GalleryBuilder` and admin `ContactSheet` `/ui/` specimens, and the plan must
    say how those catalogue tests change.
  - `specs/008-upload-pipeline/spec.md`: the upload result's library link becomes live; the upload
    page is otherwise unchanged.
- **Purge credentials are not available** (the owner has not provided a zone id or a purge token):
  the purge mechanism is built behind a replaceable interface with a log-only implementation when
  unconfigured and a recorder in tests (FR-042). No requirement, test or build step waits on them.
  Adding them is an owner step after the build (see Owner prerequisites).
- **Private galleries are purged too**: the design's lifecycle entries say "if the gallery is
  public", but a private gallery with photos has a reachable, edge-cacheable page, so FR-038 purges
  every existing page. This widens the design's purge set and contradicts nothing; the plan records
  it in the design record.
- **What "warm" fetches**: the purged pages as an anonymous visitor, and each page's first-row
  image files (the AVIF the browser picks). Warming every size of every photo is not required.
- **No-script fallback**: the owner pages may require scripting (V.3); drag and keyboard reorder
  need it. Every save is still a real form submission so a bare request behaves predictably.
- **Out of scope**: the sign-in page redesign (007 tasks D-001, needs a D-23 decision first); edge
  HTML caching rules, the production zone and live page purge (hosting feature); trash or undo;
  duplicate detection; tags or search; editing an image's files.
- **Design record changes owed by the plan** (Constitution VII.2, through the draft fragments and
  `assemble.sh`, never by editing docs/design.md): purge of private galleries' pages (FR-038); the
  shared-file rule on delete (Clarification 3); per-tile controls under the title rather than
  corner overlays (FR-004, D-23 builder); the E-16 and II.6 traceability mechanism now naming the
  publish refusal; III.10 seeding retired.
- **Owner prerequisites** (none blocks specify, plan, tasks or implement):
  1. After the build, for the live development purge: the zone id of the development image
     hostname's zone and an API token limited to Cache Purge on that zone, put in user-secrets by
     the owner. The plan pins the key names. Until then purge is log-only.
  2. During the live walkthrough: run the removal command against the development database and
     buckets.
- No constitution amendment is needed: every clarification fits v4.2.0 as written (II.5, II.6,
  IV.9, IV.10, IV.18 are met as stated).
