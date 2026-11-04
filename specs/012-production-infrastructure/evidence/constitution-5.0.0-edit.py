"""Constitution 4.5.0 -> 5.0.0: one Basic B2 plan replaces one Basic B1 plan (owner, 012).

Asserts each old string's count before writing anything. Also annotates 001 SC-004 in place.
"""
import pathlib, sys

root = pathlib.Path(__file__).resolve().parents[3]
con = root / ".specify/memory/constitution.md"
s001 = root / "specs/001-architecture-stack-design/spec.md"

OLD_HEADER_START = "<!--\nSync Impact Report\n"
header_end = "-->\n"

new_header = """<!--
Sync Impact Report
- Version change: 4.5.0 → 5.0.0 (MAJOR)
- Rationale: owner decision (2026-11-02, feature 012-production-infrastructure): production runs on
  one Azure App Service Basic B2 (Linux) plan, created by the owner, "for double the beef". VIII.1
  said "Recurring fixed hosting cost MUST NOT exceed one Azure App Service Basic B1 (Linux) plan."
  and the hosting Platform Constraint said "Azure App Service (Linux, B1)". The fixed-cost ceiling
  is redefined from B1 (US$13.14/month list, Linux) to B2 (US$26.28/month list, Linux), so the
  guarantee that fixed hosting stays at the B1 price is withdrawn. MAJOR per Governance
  ("redefined"). Unchanged: the US$10 metered cap, the US$7 alert, free tiers, free TLS, the
  hard-limit rule (VIII.2–VIII.4), and every other principle. Downgrading to B1 stays allowed.
- Modified principles: VIII (rule 1). Modified sections: Platform Constraints (Hosting bullet).
  Added/removed sections: none.
- Templates requiring updates: none (templates read the constitution at runtime).
- Prior specs swept: 001 SC-004 ("no more than one Basic B1 plan") annotated AMENDED in place;
  001's Clarification and Assumptions bullets that say B1 are historical and left as is. 012
  FR-002 is the requirement this amendment discharges.
- Deferred TODOs: none. The design record (D-01, D-18, section 8 cost table, and every entry that
  says B1) is updated by the 012 plan as a VII.2 amendment.
- History: 1.0.0 initial ratification. 1.1.0 perceived performance, private = unlisted. 2.0.0
  client-generated derivatives with server validation. 3.0.0 Azure Blob replaced R2, masters kept,
  $10 metered cap (2026-10-27). 4.0.0 images on Cloudflare R2, managed-identity guarantee
  withdrawn, III.9 added (2026-10-30). 4.1.0 III.10 seeded dev content under two-reader
  validation; IV.19 clarified (2026-10-31). 4.2.0 II.6 no empty galleries shown; publish
  requires an image (2026-10-31). 4.3.0 canonical remote (2026-11-01). 4.4.0 IX visual
  consistency through shared containers (2026-11-01). 4.5.0 home page at `/`, galleries listing
  at `/g`; IV.9 home page makes no read (2026-11-02).
-->
"""

rows_con = [
    ("1. Recurring fixed hosting cost MUST NOT exceed one Azure App Service Basic B1 (Linux) plan.",
     "1. Recurring fixed hosting cost MUST NOT exceed one Azure App Service Basic B2 (Linux) plan.", 1),
    ("- Hosting: Azure App Service (Linux, B1). Origin MUST accept traffic only from Cloudflare.",
     "- Hosting: Azure App Service (Linux, one Basic plan no larger than B2). Origin MUST accept\n"
     "  traffic only from Cloudflare.", 1),
    ("**Version**: 4.5.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02",
     "**Version**: 5.0.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02", 1),
]

rows_001 = [
    ("- **SC-004**: The projected fixed cost in the cost section is no more than one Basic B1 plan.",
     "- **SC-004**: The projected fixed cost in the cost section is no more than ~~one Basic B1 plan~~"
     " one Basic B2 plan (**AMENDED by feature 012-production-infrastructure**, constitution 5.0.0).", 1),
]

c = con.read_text()
d = s001.read_text()
bad = []
if not c.startswith(OLD_HEADER_START) or c.count(header_end) < 1:
    bad.append("header")
bad += [(o[:50], c.count(o)) for o, _, n in rows_con if c.count(o) != n]
bad += [(o[:50], d.count(o)) for o, _, n in rows_001 if d.count(o) != n]
if bad:
    print("ABORT", bad); sys.exit(1)

c = new_header + c[c.index(header_end) + len(header_end):]
for o, nw, _ in rows_con:
    c = c.replace(o, nw)
for o, nw, _ in rows_001:
    d = d.replace(o, nw)
con.write_text(c)
s001.write_text(d)
print("OK constitution 5.0.0 (3 edits + header), 001 SC-004 annotated")
