# Web Platform Perceived-Performance Research Notes

_Context: server-rendered photo gallery; public pages must work without JavaScript._

---

## Q1: Speculation Rules API

**Answer:** The Speculation Rules API is supported in Chrome/Edge (since version 109 for prerender; 103 for prefetch), but **not** in Firefox or Safari (marked "Limited availability" / "not Baseline" on MDN as of 2025). The `eagerness` values are `immediate` (default for `list` rules), `eager`, `moderate` (triggers on 200 ms hover / `pointerdown`), and `conservative` (default for `document` rules). `prefetch` fetches the response body only; `prerender` fully renders the page in a hidden tab for near-instant activation. Rules can be delivered either via an inline `<script type="speculationrules">` element (in `<head>` or `<body>`) or via a `Speculation-Rules` HTTP header that points to an external JSON file (MIME type `application/speculationrules+json`). The browser automatically suppresses speculation when the user has **Save-Data** or **Battery Saver** active.

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/API/Speculation_Rules_API
- https://developer.chrome.com/docs/web-platform/prerender-pages

**Quote:**
> "Is respectful of the user's configuration. For example, prefetching doesn't happen when the user's device is in Battery Saver or Data Saver mode."
> — MDN, Speculation Rules API

> "The `moderate` option is a middle ground, and many sites could benefit from the following speculation rule that would prerender a link when holding the pointer over the link for 200 milliseconds or on the pointerdown event as a basic—yet powerful—implementation of speculation rules."
> — developer.chrome.com, Prerender pages in Chrome

**Fetch: OK**

---

## Q2: Cross-Document View Transitions

**Answer:** Cross-document view transitions (`@view-transition { navigation: auto; }`) require both the outgoing and incoming page to opt in via that CSS at-rule. They are supported in **Chrome/Edge 126+** (June 2024) and **Safari 18.2+** (December 2024); Firefox ships **same-document** `startViewTransition()` in version 144 but cross-document support is not yet shipped (planned via Interop 2026). Elements tagged with a matching `view-transition-name` on both pages form named pairs; the browser generates `::view-transition-old(name)` / `::view-transition-new(name)` pseudo-elements and cross-fades/morphs them. In unsupported browsers, the `@view-transition` at-rule is silently ignored and a normal navigation occurs — no broken state, no polyfill needed.

**Source:**
- https://developer.chrome.com/docs/web-platform/view-transitions/cross-document
- https://blogs.abhipanseriya.dev/blog/view-transitions-went-cross-browser-the-spa-lost-its-best-excuse
- https://botmonster.com/web-dev/cross-document-view-transitions-mpa-page-navigation

**Quote:**
> "Cross-document view transitions are opt-in, with an invisible fallback. Browsers that don't support the API just do a normal full-page nav. Nothing breaks. Users simply don't see the animation. No polyfill or JS shim is needed."
> — botmonster.com

> "By setting navigation descriptor to auto you are opting in to allowing view transitions to happen for the following NavigationTypes: traverse push or replace, if the activation was not initiated by the user through browser UI mechanisms."
> — developer.chrome.com, Cross-document view transitions

**Fetch: OK**

---

## Q3: Back/Forward Cache (bfcache) Eligibility

**Answer:** The two main blockers for bfcache are `unload` event handlers and `Cache-Control: no-store`. Chrome has deprecated `unload` events (they no longer fire in Chrome desktop except where opt-back-in); Firefox still makes pages ineligible if they register an `unload` listener. For `no-store`, Chrome completed a rollout (March–April 2025) to allow `no-store` pages in bfcache **unless** cookies change, WebSocket/WebTransport/WebRTC are used, or sub-requests themselves return `no-store`; the bfcache timeout is also reduced to 3 minutes for such pages (vs. 10 minutes normally). Other browsers may still block bfcache on `no-store`, so best practice remains to avoid `no-store` unless the page genuinely contains sensitive data; use `no-cache` or `max-age=0` instead.

**Source:**
- https://web.dev/articles/bfcache
- https://developer.chrome.com/docs/web-platform/bfcache-ccns
- https://developer.chrome.com/docs/devtools/application/back-forward-cache

**Quote:**
> "The two main reasons that prevent bfcache from running are: `Cache-Control: no-store` header. This header value is recommended for pages with personal and private information. For pages you want to be reasonably fresh, use `no-cache` or a short cache time, for example, `max-age=60`. Problematic unload handlers."
> — developer.chrome.com, Test back/forward cache

