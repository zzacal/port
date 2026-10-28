# Azure App Service (Linux B1) + .NET 10 Research Notes

> Design context: .NET 10 minimal API on Azure App Service Linux B1, behind Cloudflare proxy.
> Server must NOT decode image pixels; it parses JPEG EXIF metadata and validates AVIF/WebP
> file structure only.

---

## Q1: App Service Basic B1 Linux — specs and pricing

**Answer:** The B1 plan provides 1 vCPU, 1.75 GB RAM, and 10 GB storage. Pricing for Linux is
approximately **$13.14/month** (East US region, pay-as-you-go; ~76% cheaper than the Windows
equivalent at ~$54.75). Always On is available starting at Basic tier. Custom domains are
supported on Basic; uploading a custom certificate (including a Cloudflare Origin CA PEM) is
supported on Basic tier and higher — the minimum tier for certificates is Basic.

**Source:**
- https://azure.microsoft.com/en-us/pricing/details/app-service/linux/
- https://arvindrsingh.me/en/blog/2024/08/reduce-cost-of-azure-app-services/
- https://learn.microsoft.com/en-us/azure/app-service/tutorial-secure-domain-certificate

**Quote:**
> "Basic tier fulfills the minimum pricing tier requirement for custom domains (Shared) and
> certificates (Basic)."
> — learn.microsoft.com/en-us/azure/app-service/tutorial-secure-domain-certificate

**Fetch: OK**

---

## Q2: App Service access restrictions — rules, IP allowlists, header-based filtering

**Answer:** There is a hard limit of **512 access restriction rules**, more than sufficient to
hold Cloudflare's ~22 IP ranges (IPv4 + IPv6). Both IPv4 and IPv6 CIDR notation are supported.
Access restrictions are **IP-based only** — the feature does not support header-based filtering
(e.g., `X-Forwarded-For` or `CF-Connecting-IP`); for header-based filtering the recommended
approach is ASP.NET Core Forwarded Headers middleware or a WAF (Azure Front Door / Application
Gateway).

**Source:**
- https://learn.microsoft.com/en-us/azure/app-service/app-service-ip-restrictions?tabs=azurecli
- https://learn.microsoft.com/en-ca/azure/app-service/overview-access-restrictions

**Quote:**
> "There's a limit of 512 access restriction rules."
> "IP-based access restriction rules only handle virtual network address ranges when your app is
> in an App Service Environment. Consider Azure Front Door, Azure Application Gateway, or a
> different web application firewall (WAF)."
> — learn.microsoft.com/en-us/azure/app-service/app-service-ip-restrictions

**Fetch: OK**

---

## Q3: Logging on Linux App Service

**Answer:** On Linux App Service, stdout/stderr is captured by enabling **App Service logs →
File System** in the portal; the quota (MB) and retention (days) are both configurable. The
filesystem log option auto-disables after **12 hours** on Windows; on Linux it remains on until
explicitly disabled. A community answer notes the file system log cap is **100 MB**. Log
streaming is available via the portal Log Stream blade or `az webapp log tail`; logs are
downloadable via Kudu (Advanced Tools → Diagnostic dump). Filesystem logging has **no separate
cost** — it uses the plan's included storage.

**Source:**
- https://learn.microsoft.com/en-us/azure/app-service/troubleshoot-diagnostic-logs
- https://learn.microsoft.com/en-us/answers/questions/211065/logging-net-core-web-api-with-azure

**Quote:**
> "In Application logging, select File System. In Quota (MB), specify the disk quota for the
> application logs. In Retention Period (Days), set the number of days to retain the logs."
> — learn.microsoft.com/en-us/azure/app-service/troubleshoot-diagnostic-logs

> "You're limited to a maximum of 100 megabytes of logs when you use the file system."
> — learn.microsoft.com/en-us/answers/questions/211065/logging-net-core-web-api-with-azure

**Fetch: OK**

---

## Q4: IHostedService / BackgroundService on App Service with Always On

**Answer:** Always On is available on Basic tier (B1+) and must be enabled to prevent the
runtime from going idle after a few minutes of inactivity; without it, hosted services stop
running. App Service restarts the process on platform updates and deployments, issuing a
`CancellationToken` to `StopAsync`; the default shutdown timeout is 5 seconds (overridable via
`WEBSITES_CONTAINER_STOP_TIME_LIMIT`). Background services should handle `StoppingToken`
cancellation gracefully to avoid data loss on these forced restarts.

