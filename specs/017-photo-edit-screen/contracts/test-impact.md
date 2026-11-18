# Test impact: Focused Photo Edit Screen

Grep run 2026-11-17 over `tests/` (source only) for `open=`, `lib-edit`, `lib-del-one`, `lib-gf`,
`side-panel`, `admin-aside`, `with-aside`, `Close the editor`, `LibraryOpen`, `LibraryPageModel`,
`LibraryPageData`, `/admin/library/images/`. Classes: **A** retarget/rewrite (stated change),
**B** scan or route list: add the new route and keep the old, **C** unchanged (listed so nobody
"fixes" it), **D** deleted (behaviour removed).

Every test not listed here MUST pass unmodified. A failure outside this table means the code is
wrong, not the test. Re-run this grep before the first implement batch; a new hit becomes the next
row with "(found at implement)".

## A — redirect location (string change only)

`/admin/library?page=1&open={X}&saved=1` → `/admin/library/images/{X}?page=1&saved=1`

| # | File:line |
|---|---|
| A1 | LibraryEditTests.cs:55 |
| A2 | LibraryMembershipEditTests.cs:95 |
| A3 | LibraryMembershipEditTests.cs:339 |
| A4 | EdgeModeTests.cs:114 |
| A5 | MembershipTimingEvidenceTests.cs:56 |
| A6 | LibraryTimingEvidenceTests.cs:96 |

A1 then GETs the new location; its assertions (title/caption values, `sp-status` Saved, status
line "Saved") hold on the edit screen unchanged.

## A — panel reads retargeted to the edit screen

`/admin/library?open={X}` (or `?page=1&open={X}`) → `/admin/library/images/{X}`. Selectors are
unchanged because P1 keeps `side-panel`, `lib-edit`, `lib-gf`, `lib-del-one` and the status line.

