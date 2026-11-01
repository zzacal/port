# Quickstart: 006 Public Site, Local Walking Skeleton

Run from the repo root. Never stop or restart the owner's `dotnet watch` on port 5080. Verification
instances use `--artifacts-path artifacts` and port 5090.

## Part A: one-time setup (CLI only, Constitution VI)

```bash
# Packages (research R-01)
dotnet add src/Gallery.Web package MongoDB.Driver --version 3.13.0
dotnet add src/Gallery.Web package AWSSDK.S3 --version 4.0.104.8
dotnet add src/Gallery.Web package MetadataExtractor --version 2.9.3
dotnet add tests/Gallery.Web.Tests package Testcontainers --version 4.16.0
dotnet add tests/Gallery.Web.Tests package Testcontainers.MongoDb --version 4.16.0

# Secrets (research R-10): moves the R2 keys into user-secrets without printing them
dotnet user-secrets init --project src/Gallery.Web
bash scripts/r2-keys-to-user-secrets.sh        # then the owner deletes .r2-keys-copy

# Local Mongo (research R-06): one named container, data in a named volume
bash scripts/dev-mongo.sh                      # docker run -d --name gallery-mongo -p 27017:27017 -v gallery-mongo:/data/db mongo:8
```

Owner check, one time (FR-021 r2.dev half): Cloudflare dashboard → R2 → each `gallery-dev-*`
bucket → Settings → Public Development URL must say **disabled**. Done on 2026-10-30; repeat when
a bucket is added.

## Part B: prepare and seed the owner's photos

(Retired by feature 009: the seed command, manifest and scripts no longer exist; see specs/009-library-galleries.)

```bash
bash scripts/prepare-photos.sh                 # → .dev-prepared/, "prepared 93, skipped-unchanged 0, rejected 0"
bash scripts/seed-manifest.sh                  # → seed/manifest.json (refuses if it exists); edit freely
dotnet run --project src/Gallery.Web -- seed   # uploads to gallery-dev-public, writes Mongo
```

Expected: the seed summary lists about 11 galleries (9 dated, plus "Unlisted" and "Coming
soon") and 93 images, uploads about 1,100 files the first time, and reports 0 unmatched. Running
all three again reports `skipped-unchanged 93` and `uploaded 0`. (`seed-manifest.sh` refuses to
run again, as specified.)

## Part C: run and look

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --artifacts-path artifacts \
  --no-launch-profile --urls http://localhost:5090
```

Open http://localhost:5090/ and check:

- the home grid of dated galleries. "Unlisted" and the empty "Coming soon" draft are absent
  (II.6: an empty gallery is never shown).
- open a gallery, then a photo: caption (first photo), camera line, previous/next.
- open the "Unlisted" address from Mongo or the seed output: it renders, and the page source has
  `noindex`.

## Part D: automated verification

```bash
dotnet test                                             # unit + component + integration (Mongo + S3Proxy containers)
GALLERY_R2_TESTS=1 dotnet test --filter Category=R2     # on demand: same storage tests against dev R2 (FR-022)
bash specs/006-public-site-local/evidence/verify.sh     # live HTTP, headers, redirects, noindex, secret scan, metadata scan, reseed idempotency
bash specs/006-public-site-local/evidence/p95.sh        # SC-003/SC-004 from the log alone
```

`verify.sh` and `p95.sh` start their own server on 5090 with output to
`evidence/dev-run.log` and stop only that process. The browser checks for SC-005 and SC-009
(layout shift at three widths, no-JS walk-through) are recorded in `evidence/browser.md`.

## Expected outcomes

| Check | Pass condition |
|---|---|
| SC-002 | `/` contains no private gallery; every private page has `noindex` |
| SC-003 | p95 `duration_ms` < 50 per route (output cache off) |
| SC-004 | `/`: `db_reads=1` on 100%; `/g/…`: `db_reads ≤ 2` on 100% |
| SC-006 | second seed: `uploaded 0`; collection dumps equal |
| SC-007 | every image URL on every page starts with `https://img-dev.zacal.me/`; sampled HEAD shows `immutable` |
| SC-008 | secret scan: 0 matches in `git ls-files` content and served HTML |
| SC-010 | `PublicOutputUnchangedTests` green, unmodified |
| SC-011 | `check-metadata.sh` 0 failures over `.dev-prepared/`; Mongo dump contains no `gps`/`latitude`/`longitude` key |
| SC-012 | every prepared photo's id appears on at least one gallery page |
