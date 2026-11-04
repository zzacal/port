# Production runbook (012)

Azure portal: https://portal.azure.com > subscription `c28725a0-a61d-4a6c-999a-0a8980111fbf` > resource group `portfolio`.
Cloudflare: https://dash.cloudflare.com > zone `zacal.me`.
Check command: `bash infra/check.sh <default hostname>` (az signed in).
DNS wildcard: explicit `port` and `img` records override the existing `*.zacal.me` wildcard for those two names only.

## 1. Azure app settings

1. Done 2026-11-02: App Service > Create > Resource group `portfolio` > Name `portfolio-web` > Publish `Code` > Runtime `.NET 10 (LTS)` > Linux > West US > Plan `portfolio-plan` `Basic B2` > Basic authentication `Disabled` > Application Insights `portfolio-appinsights` > Create.
2. portfolio-web > Settings > Configuration > General settings > Always on > `On` > Save.
3. portfolio-web > Settings > Configuration > General settings > HTTPS Only > `On` > Save.
4. portfolio-web > Settings > Configuration > General settings > FTP state > `Disabled` > Save.
5. portfolio-web > Settings > Configuration > General settings > Minimum Inbound TLS Version > `1.2` > Save.
6. Done 2026-11-02: portfolio-web > Settings > Configuration > General settings > SCM Basic Auth Publishing Credentials > `Off`; FTP Basic Auth Publishing Credentials > `Off` > Save.
7. portfolio-appinsights > Configure > Usage and estimated costs > Daily cap > `0.15` GB/day > OK.
8. portfolio-appinsights > Configure > Usage and estimated costs > Data Retention > `30 days` > Apply.

Check: AZ-01, AZ-02, AZ-03, AZ-04, AZ-05, AZ-06, AZ-07, AZ-15, AZ-16.

## 2. Cloudflare R2

1. R2 Object Storage > Create bucket > Name `gallery-prod-private` > Location Automatic > Create bucket.
2. R2 Object Storage > Create bucket > Name `gallery-prod-public` > Location Automatic > Create bucket.
3. R2 > gallery-prod-public > Settings > Custom Domains > Connect Domain > `img.zacal.me` > Continue > Connect domain.
4. R2 > gallery-prod-public > Settings > Public Development URL (r2.dev) > `Disabled` > Disallow.
5. R2 > gallery-prod-private > Settings > Public Development URL (r2.dev) > `Disabled`.
6. R2 > gallery-prod-private > Settings > CORS Policy > Add CORS policy > `[{"AllowedOrigins":["https://port.zacal.me"],"AllowedMethods":["PUT"],"AllowedHeaders":["content-type"],"MaxAgeSeconds":3600}]` > Save.
7. R2 > gallery-prod-private > Settings > Object lifecycle rules > Add rule > Name `staging-7d` > Prefix `staging/` > Delete uploaded objects after `7` days > Save.
8. R2 > gallery-prod-private > Settings > Object lifecycle rules > Add rule > Name `masters-ia` > Prefix `masters/` > Transition to Infrequent Access after `30` days > Save.
9. Done 2026-11-02: R2 > Manage API tokens > Create API token > Permissions `Object Read & Write` > Specify buckets `gallery-prod-private`, `gallery-prod-public` > Create API Token > saved to `.r2-keys-prod-copy`.

Check: NET-06, NET-07, NET-08 (lifecycle: no row).

## 3. MongoDB Atlas

1. Terminal > `brew install mongodb-atlas-cli`.
2. Terminal > `atlas auth login` > browser > Authorize.
3. portfolio-web > Settings > Networking > Outbound traffic configuration > Outbound addresses > copy every IPv4 address.
4. Terminal, repo root > `bash infra/atlas.sh <ip1> <ip2> ...` (project `gallery-prod`, cluster `gallery-prod` M0 AZURE US_WEST, user `gallery-app` readWrite@gallery).
5. Copy the line under "paste into Mongo__ConnectionString" > keep it private > step 7.4.

Check: AZ-11 (`Mongo__ConnectionString present`).

## 4. Google

1. Done 2026-11-02: Google Cloud console > APIs & Services > Credentials > Create credentials > OAuth client ID > Web application > Name `gallery-prod` > Authorized redirect URIs `https://port.zacal.me/signin-oidc` > Create.
2. Done 2026-11-02: Google Cloud console > APIs & Services > OAuth consent screen > Publishing status `Testing` > Test users > owner's Google account > Save.

Check: AZ-11 (`Authentication__Google__ClientSecret present`), AZ-12 (`Authentication__Google__ClientId`).

## 5. Cloudflare zone

A. Domain on Azure
1. Done 2026-11-02: portfolio-web > Settings > Custom domains > Add custom domain > All other domain services > Add certificate later > `port.zacal.me` > copy Domain verification ID.
2. Done 2026-11-02: Cloudflare > DNS > Records > Add record > TXT > Name `asuid.port` > Content the verification ID > Save.
3. Done 2026-11-02: Cloudflare > DNS > Records > Add record > CNAME > Name `port` > Target the default hostname > Proxied > Save.
4. Done 2026-11-02: portfolio-web > Custom domains > Validate > Add.

