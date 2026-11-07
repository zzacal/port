# Component Contracts: 016

Each contract is a rule the tests hold. Names in `code` are as in the repo.

## G1 Icons: eye, eye-off

- `Icons.Paths` gains `eye` and `eye-off`, with the path data copied byte-for-byte from Lucide
  1.70.0 (`icons/eye.svg`, `icons/eye-off.svg`).
- Each new entry is pinned by IconTests' SHA-256 check, as for the other 8.
- `IconRegistryGuardTests` stays unmodified: both names are used by `GalleryActions`.

## G2 GalleryActions molecule

The parameters and the state table are in [data-model.md](../data-model.md).

- Markup: one `div.gallery-actions` holding, in order:
  1. `DragHandle Icon="true"` (as 015's builder), named "Move {Title}", with Position/Total;
  2. the visibility control (G2a);
  3. view: an `IconButton` link with `Target="_blank"`, or the unavailable span;
  4. edit: an `IconButton` link.
  Edit is the last child on every card.
- G2a, visibility:
  - Available: `IconButton Type="submit" FormId="{VisibilityForm}"`. `Name="public" Value="on"`
    is set only when the gallery is private (to publish); the public case sends no `public`
    field (to unpublish). No `Toggle` and no `aria-pressed`.
  - Unavailable (private, empty): `IconButton Unavailable="true"` with its label.
  - Names come from `GalleryActions.VisibilityLabel(IsPublic, HasPhotos)`. The empty-private
    reason reuses `GalleryService.EmptyPublishReason`.
- CSS (`GalleryActions.razor.css`):
  - the same box rules as `.photo-actions`: flex, `flex-wrap: nowrap`, 4 px gap, height
    `var(--tap)`, centred;
  - no literal `44px`, no `style=` attribute, no new token.
- Rendering only: the molecule builds no admin URL, reads no service, and holds no script.

## G3 Galleries list wiring (`GalleriesPage.razor`)

- A card with a cover is `Tile TitleOnPhoto="true"` with `Caption=GalleryActions`. A card without
  one is the existing `.gal-tile.tile-empty` with the title in a page-scoped band
  (`.gal-tile .cell .gal-band`, the values of 015's `.on-photo`), then `GalleryActions`.
- `.gal-row`, `.gal-status`, `ViewFor` and the `VisibilityToggle` use on this page are removed.
  `GalleryRow.Status` is removed.
- `DisabledReasonFor` stays: the builder page still uses it.
- `is-private` dimming, the `gal-vis-{id}` forms (with token and `from=list`), `gal-order` and
  `reorder-grid` are unchanged.

## G4 Publish behaviour (unchanged server)

- Click the eye-off on a private gallery with photos: it POSTs `public=on`, and the gallery
  becomes public.
- Click the eye on a public gallery: it POSTs with no `public` field, and the gallery becomes
  private.
- Both return to the list (`from=list`) with the existing result line.
- A forged `public=on` for an empty gallery is still refused (the PublishRefusalTests guard is
  unchanged).

## G5 Grid columns (R-07)

- `ContactSheet` parameters:
  - library and builder: `Large=4 Medium=2 Small=1`;
  - list: `Large=3 Medium=2 Small=1`.
- Each page's `Sizes.ForGrid(...)` call takes the same three numbers.
- Library and builder: the `ContactSheet` sits in a wrapper `div` with
  `container-type: inline-size` (class `lib-grid` and `b-grid`).
- Inside `@media (min-width: 1200px)`, `@container (max-width: N)` sets
  `.cs { grid-template-columns: repeat(2, minmax(0, 1fr)) }`:
  - library: N = 799.98px;
  - builder: N = 991.98px.
- The ContactSheet organism and its public uses are unchanged.

## G6 Catalogue

- New `UiCatalog` entry `molecules/gallery-actions`, "Gallery actions", inserted directly after
  `VisibilityToggle`.
- Page `GalleryActionsPage.razor` shows 5 specimens, each a `w-320` card (Tile TitleOnPhoto plus
  the row): Public, Private, Public with no photos, Private with no photos, a long title.

## J1 library.js

- The `change` handler's `gal-vis-` submit branch is removed. The builder's switch path is kept:
  it submits nothing.
- The header comment's gal-vis line is removed.
