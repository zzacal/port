# Tasks: Local Sign-In Without Google

**Input**: design documents in `specs/018-local-dev-signin/`:
- spec.md (FR-001–FR-014, SC-001–SC-006)
- plan.md
- research.md (R-01–R-10)
- data-model.md
- contracts/http.md (H7 new; H1 changed when enabled)
- contracts/components.md (K1–K3)
- contracts/test-impact.md (no stated changes)
- quickstart.md

**Source of truth**: the contracts and the research decisions. Implement exactly what they say. If
something is missing or contradictory, stop and report a blocker. Do not invent anything.

**Tests**: required. The spec's SCs are measured checks, and constitution I.7 requires an automated
check that a production-shaped host has no local sign-in. Each batch writes its tests in the NEW
test files it names, in the same batch as the code.

## Rules for every executor

- **The owner may run `dotnet watch run` on :5080/:5081.**
  - Never kill or restart it, never run `pkill`/`killall`, and never bind :5080 or :5081.
  - Build and test into your own output:
    - `dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts/exec-<batch>`
    - `dotnet test Gallery.slnx --no-build --artifacts-path artifacts/exec-<batch> --blame-hang-timeout 120s --filter <your classes>`
  - macOS has no `timeout`; use `--blame-hang-timeout`. Docker must be running (`docker info`).
  - Resolve repo paths with the existing `Render.RepoRoot` helper, never a fixed `..` count.
- **Existing tests must pass UNMODIFIED.** There are no stated test changes in this feature
  (contracts/test-impact.md). Any red existing test means your change is wrong: fix the code,
  never the test, a fixture or a baseline. Executors never edit existing files under `tests/`
  (the parent owns the fixture changes in T003) or any file under `specs/`.
- **Never commit.** The parent commits after its own gate.
- **Forbidden:**
  - no new NuGet package, script, design token or `style=` attribute;
  - no edit to `OwnerStore`, `OwnerSignIn`, `OidcEvents`, `AuthSetup`, `DevTargetGuard`,
    `HostingSetup`, `AdminLayout`, `Button`, any public page, or anything under
    `src/Gallery.Web/Ui/`;
  - `AppBuilder.cs` belongs to the parent (T009);
  - do not copy `DevTargetGuard`'s checks or the claim logic: CALL `DevTargetGuard.Check` and
    `OwnerSignIn.ResolveAsync`.
- **Names, verbatim**:
  - `src/Gallery.Web/Admin/Auth/LocalSignIn.cs`: `public sealed class LocalSignIn` with
    `PasswordKey = "LocalSignIn:Password"`, `MinLength = 16`, `LocalOwner = "local-owner"`,
    `static LocalSignInState Evaluate(IConfiguration, string environmentName, StorageOptions, MongoOptions)`,
    `LocalSignInState State`, `bool Enabled`, `bool Matches(string? submitted)`, a constructor that
    takes `(IConfiguration, string environmentName, StorageOptions, MongoOptions)`;
  - `public enum LocalSignInState { Enabled, Absent, TooShort, NotDevelopmentHost }`; log names
    `enabled`, `absent`, `too_short`, `not_development_host`;
  - route `POST /admin/signin/local`, comment `// H7`;
  - component parameters `Input.Type` (string?), `SignInCard.Local` (bool), `SignInPage.Local`
    (bool); form class `si-local`; field `name="password"`; labels "Local password",
    "Sign in locally", "Claim locally";
  - notices: "That password wasn't accepted." (401) and "Your sign-in form expired. Try again." (400);
  - ProductionGuard message: `LocalSignIn:Password must not be set`.
- **Log events, verbatim** (logger category `Gallery.Auth`):
  - `auth.local.signin {sub} {ip} {claimed}` (`claimed` is `true`/`false`);
  - `auth.local.refused {ip} {reason}` (`bad_password` | `bad_form`);
  - `auth.local.state {state}` (once at startup, only when the key is present).
  - Existing and unchanged: `auth.claim`, `auth.refused`, `http.request`, `op.timing db.site.*`.
