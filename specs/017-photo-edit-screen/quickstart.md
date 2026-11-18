# Quickstart: 017 validation

## Prerequisites

- Docker running (Mongo and MinIO test containers). Port 5080 free.
- Commands run from the repository root.

## Pre-change capture (before any code change)

```sh
dotnet test tests/Gallery.Web.Tests --artifacts-path artifacts \
  --filter "FullyQualifiedName~PhotoEditTimingEvidenceTests&Category=Evidence"
```

The Setup task adds a `pre` fact (100 loads of `/admin/library?page=1&open={id}` with a
100-image library) and runs it on the unchanged tree; it writes
`specs/017-photo-edit-screen/evidence/pre-timings.log`. Commit the log, then replace the `pre`
fact with the `post` fact (100 loads of `/admin/library/images/{id}?page=1`, writing
`photo-timings.log`) once the edit screen exists.

## Gate

```sh
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts
bash specs/017-photo-edit-screen/evidence/gate.sh final
```

Expected: 0 warnings; Core and Web pass. The only test changes are those in
[contracts/test-impact.md](contracts/test-impact.md).

## Timing (SC-005)

```sh
bash specs/017-photo-edit-screen/evidence/p95.sh \
  specs/017-photo-edit-screen/evidence/pre-timings.log \
  specs/017-photo-edit-screen/evidence/photo-timings.log
```

Expected: edit-screen p95 ≤ pre × 1.10 + 1 ms; every post line `db_reads` = 3, ≤ the pre value (4).

## Design record

```sh
uv run --no-project python specs/017-photo-edit-screen/evidence/design-record-edit.py
bash specs/001-architecture-stack-design/evidence/assemble.sh
bash specs/001-architecture-stack-design/evidence/validate.sh
```

Expected: the edit script reports each old string found exactly once and replaced; validate passes;
`grep -c 'toggle the side panel' docs/design.md` = 0.

## Hand walk (owner session, local)

1. Library page 2 at 1440 × 900: click a photo's pencil. A screen opens with only that photo,
   large and whole, and the Photo panel on the right. Breadcrumb: Library › {title}.
2. Change the title and Save. Same screen, "Saved"; the breadcrumb shows the new title.
3. Clear the title and Save: the reason shows, nothing saved, the typed values stay.
4. Click "Library" in the breadcrumb: page 2 opens at that photo's tile.
5. Click a photo's folder icon: the same edit screen opens.
6. Delete photo → confirm: back on library page 2 with "1 deleted".
7. At 390 px: the photo on top, full width, the form under it, no sideways scroll.
8. Open an old address `/admin/library?open={id}`: the plain library, no panel.
9. With JavaScript off, repeat steps 1–2.

## Production (after merge and deploy)

Owner step first: sign in to the automation browser on the production host. Then the agent runs
the browser pass on `/admin/library` → edit screen at 390 and 1440 (no horizontal scroll, 0 broken
images, photo wholly visible) and saves to `evidence/browser-prod.txt`. Meanwhile, signed out:
`curl -sI https://port.zacal.me/admin/library/images/{id}` answers 302 to sign-in.
