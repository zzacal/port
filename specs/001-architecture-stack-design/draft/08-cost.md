## 8. Cost and Free-Tier Budget

The cost projections below assume a personal site at the scale stated in the spec Assumptions: about
5,000 library images, about 50 galleries, and low thousands of visitor page views a month. This
scale is used for every Projected cell and is stated here so the table can be re-checked if the
scale changes.

| Service | Dimension | Free limit | Projected | Overage behaviour |
|---|---|---|---|---|
| Azure App Service | Linux B1: 1 vCPU, 1.75 GB RAM, Always On — fixed monthly plan | none | ~$13/month (verify in pricing calculator for region) [az Q1, Gaps] | n/a — fixed plan; no metered component |
| Azure Blob Storage | `public` container, Hot tier — derivative storage | none (no free storage allowance on LRS) [asl Q1] | ~$0.20/month at ~11 GB derivatives [asl Q1, Q2] | $0.0184/GB-month (verify in calculator) [asl Q1, Gaps] |
| Azure Blob Storage | `private` container, Cool tier — master storage; 30-day minimum-stay charge applies to early deletes | none [asl Q2] | ~$0.60–$0.75/month at ~60–75 GB masters [asl Q2] | $0.010/GB-month; a master deleted within 30 days is billed for the full 30-day period [asl Q2] |
| Azure Blob Storage | write operations — staging PUTs and confirm copies, ~125,000/year | none [asl Q6] | ~$0.10/month amortized at 5,000 images/year [asl Q1, Q6] | $0.065/10,000 (Hot); $0.13/10,000 (Cool) [asl Q1] |
| Azure Blob Storage | read operations — origin pulls on edge cache misses | none | ~$0.00 at low-thousands of page views (edge cache serves the vast majority) [R-01, R-04] | verify in pricing calculator [asl Gaps] |
| Azure Blob Storage | internet egress from Blob to Cloudflare edge | first 100 GB/month free [asl Q2] | $0.00 — edge cache keeps origin egress well under 100 GB at expected traffic [asl Q2] | billed per GB above 100 GB; verify rate in calculator [asl Gaps] |
| Application Insights | data ingestion | 5 GB/month free; 150 MB daily cap configured [asl Q8, Q9] | $0.00 at ~100 MB/month (~2% of free allowance) [asl Q9] | ingestion stops for the rest of the day when the daily cap is hit; ~$2.30/GB above 5 GB/month [asl Q8] |
| Application Insights | data retention | 90 days included free [asl Q8] | $0.00 at 30-day retention policy [asl Q8] | billed per GB-day above 90 days; not applicable at 30-day retention |
| MongoDB Atlas | storage, connections, ops/s, transfer — Free tier | 0.5 GB storage, 500 connections, 100 ops/s, 10 GB/7 days [st Q7] | $0.00 at ~15 MB and minimal ops/s [R-03] | upgrade to paid cluster required; no partial overage on Free tier [st Q7] |
| Cloudflare | edge cache, URL purge (800 URLs/s, 100 per call), Tiered Cache, Early Hints | all included on Free plan [cf Q1–Q5] | $0.00 | n/a — Free plan features [cf Q1] |
| Cloudflare | Cloud Connector (image routing to Blob) and Worker fallback (100k requests/day) | Cloud Connector free on all plans; Worker 100k requests/day on Free [cf Q7, asl Q4] | $0.00 at low-thousands of page views [R-04] | paid Worker plan if traffic exceeds 100k requests/day; Cloud Connector is beta — Worker fallback applies if withdrawn [R-04] |
| Cloudflare | Origin CA certificate, 15-year | free [cf Q5] | $0.00 | n/a — free; non-renewable before the 15-year term |
| TOTAL fixed | — | — | ~$13/month (App Service B1 only; verify for region) [az Q1, Gaps] | — |
| TOTAL metered | — | — | $1.00/month at 5,000 images, 50 galleries, low-thousands of page views [R-02] | alert at $7 (decisions #6); cap at $10 (Constitution VIII.1) |

**Configured limits**: The one hard limit is the Application Insights daily ingestion cap of
150 MB, which keeps monthly ingestion under the 5 GB free allowance (Constitution VIII.4;
decisions #4; research R-09). The Azure subscription budget notifies at $7/month and again at
$10/month (Constitution VIII.1; decisions #6). An Azure budget sends alerts and does not stop
resources, so the $10 cap is a governance limit: exceeding it requires an amendment.

Prices labelled "verify" come from JavaScript-rendered pricing pages and must be confirmed in the
Azure Pricing Calculator before provisioning ([asl Gaps]).
