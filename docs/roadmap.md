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

R2 setup needed before 006 (owner, in the Cloudflare dashboard):

- [x] Dev buckets created: `gallery-dev-private` and `gallery-dev-public`. Production gets its
  own pair later. The old `port` bucket is unused and can be deleted.
- [x] Dev image hostname `img-dev.zacal.me` attached to `gallery-dev-public`.
  Leave r2.dev disabled on both buckets (III.9).
- [x] Create one API token with Object Read & Write, scoped to the two dev buckets only (III.9).
  Keep its keys in .NET user-secrets on your machine, never in the repo.
- [x] CORS on `gallery-dev-private` (owner, 2026-10-31, verified by terminal preflight):
  AllowedOrigins `https://localhost:5081` only, AllowedMethods `PUT` only, AllowedHeaders
  `content-type` only, MaxAge 3600. Preflight PUT from `https://localhost:5081` answers 204; GET,
  and any request from `http://localhost:5080`, answer 403. Presigned PUTs must sign no header
  except content-type (008 FR-044).

Storage tests run one suite against two targets. The emulator runs on every test run. The dev R2
buckets run on demand when the R2 keys are present in the environment, and must pass before any
storage change merges. That second run catches R2 differences an emulator accepts: streaming
payload signing, default SDK checksums, unsupported copy headers, and storage class. It also
checks bucket settings (r2.dev off, token scope). All R2-specific client settings live in one
place, so both targets exercise the same code.

## Order

### 006 Walking skeleton: public site, local

**Status: done, merged to main 2026-10-31 (558465e).** Evidence: `specs/006-public-site-local/evidence/`
(verify.sh 21/21 PASS, p95.sh SC-003/SC-004 PASS, browser.md, seed-run.txt, r2-tests.txt).
Changed in clarify: the seed uses the owner's photos from `dev-pictures/` through a committed
manifest rather than the 003 sample set. Not delivered: the forwarded-headers setup (PC.9). The
006 spec never picked it up, and it has no effect without a proxy in front of the app, so it moves
to the hosting feature (006 decisions row 30).

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

**Status: done, merged to main 2026-10-31 (0118c52).** Evidence:
`specs/007-owner-signin-claim/evidence/` (verify.sh 10/10 PASS, non-vacuity.sh 8/8 guards red on
their breaks, p95.sh SC-006 PASS on the 60-callback test log and the live run, live-walkthrough.md
with real Google). Not delivered: a live "cancel at Google" check, because Google's account
chooser has no cancel and scope `openid` shows no consent screen; the path is covered by an
automated test. Deferred: the sign-in page look (007 tasks D-001, needs a D-23 change). The
Upload, Library and Galleries links answer 404 for the owner until 008 and 009.

The first `/admin` route. Google sign-in, the atomic claim, the refusal path, a session cookie,
and antiforgery. AdminLayout renders an empty owner shell. This runs locally against a Google
OAuth client with a localhost redirect URI.

- D-04, D-06, the D-23 sign-in page, E-01, E-02, and PC.4.
- **Runbook first**: create the Google Cloud project and OAuth client (it has no API).
  - [x] Project and Web OAuth client created, consent screen in Testing with two test users
    (owner, 2026-10-31).
  - [x] Redirect URIs `https://localhost:5081/signin-oidc` and `http://localhost:5080/signin-oidc`.
  - [x] Client id and secret in user-secrets (`Authentication:Google:*`); dev certificate trusted.

### 008 Upload pipeline into the library

**Status: merged to main 2026-11-01 (e5a7d6a); unattended run;
defaults in `specs/008-upload-pipeline/decisions.md`).** Evidence:
`specs/008-upload-pipeline/evidence/` (verify.sh live walkthrough ALL PASS, p95.sh SC-006/007/008/010
PASS on the 40-photo log, non-vacuity.sh, browser tests for the worker path). Not delivered until
the owner's post-build live checks (008 tasks.md OWNER-ONLY). Done by the owner 2026-11-01: the
7-day lifecycle rule (O-01), the R2 test suite (O-03), staging empty after confirm on R2 (O-04),
and a JPEG live browser run (part of O-02). Still open in O-02: SC-009 browser timing (Chrome),
the HEIC/PNG refusals and the measured derivative sizes. Backburnered by the owner (2026-11-01):
the Safari macOS and iOS Safari runs, including iOS memory behaviour; D-09 stays as decided until
they run.

The browser derivative worker, presigned R2 intake, server validation, the batch lifecycle, and
the upload batch page. Confirmed images land in the library.

