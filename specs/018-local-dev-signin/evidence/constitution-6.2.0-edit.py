#!/usr/bin/env python3
"""018 constitution amendment 6.1.0 -> 6.2.0 (MINOR): I.7 development-only local sign-in.

Asserts each old string's count before writing anything. Run from the repo root:
    uv run --no-project python specs/018-local-dev-signin/evidence/constitution-6.2.0-edit.py
"""
import pathlib
import sys

C = pathlib.Path(".specify/memory/constitution.md")
S007 = pathlib.Path("specs/007-owner-signin-claim/spec.md")
S018 = pathlib.Path("specs/018-local-dev-signin/spec.md")

OLD_REPORT_HEAD = """- Version change: 6.0.0 → 6.1.0 (MINOR)
- Rationale: owner directive (2026-11-02, feature 012 analyze, on a proposed datastore keep-alive
  job): "do not overengineer marginal benefits ... high effort should yield high benefits." Adds
  V.4, proportional effort. A new principle rule with no guarantee removed: MINOR.
- Modified principles: V (rule 4 added). Added/removed sections: none.
- Templates requiring updates: none (templates read the constitution at runtime).
- Prior specs swept: none needed; V.4 binds plans and tasks from 012 on. 012 already dropped the
  keep-alive under it (decisions row 24).
- Deferred TODOs: none.
"""
NEW_REPORT_HEAD = """- Version change: 6.1.0 → 6.2.0 (MINOR)
- Rationale: owner directive (2026-11-18, feature 018 clarify): "a way for local stack to be logged
  in without a real auth", gated by a password the person starting the app sets, and able to claim
  an empty dev database so a self-contained CI browser suite can sign in. I.1 said ownership "MUST
  be established by the first successful Google OIDC sign-in"; it now also allows, on development
  hosts only, the password local sign-in of the new rule I.7. Production identity stays Google
  only. A guarantee narrowed by a stated exemption whose controls are written into the principle:
  MINOR.
- Modified principles: I (rule 1 points at the exemption; rule 7 added). Platform Constraints:
  Identity bullet notes the development-only exemption. Added/removed sections: none.
- Templates requiring updates: none (templates read the constitution at runtime).
- Prior specs swept: 007 FR-003 ("Google as the only identity provider. No passwords") annotated
  in place: still true for every non-development host.
- Deferred TODOs: none. Design record D-04 is amended by feature 018 (its FR-013).
"""
OLD_HISTORY_TAIL = """  (2026-11-02). 5.0.0 fixed hosting ceiling B1 -> B2 (2026-11-02). 6.0.0 US$7 budget alert
  withdrawn (2026-11-02).
-->"""
NEW_HISTORY_TAIL = """  (2026-11-02). 5.0.0 fixed hosting ceiling B1 -> B2 (2026-11-02). 6.0.0 US$7 budget alert
  withdrawn (2026-11-02). 6.1.0 V.4 proportional effort (2026-11-02).
-->"""

OLD_I1 = """1. Ownership MUST be established by the first successful Google OIDC sign-in initiated from
   `/admin` on an unclaimed site. Claiming MUST be atomic — when two sign-ins race, exactly one
   becomes owner and the other is refused."""
NEW_I1 = """1. Ownership MUST be established by the first successful Google OIDC sign-in initiated from
   `/admin` on an unclaimed site (or, on a development host only, by the local sign-in of rule 7).
   Claiming MUST be atomic — when two sign-ins race, exactly one becomes owner and the other is
   refused."""

OLD_I6 = """6. All admin state-changing requests MUST be protected against cross-site request forgery.
"""
NEW_I6 = """6. All admin state-changing requests MUST be protected against cross-site request forgery.
7. Development-only local sign-in. On a development host — environment `Development`, datastore
   name ending `_dev` and storage buckets starting `gallery-dev-` (the markers of III.10's
   development target), all three — the owner MAY be signed in without Google by a local sign-in,
   and an unclaimed site MAY be claimed by it for the fixed identity `local-owner`. These rules are
   the entire protection for that path:
   a. It exists only when a local sign-in password of at least 16 characters is supplied through
      the environment at startup. Otherwise no local sign-in is offered and its address answers not
      found.
   b. Each local sign-in is a deliberate form submission carrying that password and the rule-6
      forgery token. The password is compared in constant time and never appears in any page,
      response or log line; a wrong password creates no session.
   c. No setting can enable it on a host that is not a development host, and a production start
      MUST refuse to run while the password variable is present (naming the variable, never its
      value). An automated check MUST prove a production-shaped host offers no local sign-in.
   d. A local claim goes through the same atomic claim-once path as rule 1 and is logged per rule 5,
      marked local. After any claim, rules 3 and 4 hold unchanged: the claiming identity, Google or
      `local-owner`, is the only owner, and a local sign-in on a Google-claimed site signs in as
      that owner.
   e. Every local sign-in and every refused attempt is logged with outcome and client address.
"""

OLD_RATIONALE = """Rationale: a personal gallery needs no user management; a claim-once model removes the entire
account-administration surface. The unclaimed window between deploy and first login is an
accepted, documented risk that design MAY close with an optional claim token."""
NEW_RATIONALE = """Rationale: a personal gallery needs no user management; a claim-once model removes the entire
account-administration surface. The unclaimed window between deploy and first login is an
accepted, documented risk that design MAY close with an optional claim token. Rule 7 lets the
development stack (the owner's machine, a CI runner) sign in without an identity provider; it
holds only development data, and whoever set the password started the process. Production stays
Google only by rule 7c, not by the password."""

OLD_IDENTITY = """- Identity: Google OIDC, with a cookie session that has a 7-day sliding expiry (renewed while it
  is in use)."""
NEW_IDENTITY = """- Identity: Google OIDC, with a cookie session that has a 7-day sliding expiry (renewed while it
  is in use). Development hosts additionally allow the password local sign-in of I.7."""

OLD_VERSION = "**Version**: 6.1.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-02"
NEW_VERSION = "**Version**: 6.2.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-18"

OLD_007 = """- **FR-003**: Sign-in MUST use Google as the only identity provider. No passwords, no other
  providers."""
NEW_007 = """- **FR-003**: Sign-in MUST use Google as the only identity provider. No passwords, no other
  providers. **AMENDED by 018-local-dev-signin (constitution 6.2.0, I.7)**: still true on every
  non-development host; development hosts may also offer the password local sign-in of I.7."""

OLD_018 = """- **FR-012** *(blocking, before planning)*:"""
NEW_018 = """- **FR-012** *(blocking, before planning; amendment DONE: constitution 6.2.0, rule I.7)*:"""

EDITS = [
    (C, OLD_REPORT_HEAD, NEW_REPORT_HEAD),
    (C, OLD_HISTORY_TAIL, NEW_HISTORY_TAIL),
    (C, OLD_I1, NEW_I1),
    (C, OLD_I6, NEW_I6),
    (C, OLD_RATIONALE, NEW_RATIONALE),
    (C, OLD_IDENTITY, NEW_IDENTITY),
    (C, OLD_VERSION, NEW_VERSION),
    (S007, OLD_007, NEW_007),
    (S018, OLD_018, NEW_018),
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