> "Chrome is making a change to allow back/forward cache (bfcache) usage for pages using `Cache-Control: no-store` when this is safe to do... We are completing the final rollout to 100% of users over March and April 2025."
> — developer.chrome.com, Enabling bfcache for Cache-Control: no-store

**Fetch: OK**

---

## Q4: Save-Data Client Hint

**Answer:** The `Save-Data: on` request header is a low-entropy client hint sent by Chrome 49+ (and Chromium-based browsers: Edge, Opera) when the user has enabled a data-saving mode. MDN marks it "Limited availability — this feature is not Baseline because it does not work in some of the most widely-used browsers." Firefox and Safari do **not** send this header. The JS API `navigator.connection.saveData` (part of the Network Information API) similarly only works in Chromium-based browsers. Because it is low-entropy, the browser can send it without the server first advertising `Accept-CH`.

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Save-Data
- https://web.dev/articles/optimizing-content-efficiency-save-data
- https://http.dev/save-data

**Quote:**
> "The `Save-Data` client hint request header available in Chrome, Opera, and Yandex browsers lets developers deliver lighter, faster applications to users who opt-in to data saving mode in their browser."
> — web.dev, Delivering Fast and Light Applications with Save-Data

> "Note that this feature is no longer supported by default in most major browser engines."
> — MDN, Save-Data header

**Fetch: OK**

---

## Q5: `fetchpriority`, `loading=lazy`, and `decoding=async`

**Answer:** `fetchpriority` reached **Baseline (October 2024)** — all major browsers (Chrome, Edge, Firefox, Safari) support it. It accepts `high`, `low`, or `auto` and helps the browser prioritize resource fetches, e.g. `fetchpriority="high"` on a hero `<img>`. `loading="lazy"` for images/iframes is also widely supported (Baseline). `decoding="async"` is described by MDN as "well established and works across many devices and browser versions." Combining `loading="lazy" decoding="async"` on below-the-fold images and `fetchpriority="high" decoding="async"` on the LCP image is best practice.

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Attributes/fetchpriority
- https://developer.mozilla.org/en-US/docs/Web/API/HTMLImageElement/decoding
- https://developer.mozilla.org/en-US/docs/Web/Performance/Guides/Lazy_loading

**Quote:**
> "`fetchpriority` HTML attribute — Since October 2024, this feature works across the latest devices and browser versions."
> — MDN, fetchpriority HTML attribute

> "HTMLImageElement: decoding property — This feature is well established and works across many devices and browser versions."
> — MDN, HTMLImageElement decoding property

**Fetch: OK**

---

## Q6: `<picture>` with AVIF + WebP Sources

**Answer:** AVIF is now widely supported: **Chrome 85+**, **Firefox 93+**, **Edge 121+**, and **Safari 16.0+** on iOS (full support in Safari 16.4+ on macOS/iOS; partial support 16.1–16.3). WebP is universally supported (Safari 14+, all others). The correct pattern is `<picture><source type="image/avif" srcset="…"><source type="image/webp" srcset="…"><img src="…" alt="…"></picture>`; the browser picks the first source type it supports. `srcset` and `sizes` attributes are Baseline and work in all major browsers.

**Source:**
- https://caniuse.com/avif
- https://caniuse.com/srcset (referenced via search)
- https://publishing-project.rivendellweb.net/exploring-the-picture-element/

**Quote:**
> "Safari: ❌ 3.1 - 16.0: Not supported; ◐ 16.1 - 16.3: Partial support; ✅ 16.4 - 26.6: Supported"
> — caniuse.com, AVIF image format

> "Safari on iOS: ✅ 16.0 - 16.3: Supported; ✅ 16.4 - 26.6: Supported"
> — caniuse.com, AVIF image format

**Fetch: OK**

---

## Q7: `Server-Timing` Header

**Answer:** The `Server-Timing` response header exposes server-side metrics in browser DevTools (Network panel) and programmatically via `PerformanceResourceTiming.serverTiming` (returns an array of `PerformanceServerTiming` entries). Access is restricted to the **same origin** by default; cross-origin resources must include `Timing-Allow-Origin: <origin>` (or `*`) to expose their timing data to JS. The API requires a secure context (HTTPS) in some browsers. `Server-Timing` is supported in all major browsers (Chrome, Firefox, Safari, Edge).

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/API/PerformanceResourceTiming/serverTiming
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Server-Timing
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Timing-Allow-Origin

