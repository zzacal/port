# Test impact: Local Sign-In Without Google

Grep run 2026-11-18 over `tests/` (source only) for `signin/local`, `LocalSignIn`, `local-owner`,
`type="password"`, `Password`, `EndpointDataSource`, `RouteEndpoint`, `isn't set up`,
`si-notice`, `Sign in with Google`, `DevTargetGuard`, `appsettings`.

## A / B / D: none

No existing test is retargeted, extended or deleted. `AdminHost` and `LibraryHost` run in the
Production environment. The four browser fixtures (`LibraryBrowserFixture`, `NavBrowserFixture`,
`BrowserSiteFixture`, `UploadBrowserFixture`) run in Development with `--applicationName
Gallery.Web`, so they also load the owner's user-secrets on the owner's machine. None of them names
its database with `_dev`, so the state is always `Absent` or `NotDevelopmentHost`: H7 is unmapped
and H1 renders exactly as today. The only machine-dependent effect is one
`auth.local.state not_development_host` startup line when the owner has set the password in
user-secrets; no existing test reads `auth.local.*`. (Corrected at tasks: plan said every host runs
in Production.)

## C: unchanged, listed so nobody "fixes" them

| File:line | Why it still holds |
|---|---|
| AdminAccessTests.cs:342 | "Sign-in isn't set up" notice on a Production-environment host: the suppression applies only when the local sign-in is enabled. |
| SignInComponentTests.cs:60-94 | Card notice placement with `Local` false: byte-identical. |
| OrganismComponentTests.cs:146-185 | Card states with `Local` false: byte-identical. |
| OrganismStatePageTests.cs:19 | `/ui` SignInCard page specimen labels: unchanged (no new specimen). |
| ProductionGuardTests.cs:54, :99 | `Valid()` has no `LocalSignIn:Password`; the per-rule flip list is the test's own. |
| RemoveSeededTests (DevTargetGuard) | The guard is called, not changed. |
| UiPagesUnchangedTests (all `/ui` baselines) | `Input` with null `Type` and `SignInCard` with `Local` false are byte-identical. |

Every test not listed here MUST also pass unmodified. Re-run this grep before the first implement
batch; a new hit becomes the next row "(found at implement)".

## New tests (summary; tasks.md has the details)

- `LocalSignInRuleTests` (pure): `Evaluate` truth table; `Matches` on equal, different,
  different-length, empty and null passwords; `ProductionGuard` refuses the key and names it,
  never the value.
- `LocalSignInTests` (HTTP, `AdminHost(devHost: true)`): the form on SignIn and Claim; sign-in on
  a claimed site as the stored owner; claim on an unclaimed site; wrong, empty and token-less
  posts; the session works on an admin form and signs out; no password in any body or log line;
  the `http.request` duration for SC-001; each non-qualifying host shape (no form, the address
  answers 401/302, no cookie); a production-target host with the key refuses to build.
- `LocalSignInBrowserTests` (`[Collection("library-browser")]`): from an empty dev database, with
  no minted session, type the password, land on `/admin`, then open `/admin/library` (SC-006).