B. Certificate
5. Done 2026-11-02: Cloudflare > SSL/TLS > Origin Server > Create Certificate > RSA (2048) > Hostnames `port.zacal.me` > 15 years > Create > save as `infra/port-origin.pem` and `infra/port-origin.key`.
6. Done 2026-11-02: Terminal > `openssl pkcs12 -export -in infra/port-origin.pem -inkey infra/port-origin.key -out infra/port-origin.pfx` > export password.
7. Done 2026-11-02: portfolio-web > Settings > Certificates > Bring your own certificates (.pfx) > Add certificate > `infra/port-origin.pfx` > password > Name `port-origin` > Add.
8. Done 2026-11-02: portfolio-web > Custom domains > port.zacal.me > Add binding > Certificate `port-origin` > TLS/SSL type `SNI SSL` > Add.
9. Done 2026-11-02: delete `infra/port-origin.pem`, `infra/port-origin.key`, `infra/port-origin.pfx`.

C. Full (strict) for port only
10. Done 2026-11-02: Cloudflare > Rules > Configuration Rules > Create rule > Name `port strict` > Hostname equals `port.zacal.me` > SSL `Full (strict)` > Deploy.

D. HTTPS redirect for port only
11. Done 2026-11-02: Cloudflare > Rules > Redirect Rules > Create rule > Name `port https` > Hostname equals `port.zacal.me` AND SSL/HTTPS is Off > Dynamic `concat("https://port.zacal.me", http.request.uri)` > 301 > Preserve query string on > Deploy.

E. Origin secret header
12. Done 2026-11-02: Terminal > `openssl rand -hex 32` > keep the 64-character value private (step 7.4).
13. Done 2026-11-02: Cloudflare > Rules > Transform Rules > Modify Request Header > Create rule > Name `port origin secret` > Hostname equals `port.zacal.me` > Set static > Header `X-Gallery-Origin` > Value the 64-character value > Deploy.

F. Cache rules
14. Done 2026-11-02: Cloudflare > Caching > Cache Rules > Create rule > Name `port bypass` > `(http.host eq "port.zacal.me" and (starts_with(http.request.uri.path, "/admin") or starts_with(http.request.uri.path, "/signin-oidc") or starts_with(http.request.uri.path, "/signout-callback-oidc") or starts_with(http.request.uri.path, "/ui") or http.cookie contains "__Host-gallery"))` > Bypass cache > Deploy.
15. Done 2026-11-02: Cache Rules > Create rule > Name `port pages` > Hostname equals `port.zacal.me` > Eligible for cache > Edge TTL `Use cache-control header if present, bypass cache if not` > Browser TTL `Respect origin` > Deploy.
16. Done 2026-11-02: Cache Rules > Create rule > Name `img` > Hostname equals `img.zacal.me` > Eligible for cache > Edge TTL `Ignore cache-control header and use this TTL` `1 year` > Browser TTL `Respect origin` > Deploy.
17. Done 2026-11-02: Cache Rules list > order `port pages` above `port bypass`.

G. Zone toggles
18. Done 2026-11-02: Cloudflare > Caching > Tiered Cache > Smart Tiered Caching Topology > On.
19. Done 2026-11-02: Cloudflare > Speed > Optimization > Content Optimization > Early Hints > On.

Purge token
20. Done 2026-11-02: Cloudflare > My Profile > API Tokens > Create Token > Custom > Permissions `Zone` `Cache Purge` `Purge` > Zone Resources `Include` `Specific zone` `zacal.me` > Create Token > saved to `.r2-keys-prod-copy`.

Check: AZ-10, NET-02, NET-03, NET-04, NET-05, NET-09 (MANUAL).

## 6. App Service access restrictions

1. Browser > `https://www.cloudflare.com/ips-v4` and `https://www.cloudflare.com/ips-v6` > copy the ranges.
2. portfolio-web > Settings > Networking > Public network access > Enabled from select virtual networks and IP addresses > Main site > Unmatched rule action `Deny` > Save.
3. Main site > + Add > Name `cf-v4-a` > Action `Allow` > Priority `100` > Type `IPv4` > IP address block first 8 v4 ranges, comma-separated > Add rule.
4. Main site > + Add > Name `cf-v4-b` > Action `Allow` > Priority `110` > Type `IPv4` > IP address block remaining v4 ranges, comma-separated > Add rule.
5. Main site > + Add > Name `cf-v6` > Action `Allow` > Priority `120` > Type `IPv6` > IP address block all v6 ranges, comma-separated > Add rule.
6. Advanced tool site > Use main site rules > `Off` > Unmatched rule action `Allow` > Save.

Check: AZ-08, AZ-09, NET-01.

## 7. App settings

portfolio-web > Settings > Environment variables > App settings > + Add > Name > Value > Apply, one row each; then Apply > Confirm.

