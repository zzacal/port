# Tasks: Owner Sign-In and Claim

**Input**: Design documents in `specs/007-owner-signin-claim/`: spec.md, plan.md, research.md,
data-model.md, contracts/http.md, contracts/components.md, quickstart.md, decisions.md.
**Source of truth**: the contracts and research decisions (R-xx). Implement exactly what they say.
If something is missing or contradictory, stop and report a blocker. Do not invent.

**Tests**: requested by the spec (FR-025, FR-026, every SC). Each story phase writes its tests in
the story's own test file.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081 in their own window.** Never kill or
  restart it, never `pkill` dotnet or Gallery.Web, and never bind :5080 or :5081. Build and test
  into a separate output:
  - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts`
  - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts`
  - A live instance, when a task asks for one: `ASPNETCORE_ENVIRONMENT=Development dotnet run
    --project src/Gallery.Web --no-build --no-launch-profile --artifacts-path artifacts --urls
    http://localhost:5090`. Stop only the PID you started.
- **Existing tests must pass UNMODIFIED.** That is every file in `tests/` before this feature
  (415 tests at b08f11a), including `UiPagesUnchangedTests`, `PublicOutputUnchangedTests`,
  `IndexTests`, `OrganismComponentTests` and the 005/006 baseline snapshots. A failure there means
  your change is wrong. Fix the code, never the test or a snapshot.
- **Never print, log, commit or echo a secret.** Never read user-secrets values or print the
  Google client id or secret. Code reads them through configuration only. Log events never carry
  tokens, codes, cookies, emails or names; `sub` and `ip` are the only identity fields.
- **Never commit** `artifacts/`, `dev-pictures/`, `.dev-prepared/`, `.r2-keys*`.
- Gate after every task: build with 0 warnings and 0 errors, all tests green. Docker must be
  running (`docker info`; if not, `open -a Docker` and wait).
- No `style=` attributes, no `<script>` anywhere in admin pages (FR-020), no NuGet package beyond
  `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.14 (added in T001). The test project
  uses `Microsoft.IdentityModel.JsonWebTokens` / `Microsoft.IdentityModel.Tokens`, which already
  reach it through the Gallery.Web project reference; do not add them as packages.
- Logging: timing uses the existing `OpTimer.Time` / `OpTiming.Log` exactly (`op.timing {op}
  {duration_ms} {outcome}`). Auth events use one logger category `Gallery.Auth` and exactly these
  templates (research R-11):
  - `auth.claim {sub} {ip} {at}`
  - `auth.signin {sub} {ip}`
  - `auth.refused {sub} {ip} {reason}` with reason `not_owner` or `lost_race`
  - `auth.failed {ip} {reason}` with reason = the failure exception's type name only
  - `auth.signout {sub} {ip}`
- Repo paths in tests: use `Render.RepoRoot` (walks up to `Gallery.slnx`). Never count `..` levels.
- Tasks marked **[SAFETY]** implement Constitution I.1–I.6 or the FR-018 public-isolation
  guarantee. They are blocking and non-deferrable: never stubbed, skipped, weakened, or left as
  follow-ups. A [SAFETY] test that passes on first write must be shown to fail by the high tier
  (T034) before the feature is accepted.
- Escalate (stop and report) on any design question. Executors never change the contracts.

## Phase 1: Setup (high tier, before dispatch)

- [X] T001 Add the package with the CLI:
  `dotnet add src/Gallery.Web package Microsoft.AspNetCore.Authentication.OpenIdConnect --version 10.0.14`.
  Build and test green; record the test count in `specs/007-owner-signin-claim/evidence/baseline.txt`.
- [X] T002 In `src/Gallery.Web/Properties/launchSettings.json` rename profile `http` to `https`
  and set `applicationUrl` to `https://localhost:5081;http://localhost:5080` (research R-09,
  decisions #6, #9). Nothing else in the file changes.
- [X] T003 Add `Action<WebApplicationBuilder>? configure = null` as a third optional parameter of
  `AppBuilder.Build` in `src/Gallery.Web/AppBuilder.cs`, invoked right after the overrides are
  added. Existing callers are unchanged. Purpose: tests add a log-capturing provider (T037).

## Phase 2: Foundational (blocks every user story)

Sequential, one executor. Files owned: everything listed in this phase.

- [X] T004 [P] Create `src/Gallery.Web/Admin/Auth/GoogleOptions.cs`: class `GoogleOptions` bound
  from section `Authentication:Google` with `ClientId`, `ClientSecret` (strings, default ""),
  `Authority` (default `https://accounts.google.com`), `RequireHttpsMetadata` (bool, default
  true), and `bool IsConfigured => !IsNullOrWhiteSpace(ClientId) && !IsNullOrWhiteSpace(ClientSecret)`.
