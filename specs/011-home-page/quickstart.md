# Quickstart: Home Page (011)

Run everything from the repo root. If the owner's `dotnet watch` is running on 5080/5081, leave
it alone and build into `artifacts/`.

## A. Build and test

```bash
dotnet build Gallery.slnx --artifacts-path artifacts -warnaserror
dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s
```

Expected: every test passes, including the new ones:
- `HomePageTests`: routes, reads, trails, guards
- `HomeBrowserTests`: centring at 320, 390, 768, 1280 and 2560, plus the no-JS walk
- `LayoutConsistencyBrowserTests` with listing and home

## B. Evidence scripts

```bash
bash specs/011-home-page/evidence/p95.sh specs/011-home-page/evidence/home-timings.log
bash specs/011-home-page/evidence/snapshot-diff.sh specs/011-home-page/evidence/pre-011-specimens
bash specs/011-home-page/evidence/verify.sh
bash specs/001-architecture-stack-design/evidence/validate.sh
```

Expected results:
- p95: `/` ALL PASS with `db_reads == 0`, `/g` with `== 1`.
- snapshot-diff: EQUAL.
- verify: ALL PASS.
- design record: valid after the VII.2 edits.

## C. Owner walkthrough (on your watch; press Ctrl+R first, because new routes don't hot-reload)

1. Open `https://localhost:5081/`. You should see only the breadcrumb "Home" and a large
   "Galleries" link in the middle of the page, with no scrollbar. Repeat at phone width.
2. Click "Galleries". `/g` shows the grid you had at `/`, and the breadcrumb reads Home / Galleries.
3. Open a gallery. The breadcrumb reads Home / Galleries / {title}. Click "Galleries" to go back to `/g`.
4. Open a photo. The breadcrumb reads Home / Galleries / {gallery} / {photo}. Escape lands on the
   photo's tile.
5. Open `/g/` (with the slash). It redirects to `/g`.
6. Real Chrome: on `/`, hover "Galleries", then check DevTools > Application > Speculative loads.
   `/g` should show as prerendered (R-04, OWNER-ONLY).
7. Admin: "View site ↗" opens `/`, the home page.
