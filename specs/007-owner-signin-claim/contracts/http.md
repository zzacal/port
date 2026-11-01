# HTTP Contract: Owner Sign-In and Claim (007)

All `/admin` responses carry `Cache-Control: no-store` and `X-Robots-Tag: noindex` (FR-017),
including redirects and errors. Every request emits `http.request` (IV.1). "Owner session" means a
valid `__Host-gallery` ticket whose `sub` equals the stored `claimedSubject` (research R-03, R-06).
"Token" means a valid anti-forgery token in the `__RequestVerificationToken` form field or the
`X-CSRF-TOKEN` header, matched to the `__Host-af` cookie (R-04).

## Routes

| # | Method + path | Session needed | Token needed | Responses |
|---|---|---|---|---|
| H1 | GET/HEAD `/admin/signin` | no | no | 200 sign-in page (H1 states below). With an owner session: 302 → `/admin`. |
| H2 | POST `/admin/signin` | no | yes | 302 → Google authorize URL with `prompt=select_account` (R-01). No token: 400 sign-in page, notice "Your sign-in form expired. Try again." Google not configured: 503 page (R-02). |
| H3 | POST `/signin-oidc` | no | n/a (state + nonce + correlation cookie, validated by the handler) | Claimed or signed in: session cookie set, 302 → `/admin`. Refused: 302 → `/admin/signin?r=refused`, no session cookie. Failed: 302 → `/admin/signin?r=failed`. |
| H4 | GET/HEAD `/admin` | yes | no | 200 owner shell. No session: 302 → `/admin/signin`. |
| H5 | POST `/admin/signout` | yes | yes | 302 → `/admin/signin`, session cookie deleted. No session: 401. No token: 400, session kept. |
| H6 | any other `/admin/**` | yes | yes for non-GET/HEAD | Owner: 404. No session: GET/HEAD 302 → `/admin/signin`; other methods 401. |
| H7 | GET/HEAD `/ui/`, `/ui/{slug}` | yes | no | Owner: 200 as before. No session: 302 → `/admin/signin`. All environments (FR-021). |
| H8 | public routes (`/`, `/g/...`) | ignored | no | Unchanged from 006. The owner cookie is never read; no `Set-Cookie` is ever added (FR-018, SC-007). |

Route order note: H6 is a `{**rest}` catch-all route on all methods inside the protected `/admin`
group, so authorization and the anti-forgery filter run before the 404. Do NOT use `MapFallback`:
it skips paths that look like files (`/admin/x.json`), which would then 404 without passing the
owner check (FR-015).

## Sign-in page states (H1)

| Condition | `SignInCard` State | Notice |
|---|---|---|
| unclaimed site | Claim | none |
| claimed site | SignIn | none |
| `?r=refused` (any site state) | Refused | none |
| `?r=failed` | SignIn or Claim per site state | "Sign-in didn't complete. Nothing was saved." |
| token missing on H2 (400) | SignIn or Claim | "Your sign-in form expired. Try again." |
| Google not configured | SignIn or Claim | "Sign-in isn't set up on this server." |

The page renders `AdminLayout` with `ShowOwnerBar=false`, the `SignInCard` with `Cover` = the
first home card's cover (`ContentReader.HomeAsync`, absent when no public gallery has an image),
`SiteName` = "Gallery", and `Antiforgery` set. The read of the owner record and of the home cards
are two timed reads.

## Owner shell (H4)

`AdminLayout` Title "Gallery admin", `Current` null, `ViewHref` "/", `SignOut` set. Main area:

```html
<section class="admin-empty">
  <h1>Signed in</h1>
  <p>Upload and gallery management arrive in later updates.</p>
</section>
```

No scripts (FR-020).

## Log events

As research R-11. Every event is one structured JSON line; field names are exactly `sub`, `ip`,
`reason`, `duration_ms`, `outcome`.

| Route | Events |
|---|---|
| H1 | `op.timing db.site.owner.read`, `op.timing db.site.read` |
| H3 claimed | `op.timing db.site.owner.read`, `op.timing db.site.claim` (won), `auth.claim`, `auth.signin` |
| H3 returning owner | `op.timing db.site.owner.read`, `auth.signin` |
| H3 refused | `op.timing db.site.owner.read`, [`op.timing db.site.claim` (lost), `op.timing db.site.owner.read`], `auth.refused` |
| H3 failed | `auth.failed` |
| H4, H6, H7 with a session | `op.timing db.site.owner.read` |
| H5 | `op.timing db.site.owner.read`, `auth.signout` |

## Invariants (each is a test assertion)

- I-1 No response to a request without an owner session contains `class="owner-bar"`,
  `admin-empty`, or `/ui/` catalogue markup (`ui-index`/`Specimen` output), on any route in H4–H7.
- I-2 No response on H3 for a refused or failed sign-in contains `Set-Cookie: __Host-gallery=`
  with a non-empty value.
- I-3 After a refused or failed sign-in, the `site` document is byte-identical to before.
- I-4 Every non-GET/HEAD `/admin/**` request without a token returns 400 or 401 and changes
  nothing, including with an owner session (FR-016).
- I-5 Every `/admin/**` and `/signin-oidc` response has `Cache-Control: no-store`.
- I-6 Public route responses are byte-identical with and without the owner cookie, and never carry
  `Set-Cookie`.
- I-7 `auth.claim` is logged exactly once per claimed site.
