# Data Model: Production Infrastructure (012)

No datastore collection, document field, or index changes. Production starts with an empty
database `gallery` on the Atlas Free cluster. The app creates its indexes on first use
(`Indexes.EnsureOnceAsync`), as on the dev database.

The entities are configuration and environment, described here so the guard, the runbook and the
settings check agree on one list.

## Production configuration (App Service app settings on `portfolio-web`)

Section separator in app settings is `__`. S = secret: entered by the owner in the portal, never in
the repository, logged only as present/absent and length.

| Key | Value / rule | S |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `Hosting__Target` | `production`: turns on ProductionGuard | |
| `Hosting__Edge` | `cloudflare`: origin guard + forwarded headers | |
| `Hosting__OriginSecret` | ≥ 32 chars, same value as the Cloudflare transform rule | S |
| `Hosting__DataProtectionKeysPath` | `/home/data/dataprotection-keys` | |
| `Site__OutputCache` | `true` | |
| `Mongo__ConnectionString` | Atlas SRV string for user `gallery-app`; not localhost | S |
| `Mongo__Database` | `gallery`; must not end with `_dev` | |
| `Storage__ServiceUrl` | `https://<account-id>.r2.cloudflarestorage.com` | |
| `Storage__AccessKeyId` / `Storage__SecretAccessKey` | prod R2 token, two buckets only | S |
| `Storage__PublicBucket` / `Storage__PrivateBucket` | `gallery-prod-public` / `gallery-prod-private`; must not start with `gallery-dev-` | |
| `Storage__ImageBaseUrl` | `https://img.zacal.me`; must not contain `img-dev.` | |
| `Authentication__Google__ClientId` / `__ClientSecret` | the "gallery-prod" OAuth client | S (secret) |
| `Edge__ZoneId` | zone id of `zacal.me` | |
| `Edge__ApiToken` | Cache Purge only, zone `zacal.me` | S |
| `Edge__PurgePages` | `true` | |
| `Edge__SiteBaseUrl` | `https://port.zacal.me` | |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | set by the portal when App Insights is enabled | S |

Validation (ProductionGuard, contracts/startup-guard.md): all rows required when
`Hosting__Target=production`, with the stated rules. Failure means the process exits at startup
with one message listing every failing key and never a value.

## Production environment (resources)

| Resource | Name | Where | Created by |
|---|---|---|---|
| Resource group | `portfolio` | sub `c28725a0-…` | owner (done) |
| App Service plan | `portfolio-plan`, Linux B2 | West US | owner (done) |
| Web App | `portfolio-web`, .NET 10, Always On | West US | owner (done) |
| Application Insights | `portfolio-appinsights`, cap 0.15 GB/day, 30 days | West US | owner (done; cap/retention pending) |
| Managed identity | `portfolio-deploy` + federated credential `github-port-main`, Website Contributor on `portfolio-web` | `portfolio` | owner (done) |
| Custom domain | `port.zacal.me` on `portfolio-web`, Cloudflare Origin CA certificate (PFX upload, SNI binding) | | owner |
| Cloudflare DNS | `port` CNAME → `portfolio-web.<suffix>.azurewebsites.net` (proxied); `asuid.port` TXT | zone `zacal.me` | owner |
| Cloudflare settings | Configuration Rule on `port.zacal.me`: SSL Full (strict); Redirect Rule `http://port.zacal.me/*` → https (301); zone toggles Tiered Cache Smart, Early Hints | zone | owner |
| Cloudflare rules | transform `X-Gallery-Origin`; cache rules for `port` and `img` | zone | owner |
| R2 | `gallery-prod-private`, `gallery-prod-public`, `img.zacal.me` custom domain, r2.dev off, CORS, lifecycle | account | owner |
| Tokens | R2 Object R/W (2 buckets), Cache Purge (zone), read-only check token | account | owner |
| Atlas | Free cluster `gallery-prod` (Azure westus), user `gallery-app`, IP access list | Atlas | owner |
| Google | OAuth client `gallery-prod`, consent screen Testing, owner as test user | GCP project | owner (done) |
| GitHub | variables `AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID` | `zzacal/port` | owner (done) |

TLS between Cloudflare and the origin uses a Cloudflare Origin CA certificate for `port.zacal.me`
(D-02, unchanged: free, 15-year, trusted by Cloudflare in Full (strict), VIII.2). The owner creates
it in the Cloudflare dashboard, converts it to a password-protected PFX locally with `openssl
pkcs12 -export` (runbook), uploads it under App Service > Certificates > Bring your own (.pfx), and
binds it to the custom domain with SNI SSL. Basic tier supports custom-domain TLS. The custom
domain is verified by the `asuid.port` TXT record, so the CNAME can stay proxied throughout.
Rejected: the free App Service Managed Certificate. Since 28 July 2025 it validates by DigiCert
fetching an HTTP token from the hostname, which Microsoft Q&A reports failing behind a
Cloudflare-proxied CNAME at renewal time. That would make TLS expire silently six months in.

## State: environment lifecycle

`not provisioned → provisioned (runbook steps 1–N, check.sh PASS) → deployed (first main push
green) → claimed (owner's first /admin sign-in; auth.claim queried) → live (address shared)`.
The runbook forbids sharing the address before "claimed" (R-01).
