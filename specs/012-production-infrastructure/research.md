# Research: Production Infrastructure (012)

Every decision below names its evidence. Probes are committed under `evidence/` with their output.
"Owner" marks a choice the owner made in clarify; "Default" marks one taken at plan time that the
owner can overturn cheaply (also logged in decisions.md).

## R-01 Host shape: App Service Linux B2, built-in .NET 10 stack, code deploy (Owner)

- Decision: one Linux Basic B2 plan `portfolio-plan`, app `portfolio-web`, West US, Publish = Code,
  runtime .NET 10 (LTS), in resource group `portfolio`, subscription
  `c28725a0-a61d-4a6c-999a-0a8980111fbf`. Created by hand by the owner. Constitution 5.0.0 allows B2.
- Deploy artifact: the framework-dependent output of `dotnet publish src/Gallery.Web -c Release`.
  Probe `evidence/probe-publish.sh` (output `probe-publish.txt`): the Release publish starts in
  `Production` with no secrets and no dev settings, `/` and `/g` answer 200, `/admin` redirects. It
  also found two defects that only show on a plain-http, no-proxy host, which R-04 fixes:
  - `/admin/signin` answers 500 because the antiforgery cookie is `SecurePolicy=Always` and the
    request looks like http. Behind App Service, TLS ends at the platform front end, so every
    request reaches Kestrel as http. Without forwarded-proto handling, production `/admin` would be
    500 on every request.
  - HTTPS redirection logs "Failed to determine the https port".
- The publish output carries `appsettings.Development.json`. It is inert in Production (the host
  loads `appsettings.{Environment}.json` only), so it stays. It holds no secret: bucket and database
  names only.
- Alternatives: container publish (rejected by the owner after the first app; it needs a registry
  and an image build). Self-contained publish (rejected: the built-in stack already has the runtime,
  and self-contained triples the artifact).

## R-02 Deploy identity and pipeline (Owner, done)

- Decision: GitHub Actions in `zzacal/port`, workflow `.github/workflows/ci.yml`. A `test` job runs
  on every push to any branch. A `deploy` job runs only on `refs/heads/main`, `needs: test`, signs
  in with `azure/login` using OIDC as the user-assigned identity `portfolio-deploy` (repository
  variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; verified present), and
  publishes with `azure/webapps-deploy` to `portfolio-web`. The deploy job builds the artifact that
  the test job uploads, so the deployed bits are the tested bits (FR-024, "exact commit").
- The identity has Website Contributor on `portfolio-web` only. It cannot create, delete, or
  reconfigure other resources. It can change this app's settings, which is inherent to the role; the
  settings check (R-10) detects any such drift.
- The federated credential's subject is `repo:zzacal/port:ref:refs/heads/main`. The repository's OIDC
  customization reads `use_default: true` (checked with `gh api`), so the name-based subject is the
  one GitHub sends. The portal's suggested ID-based subject would not have matched.
- Basic authentication stays disabled, so there is no publish profile and no GitHub secret.
- Alternatives: publish profile (rejected: long-lived password, needs basic auth on). Portal
  "Continuous deployment" (rejected: commits its own workflow that deploys without our tests).
  Entra app registration (rejected: likely blocked in the company tenant; the managed identity
  needs no directory rights).

## R-03 Which tests the pipeline runs (Default)

- Facts: the suite needs Docker (Testcontainers Mongo and S3Proxy), headless Google Chrome
  (Playwright `Channel = "chrome"`), and `/opt/homebrew/bin/ffmpeg` and `cwebp` (hard-coded in
  `tests/Gallery.Web.Tests/Fixtures/EncodedFixtures.cs` and `PhotoEncoder.cs`). Tests that need
  encoders call `Assert.Fail` when they are missing (`DerivativeValidatorTests` turns `SkipException`
  into a failure), so the suite fails on a runner without them at those paths.
