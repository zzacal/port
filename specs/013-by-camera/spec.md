# Feature Specification: By Camera

**Feature Branch**: `013-by-camera`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "A \"By Camera\" page that is similar to galleries, but images are grouped by camera \"make and model\""

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse the cameras and open one (Priority: P1)

A visitor on the home page sees a second large link, "By Camera", under "Galleries". It opens a
listing that looks and behaves like the galleries listing, but each card is a camera (for example
"FUJIFILM X100V" or "Canon EOS R5") instead of a gallery. Each card shows a cover photo taken with
that camera and the camera's name as its caption. Opening a card shows a page that looks like a
gallery page: a grid of every public photo taken with that camera.

**Why this priority**: This is the feature. The listing and the camera page together are the
smallest thing that delivers "images grouped by camera".

**Independent Test**: Seed photos from two cameras into public galleries, open `/`, follow
"By Camera", confirm one card per camera, open a card and confirm the grid holds exactly that
camera's public photos.

**Acceptance Scenarios**:

1. **Given** public galleries holding photos from cameras A and B, **When** a visitor opens the
   home page, **Then** it shows the links "Galleries" and "By Camera", in that order, in the same
   large centred style.
2. **Given** that site, **When** the visitor follows "By Camera", **Then** the listing shows
   exactly two cards, one per camera, each with a cover photo taken with that camera and the
   camera's display name as its one-line caption.
3. **Given** the camera listing, **When** the visitor opens camera A's card, **Then** the camera
   page shows every photo taken with camera A that is in at least one listed gallery, each once,
   and no photo from any other camera.
4. **Given** a photo that sits in two public galleries, **When** its camera page renders, **Then**
   the photo appears once.
5. **Given** the camera listing and camera pages, **When** they render, **Then** they use the same
   grid, tile shape, spacing, captions, placeholders and loading behaviour as the galleries
   listing and a gallery page respectively.

---

### User Story 2 - Private photos stay out (Priority: P1)

The camera pages are a new public listing, so they follow the same visibility rules as the
galleries listing. A photo appears under its camera only while it is in at least one listed
gallery (public and non-empty). Photos that are only in private galleries, or in no gallery, never
appear, and a camera with no such photos has no card and no page.

**Why this priority**: Without it the feature would publish photos the owner never made public,
which the constitution forbids (II.2, II.5). It cannot ship without this story.

**Independent Test**: Put a photo from camera C only in a private gallery and a photo from camera
A in both a private and a public gallery; confirm camera C has no card and its address answers
not found, and camera A's page shows its photo.

**Acceptance Scenarios**:

1. **Given** a photo that is only in private galleries, **When** a visitor views the camera
   listing and that camera's page, **Then** the photo appears on neither.
2. **Given** a camera whose every photo is private or in no gallery, **When** a visitor opens the
   camera listing, **Then** that camera has no card, and its page address answers not found.
3. **Given** the owner makes a gallery private, **When** the change takes effect, **Then** its
   photos leave every camera page unless another listed gallery still holds them, and a camera
   left with no photos leaves the listing.
4. **Given** the owner publishes a gallery, adds a photo to a public gallery, or removes or
   deletes a photo, **When** a visitor next loads the camera listing or the affected camera page,
   **Then** it reflects the change within the same bound that applies to gallery pages today.
5. **Given** any camera page, **When** it renders, **Then** it links to no private gallery and
   shows nothing beyond what a listed gallery already shows (titles, photos, the visitor-facing
   camera details).

---

### User Story 3 - Step through a camera's photos (Priority: P2)

From a camera page the visitor opens a photo, sees it full size with its details, and moves to the
previous or next photo of the same camera. The photo view belongs to the camera: its breadcrumb
reads Home / By Camera / {camera} / {photo}, its position reads N / total within that camera, and
the camera crumb returns to the photo's tile on the camera page.

**Why this priority**: The grid is useful on its own (P1); stepping through photos is what makes it
feel like a gallery, but the grid ships without it if needed.

**Independent Test**: Open a camera page, open its first photo, and step forward to the last one,
checking each photo shown belongs to the expected sequence and the way back lands on the photo's
tile.

**Acceptance Scenarios**:

1. **Given** a camera page, **When** the visitor opens a photo tile, **Then** the photo opens with
   the same instant-rendering behaviour (thumbnail first, then the sharp image, transition from the
   tile) as a photo opened from a gallery.
2. **Given** an open photo, **When** the visitor uses previous/next (links or arrow keys),
   **Then** they move through that camera's visible photos in camera-page order, with no previous
   link on the first and no next link on the last, and the camera crumb returns to that photo's
   tile on the camera page.
