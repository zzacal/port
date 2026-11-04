"""Sweep 012 artifacts for the withdrawn budget alert (constitution 6.0.0). Asserts counts."""
import pathlib, sys
b = pathlib.Path(__file__).resolve().parents[1]
edits = {
    b / "plan.md": [
        ("| VIII.4 hard limits | PASS | App Insights daily cap 0.15 GB (AZ-15). The budget alerts; Azure budgets cannot stop spend (unchanged design fact). |",
         "| VIII.1 alert | N/A (withdrawn) | Constitution 6.0.0 removed the US$7 alert at the owner's request; no budget is created. |\n| VIII.4 hard limits | PASS | App Insights daily cap 0.15 GB (AZ-15), the only Azure hard limit. |", 1),
        ("D-18 and section 8 (B2 fixed cost, RG-scoped budget)", "D-18 and section 8 (B2 fixed cost; no budget alert, 6.0.0)", 1),
    ],
    b / "research.md": [
        ("| Alert email | the owner's subscription email (budget action group) |\n| Budget | `gallery-prod`, scope resource group `portfolio`, US$7 and US$10 actual-cost notifications |\n", "", 1),
        ("The projected metered total is about US$1, under the US$7 alert.", "The projected metered total is about US$1, under the US$7 threshold (VIII.3).", 1),
        ("- The budget scopes to resource group `portfolio`, so it sees only this site's Azure spend. R2 is\n  billed by Cloudflare, outside it, as today (design section 8 note).",
         "- No budget alert (constitution 6.0.0, owner). R2 is billed by Cloudflare, as today (design\n  section 8 note).", 1),
    ],
    b / "data-model.md": [
        ("| Budget | `gallery-prod`, RG scope, US$7 / US$10 | `portfolio` | owner |\n", "", 1),
    ],
    b / "contracts/runbook-check.md": [
        ("   1.2, App Insights cap 0.15 GB/day and 30-day retention, budget `gallery-prod` on resource group\n   `portfolio` at US$7 and US$10.",
         "   1.2, App Insights cap 0.15 GB/day and 30-day retention. No budget (constitution 6.0.0).", 1),
        ("AZ-17 budget                           gallery-prod, scope RG portfolio, thresholds 7 and 10 USD\n", "", 1),
    ],
    b / "decisions.md": [
        (", Google client `gallery-prod`, budget `gallery-prod` on RG `portfolio`", ", Google client `gallery-prod`", 1),
        ("needs a /speckit-constitution amendment (MAJOR: a gua", "amended: constitution 6.0.0 (MAJOR: a gua", 1),
    ],
}
texts = {p: p.read_text() for p in edits}
bad = [(p.name, o[:50], texts[p].count(o)) for p, r in edits.items() for o, _, n in r if texts[p].count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for p, r in edits.items():
    t = texts[p]
    for o, nw, _ in r: t = t.replace(o, nw)
    p.write_text(t)
print("OK", sum(map(len, edits.values())))
