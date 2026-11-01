# Implementation Plan: Owner Sign-In and Claim

**Branch**: `007-owner-signin-claim` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-owner-signin-claim/spec.md`

## Summary

The first `/admin` routes: a sign-in page over the home page's first cover, Google sign-in through
the framework's OIDC handler, the claim as one conditional upsert on the existing `site` document,
refusal for everyone else, a 7-day sliding `__Host-` session cookie scoped to admin endpoints only,
explicit anti-forgery validation on every admin state-changing request, an empty owner shell with
sign-out, and `/ui/` moved behind the owner session. Runs locally on HTTPS 5081. One new package.
Two probe findings shape the design: the built-in anti-forgery middleware does not protect POSTs
that bind no form (so D-06's mechanism is amended), and a default cookie scheme would touch every
public response (so the owner scheme is named on admin endpoints only).

## Technical Context

**Language/Version**: C# / .NET 10 (SDK 10.0.403 pinned `latestPatch`, runtime 10.0.14)

**Primary Dependencies**: existing (AWSSDK.S3, MetadataExtractor, MongoDB.Driver 3.13.0); new:
`Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.14 (research R-01). Cookie auth,
authorization and anti-forgery are in the shared framework.

**Storage**: MongoDB, existing `site` document, existing three owner fields (data-model.md)

**Testing**: xunit, Testcontainers Mongo (existing `MongoFixture`), real app hosts via
`AppBuilder.Build` + `HttpClient`, a test-only fake OIDC provider (research R-10), `HtmlRenderer`
component tests

**Target Platform**: owner's macOS machine, Kestrel HTTPS on localhost:5081 with the dev certificate

**Project Type**: web app (server-rendered minimal APIs + Razor components)

**Performance Goals**: SC-006 — ownership work < 50 ms p95, whole callback < 1 s p95, from logs

**Constraints**: public pages unchanged byte-for-byte and never carry `Set-Cookie` (FR-018,
SC-007); app must run with no Google config (R-02); no scripts on admin pages in 007 (FR-020)

**Scale/Scope**: one owner; 3 admin routes + callback + catch-all; 3 component additions; 2 pages

## Constitution Check

*GATE: before Phase 0 and re-checked after Phase 1.*

