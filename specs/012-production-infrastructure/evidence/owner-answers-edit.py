"""Owner answers 2026-11-02 after the plan. Asserts counts before writing."""
import pathlib, sys
d = pathlib.Path(__file__).resolve().parents[1] / "decisions.md"
s = d.read_text()
rows = [
("| 18 | **DEFAULT**: names", "| 18 | **OWNER** (confirmed 2026-11-02: \"Cloudflare names look good\"): names", 1),
("| 19 | **DEFAULT**: Cloudflare and Atlas made by hand from the runbook (not scripted) | Matches the owner's hand-made choice for Azure. | Low. |",
 "| 19 | **OWNER**: Cloudflare settings made by hand in the owner's existing Cloudflare account (zone `zacal.me`, where the dev R2 buckets already live). Atlas rolled out by a committed script `infra/atlas.sh` using the official Atlas CLI (`brew install mongodb-atlas-cli`; `atlas auth login` in the browser, so no API key is stored): project `gallery-prod`, Free cluster on AZURE `US_WEST`, user `gallery-app` (readWrite on `gallery`), access list from the app's outbound IPs passed as arguments. Idempotent: each step skips when the resource exists. Prints the connection string host only; the password is generated locally and shown once for the owner to paste into the app setting. | Owner asked to script the DB rollout. VI prefers the official CLI. | Low. |", 1),
("| 21 | **DEFAULT**: alerts go to the subscription owner's email | Not answered; changeable in the budget's action group. | Low. |",
 "| 21 | **OWNER**: no budget alert (\"No alert needed\"). Conflicts with constitution VIII.1 (\"An Azure budget alert MUST fire at US$7 per month\") and spec FR-020; needs a /speckit-constitution amendment (MAJOR: a guarantee is removed) before /speckit-tasks. Risk stated once: nothing notifies the owner if Azure spend rises; bounded in practice because the plan is fixed-price and the App Insights daily cap (VIII.4, kept) limits the only metered Azure line. | Owner choice. | Low: one budget in the portal restores it. |", 1),
]
bad = [(o[:40], s.count(o)) for o, _, n in rows if s.count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for o, n, _ in rows: s = s.replace(o, n)
d.write_text(s); print("OK", len(rows))
