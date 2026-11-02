# Contract: Owner review 1 (010 amendment, 2026-11-01)

Owner feedback after the build, verbatim intent:

1. Replace the photo page's "← {Gallery}" back link with a **breadcrumb at the top, centred, right
   below the nav bar**. Keep the "x / y" image count.
2. **Admin pages get breadcrumb navigation too.**
3. The admin nav bar must be **the same on every admin page**; "New gallery" next to Sign out is
   wrong. Put page actions where they belong, consistently.
4. **Previous and Next are always whole words** (no "Prev…").

This contract supersedes K3 (GalleryLink), the `Stack`/`PagerStack` half of K2, P1's centre slot
content, K7's Escape selector and decisions rows 1, 13 and 21. Everything not named here stands.
Defaults taken where the feedback left a choice are decisions rows 41–46.

## Rules for the breadcrumb (research: NN/g "Breadcrumbs: 11 Design Guidelines", fetched)

- A breadcrumb shows the page's place in the hierarchy (not session history), starts at the
  section root, every ancestor is a link, and the **current page is the last item, not a link**,
  marked `aria-current="page"`.
- It renders **only on pages that have a parent**. Roots get none: public home `/`, and the admin
  section pages `/admin/library`, `/admin/galleries`, `/admin/upload`, `/admin` (the owner bar is
  their navigation). One rule for both sites.
- Separator `›` (U+203A), drawn by CSS (`li + li::before`), so it is not read as content.
- One line, never wraps. Ancestors keep their full text where they fit; the current item truncates
  first with an ellipsis, then ancestors. Every link has the 44×44 tap box (the K1 tap rule).

## B1 `Crumb` record and `Breadcrumb` molecule (new)

`Components/Molecules/Crumb.cs`: `public sealed record Crumb(string Label, string? Href = null);`
(`Href` null = the current page.)

`Components/Molecules/Breadcrumb.razor`, parameter `Items` (`IReadOnlyList<Crumb>`, required).
Renders nothing for an empty list.

```html
<nav class="breadcrumb" aria-label="Breadcrumb"><ol><li><a class="text-link tap" href="/">Galleries</a></li><li aria-current="page"><span>September 6</span></li></ol></nav>
```

Ancestors use the `TextLink` atom with `Tap="true"`. CSS (`Breadcrumb.razor.css`):

```css
.breadcrumb { display: flex; justify-content: center; min-width: 0; padding: 0 var(--space-s); font-size: var(--type-caption); color: var(--color-muted); }
ol { display: flex; align-items: center; min-width: 0; max-width: 100%; margin: 0; padding: 0; list-style: none; white-space: nowrap; }
li { display: flex; align-items: center; min-width: 0; flex: 0 1 auto; }
li + li::before { content: "›"; padding-inline: 4px; flex: none; }
li[aria-current] { flex: 0 100 auto; overflow: hidden; }
li[aria-current] span { overflow: hidden; text-overflow: ellipsis; line-height: 44px; padding-inline: 8px; color: var(--color-text); }
```

(`flex-shrink: 100` on the current item makes it give way before the ancestors.)

## B2 Public pages

- `PublicLayout` gains `Crumbs` (`IReadOnlyList<Crumb>?`, default null), rendered as
  `<Breadcrumb Items>` **directly after** `<SiteHeader … />` on the same source line as a
  null-by-default fragment, so a page without crumbs is byte-identical.
- Gallery page: `[Crumb("Galleries", "/"), Crumb(gallery.Title)]`. Public and private alike
  (decisions row 43).
- Photo page: `[Crumb("Galleries", "/"), Crumb(GalleryTitle, "/g/{id}/{slug}#p-{imageId}"),
  Crumb(Image.Title)]`. The gallery crumb keeps landing on the photo's tile (R-03).
- Home and 404: none.

## B3 Photo count (replaces K3 `GalleryLink`)

`GalleryLink.razor(.css)` and `PagerStack.cs` are deleted; the Pager loses `Stack` and SoloViewer
loses `Stack`. New molecule `Components/Molecules/PhotoCount.razor`, parameters `Index`, `Total`:

```html
<p class="photo-count">7 / 23</p>
```

CSS: `.photo-count { margin: 0; font-size: var(--type-caption); color: var(--color-muted); white-space: nowrap; }`

PhotoPage passes `Here = <PhotoCount Index Total />` to SoloViewer when `Total > 0` (the endpoint
always sets it); with `Total` 0 (component tests that omit it) no `Here`, as before.