| Principle | Status | How |
|---|---|---|
| I.1 atomic first-sign-in claim | PASS | Conditional upsert on `{_id:"site", claimedSubject:null}` (R-05); 50-way race test against real Mongo (SC-002, FR-025). |
| I.2 bound to `sub` | PASS | Scope `openid` only, `MapInboundClaims=false`, ticket holds only `sub` (R-01, R-07). |
| I.3 others refused, no session | PASS | Refusal decided in `OnTokenValidated` before sign-in; `HandleResponse` prevents any cookie (R-07); invariant I-2/I-3. |
| I.4 no web reset | PASS | No route writes owner fields after the claim; recovery runbook is a `mongosh` edit (data-model). Guard: a source scan that the only writer of `claimedSubject` is `OwnerStore.TryClaimAsync` (and the seed's `SetOnInsert`). |
| I.5 claim logged with identity, IP, time | PASS | `auth.claim` with `sub`, `ip`, `at` (R-11); invariant I-7. |
| I.6 CSRF on all admin state changes | PASS | `AdminAntiforgeryFilter` on the `/admin` group validates every non-safe method, form or header token (R-04); invariant I-4. Sign-in start is a token-protected POST. |
| II.1–II.6 public viewing | PASS | Public routes untouched; owner cookie never read on them (R-03); SC-007 test. |
| III | N/A | No image handling. Cover on the sign-in page reuses the public read path. |
| IV.1 request timing | PASS | Existing `RequestTimingMiddleware` covers admin and `/signin-oidc`. |
| IV.2 named timing events | PASS | `db.site.owner.read`, `db.site.claim` via `OpTimer` (R-11). |
| IV.3 Server-Timing on public pages | PASS | Unchanged. |
| IV.4 time-bound SCs from logs | PASS | SC-006 via `evidence/p95.sh` over the captured log. |
| IV.18 edge-cacheable anonymous HTML | PASS | No default auth scheme, so no session handling or `Set-Cookie` on public routes; admin is `no-store` on its own routes (D-23). |
| IV.20 bfcache | PASS | Public responses unchanged; admin `no-store` is allowed (rule is for anonymous responses). |
| IV.22 no client framework on public pages | PASS | No scripts added anywhere. |
| V.1 traces to spec | PASS | Every route and component addition maps to an FR (contracts). |
| V.2 justified packages | PASS | One package, required by D-04 (OIDC ID-token validation); test signing reuses its transitive IdentityModel (R-10). |
| V.3 public pages work without JS | PASS | Unchanged; admin sign-in and sign-out also work without JS (FR-020). |
| VI official CLIs | PASS | Package via `dotnet add package`. The launch-profile URL edit is a value change in a generated file, not scaffolding (R-09, decisions #9). |
| VII.2 design deviations update the design | DEVIATION, handled | D-06 mechanism amended (R-04) and D-04 note on scheme scope and `prompt=select_account`; both edited in `specs/001-*/draft/` and reassembled in this feature (tasks). D-21 `/ui` gate executed as written. |
| VIII cost | PASS | No metered service added (Google sign-in is free). |
| Platform: Google OIDC, 7-day sliding cookie | PASS | R-01, R-03. |
| Platform: forwarded headers (PC.9) | N/A locally | The rule is to honour Cloudflare's forwarded headers; locally no edge sits in front, so there are none to honour. Trusting `CF-Connecting-IP` without the edge would let any client forge the I.5 claim IP. FR-024 records the direct connection IP; the hosting feature owns PC.9 (roadmap). |
| Workflow: tests against real Mongo, live walkthrough | PASS | Mongo container; quickstart live walkthrough with real Google. |

No Complexity Tracking entries.

**Post-Phase 1 re-check**: unchanged. The design adds no MUST deviation; the only deviation is the
D-06/D-04 mechanism update, which VII.2 permits when the design document is updated in the same
feature.

## Project Structure

### Documentation (this feature)

```text
specs/007-owner-signin-claim/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md, decisions.md
├── contracts/http.md, contracts/components.md
├── checklists/requirements.md
├── evidence/probe-auth.{txt,sh}, probe-auth-Program.cs.txt   # Phase 0 probe
└── tasks.md                                                  # /speckit-tasks
```

### Source Code

```text
src/Gallery.Web/
├── AppBuilder.cs                     # auth services, /admin + /ui mapping, HTTPS redirect for /admin
├── Properties/launchSettings.json    # https profile 5081 + 5080 (R-09)
├── Admin/
│   ├── AdminEndpoints.cs             # MapAdmin(): H1–H6 (contracts/http.md)
│   ├── AdminAntiforgeryFilter.cs     # R-04
│   ├── AdminHeaders.cs               # no-store + noindex on /admin and /signin-oidc
│   ├── Auth/AuthSetup.cs             # AddOwnerAuth(): cookie + conditional OIDC (R-01–R-03, R-06, R-07)
│   ├── Auth/OwnerSignIn.cs           # ResolveAsync outcome + auth.* events (data-model)
│   ├── Auth/GoogleOptions.cs         # ClientId, ClientSecret, Authority, RequireHttpsMetadata
│   └── Pages/SignInPage.razor, ShellPage.razor
├── Data/OwnerStore.cs                # ReadOwnerAsync, TryClaimAsync (R-05)
├── Components/ComponentTypes.cs      # + FormToken
├── Components/Organisms/SignInCard.razor(.css), OwnerBar.razor(.css)   # optional params
├── Components/Templates/AdminLayout.razor                              # optional params
└── Ui/UiEndpoints.cs                 # owner policy, all environments (R-12)

tests/Gallery.Web.Tests/
├── Fixtures/FakeGoogle.cs            # test OIDC provider (R-10)
├── Fixtures/AdminHost.cs             # app host + cookie forwarding + sign-in driver helpers
├── OwnerClaimTests.cs                # US-1, race (SC-002), seed-after-claim
├── OwnerSessionTests.cs              # US-2, minted tickets (SC-005), sign-out
├── RefusalTests.cs                   # US-3, I-2, I-3
├── AdminAccessTests.cs               # US-4, US-5, I-1, I-4, I-5, I-6, no-Google-config host
└── SignInComponentTests.cs           # contracts/components.md new states
```

**Structure Decision**: admin code goes in a new `Admin/` folder beside `Site/` (public) and `Ui/`,
mirroring the existing per-surface layout in Gallery.Web. Components stay organised by atomic level
(D-21); only endpoint pages live under `Admin/Pages/`, as `Site/Pages/` does for public pages.

## Design record updates (VII.2, in this feature)

- `specs/001-architecture-stack-design/draft/02a-decisions-platform.md`: D-06 Choice — explicit
  validation filter on every non-safe `/admin` request, token in form field or header; built-in
  middleware not relied on (probe evidence). D-04 Choice — append: owner scheme bound only to admin
  endpoints; `prompt=select_account`; scope `openid` only.
- Re-run `specs/001-architecture-stack-design/evidence/assemble.sh` then `validate.sh`; confirm
  `docs/design.md` is in the commit.
- `docs/roadmap.md`: 007 status on completion.

## Complexity Tracking

None.
