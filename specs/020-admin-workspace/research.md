# Research: 020 Admin Workspace

Facts below were read from the code on `020-admin-workspace` (cc7df88 + spec commits) unless a row
says "not probed". Probe scripts: `evidence/probe-plan.sh` (code reading), `evidence/test-impact-hits.sh`.

## R-01 One document, fragment swaps

**Decision**: `/admin`, `/admin/unplaced` and `/admin/g/{gid}` each answer a full server-rendered
document. The same addresses answer a fragment when the request carries `X-Workspace: main`,
`chrome` or `more`. The page script switches collection by fetching the `main` fragment, replacing
the middle region and calling `history.pushState`; it never reloads the document.

**Rationale**: FR-033 requires an upload to keep running while the owner opens another collection.
A document navigation tears down the worker and the in-flight PUTs (the item would show
"interrupted"). Rendering stays on the server, so the `Picture` atom remains the only place that
writes `srcset`/`sizes`, and no markup is built from strings in script.

**Alternatives**: full navigation per collection (simplest, breaks FR-033); a JSON API with
client-side rendering (duplicates `Picture` and `SrcSet` in script, about twice the script);
vendoring htmx for the swap (a package for one 20-line function; V.2).

**How the swap is done**: `new DOMParser().parseFromString(text, 'text/html')`, then
`target.replaceChildren(...doc.body.children)`. The four banned sinks (`innerHTML`, `outerHTML`,
`insertAdjacentHTML`, `document.write`; `LibrarySourceGuardTests.cs:116`, `UploadSourceGuardTests.cs:80`)
stay banned for every admin script. A new guard pins `DOMParser` to one function in `workspace.js`.
Not probed in a browser: that Razor's scoped-CSS attributes survive the parse (they are plain
attributes on server markup, so they should). Pinning test: a browser fact asserts a computed style
of a swapped-in tile. Escalation rule: if swapped markup is unstyled, escalate; do not move styles
into script.

## R-02 Reads per response

| Response | Reads (timing ops) | Today's nearest |
|---|---|---|
| W1 full (All photos) | `db.library.count`, `db.library.page`, `db.galleries.list`, `db.site.order` = 4 | library page: the same 4 |
| W2 full (Not in a gallery) | the same 4 (page read filtered) | none |
| W3 full (gallery) | `db.gallery.load`, `db.library.page`, `db.galleries.list`, `db.site.order`, `db.library.count` = 5 | builder: 2; library: 4 |
| `main` fragment, library | `db.library.count`, `db.library.page` = 2 | |
| `main` fragment, gallery | `db.gallery.load`, `db.library.page` = 2 | builder: 2 |
| `chrome` fragment | `db.galleries.list`, `db.site.order`, `db.library.count` = 3 | |
| `more` fragment | `db.library.page` = 1 | |
| W4 one tile | `db.library.page` = 1 | |

`db.library.count` becomes ONE aggregate that returns both counts (all live photos; live photos
with an empty `galleryIds`), keeping the op name so pinned op lists stay valid. The galleries list
no longer reads covers (the third read of today's galleries page disappears).

**SC-005 risk**: W3 full is one read above the library page it is compared with. Tiles are lighter
than today's (no per-tile icon components: 015 measured those at +6 ms p95 on 100 tiles), so the
plan expects W3 to pass. Fallback if the evidence test fails: widen the `db.galleries.list`
projection with `description`, `pinnedCover` and `items` and drop `db.gallery.load` on full loads
(4 reads). Not measured at plan time; the pre-change capture (quickstart Part A) is the baseline.

## R-03 Paging (FR-006)

**Decision**: keyset paging on the existing `library_page` index order `{createdAt:-1, _id:-1}`:
`?before={createdAtTicks}.{id}`, `Limit(PageSize + 1)` to know whether more exist. Chunk size is
`Library:PageSize` (100). Loaded by an `IntersectionObserver` on a sentinel after the grid, plus a
visible "Show more" button for keyboards.

**Rationale**: uploads insert at the top while the owner scrolls; skip/limit pages would repeat
tiles. Not in a gallery filters `galleryIds` empty on the same sort; at the design's 5,000 photos a
filtered walk of that index needs no new index (V.4).

Galleries render every member at once (as the builder does today); reorder needs them all.

## R-04 Selecting shows details with no request (SC-004)

Each tile's wrapper carries `data-id`, `data-title`, `data-caption`, `data-galleries` (ids,
space-separated). The details panel is a server-rendered `<template>` per context (library, gallery,
photo, selection) that the script clones and fills through `value`/`textContent`/`checked`. The
"View on site" address is composed in script from the first public gallery row in the collections
list that holds the photo (`/g/{id}/{slug}/{photoId}`, the rule `LibraryReader.Tile` uses today),
so a tile needs no galleries read.

