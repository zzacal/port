## 1. Overview

This document is the Gallery architecture and stack design. It is the source of truth for all
later features (Constitution VII.2). Any plan that deviates from it must say so in its
Constitution Check and update this document in the same feature. No application source, project
files, or infrastructure changes are produced here; those belong to the first build feature
(FR-001, FR-022).

**Stack at a glance** (Platform Constraints v4.0.0):

- **Application**: .NET 10 minimal APIs, server-rendered HTML; htmx and web components for admin
  interactivity.
- **Hosting**: Azure App Service (Linux, B1); the origin accepts traffic only from Cloudflare.
- **Edge**: Cloudflare DNS, proxy, cache, and TLS in Full (strict) mode. Never Flexible.
- **Identity**: Google OIDC, with a cookie session that has a 7-day sliding expiry renewed while
  it is in use.
- **Data**: MongoDB Atlas free tier, same region as the app.
- **Image storage**: Cloudflare R2 behind a storage abstraction. Two buckets: a public bucket of
  derivatives reachable only through the image hostname, and a private bucket of staged uploads and
  masters kept permanently. The app uses a bucket-scoped credential; uploads use short-lived
  presigned URLs (Constitution 4.0.0, III.9).
- **Image delivery**: derivatives are served on a custom image hostname attached to the public
  bucket as an R2 custom domain, through the Cloudflare cache. Browsers never fetch from the R2 S3
  endpoint or an r2.dev URL.
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
