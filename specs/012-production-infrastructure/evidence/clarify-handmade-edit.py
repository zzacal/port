"""Clarification 2026-11-02 (owner creates Azure resources by hand; no IaC).

Each row: (old, new, expected count). Aborts without writing if any count differs.
"""
import pathlib, sys

p = pathlib.Path(__file__).resolve().parents[1] / "spec.md"
s = p.read_text()

rows = [
("""  Assumptions (accepted risk: tenant administrators can see the site).
""",
"""  Assumptions (accepted risk: tenant administrators can see the site).
- Q: Infrastructure as code? → A: No. The owner creates production by hand ("I'm just making it
  by hand") and reported the Web App created: resource group `portfolio`, app `portfolio-web`,
  plan `ASP-portfolio-9863`, West US, Linux, Basic B1 (US$13.14/month), secure unique default
  hostname on, basic authentication (publish profiles) disabled, Application Insights not enabled,
  continuous deployment not set up, and publish type Container with the placeholder image
  `mcr.microsoft.com/appsvc/staticsite:latest`. (Supersedes the OpenTofu part of the previous
  bullet.) D-24 is withdrawn for this feature: User Story 5, FR-022, FR-023, SC-007 and the
  Infrastructure description entity are rewritten as a committed runbook plus a read-only settings
  check. The design record records the change in the plan (VII.2).
""", 1),

("""### User Story 5 - The owner can rebuild and update production repeatably (Priority: P2)

Every production resource is described in the repository. The owner previews a change before
applying it, applies it from their own machine, and can see when someone changed a setting by
hand. Code changes reach production through an automated pipeline: every push is built and tested,
and only `main` deploys, only after its tests pass.

**Why this priority**: The design already decided this (D-24, D-25, both on hold). It is what
makes production recoverable and keeps dashboard edits from drifting silently.

**Independent Test**: With production running, run a preview of the infrastructure description;
it reports no changes. Change one setting by hand in a dashboard; the preview reports it.

**Acceptance Scenarios**:

1. **Given** production was created from the repository, **When** the owner previews again with
   no edits, **Then** the preview reports zero changes.
2. **Given** a setting was changed by hand, **When** the owner previews, **Then** the drift is
   listed.
""",
"""### User Story 5 - The owner can rebuild production and see when it drifts (Priority: P2)

The owner creates production by hand, following a committed runbook that names every resource and
setting. A read-only check, run from the owner's machine, compares the live settings with the
runbook and lists every difference. Code changes reach production through an automated pipeline:
every push is built and tested, and only `main` deploys, only after its tests pass.

**Why this priority**: It is what makes production recoverable and keeps a dashboard edit (or a
setting the runbook forgot) from silently breaking a guarantee such as origin lock-down.

**Independent Test**: With production running, run the settings check; it reports no differences.
Change one checked setting by hand in a dashboard; the check reports it.

**Acceptance Scenarios**:

1. **Given** production was created by following the runbook, **When** the owner runs the
   settings check, **Then** it reports zero differences.
2. **Given** a checked setting was changed by hand, **When** the owner runs the check, **Then** the
   difference is listed with the expected and actual values.
""", 1),

("""  is refused. The preview shows the difference.""",
"""  is refused. The settings check compares the allow-list with the currently published ranges.""", 1),

("""- **FR-022**: Every production resource that has a management interface MUST be described in the
  repository and created from that description, previewed before it is applied, applied from the
  owner's machine (D-24), signed in as the owner. The Azure side of the description MUST be pinned
  to subscription `c28725a0-a61d-4a6c-999a-0a8980111fbf` and touch no resource outside this
  site's resource groups (the state store excepted only if it sits in its own site-only group).
  Steps with no management interface MUST be in a committed runbook.
- **FR-023**: A preview run against unchanged production MUST report zero changes.""",
"""- **FR-022**: Every production resource and setting MUST be listed in a committed runbook, in the
  order the owner creates them, with the exact names and values. Production is created by hand;
  no tool creates or changes resources (Clarification 2026-11-02).
- **FR-023**: A committed settings check MUST read the live production settings (host, edge,
  buckets, logs, alerts), changing nothing, and list every difference from the runbook's values.
  It MUST be pinned to subscription `c28725a0-a61d-4a6c-999a-0a8980111fbf` and resource group
  `portfolio`. Run against production built from the runbook, it MUST report zero differences.""", 1),

("""- **Infrastructure description**: the committed files that create and update the production
  environment, plus the state that records what exists.""",
"""- **Settings check**: the committed read-only check that compares live production settings with
  the runbook's values and lists differences.""", 1),

("""- **SC-007**: A preview of the infrastructure description against production reports zero
  changes immediately after apply.""",
"""- **SC-007**: The settings check reports zero differences against production built from the
  runbook, and reports a deliberately changed setting when run again.""", 1),

("""- Planning inputs only, not requirements: the design record's on-hold decisions are the starting
  point. D-24 is OpenTofu run locally from `infra/` with four providers;""",
"""- Planning inputs only, not requirements: the design record's on-hold decisions are the starting
  point. D-24 (OpenTofu) is withdrawn by the owner for this feature: production is made by hand;""", 1),
]

bad = [(o[:60], s.count(o), n) for o, _, n in rows if s.count(o) != n]
if bad:
    for b in bad: print("COUNT MISMATCH", b)
    sys.exit(1)
for o, nw, _ in rows:
    s = s.replace(o, nw)
p.write_text(s)
print(f"OK {len(rows)} edits")
