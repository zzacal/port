# Decisions: 017 Focused Photo Edit Screen

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | DEFAULT | The library tile's gallery (folder) icon opens the same edit screen as the pencil (FR-002). | Spec default; the panel had the Galleries field, so both icons already led to the same place. | Low: one href in LibraryPage.RowFor. |
| 2 | DEFAULT | After Save the owner stays on the edit screen with "Saved" (FR-005, L2 303 to /admin/library/images/{id}?page=N&saved=1). | Spec default; matches the builder (B2 lands on the builder). | Low: one redirect string; A1-A6 assertions move back. |
| 3 | DEFAULT | The library side panel is removed, not kept as a second way to edit; old ?open= addresses render the plain library, with no redirect (FR-009). | Spec default; owner prefers no compatibility scaffolding. | Medium: the panel code and ~40 test references would come back. |
| 4 | PLAN-CORRECTION | SC-002 at 390 px: "spans the content width (or is height-bound) and wholly visible" instead of 3x the tile's area. | A one-column tile at 390 px is already content-wide, so 3x is impossible there. | None. |
| 5 | PLAN-CORRECTION | FR-007's fallback is written from the handler: a page past the end shows the last page (the library reader clamps). | The spec's first wording guessed "first page". | None. |
| 6 | EXECUTOR | Batch A: the page projection is one shared `TileFields` field and the tile builder one private static `Tile(...)`, called by PageAsync and PhotoAsync. | Call, never copy (R-04). | None. |
| 7 | EXECUTOR | Batch A: PhotoReaderTests compares tiles with a helper that checks the list members (Variants, Galleries) as sequences, then record equality on the rest. | LibraryTile's list members compare by reference under record Equals. | None. |
| 8 | EXECUTOR | Batch B: L6 reads only `saved=1`; other status keys (added, deleted) never put a status line on the edit screen. | contracts/http.md L6. | None. |
| 9 | EXECUTOR | Batch B: if the photo vanishes between a refused save and its re-render, the edit screen answers 404 (PhotoAsync null). L2's own photo-not-found branch still re-renders the library, unchanged. | R-05 keeps the existing not-found branch; the race is a one-owner rarity. | Low. |
| 10 | EXECUTOR | Batch B: PhotoEditPage.TitlePattern aliases LibraryPage.TitlePattern, which stays on LibraryPage for the picker's newTitle input. | One regex, not two copies. | None. |
| 11 | EXECUTOR | Batch B: PhotoEditPageTests reads db_reads from the `http.request GET /admin/library/images/{iid} 200` line: the middleware logs the route TEMPLATE. Measured 3. | Observed log format. | None. |
| 12 | EXECUTOR | Batch B: PhotoEditBrowserTests measures `.pe-stage .frame`; shapes come from stored Width/Height overrides; SC-004 uses `Library:PageSize = 4` via extraOverrides. The frame follows stored dimensions, so no escalation. | tasks.md T016. | None. |
| 13 | PARENT-FIX | test-impact B1 gains a second row: EdgeSecretTests `Assert.Equal(14, scanned.Count)` -> 15 (found at implement by the Batch B executor). | The scan list gained the L6 row; the planned row missed the count pinned below it. | None. |
| 14 | PARENT-FIX | p95.sh / p95-selftest.sh / tasks.md T004 match the post route as the logged template `/admin/library/images/{iid}`, not a concrete 26-char id (found at implement by the Batch B executor). | RequestTimingMiddleware logs RoutePattern.RawText; the first p95 script would have counted 0 post lines. | None. |
| 15 | PARENT-FIX | library.js: removed the trailing blank line J1's deletion left at the end of the file. | Diff hygiene. | None. |
| 16 | DEFAULT | T022 [SAFETY] L6 break is `.AllowAnonymous()` on the L6 line rather than "mapped on app": MapLibraryEndpoints only has the admin group in scope. | The same break: the route stays mapped but answers without a session. | None. |
| 17 | STATED-CHANGE | A1-A6 L2 redirect location `/admin/library?page=1&open={X}&saved=1` -> `/admin/library/images/{X}?page=1&saved=1` (LibraryEditTests, LibraryMembershipEditTests x2, EdgeModeTests, MembershipTimingEvidenceTests, LibraryTimingEvidenceTests) | FR-005, contracts/http.md L2 | low |
| 18 | STATED-CHANGE | A7 LibraryEditTests panel reads retargeted `/admin/library?open={id}` -> `/admin/library/images/{id}` (3 sites); selectors unchanged | P1 keeps the panel's ids | low |
| 19 | STATED-CHANGE | A8 LibraryPageTests.Limits: panel limits read on the edit screen; selection-bar and newTitle limits read on `/admin/library` | Those controls stay on the library | low |
| 20 | STATED-CHANGE | A9 GalleryFieldBrowserTests: 16 OpenAsync calls retargeted to `/admin/library/images/{id}` | P1 keeps lib-gf/lib-edit | low |
| 21 | STATED-CHANGE | A10 LibraryBrowserTests: "side panel open with a selection" step becomes the edit screen (no grid, no selection click/bar line; Save the one filled; danger outlined); single-photo delete test retargeted | No grid on the edit screen | low |
| 22 | STATED-CHANGE | A11/A12 tile edit and group hrefs `?page=1&open={id}#t-{id}` -> `/admin/library/images/{id}?page=1` (LibraryPageTests, LibraryMembershipTests) | FR-001/FR-002 | low |
| 23 | STATED-CHANGE | A13 LibraryPageTests.One_primary: `?open` page keeps Add as the one primary; Save/Delete photo assertions move to the edit screen | ?open no longer read; Add never demoted | low |
| 24 | STATED-CHANGE | A14 LibraryPageTests.No_validation_nags: paths `/admin/library`, `/admin/library/images/{id}` | Panel moved | low |
| 25 | STATED-CHANGE | A15 PhotoActionsTests: fact renamed Old_open_param_marks_no_tile_and_every_icon_opens_the_edit_screen; no aria-current; every href the edit screen | FR-009 | low |
| 26 | STATED-CHANGE | A16 TileIconBrowserTests: ToggleStepsAsync -> OpenStepsAsync (pencil opens A's screen, back to library with nothing current, folder opens B's screen with its combobox in view); the two callers renamed | Nothing toggles any more | low |
| 27 | STATED-CHANGE | A17 TileIconBrowserTests.FR010 renamed ..._only_Save_on_the_edit_screen: library at rest none filled and no tile current; edit screen only Save filled | FR-009 | low |
| 28 | STATED-CHANGE | A18 LibraryAccessTests: L6 GET added after L5; 16 routes; fact renamed The_route_table_is_the_16_routes_of_the_contract | FR-011 owner-only | low |
| 29 | STATED-CHANGE | B1 EdgeSecretTests: L6 page added to the secret scan (old ?open row kept, still a page); scanned count 14 -> 15 | III leak nothing on every owner page | low |
| 30 | STATED-CHANGE | B2 AdminLayoutConsistencyBrowserTests: photo edit screen added to the measured page list | IX.4 | low |
| 31 | STATED-CHANGE | D1 GridWidthBrowserTests: Grid.LibraryOpen, its table rows and the SC007 panel-open fact deleted | P4 removed the container query; no panel beside the library grid | low |
| 32 | STATED-CHANGE | D2 TileIconBrowserTests: sticky-panel-beside-40th-tile and J1 scroll facts deleted, with the now-unused OpenParam helper | J1 and the library panel removed | low |
