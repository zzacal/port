# Quickstart: 009 Library and Gallery Management

Validation guide. Contracts hold the details: `contracts/http.md`, `contracts/edge.md`,
`contracts/components.md`, `contracts/log-events.md`, `contracts/removal-command.md`.

## Rules for every run

- Never stop or restart the owner's `dotnet watch` (ports 5080/5081) and never bind those ports.
  Verification instances use 5094 with `--no-launch-profile`; stop only the pid you started.
- Build into `--artifacts-path artifacts`. Docker must be running (Testcontainers Mongo + S3Proxy).
- macOS has no `timeout`; use `--blame-hang-timeout 120s` on `dotnet test`.
- No real Cloudflare call in tests: the recorder provider replaces the edge.

## Part A: automated (executor and parent gate)

```bash
dotnet build Gallery.slnx --artifacts-path artifacts -warnaserror
dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s
```

Expected: every test passes, including unmodified `UiPagesUnchangedTests`,
`PublicOutputUnchangedTests`, `PublicRoutesTests`, `BrowserTests`, `UploadPageTests`. The stated
requirement changes are exactly those of plan.md "Test changes" (the `AdminAccessTests`
rename in contracts/http.md, and the seed retirement listed in tasks.md T047).

Scenario coverage (one test class each, names indicative):

| Scenario | Proves |
|---|---|
| `LibraryFlowTests` US1 path: 40 uploaded images → select → add to new gallery → reorder → publish (confirm) | SC-001 action count, II.5, recorder holds `/`, gallery and photo pages |
| `LibraryEditTests` title/caption save, blank/too-long refusal, bare POST keeps values | FR-009/010, P-TEXT set |
| `GalleryListTests` reorder, stale order 409, create, publish/unpublish, empty switch disabled | FR-018–020, FR-029–032 |
| `PublishRefusalTests` [SAFETY] publish POST on an empty gallery with full form, `confirm=1`, and no body | II.6, SC-006 |
| `BuilderTests` reorder/stale, pin/unpin/remove pinned, rename 301, delete gallery | FR-021–028, II.4 |
| `ImageDeleteTests` [SAFETY] shared-file rule, missing master, one storage failure of three, crash-and-repeat | FR-014–017, SC-008 |
| `InvariantTests` runs `InvariantChecker` after every operation above | SC-009, SC-007 (home is one read) |
| `PurgeSetTests` (pure) one row per contracts/edge.md set, exact equality | SC-004 |
| `EdgeWorkerTests` 250 addresses → 3 calls of ≤100, rate spacing, failure logged, request returns before the recorder gate opens | FR-040/041, SC-003 |
| `EdgeModeTests` no `Edge:*` → app starts, every action succeeds, `edge.purge … log-only … skipped` | SC-012 |
| `EdgeSecretTests` [SAFETY] configured token value absent from every owner page and captured log | SC-011 |
| `OutputCacheEvictionTests` with `Site:OutputCache=true`: second GET served from cache; after an edit exactly the affected pages are re-read | FR-044, research R-07 |
| `RemoveSeededTests` [SAFETY] guard exits 4 per failed check with nothing changed; seeded + owner content; second run zeros | FR-045–048, SC-010 |
| `LibraryAccessTests` [SAFETY] every new route: no session 302/401, no token 400 | I.3, I.6 |
| `LibraryComponentAdditionsTests` K1–K6 defaults byte-identical; FALSE cases render no `checked`/`disabled` | FR-031 |
| `LibraryBrowserTests` (Playwright Chrome) drag reorder, keyboard reorder, Select all + count, picker public note, publish confirm, controls below the cell inside the tile box at 1440/1024/390 | FR-004, FR-008, FR-019, FR-022 |

## Part B: evidence (parent)

```bash
bash specs/009-library-galleries/evidence/p95.sh specs/009-library-galleries/evidence/library-timings.log
bash specs/009-library-galleries/evidence/verify.sh | tee specs/009-library-galleries/evidence/verify-output.txt
bash specs/009-library-galleries/evidence/non-vacuity.sh | tee specs/009-library-galleries/evidence/non-vacuity.txt
```

- `p95.sh` was first run on synthetic logs (one passing per SC, one empty → FAIL).
- `library-timings.log` is written by `LibraryTimingEvidenceTests` (tasks.md T057: 100 uploaded
  test images, 30 library GETs, 30 of each save, 5 bulk adds and deletes of 100); SC-002/003/005
  PASS with non-zero n. `verify.sh` is the live-server curl walkthrough on 5094 (T058).
- `non-vacuity.sh` breaks each [SAFETY] guard once (tasks.md T060 list: publish refusal removed,
  conditional write dropped, shared-file check removed, guard check inverted, owner filter
  dropped, token logged, routes moved off the owner group, and more) and records the red test
  with its assertion message.

## Part C: owner live walkthrough (after the build; owner present)

Local dev site, https://localhost:5081 in the owner's watch window (Ctrl+R after the build so new
routes exist):

1. Upload 5 photos, follow "Open the library". The tiles say "Not in a gallery".
2. Select 3, Add to gallery → New gallery "Coast". You land in the Coast builder.
3. Drag the third photo first; press Make cover on another; Unpin; Remove one.
4. Switch Public on, confirm the warning. View gallery shows Coast; `/` lists it.
5. Rename Coast → "Coastline"; the old address redirects.
6. In the library, edit a title; the photo page shows it at once (output cache evicted).
7. Delete a photo from its panel; it leaves Coastline and the library.
8. Run the removal command (stop nothing; it talks to Mongo and R2 directly):
   `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-launch-profile -- remove-seeded`
   Expect the counts, then a second run of zeros. The home page shows only your galleries.
9. Optional, once the owner adds `Edge:ZoneId` and `Edge:ApiToken` (Cache Purge on the dev
   image zone only) with `dotnet user-secrets set`: repeat step 7 and check the log shows
   `edge.purge … dev-zone … ok` for the deleted file URLs. Page purge stays log-only until hosting.
