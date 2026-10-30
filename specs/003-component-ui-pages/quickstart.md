# Quickstart: Component UI Pages (003)

## Prerequisites

- .NET SDK 10.0.403
- To regenerate the sample photos only (they are committed): ffmpeg, `cwebp` (`brew install
  webp`), and `curl` and `sips` (built into macOS)

No new projects are created, so there is no scaffolding step. Constitution VI is satisfied:
every file in this feature is authored source, not generated project structure.

## Regenerate the sample photos (only when changing them)

```bash
bash scripts/build-samples.sh   # writes src/Gallery.Web/wwwroot/samples/{*.avif,*.webp,samples.json}
```

## Run

```bash
dotnet run --project src/Gallery.Web    # Development profile, http://localhost:5080
open http://localhost:5080/ui/
```

## Validate

| # | Check | Expected | Covers |
|---|---|---|---|
| 1 | `dotnet build Gallery.slnx -warnaserror` | 0 warnings, 0 errors | gate |
| 2 | `dotnet test` | all pass, including the component state tests and token/`@media` agreement | SC-002, SC-008, FR-007 |
| 3 | Open `/ui/` | 5 groups, 14 links; every link opens a page | US1, SC-001 |
| 4 | Open each page | every FR-007 state is present and labelled | US2, SC-002 |
| 5 | ContactSheet page at 1440, 900, and 390 px wide | 3/3/1 and 4/3/1 columns as configured; a short row keeps cell size | US3, SC-003 |
| 6 | Any page with photos | no photo cropped (the image's rendered aspect ratio equals width/height) | SC-004 |
| 7 | Load with network throttled | the placeholder shows in the final box, and layout shift is 0 when images arrive | SC-005 |
| 8 | `ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Gallery.Web --no-launch-profile`, then `curl -o /dev/null -w '%{http_code}' localhost:5080/ui/` | `404` | SC-006 |
| 9 | 20 requests, then count `"http.request"` lines in the log | exactly 20 | SC-007 |
| 10 | `curl -sI localhost:5080/ui/` | `Server-Timing: app;dur=…`, `X-Robots-Tag: noindex` | FR-009, FR-011 |
| 11 | `bash scripts/check-metadata.sh src/Gallery.Web/wwwroot/samples/*.{avif,webp}` | exit 0 (no EXIF/XMP chunks or items) | FR-008, III.3 |

Checks 8–11 are scripted in `specs/003-component-ui-pages/evidence/verify.sh`. The readings for
checks 5–7 are taken in a real browser and saved to `evidence/browser.md`.