- [X] T005 [P] **[SAFETY]** Create `src/Gallery.Web/Data/OwnerStore.cs` per data-model.md:
  scoped, constructor `(IMongoDatabase db, OpTimer timer)`, collection `site` as `BsonDocument`.
  - `Task<string?> ReadOwnerAsync()`: find `_id:"site"`, projection `{claimedSubject:1}`, return
    the string or null (missing doc, missing field, or BSON null). Wrapped in
    `timer.Time("db.site.owner.read", ..., isDbRead: false)`.
  - `Task<ClaimResult> TryClaimAsync(string sub, string ip, DateTime atUtc)`: exactly the upsert
    of research R-05 (`UpdateOneAsync` with filter `{_id:"site", claimedSubject:null}`,
    `$set` the three fields, `IsUpsert = true`). Won when `MatchedCount == 1` or `UpsertedId` is
    not null; a `MongoWriteException` whose `WriteError.Category == ServerErrorCategory.DuplicateKey`
    is Lost; any other exception propagates. Log through `OpTiming.Log` with op `db.site.claim`
    and outcome `won` / `lost` / `error` (do not use `timer.Time`, whose outcome is only ok/error).
  - `public enum ClaimResult { Won, Lost }` in the same file.
  - No other method writes `claimedSubject`, `claimedAt` or `claimedIp` anywhere in `src/`.
  Register `builder.Services.AddScoped<OwnerStore>()` in `AppBuilder.cs` beside `ContentReader`.
- [X] T006 [P] Add `public sealed record FormToken(string FieldName, string Value);` to
  `src/Gallery.Web/Components/ComponentTypes.cs`. Implement contracts/components.md for
  `src/Gallery.Web/Components/Organisms/SignInCard.razor` (+ `.razor.css` `.si-notice`, colour
  `var(--color-danger)`), `src/Gallery.Web/Components/Organisms/OwnerBar.razor` (+ `.razor.css`
  `.ob-signout { display: contents; }`), and `src/Gallery.Web/Components/Templates/AdminLayout.razor`.
  All new parameters default to absent; output with defaults must be byte-identical
  (`UiPagesUnchangedTests`, `PublicOutputUnchangedTests` green unmodified).
- [X] T007 [P] Write `tests/Gallery.Web.Tests/SignInComponentTests.cs` (HtmlRenderer via
  `Render.Html`): SignInCard with `Antiforgery` renders exactly one
  `<input type="hidden" name="F" value="V">` inside the form in SignIn and Claim states and none in
  Refused; `Notice` renders `<p class="si-notice" role="alert">` right after `</h1>` in all three
  states and is absent by default; OwnerBar `SignOut` renders a form posting to `/admin/signout`
  with the hidden input and a "Sign out" button, absent by default; AdminLayout `ShowOwnerBar=false`
  renders no `owner-bar`; `Guards.AssertNoInlineStyle` and `Guards.AssertNoScript` on each.
- [X] T008 **[SAFETY]** Create `src/Gallery.Web/Admin/Auth/AuthSetup.cs` with
  `IServiceCollection AddOwnerAuth(this IServiceCollection, IConfiguration)`:
  - First line: `AppContext.SetSwitch("Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", true);`
    (research R-03 probe part 3).
  - `AddAuthentication()` with NO default scheme arguments. Cookie scheme name `"owner"`
    (constant `AuthSetup.OwnerScheme`) with the options of research R-03: `__Host-gallery`, Path
    `/`, `SecurePolicy=Always`, `HttpOnly`, `SameSite=Lax`, `ExpireTimeSpan=7 days`,
    `SlidingExpiration=true`. Events: `OnRedirectToLogin` → 302 to `/admin/signin` for GET/HEAD,
    else 401 empty; `OnRedirectToAccessDenied` → 403; `OnValidatePrincipal` (research R-06) →
    resolve `OwnerStore` from `context.HttpContext.RequestServices`, read owner, and if null or not
    equal to the principal's `sub` claim: `context.RejectPrincipal()` and
    `await context.HttpContext.SignOutAsync(OwnerScheme)`.
  - Only when `GoogleOptions.IsConfigured`: `AddOpenIdConnect("google", ...)` with research R-01
    settings (`Authority`, `RequireHttpsMetadata` from options, `ClientId`, `ClientSecret`,
    `ResponseType="code"`, `Scope` cleared then `openid`, `MapInboundClaims=false`,
    `SaveTokens=false`, `GetClaimsFromUserInfoEndpoint=false`, `CallbackPath="/signin-oidc"`,
    `SignInScheme=OwnerScheme`, `OnRedirectToIdentityProvider` sets `ProtocolMessage.Prompt =
    "select_account"`). The `OnTokenValidated` and `OnRemoteFailure` handlers are filled in by T016 and
    T021; leave named empty methods `OnTokenValidated` / `OnRemoteFailure` in a new
    `src/Gallery.Web/Admin/Auth/OidcEvents.cs` static class and wire them now.
  - `AddAuthorization` with policy `"owner"` = `RequireAuthenticatedUser()` +
    `AddAuthenticationSchemes(OwnerScheme)`.
  - `AddAntiforgery(o => { o.Cookie.Name = "__Host-af"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.HeaderName = "X-CSRF-TOKEN"; })`.
  - Bind and register `GoogleOptions` as a singleton.