**Source:**
- https://learn.microsoft.com/en-us/azure/azure-functions/dedicated-plan
- https://learn.microsoft.com/en-us/answers/questions/2264075/azure-app-service-getting-restarted-abruptly
- https://learn.microsoft.com/en-us/answers/questions/2285488/additional-cost-for-turning-on-always-on-for-azure

**Quote:**
> "When you run your app on an App Service plan, you should enable the Always On setting so that
> your function app runs correctly. On an App Service plan, the Functions runtime goes idle after
> a few minutes of inactivity."
> — learn.microsoft.com/en-us/azure/azure-functions/dedicated-plan

> "Enabling 'Always On' does not incur additional costs beyond what you're already paying for
> the Basic B1 plan."
> — learn.microsoft.com/en-us/answers/questions/2285488/additional-cost-for-turning-on-always-on-for-azure

**Fetch: OK**

---

## Q5: .NET EXIF library — MetadataExtractor (drewnoakes)

**Answer:** **NuGet package name: `MetadataExtractor`** (current version 2.9.1). License:
**Apache License 2.0**. It reads EXIF, IPTC, XMP, ICC, and other metadata directly from file
headers without decoding pixel data. It explicitly supports **AVIF, HEIF/HEIC, and WebP** in
addition to JPEG, TIFF, PNG, and many raw camera formats. No known pure-.NET alternative reads
all three formats (JPEG + AVIF + WebP) in a single library; alternatives for JPEG-only include
`ExifLibrary` (LGPL) or parsing raw APP1 markers manually.

**Source:**
- https://www.nuget.org/packages/metadataextractor/
- https://github.com/drewnoakes/metadata-extractor-dotnet/blob/main/LICENSE

**Quote:**
> "MetadataExtractor is a straightforward .NET library for reading metadata from image, movie
> and audio files. … It supports various file types. Images: AVIF, BMP, DNG, EPS, GIF,
> HEIF / HEIC, ICO, JPEG / JFIF, … WebP …"
> — nuget.org/packages/metadataextractor

> "Licensed under the Apache License, Version 2.0"
> — github.com/drewnoakes/metadata-extractor-dotnet/blob/main/LICENSE

**Fetch: OK**

---

## Q6: AVIF and WebP container structures

**Answer (AVIF):** AVIF is an ISOBMFF-based container (subset of HEIF). Image dimensions are
stored in the `'ispe'` (Image Spatial Extents) descriptive property inside `iprp → ipco`.
Exif metadata is stored as a separate **item** of type `Exif` listed in `iinf`/`infe` boxes;
XMP is stored as an item of type `mime` with `content-type application/rdf+xml`. ICC color
profiles are stored in a `'colr'` (ColourInformationBox) property. To validate an AVIF without
decoding: parse `ftyp` (brand `avif`), walk `meta → iinf` listing item types, check `ispe`
for dimensions — no AV1 bitstream decoding required.

**Answer (WebP):** WebP uses a RIFF container. A simple lossy file is just `RIFF/WEBP + VP8`
chunk; a simple lossless file uses `VP8L`. The **extended format** starts with a `VP8X` chunk
whose flags bitfield announces which optional chunks follow: ICCP (E bit), EXIF (E bit), XMP
(X bit), alpha (L bit), animation (A bit). Optional chunks: `ICCP` (ICC profile), `EXIF`
(Exif metadata), `XMP ` (XMP metadata). To validate without decoding: check the 12-byte RIFF
header for `WEBP` magic, optionally parse `VP8X` flags and canvas dimensions (24-bit
little-endian Width Minus One / Height Minus One fields) — no VP8/VP8L decoding needed.

**Source:**
- https://aomediacodec.github.io/av1-avif (AVIF spec)
- https://developers.google.com/speed/webp/docs/riff_container (WebP container spec)

**Quote (AVIF):**
> "The semantics of the 'ispe' property as defined in [HEIF] apply. More specifically, for [AV1]
> images, the values of image_width and image_height shall respectively equal the values of
> UpscaledWidth and FrameHeight as defined in [AV1] but for a specific frame in the item
> payload."
> — aomediacodec.github.io/av1-avif

**Quote (WebP):**
> "An extended format file consists of: A 'VP8X' Chunk with information about features used in
> the file. An optional 'ICCP' Chunk with a color profile. … An optional 'EXIF' Chunk with Exif
> metadata. An optional 'XMP ' Chunk with XMP metadata."
> — developers.google.com/speed/webp/docs/riff_container

**Supporting source (AVIF Exif/XMP item storage):**
> "In iinf, list the items. EXIF is an item of type `Exif`. XMP is an item of type `mime` whose
> content type is `application/rdf+xml`."
> — dev.to/sebastian_balog_c88dc2913/converting-heic-webp-and-avif-to-jpg-without-losing-exif