- **[SAFETY]** marks the I.7 controls: the dev-host and password gate, the unmapped route, the
  production refusal, the password never leaking, and the forgery check. Blocking and
  non-deferrable; T019 breaks each on purpose.
- **Tight assertions**: compare status codes, `Location` and subjects for EQUALITY; "absent" means
  no element (`class="si-local"` / `action="/admin/signin/local"`), never a bare substring; assert
  "no owner cookie" by checking that no `Set-Cookie` names `__Host-gallery`.
- Escalate (stop and report) on any design question. After two failed attempts on one task, stop
  and hand it back.
- **Your final message must be ONLY the JSON report** (`tasks_completed`, `build_succeeded`,
  `tests_passed`, `decisions_made`, `blockers`).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (parent)

- [X] T001 Record the baseline in `specs/018-local-dev-signin/evidence/baseline.txt`:
  `git rev-parse HEAD` and the full-suite counts, taken from the finished run's own output.
- [X] T002 Copy `specs/017-photo-edit-screen/evidence/gate.sh` to
  `specs/018-local-dev-signin/evidence/gate.sh`. Output `/tmp/018-gate-$L.txt`; add
  `specs/017-photo-edit-screen/evidence` to the restored dirs; keep `PHOTO_TIMING_LOG=/tmp/...`.
- [X] T003 Test infrastructure (parent; existing tests must stay green):
  - `tests/Gallery.Web.Tests/Fixtures/AdminHost.cs`: `CreateAsync` gains `bool devHost = false`.
    When it is true:
    - pass `--environment Development --contentRoot <Render.RepoRoot>/src/Gallery.Web`;
    - name the database `{generated}_dev` (a new database with that name on the same client);
    - set `Storage:PublicBucket = gallery-dev-pub` and `Storage:PrivateBucket = gallery-dev-priv`;
    - expose the database name through the existing `DatabaseName`.
    `devHost` is false by default, so existing callers are unchanged.
  - `tests/Gallery.Web.Tests/Fixtures/LibraryBrowserFixture.cs`: `NewSiteAsync` gains
    `bool devHost = false` (database `{generated}_dev`; a public bucket with the `gallery-dev-`
    prefix if the fixture's own isn't) and `bool ownerSession = true`. With `false`, no claim and
    no minted session; the site's session is null and `NewContextAsync` injects no cookie.
  - Prove it: one throwaway fact per fixture that starts a dev host and reads
    `IWebHostEnvironment.EnvironmentName == "Development"` and a `Mongo:Database` ending `_dev`. Run
    it, then delete it. Then run `AdminAccessTests` and `GridWidthBrowserTests` green, unmodified.
- [X] T004 Re-run the test-impact grep (header of contracts/test-impact.md). Add any new hit as a
  row "(found at implement)".

## Phase 2: Foundational (Batch A, executor; blocks every story)

Batch A owns: the new `src/Gallery.Web/Admin/Auth/LocalSignIn.cs`,
`src/Gallery.Web/Hosting/ProductionGuard.cs` (one rule), `src/Gallery.Web/Components/Atoms/Input.razor`
(K1), and the new `tests/Gallery.Web.Tests/LocalSignInRuleTests.cs`.

- [X] T005 [P] [SAFETY] Write `LocalSignIn.cs` per data-model.md:
  - `Evaluate` order: absent/whitespace → `Absent`; `DevTargetGuard.Check(environmentName, storage, mongo)`
    not empty → `NotDevelopmentHost`; length < 16 → `TooShort`; else `Enabled`;
  - the constructor computes `State` and, only when `Enabled`, keeps the SHA-256 of the UTF-8
    password (never the plain string);
  - `Matches`: false when not enabled or the input is null/empty; else SHA-256 of the input compared
    with `CryptographicOperations.FixedTimeEquals`;
  - no logging in this class.
- [X] T006 [P] [SAFETY] In `ProductionGuard.Check`, add: when `c[LocalSignIn.PasswordKey] is not null`,
  add the failure `LocalSignIn:Password must not be set` (the key, never the value). Nothing else
  changes.
