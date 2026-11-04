# Tasks: Production Infrastructure

**Input**: Design documents in `specs/012-production-infrastructure/`: spec.md, plan.md, research.md,
data-model.md, quickstart.md, decisions.md, and contracts/hosting.md, workflow.md, runbook-check.md,
evidence.md, test-impact.md.

**Source of truth**: the contracts and the research decisions (R-xx). Implement exactly what they
say. If something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: requested. The spec's SCs are measured checks, and the constitution requires a test per
[SAFETY] guard. Each batch writes its tests in the files it names, in the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081 in their own window.** Never kill or
  restart it, never run `pkill`/`killall`, and never bind :5080 or :5081. Probes use :5193 and up.
  Build and test into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s`
  - Parallel executors use their OWN output (`--artifacts-path artifacts/exec-<batch>`) and
    `--filter` on their own test classes. When a sibling's half-written file breaks the build,
    wait and retry. Never edit a file you do not own.
  - macOS has no `timeout` command. Use `--blame-hang-timeout`.
- **Existing tests must pass UNMODIFIED**, except the one stated change in contracts/test-impact.md,
  which only the parent applies (T003). A failure anywhere else means your change is wrong: fix the
  code, never the test. Executors never touch existing files under `tests/` or any file under
  `specs/`.
- **Repo paths in tests**: use `Render.RepoRoot`. Never count `..` levels.
- **Never commit.** The parent commits after its own gate. Never commit `artifacts/`.
- **Packages**: exactly one new package, added by the parent in T002 with
  `dotnet add src/Gallery.Web package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.6.0`.
  Executors add none.
- **Parent-owned files**:
  - `src/Gallery.Web/AppBuilder.cs`, `src/Gallery.Web/Program.cs`, `Gallery.Web.csproj`
  - every file under `specs/` and `docs/`
  - every existing file under `tests/`
  - `.gitignore`
- **Secrets**: never read, print, or `cat` `.r2-keys*`, `infra/.secrets.local`, user-secrets
  values, or any `*.pem`/`*.key`/`*.pfx`. Scripts that need secrets read them inside the script
  and print only presence and length.
- **Log templates, verbatim**: `origin.refused {reason}`, with reason `missing` or `mismatch`. No
  other new event. Never log the origin secret or the value received in `X-Gallery-Origin`.
- **Do not name** `claimedSubject`, `claimedAt` or `claimedIp` in any new `src/` file
  (OwnerWriteGuardTests scans for them).
- **[SAFETY]** marks a task that implements one of these:
  - origin lock-down (Platform Constraints, FR-007)
  - trusted client IP for the claim log (I.5, FR-008)
  - production-not-dev configuration (III.9/III.10, FR-015)
  - the deploy-only pipeline credential (FR-024)
  These are blocking and non-deferrable. Each [SAFETY] guard gets a deliberate break in T030 and
  must go red on its ASSERTION, not on an exception.
- **Tight assertions**: status codes and counts use equality. "Secret never logged" scans EVERY
  captured line for the exact secret string.
- **Call, never copy**: ProductionGuard calls `DevTargetGuard.DevBucketPrefix` and
  `DevDatabaseSuffix`; it never repeats the literals.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back to the parent.
- Docker must be running (`docker info`; if it isn't, `open -a Docker` and poll).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [ ] T001 Record the baseline in `specs/012-production-infrastructure/evidence/baseline.txt`:
  - `git rev-parse HEAD`
  - `dotnet --list-sdks`
  - the full-suite pass/skip count from `dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s`
  - confirm `printenv APPLICATIONINSIGHTS_CONNECTION_STRING` is empty (contracts/test-impact.md)
- [ ] T002 Add the package with `dotnet add src/Gallery.Web package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.6.0`
  (CLI only, constitution VI). Build `-warnaserror`.
- [ ] T003 Apply the stated test change: `tests/Gallery.Web.Tests/UploadSourceGuardTests.cs`
  `BaselinePackages` gains `Azure.Monitor.OpenTelemetry.AspNetCore` in ordinal order, and the
  summary comment reads "at 47de295 plus 012 R-06". Run the full suite; it must equal T001's count.
  Commit T001–T003.

## Phase 2: Foundational (blocks every story)

**Batch A1**: one executor. It owns `src/Gallery.Web/Hosting/HostingOptions.cs`,
`src/Gallery.Web/Hosting/HostingSetup.cs`, `src/Gallery.Web/Hosting/OriginGuardMiddleware.cs` and
`src/Gallery.Web/Hosting/ProductionGuard.cs`. Gate: build `-warnaserror` and the full suite still
green (nothing is wired yet).

- [ ] T004 Create `src/Gallery.Web/Hosting/HostingOptions.cs` exactly per contracts/hosting.md
  "HostingOptions": a class, `Section = "Hosting"`, the four properties, no `ToString` override,
  and helpers `IsProductionTarget` and `IsCloudflareEdge` (ordinal, case-insensitive).
- [ ] T005 [SAFETY] Create `src/Gallery.Web/Hosting/ProductionGuard.cs`: `public static
  IReadOnlyList<string> Check(IConfiguration c)` implementing every row of the contracts/hosting.md
  rule table. Each message names the key and the rule, never the value. Uses `DevTargetGuard`
  constants.
- [ ] T006 [SAFETY] Create `src/Gallery.Web/Hosting/OriginGuardMiddleware.cs` per contracts/hosting.md
  "UseHosting" step 1:
  - header `X-Gallery-Origin`
  - `CryptographicOperations.FixedTimeEquals` over UTF-8 bytes; a length mismatch counts as a mismatch
  - on refusal: 403, empty body, `Cache-Control: no-store`, `origin.refused {reason}`
  - remove the header before calling next
- [ ] T007 Create `src/Gallery.Web/Hosting/HostingSetup.cs` with `AddHosting(this IServiceCollection,
  IConfiguration)` (contract steps 1–6, in order) and `UseHosting(this WebApplication)` (guard, then
  `UseForwardedHeaders()`, both only when the edge is cloudflare). Forwarded options exactly per
  contract step 4 (`KnownIPNetworks`, not the obsolete `KnownNetworks`). Azure Monitor options
  exactly per contract step 5. Data Protection exactly per step 3.
**Parent wiring (after Batch A1)**:

- [ ] T008 Edit `src/Gallery.Web/AppBuilder.cs`:
  - add `builder.Services.AddHosting(builder.Configuration);` on the line after `AddEdge(...)`
  - add `app.UseHosting();` as the first line after `var app = builder.Build();`, before `UseRouting()`
  - no other change
  Run the full suite: existing tests unmodified, count equals T001.

**Batch A2**: one executor, after T008. It owns the new test files
`tests/Gallery.Web.Tests/HostingGuardTests.cs`, `ProductionGuardTests.cs`,
`HostingDataProtectionTests.cs` and `HostingTelemetryTests.cs`. It may fix defects its tests find
in the four `Hosting/` files (report each in `decisions_made`); it never edits AppBuilder.cs.

- [ ] T009 [SAFETY] `tests/Gallery.Web.Tests/HostingGuardTests.cs`: every case in
  contracts/hosting.md "Tests". Hosts are built with `AppBuilder.Build(["--urls",
  "http://127.0.0.1:0"], overrides)`. Use a fixed 40-char test secret, and assert it is absent
  from every captured log line.
- [ ] T010 [P] [SAFETY] `tests/Gallery.Web.Tests/ProductionGuardTests.cs`:
  - one complete valid dictionary returns an empty list
  - one `[Theory]` row per rule flips exactly one key; assert exactly one message naming that key,
    and that the bad value is absent
  - a host built with `Hosting:Target=production` plus one bad key throws at `Build` with that key
    named and the value absent
- [ ] T011 [P] `tests/Gallery.Web.Tests/HostingDataProtectionTests.cs` per contract: two sequential
  hosts share a temp key path; host 1 protects, host 2 unprotects; a `key-*.xml` file exists.
- [ ] T012 [P] `tests/Gallery.Web.Tests/HostingTelemetryTests.cs` per contract:
  - no connection string: no `TracerProvider` service is registered
  - fake unreachable connection string `InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/`:
    the host starts and `/` answers 200

**Checkpoint A (parent)**:
- build `-warnaserror`
- full suite green
- grep that every [SAFETY] mechanism exists: `FixedTimeEquals`, `"origin.refused`,
  `ForwardedForHeaderName = "CF-Connecting-IP"`, `DevTargetGuard.DevBucketPrefix` in
  ProductionGuard.cs, `EnableTraceBasedLogsSampler = false`
