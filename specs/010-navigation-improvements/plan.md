# Implementation Plan: Navigation Improvements

**Branch**: `010-navigation-improvements` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/010-navigation-improvements/spec.md` (16 FRs, 9 SCs;
clarify done with the owner, 6 answers).

## Summary

Five navigation fixes, all server-rendered, built component-first:

- **Photo page**: the bottom row becomes `Previous   ← {Gallery} · N / total   Next`. The gallery
  link points at `/g/{id}/{slug}#p-{imageId}`, the id the gallery tile already carries, so the
  browser lands on the tile with or without scripts. Previous and Next keep their look but get
  44×44 tap boxes. Left, Right and Escape click the same links (a 5-line addition to the
  existing inline module; no new script).
- **Gallery page**: listed galleries end with `← previous title` / `next title →`. The
  neighbours are denormalised onto each gallery (`nav`) by `SiteProjector`, which already
  recomputes the home cards on every owner write, so the page stays at 2 reads. Private and
  empty galleries carry no `nav`, and the page renders neighbours only when public.
- **Header**: the duplicate "Galleries" item is dropped; the brand is the only header link.
- **Library**: the status links to the first gallery by title, `Coast +2`.
- **Purging**: galleries whose `nav` changed are purged (new P-NAV), and photo pages whose
  "N / total" changed are purged (widened `Changed`), so edge copies never show stale links.

No NuGet package, no new script file, no new public datastore read.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK 10.0.403, `latestPatch` pin from 002). Public browser
code stays inline in `PublicLayout`'s single module.

**Primary Dependencies**: existing only (MongoDB.Driver 3.13.0, AWSSDK.S3, MetadataExtractor).
Tests: xunit 2.9.3, Testcontainers Mongo and S3Proxy, Microsoft.Playwright 1.64.1. Nothing added.

**Storage**: MongoDB `galleries` gains optional `nav` (data-model.md). No index. No R2 change.

**Testing**: `HtmlRenderer` component tests, `LibraryHost` integration tests with the recording
edge provider, a new `NavBrowserFixture` (Playwright Chromium) for SC-001/003/004/007 and the
keys, and a test-hosted timing capture for SC-006 (research R-12, R-13).

**Target Platform**: visitors' browsers (desktop and phone, 360 px and up); the owner's macOS dev
machine for the run.

**Project Type**: web app (server-rendered minimal APIs and Razor components).

**Performance Goals**: SC-006, read from logs only: home exactly 1 datastore read, gallery and
photo ≤ 2, p95 server time < 50 ms each over 100 requests; layout shift 0 (SC-007).

**Constraints**:

- No new public read; `nav` rides on the gallery read the page already makes.
- Public pages keep exactly 2 `<script>` elements and the module stays under 30 lines (existing
  `PageComponentTests`, unmodified).
- Component outputs with new parameters unset stay byte-identical, except the stated changes
  below.
- The owner's `dotnet watch` on 5080/5081 is never touched.

**Scale/Scope**: up to about 50 galleries and 5,000 photos. 5 new components or component
changes, 3 new catalogue pages, 2 page changes, 1 admin row change, 1 projector change,
2 purge-set changes, about 8 new test classes.

No NEEDS CLARIFICATION remains (research.md R-01 to R-14).

## Constitution Check

*GATE: checked before Phase 0 and re-checked after Phase 1 (below). Constitution v4.3.0.*

