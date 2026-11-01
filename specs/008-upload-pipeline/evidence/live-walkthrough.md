# Live walkthrough (T050/T051), 2026-11-01, automated, owner absent

Run: `ART=artifacts/exec-p2 bash specs/008-upload-pipeline/evidence/verify.sh | tee
specs/008-upload-pipeline/evidence/verify-output.txt` on the tree at 2c8b394 plus the uncommitted
Phase 9 test files (the sibling executor's T046/T047/T049). Result: **ALL PASS** (exit 0). Server log:
`verify-run.log` (JSON console log, 123 lines).

## Set-up the script used

| Piece | What ran | Isolation |
|---|---|---|
| Gates | `dotnet build -warnaserror` (0 warnings, 0 errors) and `dotnet test --filter Category!=Evidence` (777 passed, 2 assemblies) into `artifacts/exec-p2` | The evidence tests are left out because they rewrite committed evidence logs. Their own tasks run them (T049). `TEST_FILTER=` runs everything. |
| Storage | throwaway S3Proxy (pinned digest, identity `local`/`localsecret`, transient), buckets `pub`/`priv` | `docker rm -f <id>` at exit. R2 never contacted. |
| Datastore | fresh database `gallery-verify-008-<epoch>` on `gallery-mongo` | dropped at exit; afterwards the container lists only `admin config gallery_dev local`. |
| Sign-in | `evidence/fake-oidc.cs` (file-based app, no `#:package`, RS256 by hand) on `http://127.0.0.1:5093`, app given `verify-client`/`verify-secret` | Real Google not used. The owner's user-secrets are overridden by env vars and never read by the script. |
| App | built `Gallery.Web.dll`, Development, `https://localhost:5092` (one pid, `kill`) | Never bound :5080/:5081. Only its own pids were stopped. |

## Observations

| Step (quickstart Part B / T050) | Result | Evidence (verify-output.txt) |
|---|---|---|
| Sign-in via cookie jar | PASS | `POST /admin/signin 302` to the fake authorize URL (`prompt`, `nonce`, PKCE, `response_mode=form_post`), `POST /signin-oidc 302 /admin`, `GET /admin/upload 200` with `data-csrf` |
| U1 page | PASS | `no-store`; no `X-Amz-` string in the HTML (FR-043) |
| U2 state | PASS | 200 `no-store`, `{"batch":null}` before; 2 of 2 `succeeded` after U7 |
| Versioned script | PASS | `/admin-assets/upload.js?v=c2328d45` 200 `public, max-age=31536000, immutable` |
| CSRF | PASS | register POST without `X-CSRF-TOKEN` → 400 |
| Body limit | PASS | 231 412-byte register body → 413 `too-large` |
| U3 → PUT master → U4 → PUT sizes → U7 | PASS for both photos | `gps-camera.jpg` 400×200 → ladder [320]; `orientation-6.jpg` → [200]; every PUT 200 with only `Content-Type`; U7 200 `succeeded` with the committed jSquash fixtures and placeholders |
| Presigned URLs (FR-044) | PASS | all 6 carry `X-Amz-SignedHeaders=content-type%3Bhost` and nothing more; `X-Amz-Expires` 900 or 899 (never > 900); printed with query values redacted |
| U10 confirm | PASS | 200 `count 2`, redirect `/admin/upload?confirmed=<batch>`; result page says "2 photos added to your library" |
| SC-003 staging empty | PASS | S3 listing: `staging/<batch>/` 6 objects before confirm, 0 after; `masters/` 2; public bucket 4 (2 photos × AVIF+WebP) |
| FR-037 one `http.request` per call | PASS | 17 requests (including the readiness probe of `/`), 17 `http.request` lines, same method+status sequence |
| FR-038 timing events | PASS | `s3.presign` ×6, `upload.master-check` ×4 (U4 + re-check in U7), `upload.validate` ×2, `upload.confirm` ×1, `browser.decode-orient` ×2 (reported timing logged), `upload.confirmed <batch> 2 …` ×1 |
| Secrets in the log | PASS | no `X-Amz-Signature`/`X-Amz-Credential`, no `localsecret`/`verify-secret`; no Warning/Error entries |

Redaction (T051): `grep -E '[A-Za-z0-9+/=_.%-]{32,}'` over `verify-run.log`, `verify-output.txt` and
the self-test logs matches only storage paths built from 26-character opaque ids, the
`X-Amz-SignedHeaders` value, a category name and the repo path. None of them is a token, key or
subject id. There are no runs of `[A-Za-z0-9_-]{32,}` at all, so nothing was redacted. The fake
provider's subject is the literal `verify-owner`.

Run notes:
- `dotnet dev-certs https --export-path` hung unattended. Exporting the private key raises a
  keychain access prompt. The script now writes only the public certificate to a temp PEM
  (`security find-certificate`, matched by the thumbprint `--check` reports).
- The built dll needs an absolute `--contentRoot` (`$PWD/src/Gallery.Web`). A relative path
  resolves against the dll's folder and the host aborts at startup.

## Not covered here (OWNER-ONLY)

This walkthrough drives the server path with curl and committed encoder output. The real-browser
encode, Safari/iOS, `browser.total` (SC-009), R2 signed-header/copy-if-match facts and the
`staging/` lifecycle rule are **O-02** in tasks.md (quickstart Part C), owner live run.
