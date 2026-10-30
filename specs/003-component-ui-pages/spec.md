# Feature Specification: Component UI Pages

**Feature Branch**: `003-component-ui-pages`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "create /ui/ pages for the atomic components so that i can see all the components individually and make changes as I want to"

## Clarifications

### Session 2026-10-27

- Q: None of the components exist yet. Which ones are in scope? → A: The public set only. That is
  the tokens; the atoms Picture, Title, TextLink and ExifField; the molecules Tile, MetadataBar,
  Pager, Brand and Nav; the organisms ContactSheet, SoloViewer and SiteHeader; and the
  PublicLayout template. Admin components come later, with the admin features.
- Q: What does "make changes as I want to" mean? → A: Nothing for this feature. The pages are for
  viewing only. The owner will change components through future specs. Live reload and in-page
  editing controls are out of scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse every component from one index (Priority: P1)

The owner opens `/ui/` and sees every design token and public component grouped by level
(tokens, atoms, molecules, organisms, templates). Each entry links to that component's own page.

**Why this priority**: It is the entry point. Without it the owner has to know every address.

**Independent Test**: Open `/ui/` and confirm that every component listed under Scope appears
once, under the right level, and that every link opens a page.

**Acceptance Scenarios**:

1. **Given** the app is running locally, **When** the owner opens `/ui/`, **Then** the page lists
   all 13 public components and the tokens, grouped by atomic level, each one linked.
2. **Given** the index, **When** the owner follows any link, **Then** that component's page loads.

---

### User Story 2 - See each component on its own, in all its states (Priority: P1)

The owner opens one component's page. It shows just that component, rendered from the real
component code with sample content, once for each state that matters. For example: a title short
enough to fit and one long enough to be cut off; metadata with every field, with some fields, and
with none; a pager on the first, a middle, and the last image.

**Why this priority**: This is the request: seeing each UI element on its own.

**Independent Test**: Open each component page and confirm that every state listed for it in
FR-007 is shown and labelled, and that nothing else from the site surrounds it.

**Acceptance Scenarios**:

1. **Given** a component page, **When** it loads, **Then** each state of the component appears
   with a short label naming the state.
2. **Given** a component page, **When** the owner inspects it, **Then** the component is the same
   output the real site pages will use, not a copy or a mock-up.
3. **Given** the ExifField and MetadataBar pages, **When** a field has no value, **Then** nothing
   is shown for it: no dash, no empty label.

---

### User Story 3 - See the grid and viewer at every screen size (Priority: P2)

The owner opens the ContactSheet and SoloViewer pages and resizes the browser window between
desktop, tablet, and phone widths. The column counts change as specified, and every photo stays
whole and centred in its cell.

**Why this priority**: These two organisms carry most of the agreed visual design (D-20). It
matters, but only after the components exist and can be browsed.

**Independent Test**: Open the ContactSheet page at a large, a medium, and a small window width
and count the columns in each configuration shown.

**Acceptance Scenarios**:

1. **Given** the ContactSheet page shows the home configuration, **When** the window is large,
   medium, then small, **Then** it shows 3, 3, then 1 columns.
2. **Given** the ContactSheet page shows the gallery configuration, **When** the window is large,
   medium, then small, **Then** it shows 4, 3, then 1 columns.
3. **Given** sample photos in landscape, portrait, square, and panorama shapes, **When** they
   appear in the grid, **Then** each one is shown whole (never cropped), centred both ways in an
   identical cell, with its title below.

---

### Edge Cases

- A title is longer than the cell is wide: it stays on one line and ends with an ellipsis.
- A photo is much taller than the cell (portrait) or much wider (panorama): it is shown whole and
  centred, and the rest of the cell is page background.
- A grid has fewer photos than one row: the cells keep the same size and don't stretch to fill
  the row.
- A pager on the first or last image: the missing direction shows nothing. There is no disabled
  link.
- A visitor or search engine reaches `/ui/` on the hosted site: they must not be able to see it
  (FR-010).
- A sample image file fails to load: the placeholder colour and blur stay visible in its place,
  and the layout does not shift.

## Requirements *(mandatory)*

### Functional Requirements

**Components**

- **FR-001**: The site MUST have the design tokens and these 13 public components, each built
  once and reused everywhere it appears:
  - atoms: Picture, Title, TextLink, ExifField
  - molecules: Tile, MetadataBar, Pager, Brand, Nav
  - organisms: ContactSheet, SoloViewer, SiteHeader
  - template: PublicLayout
- **FR-002**: Each component MUST behave as defined in the design (D-20, D-21), including:
  - Picture shows the smallest sufficient image size for the space it fills, reserves its exact
    shape before the image loads, and shows the image's placeholder until then.
  - Tile shows the whole photo centred in a fixed-shape cell with the title only below it.
  - ContactSheet takes a column count for each of the large, medium, and small window classes.
  - SoloViewer shows the photo with the metadata line underneath and previous/next links.
