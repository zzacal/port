# Implementation Plan: By Camera

**Branch**: `013-by-camera` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: `specs/013-by-camera/spec.md` (20 FRs, 6 SCs; clarify 1A: camera-scoped photo view).
Constitution 6.1.0; no amendment needed.

## Summary

Three public pages mirror the gallery pages:
- `/c`, the camera listing;
- `/c/{slug}`, a camera's grid;
- `/c/{slug}/{iid}`, a camera photo view that steps within the camera.

The home page gains a "By Camera" link.

How it is built:

- **Data**: a new derived `cameras` collection, one document per camera with visible photos.
  `SiteProjector` is its only writer, in the projection that already runs on every owner write.
  Each document holds the visible photo ids in order, a rank, and a denormalised card. Visibility
  is "in at least one listed gallery". Pages therefore read exactly as many documents as the
  pages they mirror (1 / 2 / 2).
- **Grouping**: make and model, normalised (trim, collapse whitespace, case-fold). The display
  name comes from the photo page's own `ExifFormat.Camera`. The address is a readable slug, which
  is safe because camera names are already public.
- **Freshness**: each projection reports which cameras changed. `OperationScope` adds a P-CAM
  purge set (listing, camera pages and changed camera photo pages, under old and new slugs) and
  P-CAMTEXT for title edits to the ONE edge work item it already sends, and warms first-row
  images.
- **UI**: two new pages copied from `ListingPage` and `GalleryPage`. `PhotoPage` is reused through
  an optional `Crumbs` parameter. Trails come from `Trails.cs`. The speculation rule adds `/c` and
  `/c/*`.

No package, no new script, no owner dashboard step, and no change to stored `images`,
`galleries` or `site`.

## Technical Context

**Language/Version**: C# / .NET 10 (pinned by `global.json`), Razor components via
`RazorComponentResult`. Unchanged.

**Primary Dependencies**: existing only (MongoDB.Driver, AWSSDK.S3, MetadataExtractor; tests:
xunit, Testcontainers, Microsoft.Playwright). Nothing added (V.2).

**Storage**: MongoDB, plus the new collection `cameras` (data-model.md) with a unique index on
`slug` and an index on `rank`, created in `Indexes.EnsureAsync`. R2 is unchanged.

**Testing**:
- pure unit tests for `SiteProjector.Cameras`, `PurgeSets.Cameras` and `CameraText`, using the
  edge.md worked examples;
- `LibraryHost` integration tests for each owner action against the stored `cameras` documents
  and the purge sets;
- route tests for C1-C3, covering 404 parity and read counts;
- `HtmlRenderer` page tests;
- Playwright: the layout-consistency rows, keyboard stepping and no-JS.

**Target Platform**: Azure App Service Linux B2 behind Cloudflare (live at port.zacal.me).

**Project Type**: server-rendered web app (`src/Gallery.Web`, `src/Gallery.Core`).

**Performance Goals**: SC-004. Camera pages' p95 server time and `db_reads` are at most those of
the mirror pages, measured from `http.request` logs.

**Constraints**:
- reads 1 / 2 / 2 (research R-04);
- no client framework, and works without JavaScript (IV.22, V.3);
- one `EdgeWork` per owner request;
- the projector stays the single writer.

**Scale/Scope**: about 1,000 photos and about 20 cameras. The projection adds one `images` find
(about 300 KB) and one `cameras` find per owner write (research R-05). There is no visitor-time
aggregation.

## Constitution Check

Pre-research and post-design results are the same.

