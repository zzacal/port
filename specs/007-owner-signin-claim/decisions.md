# Decisions: 007 Owner Sign-In and Claim

The rows that most change what you see or what can break are 1, 3, 4 and 6. /speckit-clarify was
not run; every row below is a default unless marked OWNER.

| # | Decision | Owner or default | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | Every admin state-changing request is checked by an explicit anti-forgery filter on the `/admin` group; the framework's anti-forgery middleware is not used. D-06's mechanism is amended. | default (probe-driven) | Probe: with the middleware, POSTs that bind no form (sign-in start, sign-out) succeeded with no token, even with the "require token" metadata. Explicit validation refused them and accepted a token from the form field or the header (research R-04). | Low: one filter. |
| 2 | Google client `prompt=select_account` on every sign-in | default | "Try another account" must let Google ask which account to use (FR-009); the existing card links to the same route, so no component change. Cost: one extra click per sign-in, at most weekly. | Low: drop the parameter, add it only for the retry link. |
| 3 | The owner cookie scheme is named only on admin endpoints; no default auth scheme, and the framework's automatic single-scheme default is switched off | default (design- and probe-driven) | A default scheme reads the cookie, does the owner datastore read, and can add `Set-Cookie` on public pages, which breaks edge caching and byte-identical public markup (IV.18, SC-007). Probe part 3: with only the cookie scheme registered (no Google config), .NET made it the default by itself until `SuppressAutoDefaultScheme` was set. | Low. |
| 4 | Without Google config the app still runs; sign-in page says sign-in isn't set up, POST answers 503 | default | Registering the OIDC handler with a blank client id fails every request, public pages included (R-02). Every existing test host has no Google config. | Low. |
| 5 | Scope `openid` only; email and profile are never requested or stored | default | Ownership is `sub` (I.2); nothing reads email. Less personal data held. | Low: add a scope. |
| 6 | Local HTTPS on 5081 in the launch profile; `/admin` and `/signin-oidc` on plain HTTP redirect to it; only the HTTPS redirect address is used | default | `__Host-` and SameSite=None cookies need a secure context; Safari doesn't treat http://localhost as secure (R-09). Public pages stay on 5080 too. | Low. The unused http redirect URI in Google Cloud can stay or be deleted. |
| 7 | Refusal decided before any session is written, in the token-validated step | default | No session cookie can exist for a non-owner even transiently (I.3, SC-004). | Low. |
| 8 | Owner reset is a documented `mongosh` edit, not a shipped command | default | I.4 only requires out-of-band; a command is extra code with no second use yet. | Low: add a `reset-owner` mode beside `seed` later. |
| 9 | Launch-profile URL edited in place (no CLI exists for it) | default | Value change in an existing generated file, not scaffolding (VI). | None. |
| 10 | Sign-in tests drive the real handler against a test-only fake OIDC provider; only expiry and route-sweep tests mint session tickets directly | default | A minted cookie proves the gate, not the flow; the callback is where every security outcome is decided (R-10). | Medium: the fake provider is the largest test fixture. |
| 11 | `/ui/` mapped in all environments behind the owner session | default (D-21 as written) | D-21 says owner-only once sign-in exists (FR-021). | Low: revert to Development-only. |
| 12 | The sign-in page shows no owner bar | default | The owner bar's links are useless to someone who isn't signed in, and D-23 shows the card alone over the cover. | Low: one parameter. |
| 13 | Unbuilt admin routes answer 404 for the owner, sign-in redirect for others | default | Closed by default for 008/009 routes (FR-015); 404 is honest for the owner. | None. |
| 14 | Client IP is the direct connection address (`::1` locally) | default (roadmap) | PC.9 moved to the hosting feature (FR-024). | Low: forwarded-headers middleware with hosting. |
