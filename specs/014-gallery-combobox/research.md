# Research: 014 Galleries Combobox

All findings come from reading the code on `main` at 2c40aea (probe script `/tmp/probe-014.sh`). No
NEEDS CLARIFICATION remains.

## R-01 Where the search data comes from

- Decision: the panel renders every gallery as a hidden listbox option, server-side, from
  `LibraryPageData.Galleries` (`LibraryReader.GalleriesAsync`, already read on every library GET for
  the bulk picker). `library.js` filters by toggling `hidden`. No JSON blob, no endpoint.
- Rationale: zero extra reads and zero requests per keystroke (SC-003). Avoids an HTML-injection
  sink; `LibrarySourceGuardTests` bans them in `library.js`. A single owner has tens of
  galleries. 200 hidden `<li>` elements are about 20 KB, which is fine for an admin page.
- Alternatives: a `<script type="application/json">` blob, rejected because it adds a `<script>`
  to the library page and options would have to be built in JS anyway. A search endpoint,
  rejected because it is a request per keystroke and a new route (V.4).

## R-02 Apply on Save, validate everything first

- Decision: the existing L2 route `POST /admin/library/images/{iid}` gains three optional
  repeated fields, `add` (gallery id), `create` (title) and `leave` (gallery id). A new
  `ImageService.SavePanelAsync` validates the text, the image, every named gallery and every
  create title before it writes anything. It then calls the unchanged `UpdateTextAsync`, then
  applies the membership changes in one `OperationScope`.
- Rationale: owner choice in the spec ("I like your choices"). Absent fields mean no membership
  change, so the existing bare-POST and text-only tests keep their meaning unchanged.
- Accepted risk: validation and writes are not one transaction. A gallery deleted in another tab
  between validation and write (milliseconds) gives the same outcome as `AddOneAsync` today
  (`NoGallery`, nothing written for that gallery). Single owner; V.4.

## R-03 Reuse the existing write gates

- add: `GalleryService.AddOneAsync(gid, iid, scope)` is public and takes a scope (THE add gate,
  FR-012 of 009).
- create: `GalleryService.CreateAsync(title)` is private, empty, appended to `homeOrder`, and
  enforces the title rule. It runs its own empty scope (no purge, L-10). Then `AddOneAsync` into
  the shared scope.
- leave: `GalleryService.RemoveAsync(gid, iid)` opens its own scope. Add a sibling,
  `RemoveOneAsync(gid, iid, OperationScope scope)`, holding the body (Touch, PullAsync, image
  `$pull`). `RemoveAsync` keeps its signature and calls it inside its own scope. This follows the
  "keep public signatures, add a sibling" rule, and B5 output stays byte-identical.
- Purge: the shared scope touches each gallery with `PurgeKind.Members`, so `CompleteAsync`
  produces exactly the union of the per-gallery P-MEMBERS sets that the bulk add and B5 produce
  today (FR-013). The camera projection runs in the same `CompleteAsync`.
- II.5: no new code writes `visibility`. The existing guard in `LibrarySourceGuardTests` covers
  `Library/`.

## R-04 Request normalisation (server)

- `add`/`leave` ids that fail `OpaqueId.IsValid` → 404 "That gallery no longer exists." (same
  wording as L3).
- Duplicates are removed (ordinal ids; `create` titles trimmed and compared case-insensitively).
- An id in both `add` and `leave` is dropped from both (no change). The client never sends this.
- `add` of a gallery that already holds the photo is skipped by the gate. `leave` of a gallery
  that does not hold it is a no-op.
- `create` titles are not checked against existing titles. G2 and L3 allow duplicate titles too.
  The client hides "+" on an exact match (FR-006).
- No count cap on the lists. The route is owner-only and CSRF-protected, and a cap is marginal
  (V.4).

## R-05 Component placement (revised by owner, 2026-11-06)

- Decision: a generic `TokenCombobox` molecule in `Components/Molecules/` with a `/ui` catalogue
  page. Galleries are only its first data; the photo panel passes them in (contracts/components.md
  K1–K3).
- Rationale: owner direction ("I intend to make this a reusable component"). This supersedes the
  first-draft choice of page-local markup (design.md's "extract on a second consumer" rule yields
  to an explicit owner decision; recorded as a VII.2 amendment).
- Generic by construction: no gallery words in the molecule. Labels, hidden-field names, the
  remove-label format, the create limit, the empty text and the warning format are parameters. Its classes are
  `combobox`/`cb-*`, not `lib-*`.
- The filtering rules live in one pure static `TokenCombobox.Match`, which renders the catalogue
  states and is unit-tested. `library.js` mirrors it, and a browser parity test types the same
  queries into the live field and compares the visible rows with `Match`'s output, so the two
  copies cannot drift.
- Catalogue cost (stated in test-impact A2, A3 and B1): index count 36 → 37, the index baseline
  gains one `li`, and one new baseline file. Insert position chosen to keep
  `NavCataloguePageTests`' TakeLast(5) unchanged.
- Script location: the behaviour stays in `library.js`. That is the admin script every page
  with interactive admin molecules already loads (DangerConfirm's fallback lives there too), so
  no new script file or tag is added. A future consumer page outside the library must load
  `library.js` too, which is noted in the molecule's header comment.

## R-06 Accessibility pattern

(Extended by `research-notes/tag-input-patterns.md`: the first row is highlighted while typing, statuses go to a polite live region, the help text describes Backspace, and there is no placeholder.)

- WAI-ARIA APG "combobox with listbox popup": the `input` has `role="combobox"`,
  `aria-expanded`, `aria-controls`, `aria-autocomplete="list"` and `aria-activedescendant`. The
  `ul` has `role="listbox"`. Each `li` has `role="option"`, a unique id, and `aria-selected` on
  the highlighted option. Focus stays in the input. Down/Up move the highlight, Enter picks it,
  and Esc closes the list (and stops propagation only when the list was open). Backspace in an
  empty input removes the last token. Tokens live in a `ul aria-label="In galleries"`, and each
  remove button is labelled "Remove from {title}".

## R-07 One filled button per state (009 UI-3)

- `LibraryBrowserTests:521` asserts that the open panel's only filled button is "Save". Token
  remove buttons are transparent, icon-like `button type="button"`. Options are `li`, not
  buttons. The existing test therefore stays unmodified and guards this.

## R-08 Timing (IV.1, IV.2, IV.4)

- `http.request POST /admin/library/images/{iid}` already exists. New: `OpTimer.Time` wraps
  `SavePanelAsync`'s membership part as `library.image.memberships`, with outcome
  `ok`/`refused`/`notfound`. The existing `db.gallery.create`, `db.gallery.update` and
  `db.image.update` cover each create/add/remove (FR-017).
- SC-002 evidence: a new `MembershipTimingEvidenceTests` (Category=Evidence) runs 100 saves,
  each creating one gallery and adding one, and writes `evidence/membership-timings.log`.
  `evidence/p95.sh` asserts that the p95 of `library.image.memberships` and of the POST
  `http.request` is under 1000 ms, with n ≥ 100.

## R-09 Script budget

- `library.js` has 287 lines and no line cap test. No new script tag: the panel already loads
  `library.js` through `Model.ScriptUrl`. The combobox code goes in a new `// ── gallery field`
  section, using event delegation like the rest of the file.