- [X] T009 **[SAFETY]** Create `src/Gallery.Web/Admin/AdminAntiforgeryFilter.cs`: an
  `IEndpointFilter` that, for any method other than GET, HEAD, OPTIONS, TRACE, calls
  `IAntiforgery.ValidateRequestAsync(httpContext)`; on `AntiforgeryValidationException` returns
  `Results.StatusCode(400)` without calling `next` (research R-04). The sign-in POST (H2) renders
  its own 400 page instead; it calls `IAntiforgery.IsRequestValidAsync` itself and is mapped
  outside the filter (T014).
- [X] T010 Create `src/Gallery.Web/Admin/AdminHeaders.cs`: middleware extension
  `UseAdminHeaders()` that, for request paths starting with `/admin` or `/signin-oidc`, registers
  `Response.OnStarting` to set `Cache-Control: no-store` and `X-Robots-Tag: noindex` (FR-017,
  invariant I-5). Overwrite any existing values.
- [X] T011 Create `src/Gallery.Web/Admin/AdminEndpoints.cs` with `MapAdmin(this WebApplication)`:
  a group `/admin` with `.RequireAuthorization("owner").AddEndpointFilter<AdminAntiforgeryFilter>()`
  and a group fallback (`{**rest}` on all methods) returning 404 (H6). Story phases add routes to
  this file. Wire into `src/Gallery.Web/AppBuilder.cs`: `builder.Services.AddOwnerAuth(builder.Configuration)`
  in the services block; in the pipeline, after `UseRouting()`:
  `app.UseWhen(c => c.Request.Path.StartsWithSegments("/admin") || c.Request.Path.StartsWithSegments("/signin-oidc"), a => a.UseHttpsRedirection());`
  then `app.UseAdminHeaders(); app.UseAuthentication(); app.UseAuthorization();` placed before
  `UseOutputCache()`, then `app.MapAdmin();` after `app.MapPublic()`. Do NOT add `UseAntiforgery()`.
- [X] T012 Create `tests/Gallery.Web.Tests/Fixtures/FakeGoogle.cs` (research R-10): an
  `IAsyncLifetime` that starts a minimal `WebApplication` on `http://127.0.0.1:0` serving:
  - `GET /.well-known/openid-configuration` → JSON with `issuer` = its base URL,
    `authorization_endpoint` = `{base}/authorize`, `token_endpoint` = `{base}/token`,
    `jwks_uri` = `{base}/jwks`, `response_types_supported` `["code"]`,
    `subject_types_supported` `["public"]`, `id_token_signing_alg_values_supported` `["RS256"]`.
  - `GET /jwks` → the public part of one RSA-2048 key generated per fixture, as a JWK set
    (`JsonWebKeyConverter.ConvertFromRSASecurityKey`, with `kid`).
  - `POST /token` (form) → reads `code` (format `{sub}.{nonce}` base64url-encoded) and returns
    `{ "access_token": "x", "token_type": "Bearer", "expires_in": 3600, "id_token": <JWT> }` where the
    JWT (`JsonWebTokenHandler.CreateToken`, RS256 with that key) has `iss` = base, `aud` = the
    `client_id` form value, `sub`, `nonce`, `iat`/`exp` now/+5 min. A code `fail` returns 400
    `{"error":"invalid_grant"}`.
  Expose `string Authority`, `static string Code(string sub, string nonce)`.
