# Feature Specification: Navigation Improvements

**Feature Branch**: `010-navigation-improvements`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "navigation improvements" — following the owner's "navigation is
terrible" report and the navigation study delivered in conversation on 2026-11-01. The study's
recommended first set (ideas 1, 2, 3, 4, 6, 8, 11, 13) is the scope; the rest are listed as out of
scope below.

## Clarifications

### Session 2026-11-01

- Q: Where does the gallery link sit on the photo page, and what does it look like? → A: In the
  bottom navigation row (layout 2): `[ Previous ]   ← {Gallery title} · N / total   [ Next ]`,
  under the title and camera details. It is a small text link in the caption style, with a left
  arrow, not a button or icon; the counter follows it on the same line. Nothing is added above
  the photo, so the photo keeps all its height. On a narrow screen, a title too long for the row
  moves onto its own line above Previous and Next rather than squeezing them.
- Q: Which navigation ideas should this feature include? → A: The recommended set as written
  (study ideas 1, 2, 3, 4, 6, 8, 11, 13). Swipe (5), the thumbnail strip (7) and the cheap extras
  (10, 14, 15) stay out of scope.
- Q: What should Previous and Next look like once they're bigger? → A: They stay text links reading
  "Previous" and "Next", looking as they do today; each gets an invisible tap area of at least
  44×44 CSS px. They are not buttons, and the photo itself stays a non-link (no tap zones).
- Q: On a private gallery, should the end of the page show anything in place of the previous/next
  gallery links? → A: Nothing. A private gallery ends after its last photo, with no gallery links
  and no "All galleries" link. It is never anyone's previous or next gallery either.
- Q: When a library photo is in several galleries, which gallery should its "Coast +2" status link
  to? → A: The first by title, alphabetically (case-insensitive), whatever its visibility.
- Q: Should the photo page's arrow keys and Esc be shown to visitors anywhere, or work silently?
  → A: Silently. Nothing on the page mentions them: no hint line and no tooltip.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Know where I am and get back from a photo (Priority: P1)

A visitor viewing a single photo can see which gallery it belongs to and how far through the
gallery they are, and can go back to that gallery in one step, landing on the photo they were
looking at rather than the top of the grid.

**Why this priority**: Today a photo page has no link to its gallery. Both header links go to the
home page, and browser Back is the only way back to the grid. That is the dead end the owner hit.

**Independent Test**: Open any photo page directly from a pasted link (no history), follow the
gallery link, and check that the gallery page opens scrolled to that photo's tile. Check that the
photo page names the gallery and shows "N / total".

**Acceptance Scenarios**:

1. **Given** a visitor opens a photo page directly, **When** the page loads, **Then** the bottom
   navigation row reads `[ Previous ]   ← {Gallery title} · N / total   [ Next ]` (for example
   "← September 6 · 4 / 23"), with the title linking to that gallery.
2. **Given** a visitor is on photo 18 of a 23-photo gallery, **When** they follow the gallery link,
   **Then** the gallery page opens with photo 18's tile in view, not at the top of the page.
3. **Given** scripts are disabled, **When** the visitor follows the gallery link, **Then** the same
   page and position are reached.

---

### User Story 2 - Step through photos comfortably on any device (Priority: P1)

Moving to the previous or next photo is easy to hit with a thumb, quick with a keyboard, and keeps
working without scripts.

**Why this priority**: Previous and Next are small text links today: 49×15 px and 26×15 px on a
phone, sitting at the very bottom of the screen. No key does anything.

**Independent Test**: On a 390 px-wide screen, measure the Previous and Next targets. With a
keyboard, press Right, Left and Escape on a photo page and check where each one goes.

**Acceptance Scenarios**:

1. **Given** a photo page on a small screen, **When** it renders, **Then** Previous and Next still
   look like today's text links, and each one's tap area measures at least 44 by 44 CSS pixels
   without overlapping the other or the gallery link.
2. **Given** a photo page with a next photo, **When** the visitor presses the Right arrow key,
   **Then** the next photo's page opens. Left goes to the previous photo.
3. **Given** a photo page, **When** the visitor presses Escape, **Then** the gallery page opens at
   that photo's tile (as in Story 1).
