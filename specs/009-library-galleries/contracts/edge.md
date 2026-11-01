# Purge-Set Contract: 009 (FR-038, SC-004)

`PurgeSets` computes, from BEFORE and AFTER snapshots of each affected gallery
(`id, slug, visibility, items`), the exact addresses one operation purges. Tests assert equality
with the expected set: none missing, none extra. Addresses are absolute: pages on
`Edge:SiteBaseUrl`, files on `Storage:ImageBaseUrl`. Paths:

- home: `/`
- gallery page: `/g/{gid}/{slug}`
- photo page: `/g/{gid}/{slug}/{iid}`

Definitions:
- `HasPage(g)` = `items` non-empty (public or private; decisions row 7).
- `Listed(g)` = public AND `items` non-empty.
- `Home(b,a)` = `/` when `Listed(before)` or `Listed(after)`.
- `Neighbours(items, i)` = (previous, next) ids, as `GalleryNav.Neighbours`.
- `Changed(b,a)` = ids in exactly one of `b.items`, `a.items`, plus ids in both whose
  `Neighbours` differ.

For a gallery `g` with snapshots `b` and `a`, `Pages(g, ids)` = for each snapshot that `HasPage`,
the gallery page plus the photo page of each id that snapshot holds (under that snapshot's slug).

| Set | Operation | Pages | Files |
|---|---|---|---|
| P-MEMBERS | add (L3), remove (B5), reorder (B3), cover pin/unpin (B4) | gallery pages of `b` and `a` (each that `HasPage`) + `Pages(g, Changed(b,a))` + `Home(b,a)` | none |
| P-VIS | publish / unpublish (G4) | `Pages(g, all items)` + `/` | none |
| P-RENAME | slug changed (B2) | `Pages(g, all items)` under BOTH slugs + `Home(b,a)` | none |
| P-DESC | description only (B2) | gallery page if `HasPage` | none |
| P-ORDER | gallery order (G3) | `/` (only when the relative order of listed galleries changed; otherwise none) | none |
| P-GDELETE | gallery delete (B6) | `Pages(g, all items)` of `b` + `Home(b, ∅)` | none |
| P-TEXT | image title/caption (L2) | for every gallery holding the image that `HasPage`: its gallery page and the image's photo page in it + `/` when the image is the cover of a listed gallery | none |
| P-DELETE | image delete (L4, L5) | union of P-MEMBERS for every gallery that held it + `/` when any was listed | each public object key actually deleted (`ImageBaseUrl/{key}`) |

Note on P-TEXT: the photo page shows only the current image's title and caption, and prev/next
links carry no titles, so neighbours are not purged. The gallery page shows every title. The home
card's cover renders with the GALLERY title (`ViewMapper.ToCard`), so the home page does not
actually change on an image title edit; it is still purged when the image is the cover of a listed
gallery because FR-038 lists it. That is one extra URL, kept to match the spec literally (decisions
row 24 records it as a cheap narrowing for later).

Batching and rate (FR-041): addresses are de-duplicated, then sent in calls of at most
`Edge:MaxUrlsPerCall` (100). Calls are spaced so the rolling rate stays at or below
`Edge:MaxUrlsPerSecond` (800). A bulk operation enqueues ONE `EdgeWork` with the union of its
items' sets.

Warm (FR-039), after the purge of the same `EdgeWork`:
- pages: each purged page, GET as an anonymous client (no cookie), only when `Edge:PurgePages` is
  true (hosting); otherwise logged `skipped`;
- files: every AVIF width of the images in the first row of each purged page that still exists
  (home: first 3 listed cards' covers; gallery: first 4 items; photo page: the photo itself),
  GET from `ImageBaseUrl`. Deleted files are never warmed.

Modes (FR-042):

| Mode | When | Purge | Warm |
|---|---|---|---|
| log-only | `Edge:ZoneId` or `Edge:ApiToken` missing (default) | log addresses, outcome `skipped` | log addresses, outcome `skipped` |
| dev-zone | both set | Cloudflare `POST /zones/{ZoneId}/purge_cache {"files":[...]}` for file URLs; page URLs logged `skipped` unless `Edge:PurgePages` | file GETs; pages only with `PurgePages` |
| recorder | tests (DI override) | records each call | records each call; can block on a gate |

Credentials (FR-043, [SAFETY]): `Edge:ApiToken` is read from configuration (user-secrets locally),
sent only in the `Authorization: Bearer` header, never logged, never rendered, never placed in a
URL. `EdgeOptions.ToString()` is not overridden to include it; a test scans captured logs and every
owner page for the configured token value (SC-011).