- D-08, D-09, D-10, D-11, L-01 to L-06, E-03 to E-11, and R-07.
- **Before `/speckit-plan`**: ~~spike the jSquash AVIF encode of a 20 MB JPEG on Safari, iOS, and
  Chrome (memory and time, E-05). Also build the GPS-injected AVIF and WebP fixtures and confirm
  that MetadataExtractor and the structure parsers both reject them. Measure the real ThumbHash
  and derivative sizes (section 10). A failed spike reopens D-09 or D-10 before planning.~~
  - Status: Spike skipped by the owner (008 decisions row 6). Deferred to the owner's post-build
    live tests (008 tasks.md, OWNER-ONLY O-02); the GPS fixtures and reader checks it named are
    built and tested inside 008 (FR-041). First Chrome data point: probe P4 (decisions row 36).

### 009 Library and gallery management

**Status: merged to main 2026-11-01 (9082469), with the owner review fixes (decisions rows 69-77);
unattended run; defaults in `specs/009-library-galleries/decisions.md`).** Evidence:
`specs/009-library-galleries/evidence/` (verify.sh ALL PASS, re-run after rebasing onto 008's
owner-check commits; full suite 1008/1008; non-vacuity.sh 14/14). Not delivered until the owner's
post-build checks (009 tasks.md OWNER-ONLY O-01..O-04): dev-zone purge token, the one-time
remove-seeded run on dev, the live browser walkthrough, and the R2 delete tests.

The library page (titles, captions, bulk actions, delete), the gallery list, and the gallery
builder. This includes visibility, ordering, covers, and purge plus warm on every public change.
Purge calls go to the dev zone for image URLs; page purge is exercised once hosting exists. The
seeded sample content is removed in this feature.

- L-07 to L-17, E-12, E-14 to E-18, R-03, and R-04.

### 010 Navigation improvements

**Status: merged to main 2026-11-02 (d338625); defaults in
`specs/010-navigation-improvements/decisions.md`.** Evidence: `specs/010-navigation-improvements/evidence/`
(verify.sh ALL PASS, p95.sh SC-006 ALL PASS with db_reads identical to the baseline,
non-vacuity.sh 6/6, snapshot-diff.sh ALL EQUAL, read-only live walkthrough ALL PASS; full suite
104 + 1066 passed, 1 skipped). Not delivered until the owner's checks (010 tasks.md OWNER-ONLY
O-01..O-03): quickstart Part C on the owner's watch, the phone check, and the prerender landing
in real Chrome (decisions row 38).

The photo page names its gallery and position and links back to the photo's tile, with 44 px tap
areas and Left/Right/Escape; listed galleries end with previous/next gallery links; the public
header drops its duplicate link; the library status links the first gallery. Amends D-17, D-21,
section 4 (Gallery `nav`) and L-07 to L-17 (P-NAV and counter purges).

### 011 Home page

**Status: done, merged to main 2026-11-02 (8e02c9d); defaults and
stated test changes in `specs/011-home-page/decisions.md`.** Evidence: `specs/011-home-page/evidence/`
(verify.sh ALL PASS live, 12 of 14 checks FAIL on the pre-011 build; p95.sh ALL PASS: `/` 0 reads
p95 0.13 ms, `/g` exactly 1 read; snapshot-diff.sh ALL EQUAL; non-vacuity.sh 7/7; design record
validate 0 failures; full suite 108 + 1107 passed, 1 skipped). O-01 and O-02 passed (owner, 2026-11-02): the
click on Galleries activates the prerendered `/g` (activationStart 228.7 ms; decisions row 25).

`/` is a home page whose only content is a large, centred "Galleries" link; the grid moved
unchanged to `/g` (`/g/` redirects there); every public breadcrumb starts at Home. Amends D-13,
D-15, D-16, D-20, D-21, D-23, the content model's `homeCards` row, the lifecycle "Pages and
caches" lines, E-16, and the II.2/II.6/IV.9 traceability rows.

### 012 Production infrastructure

**Status: live at https://port.zacal.me (merged 9c5a4ce, 2026-11-04).** O-01..O-07 done;
O-08 dropped (decisions #49); log-based timing skipped (decisions #50). Evidence: `specs/012-production-infrastructure/evidence/` (design record validate 0
failures in `design-validate.txt`). Decisions in `specs/012-production-infrastructure/decisions.md`.

The site goes live at `https://port.zacal.me` on the owner's hand-made App Service `portfolio-web`
(Linux B2, .NET 10, West US) behind Cloudflare. The origin accepts only Cloudflare: IP allow-list
plus the secret `X-Gallery-Origin` header, with `CF-Connecting-IP` trusted only behind that guard
(replaces Authenticated Origin Pulls). Data Protection keys persist under `/home`, Application
Insights through `Azure.Monitor.OpenTelemetry.AspNetCore` with sampling off, a production startup
guard against dev names, production page purge and warm, and GitHub Actions deploying `main` as
the managed identity `portfolio-deploy`. D-24 (OpenTofu) is withdrawn: production is made by hand
from the runbook `infra/README.md` and checked by the read-only `infra/check.sh`; Atlas is scripted
by `infra/atlas.sh`. No budget alert (constitution 6.0.0). Amends D-01, D-02, D-03, D-05, D-12,
D-16, D-18, D-24, D-25, E-19, R-01, R-06, section 8 and section 10.

