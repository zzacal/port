# Implementation Plan: Admin Workspace

**Branch**: `020-admin-workspace` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/020-admin-workspace/spec.md`

## Summary

The admin becomes one screen at `/admin`: a collections list (All photos, Not in a gallery, each
gallery in home order), the photos of the open collection, and a details panel for whatever is
selected. `/admin/unplaced` and `/admin/g/{gid}` are the other two collection addresses. The page
is server-rendered; switching collection fetches a server-rendered fragment and swaps it in, so an
upload keeps running. Selecting fills the panel from data already on the tiles (no request).

Writes are eleven JSON routes under `/admin/api/`, each a thin call to the service method the old
form route called, so rules, refusals and purge sets do not change. Upload moves into a tray in the
photo view. Its item routes are unchanged; the batch-wide confirm and discard are replaced by a
per-photo join (U12) and a clear (U13), and an upload set can carry one destination gallery
(constitution 6.3.0, II.5a to 5d).

The old Upload, Library, photo edit, Galleries and builder pages, their form routes, `library.js`,
`reorder.js` and 13 components (all the icon controls among them) are deleted. The `/ui` catalogue
gains 8 entries and loses 13 (40 → 35).

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), Razor components, vanilla ES modules.

**Primary Dependencies**: none new.

**Storage**: MongoDB and R2 as today. One document shape changes: the open upload batch gains
`destination` and `added`, and items gain the state `joining` (data-model.md).

**Testing**: xUnit (`LibraryHost`/`UploadHost` HTTP tests, `Render.Html<T>` component tests),
Playwright fixtures (`LibraryBrowserFixture`, `UploadBrowserFixture`), Category=Evidence timing tests.

**Target Platform**: Azure App Service Linux behind Cloudflare; local development stack.

**Project Type**: server-rendered web app (admin area).

**Performance Goals**: SC-004 (details within 100 ms, browser); SC-005/SC-006 (p95 ≤ the replaced
request × 1.10 + 1 ms, from logs); SC-013 (each join ≤ 2 s, from logs).

**Constraints**:

- no new package, design token or `style=` attribute; one `<script>` on the page;
- public output byte-identical (SC-011); no admin script or markup on a public page;
- U3 to U9 keep their request and response shapes; `UploadFlowTests` passes unmodified (SC-012);
- existing tests unmodified except the stated changes (contracts/test-impact.md).

**Scale/Scope**: 5,000 photos and 50 galleries (design record); chunks of 100 tiles; bulk actions
of at most 100 photos; upload sets of at most 100 photos. About 60 of 144 test files change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.* (Constitution 6.3.0)

| Principle | Status | Note |
|---|---|---|
| I.3 owner-only, I.6 CSRF | PASS | Every new route is in the protected `/admin` group with `AdminAntiforgeryFilter`; JSON writes send `X-CSRF-TOKEN`. Access and CSRF facts cover W1 to W4, A1 to A11, U12, U13 [SAFETY]. |
| II.2, II.6 visibility | PASS | No visibility rule changes. A6 keeps the G4 handler's branch order (empty-private refusal first) [SAFETY]. |
| II.5, 5a to 5d aimed upload | PASS | 5a: the tray names the destination and shows the public sentence as plain text; U3 refuses a public destination without `visibleToVisitors` (409). 5b: no destination → no gallery call, no edge work. 5c: U12 never writes visibility. 5d: U12 runs only from `succeeded`/`joining`. Each is a [SAFETY] fact in `UploadJoinTests` plus a browser fact for the visible sentence. |
| III.1, III.2, III.5 to III.7 | PASS | Validation (U4, U7) is untouched. Join copies with if-match on the validated ETags; a refused copy publishes nothing of that photo (III.6) [SAFETY]. Masters stay private. |
| III.9 storage credential | PASS | No new credential or presign use. |
| IV.1, IV.2, IV.4 timing | PASS | `http.request` on every route; read ops reuse existing names; new `upload.join`, `library.bulk.remove` (contracts/log-events.md). SC-005, SC-006, SC-013 from logs; pre-change capture is a Setup task. |
| IV.10, IV.18 purge and warm | PASS | Gallery changes go through the existing `OperationScope` (A-routes and the join's `AddAsync`). Admin responses stay `no-store`. |
| IV.22, V.3 | PASS | Public pages unchanged. The admin requires scripting (allowed); `<noscript>` says so. |
| V.1 traces to spec | PASS | FR-001 to FR-047. |
| V.2 minimal deps | PASS | None added; htmx not vendored (research R-01). |
| V.4 proportional effort | PASS | Additions table below. |
| VI official CLIs | N/A | No scaffolding. |
| VII.2 design is source of truth | DEVIATION-handled | D-23 superseded by a new D-27; D-21, D-11, L-01 to L-06, E-08, the upload diagram and the II.5 traceability row edited by a scripted draft edit + assemble/validate in this feature (research R-14). |
| VIII cost | PASS | No new metered resource. Up to 100 purge calls per upload set into a public gallery (decisions row 15). |
| IX.1 to IX.3 shared container | PASS | `AdminLayout` (rewritten) is the one admin layout and owns the frame; `WorkspacePage` adds no frame CSS; the sign-in page uses its bare mode. IX.1's example "owner bar" is reworded by a PATCH (6.3.1) in the removal step. |
| IX.4 measured test | PASS | `WorkspaceLayoutBrowserTests`: six contexts at 390 and 1440 px (FR-038). |
| Platform Constraints | PASS | Server-rendered HTML; custom elements `<admin-workspace>`, `<upload-tray>` for interactivity. |

Additions beyond the literal request (V.4):

| Addition | Benefit | Cost | Kept |
|---|---|---|---|
| Fragment swap instead of page loads | uploads survive navigation (FR-033) | one `swap` function, a header on three routes | yes (required) |
| `visibleToVisitors` check at U3 | II.5a enforceable on the server | one field, one branch | yes |
| `joining` state and claim | no race between a join and Remove | one state, one atomic update | yes (safety) |
| `added` ids on the upload set | New marks and the count survive a reload (FR-035) | one array field | yes |
| Move earlier/later, Move up/down buttons | reorder by touch and keyboard (FR-023) | four buttons, no new route | yes |
| Single-tile fragment W4 | a joined photo appears without re-reading the grid | one route, one read | yes |
| Grouped purges for big uploads | fewer purge calls | timer + second write path | no (dropped; decisions row 15) |
| Lookup route for a deep `?photo=` reload | faster reload in a rare case | a route | no (dropped; decisions row 20) |

**AMENDED (tasks)**: tasks.md corrections C-01 to C-19 override the contract text they name; each is folded into the contract in place and logged in decisions.md rows 23 to 41.

Post-design re-check: unchanged. Everything is PASS, N/A or DEVIATION-handled. No MUST is crossed;
Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/020-admin-workspace/
├── plan.md  research.md  data-model.md  quickstart.md  decisions.md
├── contracts/
│   ├── http.md           # W1–W4, A1–A11, U2/U3 changed, U12, U13, removed routes
│   ├── components.md     # K1–K12, removed list, catalogue, scripts C12–C14
│   ├── log-events.md     # ops, lines, SC readings
│   └── test-impact.md    # classes U/T/R/W/G/N per file, transport table
├── evidence/
│   ├── constitution-6.3.0-edit.py
│   ├── probe-plan.sh  test-impact-hits.sh  test-impact-hits.txt
│   └── (tasks add: capture-pre.sh, gate.sh, p95.sh, compare-public.sh, non-vacuity.sh, design-record-edit.py)
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Core/Images/Sizes.cs                     # + ForFill()
src/Gallery.Web/
├── Workspace/                                       # NEW
│   ├── WorkspaceEndpoints.cs                        # W1–W4, fragments
│   ├── WorkspaceApi.cs                              # A1–A11
│   ├── WorkspaceReader.cs                           # counts aggregate, keyset chunk, gallery members
│   ├── WorkspaceModels.cs  Messages.cs
├── Admin/AdminEndpoints.cs                          # H4 shell removed; MapWorkspace
├── Admin/Pages/WorkspacePage.razor(.css)            # NEW; the six old pages deleted
├── Library/
│   ├── GalleryService.cs                            # + RemoveManyAsync
│   ├── LibraryReader.cs                             # GalleriesAsync kept; page/photo reads move out (AMENDED (tasks) C-16: Live, TileFields become internal static)
│   └── LibraryEndpoints.cs                          # DELETED
├── Upload/
│   ├── UploadEndpoints.cs                           # U1, U10, U11 removed; U2, U3 changed; U12, U13
│   ├── ConfirmService.cs                            # AMENDED (tasks) C-02: keeps its name; gains JoinAsync (per-item body moved)
│   ├── BatchStore.cs  EffectiveState.cs             # claim, added, destination, clear; joining
│   └── UploadPageModel.cs                           # DELETED
├── Data/BatchDocuments.cs                           # destination, added, joining
├── Components/                                      # K1–K11; 13 components + Icons.cs deleted
├── Ui/                                              # catalogue: 8 pages added, 13 removed, 4 changed
└── wwwroot/admin-assets/
    ├── workspace.js                                 # NEW (C12)
    ├── upload.js                                    # reworked (C13)
    └── library.js, reorder.js                       # DELETED

specs/001-architecture-stack-design/draft/*.md       # D-27, D-23, D-21, D-11, L-01–L-06, E-08 (R-14)
specs/006-public-site-local/evidence/ui-baseline/    # +8, −13, 4 regenerated (R-11)
specs/008…019/spec.md                                # superseded requirements annotated in place (FR-047)
.specify/memory/constitution.md                      # 6.3.1 PATCH: IX.1 example

tests/Gallery.Web.Tests/                             # contracts/test-impact.md
```

**Structure Decision**: one web project. The workspace's reads, routes and messages get their own
folder; the services stay where they are and are called, never copied.

## Complexity Tracking

None.
