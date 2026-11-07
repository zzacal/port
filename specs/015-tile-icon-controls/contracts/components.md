# UI contract: icon system, photo actions, title on the photo

Routes, forms and server operations are unchanged (FR-024): every control below posts the same
form or links to the same address as the word it replaces. All new markup is server-rendered.
The only script added is J1.

## K1 Icon (atom): `Components/Atoms/Icon.razor`, `Icons.cs`, `Icon.razor.css`

`Icons.cs` (namespace `Gallery.Web.Components.Atoms`):

```
// Lucide 1.70.0 (https://lucide.dev), ISC License, Copyright (c) 2026 Lucide Icons and Contributors.
// Paths copied verbatim from icons/{name}.svg. Add an icon only when a page uses it (FR-018).
public static class Icons
{
    public const string Source = "Lucide 1.70.0";
    public const string License = "ISC License, Copyright (c) 2026 Lucide Icons and Contributors";
    public static readonly IReadOnlyDictionary<string, string> Paths = …;   // 8 keys, R-01 table
}
```

The keys are `folder-plus`, `folder`, `folders`, `square-arrow-out-up-right`, `pencil`,
`grip-vertical`, `star` and `image-minus`. Each value is the inner markup of the Lucide file
(`<path …/>`, `<circle …/>`, `<rect …/>`), copied verbatim from tag `1.70.0` with the
whitespace collapsed to single spaces.

Parameters: `Name` (string, required; must be a key), `Size` (int, default 24), `Filled` (bool,
default false).

Markup (exact):

```
<svg class="icon" data-icon="{Name}" width="{Size}" height="{Size}" viewBox="0 0 24 24"
     fill="{Filled ? "currentColor" : "none"}" stroke="currentColor" stroke-width="2"
     stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">{Paths[Name] as markup}</svg>
```

- An unknown `Name` throws `ArgumentException` in `OnParametersSet`.
- CSS: `.icon { display: block; flex: none; }`.
- No `<title>` inside: the accessible name lives on the control (K2).

## K2 IconButton (atom): `Components/Atoms/IconButton.razor`, `.razor.css`

Parameters:

| Name | Type | Default | Meaning |
|---|---|---|---|
| `Icon` | string, required | | K1 name |
| `Label` | string, required | | accessible name and hover text (`aria-label` + `title`) |
| `Href` | string? | null | renders `<a>` |
| `Target` | string? | null | link only; `"_blank"` adds `rel="noopener"` |
| `Active` | bool | false | link: `aria-current="true"`; button: `aria-pressed="true"` |
| `Unavailable` | bool | false | renders a non-interactive `<span>` |
| `Filled` | bool | false | passed to K1 (the pinned star) |
| `Type` | string | `"submit"` | button only |
| `FormId`, `Name`, `Value` | string? | null | button only: `form`, `name`, `value` |
| `CssClass` | string? | null | extra class on the root |

Markup (exact; `{c}` = `"icon-button"` + `" is-active"` when Active + `" " + CssClass` when set):

```
Link:        <a class="{c}" href="{Href}" aria-label="{Label}" title="{Label}"[ aria-current="true"][ target="{Target}" rel="noopener"]><Icon/></a>
Button:      <button class="{c}" type="{Type}" aria-label="{Label}" title="{Label}"[ aria-pressed="true|false" when Active is set by a toggle][ form][ name][ value]><Icon/></button>
Unavailable: <span class="{c} is-unavailable" role="img" aria-label="{Label}" title="{Label}"><Icon/></span>
```

`aria-pressed` is rendered only on buttons whose caller marks them as a toggle (the cover button,
K6). It renders as `"true"`/`"false"`, never as a bare attribute or `"True"`. Plain buttons such as
remove carry no `aria-pressed`.

CSS (existing tokens only):

- `.icon-button`:
  - `display:inline-flex; align-items:center; justify-content:center;`
  - `width:var(--tap); height:var(--tap); padding:0; box-sizing:border-box;`
  - `border:0; border-radius:4px; background:transparent; color:var(--color-text); cursor:pointer;`
  - `text-decoration:none;`
- `:hover` → `background: var(--color-surface)`;
- `:focus-visible` → `outline: 2px solid var(--color-text); outline-offset: -2px`;
- `.is-active` → `box-shadow: inset 0 0 0 2px var(--color-text)` (a ring, not a fill: 009 UI-3,
  R-05);
- `.is-unavailable` → `color: var(--color-muted); cursor: default` with no hover background.

The 44 px values use `var(--tap)`; `NavComponentTests` bans a literal 44px in `.razor.css`.

## K3 Tile title on the photo: `Tile.razor` (+ `Tile.razor.css`)

New optional parameters:

- `TitleOnPhoto` (bool, default false): with an Image, the `Title` renders inside Picture's frame
  as `<div class="on-photo"><Title Text="…"/></div>`, a sibling AFTER the admin `.overlay`. The
  title line under the cell is then not rendered.
