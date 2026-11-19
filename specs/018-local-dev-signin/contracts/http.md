# HTTP contract: Local Sign-In Without Google

Existing routes: H1 `GET|HEAD /admin/signin`, H2 `POST /admin/signin`, H5 `POST /admin/signout`,
H6 the protected `/admin/**` catch-all. Every request already emits
`http.request {method} {path} {status} {duration_ms} {db_reads}`.

## H7 POST /admin/signin/local (new; mapped ONLY when `LocalSignIn.Enabled`)

Mapped with `app.MapPost` directly after H2, outside the protected group, so a signed-out visitor
can reach it. On any host where `Enabled` is false it is not mapped, and the address falls to H6:
signed-out POST → 401 (empty body), signed-out GET → 302 `/admin/signin`. No session either way.

| Input | Rule |
|---|---|
| antiforgery token (form field) | Validated by hand with `IAntiforgery.IsRequestValidAsync`, as H2 does. |
| `password` (form field) | `LocalSignIn.Matches(password)`; empty or missing = mismatch. |

| Outcome | Status | Response | Log |
|---|---|---|---|
| bad or missing token | 400 | sign-in page (current state), notice "Your sign-in form expired. Try again." | `auth.local.refused {ip} bad_form` |
| password mismatch | 401 | sign-in page (current state), notice "That password wasn't accepted." | `auth.local.refused {ip} bad_password` |
| claimed site | 302 `/admin` | owner cookie for the stored subject | `auth.local.signin {sub} {ip} false` |
| unclaimed site | 302 `/admin` | site claimed for `local-owner`; owner cookie | `auth.claim local-owner {ip} {at}` (existing), `auth.local.signin local-owner {ip} true` |
| lost claim race | 302 `/admin` | owner cookie for the winner | `auth.refused local-owner {ip} lost_race` (existing), `auth.local.signin {winner} {ip} false` |

No response, page or log line ever contains the configured or the submitted password.

Reads: `db.site.owner.read` (1, or 2 after a lost race), plus `db.site.claim` on an unclaimed site;
the re-rendered page on 400/401 also makes its existing cover read.

## H1 GET /admin/signin (changed when enabled)

When `LocalSignIn.Enabled`, `SignInPage` is rendered with `Local = true` (the local form appears in
the SignIn and Claim states), and the "Sign-in isn't set up on this server." notice is not set.
When it is not enabled, H1's output is byte-identical to today.

## Startup

- `auth.local.state {state}`: logged once while mapping, only when `LocalSignIn:Password` is
  present.
- `ProductionGuard`: `LocalSignIn:Password must not be set` when the key is present and
  `Hosting:Target=production` (the existing throw in `HostingSetup`).
