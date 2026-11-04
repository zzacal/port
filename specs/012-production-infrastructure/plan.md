# Implementation Plan: Production Infrastructure

**Branch**: `012-production-infrastructure` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/012-production-infrastructure/spec.md` (26 FRs,
9 SCs; clarified 2026-11-02; constitution amended to 5.0.0 first, FR-002).

## Summary

Put the site on `https://port.zacal.me`:

- Host: the owner's hand-made App Service `portfolio-web` (Linux B2, .NET 10, West US, resource
  group `portfolio`).
- Images: production R2 buckets behind `img.zacal.me`.
- Edge: Cloudflare in front, Full (strict) with an Origin CA certificate.
- Data and logs: MongoDB Atlas Free in West US; Application Insights `portfolio-appinsights`.
- Pipeline: GitHub Actions tests every push and deploys `main` as the managed identity
  `portfolio-deploy`, with no stored secret.

Production is built by hand from a committed runbook. A read-only `infra/check.sh` reports drift.

The app itself changes in one place: a new `src/Gallery.Web/Hosting/` slice, wired by two lines
in `AppBuilder`:

- an origin guard: a secret header that Cloudflare adds, plus `CF-Connecting-IP` forwarding
- a production configuration guard that refuses dev buckets, the dev database, and missing secrets
- Data Protection keys persisted to `/home` (a .NET 10 Linux App Service pitfall)
- the Azure Monitor exporter, with sampling off

Everything is off unless configured, so local runs and every existing test are unchanged.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK pinned by `global.json`, 10.0.403, `latestPatch`).
Unchanged.

