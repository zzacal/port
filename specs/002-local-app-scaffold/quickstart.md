# Quickstart: Local App Scaffold (002)

Two parts. Part A is the scaffolding command sequence, which is the source of truth for every
generated file (Constitution VI, FR-003, SC-005). Part B is how to run and validate the result.

## Prerequisites

- .NET SDK **10.0.403** (`dotnet --list-sdks` must show it)
- Docker Desktop running (`open -a Docker`, then wait until `docker info` succeeds)

## Part A: Scaffolding (run once, from the repo root)

```bash
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

# SDK pin (R-01)
dotnet new globaljson --sdk-version 10.0.403 --roll-forward latestPatch

# repo files
dotnet new gitignore --force           # the existing .gitignore is backed up and re-appended (see note)
dotnet new editorconfig

# solution
dotnet new sln --format slnx -n Gallery

# projects (R-02)
dotnet new web      -o src/Gallery.Web  -n Gallery.Web  --no-https
dotnet new classlib -o src/Gallery.Core -n Gallery.Core
dotnet new install xunit.v3.templates
dotnet new xunit3   -o tests/Gallery.Core.Tests -n Gallery.Core.Tests
dotnet new xunit3   -o tests/Gallery.Web.Tests  -n Gallery.Web.Tests

dotnet sln Gallery.slnx add --solution-folder src   src/Gallery.Web src/Gallery.Core
dotnet sln Gallery.slnx add --solution-folder tests tests/Gallery.Core.Tests tests/Gallery.Web.Tests

# references: Core depends on nothing
dotnet add src/Gallery.Web            reference src/Gallery.Core
dotnet add tests/Gallery.Core.Tests   reference src/Gallery.Core
dotnet add tests/Gallery.Web.Tests    reference src/Gallery.Web src/Gallery.Core

# packages (R-03); no version means latest stable
dotnet add src/Gallery.Web package MongoDB.Driver
dotnet add src/Gallery.Web package Azure.Storage.Blobs
dotnet add src/Gallery.Web package Azure.Identity
dotnet add tests/Gallery.Web.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Gallery.Web.Tests package Testcontainers.MongoDb
dotnet add tests/Gallery.Web.Tests package Testcontainers.Azurite
for p in tests/Gallery.Core.Tests tests/Gallery.Web.Tests; do
  dotnet add $p package xunit.v3
  dotnet add $p package xunit.runner.visualstudio
  dotnet add $p package Microsoft.NET.Test.Sdk
done

dotnet build Gallery.slnx
```

Notes:
- `dotnet new gitignore` exits 73 if `.gitignore` exists. Copy the old file aside first, run with
  `--force`, then append the old entries (it currently ignores `.specify/feature.json`).
- After `dotnet new web`, change `applicationUrl` in
  `src/Gallery.Web/Properties/launchSettings.json` to `http://localhost:5090` (R-07). This is the
  only edit made to a generated config file, and it changes a value rather than structure.
- `docker-compose.yml`, `appsettings.Development.json` values, and source files are authored
  content, not generated scaffolding.

## Part B: Run and validate

```bash
docker compose up -d                  # Mongo 8.0 on 27017, Azurite blob on 10000
dotnet run --project src/Gallery.Web  # launch profile → Development, http://localhost:5090
```

| # | Check | Expected | Covers |
|---|---|---|---|
| 1 | Open http://localhost:5090 | Placeholder page "Gallery is running" | US1-1, FR-005 |
| 2 | `curl -s localhost:5090/health` | 200, `self`, `mongo` and `blob` all `Healthy` | US1-2, FR-007 |
| 3 | `curl -sI localhost:5090/ \| grep -i server-timing` | `Server-Timing: app;dur=…` | US3-2, FR-009 |
| 4 | `docker compose stop`, then repeat 1 and 2 | Page still 200; health 503 naming `mongo` and `blob`, within about 2.5 s | US1-3, FR-007 |
| 5 | `docker compose start`, then send 20 requests and count `"http.request"` lines in the console | exactly 20 | SC-004, FR-008 |
| 6 | `dotnet test` (Docker running) | all pass in under 3 min; afterwards `docker ps` lists no testcontainers | US2-1/2, SC-003 |
| 7 | Quit Docker, then `dotnet test` | Core tests pass; Web tests fail naming Docker | US2-3 |
| 8 | In a temp copy of the repo, raise `global.json` to an SDK patch that isn't installed (e.g. 10.0.404), then run `dotnet build` | fails with "A compatible .NET SDK was not found … Requested SDK version: 10.0.404". The same mechanism rejects 10.0.103 on a machine where it's the only SDK. | SC-006 |
| 9 | Re-run Part A in an empty temp directory, then `dotnet build -warnaserror` | 0 errors, 0 warnings | SC-005 |

Reset local data: `docker compose down -v`.

Evidence for checks 5 and 6 is captured into `specs/002-local-app-scaffold/evidence/` by the
tasks phase (the console log redirected to a file, plus the test output).
