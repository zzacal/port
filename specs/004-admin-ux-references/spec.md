# Feature Specification: Admin UX References

**Feature Branch**: `004-admin-ux-references`

**Created**: 2026-10-29

**Status**: Draft

**Input**: User description: "find inspiration for the admin pages and its constituent components"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See how the best tools handle each admin page (Priority: P1)

The owner opens one reference board and, for each admin page the design already calls for
(sign-in and claim, upload batch, library, gallery list with home order, gallery builder), sees
screenshots of real products that do the same job well. Each reference says what to borrow and
why it fits this site.

**Why this priority**: This is the request. The admin pages are the next thing to build, and
nothing visual has been decided for them yet.

**Independent Test**: Open the board and check that every admin page in FR-001 has at least three
captioned references, each backed by a screenshot taken from the live product.

**Acceptance Scenarios**:

1. **Given** the board, **When** the owner reads the section for one admin page, **Then** it shows
   at least three real references with screenshots and a one-line "borrow this" note each.
2. **Given** any reference, **When** the owner reads its caption, **Then** the caption names the
   source product and matches what the screenshot shows.

---

### User Story 2 - See how the best tools handle each admin component (Priority: P1)

For each admin component the design names (FR-002), the owner sees close-up examples of that
control from real products, in the states that matter: for example an upload item that is
queued, uploading, failed with a reason, and succeeded; a drag handle mid-reorder; a public/private
switch in both positions.

**Why this priority**: The owner asked for the constituent components too, and components are
where the design gets reused, so a weak choice spreads.

**Independent Test**: For each component in FR-002, count the examples on the board and the
states shown across them.

**Acceptance Scenarios**:

1. **Given** the component section, **When** the owner looks up any component from FR-002,
   **Then** it has at least two examples, and together they show every state listed for it.
2. **Given** an example, **When** the owner reads its note, **Then** the note says which state it
   shows and what to copy or avoid.

---

### User Story 3 - Choose a direction for the admin (Priority: P2)

After the references, the owner reads two or three named directions for the whole admin, each
with its source products and a one-line sketch of every admin page, plus one recommendation. They
pick one, or mix two, and that pick feeds the admin wireframes and specs.

**Why this priority**: References alone don't produce a decision. This turns them into a choice
the next feature can build on.

**Independent Test**: Check that each direction covers every page in FR-001 and that one is
marked as recommended with its reason.

**Acceptance Scenarios**:

1. **Given** the directions section, **When** the owner reads a direction, **Then** it names its
   source references and describes every admin page in one line each.
2. **Given** the directions, **When** the owner compares them against the public site's look,
   **Then** each one states how it relates to the public contact-sheet grid.

---

### Edge Cases

- A good admin reference sits behind a login or paywall: use public product pages, documentation
  screenshots, or open demo instances, and say which in the caption. Never use a login the owner
  didn't provide.
- A site animates in or loads lazily and screenshots come out blank: retry with reveal and scroll
  steps; if it still fails, drop it or mark it "unverified visually".
- A reference is strong but needs heavy scripting or a client framework: keep it only if the
  admin page it informs is allowed to need scripts (Constitution V.3 allows that for admin), and
  say so.
- A component has no clear real-world example for one of its states: say so on the board as a
  gap rather than inventing one.
- A reference looks good but contradicts an already accepted rule (for example, it shows a
  confirm-free bulk publish, or auto-fills required fields with nags): list it under "set aside"
  with the rule it breaks.

## Requirements *(mandatory)*

### Functional Requirements

**Coverage**

- **FR-001**: The research MUST cover each admin page already defined in the design: sign-in and
  first-time claim (including the refusal shown to a non-owner), the upload batch, the library of
  all images (with per-image title and caption editing and hard delete), the gallery list with
  manual home order and public/private visibility, and the gallery builder (add and remove images,
  reorder, pin a cover, edit title and description, publish).
- **FR-002**: The research MUST cover each admin component the design names or the pages above
  require, with these states:
  - Button: primary, secondary, danger, disabled, busy.
  - Input (single-line and multi-line text): empty, filled, too long, saving, saved.
  - Upload drop zone: idle, dragging over, files refused before upload with a reason (wrong type
    or over the size limit).
  - Upload batch item: queued, processing, uploading with progress, succeeded, failed with reason
    and Restart/Delete, interrupted with Restart/Delete.
  - Batch summary: in progress, all succeeded and ready to confirm, blocked by failed items,
    discard.
  - Library grid with selection: none, one, and many selected.
  - Reorder list or grid: at rest, item being dragged, keyboard reorder.
  - Visibility switch: public and private, plus the warning shown before making something public.
  - Cover marker: pinned and unpinned.
  - Destructive confirmation: deleting an image (no undo) and deleting a gallery.
  - Admin shell (header and navigation between admin pages): signed in, with a link to the
    public site.
