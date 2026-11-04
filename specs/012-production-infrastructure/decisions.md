# Decisions: 012 Production Infrastructure

OWNER = the owner decided. DEFAULT = chosen at plan time, overturnable. FINDING = forced by
evidence. Pivot cost is the cost of reversing.

| # | Decision | Rationale | Pivot cost |
|---|---|---|---|
| 1 | **OWNER**: site at `port.zacal.me` | Clarify Q2. | Low before launch: new custom domain, cert, Google redirect. |
| 2 | **OWNER**: GitHub Actions; tests on every push, deploy main only | Clarify Q3 (D-25). | Low. |
| 3 | **OWNER**: App Service in subscription "Zac Zacal Test Ops Platform", Alaska Airlines tenant; tenant admins can see the app (accepted risk) | Owner's available subscription. | Medium after launch: redo runbook in another subscription, redeploy. |
| 4 | **OWNER**: production built by hand; no IaC (D-24 withdrawn) | "I'm just making it by hand". Replaced by runbook + read-only check.sh. | Low: check.sh fixtures map 1:1 to resources if IaC returns. |
| 5 | **OWNER**: B2 plan ("double the beef"); constitution 5.0.0 | Clarify; amendment before plan. | Low: scale down to B1 is one setting (needs no amendment). |
| 6 | **OWNER**: deploy identity = user-assigned MI `portfolio-deploy`, federated credential subject `repo:zzacal/port:ref:refs/heads/main`, Website Contributor on the app only | No GitHub secret; no directory rights needed. Name-based subject because the repo's OIDC customization is default (checked). | Low. |
| 7 | **FINDING**: deploy job declares no `environment:` | An environment changes the OIDC subject and the login would fail. | Low: add a second federated credential if environments are wanted. |
| 8 | **FINDING**: forwarded proto/IP required | Probe: on plain http `/admin/signin` → 500 (Secure antiforgery cookie). Claim log would record the platform IP. | — |
| 9 | **DEFAULT**: origin lock-down = Cloudflare IP allow-list + secret header `X-Gallery-Origin` (transform rule), not Authenticated Origin Pulls | Client-cert mode Required breaks Always On and still needs app validation; global AOP cert is shared by all Cloudflare accounts. The header closes the any-account bypass. D-03 updated (VII.2). | Low: AOP can be added later as a fourth layer. |
| 10 | **DEFAULT**: Always On pings get 403 from the origin guard | Still keeps the worker loaded; excluding by user agent would be forgeable. Filtered from evidence. | Low. |
| 11 | **FINDING**: persist Data Protection keys to `/home/data/dataprotection-keys` | dotnet/aspnetcore#64488: .NET 10 Linux App Service does not persist keys by default; breaks OIDC, antiforgery and sessions on restart/deploy. | — |
| 12 | **DEFAULT**: `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 instead of `Microsoft.OpenTelemetry` (D-12 name) | Smaller dependency set; same switches (probed). | Low: one package swap. |
| 13 | **FINDING**: sampling AND trace-based log sampling off | Distro defaults: 5 traces/s and drops logs of unsampled traces, which would thin http.request/op.timing evidence. | — |
| 14 | **FINDING**: Origin CA certificate, not App Service Managed Certificate | ASMC HTTP validation since 2025-07-28 fails behind proxied Cloudflare CNAMEs at renewal. Keeps D-02. | Low. |
| 15 | **DEFAULT**: CI tests on `macos-latest` with Colima; fallback ubuntu + configurable encoder paths | Encoder paths are hard-coded to /opt/homebrew; Chrome preinstalled; public repo = free minutes. Unverified until first run. | Low. |
| 16 | **DEFAULT**: provider mode label stays `dev-zone` in production | Renaming breaks EdgeSecretTests.cs:158 for a cosmetic gain. | Low. |
| 17 | **DEFAULT**: kept severable seams: production purge/warm (FR-011), startup guard (FR-015), KQL evidence (FR-019) | Owner did not answer the keep/cut question before plan; recommended answer taken. Each is one batch to cut. | Low. |
| 18 | **DEFAULT**: names `img.zacal.me`, `gallery-prod-private/-public`, Atlas `gallery-prod`/db `gallery`/user `gallery-app`, Google client `gallery-prod`, budget `gallery-prod` on RG `portfolio` | Owner did not answer before plan; dev naming mirrored. | Low before the runbook is executed. |
| 19 | **DEFAULT**: Cloudflare and Atlas made by hand from the runbook (not scripted) | Matches the owner's hand-made choice for Azure. | Low. |
| 20 | **DEFAULT**: separate production Google OAuth client, consent screen In production | Testing mode expires sessions after 7 days; dev client keeps localhost URIs. | Low. |
| 21 | **DEFAULT**: alerts go to the subscription owner's email | Not answered; changeable in the budget's action group. | Low. |
| 22 | **FINDING**: Atlas Free supported on Azure `westus` | Fetched Atlas region table (`evidence/probe-atlas-region.txt`). Same region as the app. | — |
| 23 | **FINDING**: production read-count evidence restated for the origin output cache | Cache hits log db_reads 0; rule becomes "non-zero lines within bound + at least one non-zero". | — |
