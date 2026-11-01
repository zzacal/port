# Contract: Components and Pages (010)

The exact markup, classes, copy and CSS each executor implements. Gaps are escalated, never
invented. Rules that apply to every item:

- Optional parameters only. With every new parameter unset, a component's output is
  byte-identical to today (the 005 `PublicOutputUnchangedTests` and 006 `UiPagesUnchangedTests`
  pin this). Use the implicit-expression pattern (`@Fragment` that is null by default), never a
  new `@if` beside existing markup: Razor trims the adjacent whitespace
  (speckit reference `razor-optional-params.md`).
- No inline `style` attributes (`Guards.AssertNoInlineStyle`). CSS lives in the component's
  `.razor.css`.
- Copy is exact. Arrows are the characters `←` (U+2190) and `→` (U+2192). The separator is
  ` · ` (U+00B7 with a space each side).

## K1 `TextLink` atom (changed): `Tap` parameter

```razor
<a class="@Class" href="@Href" rel="@RelValue" target="@Target">@ChildContent</a>
[Parameter] public bool Tap { get; set; }
private string Class => Tap ? "text-link tap" : "text-link";
```

CSS added to `TextLink.razor.css` (the one tap rule for the whole site, FR-004):

```css
.text-link.tap {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 44px;
    min-height: 44px;
    padding-inline: 8px;
    box-sizing: border-box;
}
```

## K2 `Pager` molecule (changed): tap areas and centre slot

New optional parameters:

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Here` | `RenderFragment?` | null | Centre content (the photo page passes `GalleryLink`). |
| `Stack` | `PagerStack` enum | `None` | `None`, `Narrow` (stack below 600 px), `Always`. |

Markup. Both links ALWAYS pass `Tap="true"` (so the library pager grows too; decisions row 12).
With `Here` null and `Stack` None the only byte change from today is the link class
`text-link tap`, a stated requirement change (plan, Test changes 1):

```html
<nav class="pager{stackClass}" aria-label="Image navigation">
    <span class="prev">…<a class="text-link tap" href="…" rel="prev">Previous</a>…</span>{Here}
    <span class="next">…<a class="text-link tap" href="…" rel="next">Next</a>…</span>
</nav>
```

`{Here}`, when set, renders `<div class="here">…</div>` between the spans. `{stackClass}` is ""
for None, ` pager-stack` for Narrow, ` pager-stack pager-stack-all` for Always.

CSS (`Pager.razor.css`, replacing the current two rules):

```css
.pager {
    display: grid;
    grid-template-columns: minmax(44px, 1fr) auto minmax(44px, 1fr);
    grid-template-areas: "prev here next";
    align-items: center;
    gap: var(--space-s);
}
.prev { grid-area: prev; justify-self: start; }
.next { grid-area: next; justify-self: end; text-align: right; }
.here { grid-area: here; min-width: 0; text-align: center; }
.prev ::deep .text-link { margin-inline-start: -8px; }
.next ::deep .text-link { margin-inline-end: -8px; }

@media (max-width: 599.98px) {
    .pager-stack { grid-template-columns: 1fr 1fr; grid-template-areas: "here here" "prev next"; row-gap: 0; }
}
.pager-stack-all { grid-template-columns: 1fr 1fr; grid-template-areas: "here here" "prev next"; row-gap: 0; }
```

With no `Here`, the empty middle `auto` track is 0 wide, so the library pager keeps its
Previous-left/Next-right look. The breakpoint must equal `Tokens` (600 px), as `ContactSheet`
does.

## K3 `GalleryLink` molecule (new, `Components/Molecules/GalleryLink.razor`)

Parameters: `Href` (string, required), `Title` (string, required), `Index` (int, required, 1-based),
`Total` (int, required).

```html
<p class="gallery-link"><a class="text-link" href="{Href}">← {Title}</a><span class="count"> · {Index} / {Total}</span></p>
```

Static members used by the photo page to pick the Pager's `Stack`:

```csharp
public const int NarrowChars = 26;
public const int WideChars = 60;
public static int Length(string title, int index, int total) => $"← {title} · {index} / {total}".Length;
public static PagerStack StackFor(string title, int index, int total) => Length(...) switch
    { > WideChars => PagerStack.Always, > NarrowChars => PagerStack.Narrow, _ => PagerStack.None };
