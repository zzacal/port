# Runbook: Owner Sign-In (007, local development)

Covers FR-027: the Google sign-in client for local development, where its secret lives, the
out-of-band owner reset (Constitution I.4), and how to read the claim event (E-02).

## 1. Create the Google sign-in client (once per machine/project)

1. https://console.cloud.google.com/projectcreate — create a project (e.g. `gallery-dev`). No
   Google API needs enabling.
2. https://console.cloud.google.com/auth/overview — Google Auth Platform → Get started.
   Audience **External**, publishing status **Testing**.
3. Audience → Test users: add the owner's Google account AND a second account. Only listed test
   users can sign in while the app is in Testing; the second account exercises the refusal path.
4. Clients → Create client → type **Web application**.
   Authorized redirect URI: `https://localhost:5081/signin-oidc` (the only one the app uses; an
   `http://localhost:5080/signin-oidc` entry is harmless but unused, research R-09).
   No JavaScript origins are needed.
5. Store the id and secret in user-secrets, never in the repo:

   ```sh
   dotnet user-secrets --project src/Gallery.Web set "Authentication:Google:ClientId" "<client id>"
   dotnet user-secrets --project src/Gallery.Web set "Authentication:Google:ClientSecret" "<client secret>"
   ```

   Check presence without printing values:
   `dotnet user-secrets --project src/Gallery.Web list | sed -E 's/=.*/= <set>/'`
6. Trust the development certificate (the `__Host-` cookies need HTTPS):
   `dotnet dev-certs https --trust`, then `dotnet dev-certs https --check --trust`.
   Firefox keeps its own certificate store: set `security.enterprise_roots.enabled` to `true` in
   `about:config` so it trusts the macOS keychain, or it reports a self-signed certificate.
7. Run on the `https` launch profile: `dotnet run --project src/Gallery.Web --launch-profile https`
   and open `https://localhost:5081/admin`.

Without the two secrets the site still runs; `/admin/signin` says "Sign-in isn't set up on this
server." and the sign-in button answers 503 (research R-02).

## 2. Owner reset (out of band, I.4)

No web route can change or clear the owner. To reset, edit the datastore directly (local dev DB is
`gallery_dev`):

```sh
mongosh gallery_dev --eval 'db.site.updateOne({_id:"site"}, {$set:{claimedSubject:null, claimedAt:null, claimedIp:null}})'
```

Effect:

- The site is unclaimed again: the next Google sign-in from `/admin/signin` claims it (shown as
  "Claim this site").
- Every existing owner session stops working on its next admin request: the per-request owner
  check (research R-06) finds no matching owner, rejects the session and deletes the cookie, so
  the browser is sent to the sign-in page.
- Public pages are unaffected.

To hand the site to a different account directly, set `claimedSubject` to that account's Google
`sub` (a numeric string) instead of null; the old owner's sessions end the same way.

## 3. Detecting a stranger's claim (E-02)

A claim writes `claimedSubject`, `claimedAt` and `claimedIp` once and logs one event in category
`Gallery.Auth`:

```
auth.claim {sub} {ip} {at}
```

Check it:

```sh
mongosh gallery_dev --quiet --eval 'printjson(db.site.findOne({_id:"site"},{claimedSubject:1,claimedAt:1,claimedIp:1}))'
grep -h 'auth.claim' <captured log>      # JSON console: jq -r 'select(.Message|startswith("auth.claim")).Message'
```

If the `sub`, time or IP is not yours (your own sub appears in `auth.signin` lines after you sign
in), someone else claimed the site: run the reset in section 2 and sign in again. Refused attempts
appear as `auth.refused {sub} {ip} not_owner|lost_race`; failed ones as `auth.failed {ip} {reason}`.
Log events never contain email addresses, names, tokens or cookies.