3. **Given** an open camera photo, **When** it renders, **Then** it shows the same photo, caption
   and camera details as the photo's gallery photo page, its breadcrumb is Home / By Camera /
   {camera} / {photo}, and it shows N / total for the camera.
4. **Given** a photo that is not visible under that camera (private-only, no gallery, another
   camera, or unknown), **When** a visitor opens a camera photo address for it, **Then** it answers
   not found.

---

### Edge Cases

- A photo has no camera make and model (none recorded at upload): it appears on no camera page;
  galleries are unaffected.
- A photo has only a make or only a model: it is grouped under the one value it has, shown as-is.
- The same camera is recorded with different letter case or stray spaces (`Canon` / `CANON `):
  grouped as one camera.
- Make repeated in the model (`Canon` / `Canon EOS R5`): grouped by the make-and-model pair and
  displayed once, using the same camera name the photo page already shows ("Canon EOS R5").
- No listed gallery holds any photo with camera data: the "By Camera" link stays on the home page
  and the listing shows the same empty state the galleries listing shows with no galleries.
- A wrong or stale name in a camera page address (the camera's display text changed, or was typed
  by hand): redirects to the current address if the camera is known, otherwise not found.
- A camera page address must not reveal that a camera exists only in private photos: unknown and
  private-only cameras answer the same not-found page.
- Many photos from one camera (hundreds): the camera page shows them all in one grid, as a large
  gallery does; no paging in this feature.

## Requirements *(mandatory)*

### Functional Requirements

Listing and pages

- **FR-001**: The home page MUST show a second link, "By Camera", after "Galleries", in the same
  style, leading to the camera listing.
- **FR-002**: The site MUST provide a public camera listing with one card per camera that has at
  least one visible photo (FR-008), each card showing a cover photo and the camera's display name
  as a one-line caption, laid out like the galleries listing.
- **FR-003**: Each card MUST link to that camera's page, which MUST show every visible photo taken
  with that camera, each exactly once, laid out like a gallery page, each tile captioned with the
  photo's title.
- **FR-004**: Photos MUST be grouped by camera make and model together, compared ignoring letter
  case and leading, trailing or repeated spaces. A photo with only one of the two is grouped under
  that value; a photo with neither appears on no camera page.
- **FR-005**: A camera's display name MUST be the same camera name the photo page already shows for
  that photo's details (make dropped when the model already names it).
- **FR-006**: Camera listing order MUST be by number of visible photos, most first, ties broken by
  display name A–Z. Photos on a camera page MUST be ordered newest upload first. A card's cover MUST
  be the first photo in that order.
- **FR-007**: Camera page addresses MUST stay the same for as long as the camera's grouping key is
  unchanged, and MUST NOT need any owner action to create, rename or remove.

Visibility (constitution II)

- **FR-008**: A photo is visible under its camera only while it belongs to at least one listed
  gallery (public and holding at least one image). Photos only in private galleries or in no
  gallery MUST NOT appear on the camera listing or any camera page.
- **FR-009**: A camera with no visible photos MUST have no card, and its page address MUST answer
  with the same not-found page as any unknown camera address, so its existence is not revealed.
- **FR-010**: Camera pages and the listing MUST NOT link to or name any private gallery, and MUST
  show no camera detail beyond the allowlisted details the photo page already shows (III.3).
- **FR-011**: Every owner action that changes which photos are visible, or a visible photo's title,
  cover role or position (publish, unpublish, add to or remove from a gallery, delete, rename a
  photo, upload confirm into a public gallery), MUST be reflected on the camera listing and the
  affected camera pages on the next load, within the same freshness bound the galleries listing
  has today, including any edge-cached copy (II.2 purge, best effort, bounded lifetime).
- **FR-012**: Nothing on the camera pages becomes public as a side effect: the feature MUST add no
  owner control and no new way for a photo to become visible (II.5).

Consistency and performance (constitution IV, IX)

- **FR-013**: The camera listing and camera page MUST use the same shared page container, header,
  breadcrumb and grid as the public pages they mirror, and MUST be added to the measured
  page-consistency check at narrow and wide widths (IX.1–IX.4).
- **FR-014**: Breadcrumbs MUST read Home / By Camera on the listing and Home / By Camera /
  {camera} on a camera page, built by the same trail helper as every other public trail.
- **FR-015**: The new pages MUST carry every perceived-performance behaviour the pages they mirror
  carry: placeholders, stored dimensions, first-row high priority, lazy loading after it,
  predictive loading on intent, view transitions to the photo, edge caching with a bounded
  lifetime, back/forward-cache eligibility, and no script required (IV.7–IV.22, V.3).
