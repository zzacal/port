# HTTP contract: Focused Photo Edit Screen

All routes are on the protected `/admin` group (owner policy + `AdminAntiforgeryFilter`).
Unchanged routes are not listed. Log events: every request emits `http.request {method} {path}
{status} {duration_ms} {db_reads}`; each read emits `op.timing {op} {duration_ms} {outcome}`.

## L6 GET|HEAD /admin/library/images/{iid} (new)

| Input | Rule |
|---|---|
| `{iid}` | `OpaqueId.IsValid`, else 404 (no reads). |
| `page` | `PageOf` (missing, non-numeric or < 1 → 1). Not clamped against the library's page count; the library clamps when the owner goes back. |
| `saved` | `"1"` → status line "Saved" and panel Saved state. Anything else ignored. |

Responses:
- 200: the edit screen (contracts/components.md P1). Reads: `db.library.page` ×1,
  `db.galleries.list` ×1, `db.site.order` ×1 (db_reads = 3).
- 404: unknown or tombstoned photo; empty body, as the other `{iid}` routes (db_reads = 1).
- Signed out: the group's existing challenge (302 to sign-in), nothing about the photo rendered.

## L2 POST /admin/library/images/{iid} (changed landing only)

Same fields, validation, service call (`ImageService.SavePanelAsync`) and status codes as today.

| Outcome | Before | After |
|---|---|---|
| Ok | 303 `/admin/library?page={page}&open={iid}&saved=1` | 303 `/admin/library/images/{iid}?page={page}&saved=1` |
| Photo not found | 404 library re-render, "That photo no longer exists." | unchanged |
| Refused (422 / 409 / 404 gallery / 400) | library re-render with the panel open, reason, entries kept (pending tokens on 422 only) | **edit screen** re-render with the same status code, reason and kept entries (pending tokens on 422 only) |

## L5 POST /admin/library/images/{iid}/delete

Unchanged. The edit screen's `lib-del-one` form posts `page = ReturnPage`.

## L1 GET /admin/library (changed)

`open` is no longer read: `?open=` (any value) renders the plain library, 200, with the same
four reads as without it. Every tile's `pa-group` and `pa-edit` href is
`/admin/library/images/{id}?page={page}`; no tile has `aria-current`.
