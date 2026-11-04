## 8. Cost and Free-Tier Budget

The cost projections below assume a personal site at the scale stated in the spec Assumptions: about
5,000 library images, about 50 galleries, and low thousands of visitor page views a month. This
scale is used for every Projected cell and is stated here so the table can be re-checked if the
scale changes.

| Service | Dimension | Free limit | Projected | Overage behaviour |
|---|---|---|---|---|
| Azure App Service | Linux B2 `portfolio-plan`, West US, Always On — fixed monthly plan | none | US$26.28/month list price (owner's portal summary; 012 research R-12) | n/a — fixed plan; no metered component |
| Cloudflare R2 | storage: `public` derivatives (Standard) and `private` masters (Infrequent Access after 30 days) | 10 GB-month Standard [st Q1] | ~$0.60–$0.80/month: ~11 GB derivatives, of which 1 GB is above the free 10 GB at $0.015/GB-month, plus ~60–75 GB masters at $0.01/GB-month [st Q1] | billed per GB-month; Infrequent Access has a 30-day minimum and a retrieval fee, which apply only to masters [st Q1] |
| Cloudflare R2 | Class A operations — staging PUTs and confirm copies, ~125,000/year | 1 million/month [st Q1, Q5] | $0.00 | $4.50/million [st Q1] |
| Cloudflare R2 | Class B operations — origin pulls on edge cache misses, validation reads | 10 million/month [st Q1] | $0.00 at low-thousands of page views | $0.36/million [st Q1] |
| Cloudflare R2 | egress | free on all storage classes [st Q1] | $0.00 | n/a — no egress charge [st Q1] |
| Application Insights | data ingestion | 5 GB/month free; 150 MB daily cap configured [asl Q8, Q9] | $0.00 at ~100 MB/month (~2% of free allowance) [asl Q9] | ingestion stops for the rest of the day when the daily cap is hit; ~$2.30/GB above 5 GB/month [asl Q8] |
| Application Insights | data retention | 90 days included free [asl Q8] | $0.00 at 30-day retention policy [asl Q8] | billed per GB-day above 90 days; not applicable at 30-day retention |
| MongoDB Atlas | storage, connections, ops/s, transfer — Free tier | 0.5 GB storage, 500 connections, 100 ops/s, 10 GB/7 days [st Q7] | $0.00 at ~15 MB and minimal ops/s [R-03] | upgrade to paid cluster required; no partial overage on Free tier [st Q7] |
| Cloudflare | edge cache, URL purge (800 URLs/s, 100 per call), Tiered Cache, Early Hints | all included on Free plan [cf Q1–Q5] | $0.00 | n/a — Free plan features [cf Q1] |
| Cloudflare | Origin CA certificate, 15-year | free [cf Q5] | $0.00 | n/a — free; non-renewable before the 15-year term |
| TOTAL fixed | — | — | US$26.28/month (App Service B2 only; 012 research R-12) | — |
| TOTAL metered | — | — | $1.00/month at 5,000 images, 50 galleries, low-thousands of page views [R-02] | projection threshold $7 (Constitution VIII.3); cap at $10 (Constitution VIII.1); no alert (constitution 6.0.0) |

**Configured limits**: The one hard limit is the Application Insights daily ingestion cap of
0.15 GB (150 MB) on `portfolio-appinsights`, with 30-day retention, which keeps monthly ingestion
under the 5 GB free allowance (Constitution VIII.4; decisions #4; research R-09). It is the only
control on metered Azure spend. No budget alert is configured: the owner withdrew it (constitution
6.0.0; 012 decisions #21), so nothing notifies the owner if Azure spend rises. This is bounded in
practice because the B2 plan is fixed-price and the ingestion cap limits the only metered Azure
line. The $10 cap is a governance limit: exceeding it requires an amendment, and one Azure budget
on the resource group restores an alert with no code change. R2 is billed by Cloudflare and offers
no hard spending limit, so VIII.4 adds nothing there; the projected R2 cost is under $1/month, and
the owner checks it on the Cloudflare billing page.

The B2 figure is the list price from the owner's Azure portal summary (012 research R-12); the
other unit prices come from the sources cited in each row.
