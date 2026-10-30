# Implementation Plan: Local App Scaffold

**Branch**: `002-local-app-scaffold` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-local-app-scaffold/spec.md`

## Summary

Create the runnable skeleton:
- `Gallery.slnx` with the four P-01 projects, all created with `dotnet` CLI commands
- the SDK pinned to the latest released patch (10.0.403)
- Docker Compose for local Mongo and Azurite
- a placeholder home page on port 5090
- a `/health` endpoint reporting each dependency, with short timeouts
- one `http.request` log event per request plus `Server-Timing`
- an xUnit v3 test suite whose integration tests start their own containers

No domain behaviour. P-01 is accepted and recorded as design entry D-22.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.403, runtime 10.0.14), the latest released version (research R-01)

**Primary Dependencies**:
- ASP.NET Core minimal APIs (shared framework)
- MongoDB.Driver 3.13.0
- Azure.Storage.Blobs 12.30.1
- Azure.Identity 1.21.0

All are justified in research R-03.

**Storage**: MongoDB 8.0 (local container) and the Azurite blob emulator. Connectivity only; no collections are created in this feature.

**Testing**:
- xUnit v3 4.0.2
- Microsoft.AspNetCore.Mvc.Testing 10.0.14
- Testcontainers.MongoDb and Testcontainers.Azurite 4.16.0

**Target Platform**: macOS for local development. The build must also run unchanged on App Service Linux later (FR-006).

**Project Type**: Web service (server-rendered), plus a pure-logic class library.

**Performance Goals**:
- First page served in under 10 s after the app starts (SC-002)
- `/health` responds in about 2.5 s or less even with dependencies down

**Constraints**:
- No port 5000
- No secrets locally
- Every generated file comes from the CLI (Constitution VI)
- Zero build warnings (SC-005)

**Scale/Scope**: 4 projects, 2 endpoints, 3 health checks, and about 6 tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Single owner | PASS (justified N/A) | No identity yet. The claim-once feature owns it. Nothing here opens an admin surface. |
| II. Visibility | PASS (justified N/A) | No galleries or images yet. |
| III. Images leak nothing | PASS (justified N/A) | No image handling yet. Storage is only connectivity-checked. |
| IV.1 request event | PASS | R-04 middleware logs `http.request` with route, status, and duration. Contract invariant 1 is tested. |
| IV.2 operation timing | PASS | `op.timing` events for `health.mongo` and `health.blob` (data-model). |
| IV.3 Server-Timing | PASS | Set on every response, including `/`. Contract invariant 2 is tested. |
| IV.4 verifiable from logs | PASS | SC-002 and SC-004 are checked from captured console logs (quickstart check 5, evidence/). |
| IV.5–IV.22 perceived performance | PASS (justified N/A) | There are no public image pages yet. The placeholder page has no images or scripts. |
| V.1 scope | PASS | FR-013 excludes all feature behaviour. |
| V.2 dependencies | PASS | Every package has a row in research R-03. Built-in health checks and logging are used instead of community packages. |
| V.3 no JS on public pages | PASS | The placeholder page ships no script. |
| VI. CLI scaffolding | PASS | quickstart Part A is pure CLI commands. The single launchSettings edit changes one value and is called out explicitly. The xUnit v3 template is installed through the CLI. |
| VII. Design before code | PASS | Feature 001 is done. P-01 becomes D-22 in the design source of truth during this feature (R-09). |
| VIII. Cost | PASS (justified N/A) | Local only; no hosted spend. |
| Platform Constraints | PASS | .NET 10, minimal APIs, Mongo, Blob behind configuration. With `Storage:ServiceUri` set, the app uses managed identity and no keys. Forwarded headers are deferred to the hosting feature, because there's no proxy locally. |

No violations, so Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: unchanged, all PASS. The one design adjustment found
in research was moving from the bundled xUnit v2 template to xUnit v3 through the official
template package. That adds no runtime dependency and is recorded in D-22.

## Project Structure

### Documentation (this feature)

```text
specs/002-local-app-scaffold/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/http.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
global.json                      # dotnet new globaljson
Gallery.slnx                     # dotnet new sln --format slnx
docker-compose.yml               # Mongo 8.0 + Azurite (authored)
.editorconfig, .gitignore        # dotnet new editorconfig / gitignore
src/
├── Gallery.Web/                 # dotnet new web --no-https
│   ├── Program.cs               # host, logging, endpoints
│   ├── Observability/RequestTimingMiddleware.cs
│   ├── Health/MongoHealthCheck.cs
│   ├── Health/BlobHealthCheck.cs
│   ├── Health/HealthResponseWriter.cs
│   ├── appsettings.json / appsettings.Development.json
│   └── Properties/launchSettings.json   # applicationUrl → http://localhost:5090
└── Gallery.Core/                # dotnet new classlib
    └── Observability/ServerTiming.cs    # pure header formatter
tests/
├── Gallery.Core.Tests/          # dotnet new xunit3
│   └── ServerTimingTests.cs
└── Gallery.Web.Tests/           # dotnet new xunit3
    ├── Infrastructure/ContainersFixture.cs   # one Mongo + one Azurite per assembly
    ├── Infrastructure/GalleryAppFactory.cs   # WebApplicationFactory with per-test db name
    ├── HomePageTests.cs
    ├── HealthTests.cs
    └── RequestLoggingTests.cs
```

**Structure Decision**: The P-01 layout (accepted here as D-22). The template-generated
`Class1.cs` and `UnitTest1.cs` are deleted. `Gallery.Core` references nothing, and that is
enforced by its project file only ever having been created with `classlib` and no `add reference`.

## Complexity Tracking

No entries.
