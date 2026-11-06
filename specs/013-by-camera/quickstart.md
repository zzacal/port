# Quickstart: By Camera (validation guide)

Prerequisites: Docker running (the Mongo and S3Proxy test containers), the repo's pinned SDK, and
Playwright browsers already installed by earlier features. Do not touch the owner's
`dotnet watch` (port 5080/5081). Build into `--artifacts-path artifacts`.

## 1. Build and test

```bash
dotnet build Gallery.slnx --artifacts-path artifacts -warnaserror
dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 5m
```

Expected: every test passes, with only the stated changes in contracts/test-impact.md (A).

Focused runs:

```bash
dotnet test Gallery.slnx --artifacts-path artifacts --filter "FullyQualifiedName~Camera"
dotnet test Gallery.slnx --artifacts-path artifacts --filter "FullyQualifiedName~LayoutConsistency"
```

## 2. Evidence scripts (`specs/013-by-camera/evidence/`, written in tasks)

- `p95.sh`: drives N requests each to `/g`, a gallery, a photo page, `/c`, `/c/{slug}` and
  `/c/{slug}/{iid}` on a local instance with output caching off. From the `http.request` lines
  alone it computes p95 duration and `db_reads` per route, and asserts camera ≤ mirror on both
  (SC-004). It is self-tested on synthetic plain, JSON, wrong-format and empty logs first.
- `verify.sh`: a read-only live walkthrough on a local instance (`--urls http://127.0.0.1:<port>`).
  - `/` has two home links, in order;
  - `/c` is 200 with K tiles;
  - `/c/` redirects 301 to `/c`;
  - a private-only camera and an unknown slug answer byte-identical 404s;
  - a camera page has no `href="/g/` (no gallery names);
  - the camera photo page's crumbs read Home / By Camera / {camera} / {photo}, and its prev/next
    stay inside `/c/{slug}/`;
  - there is no `<script src>` on any of the three.
- `non-vacuity.sh`: one deliberate break per [SAFETY] guard (tasks.md). Each break must turn its
  named test red, and the script restores the file afterwards.

## 3. Production (after merge and deploy; the agent runs it)

- `specs/012-production-infrastructure/evidence/verify-prod.sh` still ALL PASS.
- Browser pass on https://port.zacal.me at phone and desktop widths over `/c`, one `/c/{slug}`
  and one camera photo page:
  - no horizontal scroll and 0 broken images;
  - arrow keys step inside the camera, and Escape lands on the tile;
  - Navigation Timing (TTFB, load, CLS) over 3 loads each.

  Saved to `evidence/browser-prod.txt`.

No owner dashboard step: the existing `port pages` cache rule covers `/c` (research R-06).