## R-05 Write routes

**Decision**: new JSON routes under `/admin/api/` (contracts/http.md A1 to A11), each a thin call to
an existing service method. Every old form route (L1 to L6, G1 to G4, B1 to B6) is unmapped, so it
falls to the existing `/admin/{**rest}` 404 catch-all (FR-004).

| Route | Service call (existing unless marked) |
|---|---|
| A1 photo text | `ImageService.UpdateTextAsync` |
| A2 delete photos | `ImageDeleteService.DeleteManyAsync` |
| A3 create gallery (+ ids) | `GalleryService.CreateAsync`, or `ImageService.BulkAddAsync(ids, null, title)` when ids are sent |
| A4 gallery order | `GalleryService.ReorderHomeAsync` |
| A5 gallery text | `GalleryService.UpdateTextAsync` |
| A6 visibility | `PublishAsync` / `UnpublishAsync`, with the G4 handler's branch order kept (empty-private refusal first) |
| A7 add photos | `ImageService.BulkAddAsync(ids, gid, null)` |
| A8 remove photos | NEW `GalleryService.RemoveManyAsync(gid, ids)`: one scope, loops the existing `RemoveOneAsync` |
| A9 photo order | `GalleryService.ReorderAsync` |
| A10 cover | `GalleryService.SetCoverAsync` |
| A11 delete gallery | `GalleryService.DeleteAsync` |

**Rationale**: FR-027 (same rules, refusals, purges). Purge sets are whatever those calls produce;
no contract predicts a set. Keeping the old paths with a new body format was rejected: an old form
post would answer 400 instead of "unknown address", and test-impact rows would be ambiguous.

CSRF: the routes sit in the protected group with `AdminAntiforgeryFilter`; the script sends
`X-CSRF-TOKEN` (`AuthSetup.cs:123`), as the upload API does today.

## R-06 Per-photo joining (clarification 2)

Today: U7 validates and leaves the item `succeeded` in staging; U10 confirm copies every item to
the public and private stores, upserts the images and closes the batch (`ConfirmService`).

**Decision**: U7 is unchanged. A new call, U12 `join`, does for ONE item what confirm did per item
(`ConfirmService.ConfirmItemAsync`, moved to `JoinService` with its body kept; **AMENDED (tasks) C-02**: no `JoinService`, the method is `ConfirmService.JoinAsync`), then adds the photo
to the destination gallery through `GalleryService.AddAsync`. The page calls U12 right after U7
succeeds, and for every `succeeded`/`joining` item it finds on load (resume). U10 confirm and U11
discard are removed; U13 `clear` drops what did not join.

**Why join is its own call**: U3 to U9 keep their exact request and response shapes, so the 22
facts of `UploadFlowTests` (the GPS, width, missing-size and placeholder rejections of SC-012 among
them) run unmodified. Folding join into U7 would change the happy-path answer those facts pin.

**Safety of running joins without the batch lock** (the lock `confirmStartedAt` goes away):

- A join first claims the item: `state: succeeded|joining → joining` for the current attempt
  (atomic update). U9 delete and U8 restart answer 409 `not-active` on a `joining` item, so the
  owner cannot remove an item while its files are being published.
- `joining` never ages into `interrupted` (`EffectiveState`), so Restart is never offered for an
  item whose public files may exist. A stuck `joining` item is finished by calling U12 again.
- Copies stay copy-if-match on the ETag recorded at validation (III.6). A refused copy fails that
  item "changed after validation" and removes the public names this run created, as today.