Owner prerequisites (done):

- [x] App Service `portfolio-plan` (B2) and `portfolio-web` in resource group `portfolio`, West US;
  Application Insights `portfolio-appinsights` created (cap and retention are in O-02).
- [x] Deploy identity `portfolio-deploy` with federated credential subject
  `repo:zzacal/port:ref:refs/heads/main`, Website Contributor on `portfolio-web` only; GitHub
  variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` set.
- [x] Cloudflare steps A–G (2026-11-02), probed in `evidence/probe-cloudflare.txt`: `port.zacal.me`
  through Cloudflare, `asuid.port` TXT, http to https redirect, `/admin` not cached, `img.zacal.me`
  through the cache.
- [x] Production R2 token and purge token saved in `.r2-keys-prod-copy` (gitignored, mode 600).
- [x] Google OAuth client `gallery-prod`, redirect `https://port.zacal.me/signin-oidc`, consent
  screen in Testing with the owner as a test user.

Remaining OWNER-ONLY steps (012 tasks.md, after the build, one at a time):

- [ ] O-01 Atlas: `brew install mongodb-atlas-cli`, `atlas auth login`, `bash infra/atlas.sh
  <outbound IPs>`.
- [ ] O-02 Runbook steps 1, 6 and 7: app settings, App Insights cap and retention, access
  restrictions, every app setting including `Hosting__OriginSecret`; then delete
  `.r2-keys-prod-copy`.
- [ ] O-03 `bash infra/check.sh <default hostname>` with 0 FAIL.
- [ ] O-04 Merge 012 to main and push; the `deploy` job goes green.
- [ ] O-05 Claim immediately at `https://port.zacal.me/admin`, then `kql.sh claim` (R-01).
- [ ] O-06 Upload and publish a gallery, then `kql.sh edge`.
- [ ] O-07 `verify-prod.sh`, `drive-prod.sh`, then `kql.sh all`.
- [ ] O-08 Drift test: Always On off, `check.sh` shows `FAIL AZ-03`, then back on.

### 013 By Camera

**Status: live at https://port.zacal.me (merged 66759a2, 2026-11-05); full suite 122 + 1253 passed, 1 skipped; production browser check in `browser-prod.txt`; O-01 passed (owner).** Decisions, defaults and stated
test changes in `specs/013-by-camera/decisions.md`. Evidence: `specs/013-by-camera/evidence/`
(verify.sh ALL PASS (15 checks) live against a scratch copy of the dev data; all 4 new checks FAIL on the pre-013 build in
`verify-pre013-output.txt`; p95.sh ALL PASS: `/c` 1 read p95 0.35 ms vs `/g` 0.36, `/c/{slug}` 2 reads
0.80 vs 0.88, camera photo 2 reads 0.64 vs 0.66; non-vacuity.sh 9/9, one break per
[SAFETY] guard; design record validate 0 failures).

The home page gains a second link, "By Camera". `/c` lists every camera that took a visible photo
(most photos first), `/c/{slug}` shows that camera's photos newest first, and `/c/{slug}/{photo}`
steps through them with the camera breadcrumb and `N / total`. A photo counts only while it is in
at least one public, non-empty gallery, so private and library-only photos never appear, and a
private-only camera answers the same not-found page as an unknown one. Pages read the same number
of documents as the gallery pages (1 / 2 / 2) and are purged and warmed by every owner action that
changes them. Adds D-26 (derived `cameras` collection, written only by the site projection) and
amends D-21, the content model, L-07/08/09/11/12/17 and the II.2/IV.9 traceability rows.

OWNER-ONLY (after merge and deploy, not gates):

- [x] O-01 Look over `/c`, one camera page and stepping through its photos on the live site, at
  desktop and phone widths.

### 014 Galleries combobox in the photo panel

**Status: live at https://port.zacal.me (merged 4f2d39e, 2026-11-06; deploy green); full suite 122 + 1328 passed, 1 skipped.** Decisions, defaults and stated test
changes in `specs/014-gallery-combobox/decisions.md`. Evidence: `specs/014-gallery-combobox/evidence/`
(p95.sh ALL PASS: memberships p95 8.83 ms, save POST p95 10.43 ms, n=100 each; non-vacuity.sh 8/8,
one break per [SAFETY] guard and Match rule; design record validate 0 failures).

