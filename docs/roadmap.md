# Build Roadmap

The design (docs/design.md) decides what and why. This file decides the order in which features build it.
Each feature is a vertical slice that ends running and verified from logs, following the spec-kit flow
(specify → clarify → plan → tasks → analyze → implement). Every D-, L-, E-, and R- entry and every
deferred traceability row in docs/design.md has exactly one owning feature below. The coverage
table at the end is the check.

Feature numbers are provisional. Each one is fixed when `/speckit-specify` creates its directory.

## Local first (constitution 4.0.0)

Hosting is under review, because anyone with access to an Azure subscription can see what the site
serves (decisions #34). Until a hosting feature lands, every feature runs on the owner's machine
against:

- **Mongo**: a local container. The app is written for the Atlas free tier and uses nothing that
  M0 lacks.
- **Images**: real Cloudflare R2 development buckets for the dev run. Automated tests use an
  S3-compatible emulator in a container. The owner picked MinIO, but its community edition was
  archived in April 2026 and gets no fixes. The 006 plan picks a maintained single-node
  replacement (candidates are SeaweedFS and S3Proxy, both Apache 2.0) and records it in
  decisions.md. The test contract is the same either way: presigned PUT, copy, ranged GET, and
  delete.
- **Logs**: structured console logs with the same `http.request` and `op.timing` fields that
  Application Insights will receive. Evidence comes from the local log file.
- **Edge**: none locally. Cache and purge behaviour (D-16) is verified by response-header
  assertions until hosting exists.

R2 setup needed before 006 (owner, in the Cloudflare dashboard). The bucket that exists now is
`port`:

- Use `port` as the dev `private` bucket and create a dev `public` bucket, or create a matching
  pair under new names. Production gets its own pair later.
- Attach a dev image hostname (for example `img-dev.<domain>`) to the dev public bucket. Leave
  r2.dev disabled on both buckets (III.9).
- Create one API token with Object Read & Write, scoped to the two dev buckets only (III.9). Keep
  its keys in .NET user-secrets on your machine, never in the repo.
- CORS on the dev private bucket comes with 008, which is the first feature that uploads from the
  browser.

## Order

### 006 Walking skeleton: public site, local

What a visitor sees on localhost: the home grid, a gallery page, and the solo view, served from
local Mongo with images from the dev R2 bucket through the dev image hostname. A seed command
writes the 003 sample set to Mongo and uploads its derivatives to R2. No admin routes exist yet.

- **Read path**: Mongo reads with the denormalised home cards (D-13), ids and slug redirects
  (D-14), rendering through the 003 components (D-15, D-20, D-21), cache headers (D-16 header
  side), predictive loading (D-17), and image URLs on the image hostname (D-19).
- **Storage**: the storage abstraction over R2's S3 API (D-07), the bucket-scoped credential from
  configuration (III.9), and the emulator for tests.
- **Observability**: request timing, `op.timing` for Mongo and R2 calls, Server-Timing, the
  forwarded-headers setup (PC.9), and a local log-evidence script that replaces the saved KQL
  queries until hosting exists.

### 007 Owner sign-in and claim

The first `/admin` route. Google sign-in, the atomic claim, the refusal path, a session cookie,
and antiforgery. AdminLayout renders an empty owner shell. This runs locally against a Google
OAuth client with a localhost redirect URI.

- D-04, D-06, the D-23 sign-in page, E-01, E-02, and PC.4.
- **Runbook first**: create the Google Cloud project and OAuth client (it has no API).

### 008 Upload pipeline into the library

The browser derivative worker, presigned R2 intake, server validation, the batch lifecycle, and
the upload batch page. Confirmed images land in the library.

- D-08, D-09, D-10, D-11, L-01 to L-06, E-03 to E-11, and R-07.
- **Before `/speckit-plan`**: spike the jSquash AVIF encode of a 20 MB JPEG on Safari, iOS, and
  Chrome (memory and time, E-05). Also build the GPS-injected AVIF and WebP fixtures and confirm
  that MetadataExtractor and the structure parsers both reject them. Measure the real ThumbHash
  and derivative sizes (section 10). A failed spike reopens D-09 or D-10 before planning.

### 009 Library and gallery management

The library page (titles, captions, bulk actions, delete), the gallery list, and the gallery
builder. This includes visibility, ordering, covers, and purge plus warm on every public change.
Purge calls go to the dev zone for image URLs; page purge is exercised once hosting exists. The
seeded sample content is removed in this feature.

- L-07 to L-17, E-12, E-14 to E-18, R-03, and R-04.

### Hosting (number assigned when it starts)

Choose and provision the host, then deploy, once the owner has a subscription or account only
they control. This feature reopens D-01, D-03, D-05 region, D-12, D-18, D-24, and D-25, and it
amends the Platform Constraints for whatever host is chosen. It owns production R2 buckets and
their image hostname, Cloudflare in front of the origin, origin lock-down, the budget alerts, log
ingestion and saved queries, edge HTML caching and page purge (D-16 edge side), and the claim
window on first deploy (R-01). It can start at any point. 007 is the latest sensible point,
because R-01 only exists once `/admin` is public.

## Not a feature

- **Cost cap (D-18, E-19)**: configuration owned by the hosting feature. Until then, the only
  metered service is R2 at well under $1/month; the owner checks it on the Cloudflare billing
  page.
- **Backups (E-13, R-02)**: an accepted risk with no feature.
- **R-05 (log retention)**: an accepted risk, configured by the hosting feature.

## Coverage

| Entry | Feature |
|---|---|
| D-07, D-13, D-14, D-15, D-17, D-19, D-20 | 006 |
| D-16 | 006 (headers), 009 (purge and warm), hosting (edge cache rules) |
| D-21, D-22 | built (003, 002); followed by all |
| D-23 | 007 (sign-in, shell), 008 (upload batch), 009 (library, gallery list, builder) |
| D-04, D-06 | 007 |
| D-08, D-09, D-10, D-11 | 008 |
| D-01, D-02, D-03, D-05, D-12, D-18, D-24, D-25 | hosting (reopened) |
| L-01 to L-06 | 008 |
| L-07 to L-17 | 009 |
| E-01, E-02 | 007 |
| E-03 to E-11 | 008 |
| E-12, E-14, E-15, E-16, E-17, E-18 | 009 (E-16 rendering also in 006) |
| E-13 | accepted risk (R-02) |
| E-19 | hosting |
| R-01 | hosting (first public deploy of `/admin`) |
| R-02, R-05 | accepted; R-05 configured by hosting |
| R-03, R-04 | 009 |
| R-06 | 006 (token scope and storage), rotation runbook with hosting |
| R-07 | 008 |
| III.9 | 006 (credential, buckets), 008 (presigned uploads) |
| PC.9 | 006 |
| PC.4 | 007 |
| PC.1, PC.2, PC.3, PC.5, PC.6, PC.7, PC.8 | hosting |
