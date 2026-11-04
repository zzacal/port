"""Constitution 5.0.0 -> 6.0.0: the US$7 Azure budget alert is withdrawn (owner, 012).

Asserts each old string's count before writing anything. Also annotates prior specs in place and
updates 012 FR-020 / SC-008 / Clarifications.
"""
import pathlib, sys

root = pathlib.Path(__file__).resolve().parents[3]
con = root / ".specify/memory/constitution.md"
s001 = root / "specs/001-architecture-stack-design/spec.md"
s012 = root / "specs/012-production-infrastructure/spec.md"

header_end = "-->\n"
new_header = """<!--
Sync Impact Report
- Version change: 5.0.0 → 6.0.0 (MAJOR)
- Rationale: owner decision (2026-11-02, feature 012-production-infrastructure): "No alert
  needed." VIII.1 said "An Azure budget alert MUST fire at US$7 per month." That guarantee is
  withdrawn: nothing notifies the owner when Azure spend rises. MAJOR per Governance (a guarantee is
  removed). What stays, and is now the entire protection against overspend:
  - the US$10 metered cap as a governance limit (exceeding it still requires an amendment)
  - VIII.3: every design projects each metered cost and the total MUST stay under US$7 (the old
    alert level, kept as a planning threshold)
  - VIII.4: every metered service that offers a hard limit has one set (the Application Insights
    daily ingestion cap is the only Azure one)
  - the fixed-price plan (VIII.1), which has no metered component
  Reversal: one Azure budget on the site's resource group restores the alert; no code change.
- Modified principles: VIII (rules 1 and 3). Added/removed sections: none.
- Templates requiring updates: none (templates read the constitution at runtime).
- Prior specs swept: 001 FR-006, the cost user story and the $7 edge case annotated AMENDED in place;
  012 FR-020 and SC-008 rewritten (this feature).
- Deferred TODOs: none. The design record (D-18, section 8 "Configured limits", E-19) is updated
  by the 012 tasks as a VII.2 amendment.
- History: 1.0.0 initial ratification. 1.1.0 perceived performance, private = unlisted. 2.0.0
  client-generated derivatives with server validation. 3.0.0 Azure Blob replaced R2, masters kept,
  $10 metered cap (2026-10-27). 4.0.0 images on Cloudflare R2, managed-identity guarantee
  withdrawn, III.9 added (2026-10-30). 4.1.0 III.10 seeded dev content under two-reader
  validation; IV.19 clarified (2026-10-31). 4.2.0 II.6 no empty galleries shown; publish
  requires an image (2026-10-31). 4.3.0 canonical remote (2026-11-01). 4.4.0 IX visual
  consistency through shared containers (2026-11-01). 4.5.0 home page at `/`, galleries listing
  at `/g`; IV.9 home page makes no read (2026-11-02). 5.0.0 fixed hosting ceiling B1 -> B2
  (2026-11-02).
-->
"""

rows_con = [
    ("   US$10 per month. An Azure budget alert MUST fire at US$7 per month. Exceeding the cap requires\n   an amendment. Free tiers MUST be used wherever one covers the need.",
     "   US$10 per month. Exceeding the cap requires an amendment. No spending alert is required\n   (withdrawn in 6.0.0). Free tiers MUST be used wherever one covers the need.", 1),
    ("   scale. The projected total MUST stay under the VIII.1 alert threshold.",
     "   scale. The projected total MUST stay under US$7 per month.", 1),
    ("**Version**: 5.0.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02",
     "**Version**: 6.0.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02", 1),
]

TAG = " (**AMENDED by feature 012-production-infrastructure**, constitution 6.0.0: the $7 alert is withdrawn; $7 remains the projection threshold.)"
rows_001 = [
    ("against the VIII.1 cap ($10, with an alert at $7).",
     "against the VIII.1 cap ($10, with an alert at $7)." + TAG, 1),
    ("or metered spend crosses the $7 alert. The design defines",
     "or metered spend crosses the $7 alert" + TAG + ". The design defines", 1),
    ("the projected metered total against the $10 cap and the $7 alert,",
     "the projected metered total against the $10 cap and the $7 alert" + TAG + ",", 1),
]

rows_012 = [
    ("""- **FR-020**: A spending alert MUST notify the owner at US$7 per month of metered spend and again
  at US$10 (VIII.1, D-18), scoped to this site's resource groups, not the whole subscription, so
  other spend in the subscription neither triggers it nor hides inside it. Every metered service
  in production that offers a hard limit MUST have one set inside the cap (VIII.4).""",
     """- **FR-020**: No spending alert is configured (owner, 2026-11-02; constitution 6.0.0 withdrew the
  VIII.1 alert). Every metered service in production that offers a hard limit MUST have one set
  inside the cap (VIII.4): the Application Insights daily ingestion cap.""", 1),
    ("""  monthly metered spend stays under US$7, and the alert and ingestion limit
  are in place before the first deploy.""",
     """  monthly metered spend stays under US$7, and the ingestion limit is in place before the first
  deploy.""", 1),
    ("""  workflow written in this feature uses exactly these names. Applied to FR-024.""",
     """  workflow written in this feature uses exactly these names. Applied to FR-024.
- Q: Spending alert and optional extras? → A: no alert ("No alert needed"); constitution amended to
  6.0.0 before /speckit-tasks. Keep all three optional extras: production purge and warm
  (FR-011), saved hosted-log queries (FR-019), production startup guard (FR-015). Applied to
  FR-020, SC-008 and the severable-seams list.""", 1),
    ("""  2. Edge page purge and warm against production (FR-011)""",
     """  (Owner 2026-11-02: items 2–4 kept; none is severable any more.)
  2. Edge page purge and warm against production (FR-011)""", 1),
]

files = {con: rows_con, s001: rows_001, s012: rows_012}
texts = {p: p.read_text() for p in files}
bad = []
if not texts[con].startswith("<!--\nSync Impact Report\n"):
    bad.append("header")
for p, rows in files.items():
    bad += [(p.name, o[:50], texts[p].count(o)) for o, _, n in rows if texts[p].count(o) != n]
if bad:
    print("ABORT", bad); sys.exit(1)

c = texts[con]
texts[con] = new_header + c[c.index(header_end) + len(header_end):]
for p, rows in files.items():
    t = texts[p]
    for o, nw, _ in rows:
        t = t.replace(o, nw)
    p.write_text(t)
print("OK constitution 6.0.0; 001 annotated x3; 012 updated x4")