- commit

## Phase 3: User Story 1 + 3: the site behind the edge (P1) 🎯 MVP

**Goal**: a Release build that serves the public site behind Cloudflare, refuses direct traffic,
logs the real visitor address, and lets `/admin` work behind TLS termination.

**Independent test**:
- `probe-publish.sh` re-run with `Hosting:Edge=cloudflare` plus a secret: no header → 403; with
  the header and `X-Forwarded-Proto: https`, `/admin/signin` → 200 (it was 500 before)
- production: `verify-prod.sh` origin-refusal and image-host checks

- [ ] T013 [US1] Extend `specs/012-production-infrastructure/evidence/probe-publish.sh` (parent-owned
  evidence) with a second run:
  - set `Hosting__Edge=cloudflare` and a generated 40-char `Hosting__OriginSecret`
  - assert no header → 403
  - assert header + `X-Forwarded-Proto: https` → `/admin/signin` 200
  - assert header + `CF-Connecting-IP: 203.0.113.9` → the `http.request` line exists and
    `origin.refused` lines carry no secret
  - save the output to `probe-publish.txt`
- [ ] T014 [P] [US3] Create `specs/012-production-infrastructure/evidence/verify-prod.sh` per
  contracts/evidence.md "verify-prod.sh". It takes the base URL as `$1` and runs GETs only. It
  ports the public checks of 011/010/006, adds the 012 checks (TLS, http→https, SC-002 20 direct
  requests, image host, SC-003 cache HIT, owner-cookie bypass), and prints `PASS`/`FAIL` lines and
  `ALL PASS`. The origin default hostname comes from `$2` (`portfolio-web….azurewebsites.net`).
  Self-test it against a local Release run on :5193 with the guard on, by passing the local URL as
  both arguments. TLS/edge rows report `SKIP (no edge)` there.