- A missing staged file is accepted when its destination already exists (today: only on a resumed
  confirm). This is safe because public names are the content hash checked at validation and the
  master name is the item id; it is what makes two overlapping joins of one item converge.
- The image upsert stays `$setOnInsert`, the gallery add reports "already a member": a repeated
  join changes nothing (FR-034).

**Destination** (FR-031, constitution II.5a to 5d): the open batch gains `destination` (a gallery
id or absent). U3 sets it when the upload set is empty; while the set holds items or added photos
the stored destination wins and U3 says which one applied. When the destination gallery is public,
U3 refuses (409 `destination-public`) unless the request carries `visibleToVisitors: true`. That
makes II.5a checkable on the server: the page can only send the flag from the state in which the
notice is on screen. At join time the gallery is read again: gone → the photo joins the library
only; visibility is whatever it is then.

**Edge work**: each join into a gallery is one `AddAsync`, so one projection and one `EdgeWork`
(`EdgeQueue`, a bounded channel). 100 photos into a public gallery is 100 queued purges. They are
not grouped (V.4): grouping needs a new timer and a second write path. If the edge provider
rate-limits, the bounded HTML lifetime heals it (IV.18). Not probed against Cloudflare's limit.

**New marks** (FR-035): the batch keeps `added` (image ids joined in this upload set), returned by
U2, cleared by U13 and when a new set starts. Marks therefore survive a reload.

## R-07 Reordering without a mouse (FR-023)

**Decision**: mouse and pen drag use the HTML drag-and-drop events (as the preview does). For touch
and keyboard the details panel offers worded buttons: "Move earlier" / "Move later" for one
selected photo in a gallery, "Move up" / "Move down" in a gallery's settings for its place in the
list. Both send the same full-order request (A9, A4) as a drag.

**Rationale**: today's keyboard and touch reorder hang off a drag-handle icon, which this feature
removes. Whole-tile touch drag conflicts with scrolling and needs long-press handling; two buttons
are robust, discoverable and match "every action is a word". `reorder.js` is deleted.

## R-08 Large view

Client-side: the script clones the tile's `<picture>` into a stage element and sets `sizes` to the
stage's width, so the browser picks a larger file from the same `srcset`. Address `?photo={id}` by
`pushState`. On a direct load with `?photo=` the script opens it after render; if the photo is not
in the first chunk it loads chunks until it is (worst case 50 requests at 5,000 photos, only on a
reload of a deep photo; V.4).

## R-09 Scripts

- `workspace.js` (new): navigation, selection, panel, writes, results, drag, large view.
- `upload.js` (reworked): the pipeline core (register, PUT, worker, heartbeats, restart) is kept;
  the footer/confirm/discard code and the tile templates are replaced by the tray. Loaded with a
  dynamic `import()` of its versioned URL the first time the tray opens or an upload set exists.
- `upload-worker.js`: unchanged.
- `library.js`, `reorder.js`: deleted.

The page has one `<script type="module">` (workspace.js). Custom elements `<admin-workspace>` and
`<upload-tray>` wrap the regions (Platform Constraints: web components for admin interactivity).

## R-10 Components reused, added, removed

Reuse first (an orphan with the right shape beats a new molecule):

- `SidePanel` gains optional `Save` (default true: byte-identical). The workspace passes false.
- `ContactSheet` gains optional `Fill` (auto-fill columns of at least 168 px; default false).
- `Tile`, `Picture`, `SelectMark`, `Button`, `Input` (its `SaveState` already draws saving/saved),
  `Switch`, `DangerConfirm`, `PageHeading`, `UploadDropzone`: used as they are.
- `AdminLayout` is rewritten in place (it stays the one admin layout, IX.2; the sign-in page keeps
  using its bare mode).

