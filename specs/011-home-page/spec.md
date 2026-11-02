# Feature Specification: Home Page

**Feature Branch**: `011-home-page`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "A homepage that has a link to galleries. Gallery becomes `/g`. Breadcrumb at the root is now "home". Links (only one--galleries, for now) are big and centered on the home page."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Arrive at a home page and go to the galleries (Priority: P1)

A visitor opens the site's root address. Instead of the gallery grid, they see a home page whose
only content is one large link, "Galleries", centred on the page. Following it opens the gallery
grid, which now lives at `/g` and looks exactly as the root page did before.

**Why this priority**: This is the feature. Without it nothing else in this spec has a page to
live on.

**Independent Test**: Load `/`, confirm the page shows the "Galleries" link and no gallery cards,
follow the link, and confirm `/g` lists the same public galleries in the same order that `/`
listed before the change.

**Acceptance Scenarios**:

1. **Given** a site with public galleries, **When** a visitor opens `/`, **Then** the page shows
   one link labelled "Galleries" that goes to `/g`, and shows no gallery cards.
2. **Given** a visitor on the home page, **When** they follow "Galleries", **Then** `/g` shows
   every listed gallery (public, holding at least one image) in the owner's order, with the same
   grid, cards and loading behaviour the root page had before this feature.
3. **Given** a private gallery or an empty public gallery, **When** a visitor opens `/g`, **Then**
   that gallery is not listed, as before.
4. **Given** the site has no listed galleries, **When** a visitor opens `/`, **Then** the
   "Galleries" link is still shown, and `/g` shows an empty listing, as the root page did before.

---

### User Story 2 - The breadcrumb starts at Home everywhere (Priority: P1)

Every public page's breadcrumb now starts with "Home". On the home page the trail is just "Home",
shown as the current page. On the galleries listing it is Home / Galleries. On a gallery page it is
Home / Galleries / {gallery}, and on a photo page Home / Galleries / {gallery} / {photo}. The not
found page reads Home / Not found.

**Why this priority**: The owner named it as part of the request, and with a new root every
existing trail would otherwise send visitors "home" to the galleries listing instead of home.

**Independent Test**: Load each public page type and read its breadcrumb: the first item is
"Home" linking to `/` (or the current, unlinked item on `/`), and "Galleries" links to `/g`.

**Acceptance Scenarios**:

1. **Given** the home page, **When** it renders, **Then** its breadcrumb is the single item "Home",
   marked as the current page and not a link.
2. **Given** the galleries listing, **When** it renders, **Then** its breadcrumb is "Home" (a link
   to `/`) then "Galleries" (current, not a link).
3. **Given** a gallery page, **When** it renders, **Then** its breadcrumb is "Home" (to `/`),
   "Galleries" (to `/g`), then the gallery title (current).
4. **Given** a photo page, **When** it renders, **Then** its breadcrumb is "Home", "Galleries", the
   gallery title (linking to the photo's tile in its gallery, as before), then the photo title.
5. **Given** a page that is not found, **When** it renders, **Then** its breadcrumb is "Home" (to
   `/`) then "Not found".
6. **Given** a private gallery's page or one of its photo pages, **When** it renders, **Then** its
   breadcrumb links only to `/`, `/g` and that gallery itself, never to another gallery.

---

### User Story 3 - The home links are big, centred, and ready for more (Priority: P2)

The home page's links are large, centred horizontally and vertically in the space under the
header, and easy to hit on a phone. There is one link today; when the owner adds a second later,
it sits in the same centred group in the same style without a redesign.

**Why this priority**: The look is part of the request, but the page is usable with any link
style; this story makes it right.

**Independent Test**: Load `/` at a phone width and a desktop width and measure the link: its
text is larger than any other text on public pages, its centre lies on the page's horizontal
centre line and in the vertical middle of the space under the header, and its tap area is at
least 44 px tall.

**Acceptance Scenarios**:

1. **Given** the home page at any width from 320 px to 2560 px, **When** it renders, **Then** the
   link group is centred horizontally on the page and vertically in the area below the header,
   with no horizontal or vertical scrolling.
2. **Given** the home page, **When** it renders, **Then** the link text uses the largest text size
   on the public site, and the whole link is the tap target.
3. **Given** a home page rendered with two or more links (catalogue specimen), **When** it renders,
   **Then** the links stack in one centred column, each in the same style and size, in the given
   order.

---

### Edge Cases

- Old bookmarks and shared links to `/` now land on the home page, one click from the listing.
  The site is not live, so no redirect from `/` to `/g` is added; `/` is the home page.
- `/g` without an id and `/g/` with a trailing slash both show the listing; the existing
  `/g/{id}` short link and `/g/{id}/{slug}` pages are unchanged.
- A gallery id can never collide with the listing route, because the listing has no id segment.
- Long gallery and photo titles in the now four-item photo breadcrumb behave as the current
  three-item trail does at narrow widths (no overflow off the page, current item last).
- The home page works with scripting disabled (it is a link on a page).
- The owner's admin links that point at the public site ("View site", the sign-in card's way
  back) keep pointing at `/`, which is now the home page.
- The Escape key on a photo page still returns to the photo's tile in its gallery (it follows the
  gallery crumb, which keeps its target).

## Requirements *(mandatory)*

### Functional Requirements

Home page

- **FR-001**: The root address `/` MUST show the home page: the site header with its breadcrumb,
  and a group of home links. The group MUST hold exactly one link today, labelled "Galleries",
  going to `/g`.
- **FR-002**: The home page MUST NOT list galleries, show gallery cards, or read gallery content.
- **FR-003**: The home links MUST be rendered by one shared component that takes an ordered list
  of links, so adding a link is a data change, not a layout change.
