# Decisions: 010 Navigation Improvements

OWNER means the owner chose it (clarify, 2026-11-01). PLAN means the plan chose it; each is a
default the owner can overturn. Pivot cost is the cost to reverse after implementation.

| # | Decision | Rationale | Pivot cost | Source |
|---|---|---|---|---|
| 1 | Gallery link sits in the bottom row, `[Previous]  ← {Gallery} · N / total  [Next]`, caption style; nothing above the photo; a title too long for the row moves to its own line above Previous/Next. | Keeps the photo's full height; one row for all photo navigation. | Low: markup in Pager/GalleryLink only. | OWNER |
| 2 | Scope is the study's recommended set (1, 2, 3, 4, 6, 8, 11, 13). | Owner confirmed. | n/a | OWNER |
| 3 | Previous/Next stay text links with an invisible 44×44 tap area; the photo is not a link. | Owner's choice over buttons or photo tap zones. | Low: one CSS rule. | OWNER |
| 4 | Private galleries end with nothing: no neighbour links, no "All galleries"; never anyone's neighbour. | II.2 private = unlisted. | Low. | OWNER |
| 5 | "+N" links to the first gallery by title, case-insensitive, any visibility. | Owner's choice. | Low: one ordering in LibraryReader. | OWNER |
| 6 | Arrow keys and Escape work silently: no hint, no tooltip. | Owner's choice. | Low. | OWNER |
| 7 | Gallery neighbours are denormalised onto each gallery (`nav`) by `SiteProjector`, not read from `site` at view time. | Reading `site` would make the gallery page 3 reads (FR-015). The projector already loads every gallery on every owner write and owns the listed rule. (research R-01) | Medium: a stored field plus its purge rule; removing it means a 3rd read or a different denormalisation. | PLAN |
| 8 | Gallery pages whose `nav` changed are purged (new P-NAV), computed by the projector, not by per-operation rules. | Exact set, one place, covers reorder/publish/unpublish/rename/delete/empty. (research R-02) | Low. | PLAN |
| 9 | "N / total" widens `PurgeSets.Changed`: index change or count change marks a photo page stale. An add to a 23-photo gallery now purges all 23 photo pages. | Without it, edge copies show wrong counters for up to a day (IV.18). Found reading 009's rule. (research R-04) | Low; costs warm traffic, bounded by 009 batching. | PLAN |
| 10 | The gallery link targets `#p-{imageId}`, the id the tile's picture already carries; no scroll script. | Works without scripts; no new markup on the gallery page. (research R-03) | Low. | PLAN |
| 11 | If prerender activation ignores the fragment, the gallery link is excluded from Speculation Rules with a decided selector. | Decided now so no executor has to. (risk R-01-a) | Low. | PLAN |
| 12 | One tap-area rule on the `TextLink` atom (`Tap` parameter), used by Pager, GalleryLink and GalleryNeighbours; it also carries the ellipsis, so no consumer adds a second link rule (analyze M1). The admin library's Pager gets the same 44×44 tap boxes. | One rule, no per-page copies; owner prefers general rules. | Low. Overturn by passing `Tap=false` from the library. | PLAN |
| 13 | Long-title stacking is decided on the server by character budget (26 narrow, 60 wide), measured in the browser test at five widths. | No script and no container-query guesswork; the test checks the estimate. (research R-06) | Low: two constants. | PLAN |
| 14 | Keys are 5 lines in the existing inline module, placed before the Save-Data return, and click the real link. | Same navigation as a click (view transition, prerender reuse); Save-Data must not disable keys; stays one script under 30 lines. (research R-07) | Low. | PLAN |
| 15 | Header: the "Galleries" nav item is dropped; the brand is the only link. `SiteHeader` renders no `<nav>` when given no items. | FR-010; spec assumption. | Low: pass the item back. | PLAN (spec default) |
| 16 | Library status keeps the 009 " · public in M" suffix after the linked title and "+N". | 009 FR-007 copy; nothing in 010 asks to drop it. | Low. | PLAN |
| 17 | No `refresh-site` command: existing galleries get `nav` on the owner's first real gallery write after the build (quickstart Part C step 2). | Avoids an unrequested command; any write re-projects every gallery. (research R-11) | Low. | PLAN |
| 18 | `ProjectAsync()` keeps its signature; a new `ProjectWithNavAsync()` returns cards plus `NavChanged`. | Keeps `SiteProjectorTests` and other callers unmodified. | Low. | PLAN |
| 19 | Browser tests get their own `NavBrowserFixture` whose cards and `nav` are produced by the app's projector, not hand-written. `BrowserTests`/`BrowserSiteFixture` stay unmodified. | A hand-written `nav` would test the page, not the projection. (research R-13) | Low. | PLAN |
| 20 | Timing baseline is captured with the same evidence test BEFORE any code change and committed. | "No more reads than today" needs a recorded today. (research R-12) | n/a | PLAN |
| 21 | The photo page's gallery link gets the same 44×44 tap box as Previous and Next (`Tap="true"`); the title truncates inside it, the counter never does. | It is the main way back on a phone, the dead end the owner reported. (analyze M5) | Low: drop `Tap` on one anchor. | OWNER (approved analyze M5) |
| 22 | `SoloViewerPage` gets no new catalogue specimen; the gallery-link states live on `PagerPage`, and SoloViewer's pass-through is a component test. | `OrganismPageTests` counts exactly two thumb layers and two high-priority images on that page and must stay unmodified. (analyze H2) | Low. | PLAN |
| 23 | The library's `lib-status` span stays in `LibraryPage.razor`; `MembershipStatus` renders only its contents. | Keeps the page-scoped muted colour and the 009 class hook. (analyze M2) | Low. | PLAN |
| 24 | PhotoPage's new parameters are optional; without `GalleryHref` it renders as before. | Existing `PageComponentTests` render PhotoPage without them. (analyze M3) | Low. | PLAN |
