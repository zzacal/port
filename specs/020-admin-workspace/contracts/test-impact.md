# Test Impact: 020 Admin Workspace

"Existing tests pass unmodified unless a requirement change is stated" (constitution, Development
Workflow). This file is the statement. Every test line that names an old admin address is listed in
`evidence/test-impact-hits.txt` (regenerate with `evidence/test-impact-hits.sh`): 513 lines across
the old addresses, in about 60 of the 144 files of `tests/Gallery.Web.Tests` (1,105 facts).

`/speckit-tasks` expands each row below to its facts, with old and new expected values, before any
executor is dispatched. A fact not covered by a row must pass unmodified; a failure there means the
change is wrong.

## Classes

- **U unchanged**: must pass byte-for-byte as written.
- **T transport**: the fact's subject (stored data, purge set, refusal reason, log event) is
  unchanged. Only the request and the answer's envelope move, by the table below. The asserted
  stored values, purge sets and reason strings MUST NOT be edited.
- **R retire**: the fact asserts markup or behaviour of a removed page or component. Deleted, and
  the behaviour that survives is re-asserted by a named new file.
- **W rewrite**: a browser fact on a removed page; rewritten against the workspace for the same
  behaviour.
- **G guard list**: a source-scan or route-list fact whose list changes.
- **N new**.

Transport table (class T):

| Old request | New request | Old success | New success | Old refusal | New refusal |
|---|---|---|---|---|---|
| form `POST /admin/library/images/{iid}` title, caption | A1 JSON | 303 `…?saved=1` | 200, `message` "Saved" | page re-rendered, status N, reason in `role="status"` | status N, `message` = reason |
| the same with `add`/`create`/`leave` | A7 / A3 / A8, one call each | 303 | 200 each | 422/404 page | same status, `message` |
| form `POST /admin/library/add` `gallery={gid}` | A7 | 303 `…added=&skipped=` | 200 `added`, `skipped` | page | status, `message` |
| form `POST /admin/library/add` `gallery=new` | A3 with ids | 303 to the builder | 201 `id`, `added` | page | status, `message` |
| form `POST /admin/library/delete`, `…/images/{iid}/delete` | A2 | 303 `…deleted=` | 200 `deleted`, `leftovers`, `failed` | page | status, `message` |
| form `POST /admin/galleries` | A3 | 303 to the builder | 201 `id` | page | status, `message` |
| form `POST /admin/galleries/order` | A4 | 303 | 200 | page 409 | 409, `message` |
| form `POST /admin/galleries/{gid}` title, description | A5 | 303 `saved=1` | 200 | page | status, `message` |
| the same with `vis=1` | A5 then A6 | 303 `visibility=` | 200 each | page | status, `message` |
| form `POST …/visibility` | A6 | 303 | 200 `public` | page 409 | 409, `message` |
| form `POST …/order` | A9 | 303 | 200 | page 409 | 409, `message` |
| form `POST …/cover` | A10 | 303 | 200 | page | status, `message` |
| form `POST …/remove` `image=` | A8 with one id | 303 `removed=` | 200 `removed` | page | status, `message` |
| form `POST …/delete` | A11 | 303 `deleted=` | 200 | | |
| `GET /admin/library…`, `/admin/galleries…` used only to read a status or a token | W1/W3 | | | | |
| `POST /admin/upload/confirm` | U12 per item | 200 `count` | 200 per item | 409 `not-ready` | (no batch gate; class R) |

`LibraryHost` gains `PostJsonAsync(path, body, csrf = true)` (the helper `UploadHost` has). A T fact
whose point was the atomicity of one combined form post (014: add + create + leave in one save) is
class R with a note: the workspace applies each tick at once, so that combination no longer exists.

## Files

Fact counts in brackets. A file with two classes lists both; tasks split it by fact.

