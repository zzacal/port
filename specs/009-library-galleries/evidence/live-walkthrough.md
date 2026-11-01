# Live walkthrough (T058/T059), 2026-11-01, automated, owner absent

Run: `bash specs/009-library-galleries/evidence/verify.sh | tee
specs/009-library-galleries/evidence/verify-output.txt` on the tree at 606f367 (T001–T057 done,
clean; only the new evidence files added). Result: **ALL PASS**, 53 PASS lines, 0 FAIL, exit 0.
Server log: `verify-run.log` (JSON console log, 296 lines).

## Set-up the script used

| Piece | What ran | Isolation |
|---|---|---|
| Gates | `dotnet build -warnaserror` (0 warnings, 0 errors) and `dotnet test --filter Category!=Evidence` (1005 passed, 2 assemblies) into `artifacts` | The evidence tests are left out because they rewrite committed evidence logs; T057 and T059's full run cover them. `TEST_FILTER=` runs everything. |
| Storage | throwaway S3Proxy (008's pinned digest, identity `local`/`localsecret`, transient), buckets `gallery-dev-public-verify` / `gallery-dev-private-verify` | `docker rm -f <id>` at exit. R2 never contacted. |
| Datastore | fresh database `gallery_verify009_<epoch>_dev` on `gallery-mongo` | Dropped at exit. Afterwards the container lists only `admin config gallery_dev local`. `gallery` and `gallery_dev` are never named. |
| Sign-in | 008's `evidence/fake-oidc.cs` on `http://127.0.0.1:5095`, app given `verify-client`/`verify-secret` | Real Google not used; the owner's user-secrets are overridden by env vars and never read by the script. |
| App | built `Gallery.Web.dll`, Development, `https://localhost:5094` (one pid, `kill`), no `Edge:*` key | Never bound :5080/:5081. Only the script's own pids were stopped. |
| Removal command | `ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/bin/Gallery.Web/debug/Gallery.Web.dll remove-seeded`, same env overrides, `HOME` pointed at an empty temp dir | The command's configuration adds user-secrets AFTER env vars (contracts/removal-command.md), so the owner's real `Storage:*` keys would otherwise win over the overrides and the command would talk to R2. With no secrets file only the overrides apply. |

## Observations

| Step (T058 item 4–6) | Result | Evidence (verify-output.txt) |
|---|---|---|
| Sign-in via cookie jar | PASS | `POST /admin/signin 302` to the fake authorize URL, `POST /signin-oidc 302 /admin`, `GET /admin/upload 200` with `data-csrf` |
| 2 photos through the upload API | PASS | register → PUT master → U4 → PUT avif+webp → U7 `succeeded` ×2 → confirm `count 2` (committed fixtures) |
| L1 | PASS | `GET /admin/library` 200 `no-store`, 2 tiles, `__RequestVerificationToken` hidden field read from the page |
| L3 new gallery | PASS | `gallery=new`, `newTitle=Harbour walk` → 303 `/admin/galleries/{gid}?added=2`; stored `private harbour-walk` with both ids in library order |
| B1 + B3 reorder | PASS | builder's `b-order` form lists the stored order; reversed `order` → 303 `?ordered=1`, items reversed; PRG page says "Order saved" |
| G4 without `confirm` | PASS | 200 re-render with the publish warning (`Make public`, `name="confirm" value="1"`), stored visibility still `private` |
| G4 with `confirm=1` | PASS | 303 `/admin/galleries/{gid}?visibility=public`, stored `public`; `edge.purge gallery.publish log-only 4 0 skipped …` |
| GET `/` | PASS | lists "Harbour walk" with `/g/{gid}/harbour-walk`; that gallery page then answered 200 (cached) |
| B2 rename → 301 | PASS | 303 `?saved=1`, stored slug `harbour-at-night`; `edge.purge gallery.update log-only 7 … skipped` (both slugs + `/`); old URL 301 → `/g/{gid}/harbour-at-night` (the cached 200 was evicted); new URL 200 |
| L2 save | PASS | 303 `/admin/library?page=1&open={iid}&saved=1`, stored title and caption; `edge.purge image.update log-only 2 … skipped`; photo page shows the new text at once |
| BARE L2 POST | PASS | no body, token in `X-CSRF-TOKEN` → 303 same Location, title and caption unchanged |
| G4 on an empty gallery | PASS | G2 created it (303 `/admin/galleries/{gid}`); `public=on confirm=1` → 409 "Add a photo before making this gallery public", stored still `private` |
| POST without the token | PASS | `POST /admin/galleries` → 400, gallery count unchanged |
| L5 delete | PASS | 303 `/admin/library?page=1&deleted=1&leftovers=0`; record gone, left the gallery, both public files 404 in S3Proxy; `edge.purge image.delete log-only 2 … skipped https://img.test/…` |
| remove-seeded run 1 | PASS | mongosh inserted a seed-shaped image + public gallery (SeedShapes field set, linked, in `homeOrder`) and their 8 public files; exit 0 with `seeded galleries removed: 1`, `seeded images removed: 1`, `owner galleries changed: 0`, `public files deleted: 8`, `kept 0`, `leftover 0`; seeded docs and files gone, `homeOrder` cleaned, owner content hash unchanged |
| remove-seeded run 2 | PASS | exit 0, every count 0, owner content unchanged |
| Guard refusal | PASS | `Mongo:Database=gallery_verify` → exit 4, only `Target guard failed: Mongo:Database must end with _dev (got: gallery_verify)`; `gallery_verify` was not created; a seeded image placed in the verify database survived |
| One `http.request` per call | PASS | 36 requests (readiness probe included), 36 `http.request` lines, same method+status sequence; route templates present for every 009 route used |
| Log-only purges | PASS | 7 `edge.purge` lines in the app log, every one `log-only`; one `skipped` purge after each public change (publish, rename, L2, L5) |
| Timing and owner events | PASS | `owner.action gallery.publish`, `owner.action image.delete`, `op.timing db.library.page` ×8, `db.site.project` ×9, `library.image.delete`, `edge.lag` ×6 |
| Script hygiene | PASS | the word pkill appears nowhere in verify.sh; servers stopped by `kill <pid>` |
| Secrets in the log | PASS | no `X-Amz-Signature`/`X-Amz-Credential`, `localsecret` or `verify-secret` |

## Log notes

- Two Kestrel `Error` entries (one connection): `Unexpected exception in TimingPipeFlusher.FlushAsync`
  / `Unhandled exception while processing …`, `IOException: The encryption operation failed` →
  `Win32Exception (14): Bad address`, raised in `Http2FrameWriter.WriteGoAwayAsync` while Kestrel
  closed an HTTP/2 connection that curl had already torn down (right after `GET /` 200). No request
  failed and the next request succeeded. It happened in 2 of the 3 runs, so it is a teardown race
  between curl and Kestrel on macOS TLS, not an app defect. Recorded, not hidden.
- Redaction: `grep -oE '[A-Za-z0-9_-]{32,}'` over `verify-run.log` and `verify-output.txt` matches
  only .NET type names in that stack trace (`PoolingAsyncValueTaskMethodBuilder`) and the verify
  database name. The wider `[A-Za-z0-9+/=_.%-]{32,}` set adds only page URLs built from 26-character
  opaque ids, `Gallery.Web.Observability.OpTimer` and framework type names. No 18–25 digit runs. None
  is a token, key or subject id, so nothing was redacted. The fake provider's subject is the literal
  `verify-owner`.

## Script bugs found and fixed while writing it

- The B3 PRG check first re-fetched the builder without `?ordered=1`; it now follows the `Location`.
- `curl --aws-sigv4 --data-binary` gets `XAmzContentSHA256Mismatch` from S3Proxy; the seed files are
  uploaded with `-T <file>`.
- mongosh warns on `NumberLong(<number>)`; the insert now passes strings.

## Not covered here (OWNER-ONLY)

This walkthrough drives the server path with curl. The real-browser part (drag and keyboard reorder,
cover pin/unpin, remove, the publish warning dialog, the UI review rules UI-1–UI-8 by eye, Chrome and
Safari, the R2 bulk-delete p95) is **O-03** in tasks.md (quickstart Part C), owner live run.
