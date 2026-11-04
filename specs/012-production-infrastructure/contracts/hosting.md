# Contract: Hosting middleware and startup guard (012)

All new code lives in `src/Gallery.Web/Hosting/`. `AppBuilder.Build` gains exactly two calls:
`builder.Services.AddHosting(builder.Configuration);` after `AddEdge(...)`, and
`app.UseHosting();` as the FIRST middleware, before `UseRouting()`. No other bootstrap edits.

## HostingOptions (section `Hosting`)

```
Target                  string?  "production" turns on ProductionGuard; anything else = off
Edge                    string?  "cloudflare" turns on OriginGuard + forwarded headers; else off
OriginSecret            string?  required (>= 32 chars) when Edge == "cloudflare"
DataProtectionKeysPath  string?  when set: PersistKeysToFileSystem(path) + SetApplicationName("gallery")
```

Class, not record, and `ToString()` is NOT overridden (same rule as EdgeOptions: the secret can
never be printed). Comparisons are ordinal, case-insensitive for `Target`/`Edge`.

## AddHosting (services)

1. Bind and register `HostingOptions`.
2. If `Edge == "cloudflare"` and `OriginSecret` is shorter than 32 characters, throw
   `InvalidOperationException("Hosting:OriginSecret must be at least 32 characters when Hosting:Edge is cloudflare.")`.
3. If `DataProtectionKeysPath` is set: `AddDataProtection().PersistKeysToFileSystem(new
   DirectoryInfo(path)).SetApplicationName("gallery")`. Create the directory if missing.
4. If `Edge == "cloudflare"`: `Configure<ForwardedHeadersOptions>`:
   `ForwardedHeaders = XForwardedFor | XForwardedProto`, `ForwardedForHeaderName = "CF-Connecting-IP"`,
   `ForwardLimit = 1`, `KnownIPNetworks.Clear()`, `KnownProxies.Clear()`.
5. If `APPLICATIONINSIGHTS_CONNECTION_STRING` (configuration key) is non-empty:
   `AddOpenTelemetry().UseAzureMonitor(o => { o.ConnectionString = cs; o.SamplingRatio = 1.0F;
   o.TracesPerSecond = null; o.EnableTraceBasedLogsSampler = false; o.EnableLiveMetrics = false; })`.
   Package: `dotnet add src/Gallery.Web package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.6.0`.
6. If `Target == "production"`: run `ProductionGuard.Check(configuration)`; if the list is
   non-empty, throw `InvalidOperationException("Production configuration invalid: " + string.Join("; ", failures))`.

## UseHosting (pipeline, first middleware)

When `Edge == "cloudflare"`, in this order:

1. OriginGuard: read request header `X-Gallery-Origin`. If it is missing or does not equal
   `OriginSecret` (`CryptographicOperations.FixedTimeEquals` over UTF-8 bytes; a length mismatch is a
   mismatch), answer `403` with an empty body and `Cache-Control: no-store`, and do not call the
   next middleware. Log `origin.refused {reason}` with reason `missing` or `mismatch`, never the
   value received. Remove the header from the request before calling next, so no later code or log
   can read it.
2. `UseForwardedHeaders()`.

When `Edge` is not `cloudflare`: no-op (today's behavior). `RequestTimingMiddleware` stays where
it is, so refused requests are not logged as `http.request`; they are logged only as
`origin.refused`. Rationale: refused probes must not pollute the public-page timing evidence.

## ProductionGuard.Check(IConfiguration) → IReadOnlyList<string>

One message per failed rule, naming the key and the rule, never the value:

| Key | Rule |
|---|---|
| `Mongo:ConnectionString` | present; does not contain `localhost` or `127.0.0.1` |
| `Mongo:Database` | present; does not end with `DevTargetGuard.DevDatabaseSuffix` |
| `Storage:ServiceUrl`, `AccessKeyId`, `SecretAccessKey` | present |
| `Storage:PublicBucket`, `PrivateBucket` | present; do not start with `DevTargetGuard.DevBucketPrefix` |
| `Storage:ImageBaseUrl` | starts with `https://`; does not contain `img-dev.` |
| `Authentication:Google:ClientId`, `ClientSecret` | present |
| `Edge:ZoneId`, `Edge:ApiToken` | present |
| `Edge:PurgePages` | `true` |
| `Edge:SiteBaseUrl` | starts with `https://`; does not contain `localhost` |
| `Site:OutputCache` | `true` |
| `Hosting:Edge` | `cloudflare` |
| `Hosting:DataProtectionKeysPath` | present |

## Edge provider mode label

Unchanged. `CloudflareEdgeProvider.Mode` stays `"dev-zone"` in production logs. A `"zone"` label was
considered and dropped: `LibraryHost` sets `Edge:PurgePages=true`, so `EdgeSecretTests.cs:158`
(asserts `edge.purge gallery.publish dev-zone `) would go red, and the label is cosmetic. KQL
queries match on the op name and outcome, not the mode.

## Log events added

```
origin.refused {reason}            reason ∈ missing | mismatch
```

No change to `http.request`, `op.timing`, `auth.claim`, `auth.signin`, `auth.failed` templates. With
forwarded headers on, their `{ip}` is the visitor's `CF-Connecting-IP`.

## Tests (new files only; existing tests unmodified)

- `tests/Gallery.Web.Tests/HostingGuardTests.cs` [SAFETY]: in-process host with
  `Hosting:Edge=cloudflare` and a 40-char secret:
  - no header → 403, empty body
  - wrong header (same length, and different length) → 403
  - correct header → the request passes (`/` → 200)
  - `CF-Connecting-IP: 203.0.113.9` with the correct secret → the `auth.failed`/request IP seen by
    the app is `203.0.113.9` (assert through a test endpoint-free route: hit `/signin-oidc` with no
    state and read the `auth.failed {ip}` log line)
  - `X-Forwarded-Proto: https` → `/admin/signin` renders 200, not the antiforgery 500 (the probe-publish defect)
  - the secret value never appears in any captured log line
  - Edge unset → no header needed, `CF-Connecting-IP` ignored (logged IP is the loopback)
- `tests/Gallery.Web.Tests/ProductionGuardTests.cs` [SAFETY]: a complete valid production
  dictionary passes (empty list); then one test row per rule above flips one value and asserts
  exactly that key's message; and a host built with `Hosting:Target=production` and one bad key
  throws at `Build` with that key named and the bad value absent from the message.
- `tests/Gallery.Web.Tests/HostingDataProtectionTests.cs`: with `Hosting:DataProtectionKeysPath` set
  to a temp dir, two hosts built in sequence on the same path: a payload protected by host 1
  unprotects in host 2 (the restart case from R-05), and the directory contains a key XML file.
- `tests/Gallery.Web.Tests/HostingTelemetryTests.cs`: with no connection string, no
  `TracerProvider` is registered; with a syntactically valid, unreachable connection string the host
  starts and `/` answers 200 (probe-otel-wiring, as a test).
