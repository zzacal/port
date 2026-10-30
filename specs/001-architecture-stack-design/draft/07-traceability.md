## 7. Traceability Matrix

One row per numbered rule in constitution v3.0.0, plus one per Platform Constraints bullet (`PC.n`,
in order). A rule with no numbered list (VI) counts as one rule. Status `deferred:first-build-feature`
marks rules whose mechanism is designed here but whose compliance is a provisioning setting that
can only be checked once the resource exists.

| Rule | Mechanism | Verification | Status |
|---|---|---|---|
| I.1 | D-04: first `/admin` Google sign-in on an unclaimed site claims it with one conditional update; the loser of a race is refused | integration test: two concurrent claims against real Mongo, exactly one wins | designed |
| I.2 | D-04: `claimedSubject` stores the Google `sub`, never an email | integration test: same email with a different `sub` is refused | designed |
| I.3 | D-04: every non-owner identity is refused and receives no session cookie | integration test: second identity gets 403 and no `Set-Cookie` | designed |
| I.4 | D-04: no route writes `site` after the claim; recovery is a datastore operation | integration test: route inventory shows no claim-reset endpoint | designed |
| I.5 | D-04, D-12: claim event logged with `sub`, client IP from `CF-Connecting-IP`, timestamp | log query: claim event present with all three fields | designed |
| I.6 | D-06: built-in antiforgery on forms, required header token on JSON admin endpoints | integration test: admin POST without token is rejected | designed |
| II.1 | D-15: public pages need no account, cookie, or consent | integration test: anonymous GET of every public route returns 200 with no `Set-Cookie` | designed |
| II.2 | D-14, D-16: private galleries absent from home, listings, sitemap; `noindex`; reachable by link; purge on change, 1-day TTL bound | integration test: private gallery absent from listings and carries `noindex`; L-12 | designed |
| II.3 | D-14, D-07: 128-bit base32 ids; content-hash derivative names | integration test: ids are 26-char base32 and differ across titles | designed |
| II.4 | D-14: `/g/{id}/{slug}`, stale slug 301s to current | integration test: renamed gallery old URL returns 301 to new URL | designed |
| II.5 | D-14, D-13: galleries default private; upload and edit never change visibility | integration test: new gallery and confirmed batch leave visibility private | designed |
| III.1 | D-07: masters in the `private` Cool container, no anonymous access, no route serves them | config check: `private` container access level is off; route inventory has no master route | designed |
| III.2 | D-09, D-10: visitors receive only client-generated, server-validated derivatives | integration test: only validated derivatives are copied to `public` on confirm | designed |
| III.3 | D-10: camera allowlist extracted at upload; derivatives carry no metadata | integration test: rendered camera fields equal the allowlist subset | designed |
| III.4 | D-09: `createImageBitmap` applies orientation before encoding | browser measurement: rotated-EXIF fixture produces upright derivatives | designed |
| III.5 | D-10: AVIF and WebP structure parsers plus MetadataExtractor; client values never trusted | integration test: GPS-injected AVIF and WebP fixtures are rejected | designed |
| III.6 | D-10, D-11: any failing derivative rejects the whole image and stores nothing public | integration test: one bad derivative fails the item and leaves `public` untouched | designed |
| III.7 | D-10: allowlist read by the server from the master via ranged download | integration test: client-supplied camera values are ignored | designed |
| III.8 | D-10, D-15: origin parses headers only; no pixel decode or encode | document review: dependency list contains no image codec; D-10 | designed |
| IV.1 | D-12: `http.request` event per request with route, status, duration | log query: every request has route, status, duration | designed |
| IV.2 | D-12, D-09: `op.timing` events for each storage, query, purge op; browser timings posted with `complete` | log query: `op.timing` present for each named op | designed |
| IV.3 | D-12: `Server-Timing` on public pages | markup/header assertion: `Server-Timing` header on public responses | designed |
| IV.4 | D-12: sampling off; saved KQL query per time-bound criterion | log query: saved queries reproduce each time-bound criterion | designed |
| IV.5 | Section 7: each perceived-performance rule has a verification method | markup/header assertion: this matrix lists one for IV.11 to IV.22 | designed |
| IV.6 | D-09, D-15: 320 to 2560 ladder, AVIF and WebP, `<picture>` with `srcset` and `sizes`, no upscaling | markup/header assertion: `<picture>` has AVIF and WebP sources with `srcset` and `sizes` | designed |
| IV.7 | D-09, D-13: ThumbHash data URI and dominant color stored; the data URI is inlined as a stacked image layer (feature 003 R-03) | markup/header assertion: image slots carry the inlined placeholder, no extra request | designed |
| IV.8 | D-13, D-15: width and height stored and emitted | markup/header assertion: every `<img>` has width and height attributes | designed |
| IV.9 | D-13: denormalized `homeCards` on the site document | markup/header assertion: home page issues one datastore read (`op.timing` count) | designed |
| IV.10 | D-16: purge then warm on publish or edit; Tiered Cache on | log query: purge and warm events follow each public change | designed |
| IV.11 | D-17: inline Speculation Rules, prerender at moderate eagerness | markup/header assertion: public pages contain the `speculationrules` script | designed |
| IV.12 | D-17: hover and touch preload script | browser measurement: hover on a thumbnail starts the destination image fetch | designed |
| IV.13 | D-17: solo view prefetches previous and next after load | browser measurement: previous and next fetched after `load` | designed |
| IV.14 | D-17: Save-Data guard in scripts; Chromium suppresses speculation | browser measurement: with Save-Data on, no speculative fetches occur | designed |
| IV.15 | D-15, D-17: solo view paints cached thumbnail full size, sharp image revealed on decode | browser measurement: first paint of solo view shows the thumbnail | designed |
| IV.16 | D-17: `@view-transition` and `view-transition-name: img-{id}` | markup/header assertion: both pages declare the transition and matching names | designed |
| IV.17 | D-17: primary image `fetchpriority=high`, others `loading=lazy decoding=async` | markup/header assertion: attribute assertions on rendered pages | designed |
| IV.18 | D-16: HTML edge-cached 1 day, bypassed with session cookie, purged on change | markup/header assertion: anonymous response is cacheable; owner response bypasses | designed |
| IV.19 | D-17, D-16: origin sends `Link: rel=preload`; edge emits 103 | markup/header assertion: `Link` preload headers on public pages | designed |
| IV.20 | D-17: no `unload` handlers, no `no-store` on anonymous responses | markup/header assertion: no `no-store` header and no `unload` in served script | designed |
| IV.21 | D-07, D-15: content-hash names with one-year immutable caching; Always On; output caching | markup/header assertion: derivative `Cache-Control` is `public, max-age=31536000, immutable` | designed |
| IV.22 | D-15: no client framework, inlined critical CSS, system fonts | markup/header assertion: no framework bundle or font request in public HTML | designed |
| V.1 | Section 10 and spec scope: no comments, likes, search, multi-user, analytics | document review: every capability traces to spec 001 | designed |
| V.2 | research.md dependency table justifies every package | document review: package list matches the justification table | designed |
| V.3 | D-15: public pages work without JavaScript; admin may require it | integration test: public routes render complete HTML with no script execution | designed |
| VI.1 | Section 10: scaffolding with `dotnet new`, `az`, `wrangler` | document review: no scaffolding happens in this feature | n/a:no scaffolding in a documents-only feature |
| VII.1 | This document is feature 001, documents only | document review: feature 001 adds no source or infrastructure | n/a:satisfied by this feature itself |
| VII.2 | Section 10 and the document header: later deviations must update this document | document review: later plans cite this document in their Constitution Check | designed |
| VII.3 | Section 2: every decision has rationale, alternatives, pivot cost | document review: validate.sh C2 counts all four fields | designed |
| VIII.1 | Section 8, D-18: only B1 fixed; metered total about $1 against $10 cap and $7 budget alert | config check: Azure budget with $7 and $10 notifications exists | designed |
| VIII.2 | D-02: Universal SSL at the edge, Origin CA at the origin | config check: origin certificate issuer is Cloudflare Origin CA | designed |
| VIII.3 | Section 8: every metered dimension with free limit, unit price, projection | document review: validate.sh C7 | designed |
| VIII.4 | Section 8, D-18, D-12: Application Insights 150 MB daily cap | config check: daily cap set on the workspace | designed |
| PC.1 | D-01, D-15: .NET 10 minimal APIs, server-rendered HTML, htmx and web components for admin | document review: D-01 and D-15 | deferred:first-build-feature |
| PC.2 | D-01, D-03: App Service Linux B1; IP allowlist plus Authenticated Origin Pulls | config check: access restrictions list only Cloudflare ranges | deferred:first-build-feature |
| PC.3 | D-02: Cloudflare proxy, cache, Full (strict) TLS | config check: zone SSL mode is Full (strict) | deferred:first-build-feature |
| PC.4 | D-04: Google OIDC, `__Host-` cookie with 7-day sliding expiry | integration test: session cookie attributes and expiry | designed |
| PC.5 | D-05: Atlas Free in a Free-eligible Azure region next to the app | config check: cluster tier and region | deferred:first-build-feature |
| PC.6 | D-07, D-08: GPv2 LRS, `public` Hot and `private` Cool, managed identity, SAS uploads | config check: container tiers and access levels; no account key in app settings | deferred:first-build-feature |
| PC.7 | D-16, D-19: `img.<domain>` through Cloud Connector, Worker fallback | config check: image hostname route; markup assertion that no raw Blob URL is emitted | deferred:first-build-feature |
| PC.8 | D-12: workspace-based Application Insights, 30-day retention, daily cap | config check: retention and daily cap settings | deferred:first-build-feature |
| PC.9 | D-03: forwarded headers from `CF-Connecting-IP`, Cloudflare ranges as known networks | integration test: logged client IP equals `CF-Connecting-IP` | designed |
