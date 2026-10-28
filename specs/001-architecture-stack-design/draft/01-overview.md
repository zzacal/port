## 1. Overview

This document is the Gallery architecture and stack design. It is the source of truth for all
later features (Constitution VII.2). Any plan that deviates from it must say so in its
Constitution Check and update this document in the same feature. No application source, project
files, or infrastructure changes are produced here; those belong to the first build feature
(FR-001, FR-022).

**Stack at a glance** (Platform Constraints v3.0.0):

- **Application**: .NET 10 minimal APIs, server-rendered HTML; htmx and web components for admin
  interactivity.
- **Hosting**: Azure App Service (Linux, B1); the origin accepts traffic only from Cloudflare.
- **Edge**: Cloudflare DNS, proxy, cache, and TLS in Full (strict) mode. Never Flexible.
- **Identity**: Google OIDC, with a cookie session that has a 7-day sliding expiry renewed while
  it is in use.
- **Data**: MongoDB Atlas free tier, same region as the app.
- **Image storage**: Azure Blob Storage (general-purpose v2, LRS, same region as the app) behind
  a storage abstraction. Two containers: public Hot derivatives for anonymous read via the edge;
  private Cool masters kept permanently. The app uses managed identity; uploads use short-lived
  SAS tokens.
- **Image delivery**: derivatives are served on a custom image hostname through Cloudflare Cloud
  Connector to the public container. If Cloud Connector is unavailable on the zone, a Cloudflare
  Worker on the free plan fetches from the container instead. Browsers never fetch directly from
  the Blob endpoint.
- **Observability**: workspace-based Azure Application Insights with 30-day retention and a daily
  ingestion cap that keeps it inside the free monthly allowance (VIII.4).
- **Forwarded headers**: the app honors `CF-Connecting-IP` and the forwarded scheme from
  Cloudflare so that generated URLs and audit logs are correct.

**Scale assumption**: cost projections and sizing figures throughout this document assume
approximately 5,000 library images, 50 galleries, and low thousands of visitor page views per
month.

**How to read this document**: Section 2 is the decision record; each entry states one
architectural area with its choice, rationale, alternatives rejected, and pivot cost. Section 3
has four system diagrams covering the main request paths. Sections 4 and 5 define the content
model and the lifecycle of every event that changes stored records or visible pages. Section 6
covers edge cases. Section 7 traces every numbered constitution rule to a mechanism and a
verification method. Section 8 gives the cost table and the configured spending limits. Section 9
is the accepted-risk register. Section 10 lists items deferred to later features. Section 11 is
the source list.

This document is machine-validated by `specs/001-architecture-stack-design/evidence/validate.sh`,
which checks headings, labels, diagrams, traceability, cost figures, and hygiene before any
human review.
