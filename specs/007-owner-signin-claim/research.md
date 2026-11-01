# Research: Owner Sign-In and Claim (007)

All decisions below were made against the code on `main` at b08f11a and a throwaway probe app
(`evidence/probe-auth.txt`, `evidence/probe-auth.sh`, `evidence/probe-auth-Program.cs.txt`) run on
the installed .NET 10.0.14 runtime with SDK 10.0.403 (global.json `latestPatch`).

## R-01 Sign-in handler: the framework's OIDC handler, one package

**Decision**: `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.14 (Microsoft, matches the
runtime patch), added with `dotnet add src/Gallery.Web package
Microsoft.AspNetCore.Authentication.OpenIdConnect --version 10.0.14`. Authority
`https://accounts.google.com`, `ResponseType = code`, scope `openid` only, `MapInboundClaims =
false` (keeps Google's `sub` claim as `sub`), `SaveTokens = false`,
`GetClaimsFromUserInfoEndpoint = false`, `CallbackPath = /signin-oidc`, and `prompt=select_account`
on every challenge.

**Observed (probe part 1)**: the challenge redirects to
`accounts.google.com/o/oauth2/v2/auth` with `response_type=code`, PKCE (`code_challenge_method=S256`),
`response_mode=form_post`, a nonce, a state, and `redirect_uri=https://localhost:5191/signin-oidc`.
An `AuthenticationProperties.Items["prompt"]` value copied in `OnRedirectToIdentityProvider` reaches
the URL as `prompt=select_account`. The correlation and nonce cookies are `path=/signin-oidc;
secure; samesite=none; httponly`. SameSite=None is required because Google returns by a cross-site
form POST.

**Why scope `openid` only**: ownership is bound to `sub` (I.2); email and profile are never used or
stored, so they are not requested (spec FR-005, SC-004).

**Why `prompt=select_account` always**: "Try another account" (FR-009) needs Google to ask which
account to use. Sending it on every sign-in costs the owner one click every 7 days at most, and the
existing `SignInCard` (005) already links "Try another account" to `/admin/signin` with no extra
parameter, so no component change is needed for it.

**Alternatives rejected**: Google's own `AddGoogle` (OAuth2 handler): it reads the user's identity
from the userinfo API rather than validating a signed ID token, and D-04 names OIDC. A hand-written
OAuth exchange: more code, no gain (V.2). Requesting `email`: nothing uses it.

## R-02 Missing Google configuration must not break the site

**Decision**: register the OIDC scheme only when `Authentication:Google:ClientId` and
`ClientSecret` are both non-blank. Without them the app starts, every public page works, `GET
/admin/signin` renders the card with the notice "Sign-in isn't set up on this server.", and `POST
/admin/signin` answers 503 with the same page.

**Why**: `UseAuthentication` asks every request-handling scheme whether it owns the request path on
every request, which initialises the OIDC handler and runs its options validation (ClientId
required). With a blank ClientId every request, public pages included, would fail. All existing
integration-test hosts have no Google config. The 006 lesson "public pages must not resolve
services that need secrets" applies (speckit-orchestration, post-implement review).

## R-03 Session cookie and its scope

**Decision**: the cookie scheme `owner` with name `__Host-gallery`, `Path=/`,
`SecurePolicy=Always`, `HttpOnly`, `SameSite=Lax`, `ExpireTimeSpan=7 days`, `SlidingExpiration=true`,
`IsPersistent=true` on sign-in (survives a browser restart, US-2 test). The ticket carries exactly
one claim, `sub`. No `DefaultScheme` / `DefaultAuthenticateScheme` is set; the OIDC scheme's
`SignInScheme` is `owner`; the admin authorization policy names the `owner` scheme explicitly.

**Observed (probe part 2)**: `set-cookie: __Host-gallery=…; path=/; secure; samesite=lax; httponly`.

**Not setting a default is not enough (probe part 3)**: when exactly one authentication scheme is
registered, ASP.NET Core makes it the default automatically. That is the no-Google-config shape
(R-02: cookie scheme only), and the probe showed a public request then authenticated from the owner
cookie. The app MUST set
`AppContext.SetSwitch("Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", true)` before
the authentication services are built; with it, the same request was anonymous. A test hosts the
app with no Google config and asserts a public page with the owner cookie emits no
`db.site.owner.read` and no `Set-Cookie`.

**Why no default scheme**: with a default authenticate scheme, `UseAuthentication` would read and
validate the cookie on every public request, run the owner check (a datastore read) and, past the
sliding half-life, add `Set-Cookie` to public responses, which makes them uncacheable and varies them
by session (IV.18, FR-018, SC-007). With the scheme named only on admin endpoints, a public request
from a signed-in owner is handled exactly like an anonymous one.