`VisibilityToggle` is NOT reused: its hint and warning are written for a Save form ("When you
save…"); the workspace uses the `Switch` atom directly.

New (9): `Mark` (atom, a worded badge), `PhotoCell`, `GalleryChecklist`, `UploadRow`, `Toast`
(molecules), `CollectionList`, `UploadTray`, `PhotoStage` (organisms), `WorkspacePage` (page, no
catalogue entry).

Removed (13 catalogue entries): `OwnerBar`, `SelectionBar`, `PhotoActions`, `GalleryActions`,
`GalleryBuilder`, `BatchFooter`, `BatchItem`, `DragHandle`, `CoverMarker`, `TokenCombobox`,
`VisibilityToggle`, `Icon`, `IconButton` (with `Icons.cs`). Catalogue: 40 − 13 + 8 = 35 entries.

No new design token: the preview's sizes map to existing tokens (title at `--type-brand`).

## R-11 Catalogue snapshots

`UiPagesUnchangedTests` compares every catalogue entry byte-for-byte with
`specs/006-public-site-local/evidence/ui-baseline/{slug}.html`. It writes a missing baseline and
fails once. This feature therefore: adds 8 baseline files (generated, reviewed, committed), deletes
13, and regenerates 4 whose pages change (`index`, `templates/admin-layout`,
`organisms/side-panel`, `organisms/contact-sheet`), each with a diff against the frozen copy that
shows only the intended change. `PublicOutputUnchangedTests` (005 baseline) is append-tolerant and
must pass unmodified: `ContactSheetPage` only gains a specimen after the existing ones. **AMENDED (tasks) C-04**: it also loses specimens 4–6 at removal (they render removed components); the PARENT trims the 005 baseline (TI-57) and `PublicOutputUnchangedTests` then passes unmodified.

## R-12 Existing tests

`evidence/test-impact-hits.txt` lists every test line that names an old admin address (generated
by `evidence/test-impact-hits.sh`). Classes and the per-file table are in
`contracts/test-impact.md`. Summary: 1,105 facts in `Gallery.Web.Tests`; about 60 files change.

The guard `UploadSourceGuardTests.cs:89` bans the words `Purge` and `Warm` in `Upload/` (008
FR-033: an upload refreshes no public page). `JoinService` (**AMENDED (tasks) C-02**: `ConfirmService.JoinAsync`) calls `GalleryService.AddAsync` and
names neither word, so the guard keeps passing; its meaning is restated by two new [SAFETY] facts
(no destination → no edge work; join never changes visibility).

## R-13 Evidence

- Before any code change: run the existing timing evidence tests (library, gallery list, photo
  edit, membership, tile) and keep their logs as `evidence/pre-timings.log`; capture every public
  page's bytes as `evidence/pre-public/` (SC-005, SC-006, SC-011).
- After: `WorkspaceTimingEvidenceTests` (Category=Evidence) writes `evidence/workspace-timings.log`;
  `evidence/p95.sh` (copied from 017, with its self-test) compares.
- SC-013: `upload.join` durations from the same log.
- SC-004: a browser fact measures click to filled panel with `performance.now()`.

## R-14 Design record (VII.2)

One scripted edit (`evidence/design-record-edit.py`, asserting each old string's count) in the
001 draft fragments, then `assemble.sh` and `validate.sh`:

- D-23 marked superseded; new D-27 "Admin workspace" (choice, rationale naming the owner's two
  preview rounds, alternatives including D-23's direction and the rejected first preview, pivot cost).
- D-21 component list; D-11 rewritten for per-photo joining; lifecycle entries L-01 to L-06; E-08;
  the upload diagram; the traceability row for II.5 (mechanism: destination + notice + join).
- `docs/architecture.html` is checked for admin page names and edited if it carries any.

Constitution IX.1's example "owner bar" is reworded by a PATCH (6.3.1) when the bar is deleted.

## R-15 Build order (each step leaves the suite green and the admin usable)

1. Captures (R-13). 2. API routes and `RemoveManyAsync` beside the old pages. 3. Components and
catalogue pages. 4. Workspace page and `workspace.js` at `/admin`, old pages still reachable.
5. Join pipeline, tray, and removal of the upload page in one step (the old page cannot work
without confirm). 6. Removal of the old pages, routes, scripts and components, with the guards.
7. Phone layout and large view polish. 8. Design record, annotations, evidence, live walkthrough.

Severable at 7: the phone story and the large view (spec Assumptions).
