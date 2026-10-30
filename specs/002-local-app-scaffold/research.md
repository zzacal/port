# Research: Local App Scaffold (002)

Checked on 2026-10-27 against the official .NET release index and the installed SDK's own
`dotnet new` output. No NEEDS CLARIFICATION items remain.

## R-01: .NET SDK version and pin

- **Decision**: Pin SDK **10.0.403** with `rollForward: latestPatch`, created with
  `dotnet new globaljson --sdk-version 10.0.403 --roll-forward latestPatch`.
- **Rationale**: The release index lists .NET 10 (LTS, "active") as the newest released channel,
  with latest SDK 10.0.403. .NET 11 is 11.0.0-rc.3, a preview, and excluded (FR-003).
  `latestPatch` only rolls forward within the 10.0.4xx band, so an older SDK such as 10.0.103
  fails, and a preview never qualifies. The SDK's resolution error names the requested version.
  This was tested: a pin to an uninstalled patch fails with "A compatible .NET SDK was not found …
  Requested SDK version".
- **Alternatives**: `latestFeature` or `latestMinor` were rejected because they let a different
  feature band satisfy the pin. Having no global.json was rejected because 10.0.103 would build
  silently.

## R-02: Templates

- **Decision**: `dotnet new sln --format slnx`, `web --no-https`, `classlib`, and the SDK's
  bundled `xunit` template for both test projects. No `dotnet add package`.
- **Rationale**: The owner asked for the projects only, with no added dependencies (FR-005). The
  bundled templates need no extra template install. `--no-https` because Cloudflare terminates
  TLS in production and the app only sees HTTP behind it (D-02), so a local dev certificate tests
  nothing real.
- **Template output, recorded so it isn't mistaken for an addition**: the bundled `xunit` template
  references xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1, and
  coverlet.collector 6.0.4.
- **Alternatives**: The xUnit v3 template (`xunit.v3.templates`) was rejected for now because it
  is an extra template install. Moving to v3 is a later, separate decision.

## R-03: Repository files

- **Decision**: `dotnet new gitignore` so `bin/` and `obj/` stay out of git.
- **Rationale**: Without it, every build leaves output in the working tree.
- **Note**: The existing `.gitignore` only ignores `.specify/feature.json`. The command exits 73
  when the file exists, so: copy the old file aside, run with `--force`, then append the old entry.
- **Not added**: `.editorconfig`, `Directory.Build.props`, `docker-compose.yml`. None was asked
  for.

## R-04: Accepting P-01

- **Decision**: P-01 is accepted as D-22. D-22 states that packages, local services, and test
  infrastructure come with the features that need them, not with the scaffold.
