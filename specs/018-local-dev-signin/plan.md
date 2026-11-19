# Implementation Plan: Local Sign-In Without Google

**Branch**: `018-local-dev-signin` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/018-local-dev-signin/spec.md`

## Summary

On a development host (environment `Development`, `_dev` database, `gallery-dev-` buckets) where a
local sign-in password of 16+ characters is configured, the sign-in page shows a second form,
"Local password" with "Sign in locally" / "Claim locally". It posts to a new route,
`POST /admin/signin/local`, which:
- checks the forgery token and the password (constant time);
- signs in as the stored owner, or claims an empty site for `local-owner` through the existing
  atomic claim;
- issues the same owner cookie Google sign-in does.

On any other host the route is not mapped and the page is unchanged. A production start with the
password setting present refuses to run.

The decision is one pure startup rule (`LocalSignIn.Evaluate`) that reuses `DevTargetGuard`. No
stored data shape changes.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), minimal APIs, Razor components.

**Primary Dependencies**: none new (`System.Security.Cryptography` for SHA-256 and
`FixedTimeEquals`).

**Storage**: none changed; a local claim writes the existing `site` claim fields (data-model.md).

**Testing**: xUnit; `AdminHost` (gains `devHost`); `LibraryBrowserFixture` (gains `devHost`,
`ownerSession`) for one Playwright test; `Render.Html<T>` component tests.

**Target Platform**: the owner's machine and CI runners (development hosts); production is
excluded.

**Project Type**: server-rendered web app (admin sign-in).

**Performance Goals**: SC-001: the local sign-in POST finishes in under 2 s, read from its
`http.request` line in a test.

**Constraints**:
- no new package, design token, `style=` attribute or script;
- `/ui` baselines and every existing test unchanged (test-impact.md: no stated changes);
- the password never appears in a page, response or log line.

**Scale/Scope**: one route, one rule class, two optional component parameters, one guard rule.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I.1–I.5 single owner, claim once | PASS | A local claim goes through the existing atomic `TryClaimAsync` (via `ResolveAsync`) and logs `auth.claim`; after any claim the per-request owner check is unchanged. |
| I.6 CSRF | PASS | H7 validates the antiforgery token by hand, as H2 does. |
| I.7 development-only local sign-in | PASS | a: `Evaluate` (16+ chars, dev host); unmapped otherwise. b: form with token, `FixedTimeEquals`, never logged. c: `ProductionGuard` rule plus a production-target build test (any configuration source; constitution 6.2.2). d: `ResolveAsync`, then sign in as the winner. e: `auth.local.signin` / `auth.local.refused`. Wording of I.7a corrected by PATCH 6.2.1 (research R-02). |
| II visibility | N/A | No public output changes. |
| III leak nothing | PASS | No public markup; the password hash lives only in memory; no secret in logs (tested). |
| IV.1 / IV.4 timing | PASS | `http.request` and the existing `op.timing db.site.*` lines; new `auth.local.*` events (research R-06). |
| V.1 traces to spec | PASS | FR-001…FR-014. |
| V.2 minimal deps | PASS | None. |
| V.3 admin JS | PASS | None; plain forms. |
| V.4 proportional effort | PASS | No catalogue specimen (severable); no lockout or rate limit (one-user dev stack). |
| VI official CLIs | N/A | No scaffolding. |
| VII.2 design record | PASS | D-04 and the I.7 traceability row already amended here (`evidence/plan-record-edit.py`; validate 0 failures). |
| VIII cost | N/A | No resource. |
| IX shared containers | PASS | The card stays inside `AdminLayout`; the new form uses existing atoms (Input, Button) and tokens. |

Post-design re-check: unchanged, all PASS or N/A.

## Project Structure

### Documentation (this feature)

```text
specs/018-local-dev-signin/
├── plan.md  research.md  data-model.md  quickstart.md
├── contracts/
│   ├── http.md           # H7 new; H1 changed when enabled
│   ├── components.md     # K1 Input.Type, K2 SignInCard.Local, K3 SignInPage.Local
│   └── test-impact.md    # no stated changes
├── evidence/             # constitution 6.2.0 edit, plan-record-edit (6.2.1, spec corrections, D-04, I.7 row)
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/Gallery.Web/
├── Admin/Auth/LocalSignIn.cs            # new: Evaluate, Matches, state, constants
├── Admin/AdminEndpoints.cs              # H7 (mapped when enabled), H1 passes Local
├── Admin/Pages/SignInPage.razor         # Local passthrough
├── Components/Atoms/Input.razor         # optional Type
├── Components/Organisms/SignInCard.razor(.css)  # Local form
├── Hosting/ProductionGuard.cs           # + LocalSignIn:Password must not be set
└── AppBuilder.cs                        # register LocalSignIn after Mongo/Storage options (parent seam)

tests/Gallery.Web.Tests/
├── Fixtures/AdminHost.cs                # + devHost (parent, test infra)
├── Fixtures/LibraryBrowserFixture.cs    # + devHost, ownerSession (parent, test infra)
├── LocalSignInRuleTests.cs              # new
├── LocalSignInTests.cs                  # new
└── LocalSignInBrowserTests.cs           # new
```

**Structure Decision**: one web project. The rule sits beside the other auth code; the route sits
beside H2 so both sign-in posts are read together.

## Complexity Tracking

None.
