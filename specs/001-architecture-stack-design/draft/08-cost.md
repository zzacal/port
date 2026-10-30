## 8. Cost and Free-Tier Budget

The cost projections below assume a personal site at the scale stated in the spec Assumptions: about
5,000 library images, about 50 galleries, and low thousands of visitor page views a month. This
scale is used for every Projected cell and is stated here so the table can be re-checked if the
scale changes.

| Service | Dimension | Free limit | Projected | Overage behaviour |
|---|---|---|---|---|
| Azure App Service | Linux B1: 1 vCPU, 1.75 GB RAM, Always On — fixed monthly plan | none | ~$13/month (verify in pricing calculator for region) [az Q1, Gaps] | n/a — fixed plan; no metered component |
| Cloudflare R2 | storage: `public` derivatives (Standard) and `private` masters (Infrequent Access after 30 days) | 10 GB-month Standard [st Q1] | ~$0.60–$0.80/month: ~11 GB derivatives, of which 1 GB is above the free 10 GB at $0.015/GB-month, plus ~60–75 GB masters at $0.01/GB-month [st Q1] | billed per GB-month; Infrequent Access has a 30-day minimum and a retrieval fee, which apply only to masters [st Q1] |
| Cloudflare R2 | Class A operations — staging PUTs and confirm copies, ~125,000/year | 1 million/month [st Q1, Q5] | $0.00 | $4.50/million [st Q1] |
| Cloudflare R2 | Class B operations — origin pulls on edge cache misses, validation reads | 10 million/month [st Q1] | $0.00 at low-thousands of page views | $0.36/million [st Q1] |
| Cloudflare R2 | egress | free on all storage classes [st Q1] | $0.00 | n/a — no egress charge [st Q1] |
| Application Insights | data ingestion | 5 GB/month free; 150 MB daily cap configured [asl Q8, Q9] | $0.00 at ~100 MB/month (~2% of free allowance) [asl Q9] | ingestion stops for the rest of the day when the daily cap is hit; ~$2.30/GB above 5 GB/month [asl Q8] |
| Application Insights | data retention | 90 days included free [asl Q8] | $0.00 at 30-day retention policy [asl Q8] | billed per GB-day above 90 days; not applicable at 30-day retention |
| MongoDB Atlas | storage, connections, ops/s, transfer — Free tier | 0.5 GB storage, 500 connections, 100 ops/s, 10 GB/7 days [st Q7] | $0.00 at ~15 MB and minimal ops/s [R-03] | upgrade to paid cluster required; no partial overage on Free tier [st Q7] |
| Cloudflare | edge cache, URL purge (800 URLs/s, 100 per call), Tiered Cache, Early Hints | all included on Free plan [cf Q1–Q5] | $0.00 | n/a — Free plan features [cf Q1] |
| Cloudflare | Origin CA certificate, 15-year | free [cf Q5] | $0.00 | n/a — free; non-renewable before the 15-year term |
| TOTAL fixed | — | — | ~$13/month (App Service B1 only; verify for region) [az Q1, Gaps] | — |
| TOTAL metered | — | — | $1.00/month at 5,000 images, 50 galleries, low-thousands of page views [R-02] | alert at $7 (decisions #6); cap at $10 (Constitution VIII.1) |

**Configured limits**: The one hard limit is the Application Insights daily ingestion cap of
150 MB, which keeps monthly ingestion under the 5 GB free allowance (Constitution VIII.4;
decisions #4; research R-09). The Azure subscription budget notifies at $7/month and again at
$10/month (Constitution VIII.1; decisions #6). An Azure budget sends alerts and does not stop
resources, so the $10 cap is a governance limit: exceeding it requires an amendment. Since
constitution 4.0.0, R2 is billed by Cloudflare and the Azure budget does not see it; R2 offers no
hard spending limit, so VIII.4 adds nothing. The projected R2 cost is under $1/month, and the
owner checks it on the Cloudflare billing page until hosting is decided.

Prices labelled "verify" come from JavaScript-rendered pricing pages and must be confirmed in the
Azure Pricing Calculator before provisioning ([asl Gaps]).