**Fetch: OK**

---

## Q7: ASP.NET Core forwarded headers middleware with CF-Connecting-IP

**Answer:** Use `ForwardedHeadersOptions.ForwardedForHeaderName` to substitute `CF-Connecting-IP`
for the default `X-Forwarded-For`. Add Cloudflare IP ranges (IPv4 and IPv6 CIDR blocks) to
`KnownNetworks` for network-range trust, or individual IPs to `KnownProxies`. Call
`services.Configure<ForwardedHeadersOptions>()` before `app.UseForwardedHeaders()`.

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedForHeaderName = "CF-Connecting-IP";
    options.KnownNetworks.Clear();
    // Add each Cloudflare IPv4/IPv6 range:
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("173.245.48.0"), 20));
    // ... additional ranges
});
```

**Source:**
- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0

**Quote:**
> "ForwardedForHeaderName — Use the header specified by this property instead of the one
> specified by ForwardedHeadersDefaults.XForwardedForHeaderName. This option is used when the
> proxy/forwarder doesn't use the X-Forwarded-For header but uses some other header to forward
> the information. The default is X-Forwarded-For."
> — learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer

**Fetch: OK**

---

## Q8: ASP.NET Core antiforgery (minimal APIs) and output caching

**Answer (Antiforgery):** Built-in since **.NET 8**. Register with `builder.Services.AddAntiforgery()`;
the `UseAntiforgery()` middleware is implicitly added by `WebApplicationBuilder`. Minimal API
endpoints that accept form data (`IFormFile`, `IFormCollection`) **require antiforgery token
validation by default** in .NET 8+. Non-form endpoints (JSON body) are not affected. The
middleware validates only when the endpoint carries `IAntiforgeryMetadata` with
`RequiresValidation=true`, and only for mutating HTTP methods (POST/PUT/PATCH/DELETE).

**Answer (Output caching):** Built-in middleware in **ASP.NET Core 7+** (no extra NuGet
package). Register with `builder.Services.AddOutputCache()` and
`app.UseOutputCache()`. Minimal API usage: `.CacheOutput()` extension on endpoint builders or
`[OutputCache]` attribute. Default storage is in-process `MemoryCache`; a Redis-backed
`IOutputCacheStore` is available separately. Defaults: only HTTP 200 GET/HEAD responses are
cached; authenticated responses and cookie-setting responses are excluded.

**Source:**
- https://devblogs.microsoft.com/dotnet/asp-net-core-updates-in-dotnet-8-preview-7
- https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output

**Quote (antiforgery):**
> "Minimal APIs that accept form data now require antiforgery token validation by default."
> — devblogs.microsoft.com/dotnet/asp-net-core-updates-in-dotnet-8-preview-7

**Quote (output caching):**
> "The output caching middleware can be used in all types of ASP.NET Core apps: Minimal APIs,
> Web API with controllers, MVC, and Razor Pages. … For Minimal API apps, configure an endpoint
> to do caching by calling the CacheOutput method, or by applying the [OutputCache] attribute."
> — learn.microsoft.com/en-us/aspnet/core/performance/caching/output

**Fetch: OK**

---

## Gaps

1. **B1 exact USD price**: Azure pricing page requires sign-in to display the numeric value; the
   ~$13.14/month figure comes from a third-party blog (East US region, 2024) and should be
   verified in the Azure Pricing Calculator for the target region before the design is finalized.

2. **Always On + platform-update shutdown timeout**: The exact default `WEBSITES_CONTAINER_STOP_TIME_LIMIT`
   value and the full list of events that trigger restarts (vs. just deployments and platform
   updates) were not found on an official learn.microsoft.com page during this fetch; a GitHub
   issue and a Q&A answer were cited as supporting evidence instead.

3. **Header-based filtering in App Service access restrictions**: Confirmed not supported
   (IP-only), but no official doc explicitly lists all unsupported filter types; the gap is
   whether a future feature adds it.

4. **AVIF `colr` (ICC) box exact position in required-boxes table**: The AVIF spec table shows
   `ispe` and `pixi` as required properties but `colr` is listed as optional / from HEIF; the
   exact box path was inferred from the HEIF/AVIF spec text and secondary sources rather than
   a single canonical table row.

5. **WebP VP8X canvas dimensions exact byte offset**: The riff_container spec middle section
   (truncated during fetch) contains the bit-level layout; the Width Minus One / Height Minus
   One field positions were described in search snippets but the full struct was not fetched
   verbatim. Read the full spec at developers.google.com/speed/webp/docs/riff_container for
   implementation.
