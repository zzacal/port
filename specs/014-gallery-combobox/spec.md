# Feature Specification: Galleries Combobox in the Photo Edit Panel

**Feature Branch**: `014-gallery-combobox`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "improve the photo edit dialog's galleries section by replacing it with a combobox where galleries associated with the image are listed in a combobox's textbox and typing in some character initiated a search on existing galleries to list them out and make a selectable list. at the bottom of this list should be to \"+ [text]\" if the text is not a perfect match with an existing gallery. This causes a new gallery to be created and the photo to be added to that newly created gallery."

## Clarifications

### Session 2026-11-06

- Q: Accept the spec's defaults (apply on Save; validate all before any change; keep the three severable seams)? → A: Yes, all accepted by the owner ("I like your choices").
- Owner direction (during tasks): "I intend to make this a reusable component. Add this to the component library." → The field is a generic multi-value combobox component in the component catalogue, with its own catalogue page; galleries are its first use (FR-020, FR-021).
- Owner direction (after analyze): "research how tags are added in successful design systems … follow the important principles" → research-notes/tag-input-patterns.md; changes: first match highlighted, no placeholder state, announced counts/picks/removals, one-line options with the public warning moved to the pick, truncation, 44 px remove targets, Backspace described (FR-002, FR-005, FR-008, FR-010).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and add galleries from the photo panel (Priority: P1)

The owner opens a photo's edit panel in the library. Where the read-only "In galleries" list used
to be, a single gallery field shows every gallery the photo is in as a token inside the field. The
owner types a few characters; a list drops down under the field showing the existing galleries
whose titles contain that text and that do not already hold the photo. The owner picks one (mouse
or keyboard), it joins the tokens, and on Save the photo is added to that gallery.

**Why this priority**: Today the panel only lists memberships; adding a single photo to a gallery
means leaving the panel for the bulk selection bar and its picker. This is the core of the request.

**Independent Test**: Open a photo in no gallery, type part of an existing gallery's title, pick it,
press Save; the panel reloads showing that gallery as a token and the gallery's builder page lists
the photo.

**Acceptance Scenarios**:

1. **Given** a photo in galleries "Coast" and "Winter", **When** the owner opens its panel, **Then** the gallery field shows "Coast" and "Winter" as tokens, in title order, and nothing else.
2. **Given** galleries "Coast", "Coastline" and "Desert" exist and the photo is in "Coast", **When** the owner types "coa", **Then** the list shows "Coastline" only (case-insensitive, "Coast" is excluded because the photo is already in it, "Desert" does not match).
3. **Given** the list is showing, **When** the owner uses Down/Up arrows and Enter, or clicks an option, **Then** that gallery becomes a token, the typed text clears, and the list closes.
4. **Given** the owner picked a gallery, **When** they press Save, **Then** the photo is added to that gallery and the panel shows it among the tokens with the existing "Saved" state.
5. **Given** the owner picked a gallery, **When** they leave without saving, **Then** no membership changes.
6. **Given** a listed gallery is public, **When** it appears as an option, **Then** the option reads "{title} · Public" on one line; **When** the owner picks it, **Then** one line under the field warns "{title} is public: visitors will see this photo after you save."

---

### User Story 2 - Create a gallery from typed text (Priority: P1)

The owner types a title no existing gallery has. The last row of the list reads "+ {typed text}".
Picking it adds a token for a new gallery with that title; on Save the gallery is created private and
the photo is added to it.

**Why this priority**: Named explicitly in the request; it removes the trip to the galleries page to
create a gallery first.

**Independent Test**: Type "Night walks" (no such gallery), pick "+ Night walks", Save; a new private
gallery "Night walks" exists holding exactly this photo, and the panel shows it as a token.

**Acceptance Scenarios**:

1. **Given** no gallery is titled "Night walks", **When** the owner types "Night walks", **Then** the list ends with the row "+ Night walks", below any matching galleries.
2. **Given** a gallery titled "Coast" exists, **When** the owner types "coast" or " Coast " (case and surrounding spaces differ only), **Then** no "+" row is shown, because that counts as a perfect match; "Coast" is offered (or, if the photo is already in it, nothing is offered).
3. **Given** the owner types "Coas", **When** the list shows, **Then** it lists "Coast" and ends with "+ Coas".
4. **Given** the owner picked "+ Night walks", **When** they press Save, **Then** one private gallery "Night walks" is created, the photo is its only item, and it is placed last in the owner's gallery order like any other new gallery.
5. **Given** the typed text is longer than the gallery title limit, **When** the list shows, **Then** the "+" row is not offered and the list says the title is too long (the server refuses it too).

---

### User Story 3 - Remove a gallery token (Priority: P2)

