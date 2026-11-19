# Test Impact: 020 Admin Workspace

"Existing tests pass unmodified unless a requirement change is stated" (constitution, Development
Workflow). This file is the statement. Every test line that names an old admin address is listed in
`evidence/test-impact-hits.txt` (regenerate with `evidence/test-impact-hits.sh`): 513 lines across
the old addresses, in about 60 of the 144 files of `tests/Gallery.Web.Tests` (1,105 facts).

`/speckit-tasks` expands each row below to its facts, with old and new expected values, before any
executor is dispatched. A fact not covered by a row must pass unmodified; a failure there means the
change is wrong.

**AMENDED (tasks) C-11**: `LibraryAccessTests` is retired; `WorkspaceAccessTests` (new) carries its three guarantees for W1–W4 and A1–A11 (TI-35). Corrections C-01 to C-19 in tasks.md override the rows they name.

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


## Fact-level expansion (tasks phase, 2026-11-18)

Written by `/speckit-tasks` from the test files themselves (whole facts read, not grep lines;
helper scripts in the tasks session: per-fact symbol scan over every `tests/**/*.cs`, source-only).
Row ids `TI-nn` are what tasks.md and `evidence/test-impact-edit.py` cite. Each row names the
checkpoint (CP-0 … CP-H in tasks.md) at which the PARENT applies it; before that checkpoint the
fact must still pass as written. "Transport" means: change only the request and the envelope
assertions shown; every asserted stored value, purge set, reason string and log line not named
here stays byte-identical.

### Corrections to the table above (found by reading the facts)

- **Missing from the classes above, now rows**: `OwnerSessionTests` (TI-40), `OwnerClaimTests`
  (TI-41), `SignInComponentTests` (TI-42), `UploadResumeTests` (TI-33), `BatchStoreTests` (TI-32),
  `PhotoReaderTests` (TI-61), `OrganismStatePageTests` (TI-56), `PublicOutputUnchangedTests`
  (TI-57, baseline trim, not a test edit). These were listed as U or not at all.
- **Shared helpers live in files that are retired**: `LibraryHtml` is declared in
  `LibraryPageTests.cs:11` and `GalleryHtml` in `GalleryListTests.cs:13`; U files call them
  (`HomePageTests`, `TrailRoutesTests`, `CameraPhotoRoutesTests`, `CameraRoutesTests`,
  `Fixtures/HomeCaptureLibrary.cs`). TI-01 moves both classes verbatim into `Fixtures/` before any
  retirement. `UploadApi` (declared in `UploadLifecycleTests.cs:495`) stays where it is: that file
  is kept.
- **`ConfirmService` keeps its name** (U files call `ConfirmService.MasterKey`:
  `ImageDeleteTests.cs:59,60,72,124,166,173`, `RemoveSeededTests.cs:101,353`,
  `LibraryDeleteTests.cs:59`, `Fixtures/LibraryHost.cs:233`). The per-item join is the new method
  `ConfirmService.JoinAsync`; there is no `JoinService` type (tasks.md correction C-02).
- **U2 keeps `"confirming": false`** (`UploadFlowTests.cs:200` reads it and is U). Correction C-01.
- **`UploadSourceGuardTests` needs no edit** (its script rows are `upload.js`, `upload-worker.js`;
  the `library.js`/`reorder.js` rows live in `LibrarySourceGuardTests`, TI-36). **`UiOwnerOnlyTests`
  and `AllPagesTests` need no edit** (counts are derived from `UiCatalog.Entries`). **`LayoutFontCssTests`,
  `TrailSourceGuardTests`, `NavSourceGuardTests` need no edit** (they name no removed file).
- **`CameraRoutesTests`**: only one fact touches an owner route (TI-38), not ten.
- **`LibraryAccessTests`** is R, not G: its 16-route table, form bodies and bare-POST statuses are
  form-route specific. The same three guarantees move to the new `WorkspaceAccessTests` (TI-35).
- **`IndexTests` moves twice**: 40 → 48 at CP-B (entries added beside the old ones), 48 → 35 at
  CP-E (removal).
- **`ContactSheetPage` loses three specimens**, it does not only gain one: its specimens 4–6
  (`ContactSheetPage.razor:27-59`) render `SelectionBar`, `DragHandle`, `BatchItem`, `BatchFooter`,
  all removed. TI-57/TI-58.

### Rows

