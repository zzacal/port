# Research: Navigation Improvements (010)

Every Technical Context unknown is resolved here. Each entry gives the decision, the rationale
and the alternatives considered. All facts about the current code come from reading the files
named, on branch `010-navigation-improvements` at bf94671.

## Baseline: what the code does today

- `Site/PublicEndpoints.cs`: home reads `site.homeCards` (1 read, `db.site.read`). The gallery page
  reads the gallery (`db.gallery.read`, projection excludes `card`) and then its images
  (`db.images.read`): 2 reads. The photo page reads the gallery (projection: slug, title,
  visibility, items) and then 1–3 images: 2 reads. `GalleryTitle` already reaches `PhotoPage`
  and is used only in `<title>`.
- `Components/Organisms/SoloViewer.razor` renders `Pager` (Previous/Next `TextLink`s, 12 px
  caption type, no padding) under the title and `MetadataBar`. The photo is not a link.
- `Components/Templates/PublicLayout.razor` passes one nav item, `Galleries` → `/`, and `Brand`
  ("Gallery") also links to `/`. That is the duplicate (FR-010).
- `Components/Molecules/Pager.razor` is also used by the admin library page
  (`Admin/Pages/LibraryPage.razor:48`) for its page-by-page navigation.
- The gallery page's tiles already carry `id="p-{imageId}"` on the `Picture` root (the
  view-transition hook, `TransitionId`), so a fragment target exists today.
- `Library/SiteProjector.cs` recomputes `site.homeCards` from `site.homeOrder` and every gallery
  on EVERY owner write (`OperationScope.CompleteAsync` and `RefreshAsync`). It already reads all
  galleries named in `homeOrder` and applies the listed rule (public and `card.imageCount ≥ 1`).
- `Edge/PurgeSets.cs` computes purge sets from before/after snapshots of the galleries an
  operation touched. No set today includes a gallery page that the operation did not touch.
- `Library/LibraryReader.cs` builds the library status string (`StatusLine`); the tile's
  galleries are ordered by home order, and the side panel already links every gallery.

## R-01 Where the gallery page gets its previous/next gallery (FR-007, FR-015)

**Decision**: denormalise each gallery's neighbours onto the gallery document as a new `nav`
field, `{ prev: {id, slug, title} | null, next: {…} | null }`, written by `SiteProjector` in the
same pass that writes `homeCards`. The gallery page's existing single gallery read returns it
(its projection already includes every field except `card`), so the page stays at 2 reads.

The projector computes the listed sequence (`SiteProjector.Cards`, unchanged), then for every
gallery in `homeOrder` sets `nav` to its neighbours in that sequence via the existing
`GalleryNav.Neighbours`, or unsets it when the gallery is not listed. It writes only galleries
whose stored `nav` differs, so a write that changes no order costs zero extra updates. Galleries
missing from `homeOrder` do not exist in practice (create appends, delete removes); the projector
leaves them alone.

**Rationale**: the spec's own assumption ("derived from the home order the home page already
uses") holds only if the order travels with the gallery read. Reading `site.homeCards` on the
gallery page would make it 3 reads and break FR-015/SC-006. The projector already loads every
gallery it needs, already runs on every owner write, and is the single place the listed rule
lives, so the neighbours can never disagree with the home page. This is the fallback the spec
names ("storing neighbours with the gallery").

**Alternatives considered**:
- Read `site` on the gallery page as a second query in parallel: 3 reads, violates FR-015.
- Combine gallery + site in one `$lookup` aggregation: one round trip but two collections; it
  hides a second read inside one timing event and is harder to reason about. Rejected.
- Compute at write time only in the services that change order (reorder, publish …): duplicates
  the listed rule in five places. Rejected; the projector already runs after all of them.

## R-02 Which gallery pages a change must refresh (FR-009, SC-005)

**Decision**: `OperationScope.CompleteAsync` already projects the site. The projector
(`ProjectWithNavAsync`, contracts/edge.md E2) returns, besides the cards, the id and current slug
of every gallery whose `nav` it changed. `CompleteAsync` adds the gallery page of each such
gallery to the purge set as a new set kind, P-NAV, before the union, eviction and the one
enqueue. (A gallery that just became empty answers 404; purging its address is harmless and keeps
the rule free of exceptions.) `RefreshAsync` (image delete step 2) records its projection's
`NavChanged` in the scope, so the final `CompleteAsync` includes them.