- [ ] T015 [P] [US3] Create `specs/012-production-infrastructure/evidence/drive-prod.sh`: 100
  anonymous GETs per public page type (`/`, `/g`, one gallery, one photo, with ids read from the
  live listing), each with `?n=<i>`, at most 5 per second. Prints a count per route.

## Phase 4: User Story 2: claim and first publish (P1)

**Goal**: the owner claims production and publishes; the claim and the purge are visible in hosted
logs.

**Independent test**: `kql.sh claim` shows one `auth.claim` with the owner's real IP;
`publish-wait.sh` ≤ 60 s.

- [ ] T016 [P] [US2] Create `specs/012-production-infrastructure/evidence/kql/claim.kql`,
  `edge.kql`, `reads.kql`, `p95.kql`, `signin.kql` and `cachehits.kql` per contracts/evidence.md.
  Exclude `origin.refused` lines, and `http.request` lines with status 403. `reads.kql` uses the
  production rule from contracts/evidence.md (non-zero lines within bound, plus at least one
  non-zero).
- [ ] T017 [P] [US2] Create `specs/012-production-infrastructure/evidence/kql.sh`:
  `kql.sh <name|all> [--since ISO]`. It runs `az monitor app-insights query --app
  portfolio-appinsights -g portfolio --subscription c28725a0-a61d-4a6c-999a-0a8980111fbf
  --analytics-query @kql/<name>.kql`, appends to `kql-output.txt` with a header line per query, and
  is read-only.
