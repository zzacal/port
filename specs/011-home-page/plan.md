# Implementation Plan: Home Page

**Branch**: `011-home-page` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/011-home-page/spec.md` (19 FRs, 7 SCs; no clarify
run; constitution amended to 4.5.0 first, FR-019).

## Summary

`/` becomes a home page whose only content is a centred, display-size "Galleries" link. The
gallery grid moves unchanged to `/g`, and every public breadcrumb starts at "Home".

How it is built:

- **Routes**: the existing `/` handler moves verbatim to `/g`. Its page component is renamed
  `HomePage` → `ListingPage`. The new `/` renders a data-free `HomePage` with zero datastore
  reads. `/g/` redirects 301 to `/g`.
- **Home links**: the existing `Nav` molecule, which has had no consumer since 010 owner review 2,
  gets an optional `Home` mode that stacks centred links at the new `TypeDisplay` (48 px) token.
  `PublicLayout` gets an optional `Fill` mode that makes `main` take the space under the header and
  centre its content. Both live in shared components, not page CSS (IX.2).
- **Breadcrumbs**: one `Site/Trails.cs` builds every public trail, rooted at `Home`. A source
  guard keeps the trails single-sourced.
- **Purge and warm**: `HomePath "/"` becomes `ListingPath "/g"`. The home page is never purged.
- **Predictive loading**: the speculation rule also matches `/g`.

No package, no new script file, no new datastore field, and no new public read.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK pinned by `global.json`, `latestPatch`), Razor
components through `RazorComponentResult`. Unchanged.

**Primary Dependencies**: existing only (MongoDB.Driver, AWSSDK.S3, MetadataExtractor). Tests:
xunit, Testcontainers Mongo and S3Proxy, Microsoft.Playwright. Nothing added.

**Storage**: no change. `site.homeCards` and `site.homeOrder` keep their names (research R-01).

**Testing**:
- `HtmlRenderer` component tests for `Nav` Home mode, `HomePage`, `ListingPage`, `Trails`.
- `LibraryHost`/`AdminHost` integration tests for routes, reads, purge sets and breadcrumbs.
- `NavBrowserFixture` Playwright tests for SC-003 centring and SC-007 chrome geometry.
- Test-hosted timing capture plus `evidence/p95.sh` for SC-005 and SC-006.

**Target Platform**: local dev (Kestrel, local Mongo, dev R2), per the roadmap's local-first rule.

**Project Type**: server-rendered web app (`src/Gallery.Web`, `src/Gallery.Core`).

**Performance Goals**: `/` p95 under 50 ms with 0 reads. `/g` keeps 1 read and the 006 p95 bound.

**Constraints**:
- Public pages must work without JS (V.3).
- No inline `style` attributes (D-21 guard).
- Existing tests pass unmodified, except the stated changes in contracts/test-impact.md.
- The owner may run `dotnet watch` on 5080/5081. Build into `--artifacts-path artifacts`, never
  kill or bind those ports.

**Scale/Scope**: about 12 source files touched. New source: `Trails.cs`, with `ListingPage.razor`
by rename and `HomePage.razor` rewritten. New test files: TypeDisplayTests, TrailsTests,
HomePageTests, TrailRoutesTests, TrailSourceGuardTests, HomeBrowserTests and the
HomeCaptureLibrary fixture. Evidence scripts are under `evidence/`. Existing test edits are
listed in contracts/test-impact.md.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I Single owner | N/A (justified) | No admin or auth change. |
| II.1 Anonymous viewing | PASS | `/` and `/g` are anonymous GETs. |
| II.2 Private never listed (4.5.0) | PASS | Listing handler moved verbatim, II.6 filter included. Private pages' trails link only `/`, `/g`, self (Trails.Under). Test: private gallery trail hrefs. |
| II.3/II.4 Addresses | PASS | `/g/{id}/{slug}` unchanged; `/g` has no id segment (R-03). |
| II.6 Empty never shown (4.5.0) | PASS | Same filter, now on `/g`. Existing II.6 tests retargeted to `/g` (stated change). |
| III Images leak nothing | N/A (justified) | No image path changes. The secret-scan loops gain `/g`. |
| IV.1–4 Measurement | PASS | `http.request` on `/` and `/g`. `db.site.read` timing unchanged on `/g`; `/` emits none. p95.sh with literal `== 0` / `== 1` read checks (R-11). |
| IV.6–8 Images | PASS | Listing markup unchanged (SC-004 snapshot). The home page has no image. |
| IV.9 One read; home none (4.5.0) | PASS | `/g` = 1 read, `/` = 0 reads, asserted by equality. |
| IV.10 Warm | PASS | `WarmFiles` keys on `ListingPath` (R-08). |
| IV.11 Speculation Rules | PASS | Rule extended to `/g` (R-04); prerender is an OWNER-ONLY real-Chrome check. |
| IV.12–17 | PASS | Listing unchanged. The home link has no image to warm (R-04). |
| IV.18 Edge cacheable | PASS | `/` keeps `HtmlPolicy` and the output cache; `/g` takes over the listing's purge. |
| IV.20 bfcache | PASS | No `unload`, no `no-store`. |
| IV.22 No framework, inline CSS | PASS | CSS in `.razor.css` (inlined); no script added. |
| V.1 Minimal scope | PASS | Severable seams named in the spec checklist; no extras beyond them. |
| V.2 Dependencies | PASS | None added. |
| V.3 No-JS | PASS | Home is a link; no-JS browser test on `/` → `/g`. |
| VI CLI scaffolding | N/A (justified) | No project or package change. |
| VII.2 Design record | DEVIATION-handled | D-13, D-15, D-16, D-20, D-21 and lifecycle "Pages and caches" name the home page as the grid. Task: edit `specs/001-*/draft/02c,02d,05,06,07`, run assemble and validate. |
| VIII Cost | PASS | One fewer purged URL per change, never more. |
| IX.1–2 One container | PASS | Centring is `PublicLayout.Fill`, a container option; links are `Nav`; the page has no CSS. |
| IX.3 Size independent of parts | PASS | Breadcrumb row stays `--tap`; the one-item `Home` trail is the same height (measured). |
| IX.1 Same gap under the chrome | PASS | Centring lives inside `nav.nav--home`, which fills main, so main's first box starts at the normal gap. Home is compared on all seven chrome measures (analyze C1). |
| IX.4 Measured consistency | PASS | `PublicLayoutConsistencyBrowserTests` page list gains `listing` (`/g`); `home` stays; no exceptions. |

No violations, so Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: unchanged. Every row still PASS, N/A or
DEVIATION-handled. The contracts add no read, no script and no dependency.

## Project Structure

### Documentation (this feature)

```text
specs/011-home-page/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── routes.md          # /, /g, /g/ and what each emits
│   ├── components.md      # Nav Home mode, PublicLayout Fill, Trails, TypeDisplay, exact CSS
│   ├── edge.md            # ListingPath replaces HomePath; warm
│   └── test-impact.md     # stated changes to existing tests and snapshots
├── checklists/requirements.md
├── decisions.md           # created by tasks/implement
└── tasks.md               # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Core/Design/Tokens.cs                         # + TypeDisplay
src/Gallery.Web/Site/PublicEndpoints.cs                   # "/" → HomePage (0 reads); "/g" listing; "/g/" 301
src/Gallery.Web/Site/Trails.cs                            # NEW: every public trail
src/Gallery.Web/Site/Pages/HomePage.razor                 # REWRITTEN: Nav Home, Fill
src/Gallery.Web/Site/Pages/ListingPage.razor              # RENAMED from HomePage (git mv), Title "Galleries"
src/Gallery.Web/Site/Pages/PhotoPage.razor                # crumbs via Trails
src/Gallery.Web/Site/Pages/NotFoundPage.razor             # crumbs via Trails
src/Gallery.Web/Components/Molecules/Nav.razor(.css)      # + Home mode
src/Gallery.Web/Components/Templates/PublicLayout.razor(.css)  # + Fill; default trail; speculation rule
src/Gallery.Web/Edge/PurgeSets.cs                         # HomePath → ListingPath "/g"
src/Gallery.Web/Admin/Pages/GalleryPage.razor             # empty-gallery View → /g (DEFAULT, R-10)
src/Gallery.Web/Ui/Pages/NavPage.razor, BreadcrumbPage.razor, SiteHeaderPage.razor  # specimens
tests/Gallery.Web.Tests/HomePageTests.cs                  # NEW (routes, reads, trails, guard)
tests/Gallery.Web.Tests/HomeBrowserTests.cs               # NEW (SC-003 centring, no-JS)
specs/001-architecture-stack-design/draft/*.md            # VII.2 record update
specs/011-home-page/evidence/                             # p95.sh, snapshot-diff.sh, verify.sh, non-vacuity.sh
```

**Structure Decision**: The existing two-project layout is unchanged. New code sits beside the
public endpoints (`Site/`), and the shared look sits in the existing component and template
files.

## Complexity Tracking

None.