- [X] T007 [P] K1: add `[Parameter] public string? Type` to `Input.razor` and render `type="@Type"`
  on the single-line `<input>` only. Existing usages must render byte-identically
  (`UiPagesUnchangedTests` must stay green).
- [X] T008 Write `tests/Gallery.Web.Tests/LocalSignInRuleTests.cs` (pure, no host):
  - `Evaluate` truth table over environment {Development, Production}, database {`x_dev`, `x`},
    public bucket {`gallery-dev-p`, `p`}, password {absent, whitespace, 15 chars, 16 chars}:
    `Enabled` only for Development + `_dev` + `gallery-dev-` + 16 chars; `NotDevelopmentHost` for
    any non-dev shape with a password present (including 15 chars); `Absent` whenever the password
    is missing or whitespace; `TooShort` only on a dev host with 15 chars;
  - `Matches`: equal → true; different, different-length, empty, null → false; a disabled instance
    → false even for the configured value;
  - `ProductionGuard.Check(Valid() + LocalSignIn:Password)` → exactly one failure, equal to
    `LocalSignIn:Password must not be set`, and containing no part of the value (copy the
    `Valid()` dictionary from ProductionGuardTests into this file; do not edit that file);
  - `Input` with `Type="password"` renders `type="password"`; without `Type` it renders no `type`
    attribute (`Render.Html<Input>`).

**Checkpoint A (parent)**: read the diff; T009; gate A (full suite green, no test changes); commit.

- [X] T009 Parent seam, `src/Gallery.Web/AppBuilder.cs`: after the Storage and Mongo options are
  built, register `new LocalSignIn(builder.Configuration, builder.Environment.EnvironmentName,
  storageOptions, mongoOptions)` as a singleton. Nothing else in AppBuilder changes.

## Phase 3: US1 + US2 — the local sign-in, and only on a dev host (P1) 🎯 MVP

The two stories share one route and one rule: US1 is the route working on a dev host, and US2 is
the same route absent everywhere else. One batch implements both, with story-labelled tasks.

Batch B owns: `src/Gallery.Web/Admin/AdminEndpoints.cs`,
`src/Gallery.Web/Admin/Pages/SignInPage.razor`,
`src/Gallery.Web/Components/Organisms/SignInCard.razor` and `.razor.css`, and the new
`tests/Gallery.Web.Tests/LocalSignInTests.cs`, `LocalSignInGuardTests.cs` and
`LocalSignInBrowserTests.cs`.

- [X] T010 [P] [US1] K2: in `SignInCard.razor`, add `Local` and `LocalForm` exactly per
  contracts/components.md; `@LocalForm` goes directly after the Google form's `</form>` in the
  SignIn and Claim branches. Add the `.si-local` rule to `SignInCard.razor.css`. `Local = false`
  must be byte-identical (`UiPagesUnchangedTests`, `SignInComponentTests`,
  `OrganismComponentTests` green, unmodified).
- [X] T011 [P] [US1] K3: `SignInPage.razor` gains `Local` and passes it to `SignInCard`.
- [X] T012 [US1] [US2] [SAFETY] In `AdminEndpoints.MapAdmin` (contracts/http.md):
  - resolve `LocalSignIn` from `app.Services`; when `LocalSignIn:Password` is present in
    configuration, log `auth.local.state {state}` once;
  - H1: pass `[nameof(SignInPage.Local)] = local.Enabled`; when enabled, do not set the "isn't set
    up" notice;
  - H7: `if (local.Enabled) app.MapPost("/admin/signin/local", …); // H7`, directly after H2;
    handler steps exactly per research R-05 and data-model.md (antiforgery → `Matches` → owner read
    → `OwnerSignIn.ResolveAsync(LocalSignIn.LocalOwner, ip)` when unclaimed → re-read on a lost race
    → `ctx.SignInAsync(AuthSetup.OwnerScheme, principal{"sub"}, IsPersistent = true)` →
    `Results.Redirect("/admin")`);
  - the 400/401 re-renders use the same page parameters as H2's 400 branch, with `Local = true`
    and the contract's notices. Put the page rendering in one private helper that the new code
    calls; do not paste a fourth copy of the cover lookup (H1 and H2 may keep theirs unchanged);
  - the password is never logged, rendered or put in an exception message.