This covers every case in the spec without per-operation rules: home reorder, publish,
unpublish, rename (the neighbour's link text and href change), delete, and a listed gallery
becoming empty or non-empty. A change that leaves every neighbour unchanged (reorder of private
galleries, description edit) yields an empty P-NAV.

**Rationale**: "the pages whose links change" is exactly "the galleries whose stored nav
changed", which the projector knows precisely. Deriving it from before/after snapshots in
`PurgeSets` would need the full listed sequence before and after, which only the projector has.

**Alternatives considered**: purge every listed gallery page on any order-affecting change
(simple, over-purges, slower warm, and still needs the "which changes are order-affecting" rule);
rejected.

## R-03 Gallery link target and landing on the tile (FR-002, SC-001)

**Decision**: the gallery link's href is `/g/{id}/{slug}#p-{imageId}`, the id the tile's picture
already carries. No new markup on the gallery page. Escape navigates to the same URL.

- Without scripts, the browser scrolls to the fragment natively (spec Story 1, scenario 3).
- The layout is final at first paint (every image has width and height, IV.8), so the scroll
  target does not move while images load.
- Rename: the old slug 301s to the new one. Browsers carry the fragment across a redirect whose
  `Location` has none (RFC 9110 §10.2.2; Chromium, WebKit and Gecko all do this), so the anchor
  still applies. Covered by a browser test.
- A removed or deleted photo: the fragment names no element, so the page opens at the top with
  no error (edge case). Covered by a browser test.

**Risk R-01-a (prerender)**: the gallery URL with a fragment is matched by the Speculation Rules
(`/g/*`, moderate). If Chrome activates a prerendered gallery page without scrolling to the
fragment, SC-001 fails with scripts on. The browser test hovers the link first (forcing the
prerender) before clicking, so the failure would be caught. Fallback, decided now so no executor
has to decide it: exclude the photo page's gallery link from the rules with
`{"and":[{"href_matches":"/g/*"},{"not":{"selector_matches":".gallery-link a"}}]}`.

**Alternatives considered**: `?at={imageId}` plus a server-rendered scroll offset (needs a
script or a varying cache key per photo, so 23 cache entries per gallery page); rejected.

## R-04 Photo position "N / total" (FR-003)

**Decision**: computed in the photo endpoint from `gallery.Items`, which it already loads:
`N = index + 1`, `total = Items.Count`. No new data. The counter text is `{N} / {total}` with
plain spaces, as the spec shows.

**Consequence for purging (found reading `PurgeSets.Changed`)**: today a membership change purges
only the photo pages whose neighbours changed. Once every photo page prints "N / total", that
is not enough: adding one photo changes the total on EVERY photo page of the gallery, and a
reorder changes N on every photo whose index moved, even when its neighbours did not. Left
alone, edge-cached photo pages would show a wrong counter for up to a day.

**Decision**: `PurgeSets.Changed` also returns every id whose 1-based index differs, and every
id on both sides when the item count differs. P-MEMBERS and P-DELETE inherit this through
`Changed`. Warm cost: an add to a 23-photo gallery warms 23 photo pages' AVIF widths instead of
2 (about 140 URLs), inside the existing batching. The 009 `PurgeSetTests` facts that pin the old
neighbour-only result for add and remove change as a stated requirement change (plan, Test
changes).

## R-05 Previous/Next tap areas (FR-004, SC-003)

**Decision**: the `TextLink` atom gains an optional `Tap` parameter. When true the link gets a
second class, `text-link tap`, and ONE rule in `TextLink.razor.css` gives it
`display:inline-flex; align-items:center; justify-content:center; min-height:44px;
min-width:44px; padding-inline:8px`. Visible text, colour and size stay exactly as today; only
the hit box grows. With `Tap` unset the atom's output is byte-identical. `Pager` and
`GalleryNeighbours` (R-08) pass `Tap="true"`, so both use the same rule. In the Pager the
`.prev` link gets `margin-inline-start:-8px` and the `.next` link `margin-inline-end:-8px`, so the
visible words stay where they are today horizontally. Underline on hover and focus is unchanged.

The admin library uses the same `Pager`, so its page links get the same larger tap area. That
is one Pager look, the owner's standing "general rules over special cases" preference.
Recorded as decisions row 12.

The row grows from about 17 px to 44 px high. The photo's stage is `80vh` and sits above the
row, so the photo does not get smaller (edge case); only the page below it lengthens, at first
paint, so CLS stays 0.

**Alternatives considered**: a new `PhotoPager` organism (two pagers drifting apart; rejected);
padding on every `TextLink` (would grow every text link on the site, including the library's
Edit and View; rejected); a `::deep` tap rule copied into Pager and GalleryNeighbours (two copies
of one rule; rejected).

## R-06 The centred gallery link and the narrow-screen fallback (FR-001, FR-003, edge cases)

**Decision**: `Pager` gains an optional `Here` render fragment. When set, the root becomes a
three-column grid, `[prev] [here] [next]`, with the outer columns `1fr` and the centre `auto`,
so the centre stays centred even when one side is empty (edge case "first or last photo"). When
`Here` is unset the markup is byte-identical to today (library, catalogue, admin).

The centre content is a new molecule, `GalleryLink`: `<a href="…#p-{id}">← {title}</a> · N / total`
in the caption style. Long titles are decided on the server by the length of the centre text
(`← {title} · {N} / {total}`):

- longer than `GalleryLink.NarrowChars` = 26: `Pager` adds `pager-stack`, which below 600 px
  puts the centre on its own line above Previous and Next;
- longer than `GalleryLink.WideChars` = 60: `Pager` adds `pager-stack-all`, which does the same
  at every width;
- a centre line that still does not fit (a 200-character title) truncates with an ellipsis,
  `Title`-style, and never narrows the outer columns below 44 px.

The budgets come from the row width. At 360 px, 328 px of row minus "Previous" (≈58 px with its
tap padding), "Next" (44 px) and two 16 px gaps leaves ≈194 px, about 31 caption characters of
the system font; 26 leaves margin for wide glyphs. At 600 px the row is ≈536 px, leaving ≈390 px,
about 64 characters; 60 leaves margin. The browser test measures the result at 360, 390, 600,
1024 and 1440 px with titles of 10, 30 and 70 characters (no overlap, every tap box ≥ 44×44,
centre on one line, centred), so the constants are checked, not trusted.

**Alternatives considered**: a container query on the pager (it cannot know whether the text
fits without measuring, so it would still need a script or the same budget); always stacking
on small screens (an extra line on every phone for no reason). Stacking happens below the photo,
so even the stacked case does not shrink the photo.

## R-07 Keyboard shortcuts (FR-005, FR-006)

**Decision**: a few lines added to the existing inline module in `PublicLayout`'s `Enhance`
block, placed BEFORE its Save-Data early return (Save-Data must not disable keys, edge case).
On `keydown`, when no modifier (`altKey`, `ctrlKey`, `metaKey`, `shiftKey`) is held, the event is
not `defaultPrevented`, and the target is not an `input`, `textarea`, `select` or
`[contenteditable]`: Left/Right find `article.solo a[rel=prev]` / `a[rel=next]`, Escape finds
`article.solo .gallery-link a`, and the handler calls `.click()` on it.

Two existing tests bound this and stay unmodified: `PageComponentTests` asserts exactly 2
`<script` elements per public page and a module under 30 lines. The module is 23 lines today; the
handler is 5 lines (contracts/components.md gives them verbatim), making 28. Clicking the real link
means the navigation is identical to a mouse click: same URL, same cross-document view
transition (IV.16), same speculation/prerender reuse, and the neighbour prefetch (IV.13) is
untouched because it runs on `load` and does not depend on how the page was left. A missing
direction has no link, so nothing happens (Story 2, scenario 4). Nothing on the page mentions
the keys (clarification: silent).

**Rationale**: no new script element, no framework (IV.22), progressive enhancement (V.3). The
`rel` hooks already exist on the Pager's links.

**Alternatives considered**: `location.href = …` (skips the link's own navigation path and is a
second source of truth for the URL); a separate script file (an extra request on every photo
page). Rejected.

## R-08 Gallery-to-gallery links (FR-007, FR-008)

**Decision**: a new molecule, `GalleryNeighbours`, after the grid on the gallery page:
`<nav class="gallery-neighbours" aria-label="Galleries">` with `← {prev title}` on the left and
`{next title} →` on the right, each a `TextLink` with `rel="prev"`/`rel="next"` and the same
44×44 tap rule as the Pager. It renders nothing at all when both are null. The endpoint passes
`gallery.Nav` only when the gallery is public (II.2 render-time guard); the projector already
leaves `nav` unset on private and empty galleries, so the private case has two independent
guards. [SAFETY], because a private gallery appearing as a neighbour, or a private page linking
to listed ones, makes an unlisted gallery discoverable from a listed page.

## R-09 Header duplicate (FR-010)

**Decision**: `PublicLayout` passes no nav items, and `SiteHeader` renders no `<nav>` at all when
`NavItems` is empty. The brand ("Gallery" → `/`) is the only header link. With a non-empty list,
SiteHeader's output is byte-identical (its catalogue page keeps its "Galleries" specimen). A
guard test over the three public routes asserts that no two header links share a destination.

## R-10 Linked library status (FR-011, SC-009)

**Decision**: a new molecule, `MembershipStatus`, replaces the plain status text in the library
tile row. Zero galleries: plain text "Not in a gallery" (unchanged). One or more: a `TextLink` to
`/admin/galleries/{id}` for the first gallery by title (`StringComparer.OrdinalIgnoreCase`, ties
by id), then " +N" when N others exist, then the existing " · public in M" suffix when M ≥ 1
(009 copy kept, decisions row 16). `LibraryReader` computes the first-by-title gallery from the
holders it already has; no new read. The side panel's full list is unchanged.

## R-11 Existing dev data (the `nav` field is new)

**Decision**: no new command. Galleries in the owner's local Mongo have no `nav` until the next
owner write that changes something, because every such write ends in `SiteProjector.ProjectAsync`,
which recomputes `nav` for EVERY gallery in `homeOrder`, not just the one written. One write
anywhere fixes all of them. Quickstart Part C names one: add a word to any gallery's description,
save, remove it, save. (A save that changes nothing returns before projecting, so the edit has to
be real.) Until then a gallery page simply shows no neighbour links, which is the safe direction.

**Alternatives considered**: a `refresh-site` console command beside `remove-seeded`; rejected as
an unrequested addition for a one-time dev-data step the existing write path already covers.

## R-12 Timing evidence (FR-015, FR-016, SC-006)

**Decision**: a test-hosted evidence run, like 009's `LibraryTimingEvidenceTests`: a
`LibraryHost` (output cache off, so every request reaches Mongo) with 3 listed galleries and 23
photos sends 100 GETs each to `/`, a gallery page and a photo page, then writes its log messages
to `evidence/nav-timings.log`. `evidence/p95.sh` reads `http.request` lines by route template
(`/`, `/g/{id}/{slug}`, `/g/{id}/{slug}/{imageId}`): every line's `db_reads` must be exactly 1 for
home and ≤ 2 for the others, and p95 `duration_ms` < 50. n=0 fails a row; an empty or
wrong-format log fails. The script is self-tested on synthetic logs before the real run.

The same test runs FIRST, in the setup phase, against the unchanged code, and its output is
committed as `evidence/baseline-timings.log` and `evidence/p95-baseline.txt`, so "no more reads
than today" is compared against a captured baseline, not memory.

## R-13 Browser verification (SC-001, SC-003, SC-004, SC-007)

**Decision**: a new Playwright fixture, `NavBrowserFixture`, modelled on `BrowserSiteFixture`:
3 public galleries (23, 6 and 1 photos) and 1 private gallery (2 photos), with the 23 image
documents reusing the 6 encoded derivative sets cyclically (encoding 23 distinct photos would
add minutes to every run; the tests measure navigation, not image bytes). The fixture writes the
documents with the gallery `card`s left empty and then calls `SiteProjector.ProjectAsync` and the
card builder through the app's own services, so `homeCards` and `nav` come from the production
path, not hand-written fixtures (a fixture that writes `nav` directly would prove the page, not
the projection). Tests: every-photo gallery-link landing at 1440 and 390 (SC-001), tap areas at
390/1024/1440 and 360 (SC-003), the no-JS walk (SC-004), CLS 0 on all three pages at three
widths (SC-007), keys incl. modifiers and a focused input (FR-005), long-title stacking and
centring at the first and last photo, the rename redirect keeping the fragment, and a removed
photo's anchor opening at the top.

`BrowserTests` and `BrowserSiteFixture` stay unmodified.

## R-14 Design record (VII.2)

The plan changes what D-21 says about `Pager`, `SiteHeader` and `PublicLayout` ("no scripts
beyond D-17"), what the content model says a Gallery stores, and what the lifecycle entries say
each change purges. These are edits to `specs/001-architecture-stack-design/draft/` (D-21, D-17,
section 4 Gallery, section 5 L-entries for publish, unpublish, rename, delete, home reorder,
add, remove; the II.2 traceability row), reassembled with `evidence/assemble.sh` and checked with
`evidence/validate.sh`, as a named task in this feature. No constitution amendment: no MUST is
crossed (see plan Constitution Check).

## Dependencies

None added. No NuGet package, no script file, no font. Playwright 1.64.1 and Testcontainers are
already test dependencies.