- Decision: run the full suite on `macos-latest` (GitHub-hosted, Apple silicon). Homebrew lives at
  `/opt/homebrew` there, so `brew install ffmpeg webp` puts the encoders exactly where the tests
  look, and Google Chrome is preinstalled. Docker is the open risk on macOS runners: GitHub's macOS
  images ship no Docker daemon. The workflow installs Colima (`brew install colima docker`,
  `colima start`) and exports `DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE`. The
  R2-only tests skip because no R2 keys are in the runner (they already gate on the environment).
- Escalation rule, written into tasks: if Colima cannot start on the runner (nested
  virtualization), do NOT trim the suite. Fall back to `ubuntu-latest` and make the encoder paths
  in the two fixtures configurable (`GALLERY_FFMPEG`, `GALLERY_CWEBP`, default to the current
  `/opt/homebrew` paths, so local runs stay unchanged), installing them with apt. That is a test
  infrastructure change and needs no test edits. Record which path shipped in decisions.md.
- Cost: macOS minutes are free on a public repository (`zzacal/port` is PUBLIC, checked with `gh`).
- Unverified until the first run. The plan names it in the report.

## R-04 Forwarded headers and origin lock-down (D-03 re-decided for App Service)

- Facts: on App Service Linux, TLS ends at the platform front end. Kestrel sees http, the front end's
  address as the client, and `X-Forwarded-For`/`X-Forwarded-Proto` set by the front end. Cloudflare
  sets `CF-Connecting-IP` to the visitor. The 007 claim log and `auth.signin`/`auth.failed` read
  `Connection.RemoteIpAddress`, so today they would log a platform address.
- Authenticated Origin Pulls on App Service: the platform terminates TLS and only forwards a client
  certificate in `X-ARR-ClientCert` when client-certificate mode is on. In Required mode, Always On's
  own pings to `/` carry no certificate and would be refused, and the app would still have to
  validate the forwarded certificate itself. Global AOP uses a certificate "shared across all
  Cloudflare accounts" (Cloudflare docs), so on its own it proves only "came through Cloudflare",
  the same as the IP allow-list.
- Decision: three layers, none of which needs a certificate on the origin.
  1. App Service access restrictions on the main site: allow Cloudflare's published IPv4 and IPv6
     ranges, deny all else. The `scm` (deploy) site keeps its own unrestricted rules, protected by
     Entra sign-in since basic auth is off, so GitHub-hosted runners can deploy.
  2. A shared origin secret: a Cloudflare request-header transform rule on `port.zacal.me` sets
     `X-Gallery-Origin: <secret>` on every request it forwards. The app refuses (403, empty body)
     any request without the exact value, compared in constant time. This is Cloudflare's
     documented "HTTP header validation" pattern and closes the any-Cloudflare-account bypass that
     the IP list alone leaves open. The value is 32 random bytes, base64url, generated by the owner
     with `openssl rand -base64 32`, stored in the app setting `Hosting__OriginSecret` and in the
     Cloudflare rule, never in the repository.
  3. Forwarded headers: the app trusts `CF-Connecting-IP` for the client address and
     `X-Forwarded-Proto` for the scheme only on hosts where the origin guard is on (the secret
     proves the request came through our zone). `ForwardedHeadersOptions.KnownIPNetworks` and
     `KnownProxies` are cleared because the platform front end's addresses are not published; the
     origin secret is the trust anchor instead.
- One switch: `Hosting:Edge = "cloudflare"` turns on layers 2 and 3 together and makes startup fail
  if `Hosting:OriginSecret` is shorter than 32 characters. Trusting `CF-Connecting-IP` without the
  guard would let anyone spoof the claim log's address, so the two cannot be enabled separately.
  Unset (local runs and every existing test) means no guard and no forwarding, exactly today's
  behavior.
- Always On pings `/` without the header and get 403. That still keeps the worker process loaded,
  which is all Always On is for. The 403s are logged as `http.request GET / 403` from the
  platform's address; evidence queries filter them by status (R-09). Default; overturnable by
  excluding the `AlwaysOn` user agent, which was rejected because a user agent is trivially forged.
