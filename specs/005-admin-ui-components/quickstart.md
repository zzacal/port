# Quickstart: Admin UI Components (005)

Run all commands from the repo root.

## 1. Gates

```sh
dotnet build Gallery.slnx -warnaserror
dotnet test --no-build
```

Expected:
- 0 warnings and 0 errors
- every test passes
- the feature 003 test files are unmodified, except the index count in `IndexTests` (14 → 32),
  which is a stated requirement change (SC-004)

## 2. Live check

```sh
bash specs/005-admin-ui-components/evidence/verify.sh | tee specs/005-admin-ui-components/evidence/verify-output.txt
```

Expected: every line PASS. The script checks:
- the index lists 32 pages
- every page returns 200, with noindex, Server-Timing, no script, and no external CSS
- unknown paths return 404
- 20 requests produce 20 log events
- Production returns 404 for admin `/ui` pages

## 3. Browser (high tier, not delegated)

```sh
dotnet run --project src/Gallery.Web
```

Open `http://localhost:5080/ui/`. On each admin page, at 1440, 1024 and 390 px wide:
- `scrollWidth <= innerWidth` (SC-007)
- admin grid column counts match the public ones (SC-007)
- in Button "Link as button in a row", all three controls have the same top and height
- there is at most one filled button per bar or panel, and danger controls have no solid
  background (SC-005)
- every scoped-CSS effect named in research R-06 is visible:
  - overlay positions
  - selected outline
  - dimmed private tiles
  - drop slot
- tabbing shows a focus ring on every control (SC-006)

Record the measurements in `evidence/browser.md` and the screenshots in `evidence/shots/`.