4. **Given** the first or last photo, **When** the visitor presses the key for the missing
   direction, **Then** nothing happens, and no control for the missing direction is shown.
5. **Given** scripts are disabled, **When** the visitor uses Previous and Next, **Then** they work
   as links; only the keys are unavailable.

---

### User Story 3 - Keep going at the end of a gallery (Priority: P2)

A visitor who reaches the bottom of a gallery can move on to the next gallery, or back to the
previous one, without scrolling back to the top.

**Why this priority**: The gallery page ends after the last photo with no way onward. The only
route to another gallery is back up to the header, then the home page.

**Independent Test**: Scroll to the bottom of a listed gallery and follow "next gallery"; check
that it opens the gallery that follows it on the home page.

**Acceptance Scenarios**:

1. **Given** a gallery listed on the home page with listed galleries before and after it, **When**
   the visitor reaches the end of its photos, **Then** links to the previous and next listed
   galleries, by title, appear there, in home-page order.
2. **Given** the first (or last) listed gallery, **Then** only the link that exists is shown.
3. **Given** a private gallery (unlisted, reached by direct link), **Then** the page ends after its
   last photo with no gallery links of any kind (no previous/next, no "All galleries"), and no
   listed gallery links to it.
4. **Given** the owner reorders the home page, or publishes, unpublishes, renames or deletes a
   gallery, **When** a visitor next loads an affected gallery page, **Then** its previous/next
   gallery links reflect the change. Edge-cached copies are refreshed as for any other public
   change.

---

### User Story 4 - A header that doesn't repeat itself (Priority: P3)

The public header offers each destination once.

**Why this priority**: "Gallery" (the site name) and "Galleries" both lead to the home page. It is
small, but it is noise on every page.

**Independent Test**: Count the header links on any public page and their destinations.

**Acceptance Scenarios**:

1. **Given** any public page, **Then** the header contains no two links to the same destination.

---

### User Story 5 - From a library photo to its galleries (Priority: P3)

In the owner's library, the "In N galleries" status on a photo leads to those galleries.

**Why this priority**: The status names how many galleries hold a photo but links to none of them.
The only route today is opening the photo's side panel.

**Independent Test**: In the library, follow the status on a photo that is in one gallery, and on a
photo that is in several.

**Acceptance Scenarios**:

1. **Given** a library photo in exactly one gallery, **Then** its status links to that gallery's
   editing page.
2. **Given** a library photo in two or more galleries, **Then** its status links to the first one
   by title, alphabetically and ignoring case, public or private, and states how many others there
   are ("Coast +2"). The full list stays in the side panel.