| # | File:line(s) |
|---|---|
| A7 | LibraryEditTests.cs:183, 190, 192 |
| A8 | LibraryPageTests.cs:267 (Limits: drop the `selection-bar` and `newTitle` assertions' source — they read `html`; change them to read `GetOkAsync(h, "/admin/library")` so the library limits stay covered) |
| A9 | GalleryFieldBrowserTests.cs:183, 225, 254, 292, 313, 350, 387, 419, 437, 465, 478, 497, 537, 565, 591, 623 |
| A10 | LibraryBrowserTests.cs:662 (retarget only) and :517-521 (rewrite: the "side panel open with a selection" step becomes "edit screen": open `/admin/library/images/{ids[0]}`, drop the `Marks(panel).Nth(1)` click and the `Bar(panel)` visibility line (the screen has no grid), keep `["Save"]` as the one filled button and `AssertDangerOutlinedAsync(panel, "photo edit screen")`) |

## A — rewritten for the new behaviour

| # | File:line | Old | New |
|---|---|---|---|
| A11 | LibraryPageTests.cs:223 | edit href `?page=1&open={id}#t-{id}` | `/admin/library/images/{id}?page=1` |
| A12 | LibraryMembershipTests.cs:126 | group href `?page=1&open={iid}#t-{iid}` | `/admin/library/images/{iid}?page=1` |
| A13 | LibraryPageTests.cs:240-250 (One_primary…) | `?open` page: Save is the one primary, Add Secondary, Delete photo outlined | `?open` part split: on `/admin/library?open={id}` Add is still the one primary (old param ignored); the Save/Delete photo assertions move to `/admin/library/images/{id}` (Save the one primary; danger trigger and `form="lib-del-one"` outlined) |
| A14 | LibraryPageTests.cs:292 (No_validation_nags) | paths `/admin/library`, `?open=` | `/admin/library`, `/admin/library/images/{id}` |
| A15 | PhotoActionsTests.cs:241-262 (Open_photo_is_the_one_active_tile…) | open tile active, its icons close | rename `Old_open_param_marks_no_tile_and_every_icon_opens_the_edit_screen`: on `?open={One}` no tile has `aria-current`; every edit and group href is `/admin/library/images/{iid}?page=1` |
| A16 | TileIconBrowserTests.cs `ToggleStepsAsync` (~343-366) and its two callers (SC003 with JS at widths, without JS) | pencil opens A's panel, folder opens B's, pencil closes | `OpenSteps`: A's pencil navigates to `/admin/library/images/{a}?page=1`, the page shows `#lib-edit` and one photo; back to the library; B's folder navigates to B's edit screen. `checkViewport` (JS run) asserts B's `#lib-edit .combobox` is in the viewport at the test's width (it is high on the page). |
| A17 | TileIconBrowserTests.cs FR010 (~430-440) | `?open`: only Save filled, A active | library at rest: none filled; `/admin/library/images/{id}`: only Save filled |
| A18 | LibraryAccessTests.cs:24-46 | 15 routes | add `["L6"] = ("GET", "/admin/library/images/{iid}")` after L5; count 16; key order L1–L6, G1–G4, B1–B6; rename the fact `…_16_routes…` |

## B — scan lists: add, keep the old

| # | File:line | Add |
|---|---|---|
| B1 | EdgeSecretTests.cs:84 | `("GET /admin/library/images/{iid} (L6)", $"/admin/library/images/{a.Id}?page=1")` after the `?open=` row (kept: still a page) |
| B1 (found at implement) | EdgeSecretTests.cs:128 | `Assert.Equal(14, scanned.Count)` → 15 (the scan list gained the L6 row) |
| B2 | LayoutConsistencyBrowserTests.cs:89 | `("photo", $"/admin/library/images/{ids[0]}")` (IX.4) |

## C — unchanged (pass as is)

| File:line | Why |
|---|---|
| LibraryEditTests.cs:200-207 Unknown_open_id_leaves_the_panel_closed | still true: `?open` renders no panel at all |
| LibraryEditTests.cs refusal theory (:92), LibraryMembershipEditTests.cs:171-256 | POST refusals now re-render the edit screen; `Aside()` regex, `LibraryHtml.Status` and status codes hold |
| PhotoActionsTests.cs:143-157 | component facts on the `Active` option (catalogue state kept) |
| IconTests.cs:123-127 | IconButton component with a literal href |
| MoleculeComponentTests.cs:175-259 | DangerConfirm specimen with literal `lib-del-one` |
| OrganismStatePageTests.cs:80 | `/ui/` AdminLayout specimen still has an aside |
| BuilderTests.cs, PublishRefusalTests.cs:226, LibraryBrowserTests.cs:434, 532 | the builder's own panel |
| LibraryDeleteTests.cs:49, 69 | L5 unchanged |
| GalleryFieldBrowserTests.cs:22-23, 81-83, 143, 186, 205, 333, 339, 625, 645 | selectors kept by P1 |

## D — deleted

| # | File:line | Why |
|---|---|---|
| D1 | GridWidthBrowserTests.cs: `Grid.LibraryOpen` (enum member :22, the `or Grid.LibraryOpen` arms :28, :40, rows :53-54) and the case at :142 | the panel beside the library grid no longer exists; the container query it tested is removed (P4) |
| D2 | TileIconBrowserTests.cs `At_1440_the_sticky_panel_is_in_view_beside_the_40th_tile`, `At_390_with_js_the_stacked_panels_galleries_field_is_brought_into_view` | sticky panel beside a grid and the J1 scroll are removed (J1) |

## New tests (summary; tasks.md details)

- `PhotoEditPageTests` (HTTP): L6 200 markup (one photo, no tiles, breadcrumb href, form action,
  page field, `sizes`), 404 bad/unknown/tombstoned id, HEAD, `saved=1`, reads = 3, owner-only.
- `SizesTests` addition: `ForAside()` exact string.
- `PhotoEditBrowserTests`: SC-002 geometry (1440 × 900 three shapes; 390 × 844), SC-004 back to
  page N at the tile after a save, no horizontal scroll at 390, delete lands on page N, works
  with JS off (save).
- `PhotoEditTimingEvidenceTests` (Category=Evidence) + `evidence/p95.sh`.
