# Component and Script Contract: 020 Admin Workspace

The picture of each state is `docs/ux-research/admin-preview-v2.html`; this file fixes structure,
parameters and behaviour. Rules for every component here:

- Existing design tokens only. No new token, no `style=` attribute, no colour or size literal
  except the photo-overlay values `Tile` and `SelectMark` already use.
- New CSS classes are prefixed `ws-` (none exists today; checked by grep). A component's root
  class is listed below and must not be reused by another element.
- Every action control is a `Button` with visible text (FR-010). No `Icon`, no icon-only control.
  The one glyph on a photo is `SelectMark`.
- Script-built markup comes only from cloning a server-rendered `<template>` and filling it through
  `textContent`, `value`, `checked` or `setAttribute` (the 008 R-11 rule), or from a fragment the
  server rendered (C12 `swap`).

## Changed components

**K1 `AdminLayout`** (template, rewritten; catalogue entry kept). Parameters: `Title`, `Bare`
(bool; the sign-in page passes true and gets today's bar-less document), `List`, `ChildContent`,
`Panel` (render fragments), `Csrf` (FormToken, for the sign-out form in the list).
Markup: document head as today (inline styles, `noindex`), then
`<body class="admin"><admin-workspace class="ws" data-csrf data-cap data-upload-src data-worker-src>`
containing `<nav class="ws-list">@List</nav><main class="ws-main">@ChildContent</main><aside class="ws-panel">`.
The layout owns all frame geometry (IX.2): three columns at 900 px and wider (list 232 px, panel
320 px); below 900 px one column with the list as a horizontal strip and the panel as a bottom
sheet shown while `admin-workspace` has class `ws-sheet`. Pages add no frame CSS.
Removed parameters: `Current`, `Crumbs`, `Aside`, `ShowOwnerBar`, `SignOut`.

**K2 `SidePanel`**: new optional `Save` (bool, default true). False renders no save row. Default
render byte-identical. Used as the details panel body in all four contexts.

**K3 `ContactSheet`**: new optional `Fill` (bool, default false). True adds class `cs-fill`
(`grid-template-columns: repeat(auto-fill, minmax(168px, 1fr))`; two columns below 600 px) and
omits the `cs-l*/cs-m*/cs-s*` classes. Default render byte-identical.

## New components (each has a catalogue page with the listed states)

**K4 `Mark`** (atom, `ws-mark`). Parameters: `Text`. A small worded badge. States: Cover, New,
Private.

**K5 `PhotoCell`** (molecule, `ws-cell`). Parameters: `Image` (ImageView), `Caption`,
`GalleryIds`, `Cover` (bool), `New` (bool), `Selected` (bool), `Priority`.
Markup: `<div class="ws-cell" role="option" tabindex="-1" draggable="true" data-id data-title
data-caption data-galleries aria-selected>` holding one `Tile` (no `Href`, title under the photo,
`Overlay` = `SelectMark`, and a `Mark` when Cover or New). `Sizes` = `Sizes.ForFill()` (new, in
`Gallery.Core`, the `sizes` string for the fill grid).
States: plain, selected, cover, new, drop-position (`ws-drop`, a bar on the leading edge).

**K6 `GalleryChecklist`** (molecule, `ws-checks`). Parameters: `Galleries` (id, title, public),
`TitleMax`. Markup: a labelled text input (`data-gq`, label "In galleries", placeholder "Find a
gallery or type a new name"), one row per gallery (`<label data-name="{title lower-cased}">`
checkbox `data-member="{id}"`, title, "Public"/"Private"), and a button `data-mk` hidden by default.
States: unticked, ticked, partly ticked (`indeterminate`), narrowed, offering a new gallery
("New gallery “{text}”"), no galleries.
Rules live in one pure static function `GalleryChecklist.Match(query, titles)` (trim; case-
insensitive contains to narrow; exact case-insensitive equality means "existing"); the script
mirrors it and a browser parity fact pins the two.

**K7 `UploadRow`** (molecule, `ws-up`). Parameters: `FileName`, `State` (`UploadRowState`: Waiting,
Preparing, Uploading, Adding, Added, Failed, Interrupted), `Percent`, `Reason`, `ItemId`.
Words shown: "Waiting", "Preparing", a progress bar, "Adding", "Added", the reason + `Retry` +
`Remove` (Failed), "Interrupted" + `Retry` + `Remove`. Maps from the item states: queued,
processing, uploading, succeeded|joining → Adding, failed, interrupted.

**K8 `Toast`** (molecule, `ws-toast`, `role="status"`). Parameters: `Text`, `Failed` (bool). The one
place results appear (FR-018): fixed at the bottom centre of the window. States: result, refusal.

**K9 `CollectionList`** (organism, `ws-collections`). Parameters: `Current` (all | unplaced | gallery
id), `Total`, `Unplaced`, `Galleries`, `Naming` (bool), `Csrf`.
Markup, in order: brand; link "All photos" + count; link "Not in a gallery" + count; heading
"Galleries" with the note "in home page order"; one link per gallery (`data-gid`, `data-slug`,
`data-public`, `draggable`), with `Mark` "Private" and its count; `New gallery` (a `Button`), or
the naming input when `Naming`; "View site ↗" (new tab); the sign-out form ("Sign out").
Links are real `href`s to W1 to W3 (the script intercepts them).
States: a collection current, a private gallery, drag target (`ws-over`), reorder position
(`ws-before`), naming.

**K10 `UploadTray`** (organism, `ws-tray`, inside `<upload-tray>`). Parameters: `State`
(`TrayState`: Empty, Working, Hidden, Finished, FinishedWithFailures), `Destination` (title or
null), `DestinationPublic` (bool), `Rows`, `Added`, `Total`, `MaxMb`, `MaxPhotos`.
Heading line: Empty "Add photos to {destination | your library}"; Working "Adding to {…} · {added}
of {total}" + progress; Finished "{n} added to {…}"; FinishedWithFailures "{n} added · {k} failed".
[SAFETY] II.5a: when `DestinationPublic`, a visible sentence directly under the heading in every
state: "{destination} is public. Visitors will see these photos as soon as each one is added." It
is plain text, never inside `<details>`, a tooltip or a `title` attribute.
Controls: `Hide`/`Show` (when it has rows), `Add more` (finished), and the closing button: `Close`
(Empty), `Done` (otherwise; unavailable while any row is Waiting, Preparing, Uploading or Adding).
Body: `UploadDropzone` (Empty), else the `UploadRow`s. Hidden renders the heading line only.

**K11 `PhotoStage`** (organism, `ws-stage`). Parameters: `Position`, `Total`, `BackLabel`.
Markup: a bar with `Button` "← {BackLabel}", "{Position} of {Total}", `Previous`, `Next`; then an
empty frame the script fills with the cloned `<picture>`. States: first (Previous unavailable),
middle, last.

**K12 `WorkspacePage`** (page; `Admin/Pages/`). Renders `AdminLayout` with:

- List: `CollectionList`.
- Main: `PageHeading` (the collection's name; then "{n} photos", "· Public|Private" for a gallery,
  "View on site ↗" for a public gallery with photos; actions: `Gallery settings` (shown below
  900 px, gallery only), `Clear selection` (hidden until a selection exists), `Add photos`
  (Primary)); the `<upload-tray>` mount; `ContactSheet Fill` of `PhotoCell`s (first 4 at high
  priority) or the empty sentence; the "Show more" button and sentinel when more exist; a hidden
  `PhotoStage`.
- Panel templates (`<template data-panel="…">`, each a `SidePanel Save="false"`):
  - `library`: heading "Library" / "Not in a gallery", the counts sentence, four usage tips.
  - `gallery`: heading "Gallery"; `Input` Title, `Input` Description (multiline); `Switch` "Public"
    with the hint ("Visitors can see this gallery and its photos." / "Only you can see this
    gallery." / the empty-publish reason, disabled); `Move up`, `Move down`; danger slot:
    `DangerConfirm` "Delete gallery…".
  - `photo`: heading "Photo"; a preview frame; `Input` Title, `Input` Caption; `GalleryChecklist`;
    `Use as gallery cover`, `Move earlier`, `Move later` (gallery collections only);
    `Open large`; "View on site ↗" / "Not on the site yet" (unavailable); danger slot:
    `DangerConfirm` "Delete photo…".
  - `selection`: heading "{n} photos selected"; up to four small previews; `GalleryChecklist`;
    danger slot: `DangerConfirm` "Delete {n} photos…".
  Below 900 px each panel heading row also holds `Done`.
- A `<noscript>` sentence: "The admin needs JavaScript."

Empty sentences: library "No photos yet. Use Add photos, or drop files anywhere on this page.";
unplaced "Every photo is in a gallery."; gallery "This gallery has no photos yet. Use Add photos to
upload straight into it, or open All photos and drag photos onto its name."

Confirm wording (`DangerConfirm`, unchanged rules): one photo: today's
`PhotoEditPage.DeleteConsequence` text; several: today's bulk text; gallery: today's builder text.
`DeleteConsequence` moves to `Workspace/Messages.cs`.

## Removed

Components and their catalogue pages: `OwnerBar`, `SelectionBar`, `PhotoActions`, `GalleryActions`,
`GalleryBuilder`, `BatchFooter`, `BatchItem`, `DragHandle`, `CoverMarker`, `TokenCombobox`,
`VisibilityToggle`, `Icon`, `IconButton`, `Icons.cs`. Pages: `ShellPage`, `LibraryPage`,
`PhotoEditPage`, `GalleriesPage`, `GalleryPage`, `UploadPage`. Enums with no user left
(`BatchItemState`, `BatchState`, `BuilderState`, `CoverState`, `HandleState`, `AdminPage`).
Scripts: `library.js`, `reorder.js`.

## Catalogue (`/ui`)

- New entries (8): K4 to K11, each page showing every state named above. `PhotoCell`,
  `CollectionList` and `UploadTray` pages also show the 390 px arrangement.
- `templates/admin-layout`: the three regions with sample content, and the bare mode.
- `organisms/side-panel` and `organisms/contact-sheet`: one specimen appended after the existing
  ones (`Save="false"`; `Fill`).
- Removed entries (13) and the index reflect the list above: 35 entries.
- Insert the new entries where no test pins a list tail (after the last entry of their level).

## Scripts

**C12 `workspace.js`** (one module; registers `<admin-workspace>`).

- `swap(region, html)`: the only use of `DOMParser`. Parses a fragment the server answered and
  replaces the region's children.
- Navigation: clicks on collection links fetch the `main` fragment (header `X-Workspace: main`),
  `swap` it into `.ws-main` (the `<upload-tray>` element is moved across, not re-created), update
  the current mark in the list, `pushState`. `popstate` does the same without pushing. A failed
  fetch shows a refusal toast and leaves the page as it was.
- After any write that changes a gallery's existence, title, order, visibility or count: fetch the
  `chrome` fragment and `swap` the list and templates; the open panel is re-filled from the tile.
- Selection (FR-007, FR-008): click selects one; Ctrl/⌘-click and the select mark toggle;
  Shift-click selects the range from the last selected cell; Ctrl/⌘+A selects every loaded cell up
  to `data-cap`; Escape or a click on empty grid space clears. Passing the cap stops the selection
  and shows "You can select up to {cap} photos at a time." Roving `tabindex` over the cells; arrow
  keys move focus; Space toggles; Enter opens large.
- Panel: clones the template for the context, fills it from the cell's `data-*` (R-04).
- Save on leave (FR-013): `change` on a title, caption or description field posts A1/A5; the
  field's state shows saving then "Saved"; a refusal shows the message under the field, keeps the
  text, and leaves the cell's `data-title` unchanged.
- Checklist (FR-014, FR-015): tick → A7, untick → A8, for the selected ids; Enter in the field, or
  the offer button → A3 with the ids when the text matches no gallery, A7 when it matches one;
  focus returns to the field.
- Drag (mouse and pen): cells onto a gallery row → A7; cells within a gallery grid → A9; gallery
  rows within the list → A4. `Move earlier/later`, `Move up/down` send the same A9/A4 bodies.
- Cover → A10. Public switch → A6; a refusal puts the switch back. Delete → the `DangerConfirm`
  dialog, then A2/A11.
- New gallery: the list shows the naming input; Enter → A3 (no ids), then navigate to it; Escape
  or leaving the field cancels.
- Large view (R-08): `?photo=` in the address; Previous/Next and ←/→; Escape or the back button
  returns to the grid with the cell selected and scrolled into view.
- More: the sentinel's intersection or "Show more" fetches `more` and appends the cells.
- Below 900 px: `ws-sheet` is set while a selection exists or `Gallery settings` is on.
- Every result message comes from the server's `message` and is shown in `Toast`.

**C13 `upload.js`** (reworked; registers `<upload-tray>`; loaded by dynamic `import()` when `Add
photos` is pressed, files are dropped, or U2 reports a batch).

- Kept from today: register (U3), original PUT, master check (U4), worker encode, size PUTs (at
  most 4 at once), heartbeats (U5), complete (U7), fail (U6), restart (U8), remove (U9),
  reconcile from U2, the at-pick refusals, the `FR-044` PUT header rule.
- New: U3 carries `gallery` (the open collection when it is a gallery) and `visibleToVisitors`
  (true only when the tray is showing the public notice). After U7 succeeds, and for every
  `succeeded`/`joining` item found by reconcile, call U12; on success ask `workspace.js` (a
  `CustomEvent`) to fetch W4 and place the cell: first in All photos, first in Not in a gallery
  when there is no destination, last in the destination gallery when it is open; then refresh the
  chrome (at most once a second).
- `Done`/`Close` → U13, then remove the tray and the New marks.
- Removed: the footer, confirm, discard and their templates.

**C14 `upload-worker.js`**: unchanged.