- [X] T013 Create `tests/Gallery.Web.Tests/Fixtures/AdminHost.cs`: helper that starts the app with
  `AppBuilder.Build(["--urls","http://127.0.0.1:0"], overrides, configure)`, overrides as in
  `PublicRoutesTests.StartAppAsync` plus, when a `FakeGoogle` is given,
  `Authentication:Google:ClientId=test-client`, `ClientSecret=test-secret`, `Authority`,
  `RequireHttpsMetadata=false`. Client: `HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }`
  with a manual cookie jar that copies every `Set-Cookie` name=value into the next request's
  `Cookie` header (Secure ignored; a cookie with an expiry in the past or empty value is removed).
  Helpers:
  - `Task<HttpResponseMessage> SignInAsync(string sub)`: GET `/admin/signin`, parse the hidden
    `__RequestVerificationToken` value, POST it to `/admin/signin`, read `state` and `nonce` from
    the 302 `Location` query, POST form `code=FakeGoogle.Code(sub, nonce)` and `state` to
    `/signin-oidc`, return that response.
  - `string? SessionCookie` (current `__Host-gallery` value), `Task<string> TokenAsync(string path)`
    (GET a page, return its hidden token value), `MintSession(string sub, DateTimeOffset issued, DateTimeOffset expires)`
    returning a cookie value built with the app's own
    `IOptionsMonitor<CookieAuthenticationOptions>.Get("owner").TicketDataFormat`.
  - `List<string> LogLines` filled by a test `ILoggerProvider` registered through `configure`,
    capturing the formatted message of every log entry (thread-safe).

**Checkpoint**: build and all existing tests green; `/admin` and any `/admin/x` answer 302 to
`/admin/signin` (which is not mapped yet, so 404 there is expected until T014).

## Phase 3: User Story 1, the owner claims an unclaimed site (P1) 🎯 MVP

**Goal**: sign-in page, Google sign-in, atomic claim, claim event, owner shell.
**Independent test**: unclaimed DB → sign in → shell; stored `sub`, time, IP; one `auth.claim`.

- [X] T014 [US1] Create `src/Gallery.Web/Admin/Pages/SignInPage.razor` and `ShellPage.razor`
  (contracts/components.md). Add to `AdminEndpoints.cs`, mapped on `app` (not the protected
  group): GET/HEAD `/admin/signin` (H1) and POST `/admin/signin` (H2), per contracts/http.md:
  - H1: if the `owner` scheme authenticates (`await ctx.AuthenticateAsync("owner")` succeeded) →
    302 `/admin`. Else read owner (`OwnerStore`), first home cover (`ContentReader.HomeAsync`,
    first card with a cover, mapped by `ViewMapper.ToCard(...).Image` using the same URL builder
    as `PublicEndpoints`; make `PublicEndpoints.MakeUrl` `internal` if needed), state/notice per the
    contract table (`r=refused` / `r=failed` / not configured), `Antiforgery` from
    `IAntiforgery.GetAndStoreTokens`.
  - H2: `IAntiforgery.IsRequestValidAsync` false → 400 page with the "form expired" notice. Not
    configured → 503 page with the "isn't set up" notice. Else
    `Results.Challenge(new AuthenticationProperties { RedirectUri = "/admin" }, ["google"])`.
  - GET/HEAD `/admin` (H4) inside the protected group: `ShellPage` with `SignOut` token.
- [X] T015 [US1] **[SAFETY]** Create `src/Gallery.Web/Admin/Auth/OwnerSignIn.cs` per
  data-model.md: scoped, `(OwnerStore store, ILoggerFactory loggers)`, logger `Gallery.Auth`,
  `Task<SignInOutcome> ResolveAsync(string sub, string ip)` implementing the outcome diagram
  exactly, emitting `auth.claim` (on Won only, with `at` = the `atUtc` passed to TryClaim, ISO-8601)
  and `auth.refused` (with reason). `auth.signin` is NOT emitted here (T016 emits it after the cookie
  is issued). `public enum SignInOutcome { Claimed, SignedIn, RefusedNotOwner, RefusedLostRace }`.
  Register scoped in `AuthSetup.AddOwnerAuth`.
- [X] T016 [US1] **[SAFETY]** Implement `OidcEvents.OnTokenValidated` in
  `src/Gallery.Web/Admin/Auth/OidcEvents.cs` (research R-07): read `sub` from
  `context.Principal`; missing → `context.Fail("no sub")`. ip =
  `HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`. Call `ResolveAsync`. For
  Claimed/SignedIn: replace `context.Principal` with a new `ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], AuthSetup.OwnerScheme, "sub", null))`,
  set `context.Properties.IsPersistent = true`, log `auth.signin`. For refusals:
  `context.HandleResponse(); context.Response.Redirect("/admin/signin?r=refused");` (no sign-in
  happens).
