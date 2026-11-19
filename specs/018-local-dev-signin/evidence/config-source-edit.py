#!/usr/bin/env python3
"""018 owner directive (2026-11-18, after analyze H1): "I want the password to be anything that ends
up in the configuration. So it could be env var, user-secret, and anywhere a configuration could
come from".

- Constitution 6.2.1 -> 6.2.2 (PATCH): I.7a "supplied through the environment at startup" ->
  "configured at startup, from any configuration source"; I.7c "password variable" -> "password
  setting". No control depended on the source: the dev-host gate and the production refusal
  (which reads the final configuration, so it catches the key from any source) are the controls.
- Spec 018: the same wording in the clarification, FR-004, FR-006, US2, edge case, assumptions;
  a stated accepted risk for a committed dev settings file.
- Research R-03, plan, tasks, test-impact: the "never committed" source-scan test is dropped
  (T015 removed, later tasks renumbered).
- Traceability header version.
Run from the repo root, then assemble.sh and validate.sh.
"""
import pathlib
import re
import sys

F = "specs/018-local-dev-signin/"
fails = []


def edit(path, pairs):
    p = pathlib.Path(path)
    t = p.read_text()
    for old, new in pairs:
        n = t.count(old)
        if n != 1:
            fails.append(f"{path}: expected 1, found {n}: {old[:70]!r}")
            continue
        t = t.replace(old, new)
    return p, t


edits = []

edits.append(edit(".specify/memory/constitution.md", [
    ("- Version change: 6.2.0 → 6.2.1 (PATCH; 6.2.0 MINOR was the same day)\n",
     "- Version change: 6.2.1 → 6.2.2 (PATCH; 6.2.0 MINOR and 6.2.1 PATCH were the same day)\n"
     "- 6.2.2 (2026-11-18, feature 018 analyze, owner directive \"anywhere a configuration could\n"
     "  come from\"): I.7a said the password is \"supplied through the environment at startup\"; it is\n"
     "  now any configuration source (environment variable, user-secrets, settings file, any other\n"
     "  provider). I.7c \"password variable\" becomes \"password setting\". No control depended on the\n"
     "  source: the development-host gate and the production refusal (which reads the final\n"
     "  configuration, so it catches the setting from any source) are unchanged. PATCH.\n"),
    ("supplied through\n      the environment at startup.",
     "configured at\n      startup, from any configuration source."),
    ("while the password variable is present (naming the variable, never its\n      value)",
     "while the password setting is present (naming the setting, never its\n      value)"),
    ("**Version**: 6.2.1 |", "**Version**: 6.2.2 |"),
]))

edits.append(edit(F + "spec.md", [
    ("starting the app sets in an environment variable; the sign-in form asks for it.",
     "starting the app sets in an environment variable; the sign-in form asks for it. (Widened\n"
     "  after analyze, owner: any configuration source counts: environment variable, user-secrets,\n"
     "  settings file or any other provider.)"),
    ("refuses to run when the variable is set.", "refuses to run when the setting is present."),
    ("configured through the environment at startup.",
     "configured at startup, from any configuration source (environment variable, user-secrets,\n"
     "  settings file or any other provider)."),
    ("local sign-in password variable is present (naming the variable, never its value)",
     "local sign-in password setting is present, from any source (naming the setting, never its value)"),
    ("4. **Given** a production configuration with the local sign-in password variable set,",
     "4. **Given** a production configuration with the local sign-in password setting present,"),
    ("names the variable (never its value); without\n   the variable,",
     "names the setting (never its value); without\n   the setting,"),
    ("- **Password variable set but too short**", "- **Password setting present but too short**"),
    ("logs a warning naming the variable and", "logs a warning naming the setting and"),
    ("- Where the password lives (planning input): on the owner's machine in user-secrets or the\n"
     "  launch environment, never committed;",
     "- Where the password lives (planning input): anywhere configuration comes from. On the owner's\n"
     "  machine typically user-secrets or the launch environment;"),
    ("Production is excluded by FR-004\n  and FR-006, not by the password.",
     "Production is excluded by FR-004\n  and FR-006, not by the password. If the password is put in a committed development settings\n"
     "  file, anyone who can read the repository can sign in to any dev stack that uses it (owner\n"
     "  choice: any configuration source is allowed)."),
]))