**U (must pass unmodified)**: every file not named below, including `UploadFlowTests` [22] (the
single-photo checks of SC-012: `Rejects_gps_avif…`, `Rejects_gps_webp…`, `Rejects_wrong_width`,
`Rejects_a_missing_size`, `Rejects_placeholder_with_exif`, and the rest of U3 to U9),
`UploadIntakeTests` [6], `UploadResumeTests` [4], `UploadValidatorTests`, `UploadStorageTests`,
`UploadSeedCoexistenceTests`, `PublicOutputUnchangedTests` [1], every public-site, camera, nav
projection and purge, edge, sign-in, hosting and `Gallery.Core.Tests` file.

**T**: `LibraryEditTests` [8], `LibraryDeleteTests` [4], `LibraryMembershipTests` [5],
`LibraryMembershipEditTests` [14] (combined-save facts → R), `LibraryFlowTests` [1],
`PublishRefusalTests` [5], `InvariantTests` [1], `EdgeModeTests` [2], `EdgeSecretTests` [1],
`CameraRoutesTests` [10] (only the facts that mutate through owner routes), `NewTabLinkTests` [1],
`LibrarySourceGuardTests` [4] (its II.5 walk-through fact).

**G**: `AdminAccessTests` [16], `LibraryAccessTests` [5], `UploadAccessTests` [5] (route lists: old
routes move to the "answers as unknown" list; W1 to W4, A1 to A11, U12, U13 join the owner-only and
CSRF lists); `LibrarySourceGuardTests`, `UploadSourceGuardTests` [5] (script list: `workspace.js`,
`upload.js`; `library.js`, `reorder.js` leave); `UploadSecretScanTests` [1] (page address);
`IndexTests` [1], `AllPagesTests`, `NavCataloguePageTests` [9], `UiOwnerOnlyTests` [3] (catalogue
entry counts 40 → 35 and slugs); `UiPagesUnchangedTests` [1 theory] (baseline files: research
R-11); `LayoutFontCssTests`, `TrailSourceGuardTests`, `NavSourceGuardTests` (only if they name a
removed file).

**R, replaced by the N file in brackets**: `LibraryPageTests` [11], `PhotoEditPageTests` [10],
`GalleryListTests` [7], `BuilderTests` [9], `UploadPageTests` [14], `AdminNavConsistencyTests` [3]
(→ `WorkspacePageTests`); `UploadConfirmTests` [13] (→ `UploadJoinTests`, mapping below);
`UploadLifecycleTests` [15]: the two discard facts, `All_four_answer_409_confirm_in_progress…`, and
the two "refused confirm copy" facts (→ `UploadJoinTests`), the other ten are U;
`GalleryActionsTests` [14], `PhotoActionsTests` [9], `BuilderIconTests` [12], `IconTests` [17],
`IconRegistryGuardTests` [2], `TokenComboboxTests` [17] (→ `GalleryChecklistTests`);
the facts for removed components inside `AtomComponentTests`, `MoleculeComponentTests`,
`OrganismComponentTests`, `ComponentAdditionsTests`, `LibraryComponentAdditionsTests`,
`UploadComponentAdditionsTests`, `AtomPageTests`, `MoleculePageTests`, `OrganismPageTests`,
`AtomMoleculeStatePageTests`, `OrganismStatePageTests`, `TemplatePageTests`, `PageComponentTests`
(→ `WorkspaceComponentTests`); the six timing evidence files for removed pages
(`LibraryTimingEvidenceTests`, `GalleryListTimingEvidenceTests`, `PhotoEditTimingEvidenceTests`,
`MembershipTimingEvidenceTests`, `TileTimingEvidenceTests`, `UploadTimingEvidenceTests`), AFTER
their logs are captured (→ `WorkspaceTimingEvidenceTests`).

**W**: `LibraryBrowserTests` [17], `PhotoEditBrowserTests` [8], `GalleryFieldBrowserTests` [16],
`GalleryListBrowserTests` [7], `BuilderIconBrowserTests` [5], `TileIconBrowserTests` [7],
`GridWidthBrowserTests` [5], `LibraryTileGeometryTests` [2], `LayeringBrowserTests` [1],
`AdminNavBrowserTests` [3], `UploadBrowserTests` [10], `LocalSignInBrowserTests` [1] (landing
page only), `LayoutConsistencyBrowserTests.Every_admin_page_type_draws_the_same_chrome` (the public
fact in that file is U) → `WorkspaceBrowserTests`, `WorkspaceUploadBrowserTests`,
`WorkspaceLayoutBrowserTests`, `WorkspacePhoneBrowserTests`.

