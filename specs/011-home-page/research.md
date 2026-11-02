# Research: Home Page (011)

All decisions below come from reading the current code on branch `011-home-page`, which is
`010-navigation-improvements` plus the 011 spec and constitution 4.5.0. A throwaway routing probe
was attempted and its build approval timed out. Per the owner's rule that is a "no", so the two
framework behaviours it would have shown are pinned by tests in this feature instead (R-03, R-04)
and recorded as risks.

## R-01 Where the listing goes: move, not copy

- **Decision**: The existing `/` endpoint body (one `ContentReader.HomeAsync` read, the II.6
  filter, `ApplyHomeHeaders`, the `public-page` cache policy) moves verbatim to `/g`, and its page
  component is renamed `HomePage` → `ListingPage`. The new `/` endpoint renders a new `HomePage`
  that takes no data.
- **Rationale**: FR-007 and SC-004 ask for the same content, order, layout, loading and caching.
  Moving the handler unchanged gives that by construction, and keeps the read budget (IV.9) and
  timing events (FR-017) identical with no new code. The datastore field names (`homeCards`,
  `homeOrder`) and `HomeAsync()` keep their names. They are the owner's gallery order, which the
  constitution sweep left named "home order", and renaming them would mean a data migration for a
  label.
- **Alternatives**: keep `HomePage` as the listing and add a new `IndexPage` for `/`. Rejected:
  the component named Home would then be the page that isn't home. Calling the listing
  `GalleriesPage` was rejected because `Gallery.Web.Admin.Pages.GalleriesPage` already exists, and
  test files that import both namespaces would hit an ambiguous name.

## R-02 The home links: reuse `Nav`, don't add a component

- **Decision**: The home page renders the existing `Nav` molecule
  (`Components/Molecules/Nav.razor`, `NavItem(Text, Href, Current)`) with a new optional
  parameter `Home` (bool, default false). When it is true, the `nav` gets class `nav nav--home`
  and the items stack as a centred column at the display type size. The default output is
  byte-identical to today.
- **Rationale**: `Nav` already is "an ordered list of links" (FR-003) with `aria-label="Site"`,
  and since 010 owner review 2 removed it from the header it has no consumer left. Giving it this
  one consumer back follows D-21 ("a component is extracted only when it has a second consumer";
  here no new component is needed at all) and the owner's general-rule preference over one-off
  page markup (constitution IX.2: pages supply data, components own look).
- **Alternatives**: a new `HomeLinks` molecule. Rejected because it duplicates `Nav`, and a new
  catalogue entry also moves the `/ui/` index snapshot. Markup written directly in `HomePage`.
  Rejected because it puts styling in a page, which IX.2 forbids.

## R-03 `/g` and `/g/`

- **Decision**: Map `GET|HEAD /g`. A request for `/g/` answers `301` to `/g` with
  `ShortPolicy` caching, the same redirect type `/g/{id}` already uses. The test
  `Listing_trailing_slash_redirects` pins this. If routing does not deliver `/g/` to the `/g`
  endpoint (the result the blocked probe would have shown), the executor escalates and does not
  pick a mechanism itself.
- **Rationale**: The edge cache and the output cache key on the path, so one canonical address
  means one cache entry and one purge path (`/g`). The spec's edge-case line lists both forms.
- **Collision**: the `/g/{id}` route needs a segment, so `/g` never reaches it. `OpaqueId.IsValid`
  still guards `/g/{id}`.

## R-04 Predictive loading for the home link (FR-006)

- **Decision**: The `PublicLayout` speculation rule changes from
  `{"where":{"href_matches":"/g/*"}}` to
  `{"where":{"or":[{"href_matches":"/g"},{"href_matches":"/g/*"}]}}`, eagerness unchanged
  (`moderate`). The breadcrumb's "Galleries" link on gallery and photo pages becomes prerenderable
  as well, which is consistent with D-17.
- **Rationale**: The URL Pattern `/g/*` does not match `/g` (the `*` needs the `/` before it).
  The `or` form is in the Speculation Rules document-rules grammar Chrome ships.
- **Verification**: a markup assertion on the exact rule string. Real-browser prerender
  activation can't be observed under Playwright (Chrome refuses prerender under DevTools
  automation, recorded in 010 decisions row 38), so it is an OWNER-ONLY check:
  DevTools > Application > Speculative loads, on `/`.
- **IV.12 (hover warm of the destination image)** does not apply. The home link has no image, so
  it carries no `data-preload-srcset`.

## R-05 The home page script budget

- **Decision**: `HomePage` sets `Enhance="true"`, the same as today's `/`, so it keeps the
  speculation rules and the existing single inline module (keys, warm, prefetch, all of which
  no-op without their targets). No script lines change except the rule string in R-04.
- **Rationale**: FR-006 asks for predictive loading on `/`. The module is shared and already
  inlined by the layout, so dropping it would be a per-page divergence for no gain.

## R-06 Centring and size (FR-004, FR-005, SC-003)

- **Decision**:
  - New token `Tokens.TypeDisplay = 48`, emitted as `--type-display:48px`, listed in `All()`
    under Typography. It is the largest type token (the current maximum is `TypeBrand` 16).
  - `PublicLayout` gets an optional parameter `Fill` (bool). When it is true, `<body>` gets
    class `public fill`. Only then is the body a `100dvh` flex column, with `main` set to
    `flex:1` and its content centred (exact CSS in contracts/components.md C4).
  - The header height is never written as a literal: flex gives `main` whatever space is left.
  - `HomePage` passes `Fill="true"`.
  - `.nav--home ul` is `flex-direction:column; align-items:center; gap:var(--space-m)`.
  - `.nav--home a` is `font-size:var(--type-display); color:var(--color-text);
    display:inline-block; min-height:var(--tap); line-height:1.2; padding:0 var(--space-s)`.
