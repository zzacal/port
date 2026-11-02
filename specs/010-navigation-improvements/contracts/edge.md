# Contract: Purge Sets (010 changes to 009 `contracts/edge.md`)

Only the deltas. Everything else in `specs/009-library-galleries/contracts/edge.md` stands
(definitions `HasPage`, `Listed`; one `EdgeWork` per operation; bases; warm scope).

## E1 `Changed(b, a)` widened (FR-003 counters; research R-04)

An id is changed when ANY of:

1. it is in exactly one of `b.items`, `a.items` (as today);
2. it is in both and its neighbours differ (as today);
3. it is in both and its 1-based index differs (new);
4. it is in both and `b.items.Count != a.items.Count` (new: every photo's "of total" changed).

Rule 4 makes every photo page of a gallery whose size changed purge. Examples the tests pin:

| before | after | Changed | why |
|---|---|---|---|
| a b c | a b c e | a b c e | rule 4 (count) |
| a b c | a c | a b c | rules 1, 2, 4 |
| a b c d | b a c d | a b c | rule 2 only (unchanged from 009) |
| a b c d e | b c d a e | a b c d e | c ONLY by rule 3: same neighbours (b, d), index 3 → 2 |

## E2 P-NAV (new): neighbour links on gallery pages (FR-009, SC-005)

`SiteProjector.ProjectWithNavAsync()` (new) returns `ProjectionResult(IReadOnlyList<GalleryCardDoc> Cards,
IReadOnlyList<GalleryLinkTarget> NavChanged)`, where `GalleryLinkTarget(string Id, string Slug)`
names every gallery whose stored `nav` it set or unset in this call, with its slug AFTER the
change. The existing `ProjectAsync()` keeps its signature and returns `(await
ProjectWithNavAsync()).Cards`; `OperationScope` calls the new method.

`PurgeSets.Nav(IEnumerable<GalleryLinkTarget> navChanged, EdgeBases bases)` =
`{ GalleryPath(id, slug) | (id, slug) ∈ navChanged }`. No photo pages, no `/`.

`OperationScope.CompleteAsync` adds `PurgeSets.Nav(result.NavChanged)` to the union before
eviction and the single enqueue. `RefreshAsync` (image delete step 2) records its result's
`NavChanged` in the scope, so `CompleteAsync` purges them too even though its own later
projection finds nothing left to change. Warm for these pages follows the 009 rule (first 4
items' AVIF) and needs their `items`: `CompleteAsync` reads the NavChanged galleries that are
not already in its after-snapshots (one extra owner-side read, `db.gallery.load`, only when
the set is non-empty).

A gallery whose `nav` was unset because it stopped being listed is purged even though its page
loses links rather than gains them; a private gallery still has a page (`HasPage`) and its
cached copy must stop showing neighbours (FR-008).

## Expected sets per owner action (tested end to end through `LibraryHost`'s recorder)

Galleries A, B, C, D listed in that home order, each with photos; P private with photos.

| Action | Pages added by P-NAV |
|---|---|
| Reorder home to B, A, C, D | /g/A, /g/B, /g/C (A: prev none→B; B: next C→A; C: prev B→A) |
| Unpublish B | /g/A, /g/B, /g/C |
| Publish P (placed between B and C in homeOrder) | /g/B, /g/P, /g/C |
| Rename B (slug changes) | /g/A, /g/C (their links to B change); B's own pages come from P-RENAME |
| Delete C | /g/B, /g/D |
| Remove the last photo of B | /g/A, /g/B, /g/C |
| Edit A's description | none |
| Reorder photos inside A | none |
| Move P within homeOrder | none |
