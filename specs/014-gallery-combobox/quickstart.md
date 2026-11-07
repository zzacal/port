# Quickstart: validate 014

Prerequisites: the usual dev setup (Docker for Mongo and S3Proxy test containers, Chrome for
Playwright, user-secrets as for 009–013).

## Automated

```bash
# full gate (build -warnaserror + all tests)
bash specs/014-gallery-combobox/evidence/gate.sh run

# just this feature
dotnet test Gallery.slnx --artifacts-path artifacts \
  --filter "FullyQualifiedName~LibraryMembershipEditTests|FullyQualifiedName~GalleryFieldBrowserTests|FullyQualifiedName~LibraryEditTests"

# SC-002 from logs alone
dotnet test Gallery.slnx --artifacts-path artifacts --filter "FullyQualifiedName~MembershipTimingEvidenceTests"
bash specs/014-gallery-combobox/evidence/p95.sh     # ALL PASS expected
```

Expected: everything passes. `test-impact.md` (A) is the only edited existing test.

## By hand (local, `dotnet run --project src/Gallery.Web`)

1. Go to /admin/library, then Edit on a photo in no gallery. The Galleries field shows the
   line "Not in a gallery" under it.
2. Type part of an existing gallery title. The matching galleries are listed, one line each, with the first one
   highlighted. Press Enter. A dashed (pending) token appears.
3. Type a new title, for example "Night walks". The last row reads "+ Night walks". Pick it.
4. Save. The panel shows "Saved" and both tokens as solid links. /admin/galleries shows a private
   "Night walks" with 1 photo.
5. Remove one token with its ×, then Save. The token is gone, and that gallery's builder no
   longer lists the photo.
6. Type the exact title of an existing gallery with different case. No "+" row appears.
7. Pick a public gallery. A warning line appears under the field until you save or remove it.

Production: after the deploy, the owner repeats steps 1–4 on port.zacal.me (owner step in
tasks.md).