- [X] T013 [US1] Write `tests/Gallery.Web.Tests/LocalSignInTests.cs` (`AdminHost(devHost: true)`
  with `LocalSignIn:Password` = a 20-character value; `FakeGoogle` configured):
  - claimed site (claim `owner-1` through `OwnerStore.TryClaimAsync`): the GET page has exactly one
    `form.si-local` with `action="/admin/signin/local"`, a `type="password"` field named
    `password`, the antiforgery field and the button "Sign in locally"; the Google form is still
    there and is still the only Primary button;
  - unclaimed site: the button reads "Claim locally";
  - right password on the claimed site: 302 `Location` EQUALS `/admin`; an owner cookie is set; GET
    `/admin` with it answers 200; the session's subject is `owner-1` (one
    `auth.local.signin owner-1 … false` line); `site.claimedSubject` is still `owner-1`;
  - right password on an unclaimed site: 302 `/admin`; `claimedSubject == "local-owner"`; one
    `auth.claim local-owner` line and one `auth.local.signin local-owner … true` line;
  - a Google sign-in (FakeGoogle, subject `owner-2`) on that locally claimed site is refused
    (I.7d);
  - wrong password, empty password, no password field: each 401, notice "That password wasn't
    accepted.", no owner cookie, one `auth.local.refused … bad_password` line each;
  - no antiforgery token: 400, notice "Your sign-in form expired. Try again.", no owner cookie,
    `auth.local.refused … bad_form`;
  - the local session works on an admin form: POST `/admin/signout` with the token → 302
    `/admin/signin`;
  - no response body or header and no captured log line contains the configured password or any
    submitted password (check every response and the whole `LogLines`) [SAFETY];
  - SC-001: the `http.request POST /admin/signin/local 302` line's duration < 2000 ms.
- [X] T014 [US2] [SAFETY] Write `tests/Gallery.Web.Tests/LocalSignInGuardTests.cs`. Each host
  below has `LocalSignIn:Password` = the same 20-character value unless stated otherwise:
  - shapes: (a) Production environment (plain `AdminHost`, no devHost); (b) dev host with
    `Mongo:Database` without `_dev` (override); (c) dev host with `Storage:PublicBucket` without
    `gallery-dev-`; (d) dev host without the key; (e) dev host with a 15-character value;
  - for each: the GET page has no element with `class="si-local"` and no
    `action="/admin/signin/local"`; a POST to `/admin/signin/local` with a valid token and the right
    password answers 401 (and a GET answers 302 to `/admin/signin`), exactly like
    `/admin/signin/nonexistent`; no owner cookie; `claimedSubject` unchanged (null on an unclaimed
    site);
  - (a)–(c) and (e) log exactly one `auth.local.state` line with `not_development_host` (or
    `too_short` for e); (d) logs none;
  - a host built with `Hosting:Target=production`, the `ProductionGuardTests.Valid()` settings and
    `LocalSignIn:Password` refuses to build: the exception message contains
    `LocalSignIn:Password must not be set` and not the value (follow ProductionGuardTests' existing
    "refuses to build" fact for how it builds the host).
- [X] T015 [US1] Write `tests/Gallery.Web.Tests/LocalSignInBrowserTests.cs`
  (`[Collection("library-browser")]`, `NewSiteAsync(devHost: true, ownerSession: false,
  extraOverrides: {LocalSignIn:Password})`). SC-006: from an empty database in a fresh browser
  context, open `/admin/library` → lands on `/admin/signin`; fill "Local password", click "Claim
  locally" → lands on `/admin`; open `/admin/library` → 200 and the library heading. Then check in
  the datastore that `claimedSubject` is `local-owner`. Park the mouse at (0,0) before each
  navigation and wait for loads with `BrowserWait.LoadedAsync`, as the other browser tests do.