**Primary Dependencies**: existing (MongoDB.Driver, AWSSDK.S3, MetadataExtractor, OIDC). Added:
`Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (FR-018; research R-06, probed). Built-in only for
forwarded headers, Data Protection and constant-time comparison.

**Storage**: no schema change. Production database `gallery` on Atlas Free `gallery-prod` (Azure
westus; Free supported there per `evidence/probe-atlas-region.txt`). Production R2 buckets
`gallery-prod-private`/`-public`.

**Testing**:
- xunit in-process hosts (`AppBuilder.Build`) for the guard, forwarding, Data Protection and
  telemetry wiring.
- Source-scan test for the workflow file.
- `infra/check.sh` NET outcome rows against the live edge (no fixtures, V.4).
- Production evidence from `verify-prod.sh` and the KQL queries (owner-run).

**Target Platform**: Azure App Service Linux (built-in .NET 10 stack) behind Cloudflare; CI on
GitHub-hosted runners.

**Project Type**: server-rendered web app plus operations artifacts (`infra/`, `.github/`).

**Performance Goals**: unchanged app bounds (public page p95 < 50 ms server-side, read counts per
011). Measured from hosted logs (SC-006).

**Constraints**:
- No resource is created by any tool; the owner builds production by hand (Clarification).
- The pipeline identity can only publish to `portfolio-web`.
- No GitHub secret.
- Every Azure read is pinned to subscription `c28725a0-…` and resource group `portfolio`.
- The owner may run `dotnet watch` on 5080/5081. Build with `--artifacts-path artifacts`; probes use
  5193+.
- Existing tests pass unmodified.

**Scale/Scope**:
- 4 new source files in `Hosting/`, 4 new test files, and 1 workflow file.
- `infra/README.md` and `infra/check.sh` with fixtures.
- Evidence scripts.
- Design-record edits across 8 draft fragments.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.* Constitution 5.0.0.

| Principle | Status | How |
|---|---|---|
| I.1–I.4 claim once | PASS | Unchanged code. Runbook step 9 orders the first sign-in straight after the first deploy (R-01 accepted risk, FR-017). |
| I.5 claim logged with client IP | PASS | `auth.claim {ip}` becomes the visitor's `CF-Connecting-IP` behind the origin guard. A guard test asserts the forwarded IP is logged (contracts/hosting.md). |
| I.6 CSRF | PASS | Unchanged. Forwarded proto lets the `Secure`-only antiforgery cookie work behind TLS termination. Without it, the probe showed `/admin/signin` returns 500 (research R-01). |
| II, III.1–III.8 | PASS | No content, visibility or validation change. |
| III.9 credential scope | PASS | Production R2 token for two buckets only; r2.dev off (NET-07); secrets only in app settings; check prints lengths only. |
| III.10 seed never production | PASS | `DevTargetGuard` unchanged and refuses Production; ProductionGuard reuses its constants. |
| IV.1–IV.4 measured | PASS | Same events, now also exported to Application Insights with sampling and log-sampling off (R-06). KQL queries per criterion (FR-019). `origin.refused` event added. |
| IV.10 warm + tiered cache | PASS | Production purge and warm through the existing provider (`Edge:PurgePages=true`); Tiered Cache Smart (runbook toggle, MANUAL row NET-09). |
| IV.18 edge HTML cache, owner bypass | PASS | Cache rules (NET-04, NET-05), bypass on `__Host-gallery` cookie and admin paths; verified by `verify-prod.sh`. |
| IV.19 Early Hints | PASS | Zone toggle (runbook, MANUAL row NET-09); origin already sends `Link` preload. |
| IV.21 Always On + output cache | PASS | AZ-03; ProductionGuard requires `Site:OutputCache=true`. |
| IV.5–IV.17, IV.20, IV.22 | PASS | No page change. |
| V.1 scope | PASS | Every addition traces to an FR. Severable seams (FR-011, FR-015, FR-019) were kept by default; listed in the report. |
| V.2 dependencies | PASS | One package, justified by FR-018 (R-06). |
| V.4 proportional effort | PASS | See "Effort vs benefit" below; three marginal items dropped. |
| V.3 no-JS | PASS | No page change. |
| VI CLI scaffolding | PASS | Package added with `dotnet add package`. No project file is hand-edited. Infra is made in portals by owner choice, so VI's `az`/`wrangler` scaffolding has nothing to scaffold. Runbook steps are portal steps plus the CLI commands that exist (`openssl`, `gh variable set`). |
| VII.2 design deviations | DEVIATION, handled | The design record is updated in this feature: D-01 (B2, hand-made), D-03 (origin secret header + IP list replaces AOP, R-04), D-05 (westus, Atlas user), D-12 (package name, sampling and log-sampling off), D-16 (production page purge on), D-18 and section 8 (B2 fixed cost; no budget alert, 6.0.0), D-24 (withdrawn: hand-made + runbook + check), D-25 (managed identity, not app registration; macOS runner), R-01 and R-06 runbook pointers, section 10 items closed. Done by a committed count-asserting edit script, then assemble and validate. |
| VIII.1 fixed cost | PASS | One B2 plan (5.0.0). |
| VIII.2 free TLS | PASS | Cloudflare Universal SSL plus Origin CA (R-13). |
| VIII.3 cost table | PASS | Section 8 updated with the B2 list price and the metered lines (R-12). |
| VIII.1 alert | N/A (withdrawn) | Constitution 6.0.0 removed the US$7 alert at the owner's request; no budget is created. |
| VIII.4 hard limits | PASS | App Insights daily cap 0.15 GB (AZ-15), the only Azure hard limit. |
| IX visual consistency | N/A (justified) | No UI change. |
| Platform Constraints | PASS | App Service Linux B2. Origin only from Cloudflare (IP list plus secret header, stronger than AOP for the any-account case). Full (strict). Google OIDC. Atlas Free in the app's region. R2 two buckets. App Insights 30 days with a cap. Forwarded headers and `CF-Connecting-IP` (PC.9, now delivered). Exemption, stated: the deploy site (`*.scm.azurewebsites.net`) is outside the Cloudflare-only restriction so GitHub-hosted runners can deploy; it accepts Entra sign-in only (basic auth off), checked by AZ-07/AZ-09. |

Gate result: PASS. One VII.2 deviation, handled in-feature. No Complexity Tracking rows.

Post-design re-check (after Phase 1): unchanged. Two Phase 1 findings tightened rather than
loosened guarantees:
- The managed certificate was replaced with Origin CA (R-13), keeping D-02.
- The provider mode rename was dropped, so the "existing tests unmodified" rule holds
  (contracts/hosting.md).

## Effort vs benefit (V.4)

Every addition beyond the literal request, with its benefit and cost. Dropped rows are not built.

| Addition | Benefit | Cost | Verdict |
|---|---|---|---|
| Origin guard + forwarded headers | Platform Constraint (origin only from Cloudflare); /admin otherwise 500s; I.5 claim IP | ~60 lines + tests | keep (constitution MUST) |
| Data Protection keys on /home | Without it every deploy signs the owner out and breaks sign-in | 3 lines + 1 test | keep |
| Production startup guard (FR-015) | Stops a mis-set app publishing into dev buckets | ~60 lines + 1 test | keep (owner kept) |
| Hosted-log queries (FR-019) | IV.4 evidence on the real site | 6 small .kql files + runner | keep (owner kept) |
| Production purge/warm (FR-011) | Changes visible in seconds, not a day | configuration only | keep (owner kept) |
| Settings check `infra/check.sh` (FR-023) | Detects drift in origin lock-down, TLS, Always On | Azure `az show` rows + outcome probes | keep, trimmed (below) |
| check.sh Cloudflare API rows (CF-01..CF-12) + read-only CF token | Same facts the outcome probes already prove (TLS, redirect, cache bypass, direct-origin 403, r2.dev off, CORS preflight) | a second token, 12 API response shapes | DROP: replaced by NET outcome rows |
| check.sh `--selftest` fixture set | Proves the check can fail before production exists | ~30 JSON fixtures | DROP: owner step O-08 (deliberate drift, expect FAIL) proves it on the real thing |
| `publish-wait.sh` | Visitor-side view of SC-004 | 1 script | DROP: SC-004 evidence is the existing `edge.lag` event |
| Datastore keep-alive job | Avoids a rare reconnect after a long Atlas pause | job + test | DROPPED earlier (decisions 24) |

## Project Structure

### Documentation (this feature)

```text
specs/012-production-infrastructure/
├── plan.md  research.md  data-model.md  quickstart.md  decisions.md
├── contracts/  hosting.md  workflow.md  runbook-check.md  evidence.md
├── evidence/   probe-*.sh|txt, constitution-5.0.0-edit.py, clarify-*.py,
│               design-record-edit.py, verify-prod.sh, kql.sh, kql/*.kql,
│               drive-prod.sh, check-net-only.txt (+ owner-run outputs)
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Gallery.Web/
├── AppBuilder.cs                 # +2 lines: AddHosting(...), UseHosting()
├── Gallery.Web.csproj            # +1 package via dotnet add package
└── Hosting/
    ├── HostingOptions.cs
    ├── HostingSetup.cs           # AddHosting / UseHosting
    ├── OriginGuardMiddleware.cs
    └── ProductionGuard.cs
tests/Gallery.Web.Tests/
├── HostingGuardTests.cs          # [SAFETY]
├── ProductionGuardTests.cs       # [SAFETY]
├── HostingDataProtectionTests.cs
├── HostingTelemetryTests.cs
└── WorkflowGuardTests.cs         # [SAFETY]
.github/workflows/ci.yml
infra/
├── README.md                     # runbook
└── check.sh                      # read-only settings check (az rows + outcome probes)
specs/001-architecture-stack-design/draft/*.md   # design record (VII.2), then assemble/validate
docs/roadmap.md                                    # hosting → 012 status, coverage rows
.gitignore                                         # infra/*.pem, infra/*.key, infra/.secrets.local
```

**Structure Decision**: one new slice folder, `Hosting/`, following the per-concern folders
already in `Gallery.Web` (`Edge/`, `Storage/`, `Upload/`). Operations artifacts sit at the repo
root, where GitHub and the owner expect them.

## Complexity Tracking

None.
