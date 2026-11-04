"""Constitution 6.0.0 -> 6.1.0: V.4 proportional effort (owner, 2026-11-02, 012 analyze)."""
import pathlib, sys
root = pathlib.Path(__file__).resolve().parents[3]
con = root / ".specify/memory/constitution.md"
c = con.read_text()
he = "-->\n"
header = """<!--
Sync Impact Report
- Version change: 6.0.0 → 6.1.0 (MINOR)
- Rationale: owner directive (2026-11-02, feature 012 analyze, on a proposed datastore keep-alive
  job): "do not overengineer marginal benefits ... high effort should yield high benefits." Adds
  V.4, proportional effort. A new principle rule with no guarantee removed: MINOR.
- Modified principles: V (rule 4 added). Added/removed sections: none.
- Templates requiring updates: none (templates read the constitution at runtime).
- Prior specs swept: none needed; V.4 binds plans and tasks from 012 on. 012 already dropped the
  keep-alive under it (decisions row 24).
- Deferred TODOs: none.
- History: 1.0.0 initial ratification. 1.1.0 perceived performance, private = unlisted. 2.0.0
  client-generated derivatives with server validation. 3.0.0 Azure Blob replaced R2, masters kept,
  $10 metered cap (2026-10-27). 4.0.0 images on Cloudflare R2, managed-identity guarantee
  withdrawn, III.9 added (2026-10-30). 4.1.0 III.10 seeded dev content (2026-10-31). 4.2.0 II.6
  no empty galleries shown (2026-10-31). 4.3.0 canonical remote (2026-11-01). 4.4.0 IX visual
  consistency through shared containers (2026-11-01). 4.5.0 home page at `/`, listing at `/g`
  (2026-11-02). 5.0.0 fixed hosting ceiling B1 -> B2 (2026-11-02). 6.0.0 US$7 budget alert
  withdrawn (2026-11-02).
-->
"""
old_v3 = """3. Public pages MUST work with JavaScript disabled; scripting is progressive enhancement.
   Admin pages MAY require it.
"""
new_v3 = old_v3 + """4. Effort MUST be proportional to benefit. Work whose benefit is marginal (a rare or self-healing
   failure, a cosmetic gain, a convenience nobody asked for) MUST NOT be built when it costs more
   than a line of configuration or a sentence of documentation. Accept and record the risk
   instead. High effort is justified only by a high benefit: a constitution guarantee, a security
   or data-loss risk, or a visible improvement for visitors or the owner. Every plan MUST state,
   for each addition beyond the literal request, its benefit and its cost, and drop the ones that
   fail this rule; constitution MUST rules are never dropped under it.
"""
old_ver = "**Version**: 6.0.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02"
new_ver = "**Version**: 6.1.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02"
bad = [x[:30] for x, n in [(old_v3, 1), (old_ver, 1)] if c.count(x) != n]
if not c.startswith("<!--\nSync Impact Report\n"): bad.append("header")
if bad: print("ABORT", bad); sys.exit(1)
c = header + c[c.index(he) + len(he):]
c = c.replace(old_v3, new_v3).replace(old_ver, new_ver)
con.write_text(c); print("OK 6.1.0")