- [X] T017 [US1] Write `tests/Gallery.Web.Tests/OwnerClaimTests.cs` (`[Collection("mongo")]`,
  `MongoFixture`, `FakeGoogle`, `AdminHost`):
  - unclaimed (no site doc) → GET `/admin/signin` shows "Claim this site"; with seeded site doc
    (`claimedSubject: null`) same.
  - **[SAFETY]** `SignInAsync("owner-1")` → 302 `/admin`, `__Host-gallery` set; GET `/admin` →
    200 containing `admin-empty` and `owner-bar`; DB `claimedSubject == "owner-1"`, `claimedAt`
    within 1 min of now, `claimedIp` non-empty; `LogLines` contains exactly one `auth.claim`
    with `owner-1` and one `op.timing db.site.claim … won`.
  - **[SAFETY]** cookie attributes (FR-012; the test host ignores `Secure`, so assert the header
    text, research R-03/R-04): parse the raw `Set-Cookie` headers. The session cookie on the
    callback response is `__Host-gallery=` with attributes `path=/`, `secure`, `httponly`,
    `samesite=lax`, and NO `domain=`. The anti-forgery cookie set by GET `/admin/signin` is
    `__Host-af=` with `path=/`, `secure`, `httponly`, and NO `domain=` (probe part 2a: without
    `secure` a browser drops it and every form breaks). Compare attribute names case-insensitively.
  - the sign-in page (all three states) and the shell pass `Guards.AssertNoScript` and
    `Guards.AssertNoInlineStyle` (FR-020).
  - after claim, GET `/admin/signin` (no cookie, new client) shows "Sign in", not claim wording.
  - home with one public gallery seeded (reuse `PublicRoutesTests` seed shape) → sign-in page
    contains `has-cover`; with none → no `has-cover`.
  - **[SAFETY]** re-seeding keeps the claim: put this case in a separate class
    `OwnerClaimSeedTests` in the same file with `[Collection("s3")]` (constructor takes
    `MongoFixture` and `S3ProxyFixture`, exactly like `SeederWithS3Tests` in
    `tests/Gallery.Web.Tests/SeederTests.cs`). Run a real `Seeder` (same constructor call and
    manifest/prepared-root setup as `SeederWithS3Tests`) against a fresh DB, claim through
    `OwnerStore.TryClaimAsync("owner-1", ...)`, run a second real `Seeder.RunAsync` on the same DB,
    then assert `claimedSubject == "owner-1"` and `claimedAt`/`claimedIp` unchanged. Do NOT copy
    the seeder's update into the test; if the seeder cannot be driven this way, escalate.
- [X] T018 [US1] **[SAFETY]** Write `tests/Gallery.Web.Tests/ClaimRaceTests.cs`: against real
  Mongo, 50 tasks call `OwnerSignIn.ResolveAsync("sub-{i}", "ip")` concurrently, each in its own DI
  scope from one app host (no site doc at start); assert exactly one `Claimed`, 49
  `RefusedLostRace` or `RefusedNotOwner`, exactly one `auth.claim` line, stored `claimedSubject`
  equals the winner. Repeat with a seeded `claimedSubject: null` doc (SC-002, FR-025). Also: the
  winner calling again → `SignedIn`, no second `auth.claim`.

**Checkpoint**: US1 independently demonstrable with FakeGoogle.

## Phase 4: User Story 2, the owner signs in again later (P1)

**Goal**: returning sign-in, 7-day sliding session, sign-out.
**Independent test**: claimed DB → owner signs in → session; sign-out → sign-in card.

- [X] T019 [US2] Add POST `/admin/signout` (H5) inside the protected group in `AdminEndpoints.cs`:
  log `auth.signout {sub} {ip}`, `SignOutAsync("owner")`, 302 `/admin/signin`.
