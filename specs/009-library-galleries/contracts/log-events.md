# Log Event Contract: 009 (Constitution IV.1, IV.2, IV.4; FR-049, FR-050)

Templates are verbatim; executors MUST NOT rename fields, because `evidence/p95.sh` parses them.
Output is the existing JSON console log. No event ever carries `Edge:ApiToken` or a storage
credential (FR-043, SC-011).

| Event template | Emitted by | Notes |
|---|---|---|
| `http.request {method} {route} {status} {duration_ms} {db_reads}` | existing middleware | route = pattern, e.g. `/admin/galleries/{gid}/order` |
| `op.timing {op} {duration_ms} {outcome}` | existing `OpTimer.Time` / `OpTiming.Log` | `outcome` ∈ `ok`, `error`, `refused`, `stale`, `skipped`; `OpTimer.Time` writes `ok`/`error`, a refused gate or stale reorder adds one `OpTiming.Log` line with `refused`/`stale` |
| `edge.purge {op} {mode} {count} {duration_ms} {outcome} {addresses}` | `EdgeWorker`, once per provider call | `mode` ∈ `log-only`, `dev-zone`, `recorder`; `count` ≤ 100; `addresses` = space-joined URLs; `outcome` ∈ `ok`, `skipped`, `failed`, `rate-limited` |
| `edge.warm {op} {mode} {count} {duration_ms} {outcome}` | `EdgeWorker`, once per warm batch | |
| `edge.lag {op} {duration_ms}` | `EdgeWorker`, after the warm of one `EdgeWork` | time from enqueue to warm done (SC-005) |
| `edge.dropped {op} {count}` | `IEdgeCache.Enqueue` when the queue is full | addresses dropped, bounded TTL is the fallback |
| `owner.action {action} {target} {count} {detail}` | services | `action` ∈ `image.delete`, `gallery.delete`, `gallery.publish`, `gallery.unpublish`, `seeded.remove`; `detail` = e.g. `leftovers=1 galleries=2` |
| `library.delete_leftover {image} {bucket} {key} {error}` | `ImageDeleteService` | `error` = exception type name only |

`op` names:

| op | Where | SC |
|---|---|---|
| `db.library.page`, `db.library.count`, `db.galleries.list`, `db.site.order` | L1 | SC-002 |
| `db.image.update`, `db.image.tombstone`, `db.image.delete`, `db.images.shared` | image services | SC-003 |
| `db.gallery.create`, `db.gallery.update`, `db.gallery.delete`, `db.gallery.load` | gallery services (`db.gallery.load` is the owner read, kept apart from the public `ContentReader`'s `db.gallery.read`) | SC-003 |
| `db.membership.repair` | `MembershipRepair` | |
| `db.site.project` | `SiteProjector` (read + write) | SC-007 |
| `s3.delete` | existing `ImageStore` | |
| `library.image.delete` | one image delete, end to end | |
| `library.bulk.add`, `library.bulk.delete` | L3, L4 whole request body | SC-003 |
| `cache.evict` | output-cache eviction, per operation | |
| `edge.purge.call`, `edge.warm.request` | each provider HTTP call | SC-005 |
| `maintenance.remove-seeded` | removal command total | |

SC mapping for `evidence/p95.sh`:
- SC-002: `http.request` GET `/admin/library` with a 100-image page, p95 `duration_ms` ≤ 500.
- SC-003: `http.request` POST on every 009 route, p95 ≤ 500; plus no `edge.purge`/`edge.warm`
  event's start falls inside a request (checked in a test with the recorder gate, not from logs).
- SC-005: `edge.lag` max ≤ 60000.
The script fails on an empty log and on a log with zero matching events; it is run first on
synthetic logs (one per SC, plus an empty one) before the real capture.