- **FR-016**: The camera listing MUST cost no more datastore reads per request than the galleries
  listing does, and a camera page no more than a gallery page does, each emitting the same
  request and named timing events those pages emit (IV.1–IV.4, IV.9).

Photo view

- **FR-017**: Each tile on a camera page MUST open a camera photo view for that photo, with the
  same instant-rendering, predictive and transition behaviour as a gallery photo page (IV.13,
  IV.15, IV.16, IV.17).
- **FR-018**: The camera photo view MUST step previous/next (links and arrow keys) through that
  camera's visible photos in camera-page order, show N / total within the camera, and carry the
  breadcrumb Home / By Camera / {camera} / {photo}, the camera crumb landing on the photo's tile.
- **FR-019**: A camera photo address MUST answer not found unless the photo is currently visible
  under that camera (FR-008); a stale camera name in the address redirects as FR-007's edge case
  does.
- **FR-020**: The camera photo view MUST be included in FR-011 freshness (a change to the camera's
  visible set changes every camera photo's neighbours and total), FR-013 consistency, FR-015
  performance and FR-016 read parity (no more reads than a gallery photo page).

### Key Entities

- **Camera**: a grouping derived from the photos' recorded make and model, never entered by the
  owner. Attributes: grouping key (normalised make and model), display name, address, visible
  photo count, cover photo. Exists publicly only while it has a visible photo.
- **Camera card**: what the listing shows per camera: cover photo with its placeholder and
  dimensions, display name, link. Mirrors the gallery card.
- **Photo (existing)**: unchanged; its stored camera details are the source of grouping. Its
  membership in listed galleries decides whether it is visible under its camera.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor goes from the home page to a camera's photo grid in two clicks, and can
  step through every photo of that camera from the first to the last without leaving the camera
  trail.
- **SC-002**: On a test site with N visible photos across K cameras, the listing shows exactly K
  cards and the camera pages together show exactly N photos, none duplicated; 0 photos from
  private-only or gallery-less images appear (checked by an automated test that seeds both).
- **SC-003**: After each owner action listed in FR-011, the next load of the camera listing and the
  affected camera page shows the change (checked per action by an automated test).
- **SC-004**: Server time at the 95th percentile for the camera listing and a camera page is no
  more than that of the galleries listing and a gallery page with the same number of photos, and
  their datastore read count per request is no higher, verified from timing logs alone.
- **SC-005**: The measured page-consistency check passes with the three new page types included: the
  header, breadcrumb and gap under them sit at the same position and height as on every other
  public page at narrow and wide widths.
- **SC-006**: All three new pages show no horizontal scroll and no layout shift caused by images, and
  work with scripting disabled.

## Clarifications

### Session 2026-11-04

- Q: Where does a photo tile on a camera page lead? → A: (1A) a camera-scoped photo view: previous/next step through that camera's photos, breadcrumb Home / By Camera / {camera} / {photo}. Adds US3 acceptance 2–4, FR-017..FR-020, and the third page type in SC-005/SC-006.

## Assumptions

- Stack and existing pieces are planning inputs only: the camera details already stored per photo
  at upload (make and model among the allowlisted fields, III.3/III.7) are the grouping source;
  the existing shared grid component, tile, page container, trail helper, home-page link list,
  and camera-name formatter are reused, so the camera pages look like the gallery pages by
  construction.
- Only photos in listed galleries are visible under a camera. Including every library photo would
  make uploads public as a side effect, which II.5 forbids; that option is not offered.
- The page is called "By Camera" on the home page and in breadcrumbs, as the owner named it.
- Grouping is by make and model only. Lens, focal length and other details are not grouping keys.
- Photos with no make and no model are left out; there is no "Unknown camera" card (default,
  overturnable in clarify).
- Listing order (most photos first) and photo order (newest upload first) are defaults; the owner
  has no manual ordering for cameras in this feature.
- No paging, filtering, search or per-camera description (V.1).
- The camera listing is a new public listing in the sense of II.2; no constitution amendment is
  expected, since II.2 already covers "any other public listing". The plan records the new pages
  as additions to the design record (VII.2) and any D-entry for the derived camera grouping.
- Freshness after owner edits uses the existing purge-and-bounded-lifetime approach; widening it to
  cover camera pages is in scope (FR-011).
- Created from `main` at 6b78b89 (012 merged, site live at port.zacal.me).

### Severable seams

- Home page link (FR-001): needed so visitors can find the page; without it the listing is
  reachable only by address. One link.
- Camera photo view (US3, FR-017..FR-020): a third page type plus widened cache clearing (every
  camera photo's neighbours and total move when the camera's set changes). If it must ship
  smaller, tiles can link to the photo's gallery photo page instead and the grids still work.