3. **Given** a library photo in no gallery, **Then** the status stays plain text ("Not in a
   gallery").

---

### Edge Cases

- A one-photo gallery: the photo page shows "1 / 1", the gallery link, and neither Previous nor
  Next. The arrow keys do nothing; Escape goes to the gallery.
- A gallery renamed after a visitor's page loaded: the gallery link under the old address still
  arrives at the gallery through the existing rename redirect, and the photo position anchor
  still applies after the redirect.
- A photo removed from the gallery, or deleted, while its tile anchor is in someone's address:
  the gallery page opens at the top with no error.
- A public gallery that has become empty is not listed (constitution II.6). It is skipped when
  working out previous/next gallery links, and its own page answers not found as today.
- Keys pressed while a modifier is held (Alt+Left is browser Back, Cmd/Ctrl+arrow) or while text is
  being typed: the page does not intercept them.
- The visitor's Save-Data setting does not affect navigation links or keys; it continues to
  suppress only predictive fetching (IV.14).
- On small screens the photo MUST NOT get smaller than today at the same width. A gallery title too
  long for the navigation row moves the gallery link and counter onto their own line above
  Previous and Next; it never shrinks the Previous/Next tap areas below their minimum size (FR-004).
- A first or last photo: the row keeps the gallery link and counter centred even though one of Previous/Next
  is missing.

## Requirements *(mandatory)*

### Functional Requirements

Photo page

- **FR-001**: Every photo page MUST show the title of its gallery, preceded by a left arrow, as a
  link to that gallery page. It sits in the bottom navigation row, centred between Previous and
  Next, in the caption text style (not a button). Nothing is added above the photo.
- **FR-002**: The gallery link MUST bring the visitor to the gallery page with the current photo's
  tile in view, with and without scripts.
- **FR-003**: Every photo page MUST show the photo's position in its gallery as "N / total", using
  the gallery's own order, directly after the gallery link on the same line (separated by " · ").
- **FR-004**: Previous and Next MUST stay text links reading "Previous" and "Next", with today's
  look (no button outline or fill), each with a tap area of at least 44 by 44 CSS pixels at every
  supported width, larger than the visible text. They MUST work without scripts and MUST render
  nothing for a missing direction (as today). The photo itself MUST NOT become a link.
- **FR-005**: With scripts enabled, the Left and Right arrow keys MUST go to the previous and next
  photo, and Escape MUST go to the gallery page as in FR-002. Keys MUST NOT be intercepted when a
  modifier key is held, or when focus is in a text field. The keys work silently: no hint text or
  tooltip announces them.
- **FR-006**: Keyboard navigation MUST use the same transition between pages as clicking does
  (IV.16) and MUST NOT disturb the existing predictive loading of neighbouring photos (IV.13).

Gallery page

- **FR-007**: A listed gallery's page MUST end with links to the previous and next listed
  galleries, labelled with their titles, in home-page order. A missing direction renders nothing.
- **FR-008**: A gallery that is not listed (private, or empty) MUST NOT show previous/next gallery
  links or any other end-of-gallery link, and MUST NOT appear as anyone's previous or next gallery
  (II.2: private means unlisted).
- **FR-009**: Every change that alters which gallery comes before or after another (home reorder,
  publish, unpublish, rename, delete, a listed gallery becoming empty or non-empty) MUST refresh
  the gallery pages whose links change, including edge-cached copies (IV.18). Showing a stale
  neighbour is acceptable only within the existing cache lifetime, after a failed purge.

Header

- **FR-010**: The public header MUST NOT contain two links to the same destination.

Admin library

- **FR-011**: A library photo's gallery status MUST link to the gallery editing page when the photo
  is in at least one gallery: to the only gallery, or to the first by title (alphabetical,
  case-insensitive, regardless of visibility) with a "+N" count of the others. A photo in no gallery keeps the plain status.

Components and catalogue

- **FR-012**: Every new or changed visual element (the gallery link and counter on the photo page,
  the larger previous/next tap areas, the gallery-to-gallery links, the header, the linked library
  status) MUST be built as, or as part of, a catalogued component, with its states shown on that
  component's catalogue page before any page uses it. States include: first photo, last photo,
  one-photo gallery, first and last gallery, and a photo in zero, one and several galleries.
- **FR-013**: Pages and catalogue pages whose markup this feature changes on purpose MUST have their
  stored reference copies updated as a stated requirement change, after a check that only the
  intended markup moved. Every other page MUST stay identical.

Quality floors carried from the constitution

- **FR-014**: All navigation added by this feature MUST work with scripts disabled, except the
  keyboard shortcuts (V.3).
- **FR-015**: No public page may make more datastore reads per request than it does today because
  of this feature (home 1, gallery and photo at most 2), and layout shift on every public page MUST
  stay 0 (IV.8).
- **FR-016**: Every public page touched MUST keep emitting its request timing and datastore timing
  events (IV.1, IV.2), so FR-015 is verifiable from the log alone.

### Key Entities

- **Gallery sequence**: the order of galleries on the home page, restricted to listed galleries
  (public and holding at least one photo). It determines each listed gallery's previous and next
  gallery. It is derived from the existing home order; no new stored concept.
- **Photo position**: a photo's 1-based index and the total count within its gallery's own order.
  Derived; no new stored data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From any photo page, a visitor reaches its gallery in 1 click (or 1 key press), and
  lands with that photo's tile inside the visible area, in 100% of a run over every photo of a
  23-photo gallery at large and small widths.
- **SC-002**: Every photo page states its gallery and its position. Checked across every photo of
  every listed gallery: 0 pages missing either.
- **SC-003**: Previous and Next measure at least 44×44 CSS pixels at small, medium and large widths
  on 100% of photo pages checked.
- **SC-004**: With scripts disabled, home → gallery → photo → next → gallery (at that photo) → next
  gallery works on every listed gallery.
- **SC-005**: After each of home reorder, publish, unpublish, rename and delete, every affected
  gallery page shows the correct previous and next gallery on the next load, with 0 stale links
  in an automated before/after check.
- **SC-006**: Home logs exactly 1 datastore read per request, and gallery and photo pages at most
  2, in 100% of a 100-request run per page type. 95% of those responses are generated in under
  50 ms of server time. Both are verified from the log alone.
- **SC-007**: Layout shift is 0 on home, gallery and photo pages at small, medium and large widths,
  measured in a real browser.
- **SC-008**: 0 public pages have two header links to the same destination.
- **SC-009**: Every library photo that is in at least one gallery links to a gallery editing page
  from its status, checked over a library holding photos in 0, 1 and 3 galleries.

## Amendment: owner review 1 (2026-11-01)

The owner replaced the photo page's back link with a breadcrumb at the top of every page that has
a parent, public and admin, centred under the nav bar; kept the "x / y" count; asked for one
admin nav bar on every page with page actions out of it; and for Previous/Next as whole words.
FR-001's "nothing above the photo" and FR-002's "the gallery link" now read "the breadcrumb's
gallery item". Contract: contracts/owner-review-1.md. Decisions rows 41-46.

## Amendment: owner review 2 (2026-11-01)

The breadcrumb replaces the public header: no brand link, no nav list, separator `/`, left-aligned,
on every public page (home shows `Galleries`). FR-010 now reads "the public header is the
breadcrumb; it holds no link to the page itself and no duplicate destinations". Contract:
contracts/owner-review-2.md. Decisions rows 50-52.

## Assumptions

- Scope is the study's recommended set, confirmed by the owner in clarify (2026-11-01).
- Previous/Next: the owner chose text links with a larger invisible tap area (clarify, 2026-11-01),
  over outlined buttons or tap zones on the photo.
- The header does not stay fixed while scrolling (idea 12 is out of scope, confirmed with the scope
  answer in clarify).
- The duplicate header link is removed by dropping the "Galleries" item, because the site name
  already links home. If the owner prefers a word over the site name, the inverse works the same.
- Admin pages may require scripts (V.3), but nothing in Story 5 needs them.
- Previous/next gallery comes from the home order that already drives the home page, so the gallery
  page can know its neighbours without an extra datastore read. If that turns out false, the
  read bound in FR-015 wins and planning must find another way (for example storing neighbours
  with the gallery). Neighbours are stored per gallery by the projector (plan research R-01).
- Component-first order (owner question, 2026-11-01): the component changes and their catalogue
  states come first inside this feature, then the pages are wired, then browser tests. There is no
  separate catalogue feature.
- Planning inputs only, not requirements: server-rendered pages and catalogue as built in 003–009;
  the keyboard shortcuts as a small page script (public pages may not ship a framework, IV.22);
  the stored reference copies are the 005 public baselines and the 006 catalogue baselines.

### Out of scope (study ideas not in the recommended set; owner confirmed, 2026-11-01)

- 5: swipe left/right on phones.
- 7: a strip of nearby thumbnails under the photo.
- 9: a "Galleries › Gallery" trail on the gallery page.
- 10: a "back to top" link after the grid.
- 12: a header that stays in view.
- 14: a trail on the owner's gallery editing page and an "Add photos" link there.
- 15: page numbers or jump-to-page in the library.
- 16: opening the add-to-gallery picker straight after an upload is confirmed.

### Severable seams (additions the owner did not literally ask for)

- FR-009's refresh of edge-cached gallery pages: needed so previous/next gallery links are never
  wrong after an owner change (constitution IV.18). It widens what each owner change refreshes.
  Without it, links could stay wrong for the cache lifetime.
- FR-013's reference-copy updates: needed because the existing guards compare those pages byte for
  byte, and this feature changes them on purpose.