- **FR-003**: Components MUST NOT carry inline style attributes. All styling comes from the
  shared stylesheet, so a later visual change is made in one place (D-21).
- **FR-004**: Components MUST render without scripts (Constitution V.3).

**The /ui/ pages**

- **FR-005**: `/ui/` MUST list the tokens and every component from FR-001, grouped by atomic
  level, each linked to its own page.
- **FR-006**: Each component MUST have its own page under `/ui/` that shows only that component,
  using the real component with sample content. The page has a plain heading and labels and no
  other site elements.
- **FR-007**: Each page MUST show these states, each one labelled:
  - Tokens: every spacing value, breakpoint, the cell shape, the type sizes, and the colours, with
    their values.
  - Picture: landscape, portrait, square, and panorama; loaded eagerly at high priority and loaded
    lazily.
  - Title: a short title, and a title long enough to be cut off.
  - TextLink: a normal link.
  - ExifField: each of the six fields with a value (camera, lens, focal length, aperture, shutter
    speed, ISO), including a shutter speed of one second or longer, and one field with no value.
  - Tile: a landscape photo, a portrait photo, and a long title.
  - MetadataBar: all fields, some fields, and no fields.
  - Pager: the first image, a middle image, and the last image.
  - Brand, Nav, and SiteHeader: the default rendering.
  - ContactSheet: the home configuration (3/3/1), the gallery configuration (4/3/1), and a
    configuration with fewer photos than one row.
  - SoloViewer: a landscape photo with full metadata, and a portrait photo with partial metadata.
  - PublicLayout: an empty page body showing the layout itself.
- **FR-008**: Sample content MUST include photos in landscape, portrait, square, and panorama
  shapes, each with its sizes and placeholder prepared the same way real uploads will be, so the
  pages show real behaviour. The sample photos MUST carry no location data.
- **FR-009**: `/ui/` pages MUST NOT be linked from any public page and MUST be marked so search
  engines do not index them.
- **FR-010**: Until owner sign-in exists, `/ui/` MUST only be reachable when the app runs locally
  for development. In any other environment it MUST respond as not found. When owner sign-in
  arrives, access becomes owner-only (D-21).

**Instrumentation (carried forward from feature 002 by Constitution IV)**

- **FR-011**: Every request MUST produce one structured log event with route, status, and duration
  (IV.1). Every page response MUST tell the browser how long the server spent (IV.3). These are
  the site's first real pages, so this lands here.

### Key Entities

- **Design token**: a named shared value (a spacing step, breakpoint, cell shape, type size, or
  colour) that components use instead of literal values.
- **Component**: one building block at one atomic level, with its inputs and its states.
- **Sample photo**: a fixture photo with its shape, dimensions, prepared sizes, placeholder, title,
  and a metadata set (full, partial, or empty). It exists only to show the components.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From `/ui/`, the owner reaches the page of any of the 13 components, or the tokens
  page, in one click.
- **SC-002**: 100% of the states listed in FR-007 are visible and labelled on their pages.
- **SC-003**: At large, medium, and small window widths, the ContactSheet shows exactly the
  configured column counts in every configuration on its page.
- **SC-004**: No photo on any `/ui/` page is cropped. Every sample photo's full image is visible
  inside its cell.
- **SC-005**: No `/ui/` page moves its layout when images finish loading.
- **SC-006**: Outside local development, every `/ui/` address returns not found.
- **SC-007**: 100% of requests in a 20-request run produce exactly one request log event each,
  verified from the log alone.
- **SC-008**: Zero inline style attributes appear in any component's output.

## Assumptions

- **Planning inputs only**: the components follow D-20 (contact-sheet grid) and D-21 (atomic
  architecture: server-rendered Razor components through minimal-API endpoints, per-component CSS
  combined into one stylesheet) in `docs/design.md`. They are built in the Gallery.Web project
  from feature 002.
- `/ui/` replaces the `/styleguide` address named in D-21. The design entry is updated during
  planning (Constitution VII.2).
- Sample photos are a small committed set (one per shape, plus a few more to fill grid rows).
  They are the owner's own photos or openly licensed ones, prepared ahead of time. Real uploads
  come in a later feature, so how the samples are prepared is a planning decision.
- There is no datastore or storage work: sample content is fixed fixture data.
- Behaviours that belong to whole public pages (Speculation Rules, view transitions between
  pages, edge caching, Early Hints) are carried by PublicLayout's markup where D-21 places them,
  but they are proven when the real public pages ship, not here.
- The visual values (spacing, breakpoints, 4:3 cell, type sizes, colours) are the D-20/D-21
  defaults. Changing them is future spec work.
