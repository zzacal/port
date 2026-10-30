# Research: Local App Scaffold (002)

All facts below were checked on 2026-10-27 against live sources (the .NET release index, the
NuGet API, Docker Hub, and the installed SDK's own `dotnet new` help output). No
NEEDS CLARIFICATION items remain.

## R-01: .NET SDK version and pin

- **Decision**: Pin SDK **10.0.403** in `global.json` with `rollForward: latestPatch`, created
  with `dotnet new globaljson --sdk-version 10.0.403 --roll-forward latestPatch`.
- **Rationale**: The official release index lists .NET 10 (LTS, "active") as the newest released
  channel, with latest SDK 10.0.403 and runtime 10.0.14. .NET 11 is 11.0.0-rc.3 ("go-live"),
  which is a preview and excluded by FR-002. The owner has since installed 10.0.403 alongside
  10.0.103 (`dotnet --list-sdks`). `latestPatch` only rolls forward within the 10.0.4xx feature
  band, so an older SDK such as 10.0.103 fails resolution, and a preview SDK never satisfies it,
  because `allowPrerelease` defaults to false when a version is pinned. That covers FR-002 and
  SC-006, and the SDK's own resolution error names the required version.
- **Alternatives**: `latestFeature` or `latestMinor` were rejected because they let a different
  feature band or a future 10.1 satisfy the pin, so builds stop being reproducible. Having no
  global.json was rejected because 10.0.103 would build silently, violating FR-002.

## R-02: Solution format and project templates

- **Decision**: `dotnet new sln --format slnx -n Gallery`. Use `web` (ASP.NET Core Empty,
  `--no-https`) for Gallery.Web and `classlib` for Gallery.Core. The test projects use
  **xUnit v3** via the official `xunit.v3.templates` package (`dotnet new install
  xunit.v3.templates`, then `dotnet new xunit3`). Packages are then brought to the latest stable
  versions with `dotnet add package <id>`, which picks the latest stable version when none is
  given.
- **Rationale**: slnx is the SDK 10 default and is human-readable. The SDK's bundled `xunit`
  template still ships xunit 2.9.3 and Microsoft.NET.Test.Sdk 17.14.1. The current stable versions
  are xunit.v3 4.0.2, Microsoft.NET.Test.Sdk 18.11.0, and xunit.runner.visualstudio 4.0.1 (NuGet,
  2026-10-27). xUnit's own v3 template is still an official CLI path (Constitution VI) and avoids
  starting the repository on a legacy test framework. HTTPS is off locally because Cloudflare
  terminates TLS in production and the app only ever sees HTTP behind forwarded headers
  (D-02, PC). A local dev certificate would test nothing real.
- **Alternatives**: The bundled `xunit` (v2) template was rejected as legacy, since every later
  feature would inherit it. NUnit and MSTest were rejected for continuity with port. `.sln` was
  rejected because slnx is the default and the existing design already names `Gallery.slnx`.

## R-03: Packages (Constitution V.2 justification)

| Project | Package | Version (latest stable 2026-10-27) | Justified by |
|---|---|---|---|
| Gallery.Web | MongoDB.Driver | 3.13.0 | FR-006, FR-007 (datastore connection and health) |
| Gallery.Web | Azure.Storage.Blobs | 12.30.1 | FR-006, FR-007 (storage connection and health) |
| Gallery.Web | Azure.Identity | 1.21.0 | FR-006 ("same build runs hosted without code changes": managed identity when only a service URI is configured) |
| Gallery.Web.Tests | Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | FR-011 (in-process app host for integration tests) |
| Gallery.Web.Tests | Testcontainers.MongoDb | 4.16.0 | FR-010 (disposable datastore) |
| Gallery.Web.Tests | Testcontainers.Azurite | 4.16.0 | FR-010 (disposable storage emulator) |
| both test projects | xunit.v3, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | 4.0.2 / 4.0.1 / 18.11.0 | FR-010, FR-011 (comes from the template, bumped to latest) |

Built in, so no package is needed:
- health checks: `AddHealthChecks`, part of the ASP.NET Core shared framework
- JSON console logging: `AddJsonConsole`
- request timing: middleware written in the project

Rejected:
- `AspNetCore.HealthChecks.MongoDb` and `AspNetCore.HealthChecks.AzureStorage`: each check is
  about ten lines, and the packages add transitive dependencies.
- Serilog: JSON console logging is built in.
- `Microsoft.OpenTelemetry` (the Application Insights exporter): deferred to the hosting feature,
  because Application Insights is out of scope locally.

## R-04: Request timing and Server-Timing

- **Decision**: Use a small middleware registered after routing, so the endpoint's route pattern
  is known. It starts a `Stopwatch`, registers `Response.OnStarting` to write
  `Server-Timing: app;dur=<ms>`, and after `next()` logs one `http.request` event with `route`
  (the route pattern, or the raw path when no endpoint matched), `method`, `status`, and
  `duration_ms`. Dependency checks log `op.timing` with `op`, `duration_ms`, and `outcome`. The
  header value is formatted by a pure function in Gallery.Core (`ServerTiming.Format`). That
  function is what the unit test exercises, so FR-011's unit test asserts something real.
- **Rationale**: FR-008 requires exactly one event per request carrying the route.
- **Alternatives**: The built-in HTTP logging (`AddHttpLogging` with `CombineLogs = true` and the
  `Duration` field) was rejected because it logs the raw path rather than the route pattern and
  cannot add `Server-Timing`.

## R-05: Health checks

- **Decision**: Map `/health`, which returns JSON with an overall status plus one entry per check
  (`self`, `mongo`, `blob`), each with status, duration, and an error description on failure.
  Each check has a 2-second timeout:
  - Mongo: `ping` command, with `ServerSelectionTimeout` set to 2 s for the health client.
  - Blob: `GetPropertiesAsync` on the service client, with retries disabled for the health client.

  The HTTP status is 200 when all checks are healthy and 503 when any is unhealthy, but the body
  always lists every check. The home page touches no dependency, so it keeps loading while one is
  down (FR-007).
- **Rationale**: The default Mongo server selection timeout (30 s) and the default Blob retry
  policy would make `/health` hang for tens of seconds when Docker is stopped. Short timeouts keep
  the health report fast and still accurate.
- **Alternatives**: Checking dependencies at startup was rejected because the app would crash on
  boot without them (edge case: it must start anyway).

## R-06: Local supporting services

- **Decision**: Use a `docker-compose.yml` at the repository root with:
  - `mongo:8.0` on 27017. Atlas M0 runs the 8.0 line, so local matches hosted.
  - `mcr.microsoft.com/azure-storage/azurite` on 10000–10002, with the blob service only
    (`azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck`).

  Named volumes persist data. Reset is `docker compose down -v`. Image tags are pinned to the
  current release tag at implementation time.
- **Rationale**: FR-004 asks for persistence and a reset command. `--skipApiVersionCheck` stops
  Azurite rejecting the newer API version that Azure.Storage.Blobs 12.30 sends. When a port is
  already in use, Compose reports "port is already allocated" with the port number, which covers
  the port-conflict edge case without extra work.
- **Alternatives**: Running Mongo and Azurite as host installs (Homebrew, npm) was rejected
  because it is not reproducible. Using Testcontainers for local runs as well was rejected because
  data would not persist between app restarts.
- **Note**: Docker Desktop was not running during planning (`docker info` could not reach the
  socket). Implementation must start it first: `open -a Docker`, then poll `docker info`.

## R-07: Local address and configuration

- **Decision**:
  - Kestrel listens on `http://localhost:5090`, set in the generated `launchSettings.json`. The
    template picked a random port, and 5090 is chosen to avoid macOS AirPlay on 5000.
  - Configuration keys are `Mongo:ConnectionString`, `Mongo:Database`, and either
    `Storage:ConnectionString` or `Storage:ServiceUri`. When `ServiceUri` is set, the app uses
    `DefaultAzureCredential` (managed identity in Azure). Otherwise it uses the connection string.
  - Development values live in `appsettings.Development.json`: Mongo on localhost with no auth,
    and Azurite's well-known public development connection string. These are not secrets, which
    satisfies FR-006.
- **Rationale**: The same build runs locally and when hosted, and only configuration changes.
  Azurite's well-known account key is published by Microsoft and is safe to commit.
- **Alternatives**: User secrets were rejected because there is nothing secret locally.
- **Pitfall recorded**: `dotnet run --no-launch-profile` defaults to Production and skips
  `appsettings.Development.json`. The quickstart uses the launch profile, or sets
  `ASPNETCORE_ENVIRONMENT=Development` explicitly.

## R-08: One-command test run without Docker

- **Decision**: Run `dotnet test` at the repository root against `Gallery.slnx`. Integration
  tests share one Mongo container and one Azurite container per test assembly through an xUnit v3
  assembly fixture. Each test gets a fresh database name and container prefix, so there is no
  carry-over between tests. Testcontainers' Ryuk reaper removes containers even after a crashed
  run (SC-003, edge case "run twice").
- **Without Docker**: Testcontainers throws `DockerUnavailableException` ("Docker is either not
  running or misconfigured"), which names the cause (US2 scenario 3). Unit tests live in a
  separate assembly and still pass.
- **Alternatives**: Per-test containers were rejected because each start takes 2–5 s and would
  break the 3-minute budget (SC-003) as tests grow.

## R-09: Accepting P-01 into the design record

- **Decision**: P-01 moves into the design as **D-22: Solution and Project Layout**, added to
  `specs/001-architecture-stack-design/draft/02a-decisions-platform.md`. It is assembled with
  `evidence/assemble.sh` and checked with `evidence/validate.sh`. `docs/proposals.md` marks P-01
  `accepted`. The xUnit v3 refinement (R-02) is recorded in D-22.
- **Rationale**: Constitution VII.2 says a later feature that changes the design updates the
  design document in the same feature.