**Unauthenticated requests**: `OnRedirectToLogin` sends a 302 to `/admin/signin` for GET/HEAD and a
401 with no body for every other method. `OnRedirectToAccessDenied` answers 403. No ReturnUrl is
carried (the shell is the only destination in 007).

## R-04 Anti-forgery: validate explicitly on every admin state-changing request

**Decision**: one endpoint filter, `AdminAntiforgeryFilter`, on the `/admin` route group, calls
`IAntiforgery.ValidateRequestAsync` for every method other than GET, HEAD, OPTIONS and TRACE, and
answers 400 with no side effect when it throws `AntiforgeryValidationException`. The token is
accepted from the form field or the `X-CSRF-TOKEN` header, so the same filter covers 007's form
posts and 008's script-sent requests. The anti-forgery cookie is `__Host-af` with
`SecurePolicy=Always`. `app.UseAntiforgery()` is not added.

**Observed (probe part 2)**: with `UseAntiforgery()` in the pipeline, POSTs to an endpoint that does
not bind a form succeed with NO token, even with `RequireAntiforgeryTokenAttribute(true)` metadata
(`meta no-body: 200`, `meta form: 200`). The middleware only records a result that form binding
later checks. Explicit validation refuses both a bodiless and a form POST without a token (400) and
accepts a valid token from either the form field or the header (200). A first run without
`SecurePolicy=Always` on `__Host-af` produced a cookie the client refused to store, so every valid
token was then rejected (probe part 2a).

**Design impact**: D-06 says the built-in middleware "protects all HTML form posts" and needs the
header token only for JSON endpoints. That holds only for handlers that bind a form; sign-in start
and sign-out bind nothing. This plan amends D-06's mechanism (VII.2). The design record is edited
in this feature (tasks), not left for 008.

## R-05 The claim as one conditional upsert

**Decision**: `UpdateOne(filter {_id:"site", claimedSubject:null}, $set {claimedSubject, claimedAt,
claimedIp}, upsert: true)`.

| Situation | Driver result | Outcome |
|---|---|---|
| site doc exists, unclaimed (seeded with `claimedSubject: null`) | matched 1 | won |
| site doc missing (fresh datastore) | upserted id set | won |
| site doc claimed by anyone | duplicate-key error on `_id` (the upsert tries to insert `_id:"site"`) | lost |
| concurrent upserts on a missing doc | one inserts, the rest get duplicate-key or matched 0 | one won |

Every non-won result is followed by one read of `claimedSubject`: equal to the caller's `sub` means
the owner's own sign-in (a double-submitted callback), otherwise refused. The filter's
`claimedSubject: null` also matches a document where the field is absent. The seed's existing
`SetOnInsert` of the three fields (Seeder.cs step 5f) never overwrites a claim, so re-seeding keeps
the owner (spec edge case).

**Order of checks in the callback**: read `claimedSubject` first (one read). Equal → sign in. Set
and different → refuse (no write attempted). Null or missing → the conditional upsert above. This
keeps the claimed-site path to one read and makes every refusal skip writes.

**Alternatives rejected**: a transaction (the single-document conditional write is already atomic
and works on Atlas M0); a separate unique-index collection (more moving parts for the same result).

## R-06 The owner check on every admin request

**Decision**: the cookie scheme's `OnValidatePrincipal` reads `claimedSubject` (one read, op
`db.site.owner.read`) and rejects the principal and deletes the cookie when it differs from the
ticket's `sub` or the site is unclaimed (FR-014). It runs only on admin endpoints (R-03). After an
out-of-band reset, the old owner's cookie stops working on its next request.

## R-07 What the refused and failed paths do

**Decision**: inside `OnTokenValidated`, a refusal calls `context.HandleResponse()` and redirects to
`/admin/signin?r=refused` before any sign-in happens, so no session cookie is ever written for a
non-owner. `OnRemoteFailure` (cancel at Google, error response, bad state, replay, expired or
tampered token) logs `auth.failed` and redirects to `/admin/signin?r=failed`. The `r` value only
selects copy on a page anyone may see; a forged `?r=refused` shows the refused card and grants
nothing.

On success the principal is replaced by a new identity holding only `sub` before the cookie is
written (nothing else from Google is kept).

## R-08 Client IP

**Decision**: `HttpContext.Connection.RemoteIpAddress` (FR-024). No forwarded-header processing;
PC.9 moved to the hosting feature (roadmap). Locally this records `::1` or `127.0.0.1`.

## R-09 HTTPS locally