- [ ] T018 [P] [US2] Create `specs/012-production-infrastructure/evidence/publish-wait.sh <title>`:
  polls `https://port.zacal.me/g` every 5 s for up to 120 s until the HTML-decoded title appears;
  prints the elapsed seconds and `PASS` if ≤ 60 (SC-004).

## Phase 5: User Story 5: rebuild and drift check, plus the pipeline (P2)

**Goal**: the runbook, the read-only settings check, and the CI/CD workflow.

**Independent test**: `infra/check.sh --selftest` prints `RESULT: 0 FAIL` for good and one FAIL per
drift fixture; `WorkflowGuardTests` green; the first branch push runs `test` green with `deploy`
skipped.

**Batch B**: one executor. It owns `.github/workflows/ci.yml` and
`tests/Gallery.Web.Tests/WorkflowGuardTests.cs`.

- [ ] T019 [US5] [SAFETY] Create `.github/workflows/ci.yml` per contracts/workflow.md:
  - no `environment:` key
  - no `secrets.` reference
  - `id-token: write` only on the deploy job
  - deploy only on `refs/heads/main`, with `needs: test`
  - deploys the tested artifact
  Look up the current major versions of `actions/checkout`, `actions/setup-dotnet`,
  `actions/upload-artifact`, `actions/download-artifact`, `azure/login` and
  `azure/webapps-deploy` on their GitHub release pages, and report them in `decisions_made`.
- [ ] T020 [US5] [SAFETY] `tests/Gallery.Web.Tests/WorkflowGuardTests.cs`: read
  `.github/workflows/ci.yml` from `Render.RepoRoot` as text and assert each rule from
  contracts/workflow.md "Rules":
  - no `secrets.`
  - no `environment:`
  - the deploy job contains `needs: test` and `if: github.ref == 'refs/heads/main'`
  - `id-token: write` appears exactly once, inside the deploy job's block
  - `app-name: portfolio-web`
  - the test job runs `dotnet test`
  Split the file into job blocks by indentation; no YAML package.

**Batch C**: one executor, parallel to Batch B. It owns `infra/README.md`, `infra/check.sh`,
`infra/atlas.sh` and `infra/check-fixtures/**`.

- [ ] T021 [P] [US5] Create `infra/README.md`, the runbook, per contracts/runbook-check.md sections
  1–11:
  - bare numbered steps, each with page path, field, exact value and button (the owner's standing
    preference: no explanatory prose)
  - values from research R-11 and data-model.md
  - mark the steps already done (Azure app, deploy identity, GitHub variables, Google client in
    Testing, Cloudflare A–G) as "Done 2026-11-02"
  - no budget step
