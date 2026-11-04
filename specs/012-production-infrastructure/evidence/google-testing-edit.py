"""Owner 2026-11-02: production Google client stays in Testing. Asserts counts before writing."""
import pathlib, sys
base = pathlib.Path(__file__).resolve().parents[1]
edits = {
    base / "decisions.md": [
        ("| 20 | **DEFAULT**: separate production Google OAuth client, consent screen In production | Testing mode expires sessions after 7 days; dev client keeps localhost URIs. | Low. |",
         "| 20 | **OWNER**: separate production Google OAuth client `gallery-prod` (created, redirect `https://port.zacal.me/signin-oidc`); consent screen stays in **Testing** with the owner as a test user. Publishing needs a home page and privacy policy URL, which the site does not have. Correction: the earlier \"Testing expires sessions after 7 days\" was wrong: the 7-day limit applies to refresh tokens, which the app never requests (scope `openid`, own session cookie). Non-test accounts are blocked by Google before reaching the site, so a live refusal check needs the second test user. | Owner choice. | Low: add a `/privacy` page and publish later. |", 1),
    ],
    base / "spec.md": [
        ("  registered and the sign-in consent screen published so the owner's account is not limited to a\n  test-user list.",
         "  registered. The consent screen stays in Testing with the owner listed as a test user (owner,\n  2026-11-02): publishing requires a privacy policy page the site does not have, and Testing\n  limits only refresh tokens, which the app does not use.", 1),
    ],
    base / "research.md": [
        ("| Google | new OAuth client \"gallery-prod\", redirect `https://port.zacal.me/signin-oidc`, consent screen In production |",
         "| Google | OAuth client \"gallery-prod\" (owner, done), redirect `https://port.zacal.me/signin-oidc`, consent screen in Testing, owner as test user |", 1),
    ],
    base / "data-model.md": [
        ("| Google | OAuth client `gallery-prod`, consent screen In production | GCP project | owner |",
         "| Google | OAuth client `gallery-prod`, consent screen Testing, owner as test user | GCP project | owner (done) |", 1),
    ],
    base / "contracts/runbook-check.md": [
        ("4. Google: OAuth client `gallery-prod`, redirect URI, consent screen In production.",
         "4. Google: OAuth client `gallery-prod`, redirect URI, consent screen stays in Testing; owner listed as a test user (done).", 1),
    ],
}
texts = {p: p.read_text() for p in edits}
bad = [(p.name, o[:50], texts[p].count(o)) for p, rows in edits.items() for o, _, n in rows if texts[p].count(o) != n]
if bad:
    print("ABORT", bad); sys.exit(1)
for p, rows in edits.items():
    t = texts[p]
    for o, nw, _ in rows: t = t.replace(o, nw)
    p.write_text(t)
print("OK", sum(len(r) for r in edits.values()), "edits")
