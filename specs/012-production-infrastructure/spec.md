# Feature Specification: Production Infrastructure

**Feature Branch**: `012-production-infrastructure`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "standing up production infrastructure"

This is the roadmap's "Hosting" feature (docs/roadmap.md, "Hosting (number assigned when it
starts)"). Everything built so far runs only on the owner's machine. This feature puts the site on
the public internet: a production host, production image storage and image hostname, the edge in
front of both, hosted logs, cost alerts, and a repeatable way to create and update all of it.

## Clarifications

### Session 2026-11-02

- Q: Public address of the site? → A: `port.zacal.me`. Images stay on a separate production
  image hostname (default `img.zacal.me`). Applied to FR-005, FR-016 and Assumptions.
- Q: How does code reach production? → A: Option A, the D-25 pipeline: build and full test suite
  on every push, deploy from `main` only after they pass. The exact sign-in mechanism the pipeline
  uses depends on the host (Q1, still open). Applied to User Story 5 and FR-024.
- Q: Which host? → A: Azure App Service (Linux B1), as in the constitution and D-01, so no
  amendment is needed for the host itself. The owner expects not to have rights for a pipeline
  identity that can create resources: production resources are created by the owner, and the
  pipeline only gets the ability to publish to the one existing app. Applied to FR-001, FR-002,
  FR-022 and FR-024. Still open: what rights the owner holds in the subscription (decides the
  pipeline's credential) and whether "by hand" means the dashboard or the IaC tool run from the
  owner's machine.
- Q: Which subscription, and how are resources created? → A: Subscription "Zac Zacal Test Ops
  Platform" (`c28725a0-a61d-4a6c-999a-0a8980111fbf`) in the Alaska Airlines tenant. OpenTofu, run
  from the owner's machine, is accepted as long as every Azure resource it touches is scoped to
  that subscription. The pipeline still only publishes. Applied to FR-001, FR-020, FR-022 and
  Assumptions (accepted risk: tenant administrators can see the site).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A visitor opens the real site (Priority: P1)

A visitor types the site's public address. They get the home page over HTTPS with a valid
certificate, follow Galleries, open a gallery and a photo, and everything behaves as it does on
the owner's machine today: same pages, same images, same speed tricks (prerender on hover, the
thumbnail-first photo view, view transitions). Images come from the production image hostname.

**Why this priority**: This is the feature. Nothing else here matters until a stranger can load
the site.

**Independent Test**: From a network outside the owner's home, load the home page, the galleries
listing, one gallery and one photo page. Check the certificate, the image addresses, and that each
page answers successfully.

**Acceptance Scenarios**:

1. **Given** production is provisioned and deployed, **When** a visitor opens the public address
   over HTTPS, **Then** the home page loads with a certificate browsers trust and no warnings.
2. **Given** a visitor uses plain HTTP, **When** they open any public address, **Then** they are
   redirected to the HTTPS address.
3. **Given** a published gallery with photos, **When** a visitor opens it, **Then** every image is
   fetched from the production image hostname, and never from a storage provider's own address.
4. **Given** the site has no content yet, **When** a visitor opens the home page and the galleries
   listing, **Then** both load, and the listing is empty (the empty-state rules from 006 and 011).

---

### User Story 2 - The owner claims production and publishes the first gallery (Priority: P1)

Right after the first deploy, the owner signs in at `/admin` on the production address with their
Google account and becomes the owner. They upload photos, build a gallery, make it public, and
see it on the public listing within a minute, with its images served through the edge.

**Why this priority**: An empty public site with an unclaimed admin is the riskiest state
production can be in (R-01). The claim and first publish are what make it the owner's site.

**Independent Test**: On a freshly deployed production, sign in as the owner, confirm the claim is
recorded in the hosted logs, upload a small batch, publish a gallery, and load it anonymously.

**Acceptance Scenarios**:

1. **Given** an unclaimed production site, **When** the owner completes the first sign-in at
   `/admin`, **Then** they become the owner and the claim event, with their identity, client
   address and time, is queryable in the hosted logs.
2. **Given** a claimed production site, **When** any other Google account signs in, **Then** it is
   refused, as on the owner's machine today.
3. **Given** the owner uploads photos in production, **When** the batch is confirmed, **Then**
   masters and staged files land only in the production private storage, and derivatives only in
   the production public storage.
4. **Given** the owner makes a gallery public, **When** a visitor loads the listing within 60
   seconds, **Then** the gallery appears, because the affected pages were purged at the edge.

---

### User Story 3 - The origin can only be reached through the edge (Priority: P1)

Anyone who finds the host's own address and requests it directly is refused. Every visitor
request passes through the edge, which caches public pages and images and forwards the real
visitor address to the app.