**Quote:**
> "Access to server timing information is restricted to the same origin. To expose cross-origin timing information, the `Timing-Allow-Origin` HTTP response header needs to be set."
> — MDN, PerformanceResourceTiming: serverTiming property

> "In addition to having `Server-Timing` header metrics appear in the developer tools of the browser, the `PerformanceServerTiming` interface enables tools to automatically collect and process metrics from JavaScript."
> — MDN, Server-Timing header

**Fetch: OK**

---

## Q8: 103 Early Hints

**Answer:** 103 Early Hints are supported in **Chrome/Edge 103+** (July 2022), **Firefox 120+** for preconnect and **123+** for preload, and **Safari 17+** for `rel=preconnect` only (preload is not reliably supported in Safari). Only `rel=preload` and `rel=preconnect` are honoured in 103 responses — `rel=prefetch`, `rel=dns-prefetch`, and `rel=modulepreload` are not supported. Early Hints only fire for top-level navigation requests over HTTP/2 or HTTP/3; they are ignored for sub-resource requests, iframes, and HTTP/1.1.

**Source:**
- https://webspecification.com/blog/103-early-hints
- https://corewebvitals.io/pagespeed/103-early-hints
- https://csmbac.com/topics/http-103-early-hints

**Quote:**
> "Chrome and Edge 103+: full support for preconnect and preload (since June 2022). Firefox 123+: full support for preconnect and preload (since February 2024). Safari 17+: preconnect only. Safari does not support preload in 103 responses."
> — corewebvitals.io, 103 Early Hints

> "Early Hints only work over HTTP/2 or HTTP/3. They do not work over HTTP/1.1, from iframes, or for non-navigation requests. The browser processes hints for `preload` and `preconnect` only; `dns-prefetch` and `prefetch` are not supported in 103 responses."
> — corewebvitals.io, 103 Early Hints

**Fetch: OK**

---

## Q9: `Cache-Control: public, max-age=31536000, immutable`

**Answer:** The `immutable` directive (RFC 8246) tells the browser not to revalidate a cached resource while it is fresh — preventing unnecessary conditional requests (e.g., on reload). MDN's Cache-Control reference is "Baseline Widely available" and the page explicitly demonstrates `Cache-Control: max-age=31536000, immutable` as the canonical cache-busting pattern for content-addressed static assets. `immutable` is supported in Firefox 49+ (the first browser to ship it), Chrome 70+, Safari 11.1+, and Edge (Chromium). Unknown/ignoring browsers simply treat it as an unrecognised directive and fall back to `max-age` behaviour — safe graceful degradation.

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cache-Control
- https://httpwg.org/specs/rfc8246.html (cited by MDN)

**Quote:**
> "The `immutable` response directive indicates that the response will not be updated while it's fresh."
> — MDN, Cache-Control header

> "When a user reloads the browser, the browser will send conditional requests for validating to the origin server. But it's not necessary to revalidate those kinds of static resources even when a user reloads the browser, because they're never modified. `immutable` tells a cache that the response is immutable while it's fresh and avoids those kinds of unnecessary conditional requests to the server."
> — MDN, Cache-Control header

**Fetch: OK**

---

## Gaps

- **Speculation Rules + Save-Data on non-Chrome browsers:** The Save-Data / battery-saver respect is documented for Chrome; the same behaviour cannot be verified for Safari or Firefox since they do not implement Speculation Rules at all.
- **`immutable` exact version numbers per browser:** The MDN compatibility table section for `Cache-Control: immutable` specifically was truncated in the fetched page; version numbers (Firefox 49, Chrome 70, Safari 11.1) are from known published data but were not independently fetched from a caniuse or MDN compat table in this session.
- **Safari 103 Early Hints preload status:** Multiple sources say Safari 17 supports preconnect but not preload in 103 responses; one source notes "historically lagged." The exact Safari version where/if preload lands has not been confirmed with a current MDN compat table fetch.
- **`navigator.connection.saveData` full compat table:** Only confirmed for Chromium; no fetched page showed a full compat matrix for the Network Information API `saveData` property.
- **Firefox cross-document view transitions ship date:** Firefox 144 is cited as shipping "partial" support in some sources and "not shipped" in others; a direct MDN compat table fetch was not completed for this specific feature.
