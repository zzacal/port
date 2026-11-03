"""Clarification 2026-11-02: owner recreated the Web App as Code on a B2 plan.

Each row: (old, new, expected count). Aborts without writing if any count differs.
"""
import pathlib, sys

p = pathlib.Path(__file__).resolve().parents[1] / "spec.md"
s = p.read_text()

rows = [
("""  check. The design record records the change in the plan (VII.2).
""",
"""  check. The design record records the change in the plan (VII.2).
- Q: Plan size? → A: B2, "for double the beef". The owner recreated the app (superseding the
  Container/B1 details above): resource group `portfolio`, app `portfolio-web`, new plan
  `portfolio-plan`, West US, Linux, Publish Code, runtime .NET 10 (LTS), Basic B2 (2 cores,
  3.5 GB, US$26.28/month), secure unique default hostname on, basic authentication disabled,
  continuous deployment not enabled, Application Insights `portfolio-appinsights` (West US)
  enabled. Told once: the origin decodes no pixels (III.8) and anonymous pages are edge-cached, so
  B1 is not expected to be the bottleneck; scaling down to B1 is a plan setting with no rebuild.
  B2 exceeds constitution VIII.1 (one B1 plan), so FR-002 now requires an amendment before
  /speckit-plan. Applied to FR-001, FR-002, FR-021 and SC-008.
""", 1),

("""- **FR-001**: The production app MUST run on one Azure App Service Linux B1 plan (Platform
  Constraints, D-01) in the subscription""",
"""- **FR-001**: The production app MUST run on one Azure App Service Linux B2 plan
  (`portfolio-plan`, app `portfolio-web`, West US) in the subscription""", 1),

("""- **FR-002**: If the chosen host differs from the constitution's Platform Constraints (Azure App
  Service Linux B1) or breaks the VIII.1 fixed-cost ceiling, the constitution MUST be amended
  through /speckit-constitution before /speckit-plan, and D-01, D-03, D-05, D-12, D-18, D-24 and
  D-25 updated in the design record in this feature (VII.2).""",
"""- **FR-002** (blocking, before /speckit-plan): constitution VIII.1 ("one Azure App Service Basic
  B1 (Linux) plan") and the Platform Constraints hosting bullet ("Azure App Service (Linux, B1)")
  MUST be amended through /speckit-constitution to allow one Basic B2 plan. The metered cap
  (US$10, alert at US$7) is unchanged. In this feature the design record MUST then update D-01
  (B2), D-18 and section 8 (fixed cost), D-24 (withdrawn: production made by hand) and D-25 (deploy
  identity), and every other entry that says B1 (VII.2).""", 1),

("""- **SC-008**: Projected monthly metered spend stays under US$7,""",
"""- **SC-008**: Fixed hosting is one B2 plan (US$26.28/month list price, West US), projected
  monthly metered spend stays under US$7,""", 1),
]

bad = [(o[:60], s.count(o), n) for o, _, n in rows if s.count(o) != n]
if bad:
    for b in bad: print("COUNT MISMATCH", b)
    sys.exit(1)
for o, nw, _ in rows:
    s = s.replace(o, nw)
p.write_text(s)
print(f"OK {len(rows)} edits")