- **FR-003**: References MUST include photography-specific tools (for example photo library,
  portfolio, and image-hosting admin screens) and general content-management admin screens, so
  photo-specific and generic patterns can be compared.
- **FR-004**: Each admin page MUST have at least three references and each component at least
  two, taken from at least eight distinct products in total.

**Evidence and honesty**

- **FR-005**: Every reference MUST be backed by a screenshot taken from the live product, its
  public documentation, or a public demo, and record its source address and the date taken.
  Descriptions that could not be checked against a screenshot MUST be marked "unverified visually"
  or dropped.
- **FR-006**: No reference MAY be gathered by signing in with credentials the owner did not
  provide, and no screenshot MAY show another person's private data beyond what the source
  publishes.
- **FR-007**: Products that were checked and rejected MUST be listed with a one-line reason.

**Deliverable**

- **FR-008**: The research MUST produce one self-contained reference board the owner can open
  locally, with these sections in order: references per admin page; references per component;
  checked and set aside; patterns that repeat (each with where it was seen and whether it fits the
  constitution and the accepted design); and two or three named directions with one
  recommendation.
- **FR-009**: Each direction MUST describe every admin page from FR-001 in one line, name its
  source references, and state how it relates to the public site's contact-sheet look.
- **FR-010**: Each reference and pattern MUST be judged against the owner's already stated admin
  preferences and say where it agrees or conflicts: one filled primary action per toolbar;
  outlined, not solid, danger actions; per-item controls inside the item they act on; every
  managed item links to its public page; bulk working screens don't greet the owner with
  validation errors; input limits are enforced when files are picked, not after submit.
- **FR-011**: Jargon used for a direction or pattern MUST be defined in plain words in the same
  line.
- **FR-012**: The board, its notes, and screenshots MUST be committed alongside the existing
  public-site research so both boards live together.

**Out of scope**

- **FR-013**: This feature MUST NOT produce wireframes, components, pages, or code, and MUST NOT
  change the design record. Accepting a direction is a later step that records a design decision.

### Key Entities

- **Reference**: one screenshot of a real product, with its source, date, the admin page or
  component it informs, the states it shows, what to borrow, and any conflict with stated
  preferences.
- **Admin page**: one of the five pages in FR-001.
- **Admin component**: one of the controls in FR-002, with its list of required states.
- **Pattern**: a treatment seen in several references, with its sources and a fit verdict.
- **Direction**: a named overall approach to the admin, built from references, covering every
  admin page.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 5 of 5 admin pages have at least 3 references each.
- **SC-002**: Every component in FR-002 has at least 2 examples, and 100% of its listed states
  are shown or recorded as a gap.
- **SC-003**: At least 8 distinct products are referenced.
- **SC-004**: 100% of references have a screenshot, source address, and date; zero captions
  contradict their screenshot when checked.
- **SC-005**: The owner can pick a direction after reading the board in one sitting of about 15
  minutes, without needing to open any source site.
- **SC-006**: Each of the two or three directions describes all 5 admin pages.

## Assumptions

- The admin pages and components to research come from the accepted design (D-04 sign-in and
  claim, D-08 to D-11 upload and batch, D-13 and D-14 content model and visibility, D-21 admin
  atoms and organisms) and the content model in `docs/design.md`. The research takes those as the
  page list; it does not add admin capabilities.
- The design names `TagChip` and `TagEditor`, but the content model has no tags (titles and
  captions are the only owner-written text, feature 001 FR-009). They are left out of the
  component list. If tags are wanted, that needs its own spec.
- The process follows the earlier public-site research in `docs/ux-research/` (screenshots of live
  sites, a mood board, named directions), so the admin board sits next to the public one.
- Admin pages may need scripts (Constitution V.3), so script-heavy references are acceptable for
  admin. References are still judged on calm, minimal chrome so the admin feels like the public
  site.
- Planning inputs only: the admin is built with server-rendered pages plus htmx and web components
  (Platform Constraints, D-21). That affects which patterns are cheap to build, and the directions
  say so. It is not a requirement of the research itself.
- Desktop-first: the owner uploads and curates from a computer. Phone-width admin is noted where
  references show it but is not a coverage requirement.
- No structured timing logs apply: this feature produces documents and screenshots, not running
  code (Constitution IV applies to requests and operations, which this feature has none of).
