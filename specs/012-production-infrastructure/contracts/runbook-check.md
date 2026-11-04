# Contract: Runbook and settings check (012, FR-022, FR-023)

## infra/README.md (the runbook)

Ordered owner steps. Each step: where (portal/dashboard URL), exact names and values (from
research R-11 and data-model.md), and a closing `Check:` line naming the `check.sh` rows that turn
PASS. Sections, in this order:

1. Azure app settings (done parts marked done): Always On, HTTPS Only, FTP disabled, minimum TLS
   1.2, App Insights cap 0.15 GB/day and 30-day retention, budget `gallery-prod` on resource group
   `portfolio` at US$7 and US$10.
2. Cloudflare R2: buckets, `img.zacal.me` custom domain, r2.dev off, CORS, lifecycle, the R2 token.
3. MongoDB Atlas: Free cluster `gallery-prod` on Azure westus, user `gallery-app`, network access
   (the app's outbound IPs from portal > Networking > Outbound addresses), connection string.
4. Google: OAuth client `gallery-prod`, redirect URI, consent screen stays in Testing; owner listed as a test user (done).
5. Cloudflare zone: Origin CA certificate → PFX (`openssl pkcs12 -export`), App Service custom
   domain `port.zacal.me` (`asuid.port` TXT, proxied CNAME), certificate upload and SNI binding;
   Configuration Rule (port.zacal.me: SSL Full (strict)); Redirect Rule (http → https for
   port.zacal.me only); Tiered Cache Smart; Early Hints; transform rule
   `X-Gallery-Origin`; cache rules; purge token; read-only check token.
6. App Service access restrictions: allow Cloudflare ranges (from
   `https://www.cloudflare.com/ips-v4` and `-v6`), default deny; Advanced tool site: "Use main site
   rules" OFF, unrestricted.
7. App settings: every row of data-model.md "Production configuration", entered in portal >
   Environment variables. Generate the origin secret with `openssl rand -base64 32`.
8. First deploy: push to main, watch the `ci` run go green.
9. Claim: open `https://port.zacal.me/admin` immediately and sign in. Run
   `bash specs/012-production-infrastructure/evidence/kql.sh claim` and confirm one `auth.claim`
   with your Google subject. If someone else won: recovery is the out-of-band reset in
   docs (I.4): delete `owner` fields on the `site` document in Atlas, then sign in again.
10. Rotation: R2 token, purge token, origin secret, Google secret, Origin CA certificate: where
    each lives (Cloudflare/Google/App Service setting) and the order (create new → update app
    setting → restart → verify → revoke old). R-06 runbook.
11. Not to do: never enable basic auth, never use portal "Continuous deployment", never run the
    seed/removal command against production (it refuses), never share the address before step 9.

The runbook names the DNS wildcard finding: `port` and `img` get explicit records that override the
existing wildcard for those two names only.

## infra/check.sh (the settings check)

Usage: `bash infra/check.sh` (live) or `bash infra/check.sh --selftest`.

Live mode requires `az` signed in and `CF_READ_TOKEN` exported (a token with Zone Read, DNS Read,
Zone Settings Read, Cache Rules Read, Transform Rules Read, and Account R2 Read). It is pinned to:
`SUB=c28725a0-a61d-4a6c-999a-0a8980111fbf`, `RG=portfolio`, `APP=portfolio-web`. Every call is a
read (`az ... show|list`, HTTP GET).

Output: one line per row: `PASS <row-id> <what>` or `FAIL <row-id> <what>: expected <x>, got <y>`.
Secret rows print `present len=<n>` only. The script ends with `RESULT: <n> FAIL` and exits with the
fail count (capped at 1 for the exit code).

Rows (row-id → expected):

```
AZ-01 plan sku                         B2, Linux
AZ-02 app runtime                      DOTNETCORE|10.0
AZ-03 alwaysOn                         true
AZ-04 httpsOnly                        true
AZ-05 ftpsState                        Disabled
AZ-06 minTlsVersion                    1.2
AZ-07 basic auth (scm + ftp)           disabled
AZ-08 access restrictions main site    allow = exactly the live Cloudflare v4+v6 ranges, default Deny
AZ-09 access restrictions scm site     scmIpSecurityRestrictionsUseMain false
AZ-10 custom domain                    port.zacal.me bound, SNI SSL
AZ-11 app settings present             every key in data-model "Production configuration" (secrets: present + length only)
AZ-12 app settings values              non-secret keys equal the data-model values
AZ-13 deploy identity role             portfolio-deploy has Website Contributor at scope portfolio-web, and no other assignment in the subscription
AZ-14 federated credential             subject repo:zzacal/port:ref:refs/heads/main, issuer GitHub, audience api://AzureADTokenExchange
AZ-15 app insights cap                 dailyQuotaGb 0.15
AZ-16 app insights retention           30
AZ-17 budget                           gallery-prod, scope RG portfolio, thresholds 7 and 10 USD
CF-01 dns port                         CNAME proxied to the app's default hostname
CF-02 dns img                          R2 custom domain on gallery-prod-public, active
CF-03 ssl mode                         configuration rule on port.zacal.me sets ssl = strict
CF-04 https redirect                   redirect rule http://port.zacal.me/* -> https, 301
CF-05 tiered cache                     smart on
CF-06 early hints                      on
CF-07 transform rule                   sets X-Gallery-Origin on host port.zacal.me (value: present only)
CF-08 cache rules                      port: eligible, respect origin, bypass cookie __Host-gallery and /admin,/signin-oidc,/signout-callback-oidc,/ui; img: eligible, edge TTL 31536000
CF-09 r2 buckets                       gallery-prod-private, gallery-prod-public exist
CF-10 r2.dev                           disabled on both
CF-11 r2 cors (private)                exactly the R-11 rule
CF-12 r2 lifecycle                     staging/ 7-day delete; masters/ IA after 30 days
NET-01 origin direct                   https://<default-hostname>/ answers 403 (from this machine; not through Cloudflare)
NET-02 site through edge               https://port.zacal.me/ answers 200
NET-03 img through edge                https://img.zacal.me/ answers via Cloudflare (cf-ray header present)
```

Self-test mode reads `infra/check-fixtures/good/*.json` and `infra/check-fixtures/drift/*.json`
(recorded shapes of each `az`/API response, with secrets replaced by placeholders) instead of calling
anything, runs the same comparison functions, and must print `RESULT: 0 FAIL` for `good` and one
FAIL per drifted file for `drift`. The selftest output is committed as
`specs/012-production-infrastructure/evidence/check-selftest.txt`.

Rows that cannot be read with the owner's rights (for example AZ-13 if the owner cannot list
role assignments) print `SKIP <row-id> <reason>` rather than FAIL, and are listed in the evidence.
