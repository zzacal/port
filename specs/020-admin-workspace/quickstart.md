# Quickstart: 020 validation

## Prerequisites

- Docker running (Mongo and the S3 emulator test containers). Port 5080 free.
- Commands run from the repository root.
- The local stack with the development sign-in (feature 018) is the review target: the workspace
  cannot be opened on the live site without the owner signed in to the automation browser.

## Part A: captures, before any code change

```sh
dotnet test tests/Gallery.Web.Tests --artifacts-path artifacts \
  --filter "Category=Evidence&(FullyQualifiedName~LibraryTimingEvidenceTests|FullyQualifiedName~GalleryListTimingEvidenceTests|FullyQualifiedName~PhotoEditTimingEvidenceTests|FullyQualifiedName~MembershipTimingEvidenceTests|FullyQualifiedName~TileTimingEvidenceTests)"
bash specs/020-admin-workspace/evidence/capture-pre.sh
```

`capture-pre.sh` (a Setup task) copies those tests' logs to `evidence/pre-timings.log` and saves
the bytes of every public page type to `evidence/pre-public/`. Commit both before the first code
batch. Without them SC-005, SC-006 and SC-011 cannot be checked later.

## Gate (after every merged batch)

```sh
bash specs/020-admin-workspace/evidence/gate.sh <label>
```

Expected: build with `-warnaserror` has 0 warnings; Core and Web pass. The only test changes are
those in [contracts/test-impact.md](contracts/test-impact.md).

## Timing (SC-005, SC-006, SC-013)

```sh
dotnet test tests/Gallery.Web.Tests --artifacts-path artifacts \
  --filter "FullyQualifiedName~WorkspaceTimingEvidenceTests&Category=Evidence"
bash specs/020-admin-workspace/evidence/p95.sh \
  specs/020-admin-workspace/evidence/pre-timings.log \
  specs/020-admin-workspace/evidence/workspace-timings.log
```

Expected: each row of [contracts/log-events.md](contracts/log-events.md) "Success criteria" holds;
reads per response match research R-02.

## Public pages unchanged (SC-011)

```sh
bash specs/020-admin-workspace/evidence/compare-public.sh
```

Expected: every file under `evidence/pre-public/` equals the same page fetched now, 0 differences.

## Removal (SC-009)

```sh
dotnet test tests/Gallery.Web.Tests --artifacts-path artifacts \
  --filter "FullyQualifiedName~WorkspaceRemovalGuardTests"
bash specs/020-admin-workspace/evidence/non-vacuity.sh
```

`non-vacuity.sh` applies one deliberate break per [SAFETY] guard and expects the named test to go
red (list in tasks.md).

## Design record

```sh
uv run --no-project python specs/020-admin-workspace/evidence/design-record-edit.py
bash specs/001-architecture-stack-design/evidence/validate.sh
```

Expected: every old string found exactly once and replaced; validate reports 0 failures; D-27
exists and D-23 reads "superseded by D-27".

## Live walk (owner session, local, real browser at 1440 × 900)

Run the app against the development stack and sign in locally. In-process tests cannot see
request-body limits, so step 9 must be done against the live server.

1. `/admin` opens All photos: collections on the left, photos in the middle, details on the right.
   No control anywhere is an icon without a word.
2. Click a photo, change its title, click elsewhere: "Saved" by the field; the grid shows the new
   title; no page load (Network tab: one POST).
3. Shift-click a photo four places on: "5 photos selected". Tick a gallery: its count rises by the
   number that were missing; the result line says so, and says visitors can see them when the
   gallery is public.
4. Type a new name in "In galleries", Enter: the gallery appears on the left, private, with the
   selection; focus is still in the field.
5. Drag two photos onto a gallery name: added.
6. Open a gallery. Drag a photo to a new place; select one and press "Move later"; press "Use as
   gallery cover": the Cover mark moves. Check the public gallery page shows the same order and cover.
7. With nothing selected: edit the description (saved on leave), flip Public (applies at once),
   press "Move up": the public listing shows the new order.
8. In a PUBLIC gallery press "Add photos": the tray names the gallery and says visitors will see
   the photos, before any file is picked. Pick four photos, one of them not a JPEG: that one is
   refused by name. Press Hide, open All photos, come back: the upload carried on, the photos are at
   the end of the gallery marked New, and the public page shows them.
9. Pick 100 photos in one go (the batch limit) and reorder a gallery of 300 photos: no 413.
10. Reload mid-upload: the tray reopens; stopped photos say Interrupted with Retry and Remove.
11. Press Done: the tray closes and the New marks clear.
12. Double-click a photo: large view, "3 of 12", ← and → step, Escape returns to the grid at that
    photo. Reload on the large view: the same photo opens.
13. Request `/admin/library`, `/admin/galleries`, `/admin/upload`: each answers as any unknown
    admin address.
14. `/ui`: 35 entries; the new parts show every state; no removed part is listed.
15. At 390 × 844 with touch emulation: repeat 2, 3, 6 (with the Move buttons) and 8. Nothing
    scrolls sideways; the details panel closes with Done.

## After the merge

The production browser check of the workspace needs the owner signed in to the automation browser
on the live host (an OWNER step in tasks.md). The public-page part and the "old addresses answer
302 to sign-in when signed out" probe run without it.
