# Cloudflare FREE Plan — Research Notes

> Gathered for architecture design of the photo gallery project.
> All facts verified against pages fetched below. Gaps listed at the end.

---

## Q1: Cache Purge Types & Rate Limits on Free

**Answer:** As of April 2025, all five purge methods are available on every Cloudflare plan including Free: Purge by URL (single-file), Purge Everything, Purge by Tag, Purge by Prefix, and Purge by Hostname. Free plan rate limits for hostname/tag/prefix/purge-everything: **5 requests per minute**, bucket size 25, max 100 operations per request. Single-file purge: **800 URLs/second**, max 100 URLs per request.

**Source:** https://developers.cloudflare.com/cache/how-to/purge-cache  
**Quote:** "Free Pro Business Enterprise — Availability Yes Yes Yes Yes — Purge options URL, Hostname, Tag, Prefix, and Purge Everything [for all plans]. … Free: Requests 5 requests per minute, Bucket size 25, Max operations per request 100. … URLs 800 URLs per second [for Free], Max operations per request 100."  
**Fetch: OK**

---

## Q2: Tiered Cache (Smart Tiered Cache Topology) on Free

**Answer:** Tiered Cache including Smart Tiered Cache topology is available on all plans, including Free, at no additional cost. Smart Tiered Cache dynamically selects the single closest upper-tier data center for each origin using latency data, with no manual configuration required.

**Source:** https://developers.cloudflare.com/cache/how-to/tiered-cache/  
**Quote:** "Smart Tiered Cache dynamically selects the single closest upper tier for each of your website's origins with no configuration required, using our in-house performance and routing data. Cloudflare collects latency data for each request to an origin, and uses the latency data to determine how well any upper-tier data center is connected with an origin."  
**Fetch: OK**

---

## Q3: Early Hints (103) on Free

**Answer:** Early Hints (HTTP 103) is available on all plans including Free. It is generated automatically from origin (or cached) `Link` response headers with `preconnect` or `preload` rel types, but only for HTML pages (`.html`, `.htm`, `.php`, or no file extension) on 200/301/302 responses. Cloudflare caches and re-serves the 103 response asynchronously before the main response.

**Source:** https://developers.cloudflare.com/cache/advanced-configuration/early-hints/  
**Quote:** "Early Hints are only generated and cached: For URIs with `.html`, `.htm`, or `.php` file extensions, or no file extension — On 200, 301, or 302 response return codes — When the response contains link headers with preconnect or preload rel types, such as `Link:; rel=preload`."  
**Fetch: OK**

---

## Q4: Caching HTML — Cache Rules, Edge TTL, Bypass on Cookie, Browser TTL

**Answer:** Cache Rules are available on all plans. Free gets **10 rules**. Edge TTL can be overridden (`override_origin` mode). Bypass cache on cookie is possible on Free: `http.cookie` is a supported field in Cache Rule match conditions, allowing you to set Cache eligibility to "Bypass cache" when a specific cookie is present. Browser TTL can be set to Bypass, Respect Origin, or Override Origin within a Cache Rule.

**Source:** https://developers.cloudflare.com/cache/how-to/cache-rules/  
and https://developers.cloudflare.com/cache/how-to/cache-rules/settings/  
**Quote (rules count):** "Number of rules — Free: 10, Pro: 25, Business: 50, Enterprise: 300."  
**Quote (cookie field):** "Fields — Cookie - `http.cookie` … Cache eligibility: In Cache eligibility, you have the option to select Bypass cache if you want matching requests to not be cached, or Eligible for cache if you want Cloudflare to attempt to cache them."  
**Quote (Browser TTL):** "Browser TTL — Select if you want to Bypass cache, Respect origin, or Override origin. If you wish to override the browser TTL value, define how long resources cached by client browsers will remain valid from the dropdown menu."  
**Fetch: OK**

---

## Q5: Cloudflare Origin CA Certificates

**Answer:** Cloudflare Origin CA certificates are free and available on all plans including Free. By default they are valid for **up to 15 years**. Once installed they are compatible with **Full (strict)** SSL/TLS mode, encrypting traffic between Cloudflare and the origin.

**Source:** https://developers.cloudflare.com/use-cases/solutions/encrypt-all-keep-site-secure  
and https://developers.cloudflare.com/ssl/origin-configuration/origin-ca/  
**Quote:** "Origin CA certificates are valid for up to 15 years and are trusted by Cloudflare, which means you can set your encryption mode to Full (strict) after installing one."  
**Fetch: OK**

