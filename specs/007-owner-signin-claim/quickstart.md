# Quickstart: Owner Sign-In and Claim (007)

Validation guide. Implementation detail lives in tasks.md; contracts in `contracts/`.

## Prerequisites (owner, done 2026-10-31)

- Google OAuth web client; redirect `https://localhost:5081/signin-oidc` registered; two test users.
- `Authentication:Google:ClientId` / `ClientSecret` in user-secrets for `src/Gallery.Web`.
- Dev certificate trusted: `dotnet dev-certs https --check --trust`.
- Docker running (local Mongo and the test containers).

## Scaffolding (Constitution VI, CLI only)

```sh
dotnet add src/Gallery.Web package Microsoft.AspNetCore.Authentication.OpenIdConnect --version 10.0.14
```

No other package. The test project uses `Microsoft.IdentityModel.JsonWebTokens`, which reaches it
transitively through its project reference to Gallery.Web.

## Automated

```sh
dotnet clean && dotnet build --artifacts-path artifacts
dotnet test --artifacts-path artifacts
bash specs/007-owner-signin-claim/evidence/verify.sh     # contract invariants, tee'd to verify-output.txt
```

Expected: every existing test passes unmodified; the new suites cover FR-025/FR-026, SC-002
(50-way race), SC-003 (route sweep), SC-004, SC-005 (minted tickets), SC-007 (byte-identical public
markup with and without the owner cookie).

## Live walkthrough (real Google, owner's machine)

1. Start from an unclaimed site: `mongosh gallery_dev --eval 'db.site.updateOne({_id:"site"},{$set:{claimedSubject:null,claimedAt:null,claimedIp:null}})'`.
2. Run the app with logs captured:
   `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --launch-profile https > specs/007-owner-signin-claim/evidence/live-run.log`
3. Open `https://localhost:5081/admin` → redirected to the sign-in page showing "Claim this site"
   over the first cover.
4. Sign in with the owner account → owner shell "Signed in". `mongosh` shows `claimedSubject`
   (a numeric string, not an email), `claimedAt`, `claimedIp`.
5. Quit and reopen the browser → `/admin` goes straight to the shell.
6. Sign out → sign-in page shows "Sign in" (not claim wording).
7. Sign in with the second test account → "Not the owner"; no `__Host-gallery` cookie in DevTools;
   `site` document unchanged. "Try another account" → Google's account chooser.
8. Cancel at Google's account chooser → "Sign-in didn't complete. Nothing was saved."
9. With the owner signed in, open `/` and compare to a private window: identical markup, no
   `Set-Cookie` on public responses.
10. `/ui/` opens for the owner; in a private window it redirects to sign-in.

## Log evidence

```sh
bash specs/007-owner-signin-claim/evidence/p95.sh specs/007-owner-signin-claim/evidence/live-run.log
```

Reports: count of `auth.claim` (must be 1), p95 of `db.site.owner.read` + `db.site.claim` per
callback (< 50 ms, SC-006), p95 of `http.request` on `/signin-oidc` (< 1000 ms, SC-006). The
automated suite's fake-provider callbacks give a second sample of 50+ callbacks for the
ownership-work bound.