- This is a VII.2 deviation from D-03's mechanism (AOP). The design record is updated in this
  feature (task in the design-record batch). The guarantee "origin accepts traffic only from
  Cloudflare" (Platform Constraints) is kept, and is stronger than before for the any-account case.
- Alternatives: AOP plus app-side `X-ARR-ClientCert` validation against a per-zone certificate
  (rejected: client-cert mode Required breaks Always On, and the per-zone certificate needs an
  upload and rotation runbook for no gain over the secret). Cloudflare Tunnel (rejected: App Service
  cannot run `cloudflared` beside the app on the code stack). IP allow-list alone (rejected by D-03's
  own reasoning).

## R-05 Data Protection keys on .NET 10 Linux App Service (finding)

- Fact: dotnet/aspnetcore issue #64488 (open) reports that on .NET 10 Linux App Service the
  Data Protection keys are no longer persisted by default, which breaks the OIDC state/nonce
  round-trip and antiforgery after a restart or a second worker. The 007 sign-in depends on both,
  and the `__Host-gallery` session cookie is encrypted with the same keys, so every deploy would
  also sign the owner out.
- Decision: persist keys to the file system explicitly: `AddDataProtection()
  .PersistKeysToFileSystem(new DirectoryInfo(path)).SetApplicationName("gallery")` when
  `Hosting:DataProtectionKeysPath` is set. Production sets it to `/home/data/dataprotection-keys`.
  `/home` is App Service's persistent share, which survives restarts and deploys. Built in, no
  package (V.2). Keys are not encrypted at rest. That is acceptable: only someone with access to the
  app's file system can read them, and that person can already read the app settings. Logged as an
  accepted risk.
- Alternatives: keys in Mongo or R2 (rejected: needs a package or custom repository code for no
  gain on one instance). Leave the default (rejected by the issue above).

## R-06 Hosted logs: Application Insights through the Azure Monitor distro (D-12, re-checked)

- Package: `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (latest stable on nuget.org). D-12 named
  `Microsoft.OpenTelemetry`. Microsoft now ships both, and the newer one adds an A365 agent
  dependency set (`probe-otel-options.txt` lists both assembly sets). The Azure Monitor distro is
  the smaller one and has every switch we need. Justified against FR-018 (V.2). Recorded as a D-12
  amendment.
- Probe `evidence/probe-otel-options.sh`: `AzureMonitorOptions` exposes `SamplingRatio`,
  `TracesPerSecond`, `EnableTraceBasedLogsSampler`, `EnableLiveMetrics` and `ConnectionString`.
- The fact that changes the setup: the distro defaults to rate-limited sampling of 5 traces per
  second, and for .NET it drops logs tied to unsampled traces by default (Microsoft Learn,
  "Sampling in Azure Application Insights with OpenTelemetry"). Our evidence is the
  `http.request`/`op.timing` ILogger events, so the defaults would silently thin them. Decision:
  `SamplingRatio = 1.0F`, `TracesPerSecond = null`, `EnableTraceBasedLogsSampler = false`,
  `EnableLiveMetrics = false` (D-12 "sampling explicitly off").
- Wiring is conditional: only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (App Service sets
  it when Application Insights is enabled on the app). Local and test hosts are unchanged. Probe
  `evidence/probe-otel-wiring.sh` (output `probe-otel-wiring.txt`): this exact wiring builds on
  .NET 10 with 0 warnings and serves requests both without a connection string and with an
  unreachable one, so a dead ingestion endpoint never blocks a request.
- The existing JSON console logger stays, so App Service's log stream still shows the same lines.
- The structured fields land in `customDimensions` (`route`, `duration_ms`, `db_reads`, `op`,
  `outcome`, `sub`, `ip`). Queries use the message template prefix (`http.request`, `op.timing`,
  `auth.claim`). Exact column names are confirmed on the first production query (task), because KQL
  field names are only observable against real ingestion. If they differ, only the query files
  change.
- Daily cap 0.15 GB and 30-day retention are owner portal settings on `portfolio-appinsights`
  (runbook). They are checked by the settings check.

## R-07 Production edge purge (D-16 edge side)

- Existing code: `CloudflareEdgeProvider` purges by URL and warms anonymously. `EdgeWorker` purges
  and warms pages only when `Edge:PurgePages = true`. Page URLs are built on `Edge:SiteBaseUrl`, and
  image URLs on `Storage:ImageBaseUrl`.
- Decision: no new provider. Production sets `Edge:ZoneId` (the `zacal.me` zone),
  `Edge:ApiToken` (a token with only Zone > Cache Purge on that zone), `Edge:PurgePages = true`,
  `Edge:SiteBaseUrl = https://port.zacal.me`. No code change to the provider: its `Mode` label
  stays `dev-zone` (a rename would break `EdgeSecretTests.cs:158` for a cosmetic gain). Warm
  requests go through Cloudflare, so they carry the origin secret and pass the guard.