In the library's Edit panel, the read-only "In galleries" list becomes a field: type part of a
gallery title, pick it (or "+ the typed text" to create a new private gallery), remove a gallery
with its ×, and Save applies it all at once. Picking a public gallery warns that visitors will see
the photo after saving. The field is a reusable `TokenCombobox` molecule with its own `/ui` page.
Amends D-21 (molecule list and extraction rule) and the Library line of the admin design.

OWNER-ONLY (after merge and deploy, not gates):

- [ ] O-01 On port.zacal.me, open a photo's Edit panel, add it to an existing gallery and to a new
  one, then remove one token. Look over `/ui/molecules/token-combobox`.

### 015 Icon controls on library photos

**Status: live at https://port.zacal.me (merged 12a812c, 2026-11-06; deploy green); full suite 122 + 1455 passed, 1 skipped.** Decisions, defaults and stated test
changes in `specs/015-tile-icon-controls/decisions.md`. Evidence: `specs/015-tile-icon-controls/evidence/`
(non-vacuity.sh 10/10; baseline-diff.py ALL EQUAL; design record validate 0 failures).
**SC-005 missed, parked in the backlog** ([#3](https://github.com/zzacal/port/issues/3)): library p95
22.4 ms against a limit of 18.7 ms, builder 11.7 ms against 8.7 ms, with the same database reads.
Research (`evidence/opt/`, decisions row 44): the cost is the extra Icon component inside each
IconButton. Removing it brings both pages under the limit with byte-identical output.

Library and gallery-builder tiles show the title on the photo's bottom edge, in a dark band. Under
each photo is one row of icons. Library: gallery (folder-plus/folder/folders, labelled with the count),
view (new tab), edit. Builder: move, cover star, remove, view. Edit and the gallery icon toggle the side
panel, with one photo active at a time, and work without JS. 8 Lucide 1.70.0 icons (ISC) are copied in
as inline SVG. `Icon`, `IconButton` and `PhotoActions` each have a `/ui` page; `MembershipStatus` is
removed. Amends D-21 and D-23.

OWNER-ONLY (after merge and deploy, not gates):

- [ ] O-01 ([#4](https://github.com/zzacal/port/issues/4)) Review `/ui/atoms/icon`, `/ui/atoms/icon-button` and `/ui/molecules/photo-actions`. Then on
  port.zacal.me toggle edit on two photos, open a photo through its folder icon, and pin a cover in a
  builder.

## Not a feature

- **Cost cap (D-18, E-19)**: no budget alert (constitution 6.0.0 withdrew the $7 alert). The
  $10 metered cap is a governance limit; the Application Insights daily cap (0.15 GB, set by 012)
  is the only hard limit. The B2 plan is fixed-price; R2 stays under $1/month and the owner checks
  it on the Cloudflare billing page.
- **Backups (E-13, R-02)**: an accepted risk with no feature.
- **R-05 (log retention)**: an accepted risk; 30-day retention configured by 012.

## Coverage

| Entry | Feature |
|---|---|
| D-07, D-13, D-14, D-15, D-17, D-19, D-20 | 006 |
| D-16 | 006 (headers), 009 (purge and warm), 012 (edge cache rules, production page purge) |
| D-21, D-22 | built (003, 002); followed by all |
| D-23 | 007 (sign-in, shell), 008 (upload batch), 009 (library, gallery list, builder) |
| D-04, D-06 | 007 |
| D-08, D-09, D-10, D-11 | 008 |
| D-01, D-02, D-03, D-05, D-12, D-18, D-24, D-25 | 012 |
| D-26 | 013 |
| L-01 to L-06 | 008 |
| L-07 to L-17 | 009 |
| E-01, E-02 | 007 |
| E-03 to E-11 | 008 |
| E-12, E-14, E-15, E-16, E-17, E-18 | 009 (E-16 listing and 404 rules in 006; publish refusal in 009, II.6) |
| E-13 | accepted risk (R-02) |
| E-19 | 012 |
| R-01 | 012 (runbook step 9, first public deploy of `/admin`) |
| R-02, R-05 | accepted; R-05 configured by 012 |
| R-03, R-04 | 009 |
| R-06 | 006 (token scope and storage), 012 (rotation, runbook step 10) |
| R-07 | 008 |
| III.9 | 006 (credential, buckets), 008 (presigned uploads) |
| PC.9 | 012 (moved from 006: no effect without a proxy; 006 decisions row 30) |
| PC.4 | 007; 012 (production Google client `gallery-prod`) |
| PC.1, PC.2, PC.3, PC.5, PC.6, PC.7, PC.8 | 012 |