```

CSS:

```css
.gallery-link {
    margin: 0;
    font-size: var(--type-caption);
    color: var(--color-muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}
.gallery-link .text-link { color: var(--color-text); }
```

(`.text-link` here is a descendant inside the same component; the anchor is written directly in
this component's markup, not through the TextLink atom, so the scoped selector applies. It is
not a tap link: it sits between two tap areas, and its own height is the row's.)

## K4 `SoloViewer` organism (changed)

New optional parameter `Here` (`RenderFragment?`) and `Stack` (`PagerStack`), passed through to
`Pager`. With both unset the output is byte-identical apart from the Pager link class (K2).

## K5 `GalleryNeighbours` molecule (new, `Components/Molecules/GalleryNeighbours.razor`)

Parameters: `Prev` (`GalleryNeighbour?`), `Next` (`GalleryNeighbour?`), where
`GalleryNeighbour` is `record GalleryNeighbour(string Href, string Title)` in
`Components/Molecules/GalleryNeighbour.cs`. When both are null it renders NOTHING (no element).

```html
<nav class="gallery-neighbours" aria-label="Galleries">
    <span class="prev"><a class="text-link tap" href="{Prev.Href}" rel="prev">← {Prev.Title}</a></span>
    <span class="next"><a class="text-link tap" href="{Next.Href}" rel="next">{Next.Title} →</a></span>
</nav>
```

A missing side renders its `<span>` empty (so the other keeps its edge). Links use the
`TextLink` atom with `Tap="true"`.

CSS:

```css
.gallery-neighbours {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    gap: var(--space-s);
    max-width: 960px;
    margin: var(--space-m) auto 0;
    padding: 0 var(--space-s);
}
.prev { justify-self: start; min-width: 0; }
.next { justify-self: end; min-width: 0; text-align: right; }
.gallery-neighbours ::deep .text-link { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; display: inline-block; line-height: 44px; }
```

(The last rule keeps the 44 px height while allowing ellipsis, which `inline-flex` would
prevent. The browser test measures both boxes.)

## K6 `SiteHeader` organism (changed)

`Nav` renders only when `NavItems.Count > 0`, written as an implicit expression so the non-empty
output is byte-identical:

```razor
<header class="site-header">
    <Brand Name="@SiteName" />
    @NavFragment
</header>
private RenderFragment? NavFragment => NavItems.Count == 0 ? null : @<Nav Items="NavItems" />;
```

If this changes the non-empty bytes (whitespace), keep the original line and use the static
`MarkupString` separator recipe; the `SiteHeader` catalogue snapshot must stay identical.
`NavItems` loses `EditorRequired` and defaults to `[]`.

## K7 `PublicLayout` template (changed)

- `<SiteHeader SiteName="Gallery" />` (no nav items; FR-010).
- The `Enhance` module gains these 5 lines verbatim as its first statements, BEFORE
  `if (navigator.connection?.saveData) return;`:

```js
document.addEventListener('keydown', e => {
const k = { ArrowLeft: 'a[rel=prev]', ArrowRight: 'a[rel=next]', Escape: '.gallery-link a' }[e.key];
if (!k || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.defaultPrevented || e.target.closest?.('input,textarea,select,[contenteditable]')) return;
document.querySelector('article.solo ' + k)?.click();
});
```

  The module stays one `<script type="module">`, under 30 lines (`PageComponentTests`).

## Pages

### P1 Photo page (`Site/Pages/PhotoPage.razor`)

New parameters: `GalleryHref` (string, required), `Index` (int), `Total` (int). Renders
`SoloViewer` with `Here` = `<GalleryLink Href Title=GalleryTitle Index Total />` and
`Stack = GalleryLink.StackFor(GalleryTitle, Index, Total)`.

Endpoint: `GalleryHref = $"/g/{id}/{slug}#p-{imageId}"`; `Index = items.IndexOf(imageId) + 1`;
`Total = items.Count`. No new read.

### P2 Gallery page (`Site/Pages/GalleryPage.razor`)

New parameters `Prev`, `Next` (`GalleryNeighbour?`). After the `ContactSheet`:
`<GalleryNeighbours Prev Next />` (renders nothing when both null).

Endpoint: when `gallery.Visibility == "public"` and `gallery.Nav` is not null, map
`Nav.Prev/Next` to `GalleryNeighbour($"/g/{id}/{slug}", title)`; otherwise both null. [SAFETY]

### P3 Library tile row (`Admin/Pages/LibraryPage.razor`, `RowFor`)

`<span class="lib-status">@tile.Status</span>` becomes
`<MembershipStatus FirstId="@tile.FirstByTitle?.Id" FirstTitle="@tile.FirstByTitle?.Title" Others="@(tile.GalleryCount - 1)" Public="@tile.PublicGalleryCount" />` (K8).
The status remains inside a `span.lib-status` so the 009 tests that read it by class keep
working for the zero-gallery case.

## K8 `MembershipStatus` molecule (new, `Components/Molecules/MembershipStatus.razor`)

Parameters: `FirstId` (string?), `FirstTitle` (string?), `Others` (int), `Public` (int). Renders:

| Case | Markup (inside `<span class="lib-status">…</span>`) |
|---|---|
| `FirstId` null | `Not in a gallery` |
| `Others` 0, `Public` 0 | `<a class="text-link" href="/admin/galleries/{FirstId}">{FirstTitle}</a>` |
| `Others` N>0 | `<a …>{Title}</a> +{N}` |
| `Public` M>0 | the above, then ` · public in {M}` |

`MembershipStatus` lives in `Components/Molecules/` and takes primitives, not `LibraryTile`
(components do not depend on `Library/`). It uses the `TextLink` atom (no `Tap`: it is a dense
grid row, admin-only, desktop-first). Copy `+{N}` has no space between `+` and the number, one
space before `+`.

`LibraryReader` adds `FirstByTitle` to `LibraryTile`: the holder with the least title under
`StringComparer.OrdinalIgnoreCase`, ties by id ordinal. `Others = GalleryCount - 1`.

## Catalogue (FR-012): states shown before any page uses them

New pages, added to `UiCatalog.Entries` at the end of their level (the index count goes 31 → 34,
a stated requirement change in `IndexTests`):

| Page | Slug | States (Specimen labels, exact) |
|---|---|---|
| `GalleryLinkPage` | `molecules/gallery-link` | "Short title", "Long title (truncates)" |
| `GalleryNeighboursPage` | `molecules/gallery-neighbours` | "First gallery", "Middle gallery", "Last gallery", "Long titles" |
| `MembershipStatusPage` | `molecules/membership-status` | "In no gallery", "In one gallery", "In three galleries", "In two, public in one" |

Existing pages get APPENDED specimens only (the 005 test allows appended specimens; the 006 test
does not, so these pages' 006 snapshots are regenerated as a stated requirement change after the
verify-before-replace diff):

| Page | Appended specimens |
|---|---|
| `PagerPage` | "With gallery link, middle photo", "With gallery link, first photo", "With gallery link, last photo", "One-photo gallery", "Long gallery title (stacks on small screens)" |
| `SoloViewerPage` | "With gallery link and position" |
| `SiteHeaderPage` | "Public site (brand only)" |
| `TextLinkPage` | "Tap area" |

## Fixed copy (tested after `WebUtility.HtmlDecode`)

- `← {title}` and ` · {N} / {total}` (photo page)
- `← {prev title}`, `{next title} →` (gallery page)
- `Not in a gallery`, `+{N}`, ` · public in {M}` (library)
- `aria-label="Galleries"` on the gallery-neighbours nav
