# Research: Local Sign-In Without Google

Read on 2026-11-18 against `018-local-dev-signin` at 2e676dc (main 945bcd6 + spec/constitution).
Probes ran as throwaway tests (deleted after the run); results quoted below.

## R-01 Where the decision is made: once, at startup

**Decision**: A pure rule `LocalSignIn.Evaluate(config, environmentName, StorageOptions,
MongoOptions)` returns one state: `enabled`, `absent` (no password setting), `too_short`, or
`not_development_host`. It runs once while the app is built. The development-host part calls the
existing `DevTargetGuard.Check(environment, storage, mongo)` (empty list = development host:
environment `Development`, `Mongo:Database` ending `_dev`, `Storage:PublicBucket` starting
`gallery-dev-`). It calls that guard rather than copying it.

**Rationale**: Every condition of FR-004 and I.7 is startup configuration, so there is nothing to
re-check per request except the password. Reusing `DevTargetGuard` keeps "development host" one
definition shared with the removal command (III.10's markers, as I.7 says).

**Alternatives**: a per-request check (re-reads configuration on every POST for no gain); a new
marker setting (`Hosting:Target=development`) (a second definition of "development" that can
drift from `DevTargetGuard`).

## R-02 The route exists only when enabled (constitution PATCH 6.2.1)

**Decision**: `POST /admin/signin/local` (H7) is mapped, beside H2, only when the state is
`enabled`. On every other host it is not mapped at all.

**Observed**: an unmapped `/admin/...` address is caught by the protected group's H6 catch-all,
which sits behind the owner policy. So a signed-out POST answers 401 with no body and a signed-out
GET answers 302 to `/admin/signin`. It cannot answer 404 to a signed-out visitor. Mapping the route
everywhere and answering 404 when disabled would put a handler on the production host. Not mapping
it removes the code path entirely.

**Consequence**: spec FR-004, FR-006, SC-002 and US2 said "answers not found". They are corrected
in place to "answers exactly as any unknown admin address does". Constitution I.7a is corrected the
same way: 6.2.1, a PATCH, because the wording changes but no guarantee does (no route at all is
stronger than a 404 handler). Edits: `evidence/plan-record-edit.py`.

## R-03 The password setting

**Decision**: configuration key `LocalSignIn:Password` (environment variable
`LocalSignIn__Password`). It is read through `IConfiguration`, so any configuration source
works: environment variables, user-secrets (loaded in Development), settings files, command-line
arguments or any other provider (owner directive after analyze). Minimum 16 characters; a shorter
value counts as unset (state `too_short`). ProductionGuard reads the same final configuration, so
it refuses the key from any source; a production host does not load `appsettings.Development.json`
or user-secrets, so only a key in a source production actually loads trips it.

**I.7a source**: constitution 6.2.2 says "from any configuration source". The earlier plan
reading ("never committed", enforced by a source-scan test) is withdrawn with that test.

**Comparison**: SHA-256 of the submitted and the configured UTF-8 password, then
`CryptographicOperations.FixedTimeEquals`. Hashing first makes the comparison time independent of
length (FR-014). The configured value is held only as its hash after startup.

## R-04 Production refusal (FR-006, I.7c)

**Decision**: `ProductionGuard.Check` gains one rule: when `LocalSignIn:Password` is present
(non-null, any value), it fails with `LocalSignIn:Password must not be set`. `HostingSetup` already
throws on any failure when `Hosting:Target=production`. Existing `ProductionGuardTests` stay valid:
`Valid()` has no such key, and the per-rule flip test enumerates its own list.

## R-05 Sign-in mechanics: reuse the claim path, mint the same cookie

**Decision**: H7 runs these steps in order:
1. Antiforgery check, as H2 does by hand (the route is outside the protected group). A failure
   re-renders the page with 400 "Your sign-in form expired. Try again.".
2. Password check. A failure re-renders the page with 401 "That password wasn't accepted."
3. `ownerStore.ReadOwnerAsync()`.
   - Owner present: sign in as that subject.
   - No owner: call `OwnerSignIn.ResolveAsync("local-owner", ip)`. This is the existing atomic
     claim and logs `auth.claim`. The outcome is `Claimed` → `local-owner`. If the claim lost a
     race, re-read the owner and sign in as the winner (I.7d).
4. `ctx.SignInAsync("owner", principal{sub}, IsPersistent=true)`. This is the cookie the OIDC
   handler issues after `OidcEvents.OnTokenValidated` (same scheme, a single `sub` claim,
   persistent).
5. Redirect 302 to `/admin`, the same landing as the Google challenge's `RedirectUri`.

**Rationale**: call, never copy. The claim stays the one atomic path (I.1, I.7d). The session goes
through the same cookie scheme, so `OnValidatePrincipal`'s per-request owner check, the sliding
7-day expiry and sign-out are unchanged (FR-002).

## R-06 Log events (FR-009, FR-010, SC-004; constitution IV)

Verbatim, via the existing `Gallery.Auth` logger:
- `auth.local.signin {sub} {ip} {claimed}`: one per successful local sign-in; `claimed` is
  `true` when this sign-in claimed the site (the claim itself also emits the existing `auth.claim`
  line, whose `sub` is `local-owner`).
- `auth.local.refused {ip} {reason}`, with reason `bad_password` or `bad_form` (antiforgery).
- `auth.local.state {state}`: once at startup, only when `LocalSignIn:Password` is present (states
  from R-01), so a too-short or misplaced password is visible from the log.
Request timing (`http.request POST /admin/signin/local {status} {duration_ms} {db_reads}`) and
`op.timing db.site.owner.read` / `db.site.claim` are already emitted by existing code. SC-001's
"under 2 s" is checked from the `http.request` line in a test.

## R-07 The sign-in card

**Decision**: `SignInCard` gains `[Parameter] public bool Local`. When it is true, the SignIn and
Claim states render a second form after the Google form:
`<form class="si-local" method="post" action="/admin/signin/local">` with the token, an `Input`
labelled "Local password" (`type="password"`, `name="password"`, required) and a Secondary button,
"Sign in locally" (SignIn state) or "Claim locally" (Claim state). The Google button stays the one
primary (UI-3). Refused state: no local form. It is rendered with the 007 optional-markup
technique: an expression fragment that is null by default (`</form>@LocalForm`), so `Local=false`
output stays byte-identical and the `/ui` baselines don't move.

`Input` gains `[Parameter] public string? Type`, rendered `type="@Type"`. Razor omits an attribute
whose value is null, so every existing `Input` stays byte-identical.

"Sign-in isn't set up on this server." (shown today when Google is not configured) is suppressed
while `Local` is true, so a local-only dev stack doesn't announce itself as broken. Spec corrected:
today the Google button shows with that notice; it is not hidden.

No new `/ui` specimen (V.4): the state is covered by component tests in a new file. Severable seam:
a "Local sign-in" specimen on `/ui/organisms/sign-in-card`, which would move that page's baseline.

## R-08 Test hosts: probes

- **Probe 1** (configure hook sets `b.Environment.EnvironmentName = "Development"`): the host
  fails at start with `FileNotFoundException: Sample manifest missing`, because AppBuilder eagerly
  loads the sample catalogue in Development and a test host's content root is the test output.
- **Probe 2** (args `--environment Development --contentRoot <repo>/src/Gallery.Web`, overrides
  last): starts. `env=Development`, `appName=testhost`, Google ClientId NOT loaded (the owner's
  user-secrets belong to `Gallery.Web`, not `testhost`, so they never leak into tests).
  `appsettings.Development.json` loads (`Storage:PublicBucket=gallery-dev-public`), and the
  fixture's overrides still win (the database was the test's).

