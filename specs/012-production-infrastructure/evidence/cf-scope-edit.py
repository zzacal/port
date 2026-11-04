"""Scope SSL mode and HTTPS redirect to port.zacal.me (zone has other hostnames). Asserts counts."""
import pathlib, sys
b = pathlib.Path(__file__).resolve().parents[1]
edits = {
    b / "data-model.md": [(
        "| Cloudflare zone settings | SSL Full (strict), Always Use HTTPS, Tiered Cache Smart, Early Hints | zone | owner |",
        "| Cloudflare settings | Configuration Rule on `port.zacal.me`: SSL Full (strict); Redirect Rule `http://port.zacal.me/*` → https (301); zone toggles Tiered Cache Smart, Early Hints | zone | owner |", 1)],
    b / "contracts/runbook-check.md": [
        ("   SSL Full (strict); Always Use HTTPS; Tiered Cache Smart; Early Hints; transform rule",
         "   Configuration Rule (port.zacal.me: SSL Full (strict)); Redirect Rule (http → https for\n   port.zacal.me only); Tiered Cache Smart; Early Hints; transform rule", 1),
        ("CF-03 ssl mode                         strict",
         "CF-03 ssl mode                         configuration rule on port.zacal.me sets ssl = strict", 1),
        ("CF-04 always use https                 on",
         "CF-04 https redirect                   redirect rule http://port.zacal.me/* -> https, 301", 1),
    ],
}
texts = {p: p.read_text() for p in edits}
bad = [(p.name, o[:40], texts[p].count(o)) for p, r in edits.items() for o, _, n in r if texts[p].count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for p, r in edits.items():
    t = texts[p]
    for o, nw, _ in r: t = t.replace(o, nw)
    p.write_text(t)
print("OK")