- Edge cache for HTML: a Cloudflare Cache Rule on `port.zacal.me` makes HTML eligible for caching
  and respects the origin's `s-maxage=86400`. It bypasses the cache when the request carries the
  `__Host-gallery` cookie, and for every path under `/admin`, `/signin-oidc`, `/signout-callback-oidc`
  and `/ui`. Admin responses already carry `no-store` (AdminHeaders). On the Free plan a cookie
  condition is available in Cache Rules. Images on `img.zacal.me`: cache everything, edge TTL 1
  year (D-19). Tiered Cache (Smart) and Early Hints are zone toggles.

## R-08 Production configuration and the startup guard (FR-014, FR-015)

- Facts: `Site:OutputCache` defaults to false (`SiteOptions`); IV.21 requires origin output caching,
  so production must set it. Secrets today come from user-secrets. In production they become App
  Service app settings (double underscore for sections), entered by the owner in the portal and
  never written to the repository.
- Decision: a `ProductionGuard` in `src/Gallery.Web/Hosting/` runs at startup when
  `Hosting:Target = production` and throws one exception listing every failed key, never a value.
  It checks:
  - every Storage, Mongo, Google and Edge credential is present
  - bucket names do not start with `gallery-dev-`, the image host is not `img-dev.`, the database
    does not end with `_dev`, and the connection string is not localhost
  - `Edge:PurgePages = true` and `Edge:SiteBaseUrl` is https and not localhost
  - `Site:OutputCache = true`
  - `Hosting:Edge = cloudflare` with a valid origin secret
  - `Hosting:DataProtectionKeysPath` is set
  It reuses `DevTargetGuard`'s constants, so the dev naming rule lives in one place. The existing
  `DevTargetGuard` (seed/removal command) already refuses Production (III.10), unchanged.
- Alternatives: rely on the runbook alone (rejected: a mis-pointed production would publish into
  dev buckets and nothing would say so).

## R-09 Log evidence queries (FR-019, SC-005, SC-006)

- Decision: `specs/012-production-infrastructure/evidence/kql/*.kql`, one query per criterion,
  run by `evidence/kql.sh` through `az monitor app-insights query --app portfolio-appinsights -g
  portfolio --analytics-query @file`. Its output goes to `evidence/kql-output.txt`. Each query
  excludes `status == 403` origin-guard lines and anything before a given `--offset`. Criteria
  covered:
  - 011 SC-005: `/` makes 0 reads; `/g` makes exactly 1 read; gallery and photo pages ≤ 2.
  - 006 SC-003/011 SC-006: p95 server `duration_ms` per public route under 50 ms.
  - 007 SC-006: ownership work and callback durations.
  - 012 SC-005: the `auth.claim` event with its `ip`.
  - 012 SC-003: count of `http.request` for a page within the cache window.