Each token has a remove control. Removing a token and pressing Save takes the photo out of that
gallery; the photo itself is not deleted.

**Why this priority**: Not literally asked for, but a field that shows memberships as tokens reads as
editable; a token that can't be removed is a dead end. Severable seam (see Assumptions).

**Independent Test**: For a photo in "Coast", remove the "Coast" token and Save; the "Coast" builder
no longer lists the photo and the photo is still in the library.

**Acceptance Scenarios**:

1. **Given** a photo in "Coast", **When** the owner activates the token's remove control (or presses Backspace in the empty text box), **Then** the token disappears from the field.
2. **Given** the owner removed "Coast", **When** they press Save, **Then** the photo is taken out of "Coast" exactly as the builder's remove does today, including its cover and visitor-page effects.
3. **Given** "Coast" is public and the photo is its last image, **When** the removal is saved, **Then** the gallery stays public but drops out of public listings (existing empty-gallery rule).

---

### Edge Cases

- Empty or whitespace-only text: no list, no "+" row.
- Text matching no gallery: the list holds only the "+" row.
- Every matching gallery already holds the photo: only the "+" row (or nothing, when the text is a perfect match).
- Many matches: the list shows at most 10, title order, so the "+" row is always visible without scrolling the page.
- Typed text matching a gallery the owner just removed as a token in this edit: it is offered again.
- Picking "+ X" and then typing X again: the pending new gallery counts as a match, so no second "+ X" is offered; two new galleries with the same title are never created from one save.
- Another tab deleted a picked gallery before Save: the save checks every named gallery before changing anything, so it reports "That gallery no longer exists." and applies no gallery change; the panel reloads with current memberships.
- A title-or-caption error on Save: nothing about galleries is applied, and the picked tokens survive the re-rendered panel.
- Esc closes the list and keeps the typed text; with the list closed, Esc does nothing.
- Text typed but never picked is not saved; leaving the field with text in it announces "Not added".
- Nothing matches and nothing can be created (exact match already a token): the field announces "Already added".
- Duplicate gallery titles already in the data: each is a separate option; the "+" row is hidden when any one matches.

## Requirements *(mandatory)*

### Functional Requirements

Display

- **FR-001**: The photo edit panel MUST replace the "In galleries" heading-and-list section with one gallery field labelled "Galleries".
- **FR-002**: The field MUST show each gallery holding the photo as a token, in title order, and MUST show a visible line "Not in a gallery" when the photo is in none. Placeholder text MUST NOT carry a label, hint or state.
- **FR-003**: Each saved token MUST still lead to that gallery's admin page (the link the old list had), without interfering with the remove control.

Search and pick

- **FR-004**: Typing at least one non-space character MUST show a list of existing galleries whose title contains the trimmed text, compared case-insensitively, excluding galleries already present as tokens.
- **FR-005**: The list MUST show at most 10 galleries in title order, each on one line as "{title} · Public|Private". Picking a public gallery MUST show one warning line under the field until the pick is saved or removed: "{title} is public: visitors will see this photo after you save."
- **FR-006**: The last row of the list MUST read "+ {text}" (trimmed text) whenever the trimmed text is not a case-insensitive exact match for any existing gallery title or pending new token; it MUST be absent otherwise.
- **FR-007**: The "+" row MUST be withheld, with an inline message, when the trimmed text breaks the gallery title rule (over the title limit).
- **FR-008**: The field and list MUST be fully operable from the keyboard (Down/Up to move, Enter to pick, Esc to close) and MUST expose combobox/listbox semantics to assistive technology. Focus MUST stay in the text box, the first matching row MUST be highlighted as the owner types so Enter takes it, and the number of options, picks and removals MUST be announced ("3 options", "No matches", "Added Coast", "Removed Coast").
- **FR-009**: Picking an option or the "+" row MUST add a token, clear the text and close the list. A pending (unsaved) token MUST look different from a saved one.

Remove (severable seam, User Story 3)

- **FR-010**: Each token MUST have a remove control labelled "Remove from {title}" with a 44×44 px touch target; Backspace in an empty text box MUST remove the last token, and the field's description MUST say so. Long token labels MUST truncate with the full title still available.

Save

