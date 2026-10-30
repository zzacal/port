# Build Roadmap

The design (docs/design.md) decides what and why. This file decides the order in which features build it.
Each feature is a vertical slice that ends deployed and verified from logs, following the spec-kit flow
(specify → clarify → plan → tasks → analyze → implement). Every D-, L-, E-, and R- entry and every
deferred traceability row in docs/design.md has exactly one owning feature below. The coverage
table at the end is the check.

Feature numbers are provisional. Each one is fixed when `/speckit-specify` creates its directory.

## Order

### 006 Walking skeleton: public site, live

What a visitor sees: the home grid, a gallery page, and the solo view, served from Mongo and Blob
through Cloudflare on the real domain. They contain the 003 sample set, written by a seed command.
No admin routes exist yet, so there's no claim window (R-01) while this feature is live.

- **Provisioning (D-24)**: the whole `infra/` module except the Google OAuth settings (D-01, D-02,
  D-03, D-05, D-07, D-12, D-18, D-19). The budget and the ingestion cap are created in the same
  apply as the first metered resource (VIII.1, VIII.4).
- **Pipeline (D-25)**: test on every push, deploy from `main` over OIDC.
- **Read path**: Mongo reads with the denormalised home cards (D-13), ids and slug redirects
  (D-14), rendering through the 003 components (D-15, D-20, D-21), edge HTML cache with no purge
  yet (D-16), predictive loading (D-17), and `img.<domain>` delivery (D-19).
- **Observability**: request timing, `op.timing` for Mongo reads, Server-Timing, and the
  forwarded headers (PC.9). The saved-KQL pattern is set up here and reused by every later
  feature (section 10).
- **Prerequisites from section 10, done first**:
  - Confirm Cloud Connector on the zone.
  - Choose the Atlas region.
  - Verify unit prices.
  - Create an Atlas account and API keys (the owner has none yet).
- **Severable seam**: if this feature is too large, split it into 006a (infra, pipeline, and an
  empty deployed app) and 006b (the read path).

### 007 Owner sign-in and claim

The first `/admin` route. Google sign-in, the atomic claim, the refusal path, a session cookie,
and antiforgery. AdminLayout renders an empty owner shell.

- D-04, D-06, the D-23 sign-in page, E-01, E-02, R-01, and PC.4.
- **Runbook first**: create the Google Cloud project and OAuth client (it has no API), then add
  the client id and secret as OpenTofu variables.
- **Deploy rule**: the owner signs in right after the first deploy that exposes `/admin` (R-01).
  The claim event is checked in logs.

### 008 Upload pipeline into the library

The browser derivative worker, SAS intake, server validation, the batch lifecycle, and the upload
batch page. Confirmed images land in the library.

- D-08, D-09, D-10, D-11, L-01 to L-06, E-03 to E-11, and R-07.
- **Before `/speckit-plan`**: spike the jSquash AVIF encode of a 20 MB JPEG on Safari, iOS, and
  Chrome (memory and time, E-05). Also build the GPS-injected AVIF and WebP fixtures and confirm
  that MetadataExtractor and the structure parsers both reject them. Measure the real ThumbHash
  and derivative sizes (section 10). A failed spike reopens D-09 or D-10 before planning.

### 009 Library and gallery management

The library page (titles, captions, bulk actions, delete), the gallery list, and the gallery
builder. This includes visibility, ordering, covers, and edge purge plus warm on every public
change. The seeded sample content is removed in this feature.

- D-16 purge, L-07 to L-17, E-12, E-14 to E-18, R-03, and R-04.

## Not a feature

- **Cost cap (D-18, E-19)**: the budget alerts, the ingestion cap, and the Atlas limit are all
  configuration. They're created in 006, so no separate feature remains. This change folds the
  fifth roadmap feature into 006.
- **Backups (E-13, R-02)**: an accepted risk with no feature. Reversing it is a scheduled
  export, which would need its own spec.
- **R-05 (log retention)** and **R-06 (Cloud Connector beta)**: accepted risks. They're
  configured in 006 and need no feature work.

## Coverage

| Entry | Feature |
|---|---|
| D-01, D-02, D-03, D-05, D-07, D-12, D-13, D-14, D-15, D-17, D-18, D-19, D-20, D-24, D-25 | 006 |
| D-16 | 006 (cache), 009 (purge and warm) |
| D-21, D-22 | built (003, 002); followed by all |
| D-23 | 007 (sign-in, shell), 008 (upload batch), 009 (library, gallery list, builder) |
| D-04, D-06 | 007 |
| D-08, D-09, D-10, D-11 | 008 |
| L-01 to L-06 | 008 |
| L-07 to L-17 | 009 |
| E-01, E-02 | 007 |
| E-03 to E-11 | 008 |
| E-12, E-14, E-15, E-16, E-17, E-18 | 009 (E-16 rendering also in 006) |
| E-13 | accepted risk (R-02) |
| E-19 | 006 |
| R-01 | 007 |
| R-02, R-05, R-06 | accepted; R-05 and R-06 configured in 006 |
| R-03, R-04 | 009 |
| R-07 | 008 |
| PC.1, PC.2, PC.3, PC.5, PC.6, PC.7, PC.8, PC.9 | 006 |
| PC.4 | 007 |