| Principle | Status | How |
|---|---|---|
| I.1–I.5 ownership | N/A | No ownership change. `SiteProjector` adds only `galleries.nav` writes; it still never names the claim fields (`OwnerWriteGuardTests` unmodified). |
| I.6 CSRF | N/A | No new state-changing route. The library status link is a GET. |
| II.1 anonymous viewing | PASS | New links are plain anchors on public pages. |
| II.2 private = unlisted | PASS [SAFETY] | Two independent guards: the projector stores `nav` only on listed galleries and never names an unlisted one as a neighbour (data-model rule 2), and the gallery page renders neighbours only when `visibility == "public"`. A test writes a stale `nav` onto a private gallery and asserts no links render. Private pages get no "All galleries" link either (clarify). |
| II.3 unguessable ids | PASS | Links use the existing opaque ids. The `#p-{imageId}` fragment exposes an id the page already shows in its tile hrefs. |
| II.4 rename keeps links | PASS | The gallery link uses the current slug; an old slug 301s and the browser carries the fragment (research R-03, browser-tested). |
| II.5 owner decides visibility | PASS | No write changes visibility. |
| II.6 no empty gallery shown | PASS | An empty public gallery is not listed, so it has no `nav` and is nobody's neighbour (data-model rule 2). Its page stays 404. |
| III.1–III.10 | N/A | No image, storage or metadata change. |
| IV.1 request timing | PASS | Existing middleware; no new route. |
| IV.2 named timings | PASS | `nav` writes are inside the existing `db.site.project` timer; public reads keep `db.site.read`, `db.gallery.read`, `db.images.read` (contracts/log-events.md). |
| IV.3 Server-Timing | PASS | Unchanged; asserted on the three routes. |
| IV.4 SCs from logs | PASS | SC-006 by `evidence/p95.sh` over the captured log, self-tested on synthetic logs, with a baseline captured before any change. |
| IV.5 perceived-perf verification | PASS | Each IV rule this feature touches has a test: IV.8/CLS by Playwright at 3 widths; IV.13/IV.16 by the keys clicking the real links (browser test checks the neighbour image comes from the prefetch after a key press). |
| IV.8 no layout shift | PASS | The taller row and the neighbour links are in the first paint; no late insertion. CLS measured. |
| IV.9 home one read | PASS | Home unchanged (`homeCards`). |
| IV.10 purge then warm | PASS | P-NAV and widened `Changed` feed the same single enqueue (contracts/edge.md). |
| IV.11 Speculation Rules | PASS | Unchanged. Risk R-01-a recorded with a decided fallback selector. |
| IV.12–IV.17 | PASS | Hover preload, neighbour prefetch, Save-Data, thumbnail-first, view transitions and priorities unchanged. Keys are registered before the Save-Data return so Save-Data affects only fetching (edge case). |
| IV.18 bounded lifetime, purge on change | PASS | FR-009 via P-NAV; local output cache evicted by the same set. |
| IV.19–IV.21 | N/A | No header or asset change. |
| IV.22 no framework | PASS | 5 lines inside the existing module; no file, no library. |
| V.1 traces to spec | PASS | Every component and change maps to FR-001–FR-016 (contracts). |
| V.2 justified dependencies | PASS | None added. |
| V.3 works without JS | PASS | Links work without scripts; only the keys need them. SC-004 walk with JS off. |
| VI official CLIs | PASS | No scaffolding. `dotnet build/test` only. |
| VII.2 design deviations recorded | DEVIATION, handled | D-21 (Pager centre slot and tap areas, GalleryLink, GalleryNeighbours, MembershipStatus, SiteHeader with no nav, PublicLayout key handler), D-17 (key handler in the module), section 4 Gallery (`nav`), section 5 L-entries for publish/unpublish/rename/delete/home reorder/add/remove (P-NAV and counter purges). Edited in `specs/001-architecture-stack-design/draft/`, reassembled and validated as task-level work in this feature (research R-14). |
| VII.3 decisions | PASS | `decisions.md` rows 1+. |
| VIII cost | PASS | More purge and warm URLs per membership change (about 140 per add to a 23-photo gallery); Cloudflare purge is free and warming is within the 009 batching. No new metered service. |
| Workflow: containers and live walkthrough | PASS | Testcontainers; quickstart Part C. |

No Complexity Tracking entries: no MUST is crossed.

**Post-Phase 1 re-check**: still all PASS or N/A, with one handled VII.2 deviation. Phase 1 added
two guards and one finding:

- the source-scan guard that only `SiteProjector` writes `nav` (single writer);
- the render-time public check on the gallery page, tested with a planted stale `nav`;
- the finding that "N / total" makes every membership change stale on more photo pages than
  009's neighbour rule purges; fixed in `Changed` (research R-04), not deferred.

## Project Structure

### Documentation (this feature)

```text
specs/010-navigation-improvements/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md, decisions.md
├── contracts/components.md, contracts/edge.md, contracts/log-events.md
├── checklists/requirements.md
├── evidence/                  # baseline-timings.log, p95.sh, p95-selftest/, nav-timings.log,
│                              # verify.sh, non-vacuity.sh (+ outputs) — created by tasks
└── tasks.md                   # /speckit-tasks (not created here)
```

### Source Code

```text
src/Gallery.Web/
├── Data/Documents.cs                       # + GalleryLinkDoc, GalleryNavDoc, GalleryDoc.Nav
├── Library/SiteProjector.cs                # nav per listed gallery; returns ProjectionResult(Cards, NavChanged)
├── Library/OperationResult.cs              # OperationScope: P-NAV into the union; RefreshAsync records NavChanged
├── Library/LibraryReader.cs                # LibraryTile.FirstByTitle
├── Edge/PurgeSets.cs                       # Changed widened (index, count); PurgeSets.Nav
├── Site/PublicEndpoints.cs                 # photo: GalleryHref, Index, Total; gallery: Prev/Next when public
├── Site/Pages/PhotoPage.razor              # passes GalleryLink + Stack to SoloViewer
├── Site/Pages/GalleryPage.razor            # GalleryNeighbours after the grid
├── Admin/Pages/LibraryPage.razor           # RowFor uses MembershipStatus
├── Components/Atoms/TextLink.razor(.css)   # Tap parameter + the one tap rule
├── Components/Molecules/Pager.razor(.css)  # Here, Stack; links Tap; grid
├── Components/Molecules/PagerStack.cs      # NEW enum
├── Components/Molecules/GalleryLink.razor(.css)         # NEW
├── Components/Molecules/GalleryNeighbours.razor(.css)   # NEW
├── Components/Molecules/GalleryNeighbour.cs              # NEW record
├── Components/Molecules/MembershipStatus.razor(.css)    # NEW
├── Components/Organisms/SoloViewer.razor   # Here, Stack pass-through
├── Components/Organisms/SiteHeader.razor   # no <nav> when NavItems is empty
├── Components/Templates/PublicLayout.razor # no nav items; key handler in the module
├── Ui/UiCatalog.cs                         # + 3 entries
└── Ui/Pages/GalleryLinkPage.razor, GalleryNeighboursPage.razor, MembershipStatusPage.razor  # NEW
    Ui/Pages/PagerPage, SoloViewerPage, SiteHeaderPage, TextLinkPage .razor            # appended specimens

tests/Gallery.Web.Tests/
├── Fixtures/NavBrowserFixture.cs           # NEW (research R-13)
├── NavComponentTests, NavProjectionTests, NavPurgeTests, NavRoutesTests,
│   LibraryMembershipTests, NavSourceGuardTests, NavBrowserTests, NavTimingEvidenceTests .cs   # NEW
└── stated requirement changes: see "Test changes"

specs/001-architecture-stack-design/draft/  # D-17, D-21, section 4, section 5 edits; assemble + validate
```

