# Data Model: Navigation Improvements (010)

One stored addition (`galleries.nav`) and four derived view values. Nothing else in the content
model changes. Collections and existing fields: `specs/009-library-galleries/data-model.md` and
`docs/design.md` section 4.

## Stored: `Gallery.nav` (new, optional)

| Field | Type | Meaning |
|---|---|---|
| `nav` | `GalleryNavDoc?` | The gallery's neighbours in the gallery sequence. Absent when the gallery is not listed. |
| `nav.prev` | `GalleryLinkDoc?` | The listed gallery before it on the home page; absent for the first. |
| `nav.next` | `GalleryLinkDoc?` | The listed gallery after it; absent for the last. |

`GalleryLinkDoc`: `{ id: string, slug: string, title: string }`, copied from the neighbour at
projection time.

C# (in `Data/Documents.cs`, beside `GalleryCardDoc`):

- `GalleryLinkDoc { Id "id", Slug "slug", Title "title" }`
- `GalleryNavDoc { Prev "prev" [BsonIgnoreIfNull], Next "next" [BsonIgnoreIfNull] }`
- `GalleryDoc.Nav "nav" [BsonIgnoreIfNull]`

Rules:

1. **Single writer.** Only `SiteProjector` writes `nav` (a source-scan guard enforces it, like
   the 007 owner-field guard). Every owner write already ends in a projection, so `nav` is never
   older than the last owner write.
2. **Listed only.** A gallery that is private, or public with zero items, has no `nav` (the
   projector `$unset`s it). It is never anyone's `prev`/`next`. [SAFETY] II.2. A listed gallery
   with no neighbour at all (the only listed gallery) also has no `nav`: `nav` is stored only
   when at least one of `prev`/`next` exists, so "no links" has one representation.
3. **Order.** The sequence is `SiteProjector.Cards(homeOrder, galleries)` (unchanged 009 rule:
   `homeOrder` order, first occurrence, public, `card.imageCount ≥ 1`). Neighbours come from
   `GalleryNav.Neighbours` over the sequence's gallery ids.
4. **Minimal writes.** The projector compares each gallery's computed `nav` with the stored one
   (BSON equality, as `CardCompare` does for cards) and writes only differences:
   `$set: {nav}` or `$unset: nav`. `updatedAt` is not touched by a nav-only write (it records
   the owner's edits, not derived state).
5. **Missing from `homeOrder`.** Not expected (create appends, delete removes). Such a gallery
   is not in the sequence and the projector does not visit it. Its `nav` cannot exist, because
   only the projector writes `nav` and only for galleries it visits, and a gallery deleted from
   `homeOrder` is deleted as a document in the same operation.
6. **Existing data.** Galleries written before 010 have no `nav` until the first owner write
   after deploy (research R-11). No neighbour links show until then.
7. **Read path.** The public gallery read (`ContentReader.GalleryAsync(forPhoto: false)`)
   already returns every field except `card`, so `nav` comes with the existing read. The photo
   page's projection does not include `nav` (unused there).

### State transitions that change `nav`

Every row ends with the same projection; the "galleries whose nav changes" set (`NavChanged`)
is what P-NAV purges (contracts/edge.md).

| Owner action | Sequence change | `nav` writes |
|---|---|---|
| Home reorder (G3) | listed order changes | every listed gallery whose neighbour changed |
| Publish (non-empty) | gallery enters | it and its two new neighbours |
| Unpublish | gallery leaves | its old neighbours set; it is unset |
| Rename | neighbour's title/slug changes | its two neighbours (their links' text and href) |
| Gallery delete | gallery leaves | its old neighbours |
| Last photo removed or deleted from a listed gallery | gallery leaves | as unpublish |
| First photo added to an empty public gallery | gallery enters | as publish |
| Anything else (description, cover, member reorder, image text) | none | none |

## Derived (no storage)

### Gallery sequence

The listed galleries in home order. Computed in `SiteProjector` only. Not exposed elsewhere.

### Photo position (`PhotoPosition`)

`(int Index, int Total)` with `Index` 1-based: the position of the photo id in
`gallery.items` and `items.Count`. Computed in the photo endpoint from the gallery it already
loaded. A photo id not in `items` is already a 404 today, so the position always exists.

### Gallery link (`GalleryLinkView`)

What the photo page's centre slot shows: `Href = /g/{id}/{slug}#p-{imageId}`, `Title`,
`Index`, `Total`. Built in the photo endpoint.

### Gallery neighbours (`GalleryNeighboursView`)

`(GalleryLinkDoc? Prev, GalleryLinkDoc? Next)`, mapped to hrefs `/g/{id}/{slug}`. The endpoint
passes it ONLY when the gallery is public; otherwise both are null (render-time guard,
independent of rule 2).

### Library membership (`MembershipStatus` input)

From the holders `LibraryReader` already computes for a tile (`IReadOnlyList<GalleryRef>`):

- `First`: the holder with the smallest title by `StringComparer.OrdinalIgnoreCase`, ties
  broken by id (ordinal). Any visibility.
- `Others`: `holders.Count - 1`.
- `Public`: `holders.Count(g => g.IsPublic)` (unchanged 009 meaning).

`LibraryTile` gains `GalleryRef? FirstByTitle`. The existing `Status` string and
`StatusLine` stay (they still feed the plain zero-gallery case and existing tests).

## Validation rules carried from the spec

- FR-008: `nav` exists only on listed galleries; the gallery page renders neighbours only when
  public.
- FR-011: first by title, case-insensitive, regardless of visibility.
- FR-015: no new datastore read on any public route; `nav` rides on the existing gallery read.
