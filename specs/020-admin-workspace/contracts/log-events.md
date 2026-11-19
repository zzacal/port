# Log Events: 020 Admin Workspace

Field names and templates are fixed; evidence scripts parse them. `http.request {route} {status}
{duration_ms}` is emitted for every request as today (IV.1). **AMENDED (tasks) C-08**: the line is `http.request {method} {route} {status} {duration_ms} {db_reads}` with the route TEMPLATE (RequestTimingMiddleware.cs:46).

**AMENDED (tasks) C-17**: p95 rows read per-source pre logs (`evidence/pre-timings/*.log`), because L2 plain saves and L2 membership saves share one route template; `evidence/pre-timings.log` is their concatenation for quickstart's command.

## Reads (all `op.timing`, `isDbRead: true`, names reused so pinned lists stay valid)

| Op | Where | Note |
|---|---|---|
| `db.library.count` | W1 to W3 full, `main` (library), `chrome` | now one aggregate: total live photos and live photos in no gallery |
| `db.library.page` | W1/W2 chunk, `more`, W3 members, W4 | keyset on `{createdAt:-1,_id:-1}` |
| `db.gallery.load` | W3 full and `main` | |
| `db.galleries.list`, `db.site.order` | full documents and `chrome` | |

## Writes

A1 to A11 emit the events of the service calls they wrap, unchanged (`db.image.update`,
`library.bulk.add`, `library.bulk.delete`, `db.gallery.update`, `db.site.project`, `cache.evict`,
the edge queue's events, …). New:

| Op / line | Where | Fields |
|---|---|---|
| `op.timing library.bulk.remove` | A8 | duration, outcome |
| `op.timing upload.join` | U12, the whole call | duration, outcome |
| `upload.joined {batch} {item} {gallery} {public} {duration_ms}` | U12 success | `gallery` is the id or `-`; `public` is `true`/`false`/`-` |
| `upload.join-refused {batch} {item} {reason}` | U12, copy refused | reason `changed after validation` |
| `upload.destination-refused {batch} {gallery}` | U3, 409 `destination-public` | |
| `upload.cleared {batch} {dropped}` | U13 | |

Removed with their routes: `upload.confirm`, `upload.confirm.item`, `upload.confirmed`,
`upload.discarded`.

## Success criteria read from these

| SC | Read |
|---|---|
| SC-005 | p95 of `http.request` for W1 and W3 full documents and `main` fragments at 100 photos vs the captured library page (and builder) p95 × 1.10 + 1 ms |
| SC-006 | p95 of `http.request` for A1, A5, A7, A8, A9, A10 vs the captured p95 of the POST they replace × 1.10 + 1 ms |
| SC-013 | every `upload.join` duration for the nine good photos ≤ 2000 ms; nine `upload.joined` lines and none for the forced failure |