**Why this priority**: Constitution Platform Constraints require it, and the edge cache, purge
rules and audit log (the claim event's client address) are only correct if nothing bypasses the
edge.

**Independent Test**: Request the host's own address directly, with and without the public
hostname in the request; both must be refused. Then request the same page through the public
address and check it succeeds, and that the logged client address is the caller's, not the edge's.

**Acceptance Scenarios**:

1. **Given** a request that does not come through the edge, **When** it reaches the host,
   **Then** it is refused without the app serving any page.
2. **Given** a request that comes through the edge, **When** the app logs it, **Then** the logged
   client address and scheme are the visitor's real ones.
3. **Given** an anonymous visitor loads a public page twice, **When** the second request is made,
   **Then** it is answered from the edge cache without reaching the app.
4. **Given** the owner is signed in, **When** they load a public page, **Then** the edge cache is
   bypassed for them.

---

### User Story 4 - The owner can see what production is doing and costing (Priority: P2)

The owner can query the production logs for request timings and named operation timings, and
gets an alert before monthly spend reaches the cost cap.

**Why this priority**: The constitution makes every time bound verifiable from logs (IV.4) and
caps metered spend (VIII). Without hosted logs and the alert, production silently loses both
guarantees, but visitors are not affected on day one.

**Independent Test**: Load a few production pages, then run the committed log queries and get
per-route timing results. Inspect the configured spending alerts and the log ingestion limit.

**Acceptance Scenarios**:

1. **Given** production has served requests, **When** the owner runs a committed query, **Then**
   they get request and operation timings per route without opening any server.
2. **Given** metered spend reaches the alert threshold, **When** the threshold is crossed,
   **Then** the owner is notified.
3. **Given** a sudden flood of log volume, **When** the daily ingestion limit is reached, **Then**
   ingestion stops for the day rather than billing past the free allowance.

---

### User Story 5 - The owner can rebuild and update production repeatably (Priority: P2)

Every production resource is described in the repository. The owner previews a change before
applying it, applies it from their own machine, and can see when someone changed a setting by
hand. Code changes reach production through an automated pipeline: every push is built and tested,
and only `main` deploys, only after its tests pass.

**Why this priority**: The design already decided this (D-24, D-25, both on hold). It is what
makes production recoverable and keeps dashboard edits from drifting silently.

**Independent Test**: With production running, run a preview of the infrastructure description;
it reports no changes. Change one setting by hand in a dashboard; the preview reports it.

**Acceptance Scenarios**:

1. **Given** production was created from the repository, **When** the owner previews again with
   no edits, **Then** the preview reports zero changes.
2. **Given** a setting was changed by hand, **When** the owner previews, **Then** the drift is
   listed.
3. **Given** a change merged to the main branch, **When** the deployment path runs, **Then** the
   change is live only after the full test suite passes.

---

### Edge Cases

- The first deploy is live but the owner has not signed in yet: anyone reaching `/admin` first
  could claim the site (R-01). The runbook orders the steps so the owner signs in immediately
  after the first deploy, and the claim event shows who won.
- A stranger claims first anyway: the owner sees refusal; recovery is the out-of-band datastore
  reset (I.4), documented in the runbook.
- The edge's published address ranges change: the origin allow-list must follow, or real traffic
  is refused. The preview shows the difference.
- A purge call fails after a publish: cached HTML self-heals within its bounded lifetime (IV.18),
  as already designed (E-12).
- A deploy fails its tests: production keeps the previous version.
- The datastore's free tier pauses after long inactivity: the always-on host keeps a connection
  open, so it does not.
- Development keys or development buckets are configured in production by mistake: the app
  refuses to start, because production configuration is checked against the development names.
- The seeding command is run against production: it refuses (III.10).
- The public address currently resolves to a private home-network address (observed below): it
  must be repointed, and anything relying on that record moves first.

## Requirements *(mandatory)*

### Functional Requirements

Host and platform

- **FR-001**: The production app MUST run on one Azure App Service Linux B1 plan (Platform
  Constraints, D-01) in the subscription "Zac Zacal Test Ops Platform"
  (`c28725a0-a61d-4a6c-999a-0a8980111fbf`), inside resource groups used only by this site. No
  production Azure resource may live in any other subscription.
- **FR-002**: If the chosen host differs from the constitution's Platform Constraints (Azure App
  Service Linux B1) or breaks the VIII.1 fixed-cost ceiling, the constitution MUST be amended
  through /speckit-constitution before /speckit-plan, and D-01, D-03, D-05, D-12, D-18, D-24 and
  D-25 updated in the design record in this feature (VII.2).
- **FR-003**: The app MUST stay warm: no request may wait on a cold start of the app, and the
  datastore connection MUST stay open so the free datastore never pauses.
- **FR-004**: Production MUST use its own datastore, separate from development, in the region
  nearest the host.