`UploadConfirmTests` → `UploadJoinTests`:

| Old fact | New fact |
|---|---|
| `Confirm_with_one_item_still_uploading_is_409_not_ready`, `…two_not_ready…`, `…zero_items…` | `Join_of_an_item_that_is_not_validated_is_409_not_active` [SAFETY II.5d] |
| `Confirm_unknown_or_invalid_batch_is_404` | `Join_of_an_unknown_item_is_404` |
| `Confirm_without_csrf_is_400` | `Join_without_csrf_is_400` |
| `Mutations_on_a_confirming_batch_are_409…` | `Delete_and_restart_of_a_joining_item_are_409_not_active` [SAFETY] |
| `Happy_confirm_of_two_items` | `Join_publishes_one_item_and_leaves_the_other_untouched` (same stored-field and object assertions, per item) |
| `Confirm_twice_after_a_simulated_crash` | `Join_twice_after_a_simulated_crash` (same crash seam) |
| `Reput_after_validation_publishes_nothing` | the same name, through U12 [SAFETY III.6] |
| `Public_pages_byte_identical_around_confirm` | `Public_pages_byte_identical_around_a_join_with_no_destination` [SAFETY II.5b] |
| `Removal_command_after_confirm…`, `Confirm_after_seeded_content…`, `Confirm_upgrades_a_legacy_seedKey_index…` | the same three, through U12 |

## New files

| File | Proves |
|---|---|
| `WorkspacePageTests` | W1 to W4 markup, fragments by header, reads per response (R-02), 404s, HEAD, one `<script>`, no icon-only control (SC-007), delete last in each panel template |
| `WorkspaceApiTests` | A1 to A11: status, JSON shape, the exact messages, body limits, cap, `RemoveManyAsync` flushes once |
| `WorkspaceComponentTests`, `GalleryChecklistTests` | K1 to K11 states; `Match` rules; default renders of K2, K3 byte-identical |
| `UploadJoinTests` | the mapping above, plus: destination set only on an empty set; [SAFETY] 409 `destination-public` without the flag; [SAFETY] no destination → no gallery call, no edge work; [SAFETY] join never changes visibility (private stays private, empty private stays private); gallery gone mid-upload; `added` survives a reload; U13 gates and drops; overlapping joins converge |
| `WorkspaceRemovalGuardTests` | every removed address answers as an unknown admin address, for GET and POST (SC-009); a source scan finds no removed page, component, script or route literal; `DOMParser` appears in one function of `workspace.js`; the four sinks in no admin script |
| `WorkspaceBrowserTests` | stories 1, 3, 5: select, shift-range, cap, save on leave, tick/partly ticked, typed new gallery, drag onto a gallery, reorder by drag and by the Move buttons, cover, public switch and its refusal, new gallery, delete, large view keys, back/forward/reload keep place, SC-004 (100 ms), swapped tile is styled (R-01), checklist parity with `Match` |
| `WorkspaceUploadBrowserTests` | story 2: tray open/hide/show/done, drop anywhere, destination named, the public notice visible before picking, photos appear as each finishes, a failed photo holds nothing up (SC-013 shape), carries on across a collection switch, resume after reload, closing drops failed |
| `WorkspaceLayoutBrowserTests` | IX.4: frame geometry equal across the six contexts at 390 and 1440 px (FR-038) |
| `WorkspacePhoneBrowserTests` | story 6 at 390 px with touch: no sideways scroll, targets ≥ 44 px, sheet opens and closes, Move buttons reorder |
| `WorkspaceTimingEvidenceTests` (Category=Evidence) | SC-005, SC-006, SC-013 logs |
