# Azure Blob Storage + Application Insights Research Notes

_Context: Single-owner photo gallery on Azure App Service Linux B1, Cloudflare FREE plan (proxied, Full strict TLS). Azure Blob Storage for public resized derivatives (Hot, ~11 GB) and private JPEG masters (Cool/Cold, ~60–85 GB). Browser-direct uploads via SAS. Logs to Application Insights, 30-day retention._

---

## Q1: Azure Blob Storage pricing (LRS, East US 2)

**Answer:** For Standard GPv2 LRS in East US (Zone 1), storage costs per GB-month are approximately: **Hot ~$0.0184**, **Cool ~$0.01**, **Cold ~$0.0045**. Write operations (per 10,000): Hot $0.065, Cool $0.13, Cold $0.234. Read operations (per 10,000): Hot ~$0.0044, Cool ~$0.01, Cold ~$0.10 (from AzCopy cost-estimation docs; the read-ops column on the pricing page is JS-rendered and did not return raw values). Cool tier has a **30-day minimum retention**; Cold tier has a **90-day minimum**. There is **no free monthly storage allowance** on Blob Storage.

**Source:**
- https://azure.microsoft.com/en-us/pricing/details/storage/blobs/
- https://learn.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview
- https://learn.microsoft.com/en-us/azure/storage/blobs/azcopy-cost-estimation

**Quote (from `access-tiers-overview`):**
> "Data in the cool tier should be stored for a minimum of 30 days. The cold tier should be stored for a minimum of 90 days. Blobs are subject to an early deletion penalty if they are deleted, overwritten or moved to a different tier before the minimum number of days required by the tier have transpired."

**Quote (from `azure.microsoft.com/pricing/details/storage/blobs/` — partial extraction):**
> "Any blob that is moved to the Cool tier is subject to a Cool tier early deletion period of 30 days. This charge is prorated based on the data storage price of the corresponding tier, i.e. deleting an archived blob after 120 days will lead to this object being charged for 180 days."

**Fetch: OK** _(Note: Per-GB storage prices for Hot/Cool/Cold read from search-snippet rendering of the pricing page; the full table requires JavaScript. Values: Hot $0.0184/GB, Cool $0.01/GB, Cold $0.0045/GB cited in the pricing page description snippet. Treat as indicative; verify in Azure Pricing Calculator for East US 2 LRS.)_

---

## Q2: Azure outbound data transfer (internet egress) pricing

