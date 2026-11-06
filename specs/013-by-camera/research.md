# Research: By Camera

Every finding below cites code read on `013-by-camera` at 499e177 (main 6b78b89 + the spec).

## R-01: Where the camera grouping lives

**Decision**: a new derived collection `cameras`, one document per camera that has at least one
visible photo, written ONLY by `SiteProjector.ProjectWithNavAsync` inside its existing
`db.site.project` timer, after `homeCards` and `nav` (data-model.md).

**Rationale**:
- Every owner write that can change visibility already ends in `OperationScope.CompleteAsync`,
  which calls the projector once (`Library/OperationResult.cs:215-217`). Image delete calls it
  mid-operation as well (`RefreshAsync`, `OperationResult.cs:162`). Hooking the projector reaches
  every path with no new call site: publish, unpublish, add, remove, reorder, cover, rename,
  gallery delete, image text edit and image delete (`GalleryService.cs` lines 134-537,
  `ImageService.cs:51`, `ImageDeleteService.cs:74,97`). `RemoveSeededCommand` goes through the
  same services.
- Upload confirm writes images with `galleryIds: []` (`Upload/ConfirmService.cs:276`): a new
  upload is in no gallery, so it can never change a camera. No confirm hook is needed (spec FR-011
  corrected to drop "upload confirm into a public gallery").
- The design's rule from 010 applies: put new public data on a document the page reads, written
  by the projector that already runs on every owner write, with one writer guarded by a source
  scan (as `nav` is, `NavSourceGuardTests`).
- Public reads then match the gallery pages exactly (R-04).

**Alternatives rejected**:
- Aggregate at request time (`images` grouped by camera, joined with listed galleries): the
  listing would cost 2+ reads and a scan of every image on every uncached request (IV.9 says
  listings are one read; FR-016 says no more than `/g`).
- Store cards on the `site` document beside `homeCards`: the listing would be one read, but the
  camera page needs each camera's full photo list, so the site document would carry every visible
  photo id. That would bloat the `/g` read, which projects only `homeCards` but still loads the
  document. A separate collection keeps `/g` byte-for-byte unchanged.
- Denormalise onto each image (`images.cameraKey`) and query: the camera page would still need
  visibility, which lives on galleries. That means two reads, and a stale flag could show a
  private photo.

## R-02: Grouping key, display name and address

**Decision**:
- **Key**: `Normalise(make) + "\u001f" + Normalise(model)`. Normalise trims, collapses inner runs of
  whitespace to one space and lower-cases invariantly; a missing or blank value becomes `""`. A
  photo whose make and model both normalise to `""` has no camera.
- **Display name**: `ExifFormat.Camera(new Exif(make, model, ...))` over the whitespace-collapsed
  (not lower-cased) values of the group's most recent photo. This is the formatter the photo page
  already uses (`Gallery.Core/Images/ExifFormat.cs:7`), so FR-005 holds by construction.
- **Address**: `/c/{slug}`, where slug is `Slug.From(display name)` (`Gallery.Core/Text/Slug.cs`).
  When two different keys produce the same slug, every key after the first (in key order) gets
  `-2`, `-3` and so on. Camera photo pages are at `/c/{slug}/{imageId}`.

**Rationale**:
- Camera names are public facts (the camera is shown on every photo page), so a readable slug
  leaks nothing that II.3 protects. II.3 is about gallery and image addresses being unguessable,
  and image ids stay opaque.
- FR-009 asks that a private-only camera be indistinguishable from an unknown one. A missing
  `cameras` document gives the same 404 page for both.
- Slug from the display name is stable while the key is stable (FR-007). The display text only
  changes if the newest photo's raw spelling differs ("Canon" vs "CANON"). That moves the slug
  only when the case-folded slug differs, which it never does, because `Slug.From` lower-cases.

**Alternatives rejected**:
- Opaque ids for cameras: they need a stored id per key, kept stable across projections, which
  means extra state. They buy nothing, because the camera name is already public.
- A hash of the key in the URL: unreadable, and still not secret.

## R-03: Order and cover

**Decision** (spec FR-006):
- Photos within a camera: `createdAt` descending, ties broken by `_id` ascending.
- Cameras: visible photo count descending, then display name with ordinal ignore-case
  comparison, then key.
- Cover: the first photo in the camera's order.

**Rationale**: everything is deterministic, so a projection that changes nothing writes nothing
(R-05). This is the same newest-first ordering the library uses (`Data/Indexes.cs`
`createdAt_-1__id_-1`).

## R-04: Read budget per page (FR-016, SC-004)

