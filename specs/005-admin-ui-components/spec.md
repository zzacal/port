# Feature Specification: Admin UI Components

**Feature Branch**: `005-admin-ui-components`

**Created**: 2026-10-30

**Status**: Draft

**Input**: User description: "i'd like to create the `/ui` components for the admin pages can we do that now?"

## Clarifications

### Session 2026-10-30

- Q: How interactive should the admin components be on /ui? → A: Static. Every state is rendered by
  the server as plain markup, including dragging, drag-over, uploading and failed, which appear as
  fixed states. No scripts are added yet. Behaviour arrives with the admin features that wire
  real data.
- Q: How are the states that had no reference handled (D-23 list)? → A: They are designed directly
  as components and reviewed on /ui, and each design choice is logged in decisions.md. This replaces
  the separate wireframe step.
- Q: Which components? → A: Everything the five D-23 admin pages need: the admin atoms, molecules,
  organisms, the way the admin uses the shared contact sheet, and the admin page template.
- Q: How far? → A: Straight through specify, plan, tasks and implement, with a decisions log
  reviewed at the end.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse the admin components next to the public ones (Priority: P1)

The owner opens `/ui/` and sees the admin components listed under their own headings, after the
public ones and grouped by atomic level. Every entry links to its own page.

**Why this priority**: This is the entry point the owner asked for. It extends the index from
feature 003.

**Independent Test**: Open `/ui/`, check that every admin component listed in FR-001 appears once
under the right admin level, and follow every link.

**Acceptance Scenarios**:

1. **Given** the app is running locally, **When** the owner opens `/ui/`, **Then** the public
   entries are unchanged, and the admin entries follow them under "Admin atoms", "Admin molecules",
   "Admin organisms" and "Admin templates".
2. **Given** the index, **When** the owner follows any admin link, **Then** that component's page
   loads.

---

### User Story 2 - See every admin component in every state (Priority: P1)

The owner opens an admin component's page and sees the component alone, rendered by the real
component code with sample content, once for each labelled state in FR-006. That includes the
states no reference covered, which are designed here for the first time: claiming the site, being
refused, an upload being prepared or interrupted, moving a photo with the keyboard, the warning
before publishing, the cover marker, a field saving and saved, and confirming an image delete.

**Why this priority**: This is the request. The owner reviews and corrects the admin's look here
before any admin page is built.

**Independent Test**: Open each admin page and check that every state listed for it in FR-006 is
shown and labelled.

**Acceptance Scenarios**:

1. **Given** an admin component page, **When** it loads, **Then** each state appears once with a
   label naming it.
2. **Given** any state that involves movement or progress (dragging, drag-over, uploading,
   keyboard-lifted), **When** the page loads, **Then** that state is shown fixed in place, without
   scripts.
3. **Given** a destructive control, **When** it is shown, **Then** it names what will be deleted,
   says what happens to related items, and is red text without a solid fill.

---

### User Story 3 - See the admin pages assembled (Priority: P2)

The owner opens the page-level organisms (gallery builder, sign-in card, the admin uses of the
contact sheet) and the admin page template. They show how the components combine into the five
D-23 pages, at desktop, tablet and phone widths.

