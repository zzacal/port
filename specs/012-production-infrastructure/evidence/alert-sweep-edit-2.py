"""Second pass: purge the alert concept from 012 spec prose (6.0.0). Asserts counts."""
import pathlib, sys
p = pathlib.Path(__file__).resolve().parents[1] / "spec.md"
s = p.read_text()
rows = [
("front of both, hosted logs, cost alerts, and a repeatable way",
 "front of both, hosted logs, a log ingestion cap, and a repeatable way", 1),
("""The owner can query the production logs for request timings and named operation timings, and
gets an alert before monthly spend reaches the cost cap.

**Why this priority**: The constitution makes every time bound verifiable from logs (IV.4) and
caps metered spend (VIII). Without hosted logs and the alert, production silently loses both
guarantees, but visitors are not affected on day one.

**Independent Test**: Load a few production pages, then run the committed log queries and get
per-route timing results. Inspect the configured spending alerts and the log ingestion limit.""",
"""The owner can query the production logs for request timings and named operation timings, and
log volume can never bill past the free allowance.

**Why this priority**: The constitution makes every time bound verifiable from logs (IV.4) and
requires a hard limit on every metered service that offers one (VIII.4). Without hosted logs and
the ingestion cap, production silently loses both, but visitors are not affected on day one.

**Independent Test**: Load a few production pages, then run the committed log queries and get
per-route timing results. Inspect the log ingestion limit.""", 1),
("""2. **Given** metered spend reaches the alert threshold, **When** the threshold is crossed,
   **Then** the owner is notified.
3. **Given** a sudden flood""",
"""2. ~~**Given** metered spend reaches the alert threshold, **When** the threshold is crossed,
   **Then** the owner is notified.~~ Removed: no spending alert (owner, constitution 6.0.0).
3. **Given** a sudden flood""", 1),
("  (US$10, alert at US$7) is unchanged.", "  (US$10) is unchanged; the US$7 alert was later withdrawn (6.0.0).", 1),
("the projected total MUST stay under the alert threshold (VIII.3).", "the projected total MUST stay under US$7 (VIII.3).", 1),
("  buckets, logs, alerts), changing nothing,", "  buckets, logs), changing nothing,", 1),
("  log workspace and spending alerts that together serve", "  log workspace and its ingestion cap that together serve", 1),
]
bad = [(o[:40], s.count(o)) for o, _, n in rows if s.count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for o, n, _ in rows: s = s.replace(o, n)
p.write_text(s); print("OK", len(rows))