**TI-01 helpers (CP-0, Setup; test infrastructure, no fact changes)**: move `internal static
partial class LibraryHtml { … }` (LibraryPageTests.cs:10-71, its doc comment through the closing brace)
to `tests/Gallery.Web.Tests/Fixtures/LibraryHtml.cs`, and `internal static class GalleryHtml { … }`
(GalleryListTests.cs:12-98) to `Fixtures/GalleryHtml.cs`, verbatim, namespace
`Gallery.Web.Tests`. Add `LibraryHost.PostJsonAsync(string path, object body, bool csrf = true)`
copying `UploadHost.PostJsonAsync`'s body (UploadHost.cs:97-106, same `JsonSerializerDefaults.Web`
options) and `LibraryHost.GetJsonAsync(path)`. Add `UploadSite.OpenWorkspaceAsync(string path =
"/admin", bool javaScript = true, Func<IPage, Task>? beforeNavigate = null)` beside
`OpenUploadAsync` (UploadBrowserFixture.cs:244), same body with the path parameter. Suite counts
unchanged.

**TI-10 LibraryEditTests (T, CP-E)** — transport L2 → A1 (`PostJsonAsync($"/admin/api/photos/{iid}", new { title, caption })`):
- `Save_answers_303_and_the_panel_shows_the_stored_values_and_Saved`: 303 + Location → 200,
  JSON `title` "Harbour at dusk", `caption` "Low tide & gulls.", `message` "Saved". Stored-values
  assertion unchanged. The follow-up GET and its `aside`/`sp-status`/status-line assertions are
  dropped (no panel page; save-on-leave is `WorkspaceBrowserTests.Title_saves_on_leave_*`).
- `Invalid_text_is_422_with_the_reason_and_stores_nothing` (4 rows): 422 unchanged; `Status(html)`
  → JSON `message`; the four reason strings unchanged; stored values and empty settle unchanged.
  The arrange step's form save becomes A1 too.
- `Bare_POST_answers_303_and_keeps_both_stored_values`: `PostBareAsync(…/admin/api/photos/{iid},
  csrfHeader: true)` → **400** (`bad-request`, body not JSON; http.md shared rules), was 303.
  Stored values and empty settle unchanged. Rename `Bare_POST_answers_400_and_keeps_both_stored_values`.
- `Unknown_or_malformed_image_is_404`: both 404 unchanged, through A1.
- `Purge_is_exactly_P_TEXT_and_nothing_for_an_image_in_no_gallery`: 303 → 200 (twice); the
  purged set and the loose-title assertion unchanged.
- `Saving_a_title_evicts_the_photo_page_but_not_the_home_page`: 303 → 200; every count unchanged.
- `Panel_links_each_holding_gallery_builder_and_states_the_delete_consequence`: **R** →
  `WorkspacePageTests.Photo_cells_carry_the_delete_consequence_for_their_gallery_count` (the two
  consequence sentences, "It is in 2 galleries. …" and "It is in no gallery. …", verbatim).
- `Unknown_open_id_leaves_the_panel_closed`: **R** (no `?open=`; nothing survives).

**TI-11 LibraryDeleteTests (T, CP-E)** — L5/L4 → A2 (`/admin/api/photos/delete`, `{ ids }`):
- `L5_deletes_the_photo_everywhere_and_logs_one_owner_action`: 303 + Location → 200 with
  `deleted` 1, `leftovers` 0, `failed` 0, `message` "1 photo deleted.". Store, storage, 404 and
  `owner.action` assertions and `DeleteTimings == 1` unchanged. The library GET (status line, tile
  ids) is dropped. Second delete: 303 `deleted=0` → 200 `deleted` 0 (`DeleteManyAsync` on a gone
  id; if it answers anything else, escalate, do not edit the expectation); empty settle and the
  single owner.action line unchanged.
- `L4_of_three_with_one_failing_key_reports_the_leftover_and_calls_the_single_delete_three_times`:
  303 → 200 `deleted` 3, `leftovers` 1, `failed` 0; the status line "3 deleted · 1 files could not
  be removed and were logged" → `message` "3 photos deleted. 1 files could not be removed and were
  logged." (C-07). Counts and log lines unchanged.
- `L4_above_PageSize_is_422_and_deletes_nothing`: 422 unchanged; status → `message` "Select at most
  2 photos." unchanged; the rest unchanged.