**Why this priority**: It shows the components work together and match direction A ("Site with a
frame"). It depends on the smaller components existing first.

**Independent Test**: Open the gallery builder and the admin contact-sheet page at large, medium
and small widths. The grid keeps the public column counts and the side panel moves below the grid
on small screens.

**Acceptance Scenarios**:

1. **Given** the admin contact-sheet page, **When** it loads, **Then** it shows the library (with
   selection), the gallery list (with private galleries dimmed) and the upload batch, each using the
   same grid as the public pages.
2. **Given** the gallery builder, **When** the window is narrower than the large breakpoint,
   **Then** the side panel stacks below the grid and nothing overflows sideways.

---

### Edge Cases

- A batch item has no preview yet (queued before its file is read): the cell shows the file name
  instead of a picture, and the layout doesn't shift when a preview appears.
- A batch item's file name is longer than the cell: it truncates with an ellipsis, like titles.
- A gallery has no photos: the builder shows an empty grid message with a link to the library,
  not an error.
- A gallery has no pinned cover: the first photo shows an automatic-cover marker, and every other
  photo offers "Make cover".
- Selection is empty: the selection bar renders nothing, not a bar showing "0 selected".
- A refused file has a very long name: the refusal line truncates the name but always shows the
  reason in full.
- A control that does something isn't available yet (Confirm while photos are still uploading):
  the reason is said next to it, so the owner isn't left facing a dead button.
- The owner tabs through a page: every control shows a visible focus ring.

## Requirements *(mandatory)*

### Functional Requirements

**Components**

- **FR-001**: The site MUST have these admin components, each built once and reused wherever it
  appears:
  - atoms: Button, Input, Switch, SelectMark
  - molecules: BatchItem, CoverMarker, DragHandle, SelectionBar, DangerConfirm, VisibilityToggle
  - organisms: OwnerBar, SidePanel, UploadDropzone, BatchFooter, GalleryBuilder, SignInCard, plus
    the admin uses of the existing ContactSheet
  - template: AdminLayout
- **FR-002**: Admin pages MUST reuse the public grid. ContactSheet and Tile MUST accept optional
  admin additions (per-item controls laid over the cell, a per-item state, cells supplied by the
  caller). When those additions are not used, the public output MUST stay as it is.
- **FR-003**: Controls MUST follow the owner's standing rules: at most one filled action per bar
  or panel; destructive actions as red text with no solid fill; per-item controls inside the item
  they act on; limits stated before the owner picks files; working screens don't greet the owner
  with errors, and an unavailable action says why next to it.
- **FR-004**: Every admin component MUST be usable with the keyboard alone, with a visible focus
  indicator, and every icon-only control MUST have an accessible name.
- **FR-005**: Components MUST NOT carry inline style attributes and MUST render without scripts.
  Form controls MUST be real form elements (buttons, checkboxes, file inputs, disclosure
  elements), so they keep working when the behaviour features add scripts as enhancement.

**The /ui pages**

- **FR-006**: Each admin component MUST have its own `/ui/` page showing these states, each
  labelled:
  - Button: primary, secondary, quiet, danger, disabled, busy; and a link styled as a button in
    the same row as real buttons.
  - Input: single-line and multi-line; empty, filled, too long (with the limit stated), saving,
    saved.
  - Switch: on, off, disabled.
  - SelectMark: not selected, selected.
  - BatchItem: queued (no preview), processing, uploading with progress, succeeded, failed with
    reason, interrupted; failed and interrupted show Restart and Delete; plus a long file name.
  - CoverMarker: pinned, automatic (first photo, not pinned), unpinned.
  - DragHandle: at rest, focused, lifted for keyboard moving (with instructions and position).
  - SelectionBar: none selected (renders nothing), one, many.
  - DangerConfirm: delete image (closed and open) and delete gallery (open).
  - VisibilityToggle: private, public, and the warning shown before making a gallery public.
  - OwnerBar: each admin page current, with a primary action, and at phone width.
  - SidePanel: at rest, saving, saved, with a danger area.
  - UploadDropzone: idle with limits, drag-over, files refused with reasons.
  - BatchFooter: in progress, ready to confirm, blocked by failed items, discard confirmation.
  - GalleryBuilder: at rest, a photo being dragged with its drop slot, a photo lifted for keyboard
    moving, the publish warning, and an empty gallery.
  - SignInCard: sign-in, claiming an unclaimed site, refused non-owner, and no cover photo
    available.
  - ContactSheet (admin): library with selection, gallery list with private galleries dimmed,
    upload batch.
  - AdminLayout: the owner bar, an empty main area, and a side panel.
- **FR-007**: `/ui/` MUST list the admin components after the public ones, grouped by admin
  level, each linked to its own page. The existing public entries MUST keep their order and
  addresses.
- **FR-008**: Admin specimens MUST use the existing sample photos. Sample text (titles, file names,
  counts, refusal reasons) MUST be realistic for this site: JPEG only, 20 MB per file, titles up to
  200 characters.
- **FR-009**: The `/ui/` pages MUST stay unlinked, non-indexable and Development-only, as in
  feature 003 FR-009 and FR-010.

**Design record**

- **FR-010**: Every design choice made for a state that had no reference (D-23 list) MUST be
  recorded in the feature's decisions log with its rationale and pivot cost. Any change to the
  component set or the admin template named in D-21 or D-23 MUST be recorded in the design
  document in the same feature (Constitution VII.2).

**Instrumentation (carried forward by Constitution IV)**

- **FR-011**: The new `/ui/` pages MUST keep producing one structured request log event each, with
  server timing exposed, through the existing request pipeline (IV.1, IV.3). No new request path
  bypasses it.

### Key Entities

- **Admin component**: one building block at one atomic level, used only on admin pages, with its
  inputs and its states.
- **Batch item view**: what the batch shows for one file: file name, size, state (queued,
  processing, uploading with percent, succeeded, failed with reason, interrupted), and an optional
  preview photo.
- **Admin grid item**: a shared-grid item plus admin additions: selected, private, cover state, and
  whether it is being moved.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From `/ui/`, the owner reaches any of the 18 admin component pages in one click.
- **SC-002**: 100% of the states listed in FR-006 are visible and labelled on their pages.
- **SC-003**: Zero inline style attributes and zero scripts appear on any `/ui/` page, public or
  admin.
- **SC-004**: The public component pages from feature 003 are unchanged. Their existing tests pass
  without modification, apart from the index entry count, which grows with this feature.
- **SC-005**: Every bar and panel specimen shows at most one filled button, and every destructive
  control has no solid fill.
- **SC-006**: Tabbing through any admin page reaches every control, and each one shows a visible
  focus indicator.
- **SC-007**: At 1440, 1024 and 390 px widths, no admin page scrolls sideways, and admin grids show
  the same column counts as their public counterparts.
- **SC-008**: 100% of requests in a 20-request run over admin `/ui/` pages produce exactly one
  request log event each, verified from the log alone.

## Assumptions

- **Planning inputs only**: components follow D-21 (Razor components, per-component scoped CSS,
  tokens single-sourced in C#) and D-23 (direction A). They are built in Gallery.Web next to the
  public components, and use the `/ui/` catalogue, specimen and sample infrastructure from feature
  003.
- D-21 names TagChip, TagEditor and UploadDropzone as admin components. The content model has no
  tags (feature 004, decision D-2), so TagChip and TagEditor are not built. D-21 is updated
  accordingly.
- D-21 says AdminLayout adds htmx, the antiforgery header and admin web components. Under the
  static answer, AdminLayout ships with no scripts in this feature. The first admin feature with
  real behaviour adds them. This is a timing change recorded in the design document, not a change
  of direction.
- Two components go beyond the clarified list because D-23 pages need them: VisibilityToggle (the
  publish warning) and SignInCard (the sign-in, claim and refusal states). They are severable if the
  owner prefers them folded into other components.
- New colour tokens for danger and success are added to the token set. The exact values are
  defaults the owner can change.
- No datastore, storage, authentication, or routes outside `/ui/` are touched. Real admin pages
  come in later features.