- `Id` (string?, default null): `id` on the tile root.

Byte identity: with both parameters at their defaults, the output is byte-identical
(`PublicOutputUnchangedTests` and every `ui-baseline` page). Follow the expression-only recipe
(no `@if` beside existing markup): the cell's `ChildContent` becomes `@PhotoLayer` (overlay plus
on-photo), and the line under the cell is `<Title Text="@Image.Title" />@Caption` when
`!TitleOnPhoto`, through a `RenderFragment?` expression.

CSS:

```
.on-photo { position:absolute; left:0; right:0; bottom:0; z-index:1; pointer-events:none;
            background: rgba(0,0,0,.6); padding: 4px 8px; }
.on-photo ::deep .title { color:#fff; text-align:left; }
```

- z-index 1 keeps it under `.overlay` (z-index 2), so the select mark stays on top.
- `rgba(0,0,0,.6)` and `#fff` are photo-overlay values, as in `SelectMark.razor.css`; they are
  not theme tokens (R-04, decisions).
- The Title atom keeps `title="{Text}"`, its one-line ellipsis and its caption size.
- With `TitleOnPhoto`, the caption row sits 8 px under the cell (the existing `.cell`
  margin-bottom).

## K4 PhotoActions (molecule): `Components/Molecules/PhotoActions.razor`, `.razor.css`, `PhotoActions.cs`

Data-agnostic: no gallery routes and no `Library` types (FR-020). The page passes every href.

Parameters: `Count` (int: how many containers hold the item), `GroupHref` (string, required),
`ViewHref` (string?), `EditHref` (string, required), `Active` (bool), `ItemLabel` (string,
required: the item title used in the labels).

`PhotoActions.cs` holds the pure rules (unit-tested):

```
public enum Membership { None, One, Many }
public static Membership MembershipOf(int count) => count <= 0 ? None : count == 1 ? One : Many;
public static string IconOf(Membership m) => m switch { None => "folder-plus", One => "folder", _ => "folders" };
public static string CountLabel(int count) => count <= 0 ? "Not in a gallery" : count == 1 ? "In 1 gallery" : $"In {count} galleries";
```

`CountLabel` is the FR-006 copy, owner-fixed. It is the only place the noun "gallery" appears in
the molecule; it is a label default, not a route, so FR-020's "no knowledge of galleries" is held
by `GroupHref` being supplied by the page.

Markup (exact order: gallery, view, edit; FR-005):

```
<div class="photo-actions">
  <IconButton Icon="{IconOf}" Label="{CountLabel(Count)}" Href="{GroupHref}" Active="{Active}" CssClass="pa-group" />
  ViewHref set:  <IconButton Icon="square-arrow-out-up-right" Label="View {ItemLabel} (opens in a new tab)" Href="{ViewHref}" Target="_blank" CssClass="pa-view" />
  ViewHref null: <IconButton Icon="square-arrow-out-up-right" Label="No public page" Unavailable="true" CssClass="pa-view" />
  <IconButton Icon="pencil" Label="{Active ? $"Close the editor for {ItemLabel}" : $"Edit {ItemLabel}"}" Href="{EditHref}" Active="{Active}" CssClass="pa-edit" />
</div>
```

CSS: `.photo-actions { display:flex; justify-content:center; gap:4px; height:var(--tap); }`. It is
one row that never wraps; 3 × 44 + 2 × 4 = 140 px fits the smallest phone tile.

## K5 Library page wiring: `LibraryPage.razor` (+ `.razor.css`)

- Each `<Tile …>` gains `TitleOnPhoto="true" Id="@($"t-{tile.Image.Id}")"`.
- `RowFor(tile)` becomes:
  - `<PhotoActions Count="@tile.GalleryCount" ItemLabel="@tile.Image.Title"`
    `ViewHref="@tile.ViewHref" Active="@open" GroupHref="@href" EditHref="@href" />`
  - where `open = Data.Open?.Image.Id == tile.Image.Id`;
  - and `href = open ? $"?page={Data.Page}#t-{id}" : $"?page={Data.Page}&open={id}#t-{id}"`.
- The gallery and edit icons share one href (FR-007: "exactly as the edit icon").
- Remove `.lib-row` and `.lib-status` from `LibraryPage.razor.css` and the `MembershipStatus`
  use. Keep `ViewFor` for the panel's own "View ↗" link (panel unchanged, FR-024).

## K6 Builder tiles: `GalleryBuilder.razor`, `DragHandle.razor`, `CoverMarker.razor`