| Rule | Status | How |
|---|---|---|
| I.1-I.6 | PASS | No admin surface added; public routes only. |
| II.2 private never listed | PASS [SAFETY] | Visible = in a listed gallery (data-model); projector single writer + source guard; projection unit tests; route 404 parity (research R-09). |
| II.3 unguessable | PASS | Gallery and image ids stay opaque. Camera slugs are public facts with no private content behind them (R-02). |
| II.4 renames don't break links | PASS | Camera pages never carry gallery slugs. The camera slug is stable per key (data-model `slug` rule). |
| II.5 nothing public by side effect | PASS [SAFETY] | Only gallery visibility drives inclusion; upload confirm cannot (R-01). |
| II.6 never show empty | PASS | A camera with 0 visible photos has no document → 404 (FR-009). |
| III.1-III.10 | PASS | Only allowlisted camera fields are shown, through the existing `MetadataBar` on the reused `PhotoPage`. Make and model are used for grouping only. No new image path. |
| IV.1-IV.4 timing | PASS | The new reads are timed through `ContentReader`. The projection adds `db.cameras.load` and `db.cameras.write` inside `db.site.project`. SC-004 comes from logs (`evidence/p95.sh`). |
| IV.5-IV.8, IV.15-IV.17 | PASS | The same components (`ContactSheet`, `Picture`, `SoloViewer`) are used, so placeholders, dimensions, priority, view transitions and the thumbnail-first solo view come by reuse. Markup is asserted in page tests. |
| IV.9 single-read listing | PASS | `/c` makes 1 read of the denormalised cards. `/` still makes 0 reads. |
| IV.10 warm on publish or edit | PASS | P-CAM and P-CAMTEXT pages are purged, and the first-row images are warmed (contracts/edge.md E4). |
| IV.11-IV.14 predictive loading | PASS | The speculation rule adds `/c` and `/c/*`. Hover preload and neighbour prefetch come by reuse. Save-Data is handled by the existing module. |
| IV.18-IV.21 delivery | PASS | `HtmlPolicy`, the page tag, output cache, and the existing Cloudflare `port pages` rule covers `/c` (R-06). bfcache-safe through the same layout. |
| IV.22, V.3 | PASS | No new script. JS-off browser test. |
| V.1 traces to spec | PASS | |
| V.2 dependencies | PASS | None added. |
| V.4 proportional effort | PASS | Additions beyond the literal request, with benefit and cost: home link (findability, 1 line); camera photo view (owner chose 1A; 1 route plus an optional parameter); P-CAM purge (II.2 and IV.18 MUST); InvariantChecker rule (one function reused by 150+ tests; cheap [SAFETY] coverage). Dropped: a stale-slug redirect table (R-10; does not occur, costs a lookup structure). |
| VI CLI scaffolding | PASS (N/A) | No project or package. |
| VII.2 design record | PASS | New decision D-26 plus content-model, lifecycle and traceability rows, through a scripted fragment edit, then `assemble.sh` and `validate.sh` (R-11). A named task. |
| VIII cost | PASS | No metered addition. The collection is under 1 MB on Atlas free; Cloudflare purges stay within the existing per-request work. |
| IX.1-IX.4 | PASS | All three pages render in `PublicLayout`, with no page margins. The measured test gains three rows (test-impact C). |
| Workflow: existing tests unmodified | PASS | Only the 4 stated changes in test-impact (A). |

## Project Structure

### Documentation (this feature)

```text
specs/013-by-camera/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/  routes.md  components.md  edge.md  test-impact.md
├── decisions.md
├── checklists/requirements.md
└── evidence/   (tasks phase: p95.sh, verify.sh, non-vacuity.sh, design-record-edit.py, outputs)
```

### Source Code (repository root)

```text
src/Gallery.Web/
├── Data/Documents.cs            + CameraDoc (named CameraGroupDoc to avoid the existing CameraDoc EXIF type), CameraCardDoc
├── Data/ContentReader.cs        + CamerasAsync, CameraAsync
├── Data/Indexes.cs              + cameras indexes (slug unique, rank)
├── Library/SiteProjector.cs     + static Cameras(...), camera write, ProjectionResult.Cameras
├── Library/OperationResult.cs   + camera before/after tracking, P-CAM/P-CAMTEXT, warm shapes
├── Edge/PurgeSets.cs            + CameraSnapshot, Cameras, CameraText, paths, WarmFiles cameraCards
├── Site/PublicEndpoints.cs      + C1-C3
├── Site/Trails.cs               + CamerasLabel/Href, Cameras, UnderCameras
├── Site/ViewMapper.cs           + ToCameraCard
├── Site/Pages/CameraListingPage.razor, CameraPage.razor (new); PhotoPage.razor (+Crumbs); HomePage.razor (+link)
└── Components/Templates/PublicLayout.razor   speculation rule
src/Gallery.Core/Images/CameraKey.cs  (new, pure: Normalise, Key, DisplayName)
tests/Gallery.Core.Tests/CameraKeyTests.cs
tests/Gallery.Web.Tests/  CameraProjectionTests, CameraPurgeSetTests, CameraRoutesTests,
                          CameraPageComponentTests, CameraSourceGuardTests, CameraTimingEvidenceTests,
                          CameraBrowserTests (+ test-impact A/C edits)
```

**Structure Decision**: existing two-project layout. The pure key and name logic lives in
`Gallery.Core` beside `ExifFormat`, and everything else sits in the web project's existing folders.

**Naming**: `CameraDoc` already exists as the EXIF sub-document (`Documents.cs:24`). The new
collection's document type is `CameraGroupDoc`, and its card is `CameraCardDoc`. The collection
name stays `cameras`.

## Complexity Tracking

None. No MUST is crossed.

## Severable seams (owner terms)

- **Camera photo view** (`/c/{slug}/{iid}`): previous and next stay within the camera. If it is
  cut, tiles open the photo's gallery page instead, and visitors leave the camera after one
  click. Size: 1 route, 1 optional parameter, and the photo-page part of the purge set (about a
  third of the edge work).
- **Home link**: without it, `/c` is reachable only by typing its address. Size: 1 line plus 2
  stated test changes.