Addresses, TLS and the edge

- **FR-005**: The site MUST be served at `https://port.zacal.me` and images at a separate
  production image hostname attached to the production public bucket.
- **FR-006**: All traffic MUST be HTTPS with free certificates at the edge and between edge and
  host, in strict mode (never flexible); plain HTTP MUST redirect to HTTPS.
- **FR-007**: The host MUST refuse every request that did not come through the edge. An address
  allow-list alone is not enough (D-03): the edge MUST also authenticate itself to the host.
- **FR-008**: The app MUST honour the edge's forwarded scheme and visitor address (PC.9), trusting
  them only from the edge, so generated URLs and the claim event's client address are correct.
- **FR-009**: Anonymous public HTML MUST be cached at the edge with the existing bounded
  lifetime and MUST bypass the edge cache whenever the owner session is present (D-16, IV.18).
- **FR-010**: Image responses on the production image hostname MUST be cached at the edge for one
  year (D-19), and tiered caching and early hints MUST be on (IV.10, IV.19).
- **FR-011**: Page purge and warm, today log-only for pages, MUST run against production pages
  and images on every owner change that already triggers them (D-16), using a credential that can
  only purge cache.

Storage and secrets

- **FR-012**: Production MUST have its own private and public buckets and its own storage
  credential, scoped to object read and write on those two buckets only; the public bucket's
  development address MUST be disabled (III.9).
- **FR-013**: The production private bucket MUST accept browser uploads only from the production
  admin origin, only by PUT, signing no header except content type (008 FR-044), and its staging
  area MUST have the 7-day cleanup rule (D-11).
- **FR-014**: Every secret (storage keys, purge credential, datastore connection, identity client
  secret) MUST live outside source control and outside any build artifact, and MUST NOT reach the
  browser.
- **FR-015**: The app MUST refuse to start in production when any configured bucket, image
  hostname or datastore is a development one, and the seed command MUST refuse production
  (III.10).

Identity and claim

- **FR-016**: Google sign-in MUST work on the production address, with the production callback
  registered and the sign-in consent screen published so the owner's account is not limited to a
  test-user list.
- **FR-017**: The runbook MUST put the owner's first sign-in immediately after the first deploy and
  before the address is shared, and MUST say how to check the claim event and how to recover if a
  stranger won (R-01, I.4).

Observability and cost

- **FR-018**: Production logs MUST be hosted and queryable, carrying the same request and
  operation timing events the app emits today, with sampling off, 30-day retention, and a daily
  ingestion limit that keeps usage inside the free allowance (D-12, VIII.4).
- **FR-019**: One committed query per existing time-bound success criterion MUST run against the
  hosted logs, and this feature's evidence MUST include their output (section 10 deferred item).
- **FR-020**: A spending alert MUST notify the owner at US$7 per month of metered spend and again
  at US$10 (VIII.1, D-18), scoped to this site's resource groups, not the whole subscription, so
  other spend in the subscription neither triggers it nor hides inside it. Every metered service
  in production that offers a hard limit MUST have one set inside the cap (VIII.4).
- **FR-021**: The design's cost table MUST be updated with verified unit prices for the chosen
  host and region, and the projected total MUST stay under the alert threshold (VIII.3).

Repeatability

- **FR-022**: Every production resource that has a management interface MUST be described in the
  repository and created from that description, previewed before it is applied, applied from the
  owner's machine (D-24), signed in as the owner. The Azure side of the description MUST be pinned
  to subscription `c28725a0-a61d-4a6c-999a-0a8980111fbf` and touch no resource outside this
  site's resource groups (the state store excepted only if it sits in its own site-only group).
  Steps with no management interface MUST be in a committed runbook.
- **FR-023**: A preview run against unchanged production MUST report zero changes.
- **FR-024**: Every push to any branch MUST be built and run the full test suite automatically.
  Only pushes to `main` deploy, and only after the full build and test suite pass on the exact
  commit deployed. The pipeline MUST NOT be able to create, delete or reconfigure any resource; it
  can only publish a build to the one existing production app. It MUST hold no long-lived
  credential where the owner's rights allow a federated sign-in; otherwise the one credential it
  holds MUST be limited to publishing to that app and covered by the rotation runbook.

Verification

- **FR-025**: The feature MUST end with a live walkthrough against production that repeats the
  existing read-only public checks (006, 010, 011 verify scripts) on the production address, plus
  the origin-refusal, forwarded-address and edge-cache checks of User Story 3.
- **FR-026**: The roadmap's coverage rows owned by hosting (D-01, D-02, D-03, D-05, D-12, D-16
  edge side, D-18, D-24, D-25, E-19, R-01, R-05 configuration, R-06 rotation runbook, PC.1–PC.3,
  PC.5–PC.9) MUST each be delivered or recorded as deferred with a reason.

