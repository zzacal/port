# Components and Pages Contract: 009

Rules (from the owner's UI review conventions and 005/008):
- Components stay organised by atomic level (D-21). No new catalogue component: page-specific
  markup (the gallery picker, status lines) lives in the pages, since it has one consumer
  (D-21 "extracted only when it has a second consumer"). `UiCatalog`, every `/ui/` page and the
  index stay untouched, so `UiPagesUnchangedTests`, `PublicOutputUnchangedTests`,
  `OrganismStatePageTests` and every existing component test pass UNMODIFIED.
- Additions to existing components are OPTIONAL parameters whose default output is byte-identical,
  added as RenderFragment/attribute EXPRESSIONS, never new `@if` blocks beside existing markup
  (`razor-optional-params.md`). Each addition is covered in the NEW file
  `tests/Gallery.Web.Tests/LibraryComponentAdditionsTests.cs`, which asserts by attribute regex
  (scoped components add `b-xxxx`, void elements self-close) on HTML-decoded output.
- Per-item controls live inside the item's tile, in a row under the title; only the library select
  mark is drawn over the photo; status text sits under the cell (FR-004).
- One filled (Primary) button per bar or panel in EVERY state; danger buttons outlined (existing
  `ButtonVariant.Danger`). A state that adds its own primary demotes the container's.
- Bound form state: every control showing persisted state renders it, and tests cover the FALSE
  case for ABSENCE of the attribute (`checked`, `disabled`), not `checked="False"` (FR-031).
- Every studio row links to its visitor view when one exists (FR-005); nothing links to a page that
  would 404.

## Component additions

| # | Component (level) | New optional parameters | Default | Rendered when set |
|---|---|---|---|---|
| K1 | `Switch` (atom) | `FormId` (string?), `Value` (string?) | null → no `form`/`value` attribute | `form="…"`, `value="…"` on the input |
| K2 | `Input` (atom) | `Required` (bool), `Pattern` (string?) | false/null → attributes absent | `required`, `pattern=".*\S.*"` on the input/textarea |
| K3 | `VisibilityToggle` (molecule) | `FormId` passed to the Switch (K1, `Value="on"`); `DisabledReason` (string?) | null → unchanged | switch rendered `disabled`, and the hint line replaced by `<p class="hint">Add a photo before making this gallery public</p>` (the reason text) |
| K4 | `CoverMarker` (molecule) | `ImageId` (string?), `UnpinFormId` (string?) | null → unchanged | Make cover: `name="image" value="{ImageId}" type="submit"` on `FormId`; Unpin: `type="submit"` on `UnpinFormId` (no image field = unpin) |
| K5 | `SelectionBar` (molecule) | `Render` (bool, renders even at Count 0 with the `hidden` attribute), `SelectAllLabel` (string?, adds a Quiet `type="button"` "Select all on this page" after Clear), `Limit` (int?, renders `data-limit`) | false/null → unchanged | as described; the script toggles `hidden` and the count |
| K6 | `GalleryBuilder` (organism) | `ItemIds` (IReadOnlyList<string>?), `RemoveFormId`, `CoverFormId`, `UnpinFormId`, `OrderFormId` (string?), `PhotoHrefBase` (string?), `PublicDisabledReason` (string?), `TitleMax`/`DescriptionMax` (int?), `Status` (string?) | null → unchanged (the `/ui/` builder page passes none) | under each title the existing `b-ctl` row gains, in order: DragHandle, CoverMarker (K4), `Remove` (Quiet submit, `name="image" value="{id}"`, `form=RemoveFormId`), and `View ↗` (TextLink to `{PhotoHrefBase}/{id}`) when PhotoHrefBase is set; the grid is wrapped in `<reorder-grid data-form="{OrderFormId}">` when set; inputs get K2 `Required`/`Pattern` and the max lengths; VisibilityToggle gets K3 |

`Tile`, `ContactSheet`, `SelectMark`, `DragHandle`, `DangerConfirm`, `SidePanel`, `OwnerBar`,
`AdminLayout`, `Pager`, `TextLink`, `Button` are used as they are.

## C1 Library page — `Admin/Pages/LibraryPage.razor` (GET `/admin/library`)

- `AdminLayout Title="Library" Current=Library`, `ViewHref="/"`, no owner-bar primary action
  (the selection bar's "Add to gallery…" is the page's one primary, and only while a selection
  exists).
- Hidden forms at the top of `<main>`: `lib-select` (POST `/admin/library/add`) and `lib-delete`
  (POST `/admin/library/delete`), each with the antiforgery field and `page`.
- `ContactSheet 4/3/1` with one `Tile` per image, `Href` null (the tile is a `div`, since a checkbox
  inside a link is invalid), `CssClass="lib-tile"`:
  - `Overlay`: `<div class="pos-tl"><SelectMark Label="Select {title}" Name="ids" Value="{id}"
    FormId="lib-select" /></div>` (the only mark over the photo).
  - `Caption`: `<div class="lib-row"><span class="lib-status">{status}</span><TextLink
    Href="?page=n&open={id}">Edit</TextLink>{View}</div>`, where View is `TextLink "View ↗"` to the
    photo page (R-04) or nothing.
- `SelectionBar Render=true Count=0 Total={tiles} SelectAllLabel="Select all on this page"
  Limit={PageSize}` with children: `Add to gallery…` (Primary, `type="button"`, opens the picker),
  `Delete…` (Danger, `type="button"`, opens the bulk confirm). The Delete form uses `lib-delete`
  and the same ticked ids (the script copies them).
- Gallery picker: a `<dialog id="lib-picker">` whose `<form>` targets `lib-select`: one radio
  `gallery={gid}` per gallery in home order, label "{title} · Public|Private · {n} photos"; public
  options carry a second line "Visitors will see these photos once they're added." (FR-011, II.5,
  visible without script); a last radio `gallery=new` with an `Input Name="newTitle"
  Required Pattern MaxLength={TitleMax}`; buttons "Add" (Primary) and "Cancel" (Quiet). Inside the
  open dialog the selection bar's Add is visually behind it, so one primary is visible.
- Bulk delete confirm: a `DangerConfirm Action="Delete" Target="{n} photos" Consequence="This
  removes them from every gallery and deletes the original files. It can't be undone."
  ConfirmLabel="Delete photos" FormId="lib-delete"`, kept `hidden` until Delete… is pressed.
- `Pager` with `?page=n-1` / `?page=n+1`, and "Page n of m".
- Status text (`role="status"`) from the PRG query: "Saved", "N added · M were already in
  {gallery}", "N deleted" plus " · K files could not be removed and were logged" when K > 0, and
  the refusal reason on 4xx.
- Side panel (`?open={id}`): `SidePanel Heading="Photo" FormId="lib-edit"`, ChildContent:
  `Input Label="Title" Name="title" Value={stored} MaxLength={TitleMax} Required Pattern`, `Input
  Label="Caption" Name="caption" Multiline Value={stored} MaxLength={CaptionMax}`, a list "In
  galleries" of links to each builder (or "Not in a gallery"), and `View ↗` when it exists.
  Danger: `DangerConfirm Action="Delete photo" Target="{title}" Consequence="It is in {n}
  galleries. This removes it from every gallery and deletes the original file. It can't be undone."
  ConfirmLabel="Delete photo" FormId="lib-del-one"`.
- Empty library: `<p class="lib-empty">No photos yet. <a href="/admin/upload">Upload photos</a></p>`.
- No warning, required-field error or missing-metadata prompt anywhere (FR-007).
- Script: `<script type="module" src="/admin-assets/library.js?v={hash}">`.

## C2 Gallery list — `Admin/Pages/GalleriesPage.razor` (GET `/admin/galleries`)

- `AdminLayout Title="Galleries" Current=Galleries`, `PrimaryAction` = "New gallery" (Primary
  button opening `<dialog id="gal-new">` with `Input Name="title" Required Pattern MaxLength` and
  Create (Primary) / Cancel; the dialog's form posts G2). While a publish warning is shown the
  owner-bar action is Secondary (one primary per state).
- `<reorder-grid data-form="gal-order">` around `ContactSheet 3/3/1`; hidden form `gal-order` (G3)
  holds one `order` input per gallery in home order.
- One `Tile` per gallery in home order: `Image` = card cover or null with `EmptyTitle` = title,
  `Href` null, `CssClass` `is-private` for private galleries (dimmed). `Caption`:
  `<div class="gal-row"><span class="gal-status">{status}</span><DragHandle Label="Move {title}"
  Position Total /><VisibilityToggle Public FormId="gal-vis-{gid}" GalleryTitle PhotoCount
  DisabledReason={empty ? reason : null} Warning={warning for this gid} />{View}<TextLink
  Href="/admin/galleries/{gid}">Edit</TextLink></div>`. Status: "Public", "Private",
  "Public · hidden until it has a photo", "Private · no photos". View = `TextLink "View ↗"` to
  `/g/{gid}/{slug}` only when the gallery has photos.
- One hidden form per card `gal-vis-{gid}` (G4, `from=list`). The switch submits it on change
  (script); "Make public" in the warning submits it with `confirm=1`.
- Status / refusal text as C1 (`role="status"`).

## C3 Gallery builder — `Admin/Pages/GalleryPage.razor` (GET `/admin/galleries/{gid}`)

- `AdminLayout Title="{title}" Current=Galleries`, `ViewHref` = `/g/{gid}/{slug}` when the gallery
  has photos, else `/admin/galleries`, label "View gallery" / "Back to galleries".
- Hidden forms: `b-panel` (B2), `b-order` (B3), `b-cover` (B4 pin), `b-unpin` (B4 unpin),
  `b-remove` (B5), `b-vis` (G4, `from=builder`), `b-delete` (B6).
- `GalleryBuilder` with K6: `Items` in gallery order, `ItemIds`, `PinnedIndex`, `Public`,
  `PublicDisabledReason` when empty, `FormId="b-panel"` (title/description/save), the visibility
  toggle on `b-vis`, `State` = `PublishWarning` when the warning is shown (Save demoted, existing),
  `Empty` when no photos (existing copy and library link), `PhotoHrefBase="/g/{gid}/{slug}"` when
  it has photos, `DangerConfirm` on `b-delete` (existing copy "Its N photos stay in your library.
  This can't be undone.").
- The builder's Remove has no confirmation (FR-024).
- Script: `reorder.js` (drag + keyboard) and `library.js` (switch auto-submit).

## C4 Scripts (`wwwroot/admin-assets/`, versioned `?v=` like 008)

- `reorder.js` defines `<reorder-grid data-form="…">`. Pointer drag on a tile's `.handle` shows a
  ghost slot (`.drop-slot`, the existing class) at the landing index. Keyboard: Space/Enter on a
  focused handle lifts (sets the DragHandle's `aria-pressed="true"` and shows the lift help text),
  arrows move one place, Space/Enter drops, Escape restores the original order; each move updates a
  polite live region "Position p of n". On drop it rewrites the `order` inputs of its form in the
  new order and submits. It resolves the form at use time, not in `connectedCallback`.
- `library.js`: selection count and bar visibility; Clear; Select all on this page (ticks every
  `ids` box on the page; the page never holds more than `data-limit`, so the limit holds at
  selection time); opens/closes the picker and the bulk delete confirm and copies ticked ids into
  `lib-delete`; submits a visibility form when its switch changes.
- Neither script runs on public pages (test: no `/admin-assets/` script on any public route).

## Copy (fixed strings, tested after `WebUtility.HtmlDecode`)

| Where | Text |
|---|---|
| empty publish refusal / disabled reason | Add a photo before making this gallery public |
| stale gallery order | This gallery changed in another tab. Reload to see the current order. |
| stale home order | The gallery list changed in another tab. Reload to see the current order. |
| publish warning (existing 005) | Make “{title}” public? It will be listed on the home page with its {n} photos, and anyone can see them. |
| picker public note | Visitors will see these photos once they're added. |
| add result | {n} added · {m} were already in {gallery} (the " · …" part only when m > 0) |
| delete confirm (one) | It is in {n} galleries. This removes it from every gallery and deletes the original file. It can't be undone. ("1 gallery"; "It is in no gallery." when 0) |
| delete confirm (bulk) | This removes them from every gallery and deletes the original files. It can't be undone. |
| empty library | No photos yet. Upload photos |
| empty builder (existing 005) | No photos in this gallery yet. Add photos from the library |