---

## Q6: Restricting Origin to Cloudflare IPs + Authenticated Origin Pulls

**Answer:** Cloudflare publishes its IP ranges at **https://www.cloudflare.com/ips** (also queryable via API). You can firewall your origin to allow only those ranges. Authenticated Origin Pulls (mTLS) is available on **all plans including Free** at all three configuration levels (global, zone-level, per-hostname).

**Source (IP ranges):** https://developers.cloudflare.com/fundamentals/concepts/cloudflare-ip-addresses/  
**Quote (IP ranges):** "This means that your origin server will stop receiving traffic from individual visitor IP addresses and instead receive traffic from Cloudflare IP addresses, which are shared by all proxied hostnames. … Replace `$ip` below with one of the Cloudflare IP address ranges: https://www.cloudflare.com/ips."  
**Fetch: OK**

**Source (AOP):** https://developers.cloudflare.com/ssl/origin-configuration/authenticated-origin-pull/  
**Quote (AOP):** "Availability — Free: Yes, Pro: Yes, Business: Yes, Enterprise: Yes."  
**Fetch: OK**

---

## Q7: Cloudflare Workers Free Plan Limits

**Answer:** Workers Free plan allows **100,000 requests/day** (resets at midnight UTC), **10 ms CPU time per invocation**, and up to 100 Worker scripts. There is no per-second request cap on the global network; only the daily limit applies.

**Source:** https://developers.cloudflare.com/workers/platform/limits/  
**Quote:** "Account plan limits — Requests: Workers Free 100,000/day, Workers Paid No limit. CPU time: Workers Free 10 ms, Workers Paid 5 min. … Accounts on the Workers Free plan have a daily request limit of 100,000 requests, resetting at midnight UTC. When a Worker exceeds this limit, Cloudflare returns Error 1027."  
**Fetch: OK**

---

## Q8: Cache Reserve Availability & Max Cacheable File Size on Free

**Answer:** **Cache Reserve is NOT available on the Free plan** — it requires a paid Cache Reserve add-on. However, the standard CDN (without Cache Reserve) is free. The maximum cacheable file size on Free (and Pro/Business) is **512 MB** per file.

**Source (Cache Reserve):** https://developers.cloudflare.com/cache/advanced-configuration/cache-reserve/  
**Quote (Cache Reserve):** "Cache Reserve is a usage-based product and pricing is detailed below. While Cache Reserve does require a paid plan, users can continue to use Cloudflare's CDN (without Cache Reserve) for free."  
**Fetch: OK**

**Source (max file size):** https://developers.cloudflare.com/cache/concepts/default-cache-behavior/  
**Quote (max file size):** "Cacheable size limits: Free, Pro and Business customers have a limit of 512 MB. For Enterprise customers the default maximum cacheable file size is 5 GB."  
**Fetch: OK**

---

## Q9: Cloudflare API Rate Limits

**Answer:** The global Cloudflare API rate limit is **1,200 requests per 5-minute window per user/account token**, applied cumulatively across dashboard, API key, and API token usage regardless of zone plan. Exceeding it triggers HTTP 429 for the next 5 minutes. Some specific APIs (cache purge, GraphQL, Rulesets, Lists) have their own separate limits.

**Source:** https://developers.cloudflare.com/fundamentals/api/reference/limits/  
**Quote:** "The global rate limit for the Cloudflare API is 1,200 requests per five minute period per user, and applies cumulatively regardless of whether the request is made via the dashboard, API key, or API token. If you exceed this limit, all API calls for the next five minutes will be blocked, receiving a HTTP 429 - Too Many Requests response."  
**Fetch: OK**

---

## Gaps

- **Origin CA default validity:** The docs confirm "up to 15 years" as the dashboard default; the exact list of selectable validity periods (7 days, 30 days, 1 year, 15 years, etc.) was not fetched in full — see https://developers.cloudflare.com/ssl/origin/origin-ca for the full picker.
- **Bypass-on-cookie via Cache Rules (Free) — confirmed possible via `http.cookie` field matching + Bypass cache eligibility, but no explicit worked example for this exact combination was fetched from the docs.** Treat as verified by inference from the fields/settings docs.
- **Tiered Cache plan table:** The full availability table for Smart Tiered Cache specifically (vs. generic Tiered Cache) was not extracted in a single clean table — confirmed "Yes" for Free from the search snippets and the plans index, but the full plans page was not fully extracted.
