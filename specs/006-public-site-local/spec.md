# Feature Specification: Public Site, Local Walking Skeleton

**Feature Branch**: `006-public-site-local`

**Created**: 2026-10-30

**Status**: Draft

**Input**: User description: "006 walking skeleton (docs/roadmap.md): the public site running
locally. Home grid, gallery page, and solo view served from real stored content, with images from
the dev image storage through the dev image hostname. A seed command loads the sample photos. Keep
.NET."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse the site from stored content (Priority: P1)

A visitor opens the home page and sees the public galleries as a grid of cover photos, in the
owner's order. They open a gallery and see its photos as a grid. They open a photo and see it large,
with its title, caption, camera details, and previous/next links. Everything shown comes from
stored content, not from fixture files bundled with the app.

**Why this priority**: This is the site. Every later feature (sign-in, upload, management) writes
the content these pages read.

**Independent Test**: Seed the content, start the app, and walk home → gallery → photo → next
photo → back, checking each page against the seeded content.

**Acceptance Scenarios**:

1. **Given** seeded content, **When** a visitor opens the home page, **Then** every public gallery
   appears exactly once, in the owner's order, each card showing its cover photo and title.
2. **Given** the home page, **When** the visitor opens a gallery, **Then** its photos appear in the
   gallery's order with their titles, and the gallery's title and description are shown.
3. **Given** a gallery page, **When** the visitor opens a photo, **Then** the photo appears with
   its title, its caption when it has one, the camera fields it has, and previous and next links
   within that gallery.
4. **Given** the first or last photo of a gallery, **When** it is shown, **Then** the missing
   direction shows no link.

---

### User Story 2 - Only public galleries are listed (Priority: P1)

The owner's private galleries do not appear on the home page and ask search engines not to index
them, but anyone with the link can still open them (Constitution II: private means unlisted).

**Why this priority**: Visibility is a constitutional guarantee. It must hold from the first page
that reads real data.

**Independent Test**: Seed one private gallery, confirm it is absent from the home page, then open
its address directly and confirm it renders and is marked not to be indexed.

**Acceptance Scenarios**:

1. **Given** a private gallery, **When** the home page loads, **Then** that gallery does not
   appear.
2. **Given** a private gallery's address, **When** it is opened directly, **Then** the gallery
   renders and the page tells search engines not to index it.
3. **Given** a photo opened from a private gallery, **When** it renders, **Then** that page is
   also marked not to be indexed.

---

### User Story 3 - Links survive renames and bad addresses fail cleanly (Priority: P2)

Gallery addresses contain a stable id plus a readable name. If the readable part is outdated or
missing, the visitor is sent to the current address. Addresses that point at nothing show a
not-found page.

**Why this priority**: Shared links must survive renames (II.4). Renaming isn't possible until the
management feature, but the address scheme is fixed now.

**Independent Test**: Request a gallery address with a wrong readable name and confirm a permanent
redirect to the current one. Request an unknown id and confirm not found.

**Acceptance Scenarios**:

1. **Given** a gallery address with an outdated or missing readable name, **When** it is
   requested, **Then** the visitor is permanently redirected to the current address. The same
   applies to photo addresses inside that gallery.
2. **Given** a gallery id that doesn't exist, **When** it is requested, **Then** a not-found page
   is returned.
3. **Given** a photo id that isn't in the requested gallery, **When** it is requested, **Then** a
   not-found page is returned.

---

### User Story 4 - The owner loads sample content with one command (Priority: P2)

The owner runs one command that loads the sample photos into the dev image storage and creates
sample galleries in the datastore. Running it again leaves the same result, with no duplicates.

**Why this priority**: Until upload exists, this is the only way content gets in. Every later local
feature depends on it.

**Independent Test**: Run the command on empty storage, then run it again, and compare galleries,
photos, and stored image files after each run.

**Acceptance Scenarios**:

1. **Given** empty datastore and image storage, **When** the owner runs the seed command, **Then**
   the sample galleries and photos exist and every image file the pages reference loads from the
   image hostname.
2. **Given** a seeded site, **When** the command runs again, **Then** the galleries, photos, and
   stored files are the same as before: nothing is duplicated, and image files that already exist
   are not uploaded again.
3. **Given** the seed has run, **When** the owner inspects the content, **Then** it includes at
   least two public galleries, one private gallery, one gallery with no photos, one photo in more
   than one gallery, and one photo with a caption.

---

### User Story 5 - The site feels instant (Priority: P2)

Pages paint their layout at once with every image slot already showing a preview, the next page a
visitor is likely to open is fetched before they click, and moving between grid and photo animates
smoothly where the browser supports it. Without JavaScript, every page still works completely.

**Why this priority**: Perceived speed is the owner's top priority (Constitution IV). The
components from 003 already carry most of this; this feature proves it on real pages.

**Independent Test**: Load each page type in a real browser and measure layout shift, check that
hover on a photo starts loading the destination image, and repeat with scripts disabled.