**Decision**: change the existing `http` launch profile's `applicationUrl` in
`src/Gallery.Web/Properties/launchSettings.json` to `https://localhost:5081;http://localhost:5080`
(rename the profile `https`), and send `/admin` and `/signin-oidc` requests that arrive over plain
HTTP to the HTTPS address with `UseHttpsRedirection` scoped to those paths (`UseWhen`). Only
`https://localhost:5081/signin-oidc` is used as the redirect address.

**Why**: the `__Host-` cookie and the SameSite=None correlation cookies need a secure context.
Chromium treats `http://localhost` as secure but Safari does not, so the plain-HTTP redirect address
the owner also registered is not relied on. Public pages stay reachable on 5080 so the owner's
existing habits and the browser tests (which bind plain HTTP on a random port, where no HTTPS port
exists and the redirect middleware passes through) are unaffected. The dev certificate is already
trusted (checked 2026-10-31).

**Constitution VI**: there is no CLI that edits a launch profile; this is a one-value change to an
existing generated file, not scaffolding. Recorded in decisions.md.

## R-10 Testing sign-in without Google

**Decision**: a test-only OIDC provider, `FakeGoogle`, in `tests/Gallery.Web.Tests/Fixtures/`: a
minimal Kestrel app on `127.0.0.1:0` serving `/.well-known/openid-configuration`, `/jwks` (one RSA
key generated per run), and `/token`, which returns an ID token signed with that key for the `sub`
and `nonce` the test packed into the authorization `code`. Tests point the app at it with the
configuration overrides `Authentication:Google:Authority` and
`Authentication:Google:RequireHttpsMetadata=false`. The app reads both keys; with no override the
authority is Google and HTTPS metadata is required.

The test drives the real flow: GET `/admin/signin` (anti-forgery cookie and token) → POST
`/admin/signin` (302 to the fake's authorize URL, carrying state and nonce, plus the correlation and
nonce cookies) → POST `/signin-oidc` with `code` and `state` as Google's form_post would. Cookies are
forwarded by hand from `Set-Cookie` to `Cookie`, because they are `Secure` and the test host is
plain HTTP; `HttpClientHandler.UseCookies = false`.

Token signing uses `Microsoft.IdentityModel.JsonWebTokens`, which arrives transitively with the OIDC
package through the project reference. No new test package.

**Why not mint session cookies directly for every test**: a test that writes the gate-satisfying
state itself proves the gate, not the flow (speckit-orchestration). The callback is where every
security outcome is decided, so it is exercised end to end. Minting a ticket with the app's own
`TicketDataFormat` is used only where the flow is irrelevant: the 7-day expiry tests (an expired
ticket and a ticket in its second half) and the access-control sweep.

## R-11 Observability

| Event | Fields | Emitted when |
|---|---|---|
| `auth.claim` | sub, ip, at | the claim write won (I.5, exactly once per site) |
| `auth.signin` | sub, ip | an owner session is issued (claim or returning owner) |
| `auth.refused` | sub, ip, reason (`not_owner` / `lost_race`) | a completed Google sign-in is refused |
| `auth.failed` | ip, reason (handler failure message class, no token contents) | `OnRemoteFailure` |
| `auth.signout` | sub, ip | sign-out |
| `op.timing db.site.owner.read` | duration_ms, outcome | every owner read (callback and admin requests) |
| `op.timing db.site.claim` | duration_ms, outcome (`won`/`lost`/`error`) | the conditional upsert |
| `http.request` | existing fields | every request; the callback appears as route `/signin-oidc` |

SC-006 is computed from these: ownership work is `db.site.owner.read` + `db.site.claim` per
callback (p95 < 50 ms), the whole return step is `http.request` for `/signin-oidc` (p95 < 1 s; it
includes the server-to-Google token exchange).

## R-12 `/ui/` owner-only

**Decision**: `/ui` is mapped in every environment behind the owner policy, replacing the
Development-only gate (D-21, FR-021). No existing test requests `/ui` over HTTP (the catalogue tests
render components in-process), and the browser tests do not visit it.

## R-13 Component additions (optional parameters, existing output unchanged)

Needed because the 005 components post forms without a token and have no notice slot:

- `SignInCard`: `Antiforgery` (`FormToken?`) renders a hidden input inside the form when set;
  `Notice` (`string?`) renders `<p class="si-notice" role="alert">` under the heading when set.
- `OwnerBar`: `SignOut` (`FormToken?`) renders a sign-out form with a Quiet button when set.
- `AdminLayout`: `ShowOwnerBar` (default true) and `SignOut` passed through.

With every new parameter at its default, markup is byte-identical, which
`UiPagesUnchangedTests` (006 baseline) already enforces for every catalogue page. No catalogue page
changes; new states are covered by component tests in a new file.