| Page | Reads today (mirror) | Reads planned |
|---|---|---|
| `/c` listing | `/g`: 1 (`db.site.read`) | 1: `db.cameras.list`, cards projection only |
| `/c/{slug}` | gallery page: 2 (`db.gallery.read`, `db.images.read`) | 2: `db.camera.read` (items), `db.images.read` grid projection |
| `/c/{slug}/{iid}` | photo page: 2 (`db.gallery.read` forPhoto, `db.images.read` full ×≤3) | 2: `db.camera.read` (slug, title, items), `db.images.read` full ×≤3 |

All of them go through `ContentReader` with `isDbRead: true`
(`Data/ContentReader.cs`), so `Server-Timing` and the `http.request` read count are emitted the
same way as on today's pages (IV.1-IV.3). The listing sorts by a stored `rank` field, so the read
needs no in-memory sort of documents. With tens of cameras it is one small find; an index on
`rank` is added by `Indexes.EnsureAsync`.

## R-05: Projection cost and write discipline

**Decision**: the projector computes the full camera set from data it can read in two queries:

1. Listed galleries: it already loads every gallery in `homeOrder`. Today it keeps them in memory
   to compute `Cards`, which gives the listed set (public and `imageCount >= 1`).
2. One `images` find for the union of listed galleries' `items`, projecting only `_id`, `title`,
   `width`, `height`, `placeholder`, `derivatives`, `camera.make`, `camera.model`, `createdAt` and
   `deletingAt`.

Then it diffs against one `cameras` find (all documents) and writes only what changed: an upsert
per changed camera and a delete per camera that left. Nothing is written when nothing changed.

Photos with `deletingAt` set are excluded. `ImageDeleteService` tombstones before it pulls the
image from galleries, and its mid-operation `RefreshAsync` must already drop the image.

**Cost at stated scale** (a personal gallery: about 1,000 photos, about 20 cameras):
- one extra `images` find of at most about 1,000 small projected documents, about 300 KB;
- one `cameras` find;
- zero to a few writes per owner action.

This runs only on owner writes, never on a visitor request. VIII: Atlas free tier, no new metered
cost. The `cameras` collection is about 1,000 ids × 30 B plus cards, well under 1 MB.

**Timing**: it stays inside `db.site.project`, which is already asserted by
`LibraryTimingEvidenceTests` and `SiteProjectorTests:67` (exactly one `db.site.project ok` per
projection). The camera work emits nested named timers `db.cameras.load` and
`db.cameras.write` (IV.2), which both appear in the evidence test's op list as added entries.

## R-06: Purge set for camera pages (FR-011, FR-020)

**Decision**: the projection returns a `CameraChange` set (before and after camera documents for
every key whose document changed, or that appeared or left). A new pure function
`PurgeSets.Cameras(before, after)` returns:

- `/c` when any card changed (`card` sub-document or `rank` differ, or a camera appeared or
  left). The card's caption and alt text are the camera name, as a gallery card uses the gallery
  title (`ViewMapper.ToCard`), so a cover photo's title is not on the listing;
- for each changed camera, its page `/c/{slug}` under BOTH the before and after slugs;
- each camera photo page whose content changed, using the same rule as `PurgeSets.Changed`
  (010 E1): ids in only one list, every id when the count changed, ids whose neighbours or index
  changed.

The camera document stores photo ids, not titles or captions, so a title or caption edit does
not change it. `OperationScope` already knows the text-changed image ids (`TouchText`). For each
one that is in an AFTER camera, `PurgeSets.CameraText(iid, after)` adds `/c/{slug}` (the tile
caption) and `/c/{slug}/{iid}` (the photo page). It never adds `/c`, because the card shows the
camera name.

`OperationScope` keeps the first projection's BEFORE camera set and the last projection's AFTER
set, and adds `PurgeSets.Cameras` to the union it already enqueues as ONE `EdgeWork`
(`OperationResult.cs:256`). Local eviction gets the same paths through `PageCacheEvictor`, using
the `page:{path}` tags that `StatusAndHeadersResult` already adds to every cached public response.

**Warm (IV.10)**: purged `/c` warms the first 3 card covers; a purged `/c/{slug}` warms its first
4 photos; a purged camera photo page warms the photo. This is the same rule as `WarmFiles`,
extended with the `/c` shapes.

**Edge rules**: the Cloudflare cache rule `port pages` (`infra/README.md` step 15) already makes
every `port.zacal.me` path cacheable with the origin's `s-maxage=86400`. The bypass rule only
matches `/admin`, sign-in, `/ui` and the owner cookie. So `/c` pages are edge-cached with the
bounded lifetime and bypassed for the owner with no dashboard change (IV.18). No owner step.

