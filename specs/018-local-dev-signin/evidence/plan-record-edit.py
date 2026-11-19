#!/usr/bin/env python3
"""018 plan-time record edits (research R-02, R-07, R-10). Asserts each old string's count first.

- Constitution 6.2.0 -> 6.2.1 (PATCH): I.7a "its address answers not found" -> the address is not
  mapped and answers as any unknown admin address. Protective intent unchanged (stronger: no
  handler exists on a non-qualifying host).
- Spec 018: the same correction in US2, FR-004, FR-006, SC-002; the Google-unconfigured edge case
  corrected to today's behaviour (button shown with a notice).
- Design record: D-04 gains the development-only local sign-in; traceability gets the I.7 row and
  the constitution version in its header (validate.sh C6.ids requires one row per rule).
Run from the repo root, then assemble.sh and validate.sh:
    uv run --no-project python specs/018-local-dev-signin/evidence/plan-record-edit.py
"""
import pathlib
import sys

C = pathlib.Path(".specify/memory/constitution.md")
S = pathlib.Path("specs/018-local-dev-signin/spec.md")
D = pathlib.Path("specs/001-architecture-stack-design/draft")
DEC = D / "02a-decisions-platform.md"
TR = D / "07-traceability.md"

UNKNOWN = "answers exactly as any unknown admin address does (a signed-out GET is sent to sign-in, any other method is refused)"

EDITS = [
    # ── constitution PATCH ─────────────────────────────────────────────────────────────
    (C, "- Version change: 6.1.0 → 6.2.0 (MINOR)\n",
        "- Version change: 6.2.0 → 6.2.1 (PATCH; 6.2.0 MINOR was the same day)\n"
        "- 6.2.1 (2026-11-18, feature 018 plan, research R-02): I.7a said the disabled local sign-in's\n"
        "  address \"answers not found\"; the plan does not map the route at all on a non-qualifying host,\n"
        "  so it answers as any unknown admin address does. Wording only, no guarantee changed: PATCH.\n"),
    (C, "      the environment at startup. Otherwise no local sign-in is offered and its address answers not\n      found.\n",
        "      the environment at startup. Otherwise no local sign-in is offered and its address is not\n"
        "      mapped, so it answers as any unknown admin address does, creating no session.\n"),
    (C, "**Version**: 6.2.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-18",
        "**Version**: 6.2.1 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-18"),
    # ── spec corrections ───────────────────────────────────────────────────────────────
    (S, "   the address answers not found, with no session created.",
        f"   the address {UNKNOWN}, with no session created. (Corrected at plan, R-02: \"answers not\n"
        "   found\" was unreachable, because every unmapped `/admin` address sits behind the sign-in check.)"),
    (S, "   renders or the local sign-in address is posted to, **Then** no local sign-in is offered and the\n   address answers not found.",
        "   renders or the local sign-in address is posted to, **Then** no local sign-in is offered and the\n"
        f"   address {UNKNOWN}."),
    (S, "  fails, the sign-in page MUST NOT offer it and its address MUST answer not found without creating\n  a session.",
        "  fails, the sign-in page MUST NOT offer it and its address MUST NOT exist: it\n"
        f"  {UNKNOWN}, creating no session (corrected at plan, R-02)."),
    (S, "  local sign-in and answers not found at its address.",
        f"  local sign-in and that its address {UNKNOWN}."),
    (S, "  local sign-in is absent from the page, its address answers not found, and no session is created;",
        "  local sign-in is absent from the page, its address answers as an unknown admin address, and no\n"
        "  session is created;"),
    (S, "  point of the feature); the Google button is absent, as today.",
        "  point of the feature). The Google button stays as today, but its \"Sign-in isn't set up on this\n"
        "  server.\" notice is not shown while the local sign-in is offered. (Corrected at plan, R-07: today\n"
        "  the button is shown with that notice, not hidden.)"),
    # ── design record ──────────────────────────────────────────────────────────────────
    (DEC, "the framework's automatic single-scheme default suppressed, so public requests never read the session; every challenge sends `prompt=select_account`; the requested scope is `openid` only.\n",
          "the framework's automatic single-scheme default suppressed, so public requests never read the session; every challenge sends `prompt=select_account`; the requested scope is `openid` only. Feature 018 (constitution I.7): on a development host only (environment `Development`, datastore name ending `_dev`, buckets starting `gallery-dev-`) with a local sign-in password of at least 16 characters configured at startup, `POST /admin/signin/local` takes that password and issues the same owner cookie for the stored owner, or claims an unclaimed site for the fixed identity `local-owner` through the same atomic claim; on any other host the route is not mapped, and a production start with the password setting present refuses to run.\n"),
    (DEC, "**Alternatives**: An optional claim token to pre-seed the owner before the first sign-in was noted in Constitution I as a design option for the unclaimed-window risk but was not adopted. No other identity providers were evaluated because the constitution mandates Google OIDC.\n",
          "**Alternatives**: An optional claim token to pre-seed the owner before the first sign-in was noted in Constitution I as a design option for the unclaimed-window risk but was not adopted. No other identity providers were evaluated because the constitution mandates Google OIDC. For the development stack (feature 018), a request-from-localhost gate without a password was rejected by the owner in favour of the password, which also works on CI runners; a local sign-in that never claims was rejected because a self-contained CI suite starts from an empty database.\n"),
    (TR, "One row per numbered rule in constitution v6.1.0,", "One row per numbered rule in constitution v6.2.1,"),
    (TR, "| II.1 | D-15:",
         "| I.7 | D-04: on a development host only, with a startup password of 16+ characters, `POST /admin/signin/local` signs in the stored owner or claims an unclaimed site for `local-owner`; not mapped on any other host; production start refuses while the setting is present (feature 018) | integration tests: each non-qualifying host shape has no local form and an unmapped address; wrong password creates no session; ProductionGuard refuses the setting; non-vacuity break per guard (018) | designed |\n| II.1 | D-15:"),
]


def main() -> int:
    texts, bad = {}, 0
    for path, old, new in EDITS:
        text = texts.get(path) or path.read_text(encoding="utf-8")
        n = text.count(old)
        if n != 1:
            print(f"FAIL {path}: {old[:60]!r} occurs {n}x, expected 1")
            bad += 1
            continue
        texts[path] = text.replace(old, new)
        print(f"OK {path}: {old[:60]!r}")
    if bad:
        print("aborted, nothing written")
        return 1
    for path, text in texts.items():
        path.write_text(text, encoding="utf-8")
    print(f"wrote {len(texts)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