**Acceptance Scenarios**:

1. **Given** any public page, **When** it loads, **Then** nothing moves when images finish loading,
   and each image slot shows its preview until the photo arrives.
2. **Given** a grid page, **When** the visitor hovers or presses on a photo, **Then** the image the
   next page shows starts loading before the click, unless the browser asks to save data.
3. **Given** a photo page, **When** it has loaded, **Then** the previous and next photos are
   fetched in the background, unless the browser asks to save data.
4. **Given** scripts are disabled, **When** the visitor browses home → gallery → photo, **Then**
   every page renders completely and every link works.

---

### Edge Cases

- A gallery with no photos: its home card shows an empty cover cell with the title (E-16), and its
  page shows the title and no grid.
- A pinned cover is not set: the gallery's first photo is the cover.
- The home page has no public galleries: it renders the site header and an empty page, not an
  error.
- A photo has no camera details: no metadata line is shown, with no empty labels or dashes.
- A photo has a long title: it stays on one line with an ellipsis in grids, as built in 003.
- The image hostname can't be reached (the dev machine is offline): pages still render with their
  placeholders and no layout shift. Image loading fails visibly only as missing pixels.
- The datastore is down: the page returns a server error, and the failure is logged with the
  operation name and duration.
- The image storage credential is missing or wrong: the seed command stops with a clear message
  naming the missing setting, without printing any secret.

## Requirements *(mandatory)*

### Functional Requirements

**Public pages**

- **FR-001**: The home page MUST show every public gallery once, in the owner's order, as a grid of
  cards (cover photo and title) using the home grid configuration from 003.
- **FR-002**: A gallery page MUST show the gallery's title, its description when it has one, and
  its photos in the gallery's order as a grid using the gallery grid configuration from 003.
- **FR-003**: A photo page MUST show the photo, its title, its caption when it has one, the camera
  fields it has, and previous/next links within the gallery it was opened from.
- **FR-004**: The home page MUST be built from a single datastore read (IV.9). The gallery and photo
  pages MUST each need at most two datastore reads, regardless of how many photos the gallery has.
- **FR-005**: Every image on every page MUST be addressed on the image hostname. No page may contain
  an address of the storage service itself (III.9, D-19).
- **FR-006**: Pages MUST render the components built in 003 unchanged. The `/ui/` pages MUST keep
  rendering exactly as they do now.

**Visibility and addresses**

- **FR-007**: Private galleries MUST NOT appear on the home page, and their pages, along with photo
  pages opened from them, MUST tell search engines not to index them (II.2).
- **FR-008**: Private galleries and their photo pages MUST still render when opened by address
  (II.2: private means unlisted).
- **FR-009**: Gallery addresses MUST be `/g/{id}/{slug}` and photo addresses
  `/g/{id}/{slug}/{imageId}`. A request with an outdated or missing slug MUST permanently redirect
  to the current address (II.4, D-14).
- **FR-010**: Ids MUST be opaque random values that reveal nothing about titles or order (II.3).
  Unknown gallery ids, and photo ids not in the requested gallery, MUST return not found.

**Sample content**

- **FR-011**: A seed command MUST load the 003 sample photos' prepared image files into the dev
  image storage and create galleries and photos in the datastore that reference them.
- **FR-012**: The seed MUST be repeatable: a second run leaves identical content and does not upload
  files that already exist.
- **FR-013**: The seeded content MUST include at least two public galleries, one private gallery,
  one gallery with no photos, one photo that belongs to more than one gallery, and one photo with a
  caption. Gallery order on the home page is fixed by the seed.
- **FR-014**: Stored image files MUST be served with one-year immutable caching (IV.21), set when
  they are stored.

**Image storage protection (Constitution III.9)**

- **FR-015**: The app and the seed command MUST read the storage credential from configuration
  kept outside the repository. No credential, key, or storage-service address may appear in any
  committed file or served response.
- **FR-016**: The credential MUST be limited to reading and writing objects in the two image
  buckets. The feature MUST include an automated check, run against the real dev storage, that the
  credential cannot reach anything else and that the public bucket has no development address.
- **FR-017**: Storage behaviour MUST be tested against a local storage emulator on every test run,
  and the same tests MUST be runnable on demand against the real dev image storage when its
  credential is present. Storage changes are not accepted until the on-demand run passes.

