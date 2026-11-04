# Contract: Test impact (012)

Whole `tests/` tree grepped at plan time for what this feature changes: the package set,
`AppBuilder` wiring, middleware order, client IP, forwarded headers, `APPLICATIONINSIGHTS_*`, edge
mode label, and source scans over `src/`.

## Stated requirement change (parent applies it; executors never edit it)

| File:line | Pins | Old | New | Why |
|---|---|---|---|---|
| `tests/Gallery.Web.Tests/UploadSourceGuardTests.cs:22-28` `BaselinePackages` | exact `PackageReference` set of Gallery.Web | 4 packages | the same 4 plus `Azure.Monitor.OpenTelemetry.AspNetCore` (kept in ordinal order) | FR-018, research R-06, V.2 justification. Edit the array only, and its summary comment to "at 47de295 plus 012 R-06". |

## Checked and unaffected (must stay green unmodified)

- `EdgeSecretTests.cs:158`, `CloudflareEdgeProviderTests.cs:74/253`: the mode label stays `dev-zone`.
- `OwnerWriteGuardTests`, `NavSourceGuardTests`, `MoleculeComponentTests:286`,
  `LibrarySourceGuardTests:199`, `UploadSourceGuardTests` (codec scan): these scan `src/` for
  owner field names, `"nav"` writes, "again to cancel", retired seed tokens and codec libraries.
  None of these appear in the new `Hosting/` files. Executors must not name `claimedSubject`,
  `claimedAt` or `claimedIp` in Hosting code (OwnerWriteGuard).
- Tests that assert `claimedIp == "127.0.0.1"` (`OwnerSessionTests:36`, `UiOwnerOnlyTests:76`) seed
  the value directly; `Hosting:Edge` is unset in every existing host, so no forwarding happens.
- `PublicRoutesTests`, `AdminHost`, `LibraryHost`, `AppBuilder.Build` callers: no `Hosting:*` key,
  no `APPLICATIONINSIGHTS_CONNECTION_STRING`, so `AddHosting`/`UseHosting` are no-ops. The parent
  checks, before the first build, that no developer-machine or CI environment variable sets
  `APPLICATIONINSIGHTS_CONNECTION_STRING` (checked 2026-11-02: unset).
- Timing-evidence tests (`*TimingEvidenceTests`) read `http.request` lines. Refused requests never
  reach `RequestTimingMiddleware`, and no existing host enables the guard.