### Key Entities

- **Production environment**: the host, datastore, two buckets, two hostnames, edge settings,
  log workspace and spending alerts that together serve the public site. Exactly one exists.
- **Infrastructure description**: the committed files that create and update the production
  environment, plus the state that records what exists.
- **Runbook**: the ordered owner-only steps that have no management interface (domain and account
  setup, identity console, first sign-in and claim check, credential rotation).
- **Production configuration**: the secret and non-secret values the app reads in production;
  checked at start-up against development names.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor outside the owner's network loads the home page, the galleries listing, a
  gallery and a photo page on the public address over HTTPS with no certificate warning; every
  check in the existing read-only public verify scripts passes against production.
- **SC-002**: 100% of at least 20 direct requests to the host that bypass the edge are refused.
- **SC-003**: For every public page type, the second anonymous request within the cache lifetime
  produces zero request events in the app's logs.
- **SC-004**: After the owner publishes a gallery, it appears on the public listing for an
  anonymous visitor within 60 seconds.
- **SC-005**: The claim event for the owner is queryable in the hosted logs within 5 minutes of
  the first sign-in, showing the owner's identity and their real client address.
- **SC-006**: Each prior time-bound success criterion that applies to public pages is reproduced
  from hosted-log queries alone, and its p95 is no worse than the local evidence by more than the
  network distance to the host.
- **SC-007**: A preview of the infrastructure description against production reports zero
  changes immediately after apply.
- **SC-008**: Projected monthly metered spend stays under US$7, and the alert and ingestion limit
  are in place before the first deploy.
- **SC-009**: No secret value appears in the repository, its history, build output, or any page
  or response served to a browser (checked by scanning for the real values, not their shape).

## Assumptions

- Planning inputs only, not requirements: the design record's on-hold decisions are the starting
  point. D-24 is OpenTofu run locally from `infra/` with four providers; D-25 is GitHub Actions in
  `zzacal/port` with OIDC to the host and tests on every push; D-12 is Application Insights; D-03
  is the edge IP allow-list plus Authenticated Origin Pulls. They are re-checked against whatever
  host Q1 picks, and change only through the design record (VII.2).
- The domain is `zacal.me` on Cloudflare in the same account as R2 (owner profile). The site's
  address is `port.zacal.me` (Clarification 2026-11-02). Observed 2026-11-02: `zacal.me`,
  `www.zacal.me`, `port.zacal.me` and `img.zacal.me` all resolve to `192.168.1.112`, a private
  home-network address, so a DNS-only wildcard record most likely exists; `img-dev.zacal.me`
  resolves through Cloudflare. Explicit `port` and `img` records override the wildcard for those
  two names only, so whatever else uses the wildcard keeps working.
- Accepted risk (owner, 2026-11-02): the subscription sits in the Alaska Airlines tenant, so the
  tenant's administrators can see the app, its settings and its secrets, and tenant policy (for
  example blocked app registrations or required tags) can limit what OpenTofu and the pipeline
  may do. This partly reverses the reason the site left Azure (001 decisions #34); images stay on
  R2 in the owner's Cloudflare account, so masters are not in the tenant. Reversal cost: low
  before launch, medium after (re-apply the description in another subscription and redeploy; no
  data lives in Azure except logs).
- Production content starts empty. Development content is never copied to production (III.10);
  the owner uploads through production `/admin`.
- Production bucket names default to `gallery-prod-private` and `gallery-prod-public`, matching
  the development naming; the image hostname defaults to `img.zacal.me` (D-19). Overturnable in
  clarify.
- The production Google OAuth client is a new client (or new redirect URIs on the existing one)
  in the same Google Cloud project; the consent screen moves out of Testing. Overturnable.
- The existing edge-purge provider gains a production page mode; its log-only and development
  modes stay for local runs and tests.
- Backups stay an accepted risk (R-02, E-13); no backup work is in scope.
- Owner-only steps (account creation, domain/DNS dashboard steps, the Google console, the first
  sign-in, the live walkthrough from outside the home network) happen after the build, one at a
  time, and are listed in the runbook and tasks as OWNER-ONLY, not as pre-plan gates.
- Severable seams (not literally named in "standing up production infrastructure", included
  because the roadmap gives them to hosting; each can be cut before planning):
  1. (Accepted by the owner, Clarification 2026-11-02; no longer severable.) The deployment
     pipeline (D-25, FR-024, User Story 5 scenario 3).
  2. Edge page purge and warm against production (FR-011) — owned by hosting per D-16; without it
     published changes wait up to a day.
  3. Committed hosted-log queries with evidence (FR-019) — a deferred design item; without it
     IV.4 is only provable locally.
  4. The production start-up check against development names (FR-015) — not in the design;
     added because a mis-pointed production would publish into development buckets.