- `GalleryBuilder` passes `TitleOnPhoto="true"` on its tiles. The `b-ctl` row keeps its class,
  its order (move, cover, remove, view; FR-022) and every form/name/value:
  - `<DragHandle … Icon="true" />`: the inner `<span aria-hidden="true">&#x2807;</span>`
    becomes `<Icon Name="grip-vertical" />`. Same `button`, same `class="handle…"`, same
    `aria-label` and `aria-pressed` (reorder.js and the browser tests key on them).
    `DragHandle.razor.css`: `.handle:has(> .icon) { width:var(--tap); height:var(--tap);`
    `background:transparent; }`. `Icon` defaults to false, so the gallery list's handle is
    unchanged.
  - `<CoverMarker … Icon="true" />`, keeping the `<div class="cover-marker">` root:
    - Unpinned: `<IconButton Icon="star" Label="Make {ImageTitle} the cover" Type="{as today}" FormId Name="image" Value="{ImageId}" />`
      with `aria-pressed="false"`;
    - Automatic: `<IconButton Icon="star" Filled="true" Unavailable="true" Label="Cover: first photo" />`;
    - Pinned: `<IconButton Icon="star" Filled="true" Active="true" Label="Unpin cover" Type="{as today}" FormId="{UnpinFormId ?? FormId}" />`
      with `aria-pressed="true"`.
    - `Icon` defaults to false: the word markup is unchanged.
  - Remove: `<IconButton Icon="image-minus" Label="Remove {title} from this gallery" FormId="{RemoveFormId}" Name="image" Value="{id}" />`.
  - View: `<IconButton Icon="square-arrow-out-up-right" Label="View {title} (opens in a new tab)" Href="{PhotoHrefBase}/{id}" Target="_blank" />`.
- `.b-ctl` CSS: `justify-content:center; gap:4px; min-height:var(--tap)`, with no wrap at any
  width. 4 × 44 + 3 × 4 = 188 px.

## K7 Admin layout: sticky aside (`AdminLayout.razor.css`)

At ≥ 1200 px only: `.admin-aside { position: sticky; top: 0; max-height: 100vh; overflow: auto; }`.
This puts the panel beside whichever tile opened it (R-03). The chrome geometry is unchanged
(`AdminLayoutConsistencyBrowserTests`).

## J1 library.js: bring the stacked panel into view

New section `// ── panel into view (015 R-03)`, at most 8 lines:

- On `DOMContentLoaded`, if `#lib-edit` exists and `matchMedia('(max-width: 1199.98px)').matches`
  and its top is below the viewport, call `scrollIntoView({ block: 'start' })` on the panel's
  `.combobox` (the Galleries field) when present, else on `#lib-edit`.
- No injection sink (`LibrarySourceGuardTests`).

## K8 Catalogue (`UiCatalog.cs`, `Ui/Pages/`)

- Atoms: insert after `SelectMark`:
  - `new(UiLevel.Atoms, "Icon", "atoms/icon", typeof(IconPage))`;
  - `new(UiLevel.Atoms, "IconButton", "atoms/icon-button", typeof(IconButtonPage))`.
- Molecules: `PhotoActions` replaces `MembershipStatus` at its position:
  `new(UiLevel.Molecules, "PhotoActions", "molecules/photo-actions", typeof(PhotoActionsPage))`.
  `MembershipStatus.razor`, `MembershipStatusPage.razor` and the
  `ui-baseline/molecules/membership-status.html` baseline are deleted.
- Specimens (labels verbatim; the order is fixed):
  - `IconPage`:
    - "Icons, 24 px": every key of `Icons.Paths` in key order, each with its name under it;
    - "Icons, 16 px": the same at Size 16;
    - "Filled": the star;
    - "Source": the text `{Icons.Source}, {Icons.License}` and a link to https://lucide.dev.
  - `IconButtonPage`:
    - "Link": pencil, Label "Edit Pier";
    - "Link, active": pencil, Active;
    - "Button": image-minus;
    - "Toggle button, pressed": star, Filled, Active, with `aria-pressed="true"`;
    - "Unavailable": square-arrow-out-up-right, "No public page";
    - "Every icon": one IconButton per icon in a row.
  - `PhotoActionsPage`, each a full library tile (`Tile` with `TitleOnPhoto` and a
    `PhotoActions` caption, `Width="w-320"`, sample images):
    - "Not in a gallery": Count 0, ViewHref null;
    - "In 1 gallery": Count 1;
    - "In 3 galleries": Count 3;
    - "No public page": Count 1, ViewHref null;
    - "Edit active": Count 2, Active;
    - "Long title": a 60-character title, Count 1.
  - `TilePage`: append "Title on photo, landscape", "Title on photo, portrait" and "Title on
    photo, long title", after the existing specimens.
  - `DragHandlePage`: append "Icon".
  - `CoverMarkerPage`: append "Icon, unpinned", "Icon, automatic" and "Icon, pinned".
  - `GalleryBuilderPage`: unchanged source; its markup changes through K6.