## R-07: Predictive loading (IV.11, IV.12, IV.13)

The speculation rule in `Components/Templates/PublicLayout.razor:19` matches only `/g` and `/g/*`.
It gains `/c` and `/c/*`:

`{"prerender":[{"where":{"or":[{"href_matches":"/g"},{"href_matches":"/g/*"},{"href_matches":"/c"},{"href_matches":"/c/*"}]},"eagerness":"moderate"}]}`

URL patterns do not match their bare root, so both forms are needed (011 lesson). Hover preload
(`data-preload-srcset` on tiles) and solo-view neighbour prefetch (`data-prefetch`) are component
behaviour, inherited by reuse. The arrow-key and Escape handler is selector-based
(`article.solo a[rel=prev]`, `.breadcrumb a[href*="#p-"]`). The camera crumb on a camera photo
page is `/c/{slug}#p-{iid}`, so Escape works with no script change.

**Test impact**: `HomePageTests.SpeculationRule` (const, line 20) is an exact-string pin. It
changes as a stated requirement change (contracts/test-impact.md). The 011 evidence script
`snapshot-diff.sh` pins the old rule too. It is frozen 011 evidence, not run by the suite, and is
left untouched.

## R-08: Home page link and its tests

`HomePage.razor` passes `Links = [Galleries]` to `Nav Home="true"`. Adding `By Camera` as a second
`NavItem` uses the existing stacked mode. The 011 catalogue already shows "Home, three links"
(`Ui/Pages/NavPage.razor:4`), so the stacked layout is a built state.

Tests that pin one link (stated changes in test-impact):
- `HomePageTests.Home_has_one_Galleries_link_in_nav_home_and_no_grid` (`Assert.Single(AnchorRx)`
  at line 113);
- `HomeBrowserTests.Home_link_is_centred_unscrolled_largest_and_tappable` (`count != 1`, line 65;
  it measures a single link's box).

The JS-off click test (`HomeBrowserTests` line 141) filters by text "Galleries" and stays valid.

## R-09: Visibility guarantees and their tests ([SAFETY] II.2, II.5, FR-008-FR-010)

- **Single writer**: a source guard (`CameraSourceGuardTests`, modelled on `NavSourceGuardTests`)
  fails on any `"cameras"` collection literal outside `Library/SiteProjector.cs`,
  `Data/ContentReader.cs`, `Data/Indexes.cs` and `Data/Documents.cs`. A non-vacuity fact checks
  that the projector does write.
- **Projection unit tests** (pure function `SiteProjector.Cameras(listedGalleries, images)`):
  - a photo only in a private gallery is excluded;
  - a photo only in an empty-but-public gallery cannot exist (empty means no photos);
  - a photo in no gallery is excluded;
  - a photo in a private and a public gallery is included once;
  - a tombstoned photo is excluded;
  - a photo without make and model is excluded;
  - case and space variants group together.
- **Route tests**:
  - an unknown slug, a private-only camera, and a known camera with a wrong image id or a
    private-only image id all give the same 404 body and headers as `NotFoundResult`;
  - pages carry no gallery link (FR-010): the camera page and camera photo page contain no
    `href="/g/` except the trail's `/g`, which they must not have either, because their trail is
    Home / By Camera.
- **Re-check at render**: the camera route renders only ids in the stored camera document, and
  the stored document holds only visible ids. The image read also filters `deletingAt` absent, as
  a render-time guard mirroring II.6's double filter on `/g`.

## R-10: Stale camera address (spec edge case)

`/c/{slug}` with no document → 404. There is no redirect table. A slug only changes when the key's
display name changes its slug, which R-02 shows does not happen for case or space variants. A
redirect layer would cost a second lookup structure for a case that does not occur (V.4).
Recorded in decisions.md. The spec edge case "redirects if the camera is known" is narrowed to
"a stale slug answers not found", via a spec edit.

## R-11: Design record (VII.2)

This adds a derived collection and three public routes. That is a new design decision, not a
deviation from an existing one. It is recorded as D-26 "Camera grouping" in
`specs/001-architecture-stack-design/draft/02c-decisions-content.md`. The `cameras` row goes into
the content model (`draft/04-content-model.md`). The L-07..L-17 lifecycle "Pages and caches"
lines gain "and P-CAM" in `draft/05-lifecycle.md`. The traceability rows for II.2/IV.9 gain the
camera mechanism. Then `evidence/assemble.sh` and `evidence/validate.sh` are run, with a scripted
edit (`evidence/design-record-edit.py`) that asserts the occurrence count of each old string.