## B4 Pager: whole words (fixes "Prev…")

Root cause: the side tracks were `minmax(44px, 1fr)`, so a wide centre squeezed them to 44 px and
the tap rule's ellipsis cut "Previous". Replace the Pager CSS with:

```css
.pager {
    display: grid;
    grid-template-columns: minmax(max-content, 1fr) auto minmax(max-content, 1fr);
    grid-template-areas: "prev here next";
    align-items: center;
    gap: var(--space-s);
}
.prev { grid-area: prev; justify-self: start; }
.next { grid-area: next; justify-self: end; text-align: right; }
.here { grid-area: here; min-width: 0; text-align: center; }
.prev ::deep .text-link { margin-inline-start: -8px; }
.next ::deep .text-link { margin-inline-end: -8px; }
```

(The stack media query and `.pager-stack-all` go.) Guard: at 320–1440 px the rendered text of
every Pager link equals its label and `scrollWidth == clientWidth` (no ellipsis), on the photo
page and the admin library pager.

## B5 Keys (K7 amended, still 5 lines)

```js
document.addEventListener('keydown', e => {
const k = { ArrowLeft: 'article.solo a[rel=prev]', ArrowRight: 'article.solo a[rel=next]', Escape: '.breadcrumb a[href*="#p-"]' }[e.key];
if (!k || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.defaultPrevented || e.target.closest?.('input,textarea,select,[contenteditable]')) return;
document.querySelector(k)?.click();
});
```

Escape clicks the photo page's gallery crumb (the only crumb with `#p-`); on other pages it does
nothing, as before.

## B6 Admin: one owner bar, a page heading row, breadcrumbs

- `OwnerBar` loses `PrimaryAction`. `AdminLayout` loses `PrimaryAction` and gains `Crumbs`
  (`IReadOnlyList<Crumb>?`), rendered directly after the owner bar exactly as in B2. No page passes
  `ViewHref` to `AdminLayout` any more: the bar's link is always `View site ↗` to `/`. The bar is
  then identical on every admin page except `aria-current`.
- New molecule `Components/Molecules/PageHeading.razor`, parameters `Text` (required), `Actions`
  (`RenderFragment?`):

  ```html
  <div class="page-heading"><h1>Galleries</h1><div class="page-actions">…</div></div>
  ```

  `.page-actions` renders only when `Actions` is set. CSS: flex row, `justify-content:
  space-between`, `align-items: center`, `gap: var(--space-s)`, `margin: 0 0 var(--space-s)`,
  wraps; `h1 { margin: 0; font-size: var(--type-brand); }`.
- **Where page actions go** (decisions row 44): a page-level create action sits in the page heading
  row, right-aligned beside the page title (Atlassian page header; GitHub "New repository").
  Global bars hold navigation and account only.
  - Galleries: `<PageHeading Text="Galleries">` with the `New gallery` primary button as `Actions`
    (same button, same `name="action" value="new-gallery"`); `library.js` opens the dialog for it
    by `button.closest('.page-heading')` instead of `.owner-bar`.
  - Library: `<PageHeading Text="Library" />` (no action; decisions row 45).
  - The pages' own `gal-heading`/`lib-heading` h1s and their CSS rules go.
- Gallery builder: `Crumbs = [Crumb("Galleries", "/admin/galleries"), Crumb(title)]`. The side
  panel's "Back to galleries" link (empty gallery) is removed; the panel shows `View gallery ↗` only
  when the gallery has photos.
- Upload and the shell page: unchanged apart from the bar (no crumbs: section roots).

## Catalogue

`UiCatalog`: `molecules/gallery-link` → `molecules/photo-count` ("PhotoCount"), plus
`molecules/breadcrumb` ("Breadcrumb") and `molecules/page-heading` ("PageHeading") at the end of
Molecules (36 entries).

| Page | Specimens (exact labels) |
|---|---|
| `BreadcrumbPage` | "Gallery page", "Photo page", "Admin gallery", "Long titles (narrow)" |
| `PageHeadingPage` | "Title only", "With an action" |
| `PhotoCountPage` | "Middle photo", "One-photo gallery" |

`PagerPage`'s 010 specimens become: "With count, middle photo", "With count, first photo",
"With count, last photo", "One-photo gallery". `OwnerBarPage` drops its `PrimaryAction`
specimen content (labels "Upload current", "Library current", "Galleries current", "Phone width").
`TemplatePageTests`, `OrganismStatePageTests` and the snapshot procedure of T025 apply.
