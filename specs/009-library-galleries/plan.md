# Implementation Plan: Library and Gallery Management

**Branch**: `009-library-galleries` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/009-library-galleries/spec.md` (50 FRs, 12 SCs;
clarify done with defaults while the owner was away). Unattended run: every choice below that the
owner did not make is a DEFAULT row in `decisions.md` (rows 15+).

## Summary

The feature adds the three owner pages the D-23 design names, all inside the existing `/admin`
group and composed from the 005 components:

- **Library** (`/admin/library`): every image, newest first, 100 per page. Each tile has a select
  mark, a status line under the title, and Edit and View links. A selection bar offers "Add to
  gallery…" and "Delete…". A side panel edits the title and caption and holds the delete area.
- **Gallery list** (`/admin/galleries`): the home grid in home order with private galleries
  dimmed. Each card has a status, a drag handle, a Public switch, and View and Edit links.
  "New gallery" is the page's primary action.
- **Gallery builder** (`/admin/galleries/{id}`): the gallery grid. Under each title is a row with
  the drag handle, the cover control, Remove and View. A side panel edits the title and
  description, holds the Public switch and View gallery, and has a danger area for delete.

Underneath, five pieces:

1. **Write services**, built from ordered single-document updates. `Gallery.items` is
   authoritative, and `Image.galleryIds` is a derived index that can be repaired. A gallery card
   builder and a site projector are shared by every path.
2. **Image delete**: tombstone first, then leave every gallery, then delete the files (a shared
   content-hash file is kept), then remove the record. A repeat finishes an interrupted run.
3. **Purge and warm** behind `IEdgeCache`. A background queue and worker split each batch into
   calls of at most 100 URLs at no more than 800/s. There are three providers: log-only (the
   default, since no credentials exist), the development zone (image-file URLs only), and a test
   recorder. Purge sets are computed purely from before and after snapshots.
4. **Per-page eviction of the local output cache** in the request.
5. **Seed retirement**: the seed command, manifest and scripts are deleted. A one-time
   `remove-seeded` command deletes seeded content through the same delete services, behind the
   seed's development-target guard.

No NuGet package is added. Probe results are in `evidence/probe-results.txt`:

- P1: the dev and test Mongo instances are standalone, so the plan uses no transactions.
- P2: output-cache eviction did not run, because an approval prompt was denied. The fallback is
  recorded as R-07.
- P3: S3 deletes of missing keys are not errors.
- P4: the Cloudflare purge limits and permission come from fetched docs.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK 10.0.403 `latestPatch`). Admin browser code is plain ES
modules with no bundler, as in 008.

**Primary Dependencies**: existing AWSSDK.S3 4.0.104.8, MongoDB.Driver 3.13.0 and MetadataExtractor
2.9.3. Built-in `System.Threading.Channels`, `BackgroundService`, `IHttpClientFactory`,
`IOutputCacheStore`. No new package and no new vendored asset (htmx is not added; research R-08).

**Storage**: MongoDB `images`, `galleries`, `site` (data-model.md: new tombstone field and three
indexes; `GalleryDoc.SeedKey` becomes nullable). R2 `gallery-dev-private` (`masters/`) and
`gallery-dev-public`. S3Proxy in tests.

**Testing**: xunit 2.9.3 with Testcontainers Mongo and S3Proxy (existing fixtures), `AdminHost`
owner sessions, a new `LibraryHost` with the recording edge provider, `HtmlRenderer` component
tests, and Playwright Chrome (`LibraryBrowserFixture`, modelled on `UploadBrowserFixture`). No
real Cloudflare call is made in any test.

**Target Platform**: the owner's macOS dev machine; desktop Chrome/Safari for the owner pages.

**Project Type**: web app (server-rendered minimal APIs and Razor components; three admin pages,
two admin scripts, one console command).

**Performance Goals**: all read from logs (`contracts/log-events.md`):

- SC-002: a 100-image library page within p95 500 ms.
- SC-003: each save, including bulk saves of up to 100, within p95 500 ms, with no purge or warm
  inside the request.
- SC-005: purge and warm finish within 60 s of the change.
- SC-007: the home page stays one read.

**Constraints**:

- No public page markup changes. Public routes only gain an output-cache tag.
- No owner markup or script on public pages (FR-003).
- Existing tests pass unmodified, except the stated requirement changes listed under Test changes.
- The owner's `dotnet watch` on 5080/5081 is never touched.

**Scale/Scope**: one owner and up to 5,000 images. There are 14 owner routes, 3 pages, 6 optional
component parameters, 2 scripts, 1 command and about 12 new test classes.

## Constitution Check

*GATE: checked before Phase 0 and re-checked after Phase 1 (below). Constitution v4.2.0.*

| Principle | Status | How |
|---|---|---|
| I.1, I.2, I.4, I.5 | N/A | No ownership change. `SiteProjector` sets only `homeOrder`, `homeCards`, `updatedAt` (`OwnerWriteGuardTests` keeps scanning; its Seeder allowance is removed with the seeder). |
| I.3 others refused | PASS [SAFETY] | Every route in the `/admin` group, before the catch-all. `LibraryAccessTests`: no session → 302/401 on all 14. |
| I.6 CSRF | PASS [SAFETY] | Existing `AdminAntiforgeryFilter` on the group. Every POST is tested without a token and expects 400. |
| II.1 | PASS | Public routes are unchanged apart from the cache tag. |
| II.2 private unlisted, noindex, purge on change | PASS | `SiteProjector` lists only public, non-empty galleries. Unpublish purges `/` and the gallery pages (P-VIS). |
| II.3 unguessable ids | PASS | `OpaqueId.NewId()` for new galleries. |
| II.4 rename keeps links | PASS | The slug is recomputed and the existing route 301s a stale slug. A test checks the old URL gets 301. |
| II.5 visible only by owner action | PASS | Create, edit, reorder and upload never touch `visibility`. The picker's public-gallery note is shown before adding (FR-011). Tested. |
| II.6 no empty gallery shown; publish refuses | PASS [SAFETY] | G4 checks `items` first, then makes a conditional write on `items.0`. The switch is disabled with the reason. The refusal is tested with a full body, `confirm=1`, and no body. |
| III.1 masters private | PASS | Delete is the only new master operation. |
| III.2–III.8 | PASS / N/A | No new path writes to the public bucket. Deletes remove public objects only, guarded by the shared-file rule ([SAFETY]). |
| III.9 credentials | PASS | No new storage credential. `Edge:ApiToken` comes from user-secrets, goes out only in the outbound `Authorization` header, and is never logged or rendered ([SAFETY] secret scan, SC-011). |
| III.10 seeded dev content | N/A (retired) | The seed is removed (FR-045). III.10 is a permission that is no longer used, so no MUST is affected and no amendment is needed. The design-record row is now `n/a`. The removal command keeps the seed's target guard ([SAFETY]). |
| IV.1 request timing | PASS | The existing middleware covers every new route. |
| IV.2 named timings | PASS | `contracts/log-events.md`: db ops, `s3.delete`, `edge.purge.call`, `edge.warm.request`, bulk totals, `edge.lag`. |
| IV.3 Server-Timing | PASS | Public pages are unchanged. |
| IV.4 SCs from logs | PASS | `evidence/p95.sh` reads SC-002/003/005 and is tested on synthetic logs first. |
| IV.5 perceived-perf verification | N/A | No public rendering change. |
| IV.6–IV.8 | N/A | No new derivatives. |
| IV.9 home one read | PASS | `homeCards` is recomputed on every write. `InvariantTests` asserts `db_reads=1` on `/` after each operation (SC-007). |
| IV.10 purge then warm on public change | PASS | `IEdgeCache` and `EdgeWorker` (research R-06). Warming covers pages when hosting exists and image files now. The pages of private galleries are included (D-16 amended). |
| IV.11–IV.17 | N/A | No public markup change. |
| IV.18 bounded lifetime, purge on change | PASS | Purge is best effort. A failure is logged and the 1-day HTML / immutable TTL is the fallback (FR-040). The local output cache is evicted per page (FR-044). |
| IV.19–IV.21 | N/A / PASS | No header change. Deleted files are content-addressed and purged by URL. |
| IV.22 no framework on public pages | PASS | The scripts are admin-only. A test asserts no `/admin-assets/` script appears on a public route. |
| V.1 traces to spec | PASS | Every route, component addition and event maps to FRs (contracts). |
| V.2 justified dependencies | PASS | None added. Channels, BackgroundService and HttpClient are built in. |
| V.3 public works without JS; admin may need it | PASS | Saves are plain forms. Drag and bulk selection need scripts. |
| VI official CLIs | PASS | No scaffolding. File moves use `git mv`. `dotnet build/test` only. |
| VII.2 design deviations recorded | DEVIATION, handled | D-16, D-23, content model, L-07–L-17, E-16–E-18 and traceability II.6/III.10 were edited in `specs/001-*/draft/` and reassembled. `validate.sh` reports 0 failures. They are committed in 90ba78c before this plan. |
| VII.3 decisions | PASS | `decisions.md` rows 15+. |
| VIII cost | PASS | R2 deletes are free Class A operations (well under 1M). Cloudflare purge is free on the Free plan. No new metered service. |
| Platform | PASS | R2 two buckets, Mongo, .NET 10 minimal APIs, web components for admin interactivity. |
| Workflow: containers and live walkthrough | PASS | Testcontainers. quickstart Part C is the owner walkthrough. |

No Complexity Tracking entries: no MUST is crossed.

**Post-Phase 1 re-check**: still all PASS or N/A, with one handled VII.2 deviation. Phase 1 added
three guards and no new risk:

- the conditional publish write (closes the empty-publish race; strengthens II.6);
- the tombstone and repair sequence (FR-017, FR-036);
- the `$nin:[null,""]` removal filter (owner content can never be selected).

## Project Structure

### Documentation (this feature)

```text
specs/009-library-galleries/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md, decisions.md
├── contracts/http.md, contracts/edge.md, contracts/components.md,
│   contracts/log-events.md, contracts/removal-command.md
├── checklists/requirements.md
├── evidence/probe-results.txt                       # P1–P4
└── tasks.md                                         # /speckit-tasks (not created here)
```

### Source Code

```text
src/Gallery.Web/
├── Program.cs                         # `seed` removed; `remove-seeded` → Maintenance/RemoveSeededCommand
├── AppBuilder.cs                      # AddLibrary(config), AddEdge(config); MapLibrary inside MapAdmin
├── Admin/AdminEndpoints.cs            # admin.MapLibrary() before the H6 catch-all
├── Admin/Pages/LibraryPage.razor      # NEW C1
├── Admin/Pages/GalleriesPage.razor    # NEW C2
├── Admin/Pages/GalleryPage.razor      # NEW C3 (builder)
├── Library/                           # NEW
│   ├── LibraryOptions.cs              # Library:* limits (R-09)
│   ├── LibrarySetup.cs                # DI registrations
│   ├── LibraryEndpoints.cs            # L1–L5, G1–G4, B1–B6 (contracts/http.md)
│   ├── LibraryReader.cs               # page query, gallery refs, status lines (R-04)
│   ├── ImageService.cs                # title/caption save, add-to-gallery (single + bulk loop)
│   ├── ImageDeleteService.cs          # R-02 sequence, shared-file rule
│   ├── GalleryService.cs              # create, rename/description, reorder, cover, remove, publish, delete
│   ├── CardBuilder.cs                 # one card function
│   ├── SiteProjector.cs               # homeCards from homeOrder
│   ├── MembershipRepair.cs            # galleryIds from items
│   └── PageCacheEvictor.cs            # IOutputCacheStore.EvictByTagAsync per page path
├── Edge/                              # NEW
│   ├── EdgeOptions.cs, EdgeSetup.cs
│   ├── PurgeSets.cs                   # pure address computation (contracts/edge.md)
│   ├── IEdgeCache.cs, EdgeQueue.cs    # Channel-backed enqueue
│   ├── EdgeWorker.cs                  # BackgroundService: batch, rate, purge, warm, log
│   ├── IEdgeProvider.cs, LogOnlyEdgeProvider.cs, CloudflareEdgeProvider.cs
├── Maintenance/                       # NEW
│   ├── DevTargetGuard.cs              # the seed's three checks
│   └── RemoveSeededCommand.cs
├── Data/Documents.cs                  # GalleryDoc.SeedKey → string? [BsonIgnoreIfNull]; ImageDoc.DeletingAt
├── Data/Indexes.cs                    # NEW: seedKey partial (moved from Seeder) + 009 indexes
├── Site/PublicEndpoints.cs            # adds the page:{path} tag only (R-07); markup unchanged
├── Site/PublicCache.cs                # page tag helper; CacheOutput wiring if the R-07 test shows it is missing
├── Upload/ConfirmService.cs           # calls Indexes.EnsureAsync; title max from LibraryOptions
├── Upload/DerivativeValidator.cs      # git mv from Seed/ (namespace Gallery.Web.Upload)
├── Upload/CameraFields.cs             # git mv from Seed/
├── Seed/                              # DELETED: Seeder, SeedCommand, Manifest, PhotoJson
├── Components/Atoms/Switch.razor, Input.razor                 # K1, K2 optional params
├── Components/Molecules/VisibilityToggle.razor, CoverMarker.razor, SelectionBar.razor  # K3–K5
├── Components/Organisms/GalleryBuilder.razor                  # K6
└── wwwroot/admin-assets/library.js, reorder.js                # NEW C4