**Checkpoint B (parent)**: grep the merged tree for each [SAFETY] mechanism:
- the `if (local.Enabled)` guard around H7;
- `FixedTimeEquals`;
- the ProductionGuard rule;
- the manual antiforgery call in H7;
- no `password` in any log template.

Then run the quickstart owner steps against a throwaway dev host, run gate B, and commit.

## Phase 4: Polish (parent)

- [X] T016 [P] Write `specs/018-local-dev-signin/decisions.md`: the clarify answers (OWNER), the
  plan corrections (R-02 the 6.2.1 wording, R-07 the notice, the test-impact correction about
  browser fixtures, the "environment" reading of I.7a), every executor `decisions_made`, and the
  severable seam (a `/ui` specimen).
- [X] T017 [P] Write the 018 entry in `docs/roadmap.md`: status, evidence, OWNER-ONLY O-01.
- [X] T018 Re-run the test-impact grep against the finished tree; confirm no existing test file
  changed (`git diff --stat <base> -- tests/` lists only the new files and the T003 fixtures).
- [X] T019 Write `specs/018-local-dev-signin/evidence/non-vacuity.sh` from 017's. One break per
  guard, each anchored to the code line (not a comment) and failing on its own assertion:
  - `Evaluate` skips the dev-host check → LocalSignInRuleTests table and GuardTests (b) red;
  - `MinLength` 16 → 1 → RuleTests 15-char row and GuardTests (e) red;
  - the ProductionGuard rule removed → RuleTests guard fact and GuardTests production build red;
  - `Matches` returns true → LocalSignInTests wrong-password fact red;
  - H7 mapped without `if (local.Enabled)` → GuardTests (a)–(e) POST facts red;
  - H7 skips the antiforgery check → LocalSignInTests no-token fact red;
  - H7 signs in as `local-owner` without reading the owner → LocalSignInTests claimed-site subject
    fact red;
  - the `auth.local.refused` template gains the submitted password → LocalSignInTests no-leak fact
    red.
  Run `NV_DRYRUN=1`, then the real run; tee to `non-vacuity.txt`.
- [X] T020 Final gate (`evidence/gate-final.txt`): `dotnet clean`, `-warnaserror` build, full
  suite; restore earlier features' logs; commit.
- [ ] T021 Merge to main (`--no-ff`, body as in 017's) and push; watch the deploy go green. Signed
  out, on port.zacal.me:
  - `curl -s -o /dev/null -w '%{http_code}' -X POST https://port.zacal.me/admin/signin/local` → 401;
  - `/admin/signin` contains no `si-local`.
  Save to `evidence/browser-prod.txt`. The production configuration has no `LocalSignIn:Password`,
  so the deploy itself proves the guard did not fire.

## OWNER-ONLY (after the build; not gates)

- [ ] O-01 On your machine: `dotnet user-secrets set LocalSignIn:Password "<16+ chars>" --project
  src/Gallery.Web`, Ctrl+R the watch window, then sign in at https://localhost:5081/admin/signin
  with "Sign in locally".

## Dependencies and execution order

- Setup (parent): T001 → T002 → T003 → T004.
- Batch A: T005 ‖ T006 ‖ T007 → T008; then the parent's T009; checkpoint A.
- Batch B (needs A and T009): T010 ‖ T011, then T012; then T013, T014, T015; checkpoint B.
- Polish: T016 ‖ T017, T018 → T019 → T020 → T021.

## Parallel example

```text
Wave 1: Batch A: T005 (rule) ‖ T006 (guard) ‖ T007 (Input), then T008; parent T009
Wave 2: Batch B: T010 (card) ‖ T011 (page), T012 (routes), then T013–T015
Wave 3: T016 decisions ‖ T017 roadmap; T018; non-vacuity; final gate; merge
```

## Implementation strategy

- MVP = Phases 1–3: the local sign-in on dev hosts and its absence everywhere else. Each half is
  pointless without the other, so they ship together.
- Severable seams: a `/ui` "Local sign-in" specimen (moves a baseline); the browser test T015
  (the HTTP tests cover the same flow; it proves SC-006 in a real browser).
