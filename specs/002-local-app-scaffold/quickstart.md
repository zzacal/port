# Quickstart: Local App Scaffold (002)

## Prerequisites

- .NET SDK **10.0.403** (`dotnet --list-sdks` must show it)

## Scaffolding (run once, from the repo root)

This command sequence is the source of truth for every generated file (Constitution VI).

```bash
export DOTNET_NOLOGO=1

dotnet new globaljson --sdk-version 10.0.403 --roll-forward latestPatch

cp .gitignore /tmp/gallery.gitignore.bak
dotnet new gitignore --force
cat /tmp/gallery.gitignore.bak >> .gitignore

dotnet new sln --format slnx -n Gallery

dotnet new web      -o src/Gallery.Web            -n Gallery.Web --no-https
dotnet new classlib -o src/Gallery.Core           -n Gallery.Core
dotnet new xunit    -o tests/Gallery.Core.Tests   -n Gallery.Core.Tests
dotnet new xunit    -o tests/Gallery.Web.Tests    -n Gallery.Web.Tests

dotnet sln Gallery.slnx add --solution-folder src   src/Gallery.Web src/Gallery.Core
dotnet sln Gallery.slnx add --solution-folder tests tests/Gallery.Core.Tests tests/Gallery.Web.Tests

dotnet add src/Gallery.Web          reference src/Gallery.Core
dotnet add tests/Gallery.Core.Tests reference src/Gallery.Core
dotnet add tests/Gallery.Web.Tests  reference src/Gallery.Web src/Gallery.Core
```

If `src/Gallery.Web/Properties/launchSettings.json` came out with port 5000, change that one
value to a free port (macOS AirPlay owns 5000).

## Validate

| # | Command | Expected | Covers |
|---|---|---|---|
| 1 | `dotnet build Gallery.slnx -warnaserror` | 0 errors, 0 warnings | US1-1, SC-001 |
| 2 | `dotnet run --project src/Gallery.Web`, then open the printed address | "Hello World!" | US1-2, SC-004 |
| 3 | `dotnet test` | both test projects run and pass | US2-1, SC-002 |
| 4 | In a temp copy, set `global.json` to an uninstalled patch (e.g. 10.0.404), then run `dotnet build` | "A compatible .NET SDK was not found … Requested SDK version: 10.0.404" | SC-003 |
| 5 | Re-run the scaffolding in an empty temp directory, then repeat check 1 | 0 errors, 0 warnings | SC-001 |
