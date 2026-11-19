# Quickstart: 018 validation

## On the owner's machine

```sh
dotnet user-secrets set LocalSignIn:Password "<16 or more characters>" --project src/Gallery.Web
```

Then press Ctrl+R in the `dotnet watch` window (a new route needs a restart). The watch log shows
`auth.local.state enabled`.

1. Open https://localhost:5081/admin/library while signed out: you are sent to the sign-in page,
   which shows "Sign in with Google" and, below it, "Local password" with "Sign in locally".
2. Type a wrong password: "That password wasn't accepted.", still signed out.
3. Type the right password: you land on `/admin`, signed in as your Google owner (your development
   database is already claimed). Open the library and save a title: it works as usual.
4. Sign out: back on the sign-in page.

To turn it off: `dotnet user-secrets remove LocalSignIn:Password --project src/Gallery.Web`, then
Ctrl+R. The local form is gone.

## On a CI runner or another machine (a self-contained browser suite)

Environment for the app process:

```sh
ASPNETCORE_ENVIRONMENT=Development
LocalSignIn__Password=<16+ characters, e.g. generated per run>
Mongo__Database=<anything ending _dev>      # the suite's own empty database
Storage__PublicBucket=gallery-dev-<...>     # dev-marked buckets (appsettings.Development.json already does this)
```

The browser fills the password on `/admin/signin` and lands on `/admin` as `local-owner`; the empty
database is now claimed by `local-owner`. `LocalSignInBrowserTests` does exactly this.

## Gate

```sh
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts
bash specs/018-local-dev-signin/evidence/gate.sh final
```

Expected: 0 warnings; every existing test passes unmodified (test-impact.md has no stated
changes); the new classes pass.

## Production (after merge and deploy)

Signed out, agent-run: `curl -s -o /dev/null -w '%{http_code}' -X POST https://port.zacal.me/admin/signin/local`
answers 401 (an unmapped admin address), and `/admin/signin` contains no `si-local`.