- The only `az` use in this feature is read-only: queries and the settings check, run on the
  owner's machine signed in as the owner.

## R-10 Runbook plus read-only settings check (FR-022, FR-023; Owner: made by hand)

- Decision: `infra/README.md` is the runbook. It is ordered, every step has exact names and values,
  and each step ends with a "check" line. `infra/check.sh` is the settings check. It is read-only,
  pinned to the subscription and resource group, and calls `az ... show` / `az ... list`,
  Cloudflare API GETs, and `dig`/`curl`. It prints one `PASS`/`FAIL <expected> <actual>` line per
  setting and exits non-zero on any FAIL. Secrets are only checked for presence and length. The
  Cloudflare read uses a separate read-only token the owner exports as `CF_READ_TOKEN`, which is not
  stored anywhere.
- The Cloudflare IP ranges are fetched live from `https://api.cloudflare.com/client/v4/ips`, so a
  range change shows up as a FAIL on the access-restriction row (spec edge case).
- Self-test: `infra/check.sh --selftest` feeds recorded JSON fixtures (one all-good, one with a
  single drifted value per section) through the same comparison code and must report 0 and N FAILs
  respectively. This proves the check can fail before production exists.

## R-11 Owner one-time values (Defaults, overturnable)

The plan uses exactly these values. Each one is an owner portal or dashboard step in the runbook.

| Item | Value |
|---|---|
| Site hostname | `port.zacal.me` (Owner) |
| Image hostname | `img.zacal.me` |
| R2 buckets | `gallery-prod-private`, `gallery-prod-public` |
| R2 token | Object Read & Write, those two buckets only (III.9) |
| Purge token | Zone > Cache Purge, zone `zacal.me` only |
| CORS on private bucket | AllowedOrigins `https://port.zacal.me`, Methods `PUT`, Headers `content-type`, MaxAge 3600 |
| Staging lifecycle | delete `staging/` after 7 days; `masters/` to Infrequent Access after 30 days |
| Atlas | Free cluster `gallery-prod` on Azure `westus` (Free supported there: `probe-atlas-region.txt`), database `gallery`, user `gallery-app` (readWrite on `gallery` only), network access = the app's outbound IPs |
| Google | OAuth client "gallery-prod" (owner, done), redirect `https://port.zacal.me/signin-oidc`, consent screen in Testing, owner as test user |
| Alert email | the owner's subscription email (budget action group) |
| Budget | `gallery-prod`, scope resource group `portfolio`, US$7 and US$10 actual-cost notifications |
| App Insights | `portfolio-appinsights`, daily cap 0.15 GB, retention 30 days |

## R-13 Origin TLS certificate (D-02 kept)

- Decision: Cloudflare Origin CA certificate (15-year) for `port.zacal.me`, uploaded to App Service
  as a PFX and bound with SNI. This is D-02 unchanged.
- Rejected: App Service Managed Certificate. Since 28 July 2025 it is validated over HTTP by
  DigiCert, and Microsoft Q&A ("App Service Managed Certificate renewal with Cloudflare-proxied
  CNAME") reports issuance and renewal failing behind a proxied record. The failure would surface
  only at renewal.
- The PFX and its private key never enter the repository. `.gitignore` already ignores `*.pfx`;
  the runbook adds `*.pem` and `*.key` under `infra/` (same commit as the runbook).

## R-12 Cost (VIII.1, VIII.3)

- Fixed: B2 Linux, US$26.28/month list price (owner's portal summary), inside the 5.0.0 ceiling.
- Metered: R2 under US$1 (existing estimate); Application Insights US$0 under 5 GB a month; Atlas
  Free US$0; Cloudflare Free US$0. The projected metered total is about US$1, under the US$7 alert.
  Section 8 of the design record is updated.
- The budget scopes to resource group `portfolio`, so it sees only this site's Azure spend. R2 is
  billed by Cloudflare, outside it, as today (design section 8 note).
