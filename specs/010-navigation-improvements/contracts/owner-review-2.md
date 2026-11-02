# Contract: Owner review 2 (010 amendment, 2026-11-01)

Owner feedback: the public site's "Gallery" brand link is redundant. **The breadcrumb is the public
site's main navigation and replaces the existing header nav.** Keep its size and spacing. Separator
`/` instead of `›`. Align it to the left (an interim "align right" was withdrawn by the owner).

Supersedes in owner-review-1.md: B1's separator, B2 (crumbs only on pages with a parent, rendered
under the header) and decisions row 43 for the public site. The admin side is unchanged apart from
the shared molecule's look (separator and alignment).

## R1 `SiteHeader` organism = the breadcrumb

```html
<header class="site-header"><nav class="breadcrumb" aria-label="Breadcrumb"><ol>…</ol></nav></header>
```

Parameter `Crumbs` (`IReadOnlyList<Crumb>`, required). No brand, no `Nav`; `SiteName`/`NavItems`
are removed. CSS: vertical padding only (`var(--space-s) 0`, `var(--space-m) 0` from 1200 px); the
breadcrumb carries the page-edge side padding (16/32/48 px).

## R2 Every public page has a trail (the header is never empty)

| Page | Trail |
|---|---|
| Home | `Galleries` (current, not a link) |
| Gallery | `Galleries / {title}` |
| Photo | `Galleries / {gallery} / {photo}` (gallery item → the photo's tile) |
| 404 | `Galleries / Not found` ("Back to galleries" removed) |
| Anything rendering `PublicLayout` without `Crumbs` | `Galleries` linking `/` |

## R3 Breadcrumb look

- Separator `/` (CSS `li + li::before`), 4 px each side, unchanged size and spacing otherwise.
- Left-aligned; the first item's TEXT starts on the page margin (the `ol` is pulled left by the
  8 px tap padding), so it lines up with the grid's left edge.

## Catalogue

`SiteHeaderPage` specimens: "Home", "Gallery page", "Photo page" (the brand/nav specimens go).
`NavPage` and `BrandPage` stay (the molecules still exist; `Nav` is the admin-style list,
`Brand` is unused on public pages; deleting them is a separate cleanup, decisions row 52).
