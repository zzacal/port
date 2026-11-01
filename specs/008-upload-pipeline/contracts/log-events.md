# Log Event Contract: 008 (Constitution IV.1, IV.2, IV.4; FR-037–FR-040)

Templates are verbatim. Executors MUST NOT rename fields, because `evidence/p95.sh` parses them.
Output goes to the JSON console log (existing `AddJsonConsole`).

| Event template | Emitted by | Fields |
|---|---|---|
| `http.request {method} {route} {status} {duration_ms} {db_reads}` | existing `RequestTimingMiddleware` | unchanged; route = pattern, e.g. `/admin/upload/items/{itemId}/complete` |
| `op.timing {op} {duration_ms} {outcome}` | existing `OpTiming.Log` / `OpTimer.Time` | `outcome` ∈ `ok`, `error`, `rejected` |
| `upload.rejected {batch} {item} {file} {reason}` | validation and master check | `file` = staged name (`master.jpg`, `640.webp`, `placeholder`); `reason` = a fixed server string (never a metadata value) |
| `upload.confirmed {batch} {count} {duration_ms}` | confirm | |
| `upload.discarded {batch} {count}` | discard | |
| `upload.cleanup_failed {key} {error}` | any best-effort delete | `error` = exception type name only |
| `upload.timings_dropped {item} {count}` | complete | browser timings refused by the name/range filter |

`op` names used by 008:

| op | Where | SC |
|---|---|---|
| `s3.presign` | every permission (U3, U4, U8) | SC-006 (p95 ≤ 200 ms) |
| `s3.head`, `s3.get`, `s3.get-range`, `s3.list`, `s3.copy`, `s3.delete` | ImageStore | FR-038 |
| `db.batch.read`, `db.batch.create`, `db.batch.update`, `db.image.upsert` | batch store, confirm | FR-038 |
| `upload.master-check` | U4 and the re-check inside U7 | FR-038 |
| `upload.validate` | U7, whole photo: master re-check + every size + placeholder | SC-007 (p95 ≤ 2000 ms) |
| `upload.confirm.item` | U10, per photo | FR-038 |
| `upload.confirm` | U10, whole batch | SC-008 (≤ 60 000 ms for 40) |
| `browser.codec-init`, `browser.decode-orient`, `browser.resize.{w}`, `browser.encode.avif.{w}`, `browser.encode.webp.{w}`, `browser.placeholder`, `browser.total` | U7, one event per reported timing, `outcome` `ok` | SC-009 (`browser.total`, p95 ≤ 60 000 ms, owner live run), FR-039 |

SC-010 (the upload page with a 100-photo batch, p95 ≤ 500 ms) is read from `http.request` with
route `/admin/upload` and method GET.

`evidence/p95.sh` (task) takes a captured log and prints n, p95 and PASS/FAIL per SC row above. It
must FAIL on an empty log and on a log with zero matching events (orchestration rule: test the
evidence script on synthetic logs first).