- **FR-004**: The home link group MUST be centred horizontally on the page and vertically in the
  area below the header, at every width from 320 px to 2560 px, without causing scrolling.
- **FR-005**: Each home link's text MUST be the largest text size on the public site, and each
  link's tap area MUST be at least 44 px tall and span the link's full text. With several links
  they MUST stack in one centred column in the given order.
- **FR-006**: The home page MUST work with scripting disabled and MUST meet the same
  speed and loading rules as other public pages: following the "Galleries" link MUST be eligible
  for the site's existing predictive loading on hover or press intent.

Galleries listing

- **FR-007**: The galleries listing (the grid previously at `/`) MUST be served at `/g`, with the
  same content, order, layout, loading behaviour and caching it had at `/`.
- **FR-008**: The listing MUST keep every visibility rule it had at `/`: only public galleries that
  hold at least one image are listed; private and empty galleries never appear.
- **FR-009**: Every owner change that previously refreshed the cached root page MUST now refresh
  the cached listing at `/g` instead (purge and warm). The home page MUST carry no content that
  owner changes affect.
- **FR-010**: No redirect, alias or compatibility route MUST be added from `/` to the listing or
  from any other old address; `/` is the home page.

Breadcrumb

- **FR-011**: The root item of every public breadcrumb MUST be "Home", linking to `/`, except on
  the home page itself, where "Home" is the only item and is the current page (not a link).
- **FR-012**: Breadcrumbs MUST read: home `Home`; listing `Home / Galleries`; gallery page
  `Home / Galleries / {gallery}`; photo page `Home / Galleries / {gallery} / {photo}`; not found
  `Home / Not found`. "Galleries" links to `/g` wherever it is an ancestor.
- **FR-013**: The photo page's gallery crumb MUST keep its current target (the photo's tile in its
  gallery), so the Escape key behaviour is unchanged.
- **FR-014**: A private gallery's pages MUST link to no other gallery; their breadcrumb links only
  to `/`, `/g` and the gallery itself.
- **FR-015**: The breadcrumb used where a page passes no trail (catalogue specimens, component
  tests) MUST be the root trail `Home` linking to `/`.

Consistency and evidence

- **FR-016**: The header and breadcrumb row MUST render at the same position, height and spacing on
  the home page and the listing as on every other public page; the existing measured layout
  consistency check MUST include both page types.
- **FR-017**: The listing at `/g` MUST emit the same request timing and named datastore timing
  events the root listing emitted, and the home page MUST emit its request timing event and no
  datastore timing event.
- **FR-018**: The owner's component catalogue MUST show the home links component with one link and
  with several links.
- **FR-019** (blocking, before planning): The constitution's rules that name the home page as the
  place galleries are listed (II.2, II.6, IV.9) MUST be amended so the guarantees move to the
  galleries listing, before `/speckit-plan` runs.

### Key Entities

- **Home link**: a label and a destination shown on the home page; ordered. One exists today
  ("Galleries" → `/g`). Fixed in the site, not owner-editable.
- **Breadcrumb trail**: the ordered list of ancestor links plus the current page that every public
  page shows; its root is now "Home".
- **Galleries listing**: the existing grid of listed gallery cards, unchanged in content, now at
  `/g`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor reaches the galleries listing from the root address in exactly one click.
- **SC-002**: 100% of public page types (home, listing, gallery, photo, not found) show a
  breadcrumb whose first item is "Home", checked by an automated test over every type.
- **SC-003**: At 320, 390, 768, 1280 and 2560 px wide, the home link's centre is within 2 px of the
  page's horizontal centre and of the vertical middle of the area below the header, and the page
  has no scrollbar.
- **SC-004**: The listing at `/g` shows the same galleries, in the same order and with the same
  markup for its grid, as `/` did before the change, checked against a pre-change capture.
- **SC-005**: The home page renders with zero datastore reads, and the listing with the same
  number of reads as the old root page, both readable from the request timing logs alone.
- **SC-006**: 95% of home page requests complete in under 50 ms at the origin, measured from the
  timing logs.
- **SC-007**: The header geometry on home and listing matches every other public page type within
  1 px at narrow and wide widths, in the measured consistency test.

## Assumptions

- Planning inputs only (not requirements): the existing .NET server-rendered public site,
  `PublicLayout` as the one public container (constitution IX), the existing `Breadcrumb`,
  `SiteHeader` and `ContactSheet` components, the 010 speculation rules and output cache policy.
- Built on branch `010-navigation-improvements`, which is implemented but not yet merged; this
  feature changes its breadcrumbs, so it stacks on 010 and merges after it.
- The site is not live, so moving the listing needs no redirect from `/` (FR-010), consistent with
  earlier pre-production decisions.
- "Centred" is taken to mean both horizontally and vertically in the space under the header
  (overturnable: horizontal only, link near the top).
- "Big" is taken to mean the largest text size on the public site, larger than gallery titles; the
  exact size is a design-system choice made in planning (overturnable).
- The home page gets no title text, description, image or cover: the owner asked for links only,
  and the breadcrumb already names the page "Home".
- The listing's breadcrumb label is "Galleries", the same word as the home link, and its page
  title becomes "Galleries"; the home page keeps the site title it has today.
- Admin breadcrumbs are unchanged: their root remains the admin "Galleries" section, which is a
  different site with its own container (constitution IX.2).
- The design record (D-15, D-21 pages list, D-16 purge set, lifecycle "pages and caches" entries
  that name the home page) is updated in the plan as a VII.2 amendment, not here.
- Earlier specs (006, 009, 010) say "home page" for the gallery grid. Those requirements now
  belong to the listing at `/g`; the prior specs get an in-place annotation in the same change as
  the constitution amendment (FR-019), not a rewrite.
- The roadmap gets an 011 entry when this spec is accepted.