edits.append(edit(F + "research.md", [
    ("""`LocalSignIn__Password`). It is read from the process configuration: environment variables, plus
the per-user secret store on the owner's machine (the host loads user-secrets in Development only).
A source-scan test fails if any committed `appsettings*.json` or `launchSettings.json` contains
`LocalSignIn`. Minimum 16 characters; a shorter value counts as unset (state `too_short`).""",
     """`LocalSignIn__Password`). It is read through `IConfiguration`, so any configuration source
works: environment variables, user-secrets (loaded in Development), settings files, command-line
arguments or any other provider (owner directive after analyze). Minimum 16 characters; a shorter
value counts as unset (state `too_short`). ProductionGuard reads the same final configuration, so
it refuses the key from any source; a production host does not load `appsettings.Development.json`
or user-secrets, so only a key in a source production actually loads trips it."""),
    ("""**Reading of I.7a "supplied through the environment at startup"**: the run environment outside
source control (an environment variable or the owner's user-secrets), never a committed file. The
source-scan test enforces the "never committed" half.""",
     """**I.7a source**: constitution 6.2.2 says "from any configuration source". The earlier plan
reading ("never committed", enforced by a source-scan test) is withdrawn with that test."""),
]))

edits.append(edit(F + "plan.md", [
    ("c: `ProductionGuard` rule plus a production-target build test, and the source scan for committed config.",
     "c: `ProductionGuard` rule plus a production-target build test (any configuration source; constitution 6.2.2)."),
    ("├── LocalSignInSourceTests.cs            # new\n", ""),
]))

edits.append(edit(F + "contracts/test-impact.md", [
    ("""- `LocalSignInSourceTests`: no committed `appsettings*.json` / `launchSettings.json` contains
  `LocalSignIn`.
""", ""),
]))

p, t = edit(F + "tasks.md", [
    ("`tests/Gallery.Web.Tests/LocalSignInTests.cs`, `LocalSignInGuardTests.cs`,\n"
     "`LocalSignInSourceTests.cs` and `LocalSignInBrowserTests.cs`.",
     "`tests/Gallery.Web.Tests/LocalSignInTests.cs`, `LocalSignInGuardTests.cs` and\n"
     "`LocalSignInBrowserTests.cs`."),
    ("- Batch B (needs A and T009): T010 ‖ T011, then T012; then T013, T014 ‖ T015, T016; checkpoint B.",
     "- Batch B (needs A and T009): T010 ‖ T011, then T012; then T013, T014, T015; checkpoint B."),
    ("then T013–T016", "then T013–T015"),
    ("the browser test T016", "the browser test T015"),
    ("- Polish: T017 ‖ T018, T019 → T020 → T021 → T022.",
     "- Polish: T016 ‖ T017, T018 → T019 → T020 → T021."),
    ("Wave 3: T017 decisions ‖ T018 roadmap; T019;", "Wave 3: T016 decisions ‖ T017 roadmap; T018;"),
    ("non-deferrable; T020 breaks each on purpose.", "non-deferrable; T019 breaks each on purpose."),
])
blk = re.search(r"- \[ \] T015 \[P\] \[US2\] \[SAFETY\] Write `tests/Gallery\.Web\.Tests/LocalSignInSourceTests\.cs`.*?(?=- \[ \] T016 )", t, re.S)
if not blk:
    fails.append("tasks.md: T015 block not found")
else:
    t = t.replace(blk.group(0), "")
    for n in range(16, 23):
        old = f"- [ ] T{n:03d} "
        if t.count(old) != 1:
            fails.append(f"tasks.md: {old} count {t.count(old)}")
        t = t.replace(old, f"- [ ] T{n - 1:03d} ")
    t = t.replace("environment variable or user-secrets", "environment variable or user-secrets")
edits.append((p, t))

edits.append(edit("specs/001-architecture-stack-design/draft/07-traceability.md", [
    ("constitution v6.2.1", "constitution v6.2.2"),
]))

if fails:
    print("NOT WRITTEN:\n" + "\n".join(fails))
    sys.exit(1)
for p, t in edits:
    p.write_text(t)
    print("wrote", p)