**Structure Decision**: the existing per-surface layout from 006–009. New visual pieces are
molecules in `Components/Molecules/` (atomic level only, no public/admin split), each with a
`/ui/` page. Data logic stays in `Library/` (projector, reader) and `Edge/` (purge sets).

## Test changes (stated requirement changes; everything else unmodified)

1. **Pager link class and public header** (`text-link` → `text-link tap`, FR-004; no nav in the
   public header, FR-010). These change the bytes of: `PagerPage`, `SoloViewerPage` and
   `templates/public-layout` (006 ui-baseline, `UiPagesUnchangedTests`), and `SoloViewerPage` and
   `PublicLayoutPage` (005 public baseline, `PublicOutputUnchangedTests`). Those snapshots are
   regenerated by the verify-before-replace procedure: copy the baseline aside, delete it,
   re-run to rewrite it, then diff old against new with the intended changes stripped (the
   ` tap` class, the Pager wrapper markup, the removed `<nav class="nav" aria-label="Site">…</nav>`,
   appended specimens). Equal after stripping = commit; any residue = stop and escalate.
   Also affected, same procedure: catalogue pages with appended specimens (`TextLinkPage`,
   `SiteHeaderPage`), and the index (3 new entries). `molecules/nav` and
   `organisms/site-header`'s existing specimen must stay byte-identical (only appended).
2. **`IndexTests`**: entry count 31 → 34.
3. **`TemplatePageTests.SiteHeader_page_renders_brand_and_nav`**: unchanged (the catalogue page
   still shows the "Galleries" specimen first). Public pages lose the nav; any public-route
   assertion on "Galleries" in the header is flipped to assert-absent and named in the task.
4. **`PurgeSetTests`**: `Changed_is_added_removed_and_neighbour_changed_ids`,
   `P_MEMBERS_add_…` and `P_MEMBERS_remove_…` and `Union_…` gain the photo pages the widened
   `Changed` now returns (contracts/edge.md table), and the add test is renamed
   `P_MEMBERS_add_purges_every_photo_page_when_the_count_changes`. `P_DELETE_…`,
   `LibraryFlowTests` and `EdgeModeTests` expectations are rechecked against the new rule and
   updated only where the count changed. Every such edit is listed in tasks.md with the old and
   new expected set.
5. **`SiteProjectorTests`** and `InvariantChecker`: unchanged. The site document gains nothing
   (`nav` lives on galleries), and `ProjectAsync()` keeps its signature: it calls the new
   `ProjectWithNavAsync()` and returns its `.Cards`, so existing callers and tests compile and
   pass untouched.
6. **`LibraryPageTests` status assertions** for photos in 1+ galleries read the link text instead
   of the plain string. The zero-gallery assertion stays as is.

## Risks

- **R-01-a prerender and fragments**: a prerendered gallery page activated from the photo page
  might not scroll to `#p-{id}`. Caught by the browser test that hovers first; the fallback
  selector is decided (research R-03).
- **Widened purge sets** grow warm traffic per add (one gallery's photo pages). Bounded by the
  009 batching (≤ 100 URLs per call, ≤ 800/s). If the owner adds 100 photos to a 1,000-photo
  gallery, that is about 1,000 photo pages purged once; acceptable at this scale, logged in
  decisions row 9.
- **Character budget for stacking** (26 / 60) is an estimate of the system font; the browser test
  measures it at five widths and three title lengths.

## Roadmap notes

- 010 delivers the study's recommended set (ideas 1, 2, 3, 4, 6, 8, 11, 13). Ideas 5, 7, 9, 10,
  12, 14, 15, 16 stay out of scope (spec).
- No owner prerequisite before implement. After the build: quickstart Part C on the owner's own
  watch.