seed/manifest.json, scripts/seed-manifest.sh, scripts/prepare-photos.sh   # DELETED

tests/Gallery.Web.Tests/
├── Fixtures/LibraryHost.cs, Fixtures/RecordingEdgeProvider.cs, Fixtures/LibraryBrowserFixture.cs,
│   Fixtures/InvariantChecker.cs, Fixtures/SeedShapes.cs   # NEW (seed-shaped docs without the seeder)
├── Library*/Gallery*/Publish*/Builder*/ImageDelete*/Invariant*/PurgeSet*/Edge*/OutputCacheEviction*/
│   RemoveSeeded*/LibraryAccess*/LibraryComponentAdditions*/LibraryBrowser* Tests.cs   # NEW
├── SeederTests.cs, SeedCommandTests.cs, ManifestTests.cs                   # DELETED (code retired)
└── UploadSeedCoexistenceTests.cs, OwnerClaimTests.cs (OwnerClaimSeedTests), UploadConfirmTests.cs (2 seed facts),
    AdminAccessTests.cs (library 404 → 200), OwnerWriteGuardTests.cs (Seeder allowance removed)   # requirement changes
```

**Structure Decision**: owner content management gets a `Library/` folder beside `Upload/`,
`Site/` and `Admin/`, following the per-surface layout of 008. Edge purge is its own `Edge/`
folder because the hosting feature extends it. Pages go in `Admin/Pages/`. Components stay
organised by atomic level with optional parameters only.

## Design record updates (VII.2, committed before this plan: 90ba78c)

The edits were made in `specs/001-architecture-stack-design/draft/` and reassembled with
`evidence/assemble.sh`. `evidence/validate.sh` reports 0 failures.

- **D-16**: pages of private galleries that hold photos are purged too. Purge and warm run after
  the save in a background queue behind one interface with three modes. The warm scope is named.
- **D-23 builder**: controls sit in a row under the tile title, never as corner overlays. Only the
  library select mark is drawn over a photo.
- **Content model**: the shared-file delete rule. `items` is authoritative and `galleryIds` is
  derived.
- **L-07, L-08, L-09, L-11, L-12, L-13, L-15, L-16, L-17**: purge sets now name the photo pages and
  private galleries with pages. L-09 carries the shared-file rule and leftover handling.
- **E-16**: the publish refusal is the L-11 action (the seed-manifest stand-in is retired).
  **E-17, E-18**: the shared-file rule.
- **Traceability II.6**: names the publish refusal and its tests. **III.10**: `n/a: seeding
  retired by feature 009`.

## Test changes (stated requirement changes; everything else unmodified)

1. `AdminAccessTests.Owner_GetAdminUpload_returns_200_and_library_still_404`: the library assertion
   flips to 200, the 404 guard moves to `/admin/not-built`, and the test is renamed.
2. `SeederTests`, `SeedCommandTests`, `ManifestTests` are deleted with the retired code.
   `OwnerClaimSeedTests` (reseed preserves the claim) is deleted because no reseed path exists; its
   I.4 check moves to `RemoveSeededTests` (a claim set before the removal command survives it) and
   stays covered by `OwnerWriteGuardTests` and by `SiteProjector` writing only its three fields.
3. `UploadSeedCoexistenceTests` and the two seed facts in `UploadConfirmTests` stop calling the
   seeder. They insert seed-shaped documents (`Fixtures/SeedShapes.cs`) and call
   `Indexes.EnsureAsync`. Their assertions stay (a second image without `seedKey` confirms; a
   legacy plain index is upgraded).
4. `OwnerWriteGuardTests`: the `Seed/Seeder.cs` allowance is removed (the file is gone). The
   assertion stays.
5. `PublicRoutesTests` and `BrowserSiteFixture` already insert documents directly with `SeedKey`
   set. They keep compiling, since `string?` accepts the value, and stay unmodified.

## Risks

- **R-07 output cache may be inactive** (P2 not run). The first Library task is the
  `OutputCacheEvictionTests` red test. If the 006 cache never applies, wiring `CacheOutput` changes
  no markup but makes local pages cache for 60 s, and eviction then matters. Logged as decisions
  row 23.
- **Bulk delete of 100 within 500 ms (SC-003)**: 100 × (tombstone + gallery updates + about 13
  storage deletes) can exceed 500 ms against R2. The test target is S3Proxy and local Mongo. The
  live R2 timing is measured in Part C and, if it fails, reported as an SC deviation with the
  measured p95 (decisions row 22) rather than moving storage deletes out of the request.

## Roadmap notes (for the parent; docs/roadmap.md not edited)

- 009 delivers L-07–L-17, E-12, E-14–E-18, R-03, R-04, the D-23 Library, Gallery list and
  Gallery builder pages, the D-16 purge and warm (log-only plus the dev-zone image purge), the
  II.6 publish refusal, and the seed removal.
- Still owed to the hosting feature: live page purge (`Edge:PurgePages=true` once the site
  hostname is on the zone) and edge HTML caching rules.
- Owner prerequisite after the build: `Edge:ZoneId` and `Edge:ApiToken` (Cache Purge only, dev
  image zone) in user-secrets. Until then purge is log-only.
- Optional constitution PATCH later: delete the now-unused III.10 permission and the "or seeded"
  clauses in III.2 and IV.6. This is not required, since no MUST is affected.