**Answer:** The **first 100 GB/month of internet egress from any Azure region is free**. From North America/Europe, the next 10 TB/month costs $0.087/GB (Microsoft Premium Global Network routing) or $0.08/GB (ISP transit routing). Data inbound to Azure is always free. This free 100 GB applies to Blob Storage egress to the internet (including to Cloudflare's edge). When Cloudflare caches blobs, most origin-pull traffic stays within that 100 GB free tier for a small gallery.

**Source:**
- https://azure.microsoft.com/en-us/pricing/details/bandwidth/

**Quote:**
> "Internet Egress (routed via Microsoft Premium Global Network) | Source Continent: From North America, Europe to any destination | First 100 GB/Month: Free | Next 10 TB/Month: $0.087 per GB"

**Fetch: OK**

---

## Q3: Anonymous public read access on a container

**Answer:** Anonymous access requires two settings: (1) the **storage account** must have `AllowBlobPublicAccess = true` (account-level), and (2) the **container** must have its access level set to `Blob` (allows anonymous read for blobs only, not listing) or `Container` (allows listing too). **By default on new storage accounts, `AllowBlobPublicAccess` is set to `false`** (disabled), meaning no container-level anonymous access is possible until it is explicitly enabled. The portal exposes this as a toggle; ARM templates use `"allowBlobPublicAccess": true`.

**Source:**
- https://learn.microsoft.com/en-us/azure/storage/blobs/anonymous-read-access-configure

**Quote:**
> "Anonymous access to your data is always prohibited by default. Two settings affect anonymous access... When you configure a container's access level setting to permit anonymous access, clients can read data in that container without authorizing the request."

**Quote:**
> "Unless you explicitly enable anonymous access, all requests to a container and its blobs must be authorized."

**Fetch: OK**

---

## Q4: Serving a blob container through Cloudflare FREE plan on a custom domain (HTTPS)

**Answer:** The core challenge is that Azure Blob Storage rejects requests where the `Host` header doesn't match the account's `.blob.core.windows.net` endpoint or a registered custom domain on the account. Cloudflare's **Origin Rules "Host Header override"** is **Enterprise-only** (not available on Free, Pro, or Business plans). Three documented alternatives exist:

1. **Cloudflare Cloud Connector** (beta, all plans including Free): Routes traffic for a custom hostname to an Azure Blob bucket URL directly. Handles the Host header issue transparently. Announced August 2024, rolling out gradually. Configure under _Rules → Cloud Connector_ in the zone dashboard.
2. **Register a custom domain on the storage account** (CNAME + asverify): Azure supports mapping a custom subdomain (e.g. `img.example.com`) via a CNAME to the blob endpoint. However, enabling HTTPS on that custom domain via Azure requires Azure CDN — the native custom domain feature does **not** support TLS on its own. Cloudflare's proxy can terminate TLS for visitors, but Full (strict) mode requires a valid certificate on the origin; the raw `*.blob.core.windows.net` origin already has a valid cert so Full (strict) can work if Cloudflare sends the Host header as the blob endpoint hostname — which requires Cloud Connector or an Enterprise Origin Rule to override.
3. **Cloudflare Worker** (free tier: 100,000 requests/day): Fetch from the blob URL server-side in the Worker, rewriting the Host header. Circumvents the origin-rules limitation at no cost for moderate traffic.

Azure Front Door / Azure CDN can also front a blob container with custom domain + HTTPS at additional cost.

**Source:**
- https://developers.cloudflare.com/rules/origin-rules/ (availability table)
- https://blog.cloudflare.com/cloud-connector/ (Cloud Connector announcement)
- https://developers.cloudflare.com/rules/cloud-connector/providers/

**Quote (Origin Rules availability — from `developers.cloudflare.com/rules/origin-rules/`):**
> "| Override Host header | No | No | No | Yes |" _(columns: Free, Pro, Business, Enterprise)_

**Quote (Cloud Connector — from `blog.cloudflare.com/cloud-connector/`):**
> "Cloud Connector is available in beta to all plans and is completely free... Before Cloud Connector, many of our Free, Pro, and Business customers faced a significant challenge: it was not straightforward to route traffic for the same hostname to one or more cloud providers on the backend."

**Quote (Cloud Connector Azure support — from `developers.cloudflare.com/rules/cloud-connector/providers/`):**
> "The hostname of your Blob Storage bucket URL must have one of the following formats: `<BUCKET_NAME>.blob.core.windows.net` or `<BUCKET_NAME>.web.core.windows.net`."

**Fetch: OK** _(Note: Cloud Connector was in gradual rollout as of Aug 2024; verify it is available in your zone's dashboard. The asverify CNAME + Azure custom domain registration route does not provide TLS without Azure CDN.)_

---

## Q5: SAS tokens for browser PUT uploads, CORS, and upload limits

**Answer:** **Put Blob** (single-request upload) supports a maximum of **5,000 MiB (≈4.88 GiB)** per request as of service version 2019-12-12 and later. For larger files, use **Put Block** (each block up to 4,000 MiB) + **Put Block List** to commit; max total blob size is ~190.7 TiB. **User Delegation SAS** (signed with Entra/AAD credentials) is preferred for browser uploads — it expires within a maximum of **7 days** (hard limit tied to the user delegation key lifetime). Service SAS (signed with account key) has no hard API expiry but best practice is short-lived (minutes to hours). CORS must be configured on the Blob service (not at the container level): set `AllowedOrigins`, `AllowedMethods` (include `PUT`, `OPTIONS`, `HEAD`), `AllowedHeaders` (at minimum `Content-Type`, `x-ms-blob-type`, `Authorization`), and `MaxAgeInSeconds`.

**Source:**
- https://learn.microsoft.com/en-us/rest/api/storageservices/put-blob
- https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-user-delegation-sas-create-cli
- https://learn.microsoft.com/en-us/azure/storage/common/sas-expiration-policy
- https://learn.microsoft.com/en-us/azure/storage/blobs/quickstart-blobs-javascript-browser

**Quote (Put Blob limits — from `rest/api/storageservices/put-blob`):**
> "Version 2019-12-12 and later | Maximum block size (via Put Block): 4,000 mebibytes (MiB) | Maximum blob size (via Put Block List): Approximately 190.7 TiB | Maximum blob size via single write operation (via Put Blob): 5,000 MiB"

**Quote (User Delegation SAS expiry — from `sas-expiration-policy`):**
> "A user delegation SAS has a maximum expiry interval of 7 days, regardless of the SAS expiration policy. If the SAS expiration policy is set to a value greater than 7 days, then the policy has no effect for a user delegation SAS."

**Quote (CORS — from `quickstart-blobs-javascript-browser`):**
> "Before your web application can access blob storage from the client, you must configure your account to enable cross-origin resource sharing, or CORS."

**Fetch: OK**

---

## Q6: Blob lifecycle management policies

**Answer:** Azure Blob Storage lifecycle management is supported on **General-Purpose v2 (GPv2)**, premium block blob, and legacy Blob Storage accounts. Policies are JSON rules that can filter by name **prefix** and/or blob index tags, then delete (or tier-transition) blobs based on creation time, last-modified time, or last-accessed time. Policies can delete by prefix after N days since creation. The **lifecycle management feature itself is free**; only the resulting tier-change write operations and standard delete operations (free) are billed. Lifecycle policies run periodically (typically once per day).

**Source:**
- https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview
- https://learn.microsoft.com/en-us/azure/storage/blobs/azcopy-cost-estimation (billing context)

**Quote (from `lifecycle-management-overview`):**
> "Lifecycle management policies are supported for block blobs and append blobs in general-purpose v2, premium block blob, and Blob Storage accounts."

**Quote (billing — from the same page, found via search result):**
> "Lifecycle management policies are free of charge. Delete operations are free."

**Quote (filter capabilities — from `lifecycle-management-overview`):**
> "Apply rules to an entire storage account, to select containers, or to a subset of blobs using name prefixes or blob index tags as filters."

**Fetch: OK**

---

## Q7: Server-side copy between containers (Copy Blob / Put Blob From URL) and ranged reads

**Answer:** **Copy Blob** (async) and **Put Blob From URL** / **Put Block From URL** (synchronous) both perform server-side copies without transferring data through the client. For GPv2 accounts, `CopyBlob` is billed as a **write operation** on the destination tier; `CopyBlobFromURL` is also billed as a **write operation**. When copying within the same account (same region), there is **no inter-region bandwidth charge**. Ranged reads use the standard HTTP `Range` header (Azure docs refer to it as `x-ms-range` in the REST spec), and are billed as **read operations** against the source tier. No separate per-byte read fee beyond the normal read-ops and any data retrieval charges on Cool/Cold tiers.

**Source:**
- https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-copy
- https://learn.microsoft.com/en-my/azure/storage/blobs/map-rest-apis-transaction-categories
- https://github.com/MicrosoftDocs/azure-docs/blob/main/articles/storage/blobs/cost-estimate-archive-retrieval-copy-blob.md

**Quote (Copy Blob billing — from `cost-estimate-archive-retrieval-copy-blob.md`):**
> "The Copy Blob operation is billed as a read operation on the source account and a write operation on the destination account."

**Quote (REST operation mapping — from `map-rest-apis-transaction-categories`):**
> "CopyBlob | Copy Blob | Write² | Write² | Write²" _(footnote 2 indicates applies only when copying to another region for inter-region; within-account same-region is write on destination only)_

**Quote (copy methods — from `storage-blob-copy`):**
> "Put Blob From URL — This operation is preferred for scenarios where you want to move data into a storage account and have a URL for the source object. This operation completes synchronously."

**Fetch: OK**

---

## Q8: Azure Application Insights / Log Analytics pricing

**Answer:** The **first 5 GB/month per billing account** of Analytics Logs ingestion is **free** (confirmed by multiple Microsoft Q&A answers and the Azure Monitor pricing page). Beyond that, pay-as-you-go Analytics Logs ingestion is approximately **$2.30/GB** in US regions (exact price varies by region; pricing page shows `$-` placeholders requiring JS). Application Insights data lands in a Log Analytics workspace under the Analytics Logs plan. **Included free retention:** 90 days for workspace-based Application Insights (and classic App Insights). After 90 days, additional retention costs ~$0.10/GB-month. The **daily cap** feature (configurable in the Application Insights resource under _Usage and Estimated Costs_) can be set to prevent runaway ingestion costs. 30-day retention is readily achievable and within the free window; you can configure any retention from 30 days up to 730 days (interactive) or 12 years (long-term archive).

**Source:**
- https://azure.microsoft.com/en-us/pricing/details/monitor/
- https://learn.microsoft.com/en-us/azure/azure-monitor/logs/cost-logs
- https://learn.microsoft.com/en-us/answers/questions/349683/application-insights-pricing

**Quote (free allowance — from Microsoft Q&A `349683`):**
> "First 5GB ingested per month is free."

**Quote (retention — from `azure.microsoft.com/pricing/details/monitor/`):**
> "Data retained beyond these no-charge periods will be charged for each GB of data retained for a month (pro-rated daily)... 90 days – data ingested into either classic or workspace-based Application Insights."

**Quote (daily cap — from `cost-logs`):**
> "To control your costs by configuring a maximum volume that might be ingested in a workspace each day, see Set daily cap on Log Analytics workspace."

**Fetch: OK** _(Note: Per-GB ingestion price on the Azure Monitor pricing page is JS-rendered and returned as `$-` in extraction. ~$2.30/GB is from a Microsoft Q&A answer citing the PAYG rate. Verify exact current price in Azure Pricing Calculator.)_

---

## Q9: .NET OpenTelemetry distro for Application Insights (Azure.Monitor.OpenTelemetry.AspNetCore)

**Answer:** The legacy package name is **`Azure.Monitor.OpenTelemetry.AspNetCore`** (NuGet). Microsoft has since introduced a unified **`Microsoft.OpenTelemetry`** distro (`dotnet add package Microsoft.OpenTelemetry`) that wraps OpenTelemetry and exports to Azure Monitor. It auto-collects: **HTTP requests** (ASP.NET Core incoming), **outgoing HTTP dependencies**, **SQL dependencies**, and **ILogger logs** (via .NET's `ILogger` bridge). ILogger structured log properties (message template parameters) are captured via `ParseStateValues` and **become entries in `customDimensions`** queryable in KQL — e.g. `customDimensions["OrderId"]`. **Sampling is NOT enabled by default** in the OpenTelemetry distros; it must be explicitly configured. Two strategies are supported: fixed-rate (percentage ratio 0–1) and ingestion sampling (configured in the Azure portal under _Usage and Estimated Costs_).

**Source:**
- https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable
- https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-add-modify
- https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-sampling (via search result)

**Quote (ILogger → customDimensions — from `opentelemetry-add-modify` search snippet):**
> "Any attributes you add to spans are exported as custom properties. They populate the _customDimensions_ field in the requests, dependencies, traces, or exceptions table... OpenTelemetry uses .NET's `ILogger`."

**Quote (package — from `opentelemetry-enable`):**
> ".NET | `Microsoft.OpenTelemetry` | `dotnet add package Microsoft.OpenTelemetry`"

**Quote (sampling default — from search result of `opentelemetry-sampling`):**
> "Sampling is not enabled by default in Application Insights OpenTelemetry distros."

**Fetch: OK** _(Note: `Azure.Monitor.OpenTelemetry.AspNetCore` is the prior/legacy distro name; the current Microsoft distro is `Microsoft.OpenTelemetry`. Both are on NuGet. For existing projects using the Azure distro, no migration is required. Verify NuGet package latest version at nuget.org.)_

---

## Q10: Azure.Storage.Blobs NuGet capabilities

**Answer:** The **`Azure.Storage.Blobs`** NuGet package (latest v12.x) provides: SAS generation via `BlobSasBuilder` + `BlobClient.GenerateSasUri()`; ranged download via `BlobClient.DownloadStreamingAsync(new BlobDownloadOptions { Range = new HttpRange(offset, length) })`; server-side copy via `BlobBaseClient.StartCopyFromUriAsync()` (async Copy Blob) and `BlockBlobClient.SyncUploadFromUriAsync()` (synchronous Put Blob From URL). **Managed identity / `DefaultAzureCredential`**: pass a `DefaultAzureCredential` (from `Azure.Identity`) to `BlobServiceClient` or `BlobContainerClient` constructor in place of a connection string. On Azure App Service, the system-assigned managed identity is automatically picked up — no secrets in config. Assign the identity the **Storage Blob Data Contributor** (or Reader) role on the storage account.

**Source:**
- https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-copy
- https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-download
- https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs

**Quote (copy methods — from `storage-blob-copy`):**
> "Put Blob From URL... `SyncUploadFromUri` / `SyncUploadFromUriAsync`... Copy Blob... `StartCopyFromUri` / `StartCopyFromUriAsync`"

**Quote (download — from `storage-blob-download`):**
> "When enabled, the Blob SDK computes and validates CRC64-NVME checksums during upload and download operations... Transfer validation with MD5 is available to verify that the data sent by your application matches the data received."

**Quote (NuGet package reference — from `storage-blob-copy`):**
> "Package (NuGet): https://www.nuget.org/packages/Azure.Storage.Blobs"

**Fetch: OK**

---

## Gaps

1. **Blob Storage per-GB storage prices (Q1):** The Azure pricing page requires JavaScript to render the capacity pricing table for a selected region. Extracted values (Hot ~$0.0184/GB, Cool ~$0.01/GB, Cold ~$0.0045/GB for LRS East US 2) come from search-snippet rendering of the page description and third-party sources, not a directly fetched table row. Verify with the Azure Pricing Calculator at https://azure.microsoft.com/en-us/pricing/calculator/ selecting East US 2 → Blob Storage → Standard LRS.

2. **Blob Storage read operation prices (Q1):** The read-ops price row in the pricing page table was blank in extraction (JS-rendered). Values used (~$0.0044/10,000 Hot, ~$0.01/10,000 Cool, ~$0.10/10,000 Cold) are inferred from the AzCopy cost-estimation docs example calculations. These may not precisely match the current East US 2 pricing page.

3. **Application Insights per-GB PAYG ingestion price (Q8):** The Azure Monitor pricing page returned `$-` for all ingestion prices (JS-rendered). The ~$2.30/GB figure is from a Microsoft Q&A community answer. Verify current price in the Azure Pricing Calculator.

4. **Cloud Connector rollout status (Q4):** Cloud Connector for Azure Blob was announced in beta in August 2024 with gradual rollout. As of this research, it may not be available in all zones. Verify availability in your Cloudflare zone's _Rules → Cloud Connector_ tab before relying on it.

5. **`Azure.Monitor.OpenTelemetry.AspNetCore` vs `Microsoft.OpenTelemetry` (Q9):** Microsoft is transitioning from the Azure-SDK distro to the unified Microsoft distro. The older package still works. For a new project, use `Microsoft.OpenTelemetry`; for existing code on `Azure.Monitor.OpenTelemetry.AspNetCore`, no forced migration is required but the new distro is the recommended path going forward.

6. **Exact SAS expiry for service SAS (Q5):** User Delegation SAS is hard-capped at 7 days. Service SAS (account-key signed) has no hard maximum set by the API, but a SAS expiration policy can be configured on the account to enforce a recommended maximum (e.g., 1 hour). Best practice is short-lived SAS (minutes to 1 hour for upload tokens).