- **Rationale**: IX.2 says the layout owns the frame, so the "fill the space under the header"
  rule lives on the container as a named option, not as page CSS. The flex rules are scoped to
  `body.public.fill`, so other pages' boxes cannot move. The measured chrome test (IX.4) checks
  the header on `/` against every other page type.
- **Risks to verify in the browser**:
  - A flex-column body stops margin collapsing between header and main. On `/` the IX.2 rule
    already zeroes main's first-child top margin, and the chrome test measures the header box.
    The gap to content on `/` is the centring gap and is excluded from that comparison for `home`
    only (contracts/test-impact.md T-IX).
  - The main padding-bottom must not cause a scrollbar on `/`. `Fill` sets it to 0, and SC-003
    checks `scrollHeight <= innerHeight`.

## R-07 Breadcrumbs (FR-011 to FR-015)

Every trail is built in one of five places today:
- `HomePage.razor:12`
- `PublicEndpoints.cs:117` (gallery)
- `PhotoPage.razor:23-25`
- `NotFoundPage.razor:7`
- `PublicLayout.razor:64` (the default)

- **Decision**: Add one static helper, `Site/Trails.cs`:
  - `Trails.Home = [new("Home")]`
  - `Trails.Listing = [new("Home", "/"), new("Galleries")]`
  - `Trails.Under(params Crumb[] tail) => [new("Home", "/"), new("Galleries", "/g"), ..tail]`
  - `Trails.NotFound = [new("Home", "/"), new("Not found")]`
  - `Trails.Default = [new("Home", "/")]`
  
  Every one of the five sites calls it. A source-scan guard (`TrailSourceGuardTests`) fails if any
  `new Crumb(`/`new("Galleries"` crumb literal for a public page appears outside `Trails.cs` and
  the `/ui` specimen pages.
- **Rationale**: Five hand-built trails are how the root label drifted before. One builder means
  the next root rename is one line. The private-page rule (FR-014) holds structurally, because
  `Under` only ever adds `/` and `/g` ahead of the gallery's own crumbs.
- **Escape key**: the selector in `PublicLayout.razor:21` is `.breadcrumb a[href*="#p-"]`, which
  only the photo page's gallery crumb matches. Adding `Home` and `Galleries` ahead of it changes
  nothing (FR-013).

## R-08 Purge and warm (FR-009)

- **Decision**: `PurgeSets.HomePath` is replaced by `PurgeSets.ListingPath = "/g"`. Every
  existing use moves with it, and the purge sets keep their membership rules, with the path
  string being the only change. `WarmFiles` matches `ListingPath`. The home page `/` is never in
  a purge set: it shows fixed links, so no owner change affects it (FR-009, second sentence).
- **Rationale**: The page that changes on an owner write is the listing. Purging `/` would cost a
  purge and a warm for nothing.
- **Test impact**: every assertion that expects `"/"` (or `LibraryHost.Page("/")`) in a purge or
  warm set now expects `"/g"`. This is a stated requirement change, listed per file in
  contracts/test-impact.md.

## R-09 Titles

- **Decision**: The listing's `<title>` is `Galleries` (spec Assumption). The home page keeps
  `Gallery`, today's `/` title.
- The 010 owner review 4 rule (no page heading under the breadcrumb) applies to the listing too:
  it shows no heading, and the breadcrumb names it.

## R-10 Admin links to the public site

- `OwnerBar` "View site ↗" (`/`) and `SignInCard` "Back to the gallery" (`/`) keep pointing at `/`
  (spec edge case).
- **Default (overturnable)**: the builder's View link for a gallery with no photos
  (`Admin/Pages/GalleryPage.razor:30`, `PhotoHrefBase ?? "/"`) goes to `/g`, the listing, which
  is the nearer page. A one-line change, logged as a DEFAULT row.

## R-11 Evidence and timing (FR-017, SC-005, SC-006)

- **Decision**: Add `specs/011-home-page/evidence/p95.sh` with routes `/ /g /g/{id}/{slug}
  /g/{id}/{slug}/{imageId}` and max reads `0 1 2 2`. It is copied from the 010 script with the
  route list changed, and its self-test is re-run. The test-hosted timing capture
  (`NavTimingEvidenceTests` pattern) adds `/` and `/g`.
- Comparisons are literal: `/` has `db_reads == 0`, `/g` has `db_reads == 1` (an equality, not
  `<=`).
- The `http.request` route template for the listing is `/g` (RoutePattern.RawText).

## R-12 Snapshots that move (stated changes)

- `specs/006-public-site-local/evidence/ui-baseline/` is strict byte-identity
  (`UiPagesUnchangedTests`):
  - `tokens.html` gains the TypeDisplay row
  - `molecules/nav.html` gains the Home specimens (one link, three links)
  - `molecules/breadcrumb.html` and `organisms/site-header.html` move to the Home root
  - `templates/public-layout.html` moves for the default trail
- `specs/005-admin-ui-components/evidence/baseline/PublicLayoutPage.html` is head/tail strict
  (`PublicOutputUnchangedTests`). It changes for the default trail and the speculation rule.
- Before regeneration, the pre-change copies are frozen into
  `specs/011-home-page/evidence/pre-011-specimens/`. Then `snapshot-diff.sh` strips exactly the
  intended changes:
  - `Galleries` root → `Home` root
  - the rule string
  - the token row
  - the appended Nav specimens
  
  It must report EQUAL. It is self-tested first: it must report RESIDUE on a stray edit.
