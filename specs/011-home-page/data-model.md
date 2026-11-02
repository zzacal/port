# Data Model: Home Page (011)

No stored data changes. There are no new collections or fields, no index changes and no
migration. The entities below are in-code values.

## Home link (in code)

- Type: existing `NavItem(string Text, string Href, bool Current = false)`.
- Instances: `HomePage` holds a static list with exactly one entry, `new("Galleries", "/g")`.
- Rules: the list is ordered and fixed in code, not owner-editable. `Current` is never set on the
  home page, since no home link points at the home page itself.

## Breadcrumb trail (in code)

- Type: existing `Crumb(string Label, string? Href = null)`, as a list.
- Source: `Site/Trails.cs` only (contracts/components.md C3).

| Page | Trail |
|---|---|
| `/` | Home |
| `/g` | Home → `/`, Galleries |
| `/g/{id}/{slug}` | Home → `/`, Galleries → `/g`, {gallery title} |
| `/g/{id}/{slug}/{imageId}` | Home → `/`, Galleries → `/g`, {gallery title} → `/g/{id}/{slug}#p-{imageId}`, {photo title} |
| 404 | Home → `/`, Not found |
| (default, no trail passed) | Home → `/` |

- Invariant (FR-014): a trail's hrefs are a subset of `{"/", "/g", own gallery page}`.

## Galleries listing (stored, unchanged)

- Read: `site.homeCards` through `ContentReader.HomeAsync()`, which is one read (IV.9).
- Render filter: cards with a non-null cover (II.6), as before.
- The names `homeCards`, `homeOrder` and `HomeAsync` stay (research R-01). They mean "the owner's
  gallery order", and the constitution sweep kept the term "home order".