1. `ASPNETCORE_ENVIRONMENT` = `Production`
2. `Hosting__Target` = `production`
3. `Hosting__Edge` = `cloudflare`
4. `Hosting__OriginSecret` = the 64-character value from 5.12 (the same value as Cloudflare rule `port origin secret`)
5. `Hosting__DataProtectionKeysPath` = `/home/data/dataprotection-keys`
6. `Site__OutputCache` = `true`
7. `Mongo__ConnectionString` = the line from 3.5
8. `Mongo__Database` = `gallery`
9. `Storage__ServiceUrl` = `https://<account-id>.r2.cloudflarestorage.com` (Cloudflare > R2 Object Storage > Account Details > Account ID)
10. `Storage__AccessKeyId` = Access Key ID from `.r2-keys-prod-copy`
11. `Storage__SecretAccessKey` = Secret Access Key from `.r2-keys-prod-copy`
12. `Storage__PublicBucket` = `gallery-prod-public`
13. `Storage__PrivateBucket` = `gallery-prod-private`
14. `Storage__ImageBaseUrl` = `https://img.zacal.me`
15. `Authentication__Google__ClientId` = Google Cloud console > Credentials > `gallery-prod` > Client ID
16. `Authentication__Google__ClientSecret` = Google Cloud console > Credentials > `gallery-prod` > Client secret
17. `Edge__ZoneId` = Cloudflare > zacal.me > Overview > API > Zone ID
18. `Edge__ApiToken` = purge token from `.r2-keys-prod-copy`
19. `Edge__PurgePages` = `true`
20. `Edge__SiteBaseUrl` = `https://port.zacal.me`
21. Done 2026-11-02: `APPLICATIONINSIGHTS_CONNECTION_STRING` = set by the portal (Application Insights enabled).
22. Terminal > `rm .r2-keys-prod-copy`.

Check: AZ-11, AZ-12.

## 8. First deploy

1. Done 2026-11-02: Managed Identities > Create > Resource group `portfolio` > Name `portfolio-deploy` > West US > Create.
2. Done 2026-11-02: portfolio-deploy > Settings > Federated credentials > Add Credential > GitHub Actions > Organization `zzacal` > Repository `port` > Entity `Branch` > Branch `main` > Name `github-port-main` > Audience `api://AzureADTokenExchange` > Add.
3. Done 2026-11-02: portfolio-web > Access control (IAM) > Add role assignment > `Website Contributor` > Managed identity `portfolio-deploy` > Review + assign.
4. Done 2026-11-02: GitHub > zzacal/port > Settings > Secrets and variables > Actions > Variables > `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
5. Terminal > merge the feature branch into `main` > `git push origin main`.
6. GitHub > zzacal/port > Actions > `ci` > jobs `test` and `deploy` green.

Check: AZ-13, AZ-14, NET-01, NET-02, NET-05.

## 9. Claim

1. Browser > `https://port.zacal.me/admin` immediately after step 8.6 > Sign in with Google (owner account).
2. Terminal > `bash specs/012-production-infrastructure/evidence/kql.sh claim` > one `auth.claim` with your Google subject.
3. If another subject won: Atlas > gallery-prod > Browse Collections > `gallery.site` > delete the `owner` fields > Update > repeat 9.1.
4. Share the address only after 9.2 shows your subject.

Check: no check.sh row (kql.sh claim).

## 10. Rotation

Order for every secret: create new > update app setting > restart > verify > revoke old.

1. R2 token: Cloudflare > R2 > Manage API tokens > Create API token (as 2.9) > portfolio-web > Environment variables > `Storage__AccessKeyId`, `Storage__SecretAccessKey` > Apply > Confirm > portfolio-web > Overview > Restart > upload one photo in `/admin` > R2 > Manage API tokens > old token > Delete.
2. Purge token: Cloudflare > My Profile > API Tokens > Create Token (as 5.20) > `Edge__ApiToken` > Apply > Confirm > Restart > publish a gallery > `kql.sh edge` > old token > Delete.
3. Origin secret: Terminal > `openssl rand -hex 32` > Cloudflare > Transform Rules > `port origin secret` > Value new value > Save > `Hosting__OriginSecret` new value > Apply > Confirm > Restart > `bash infra/check.sh <default hostname>` (NET-01, NET-02 PASS).
4. Google secret: Google Cloud console > Credentials > `gallery-prod` > Add secret > `Authentication__Google__ClientSecret` > Apply > Confirm > Restart > sign in at `/admin` > old secret > Disable > Delete.
5. Origin CA certificate: repeat 5.5–5.9 > portfolio-web > Custom domains > port.zacal.me > binding > new certificate > Update > `bash infra/check.sh <default hostname>` (AZ-10, NET-02 PASS) > Certificates > old > Delete > Cloudflare > SSL/TLS > Origin Server > old certificate > Revoke.
6. Atlas password: Atlas > gallery-prod > Database Access > `gallery-app` > Edit > Edit Password > Autogenerate > `Mongo__ConnectionString` new password > Apply > Confirm > Restart > open `https://port.zacal.me/g`.

Check: AZ-10, AZ-11, NET-01, NET-02.

## 11. Not to do

1. Never enable basic authentication (SCM or FTP).
2. Never use portfolio-web > Deployment Center > Continuous deployment.
3. Never run the seed or removal command against production.
4. Never share `https://port.zacal.me` before step 9.2.

Check: AZ-07.
