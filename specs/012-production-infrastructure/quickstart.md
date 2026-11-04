# Quickstart: Production Infrastructure (012)

Validation guide. The owner's one-time steps are the runbook (`infra/README.md`, contract
`contracts/runbook-check.md`); this file says how to prove each part works.

## A. Local (automated; no production needed)

```
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts
dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s
bash infra/check.sh --selftest | tee specs/012-production-infrastructure/evidence/check-selftest.txt
```

Expected: full suite green, existing tests unmodified. The new Hosting*, ProductionGuard and
WorkflowGuard tests pass, and the self-test prints `RESULT: 0 FAIL` for `good` and one FAIL per
drift fixture.

Publish shape (same as the CI artifact):

```
bash specs/012-production-infrastructure/evidence/probe-publish.sh
```

Expected after this feature, on a plain-http Production run with `Hosting:Edge` unset:
`/` 200, `/g` 200, `/admin` 302. `/admin/signin` still answers 500 on plain http with no forwarded
proto. That is the probe's control case. The guard test proves the forwarded-proto fix
(contracts/hosting.md).

## B. Pipeline (first push)

1. Push the feature branch. Expected: the `ci` run's `test` job is green and `deploy` is skipped
   (not main).
2. After merging to main, expected: `deploy` signs in with no secret and publishes to
   `portfolio-web`. The smoke step gets a 200 from `https://port.zacal.me/` once the runbook is done.
   Before DNS is done it fails the smoke step only. That is expected, and the log says so.

## C. Production (owner, after the runbook)

```
az login --tenant 0f44c5d4-42b0-45c2-bf55-d0fea8430d33
export CF_READ_TOKEN=...            # read-only token, runbook step 5
bash infra/check.sh | tee specs/012-production-infrastructure/evidence/check-output.txt
bash specs/012-production-infrastructure/evidence/verify-prod.sh https://port.zacal.me \
  | tee specs/012-production-infrastructure/evidence/verify-prod-output.txt
```

Expected: `RESULT: 0 FAIL` (SKIP rows listed), and `ALL PASS`.

Claim (runbook step 9), immediately after the first deploy:
open `https://port.zacal.me/admin` → sign in → `bash .../evidence/kql.sh claim` shows exactly one
`auth.claim` with your subject and your public IP (SC-005).

Then upload a few photos, publish a gallery, and run
`bash .../evidence/publish-wait.sh` (SC-004, ≤ 60 s).

Timing evidence (SC-006): `evidence/drive-prod.sh` sends 100 anonymous GETs per public page type
through Cloudflare, each with a unique `?n=<i>` query. Cloudflare keys its cache on the query, so
every request reaches the origin; the origin output cache varies by path only, so most of them are
answered from it, as real traffic would be. Then:

```
bash specs/012-production-infrastructure/evidence/kql.sh all
```

Drift check (SC-007): change Always On to off in the portal, re-run `check.sh` → `FAIL AZ-03`,
then set it back.