**Decision**: `AdminHost.CreateAsync` gains `bool devHost = false`. When true it:
- passes those two args;
- names the database `{generated}_dev`;
- sets `gallery-dev-pub` / `gallery-dev-priv` buckets.

Each non-qualifying shape is a dev host with one override flipped. `AdminHost` and `LibraryHost`
run in the Production environment. The four browser fixtures already run in Development with
`--applicationName Gallery.Web` (they load the owner's user-secrets), but none names its database
with `_dev`, so the feature is absent there too (test-impact.md; corrected at tasks).
`LibraryBrowserFixture.NewSiteAsync` gains the same `devHost` option plus `ownerSession` (default
true) for the SC-006 browser test from an empty database.

## R-09 Test impact

Grep of `tests/` (2026-11-18) for `signin/local`, `LocalSignIn`, `local-owner`, password inputs,
endpoint inventories, the "isn't set up" notice and `si-notice`: the only hits are
`AdminAccessTests.cs:342` (notice on a non-dev host: unchanged) and `SignInComponentTests` (default
card: byte-identical). No stated test changes; every existing test must pass unmodified.

## R-10 Design record (VII.2)

D-04 gains the development-only local sign-in, and its Alternatives gain the two options the owner
rejected. The traceability matrix gains the I.7 row: `validate.sh` C6.ids failed with
`missing=[I.7]` after the 6.2.0 amendment, and passes with 0 failures after
`evidence/plan-record-edit.py` + `assemble.sh` (output `evidence/plan-record-edit.txt`).