**Perceived performance (Constitution IV, carried by 003's components and proven here)**

- **FR-018**: Every image slot MUST paint its stored preview and reserve its final size at first
  paint, so nothing moves when images load (IV.7, IV.8).
- **FR-019**: The primary image of each page MUST load eagerly at high priority, and all others
  lazily (IV.17).
- **FR-020**: Grid and photo pages MUST declare prerendering of gallery and photo links on intent
  (IV.11), MUST start loading the destination image on hover or press (IV.12), and the photo page
  MUST fetch the previous and next photos after it has loaded (IV.13). All three MUST be skipped
  when the browser asks to save data (IV.14).
- **FR-021**: The photo page MUST paint the gallery thumbnail full size immediately and reveal the
  sharp image over it (IV.15). Moving between a grid and a photo MUST animate the photo between the
  two pages where the browser supports it, and navigate normally elsewhere (IV.16).
- **FR-022**: Public pages MUST work completely with scripts disabled (V.3).

**Delivery headers (Constitution IV.18–20, origin side)**

- **FR-023**: Public page responses MUST be cacheable with a bounded lifetime (IV.18), MUST NOT
  forbid back/forward caching, and pages MUST NOT register unload handlers (IV.20).
- **FR-024**: Public page responses MUST carry preload hints for the critical stylesheet and the
  primary image (IV.19).

**Instrumentation (Constitution IV.1–4)**

- **FR-025**: Every request MUST produce one structured log event with route, status, and
  duration. Every datastore read and every storage operation MUST produce a named timing event with
  duration and outcome. Public pages MUST tell the browser how long the server spent.
- **FR-026**: The feature MUST include a committed script that computes each time-bound success
  criterion from the captured local log alone.

### Key Entities

- **Site**: the single site record. Holds the owner's order of all galleries and the denormalised
  cards for the public ones, so the home page is one read.
- **Gallery**: a stable id, a readable name derived from the title, a title, an optional
  description, a visibility (public or private), an ordered list of photos, an optional pinned
  cover, and its denormalised card.
- **Gallery card**: what the home page needs for one gallery: id, readable name, title, cover
  photo's sizes and preview, and photo count.
- **Photo**: a stable id, title, optional caption, pixel size, preview (colour and tiny image),
  allowlisted camera fields, the list of stored sizes with their image addresses, and the galleries
  it belongs to.
- **Stored image file**: one prepared size of one photo in one format, named by a hash of its
  content and kept in the public image storage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor reaches any seeded photo from the home page in two clicks, and every page
  on the way shows only seeded content.
- **SC-002**: 0 private galleries appear on the home page, and 100% of private gallery and private
  photo pages carry the do-not-index marker.
- **SC-003**: Over a 100-request run per page type on the dev machine, 95% of home, gallery, and
  photo responses are generated in under 50 ms of server time, verified from the log alone.
- **SC-004**: The home page logs exactly 1 datastore read per request, and gallery and photo pages
  at most 2, in 100% of requests in the run, verified from the log alone.
- **SC-005**: Layout shift on every public page type is 0, measured in a real browser at large,
  medium, and small widths.
- **SC-006**: Running the seed command twice produces identical content, and the second run uploads
  0 files.
- **SC-007**: 100% of image files referenced by seeded pages load from the image hostname with
  one-year immutable caching.
- **SC-008**: 0 occurrences of a credential, key, or storage-service address in served pages and
  committed files, checked by an automated scan.
- **SC-009**: With scripts disabled, home → gallery → photo → next → back works on every seeded
  gallery.
- **SC-010**: Every `/ui/` page renders byte-identical output before and after this feature.

## Assumptions

- **Planning inputs only**: .NET 10, server-rendered Razor components through minimal APIs (D-15,
  D-21), MongoDB in a local container, and Cloudflare R2 through its S3-compatible API (D-07, D-19)
  are the stack. Dev buckets `gallery-dev-private` and `gallery-dev-public`, image hostname
  `img-dev.zacal.me`, and a bucket-scoped token already exist (docs/roadmap.md). The credential is
  moved from the owner's local key file into .NET user-secrets during setup.
- The test storage emulator replaces MinIO, whose community edition was archived in April 2026. The
  plan picks a maintained one (SeaweedFS or S3Proxy) and records it in decisions.md.
- The private bucket is not used by this feature: nothing private is stored until upload (008).
  It is named in configuration and covered by the credential check only.
- Content is written only by the seed. There is no owner sign-in, no editing, and no upload in
  this feature, so the denormalised cards are written by the seed, not recomputed on change.
- There is no edge in front of localhost. Edge caching, purge, warming, and early hints sent by the
  edge are proven by the hosting feature; this feature proves the origin's headers only.
- The dev site needs network access to `img-dev.zacal.me` to show image pixels. Offline, pages fall
  back to their placeholders.
- The time bound in SC-003 is measured on the owner's development machine against a local datastore
  container. It is a regression floor for this machine, not a production target.
- The seed reuses the 003 sample files and their metadata as-is. They are already free of embedded
  metadata (checked in 003 by `scripts/check-metadata.sh`), and their names are already content
  hashes.
- Gallery descriptions are plain text shown under the gallery title. 003 has no description
  component, so the plan adds the smallest one needed or reuses an existing text style; this is a
  default the owner can change.
- `/ui/` pages keep rendering from the bundled sample files, not from the datastore, so they work
  without seeding.
