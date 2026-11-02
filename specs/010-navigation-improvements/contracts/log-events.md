# Contract: Log Events (010)

No new event types and no renamed fields (Constitution IV.1, IV.2, IV.4; FR-016). The evidence
script parses the existing templates verbatim:

| Event template | Emitted by | 010 use |
|---|---|---|
| `http.request {method} {route} {status} {duration_ms} {db_reads}` | `RequestTimingMiddleware` | SC-006: `route` is the pattern (`/`, `/g/{id}/{slug}`, `/g/{id}/{slug}/{imageId}`) |
| `op.timing {op} {duration_ms} {outcome}` | `OpTimer.Time` | public reads `db.site.read`, `db.gallery.read`, `db.images.read` (unchanged names); `db.site.project` now also covers the `nav` writes |

Unchanged rules this feature must keep, each guarded by a test:

- The home page logs `db_reads=1`; gallery and photo pages `db_reads ≤ 2` (FR-015). `nav` rides on
  the existing `db.gallery.read`; there is no new public read.
- `Server-Timing` stays on every public response (IV.3).
- `db.site.project` keeps its name; its `nav` updates happen inside the same timed call, so the
  009 SC-003 owner-write bound (p95 500 ms) still covers them. The 009 `LibraryTimingEvidenceTests`
  stays unmodified and passing.

## SC-006 evidence (`evidence/p95.sh`)

Input: `evidence/nav-timings.log`, one formatted message per line (as 009's
`library-timings.log`), or the JSON console log (`.Message` via jq), auto-detected as in 009.

Rows (nearest-rank p95, `ceil(0.95 n)`):

| Row | Filter | Pass |
|---|---|---|
| SC-006 home reads | `http.request GET / 200` | every line `db_reads == 1`, n ≥ 100 |
| SC-006 gallery reads | `http.request GET /g/{id}/{slug} 200` | every line `db_reads ≤ 2`, n ≥ 100 |
| SC-006 photo reads | `http.request GET /g/{id}/{slug}/{imageId} 200` | every line `db_reads ≤ 2`, n ≥ 100 |
| SC-006 home time | same as home reads | p95 `duration_ms` < 50 |
| SC-006 gallery time | same | p95 < 50 |
| SC-006 photo time | same | p95 < 50 |

n below 100 on any row fails (an unexercised route or a format mismatch, not a fast app). A
missing, empty or unreadable log fails. Exit status = number of failed rows.

The script is self-tested before the real capture on synthetic logs in `evidence/p95-selftest/`:
one passing plain log, one passing JSON log, one with a home line at `db_reads 2` (must fail
only that row), one with a photo p95 of 60 ms (must fail only that row), one with 99 gallery
lines (must fail only that row), and an empty file (must fail).