- **FR-011**: Membership changes MUST apply only when the owner presses the panel's Save, together with title and caption; leaving the panel discards them.
- **FR-012**: On Save, each "+" token MUST create one private gallery with that title (placed last in the owner's gallery order) and add the photo to it; each existing-gallery token added MUST add the photo through the same add rule the bulk picker uses; each removed token MUST take the photo out through the same rule as the builder's remove.
- **FR-013**: A new gallery created here MUST be private (Constitution II.5); adding the photo to a public gallery MUST trigger the same visitor-page refreshes as the bulk add does today, and a removal the same refreshes as the builder's remove.
- **FR-014**: The server MUST validate every requested change (title rule, gallery exists, photo exists) before applying any gallery change; a refusal MUST be shown in the panel with the reason, and title/caption validation failures MUST apply no gallery change.
- **FR-015**: After a successful Save the panel MUST re-render from stored data, showing the photo's actual memberships, with the existing "Saved" state.
- **FR-016**: The save request MUST be protected against cross-site request forgery like every other admin write.

Observability (Constitution IV.1–2, IV.4)

- **FR-017**: The save MUST emit a named timing event for the whole membership update and one per created gallery, add and removal, each with duration and outcome, so SC-002 is verifiable from logs alone.

Scope guards

- **FR-018**: The bulk "Add to gallery…" picker on the selection bar MUST be unchanged by this feature.
- **FR-019**: Existing tests MUST pass unmodified except those listed in `contracts/test-impact.md` (the removed "In galleries" markup and the catalogue's index count and baselines), each with old and new expected output.

Reuse (owner direction)

- **FR-020**: The field MUST be built as a reusable component of the component library that knows nothing about galleries: its label, values, searchable options, field names, create rule and messages are supplied by the page using it.
- **FR-021**: The component catalogue MUST list the component with specimens of its states: empty, with values, typing with matches and the create row, a highlighted option, an exact match with no create row, create row only, pending values, and text over the limit.

### Key Entities

- **Gallery** (existing): title, visibility, items. Gains no new attributes; can now be created from the photo panel.
- **Photo membership edit** (new, transient): the photo id plus the set of galleries to add (existing ids), new titles to create, and galleries to leave, sent with one Save.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The owner can add a photo to an existing gallery from its panel in one typed fragment, one pick and one Save, without leaving the panel (previously: close panel, tick the photo, open the bulk picker, pick, submit).
- **SC-002**: The owner can create a new gallery holding the photo in one Save; the save completes in under 1 second at p95 locally, read from the timing events.
- **SC-003**: The list updates within 100 ms of each keystroke with up to 200 galleries (no visible lag; no request per keystroke).
- **SC-004**: 100% of saves that create a gallery leave it private, and 0 saves create two galleries from one "+" token.
- **SC-005**: Every flow in User Stories 1–3 is completed with keyboard only in a browser test.

## Assumptions

- "Photo edit dialog" is the library's side panel opened by a tile's Edit link (`/admin/library?open={id}`, LibraryPage "Photo" panel). The builder page and the bulk picker are out of scope.
- Default (clarify may overturn): changes apply on the panel's Save, not instantly per pick, so the panel keeps one commit point and a typed title never ships half-saved. The alternative is apply-on-pick with no Save involvement.
- Default: matching is case-insensitive substring on the trimmed text; "perfect match" means a case-insensitive, trimmed exact title match. No fuzzy matching.
- Design principles (owner direction "follow the important principles of a good design of this kind of feature"): the component follows the 12 principles in `research-notes/tag-input-patterns.md`, drawn from W3C APG, Fluent 2, Primer, Atlassian, Polaris, Carbon, Material 3 and GOV.UK. Consciously not adopted: Tab/blur turning typed text into a value, and inline completion.
- Reuse (owner direction): the component is generic; the photo panel is its first consumer. Its interactive behaviour ships in the admin script the library page already loads, so a future page using it must load the same script.
- Default: search runs over the galleries the library page already loads for the bulk picker, so typing makes no server requests (SC-003). Owner gallery counts are small (single owner).
- Severable seams, not literally requested: (1) token removal (User Story 3, FR-010) — a field that shows memberships as tokens implies it; cutting it leaves tokens read-only and removal on the builder page; small. (2) the public-gallery warning on pick (FR-005) — cutting it loses the only warning before a photo goes public by being added. (3) token links to the gallery admin page (FR-003) — keeps what the old list offered.
- Admin pages may require JavaScript (Constitution V.3); the field needs scripting. Without scripts the panel still shows saved tokens as links.
- No new dependency is expected; the existing admin script handles the field (planning input, not a requirement).
- Constitution II.5 (nothing public as a side effect) is satisfied because new galleries are private and adding to an already-public gallery is an explicit owner pick with the visibility note shown.
- Probed 2026-11-06: the panel today renders an "In galleries" `h3` with a link list or "Not in a gallery" (`LibraryPage.razor:120-133`); creating a gallery and adding photos already exist as service operations used by the bulk add (`ImageService.BulkAddAsync`); single-photo removal exists on the builder (B5). No combobox/listbox exists in the codebase.