- [X] T020 [US2] Write `tests/Gallery.Web.Tests/OwnerSessionTests.cs`:
  - claimed by `owner-1`; `SignInAsync("owner-1")` → shell; DB unchanged; no `auth.claim`; one
    `auth.signin`.
  - **[SAFETY]** sign-out with token (from `TokenAsync("/admin")`) → 302 `/admin/signin`,
    `__Host-gallery` cleared; GET `/admin` → 302 `/admin/signin`; `LogLines` has exactly one
    `auth.signout` with `owner-1` (FR-023).
  - **[SAFETY]** sign-out without token, with session → 400, session still valid (GET `/admin` 200).
  - `MintSession` issued 1 day ago, expires in 6 days → 200 (SC-005 live).
  - `MintSession` issued 8 days ago, expired 1 day ago → 302 `/admin/signin` (SC-005 idle).
  - `MintSession` issued 4 days ago, expires in 3 days (past half-life) → 200 and a new
    `__Host-gallery` `Set-Cookie` (sliding renewal) carrying the same attributes as in T017
    (`path=/`, `secure`, `httponly`, `samesite=lax`, no `domain=`).
  - **[SAFETY]** owner reset out of band (set `claimedSubject` null in DB) → the old cookie gets
    302 `/admin/signin` (FR-014); set to another sub → same.
  - signed-in owner GET `/admin/signin` → 302 `/admin`.

## Phase 5: User Story 3, anyone else is refused (P1)

**Goal**: refused card, no session, nothing stored; failed sign-ins handled.
**Independent test**: claim with A, sign in with B → refused, no cookie, DB identical.

- [X] T021 [US3] Implement `OidcEvents.OnRemoteFailure` in `src/Gallery.Web/Admin/Auth/OidcEvents.cs`:
  log `auth.failed {ip} {reason}` (reason = `context.Failure?.GetType().Name ?? "unknown"`),
  `context.HandleResponse(); context.Response.Redirect("/admin/signin?r=failed");`.
- [X] T022 [US3] **[SAFETY]** Write `tests/Gallery.Web.Tests/RefusalTests.cs`:
  - claimed by `owner-1`; snapshot the site doc (`ToJson()`); `SignInAsync("intruder")` → 302
    `/admin/signin?r=refused`, no `Set-Cookie: __Host-gallery=` with a non-empty value on that
    response (I-2); site doc byte-identical (I-3); one `auth.refused … not_owner`; no
    `auth.signin`; GET `/admin/signin?r=refused` shows "Not the owner".
  - the refused client then GETs `/admin` → 302 `/admin/signin` (no admin content).
  - POST `/admin/signin` → `Location` contains `prompt=select_account` (FR-009).
  - callback with code `fail` → 302 `/admin/signin?r=failed`, page shows "Sign-in didn't
    complete", no session, site doc identical, one `auth.failed`.
  - **cancel at Google**: drive `SignInAsync` up to the authorize redirect, then POST to
    `/signin-oidc` the form `error=access_denied` with the real `state` (what Google sends when the
    user cancels) → 302 `/admin/signin?r=failed`, no session cookie, site doc identical, one
    `auth.failed`. Add an `AdminHost` helper `StartSignInAsync()` returning `state`/`nonce` for this
    (refactor `SignInAsync` to use it).
  - callback with a tampered `state` → same failed outcome.
  - callback replay: POST the same `code`+`state` a second time with the same cookies → failed
    outcome, no second `auth.signin` (the correlation cookie was consumed).

## Phase 6: User Story 4, admin is closed to anyone without the owner session (P1)

**Goal**: route sweep, CSRF, no-store, public isolation, no-config host.
**Independent test**: every `/admin` and admin action refused without the session; public markup
identical with and without it.

- [X] T023 [US4] **[SAFETY]** Write `tests/Gallery.Web.Tests/AdminAccessTests.cs`:
  - I-1: without a session, GET `/admin`, `/admin/upload`, `/admin/library`, `/admin/galleries`,
    `/admin/x/y` → 302 `/admin/signin`, body contains none of `owner-bar`, `admin-empty`.
  - I-4: without a session, POST/PUT/DELETE to `/admin`, `/admin/signout`, `/admin/x` → 401 or
    400, DB unchanged. With an owner session and no token → 400 for each (FR-016); with token on
    `/admin/x` → 404.
  - I-5: every response above, the sign-in page, and a `/signin-oidc` response carry
    `Cache-Control: no-store` and `X-Robots-Tag: noindex`.
  - Owner with session GET `/admin/upload` → 404.
  - `LogLines` contains an `http.request GET /admin` line and an `http.request POST /signin-oidc`
    line with status and duration (FR-022).
  - **I-6 / SC-007**: seed public content (reuse the `PublicRoutesTests` seed shape) and sign in
    as owner. For `/`, a gallery page, a photo page: body with the owner cookie == body without
    any cookie (byte-identical), and neither response has a `Set-Cookie` header; `LogLines` for
    those requests contains no `db.site.owner.read`.
  - Same I-6 assertion on a host with NO Google config and a minted session cookie (the
    single-scheme case of research R-03 probe part 3).
  - No-config host: GET `/` 200; GET `/admin/signin` 200 with "Sign-in isn't set up"; POST
    `/admin/signin` with token → 503.
  - HTTP→HTTPS: on a host bound to both `http://127.0.0.1:0` and `https://127.0.0.1:0`, GET
    `/admin` over http → 307/308 to https; GET `/` over http → 200 (no redirect). If binding the
    dev certificate in tests fails on the runner, escalate instead of deleting the case.

