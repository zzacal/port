"""Second pass: echoes of the V.4 drops. Asserts counts."""
import pathlib, sys
b = pathlib.Path(__file__).resolve().parents[1]
E = {
"quickstart.md": [
 ("export CF_READ_TOKEN=...            # read-only token, runbook step 5\nbash infra/check.sh |",
  "bash infra/check.sh <origin-default-hostname> |", 1),
 ("`bash .../evidence/publish-wait.sh` (SC-004, ≤ 60 s).", "`bash .../evidence/kql.sh edge` (SC-004: edge.lag ≤ 60 s).", 1),
],
"tasks.md": [("`publish-wait.sh` ≤ 60 s.", "`kql.sh edge` shows edge.lag ≤ 60 s for gallery.publish.", 1)],
"research.md": [
 ("Cloudflare read uses a separate read-only token the owner exports as `CF_READ_TOKEN`, which is not",
  "(Amended by constitution 6.1.0 V.4, decisions 27: no Cloudflare API rows or token; Cloudflare and R2 are checked by outcome probes. Original text follows.) Cloudflare read uses a separate read-only token the owner exports as `CF_READ_TOKEN`, which is not", 1),
 ("- Self-test: `infra/check.sh --selftest` feeds", "- (Dropped under V.4, decisions 27.) Self-test: `infra/check.sh --selftest` feeds", 1),
],
"plan.md": [
 ("- `infra/check.sh --selftest` on recorded fixtures.", "- `infra/check.sh` NET outcome rows against the live edge (no fixtures, V.4).", 1),
 ("r2.dev off (CF-10)", "r2.dev off (NET-07)", 1),
 ("Tiered Cache Smart (CF-05).", "Tiered Cache Smart (runbook toggle, MANUAL row NET-09).", 1),
 ("Cache rules (CF-08),", "Cache rules (NET-04, NET-05),", 1),
 ("Zone toggle (CF-06);", "Zone toggle (runbook, MANUAL row NET-09);", 1),
 ("drive-prod.sh, publish-wait.sh, check-selftest.txt (+ owner-run outputs)", "drive-prod.sh, check-net-only.txt (+ owner-run outputs)", 1),
],
"spec.md": [("covered by the settings check (CF-03, AZ-10).", "covered by the settings check (NET-02 proves strict TLS end to end; AZ-10).", 1)],
}
texts = {k: (b / k).read_text() for k in E}
bad = [(k, o[:50], texts[k].count(o)) for k, r in E.items() for o, _, n in r if texts[k].count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for k, r in E.items():
    t = texts[k]
    for o, nw, _ in r: t = t.replace(o, nw)
    (b / k).write_text(t)
print("OK", sum(map(len, E.values())))
