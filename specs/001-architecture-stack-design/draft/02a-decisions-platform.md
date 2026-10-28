## 2. Decision Record

### D-01: App Service Linux B1 with .NET 10 Minimal APIs
**Area**: hosting-runtime
**Status**: given
**Choice**: Azure App Service Linux B1 with Always On enabled, running .NET 10 minimal APIs.
**Rationale**: Platform Constraints (v3.0.0) fix the hosting tier to Azure App Service Linux B1 and the runtime to .NET 10 minimal APIs; Constitution VIII.1 bounds the fixed recurring cost to one B1 plan; Always On prevents the Atlas cluster from pausing after 30 days of zero connections (research R-08; research R-03).
**Alternatives**: None; the hosting tier and runtime are binding Platform Constraints (v3.0.0) with no alternative configurations evaluated in this design.
**Pivot cost**: high: changing the hosting tier or runtime requires a constitution amendment, re-provisioning the managed identity and access restrictions, and re-validating all cost projections.

### D-02: Cloudflare Free in Full (Strict) TLS Mode with Origin CA
**Area**: edge-dns-tls
**Status**: given
**Choice**: Cloudflare Free plan with DNS proxy, Full (strict) TLS mode, and a 15-year Cloudflare Origin CA certificate installed on the origin [cf Q5].
**Rationale**: Constitution VIII.2 mandates free TLS certificates; Platform Constraints (v3.0.0) specify Cloudflare DNS, proxy, cache, and TLS in Full (strict) mode — never Flexible. Cloudflare Universal SSL covers the edge; the Origin CA is free and lasts 15 years (research R-01; [cf Q5]).
**Alternatives**: Flexible TLS mode was rejected because it leaves the edge-to-origin hop unencrypted and the Platform Constraints forbid it. A paid or publicly trusted origin certificate was rejected by Constitution VIII.2. Cache Reserve was rejected as paid-only [cf Q8]. A Cloudflare Worker in front of the origin was rejected as unnecessary because Cache Rules already handle the session-cookie bypass (research R-01).
**Pivot cost**: low: switching to a different edge or TLS provider requires DNS record changes and replacing the origin certificate, with no data migration.

### D-03: Cloudflare IP Allowlist plus Authenticated Origin Pulls
**Area**: origin-lockdown
**Status**: chosen
**Choice**: Cloudflare's published IPv4 and IPv6 ranges added to App Service access restrictions (up to 512 IP rules); Authenticated Origin Pulls (mTLS, free on all Cloudflare plans) enabled; the app reads the real client IP from `CF-Connecting-IP` and trusts only Cloudflare ranges as known networks [az Q2], [cf Q6], [az Q7].
**Rationale**: An IP-only allowlist can be bypassed by any attacker routing through a Cloudflare account, because the IP ranges are public; Authenticated Origin Pulls closes that gap at no extra cost. The forwarded-header middleware using `CF-Connecting-IP` ensures audit logs and generated URLs carry the real visitor IP (research R-08; decisions #14; [az Q2], [cf Q6], [az Q7]).
**Alternatives**: IP allowlist alone was rejected because the Cloudflare IP ranges are public and any Cloudflare account can be used to reach an IP-only origin [az Q2]. Direct origin exposure without the allowlist was rejected because it would allow cache bypasses and expose the origin to unfiltered traffic.
**Pivot cost**: low: removing Authenticated Origin Pulls and the IP allowlist is a Cloudflare and App Service configuration change with no data migration (decisions #14).

### D-04: Google OIDC, Host-Prefixed Cookie, Atomic Conditional Claim
**Area**: identity-claim
**Status**: given
**Choice**: Google OIDC via the built-in ASP.NET OIDC handler; session stored in a `__Host-` prefixed cookie (Secure, HttpOnly, SameSite=Lax) with a 7-day sliding expiry; ownership established by an atomic conditional write to the datastore as specified in data-model.md; a winning claim emits a `claim` event to Application Insights carrying the Google subject, the client IP from `CF-Connecting-IP`, and the timestamp, which are also stored on the site document; no claim token is used (research R-11; decisions #3; spec FR-018).
**Rationale**: Constitution I.1 requires the first successful Google OIDC sign-in from `/admin` to claim ownership atomically — when two sign-ins race, exactly one becomes owner and the other is refused; Constitution I.2 binds ownership to the Google subject identifier, never an email address; Constitution I.5 requires the claim event to be logged with identity, client IP, and timestamp; Platform Constraints (v3.0.0) specify Google OIDC with a 7-day sliding session renewed while in use (research R-11; decisions #3; spec FR-018).
**Alternatives**: An optional claim token to pre-seed the owner before the first sign-in was noted in Constitution I as a design option for the unclaimed-window risk but was not adopted. No other identity providers were evaluated because the constitution mandates Google OIDC.
**Pivot cost**: low: changing the session duration is a single configuration setting; switching the identity provider requires a constitution amendment and re-provisioning (decisions #3).

### D-05: MongoDB Atlas Free Cluster Co-Located with App Service
**Area**: datastore
**Status**: given
**Choice**: One MongoDB Atlas Free cluster in a Free-eligible Azure region co-located with the App Service; Always On keeps a persistent connection open so the cluster never pauses after 30 days of inactivity (research R-03; [st Q7], [st Q10]).
**Rationale**: Platform Constraints (v3.0.0) specify MongoDB Atlas free tier in the same Azure region as the app; Constitution VIII.1 requires free tiers wherever one covers the need. The Free cluster supports transactions and unique indexes; at 5,000 images the document store occupies about 15 MB, which is 3% of the 0.5 GB quota; the 100 ops/s limit and 10 GB per 7 days of transfer are comfortable at the stated scale (research R-03; [st Q7], [st Q10]).
**Alternatives**: None; MongoDB Atlas Free is a binding Platform Constraint (v3.0.0) with no alternative datastores evaluated in this design.
**Pivot cost**: medium: migrating to a different datastore requires updating the data-access layer, rewriting all queries, and performing a live data migration, though image files in Blob Storage are unaffected.

### D-06: Built-In Antiforgery for Forms and Header Token for JSON Endpoints
**Area**: csrf
**Status**: chosen
**Choice**: ASP.NET Core built-in antiforgery middleware protects all HTML form posts; JSON admin endpoints (presign and complete) require an explicit antiforgery header token validated by the middleware (research R-11; [az Q8]).
**Rationale**: Constitution I.6 requires all admin state-changing requests to be protected against cross-site request forgery. The built-in antiforgery service is automatically enforced for form binding; for JSON endpoints the header token must be required explicitly because minimal APIs do not enforce it automatically for non-form binding ([az Q8]; research R-11).
**Alternatives**: None documented in the research; the built-in ASP.NET antiforgery middleware is the only CSRF mechanism evaluated for the minimal APIs pattern ([az Q8]).
**Pivot cost**: low: CSRF protection is implemented entirely in middleware configuration and does not affect stored data or external service integrations (research R-11).