## Phase 7: User Story 5, the component catalogue becomes owner-only (P3)

- [X] T024 [US5] **[SAFETY]** In `src/Gallery.Web/Ui/UiEndpoints.cs` add
  `.RequireAuthorization("owner")` to the `/ui` group and update the summary comment (FR-021). In
  `AppBuilder.cs` map `/ui` in every environment (move `app.MapUi()` out of the `IsDevelopment`
  block; keep the eager fixture loads where they are).
- [X] T025 [US5] Write `tests/Gallery.Web.Tests/UiOwnerOnlyTests.cs`: Production-environment
  host: without a session GET `/ui/` and `/ui/{first catalogue slug}` → 302 `/admin/signin`, body
  without `Specimen`/catalogue markup; with a minted owner session → 200 and the index lists
  `UiCatalog.Entries.Count` links.

## Phase 8: Polish, design record, evidence (high tier unless noted)

- [X] T026 Write `specs/007-owner-signin-claim/runbook.md` (FR-027): creating the Google client
  (the owner's steps from 2026-10-31: project, Google Auth Platform, External/Testing, test users,
  Web client, redirect `https://localhost:5081/signin-oidc`, user-secrets keys
  `Authentication:Google:ClientId`/`ClientSecret`, `dotnet dev-certs https --trust`); the owner
  reset `mongosh` command from data-model.md and what it does to existing sessions (R-06); how to
  read `auth.claim` to detect E-02.
- [X] T027 **[SAFETY]** Add `tests/Gallery.Web.Tests/OwnerWriteGuardTests.cs`: scan every `*.cs`
  under `src/` (via `Render.RepoRoot`) for the strings `claimedSubject`, `claimedAt`, `claimedIp`;
  allowed only in `Data/OwnerStore.cs` and `Seed/Seeder.cs` (the latter only inside `SetOnInsert(`
  lines). Any other occurrence fails with the file name (I.4).
- [X] T028 Edit `specs/001-architecture-stack-design/draft/02a-decisions-platform.md`: D-06
  Choice/Rationale/Alternatives per plan.md "Design record updates" (explicit validation filter on
  every non-safe `/admin` request, token from form field or `X-CSRF-TOKEN` header, built-in
  middleware not relied on; cite 007 research R-04 and the probe; middleware-only listed as a
  rejected alternative). D-04 Choice: append owner scheme bound to admin endpoints only with the
  automatic default suppressed, `prompt=select_account`, scope `openid` only (007 R-01, R-03).
  Update the I.6 row in `draft/07-traceability.md` to the new mechanism. Run
  `bash specs/001-architecture-stack-design/evidence/assemble.sh` then `validate.sh`; all checks pass.
- [X] T029 Write `specs/007-owner-signin-claim/evidence/verify.sh` (tee to `verify-output.txt`),
  modelled on 006's: build/test gates; starts its own instance on :5090 (Development, no launch
  profile, user-secrets present) and checks: `/` has no `Set-Cookie`; `/admin` → 302
  `/admin/signin` with `no-store`; `/ui/` → 302 `/admin/signin`; GET `/admin/signin` then POST it
  with the token → 302 whose Location host is `accounts.google.com` and contains
  `prompt=select_account` and `scope=openid` (no further Google step); POST `/admin/signin` without
  token → 400. Committed-tree secret scan: load `Authentication:Google:ClientSecret` from
  user-secrets into a variable and `git ls-files | xargs grep -lF` it (prints file names only).
  Stops only its own PID.
- [X] T030 Write `specs/007-owner-signin-claim/evidence/p95.sh` (SC-006). **One input format**:
  plain message lines, one log message per line, exactly as the templates render them
  (`op.timing db.site.claim 3.21 won`, `http.request POST /signin-oidc 302 41.7 0`,
  `auth.claim owner-1 ::1 2026-…`). The script takes the file path as `$1`; if the first non-blank
  line starts with `{`, it first converts with `jq -r '.Message'` (the console JSON log of the live
  run), otherwise it reads the lines as they are (the test log). Then for each `/signin-oidc` `http.request` sum the preceding `db.site.owner.read` and
  `db.site.claim` `op.timing` durations of that callback (pair by order: `http.request` is logged
  when a request ends, so a callback's events are those after the previous `http.request` line of
  ANY route and before its own; requests are sequential in both sources), report p95 of the ownership sum (< 50) and of the callback
  `duration_ms` (< 1000), and the `auth.claim` count. Sources: `evidence/sc006-callbacks.log`
  (T031) and `evidence/live-run.log` (T033).