- [ ] T022 [P] [US5] Create `infra/atlas.sh` per decisions row 19:
  - official Atlas CLI only; `atlas auth login` is run by the owner beforehand
  - idempotent: list before create
  - project `gallery-prod`; cluster `gallery-prod`, tier M0, provider AZURE, region US_WEST
  - user `gallery-app` with role `readWrite@gallery`; password from `openssl rand -base64 24`,
    printed once at the end with a "paste into Mongo__ConnectionString" line
  - access list from IPs passed as arguments (the app's outbound IPs)
  - print the SRV host
  - `bash -n` must pass
  - a `--dry-run` flag prints each command instead of running it; run it and save the output to
    `specs/012-production-infrastructure/evidence/atlas-dryrun.txt`
- [ ] T023 [P] [US5] Create `infra/check.sh` and `infra/check-fixtures/{good,drift}/*.json` per
  contracts/runbook-check.md:
  - row ids AZ-01..AZ-16, CF-01..CF-12, NET-01..NET-03; AZ-17 was removed under constitution 6.0.0
  - pinned to the subscription, resource group and app
  - read-only calls only
  - secrets as presence and length
  - `--selftest` reads fixtures through the same comparison functions
  - `jq` is the only parser
  Run `bash infra/check.sh --selftest` and save the output to
  `specs/012-production-infrastructure/evidence/check-selftest.txt`. It must show 0 FAIL for good
  and exactly one FAIL per drift file.

**Parent (after Batches B and C)**:

- [ ] T024 [US5] Add `infra/*.pem`, `infra/*.key`, `infra/*.pfx` and `infra/.secrets.local` to
  `.gitignore`, and confirm with `git check-ignore -v`. Commit with Batches B and C.

## Phase 6: User Story 4: production observability evidence (P2)

Covered by T012 (telemetry wiring test), T016–T017 (queries) and the owner run in O-07. No separate code.

## Phase 7: Design record and roadmap (parent, VII.2)

- [ ] T025 Write `specs/012-production-infrastructure/evidence/design-record-edit.py`: a count-asserting
  edit script over `specs/001-architecture-stack-design/draft/*.md` that aborts before writing if
  any count is off. Rewrite, with Choice/Rationale/Alternatives/Pivot cost where an entry changes:
  - D-01: B2, made by hand, `portfolio-web`, West US
  - D-02: keep the Origin CA; add the rejected App Service Managed Certificate with the ASMC reason
  - D-03: IP allow-list plus the secret `X-Gallery-Origin` header replaces AOP; `CF-Connecting-IP`
    trusted only behind the guard
  - D-05: Azure westus, user `gallery-app`, scripted by `infra/atlas.sh`
  - D-12: package name; sampling and trace-based log sampling off; conditional wiring
  - D-16: production page purge on
  - D-18 and section 8: B2 at US$26.28; no budget alert (constitution 6.0.0); App Insights cap is
    the only hard limit
  - D-24: withdrawn by the owner; replaced by a runbook plus check.sh
  - D-25: user-assigned managed identity `portfolio-deploy`, branch subject, macOS runner, no
    `environment:`
  - E-19: no $7 alert
  - R-01: runbook step 9
  - R-06: runbook step 10 (rotation)
  - Traceability rows VIII.1, PC.2 and PC.9: new mechanisms and verification
  - Section 10: close the provisioning, price-verify, Atlas-region and KQL items
  Then run `bash specs/001-architecture-stack-design/evidence/validate.sh | tee
  specs/012-production-infrastructure/evidence/design-validate.txt`; it must report 0 failures. If
  a validator keyword no longer applies (e.g. "Authenticated Origin Pulls"), update validate.sh and
  the design contract together and note the old keyword. Grep the assembled `docs/design.md` for
  leftover `B1`, `Authenticated Origin Pulls`, `$7 budget`, `Microsoft.OpenTelemetry` and
  `app registration`, and review each hit. Confirm `docs/design.md` is in the commit (`git show
  --stat HEAD`).
- [ ] T026 Update `docs/roadmap.md`:
  - the Hosting section becomes "012 Production infrastructure" with status, owner prerequisites as
    ticked checkboxes, and remaining OWNER-ONLY items
  - coverage table rows for D-01/02/03/05/12/18/24/25, E-19, R-01, R-05, R-06 and PC.1–PC.9 name 012
  - the "Not a feature" cost bullet reflects 6.0.0

## Phase 8: Polish & cross-cutting

- [ ] T027 Parent: full gate:
  - `dotnet clean`, build `-warnaserror`, full suite
  - existing tests unmodified except T003 (`git diff --stat <T001 head> -- tests/` lists only
    UploadSourceGuardTests.cs plus new files)
- [ ] T028 Parent: live local walkthrough. Run `probe-publish.sh` (both runs) and
  `verify-prod.sh http://127.0.0.1:5193 http://127.0.0.1:5193` against the guarded local Release
  build; save the outputs.
- [ ] T029 Parent: secret scan. `git log -p` of the branch plus the publish output grepped for the
  real values of the production R2 key, purge token and origin secret. Read them inside the script
  from `.r2-keys-prod-copy` and `infra/.secrets.local` if present, printing only `found`/`not
  found` per secret, never the value. Save to `evidence/secret-scan.txt` (SC-009).
- [ ] T030 Parent: non-vacuity. Copy the 011 `non-vacuity.sh` pattern into
  `specs/012-production-infrastructure/evidence/non-vacuity.sh`, with one break per [SAFETY] guard:
  - FixedTimeEquals → always true (HostingGuardTests red)
  - ForwardedForHeaderName → X-Forwarded-For (IP test red)
  - ProductionGuard drops the dev-bucket rule (ProductionGuardTests red)
  - workflow gets `environment: production` (WorkflowGuardTests red)
  - workflow deploy job loses `needs: test` (WorkflowGuardTests red)
  Dry-run first (`NV_DRYRUN=1`), then run, and save `non-vacuity.txt`. Every break must be red on
  an assertion.
- [ ] T031 Parent: fold every executor's `decisions_made` into decisions.md (accepted or REJECTED),
  mark tasks [X], and commit docs, then code, then chore (speckit-orchestration landing rules).
- [ ] T032 Parent: push the branch. The `ci` run's `test` job must be green and `deploy` skipped.
  If Colima fails on the macOS runner, apply the research R-03 fallback (ubuntu plus configurable
  encoder paths through `GALLERY_FFMPEG`/`GALLERY_CWEBP`, defaulting to the current paths;
  test-infrastructure change only) and record which path shipped in decisions.md.

## OWNER-ONLY (after the build; one at a time)

- [ ] O-01 Run `brew install mongodb-atlas-cli`, then `atlas auth login`, then `bash infra/atlas.sh
  <outbound IPs>`. Keep the printed password private.
- [ ] O-02 Runbook steps 1, 6 and 7: Always On, HTTPS Only, FTP off, TLS 1.2, the App Insights cap
  and retention, access restrictions, and every app setting, including `Hosting__OriginSecret`
  (the same value as Cloudflare rule E) and the R2 and purge tokens from `.r2-keys-prod-copy`.
  Then delete `.r2-keys-prod-copy`.
- [ ] O-03 Run `bash infra/check.sh` with `CF_READ_TOKEN` exported, and save
  `evidence/check-output.txt`. Target: 0 FAIL.
- [ ] O-04 Merge 012 to main and push. The `deploy` job must go green, including its smoke check.
- [ ] O-05 Immediately sign in at `https://port.zacal.me/admin`, then run `bash
  .../evidence/kql.sh claim` (SC-005).
- [ ] O-06 Upload a few photos, publish a gallery, and run `publish-wait.sh "<title>"` (SC-004).
- [ ] O-07 Run `verify-prod.sh https://port.zacal.me <default hostname>`, then `drive-prod.sh`,
  then `kql.sh all` (SC-001/002/003/006). Save the outputs.
- [ ] O-08 Drift test: turn Always On off, run `check.sh` and expect `FAIL AZ-03`, then turn it
  back on (SC-007).

## Dependencies & execution order

- T001 → T002 → T003 → Batch A1 (T004–T007) → T008 → Batch A2 (T009–T012) → Checkpoint A.
- After Checkpoint A, these run in parallel:
  - Phase 3 evidence scripts (T013–T015, parent and executor)
  - Phase 4 (T016–T018)
  - Batches B and C
  - Phase 7 (parent)
- T024 follows Batches B and C. T027–T032 come last. The OWNER-ONLY steps run after T032, in order.
- Story independence: US1/US3 need only Batch A. US5 needs nothing from US1 except the green
  suite in CI. US2 and US4 are verified only in production (OWNER-ONLY).

## Parallel examples

- After Checkpoint A, dispatch together: Batch B (workflow), Batch C (infra), and one executor for
  T014–T018 (evidence scripts), while the parent does T025–T026.

## Implementation strategy

MVP = Phases 1–3: the app can safely run behind Cloudflare, proven locally. Then the pipeline and
runbook (Phase 5), so the owner can deploy. The design record (Phase 7) lands before merge. The
production SCs are closed by the OWNER-ONLY steps.