- `The_bulk_confirm_copy_matches_the_contract_and_is_outlined_danger`: **R** →
  `WorkspacePageTests.Selection_template_delete_is_the_last_control_with_the_bulk_copy` (the
  sentence "This removes them from every gallery and deletes the original files. It can't be
  undone." and `btn-quiet danger-cancel` then `btn btn-danger`, no `<details`).

**TI-12 LibraryMembershipTests (R, CP-E)**, 5 facts: every fact asserts the `PhotoActions` icon row
(`pa-group`, `data-icon`, `aria-label` "In 1 gallery"). Nothing survives (owner decision 2: no
icons). Retired with the file.

**TI-13 LibraryMembershipEditTests (CP-E)** — these facts drive `ImageService.SavePanelAsync`
through L2; A7/A3/A8 call `BulkAddAsync`/`RemoveManyAsync`, whose op lines differ.
- `Add_to_an_existing_gallery_is_303_and_purges_exactly_what_the_bulk_add_purges`: T → A7
  `{ ids: [img] }`: 200 `added` 1 `skipped` 0; Items/GalleryIds unchanged; the
  `library.image.memberships ok` line assertion → `Assert.Single(Lines(h, "library.bulk.add"), l =>
  l.EndsWith(" ok"))`; the comparison half (B5/L3) becomes A8 then A7, purge sets compared as before.
- `Create_makes_one_private_gallery_holding_the_photo_last_in_homeOrder_with_no_purge`: T → A3
  `{ title: "Night walks", ids: [img] }`: 201; every stored and purge assertion unchanged.
- `Create_add_and_title_in_one_POST_are_all_applied_with_one_ok_line`: **R** (combined save no
  longer exists; each part is its own A-call, covered above and in TI-10).
- `A_201_char_create_title_is_422_and_writes_nothing`: T → A3 with ids: 422, `message` "The title
  can be at most 200 characters."; the `library.image.memberships refused` line assertion is
  dropped (that op belongs to SavePanelAsync); stored title and empty settle unchanged.
- `An_unknown_add_id_with_a_create_is_404_and_creates_nothing`: **R** (combined).
- `An_unknown_add_id_re_renders_the_panel_on_the_stored_holders_with_no_pending_token`: **R** (panel markup).
- `A_malformed_add_id_is_404_and_writes_nothing`: T → A7 on `/admin/api/galleries/not-an-id/photos`:
  404; the status-line assertion dropped (an invalid `{gid}` is a bare 404 by the shared rule);
  empty settle unchanged.
- `A_text_refusal_with_an_add_is_422_and_changes_no_membership`: **R** (combined).
- `With_a_session_and_no_antiforgery_token_add_and_create_are_400_and_store_nothing`: T → A7 and A3
  with `csrf: false`: 400 each; dump and empty purge unchanged.
- `An_empty_edit_logs_no_memberships_line`: **R** (SavePanelAsync op).
- `Adding_an_already_held_gallery_is_303_with_no_duplicate`: T → A7: 200 `added` 0 `skipped` 1,
  `message` "Already in Coast." (gallery title per the fact); stored assertions unchanged.
- `Leave_takes_the_photo_out_clears_its_pinned_cover_and_purges_exactly_what_B5_purges`: T → the
  pin step B4 → A10 `{ image }` 200; the leave → A8 `{ ids: [img] }` 200 `removed` 1; the
  memberships `ok` line assertion dropped; the comparison step B5 → A8; purge-set equality unchanged.
- `Leaving_a_public_gallery_s_last_photo_keeps_it_public_and_drops_it_from_g`: T → A8: 200
  `removed` 1, `listed` false, `message` ends " Visitors can no longer see {title} until it has a
  photo."; every stored and page assertion unchanged.
- `Leave_and_add_of_the_same_id_is_303_and_changes_nothing`: **R** (combined).

**TI-14 LibraryFlowTests (T, CP-E)** `Forty_photos_become_a_public_gallery_in_three_actions`:
GET `/admin/library` → GET `/admin` (200). L3 new → A3 `{ title: "Coast", ids: forty }` 201, gid
from JSON `id`, `added` 40 (was the Location regex). B3 → A9 `{ order }` 200 (was 303 + Location).
The builder GET and its "When you save, …" warning assertion are dropped (no Save form; decisions
row 7). B2-with-public → A6 `{ public: true }` 200, `message` "Coast is now public. Visitors can
see it." The `actions <= 6` count, the purge-set equality, the `/g` assertions unchanged. The
library GET with `pa-group` labels is dropped. The repeat L3 → A7 `{ ids: forty+five }` 200
`added` 5 `skipped` 40, `message` "5 photos added to Coast. 40 were already in it. Visitors can see
them now."; item-count assertion unchanged.

**TI-15 PublishRefusalTests (CP-E)**:
- `An_empty_gallery_is_refused_409_for_every_request_shape_and_nothing_changes`: T. The five G4
  shapes become A6 `{ public: true }` (three of them collapse to this one), A6 `{ public: false }`
  (old `[]`), each 409 with `message` = `GalleryService.EmptyPublishReason`; the old bare POST with
  the CSRF header becomes A6 bare → **400** (`bad-request`; nothing parsed). The two B2-with-public
  shapes become A6 `{ public: true }` 409 (A5 is not sent: text is not the subject). `AssertRefusedAsync`
  reads `message` instead of the status line. Dump, home bytes, recorder count, owner.action absence unchanged.
- `The_refusal_rerenders_the_page_it_came_from`: **R** (no pages).
- `A_remove_racing_the_publish_is_refused_by_the_conditional_write`: T → A6 `{ public: true }`: 409,
  `message` = Reason; every other assertion unchanged (the race seam is in the service).
- `A_remove_racing_the_Save_publish_is_refused_by_the_conditional_write_and_stores_no_text`: **R**
  (the combined text+publish save no longer exists; the conditional write is still proved by the fact above).
- `The_empty_card_and_the_empty_builder_render_the_switch_disabled_with_the_reason`: **R** →
  `WorkspacePageTests.Empty_gallery_default_panel_has_the_switch_disabled_with_the_reason`.

**TI-16 InvariantTests (T, CP-E)** `Every_step_of_an_owner_session_keeps_the_invariants_and_a_one_read_home_page`:
the `Post` helper sends JSON (`PostJsonAsync`) and asserts the status. Steps: G2 → A3 `{ title }`
**201**, gid = JSON `id` (both creates); L3 → A7 200 (`added` 5, `skipped` 0 replaces the Location);
B3 → A9 200; B4 pin → A10 `{ image }` 200, unpin → A10 `{ image: null }` 200; B5 → A8 `{ ids:
[order[0]] }` 200 (`removed` 1 replaces the Location); G4 on → A6 `{ public: true }` 200; B2 rename →
A5 `{ title: "Coast Path" }` 200; G4 off → A6 `{ public: false }` 200; republish → A6 200; L4 → A2
200 (`deleted` 2, `leftovers` 0, `failed` 0 replace the Location); B6 → A11 200 (Location dropped);
refusal → A6 409, `message` contains `EmptyPublishReason` (was the decoded body); stale → A9 409,
`message` = "This gallery changed in another tab. Reload to see the current order.". Every stored,
home and invariant assertion unchanged.

**TI-17 EdgeModeTests (T, CP-E)**:
- `With_no_edge_settings_every_009_post_answers_303_and_log_only_lines_carry_the_addresses`:
  `Post303Async` → `PostOkAsync` (asserts 200/201 per route, returns the JSON). G2 → A3 201 (`id`);
  L3 existing → A7 200 (`added` 2, `skipped` 0); L3 new → A3 `{ title: "Dunes", ids: [c] }` 201;
  L2 → A1 200; B2 → A5 200; B3 → A9 200; B4 pin/unpin → A10 200; G4 publish → A6 200; G3 → A4 200;
  B5 → A8 200; G4 unpublish → A6 `{ public: false }` 200; L5 → A2 `{ ids: [d] }` 200; L4 → A2 200;
  B6 → A11 200. Every Location assertion is replaced by the status; every edge-line, P-VIS and
  deleted-file assertion unchanged. Rename `…every_workspace_post_answers_ok_and_…`.
- `With_the_gate_closed_L3_G4_and_L4_answer_303_with_zero_recorder_calls_and_the_gate_delivers_them`:
  `Gated303Async` asserts 200/201 instead of 303; L3 → A3 with ids (201, `id`), G4 → A6 200, L4 →
  A2 200. Every recorder and stored assertion unchanged.

**TI-18 EdgeSecretTests (T, CP-E)**: scans: A3 `{ title: "Coast", ids: [a, b] }` (was L3);
GETs `/admin`, `/admin` with `X-Workspace: main`, `/admin` with `X-Workspace: chrome`,
`/admin/g/{gid}`, `/admin/tiles/{a}` (was L1, L1?open, L6, G1, B1; still 5); A5 `{ title: "   " }`
422 (was B2 refused); A6 `{ public: true }` 200 (was B2 save with public); A6 `{ public: false }`
200; A6 `{ public: true }` 200; A5 `{ title: "Coast Path" }` 200; A2 `{ ids: [b] }` 200 (was L5);
A11 200 (was B6); after-GETs `/admin` and `/admin/unplaced` (was the two PRG targets).
`Assert.Equal(15, scanned.Count)` unchanged (1 + 5 + 7 + 2). Labels name the new route ids.

**TI-19 NewTabLinkTests (T, CP-E)**: path list → `["/admin", $"/admin/g/{pub}", $"/admin/g/{priv}"]`
(was 4 paths). Rules unchanged; the `<template>` "View on site ↗" link counts as an arrow link and
must carry `target="_blank" rel="noopener"`.

**TI-20 UploadConfirmTests (R, CP-D)** → `UploadJoinTests` per the mapping table above (13 facts;
four [SAFETY]).

**TI-21 UploadLifecycleTests (CP-D)**: retire `Discard_deletes_the_batch_prefix_and_closes_the_batch`,
`Discard_of_a_batch_that_is_not_the_open_one_is_404`, `All_four_answer_409_confirm_in_progress_while_confirming`
(→ `UploadJoinTests.Delete_and_restart_of_a_joining_item_are_409_not_active`),
`Delete_works_on_the_item_a_refused_confirm_copy_left_failed` and
`Restart_of_the_item_a_refused_confirm_copy_left_failed_asks_for_the_file` (→ the same names with
"join" for "confirm" in `UploadJoinTests`). The other ten facts and `UploadApi` stay byte-identical.

**TI-30 UploadAccessTests (G, CP-D)**: the `Routes` table loses `U1`, `U10`, `U11` and gains
`["U12"] = new("POST", "/admin/upload/items/{item}/join", r => new { attemptId = r.AttemptId },
HttpStatusCode.Conflict, "not-active")` (a registered item is `queued`) and `["U13"] = new("POST",
"/admin/upload/clear", r => new { batchId = r.BatchId }, HttpStatusCode.Conflict, "still-working")`.
Theory rows: all routes 11 → 10, POST routes 10 → 10. Nothing else changes.

**TI-31 UploadSecretScanTests (G, CP-D)**: in `CollectAsync` the route list `"/admin/upload"` →
`"/admin"`; the count comment "(U3, U4, U1, U2)" → "(U3, U4, W1, U2)"; `4 + …` unchanged.

**TI-32 BatchStoreTests (CP-D)**: retire `Append_after_TryStartConfirm_refuses_every_item` and
`TryStartConfirm_refuses_an_empty_batch_and_one_with_an_unready_item` (the batch lock is removed;
the per-item claim is `UploadJoinTests.Fifty_concurrent_claims_of_one_item_let_exactly_one_join`).
`UploadHost_signs_in_and_caches_a_csrf_token` (GET `/admin` 200 + a token) stays U: W1 renders the
sign-out form's antiforgery input.

**TI-33 UploadResumeTests (CP-D)** `After_121_seconds_U2_and_the_page_show_the_three_active_items_interrupted`:
delete the page part (UploadResumeTests.cs:74-84, from `var page = await host.GetAsync("/admin/upload")`
through the four `Regex.Count` asserts); the U2 and stored-state assertions stay. Rename
`After_121_seconds_U2_shows_the_three_active_items_interrupted`. The tray's rendering of the same
state is `WorkspaceUploadBrowserTests.Reload_mid_upload_reopens_the_tray_with_interrupted_rows`.

**TI-34 AdminAccessTests (G)**: `Owner_GetAdminUpload_and_library_return_200` → at CP-D
`/admin/upload` expects **404** (library still 200); at CP-E `/admin/library` expects 404 too;
rename `Owner_gets_the_workspace_and_old_pages_answer_404`, and add `GET /admin` 200 first. The
I1/I5 theory rows with `/admin/upload`, `/admin/library`, `/admin/galleries` stay as written
(signed-out requests to an unknown admin address still answer 302, the catch-all is inside the
owner group). Every other fact U.

**TI-35 LibraryAccessTests (R, CP-E)** → `WorkspaceAccessTests` [SAFETY I.3, I.6]: the same three
facts over W1–W4 and A1–A11 (no session: GET 302 to sign-in / POST 401, dump unchanged; session
without token: POST 400 with form, bare and wrong token, dump unchanged, no purge; a bare POST with
the CSRF header answers **400 `bad-request`** on every A-route, never 500), plus U12/U13 already in
TI-30. The 16 old routes join `WorkspaceRemovalGuardTests`' unknown-address list.

**TI-36 LibrarySourceGuardTests (CP-E)**:
- `Public_pages_carry_no_owner_markup_with_or_without_the_owner_session` (T + G): build step L3 →
  A3 `{ title: "Coast", ids: [a, b] }` 201 (`id`), G4 → A6 `{ public: true }` 200. Non-vacuity GET
  `/admin/library` → `/admin`, `Contains("owner-bar")` → `Contains("class=\"ws-collections\"")`;
  signed-out `/admin` → `Redirect`. `AssertNoOwnerMarkup` gains the class prefix `ws-` beside
  `lib-`/`gal-`/`b-ctl` and keeps the `owner-bar` and `/admin-assets/` checks [SAFETY IV.22].
- `Library_script_has_no_html_injection_sink`: rows `library.js`, `reorder.js` → one row
  `workspace.js` (2 → 1).
- The other two facts U.

**TI-37 UploadSourceGuardTests**: none (see corrections).

**TI-38 CameraRoutesTests (T, CP-E)** `Camera_pages_carry_no_owner_markup_with_or_without_the_owner_session`:
`/admin/library` → `/admin` (twice) and `Contains("owner-bar")` → `Contains("class=\"ws-collections\"")`.

**TI-40 OwnerSessionTests (CP-C)** `Returning_owner_signin_shell_no_claim_one_signin`:
`Contains("admin-empty")` → `Contains("<admin-workspace")`, `Contains("owner-bar")` →
`Contains("class=\"ws-collections\"")`. Rest U.

**TI-41 OwnerClaimTests (CP-C)**: `SignIn_claims_site_sets_session_cookie_and_logs_claim` the same
two replacements (lines 90-91); `Shell_page_passes_security_guards`: `Guards.AssertNoScript(html)` →
`Assert.Equal(1, Guards.Count(html, "<script"))` and `Assert.Matches("<script type=\"module\" src=\"/admin-assets/workspace\\.js\\?v=[0-9a-f]{8}\"", html)`
(the workspace's one script, plan constraint); `AssertNoInlineStyle` unchanged. Rename
`Workspace_page_passes_security_guards`.

**TI-42 SignInComponentTests (CP-E)**: retire `OwnerBar_SignOut_renders_form_with_button_and_token`
and `OwnerBar_SignOut_absent_by_default` (the sign-out form is in `CollectionList`:
`WorkspaceComponentTests.CollectionList_sign_out_form_carries_the_token`).
`AdminLayout_ShowOwnerBar_false_renders_no_owner_bar` → parameter `["ShowOwnerBar"] = false` →
`["Bare"] = true`, assertions → no `<admin-workspace`, no `ws-list`, no `owner-bar`; rename
`AdminLayout_Bare_renders_no_workspace_frame`. `AdminLayout_default_renders_owner_bar` →
`Contains("<admin-workspace")` and the three region classes `ws-list`, `ws-main`, `ws-panel`;
rename `AdminLayout_default_renders_the_workspace_frame`.

**TI-50 IndexTests (G)**: CP-B `Assert.Equal(40, …)` → 48 (comment: "+ 8 from 020"); CP-E → 35
(comment: "− 13 removed by 020"). Headings, `noindex`, no style/script unchanged.

**TI-51 NavCataloguePageTests (CP-E)**: `NewPages` loses the `PhotoActionsPage` row (5 → 4 rows);
`Catalogue_lists_the_010_pages_at_the_end_of_the_molecules`: `TakeLast(5)` → `TakeLast(4)` and the
`PhotoActions` tuple removed; `PhotoActions_page_shows_the_fixed_copy` retired.

**TI-52 UiPagesUnchangedTests (baselines, not the test)**: CP-B adds 8 files
(`atoms/mark.html`, `molecules/photo-cell.html`, `molecules/gallery-checklist.html`,
`molecules/upload-row.html`, `molecules/toast.html`, `organisms/collection-list.html`,
`organisms/upload-tray.html`, `organisms/photo-stage.html`) and regenerates `index.html`,
`organisms/side-panel.html`, `organisms/contact-sheet.html` (appended specimens only). CP-E deletes
13 (`atoms/icon.html`, `atoms/icon-button.html`, `molecules/batch-item.html`,
`molecules/cover-marker.html`, `molecules/drag-handle.html`, `molecules/selection-bar.html`,
`molecules/visibility-toggle.html`, `molecules/gallery-actions.html`, `molecules/token-combobox.html`,
`molecules/photo-actions.html`, `organisms/owner-bar.html`, `organisms/batch-footer.html`,
`organisms/gallery-builder.html`) and regenerates `index.html`, `templates/admin-layout.html`,
`organisms/contact-sheet.html` (specimens 4–6 gone). Every regeneration is diffed against the
frozen copy `evidence/pre-ui-baseline/` (Setup) and may differ only as stated.

**TI-53 AtomMoleculeStatePageTests (CP-E)**: retire `BatchItemPage_…`, `CoverMarkerPage_…`,
`DragHandlePage_…`, `SelectionBarPage_…`, `VisibilityTogglePage_contains_all_state_labels`. The
other five facts U.

**TI-54 MoleculeComponentTests (CP-E)**: retire the 21 facts that render a removed component
(11 `BatchItem_*`, 3 `CoverMarker_*`, 2 `DragHandle_*`, 2 `SelectionBar_*`, 3 `VisibilityToggle_*`);
remove the then-unused `using`/helper lines only. The other 8 facts U.

**TI-55 OrganismComponentTests (CP-E)**: retire the 5 `OwnerBar_*`, 5 `BatchFooter_*`, 3
`GalleryBuilder_*` facts and `AdminLayout_has_owner_bar_class`. `AdminLayout_has_exactly_one_style_element`,
`AdminLayout_has_no_script_element`, `AdminLayout_always_has_noindex_meta` stay U: the rewritten
layout keeps one `<style>`, renders NO `<script>` (WorkspacePage renders it), keeps `noindex`.

**TI-56 OrganismStatePageTests**: CP-B: the `SidePanelPage` row gains the appended label
"Without a save row" (6 labels). CP-E: the `OwnerBarPage`, `BatchFooterPage`, `GalleryBuilderPage`
rows are removed (6 → 3 rows); `Danger_confirms_on_organism_pages_are_dialogs_with_Cancel` iterates
`SidePanelPage` only and loses the `footer`/`builder` asserts; `ContactSheet_page_admin_usages_keep_public_column_counts`
→ headings `Skip(3)` equal `["Workspace fill grid"]`, `Count("cs cs-l4 cs-m3 cs-s1") == 2`,
`Count("cs cs-l3 cs-m3 cs-s1") == 1`, `Count("cs-fill") == 1`, the two `tile is-…` counts removed;
rename `ContactSheet_page_shows_the_workspace_fill_grid_after_the_public_ones`.
`AdminLayout_page_is_a_full_document_with_bar_and_aside_and_no_script` → `owner-bar` →
`class="ws-list"`, `admin-aside` → `class="ws-panel"`, `aria-current="page"` kept (the current
collection), the rest unchanged; rename `AdminLayout_page_is_a_full_document_with_the_three_regions_and_no_script`.

**TI-57 PublicOutputUnchangedTests (baseline, CP-E)**: `specs/005-admin-ui-components/evidence/baseline/ContactSheetPage.html`
loses its specimens 4–6 ("Admin library, 3 selected", "Admin gallery list, 2 private", "Admin
upload batch"); the head, the three public specimens and the tail stay byte-identical. The test
is not edited; it then passes with the appended "Workspace fill grid" specimen.

**TI-58 OrganismPageTests**: none (the `>Admin library` cut becomes a no-op; the page still holds
exactly 2 `cs cs-l4 cs-m3 cs-s1` because the fill specimen omits `cs-l*`).

**TI-59 LibraryComponentAdditionsTests (CP-E)**: retire the 16 facts on `SelectionBar` (4),
`VisibilityToggle` (5), `CoverMarker` (2), `GalleryBuilder` (5); the other 6 U.
**UploadComponentAdditionsTests (CP-E)**: retire the 4 `BatchItem_*` facts; the other 4 U.

**TI-60 component files retired whole (CP-E)**: `GalleryActionsTests` [14], `PhotoActionsTests` [9],
`BuilderIconTests` [12], `IconTests` [17], `IconRegistryGuardTests` [2], `TokenComboboxTests` [17]
(match rules → `GalleryChecklistTests`).

**TI-61 PhotoReaderTests (R, CP-E)**: `LibraryReader.PhotoAsync`/`PageAsync` are deleted with
their pages. Facts → `WorkspacePageTests`: `Tile_fragment_is_one_read_and_404s_unknown_and_tombstoned_ids`
(1 `db.library.page` line, 0 `db.galleries.list` lines for each 404).

**TI-62 page files retired whole**: `LibraryPageTests` [11], `PhotoEditPageTests` [10],
`GalleryListTests` [7], `BuilderTests` [9], `AdminNavConsistencyTests` [3] (CP-E);
`UploadPageTests` [14] (CP-D). What survives moves as follows (new fact in brackets):
`Pages_hold_PageSize_newest_first…` [WorkspacePageTests.Library_chunk_is_PageSize_newest_first_with_a_next_cursor];
`Tombstoned_images_are_not_listed` [… .Tombstoned_photos_are_in_no_collection];
`Empty_library_says_…` [… .Empty_collections_say_how_to_fill_them]; `Limits_come_from_LibraryOptions`
[… .Limits_reach_the_page_from_LibraryOptions]; `Bulk_ids_above_PageSize_…` [WorkspaceApiTests.Bulk_routes_refuse_more_than_PageSize_ids];
`Picker_lists_galleries_in_home_order…` [WorkspacePageTests.Collections_follow_home_order_with_counts_and_Private];
`G2_creates_a_private_gallery_last_in_home_order…`, `G3_stores_public_and_private_alike…`,
`Publish_from_the_list_and_unpublish…` [WorkspaceApiTests A3, A4, A6 facts, same stored/purge asserts];
`B1_renders_the_builder_and_an_unknown_gallery_is_404` [WorkspacePageTests.Gallery_collection_lists_members_in_order_and_unknown_is_404];
`B2_saves_title_description…`, `B2_with_a_too_long_title…`, `Order_and_cover_steps_set_the_home_card_cover`,
`Removing_photos_clears_the_pin…`, `Rename_redirects_the_old_address…`, `Delete_gallery_keeps_its_photos…`
[WorkspaceApiTests A5, A9/A10, A8, A5, A11 facts, same stored/purge/page asserts];
`Malformed_unknown_and_tombstoned_ids_are_404_and_HEAD…` [WorkspacePageTests HEAD + 404 facts];
`No_inline_style_and_exactly_one_script` [WorkspacePageTests.One_module_script_and_no_inline_style];
`Admin_assets_cache_rules`, `Script_version_is_the_content_hash`, `Missing_asset_gets_version_dev`
[WorkspacePageTests, the same three for `workspace.js`]; `Page_holds_no_storage_url_or_key`
[WorkspacePageTests.Workspace_holds_no_storage_url_or_key]; `Signed_out_get_redirects_to_signin`
[WorkspaceAccessTests]. The rest assert removed markup (owner bar, icon rows, footers, `?open=`,
breadcrumbs, Save forms) and are dropped.

**TI-63 timing evidence files retired (CP-E, after Setup's capture)**: `LibraryTimingEvidenceTests`,
`GalleryListTimingEvidenceTests`, `PhotoEditTimingEvidenceTests`, `MembershipTimingEvidenceTests`,
`TileTimingEvidenceTests`; `UploadTimingEvidenceTests` at CP-D (its committed
`specs/008-upload-pipeline/evidence/upload-timings.log` stays as history). → `WorkspaceTimingEvidenceTests`.

**TI-70 browser files (W)** → rewritten in the new browser files; each old fact either maps to a
named new fact or is dropped with the reason:
- `LibraryBrowserTests` [17] (CP-E): fixture guard → `WorkspaceBrowserTests.Fixture_guard_workspace_is_styled_and_the_script_upgraded`;
  builder drag → `Drag_a_photo_to_the_front_stores_the_new_order`; gallery-card drag →
  `Drag_a_gallery_name_to_the_top_stores_the_home_order`; keyboard lift/escape facts →
  `Move_earlier_and_later_buttons_store_the_order` (no keyboard lift: R-07); select-three/select-all/limit
  → `Shift_click_selects_the_range_and_select_all_stops_at_the_cap`; tick past the limit →
  `Selecting_past_the_cap_is_refused_at_selection_time`; picker public note →
  `Ticking_a_public_gallery_says_visitors_can_see_them_now`; list/builder switch facts →
  `Public_switch_applies_at_once_and_an_empty_gallery_refuses_with_the_reason`; controls under the
  photo → `Only_the_select_mark_is_drawn_on_a_photo`; one filled button →
  `One_primary_button_per_context_and_every_delete_is_outlined_and_last`; bulk/single delete modal
  facts → `Delete_confirms_name_what_is_deleted_and_Cancel_deletes_nothing`; public pages load no
  admin assets → `Public_pages_load_no_admin_assets_even_for_the_owner` (same body).
- `PhotoEditBrowserTests` [8], `TileIconBrowserTests` [7], `BuilderIconBrowserTests` [5],
  `GalleryListBrowserTests` [7], `GalleryFieldBrowserTests` [16], `GridWidthBrowserTests` [5],
  `LibraryTileGeometryTests` [2], `LayeringBrowserTests` [1], `AdminNavBrowserTests` [3] (CP-E):
  surviving behaviours → `WorkspaceBrowserTests` (`Checklist_rows_equal_GalleryChecklist_Match_for_each_query`,
  `Typing_a_new_name_and_Enter_creates_a_private_gallery_with_the_selection_and_keeps_focus`,
  `Typing_an_existing_name_and_Enter_adds_without_a_duplicate`, `Large_view_*` (Phase 7),
  `Select_mark_tick_is_centred_and_placed_from_each_photos_corner`, `Every_cell_title_fits_its_cell_at_1440`);
  icon, title-band, builder-row, pager, breadcrumb, Save-form and no-JS facts are dropped (removed
  surfaces; the workspace needs scripting, spec Edge Cases).
- `UploadBrowserTests` [10] (CP-D) → `WorkspaceUploadBrowserTests`: worker path → `Two_photos_join_as_each_finishes_with_no_confirm`;
  `Storage_PUTs_carry_only_content_type` (same body, opened through the tray); pick-time refusals
  (same); drop on document → `Dropping_files_anywhere_opens_the_tray_and_takes_them`; worker
  failure → `A_failed_photo_holds_nothing_up_and_shows_Retry_and_Remove`; restart without master
  (same, from the tray); discard modal → `Done_with_failed_photos_drops_them_and_says_how_many`;
  no-JS notice → `Without_scripting_the_workspace_says_it_needs_JavaScript`; main thread responsive (same).
- `LocalSignInBrowserTests` [1] (CP-E): the landing part stays; the "library opens" part
  (`/admin/library`, title "Library", breadcrumb, `section.lib`) → `/admin` 200, title "All photos",
  one `admin-workspace`, one `.ws-collections`.
- `LayoutConsistencyBrowserTests.Every_admin_page_type_draws_the_same_chrome` (CP-D, it loads
  `/admin/upload`) → `WorkspaceLayoutBrowserTests`. `Every_public_page_type_draws_the_same_chrome` U.