- [X] T031 Add `tests/Gallery.Web.Tests/SignInTimingEvidenceTests.cs` (trait `Category=Evidence`):
  60 sequential FakeGoogle callbacks (1 claim, 49 returning owner, 10 refused) on one host; writes
  `LogLines` (the formatted messages, one per line, no JSON, in the order logged) to
  `specs/007-owner-signin-claim/evidence/sc006-callbacks.log`. It stays in the default test run
  (it takes seconds), so the file is regenerated on every gate.
- [X] T032 Fold every executor `decisions_made` entry into `specs/007-owner-signin-claim/decisions.md`
  as numbered rows; reject any that contradict the contracts.
- [X] T033 Live walkthrough with real Google (owner at the keyboard, high tier guiding):
  quickstart.md steps 1–10 on `https://localhost:5081`, log captured to `evidence/live-run.log`;
  record observations in `evidence/live-walkthrough.md`; run `p95.sh` on both logs, tee to
  `evidence/p95-output.txt`.
- [X] T034 Non-vacuity (high tier): for each [SAFETY] guard, apply the deliberate break, confirm
  the named test fails on its assertion (not an exception), restore, `touch` the file, rebuild:
  (a) drop `claimedSubject:null` from the claim filter → ClaimRaceTests fails;
  (b) remove the refusal `HandleResponse` → RefusalTests I-2 fails;
  (c) remove `AdminAntiforgeryFilter` from the group → AdminAccessTests I-4 fails;
  (d) remove the `SuppressAutoDefaultScheme` switch → the no-config I-6 case fails;
  (e) remove `OnValidatePrincipal` owner check → OwnerSessionTests reset case fails;
  (f) remove `.RequireAuthorization` from `/ui` → UiOwnerOnlyTests fails;
  (g) add a `claimedSubject` write elsewhere → OwnerWriteGuardTests fails;
  (h) remove `SecurePolicy = Always` from the anti-forgery cookie → OwnerClaimTests cookie-attribute
  case fails;
  (i) run `p95.sh` on both logs and confirm it reports a non-zero callback count for each (a zero
  count means a format mismatch, not a fast app).
  Record each red message in `evidence/non-vacuity.txt`.
- [X] T035 Update `docs/roadmap.md` 007 status on completion (evidence paths, what was not
  delivered), clean rebuild (`dotnet clean` first) with 0 warnings, all tests green.

## Deferred (owner-reported, not in 007 scope)

- [ ] D-001 Redesign the sign-in page look (owner review 2026-10-31: "really ugly", the background
  cover image makes it bad). The cover-behind-the-card treatment comes from design D-23 ("one card
  over the home page's first cover"), so the fix needs a design decision first (drop or tone down
  the cover, or a different layout), then a D-23 amendment and a `SignInCard` change. Do not
  change it inside 007.

## Dependencies & Execution Order

- Phase 1 (high tier) → Phase 2 (one executor, sequential except T004–T007 which touch disjoint
  files) → story phases.
- US1 (T014–T018) first: it creates the sign-in page and the callback logic every other story uses.
- US2 (T019–T020), US3 (T021–T022), US4 (T023), US5 (T024–T025) all depend only on US1 and can run
  in parallel; file ownership is disjoint except `AdminEndpoints.cs` (US2 only, additive) and
  `OidcEvents.cs` (US3 only). US4 is test-only.
- Phase 8: T026, T027 any time after Phase 2; T028 any time; T029–T031 after all stories; T033 after
  T029–T031; T034 after T033; T035 last.

## Parallel opportunities

- Phase 2: T004, T005, T006+T007 in parallel (disjoint files); T008–T013 sequential.
- After US1: four executors — US2, US3, US4, US5 — each with its own
  `--artifacts-path artifacts/exec-<story>` and a test filter on its own class.
- Phase 8: T026, T027, T028 in parallel with the story batch.

## Implementation Strategy

MVP = Phase 1 + 2 + US1: the owner can claim and see the shell, the race is proven. US3 and US4
complete the security story and must land before the feature is accepted (all P1). US5 is
severable (decisions #11). Evidence and the live walkthrough close the feature.
