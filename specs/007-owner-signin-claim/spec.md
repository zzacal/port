# Feature Specification: Owner Sign-In and Claim

**Feature Branch**: `007-owner-signin-claim`

**Created**: 2026-10-31

**Status**: Draft

**Input**: User description: "007" (docs/roadmap.md, "007 Owner sign-in and claim"): the first
`/admin` route. Google sign-in, the atomic claim, the refusal path, a session cookie, and
antiforgery. AdminLayout renders an empty owner shell. Runs locally against a Google OAuth client
with a localhost redirect URI. Owns D-04, D-06, the D-23 sign-in page, E-01, E-02, and PC.4.

> **AMENDED by feature 011-home-page (constitution 4.5.0, 2026-11-02)**: `/` is now a home page
> that links to the galleries, and the gallery grid moved to `/g` as the galleries listing. Where
> this spec says "home page" or "home grid" for the grid of gallery cards, read "galleries
> listing (`/g`)"; its requirements apply there unchanged. "Home order" keeps its name. FR-002 is annotated in place.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The owner claims an unclaimed site (Priority: P1)

The owner opens `/admin` on a site nobody has claimed yet. They see the sign-in card over the home
page's first cover, telling them that the first Google account to sign in becomes the owner and
that this can't be changed later from the web. They sign in with Google, become the owner, and
land on the empty owner shell.

**Why this priority**: Nothing in the admin can exist until there is an owner. Upload (008) and
management (009) both sit behind this.

**Independent Test**: Start from a datastore with no owner, open `/admin`, sign in with a Google
account, and confirm the shell appears, the stored owner is that account's subject identifier, and
a claim event was logged with identity, client IP, and time.

**Acceptance Scenarios**:

1. **Given** an unclaimed site, **When** anyone opens `/admin` without a session, **Then** they
   see the claim version of the sign-in card, and no admin content.
2. **Given** an unclaimed site, **When** a person completes Google sign-in from that card,
   **Then** their Google account becomes the owner, they receive an owner session, and they land on
   the owner shell.
3. **Given** a successful claim, **When** the stored owner record is inspected, **Then** it holds
   the account's immutable subject identifier (not its email address), the claim time, and the
   client IP.
4. **Given** a successful claim, **When** the logs are read, **Then** exactly one claim event
   appears, carrying the subject identifier, client IP, and timestamp.

---

### User Story 2 - The owner signs in again later (Priority: P1)

The owner returns to `/admin` on a claimed site. If their session is still alive they go straight
to the shell. If not, they see the ordinary sign-in card, sign in with the same Google account, and
reach the shell. While they keep using the admin, the session stays alive.

**Why this priority**: Every later admin feature depends on the owner getting back in reliably.

**Independent Test**: With a claimed site, sign in as the owner, close and reopen the browser,
confirm the session still holds; then sign out and confirm `/admin` shows the sign-in card again.

**Acceptance Scenarios**:

1. **Given** a claimed site and no session, **When** someone opens `/admin`, **Then** they see the
   ordinary sign-in card (not the claim wording).
2. **Given** a claimed site, **When** the owner's Google account completes sign-in, **Then** an
   owner session is issued and the shell appears. No claim event is logged and the stored owner
   record is unchanged.
3. **Given** an owner session in use, **When** the owner keeps using admin pages within 7 days of
   their last request, **Then** the session keeps renewing and they are not asked to sign in.
4. **Given** an owner session unused for more than 7 days, **When** the owner opens `/admin`,
   **Then** they are asked to sign in again.
5. **Given** an owner session, **When** the owner signs out, **Then** the session ends and the
   next `/admin` request shows the sign-in card.

---

### User Story 3 - Anyone else is refused (Priority: P1)

A person who is not the owner signs in with Google on a claimed site. They are told plainly that
this account doesn't own the site, nothing is saved, and they get no session. They can go back to
the gallery or try a different account. The same refusal applies to the loser of a race for an
unclaimed site.

**Why this priority**: This is the site's only access control (Constitution I.3). A refusal path
that leaks a session or admin content is a full takeover.

**Independent Test**: Claim the site with one account, sign in with a second account, and confirm
the refused card, no session, no admin content on any `/admin` route, and an unchanged owner
record. Separately, run two claims at once against an unclaimed site and confirm exactly one wins.

**Acceptance Scenarios**:

1. **Given** a claimed site, **When** a different Google account completes sign-in, **Then** the
   refused card appears ("Not the owner"), no session is issued, and the owner record is unchanged.
2. **Given** a refused visitor, **When** they open any `/admin` address, **Then** they see the
   sign-in card and no admin content.
3. **Given** a refused visitor, **When** they choose "Try another account", **Then** Google lets
   them pick a different account instead of silently reusing the refused one.
4. **Given** an unclaimed site, **When** two different accounts complete sign-in at the same
   moment, **Then** exactly one becomes the owner and receives a session; the other is shown the
   refused card and receives no session (E-01).
5. **Given** any refusal, **When** the logs are read, **Then** a refusal event appears with the
   refused subject identifier, client IP, and reason (not the owner / lost the race), and no claim
   event appears for it.

---

### User Story 4 - Admin is closed to anyone without the owner session (Priority: P1)

Every `/admin` address other than the sign-in flow itself, and every admin action, is unreachable
without the owner session. Admin pages are never cached for or shown to anyone else, and the
public pages are unaffected by whether the owner is signed in.

**Why this priority**: This is the guarantee later features build on; 008 and 009 add pages under
`/admin` and rely on them being closed by default.

**Independent Test**: Without a session, request every `/admin` route and every admin action and
confirm none returns admin content or changes anything; with the owner session, confirm the public
pages render exactly the same markup as anonymously.

**Acceptance Scenarios**:

1. **Given** no owner session, **When** any `/admin` page other than sign-in is requested, **Then**
   the response is the sign-in card (or a redirect to it), never admin content.
2. **Given** no owner session, **When** any admin state-changing request is sent, **Then** it is
   refused and nothing changes.
3. **Given** an owner session, **When** a state-changing admin request arrives without a valid
   anti-forgery token (for example from another site), **Then** it is refused and nothing changes.
4. **Given** any admin response, **When** its caching headers are inspected, **Then** it is marked
   as not storable by shared caches.
5. **Given** an owner session, **When** the owner opens a public page, **Then** the public markup
   is identical to what an anonymous visitor receives, so no owner markup can reach a cached public
   page.

---

### User Story 5 - The component catalogue becomes owner-only (Priority: P3)

The unlinked component catalogue (`/ui/`), which until now existed only on the development machine,
is reachable only by the signed-in owner, as the design record already specifies for the moment
owner sign-in exists (D-21).

**Why this priority**: The design names this feature as the point the catalogue changes hands; it
is small and severable.

**Independent Test**: Without a session, request `/ui/` and a component page and confirm no
catalogue content; with the owner session, confirm both render.

**Acceptance Scenarios**:

1. **Given** no owner session, **When** `/ui/` or any catalogue page is requested, **Then** no
   catalogue content is returned.
2. **Given** an owner session, **When** `/ui/` is requested, **Then** the catalogue renders as
   before.

---

### Edge Cases

- **Site has no content yet**: an unclaimed site with no galleries can still be claimed; the
  sign-in card renders without a cover photo.
- **The owner record does not exist at all** (fresh datastore, never seeded): treated as
  unclaimed; the claim creates it, and a race on creation still has exactly one winner.
- **Re-running the dev seed after a claim**: the claim survives; seeding never resets the owner.
  (Retired by feature 009: the seed command, manifest and scripts no longer exist; see specs/009-library-galleries.)
- **Stranger claims first (E-02)**: they become owner. The claim event (identity, IP, time) is the
  detection; the real owner's sign-in is refused. Recovery is out of band only: the operator clears
  the owner fields directly in the datastore, and the runbook says how.
- **Owner cancels at Google or Google returns an error**: the visitor returns to the sign-in card
  with a short message that sign-in didn't complete; no session, no claim, nothing saved; a
  sign-in failure event is logged.
- **Tampered, replayed, or expired sign-in response**: rejected; no session, no claim.
- **The owner's Google account changes its email address**: they remain the owner, because
  ownership is bound to the subject identifier.
- **A refused visitor still holds an old owner session cookie from another account**: impossible
  by design for non-owners (they never got one); a forged or stale cookie that doesn't validate is
  ignored and treated as no session.
- **Signed-in owner opens `/admin` sign-in again**: goes straight to the shell.
- **Admin pages linked from the owner bar that do not exist yet** (Upload, Library, Galleries):
  they arrive with 008 and 009; until then they answer as not found for the owner and with the
  sign-in card for everyone else.
- **Sign-out request without a valid anti-forgery token**: refused; the session stays (prevents a
  third-party site from signing the owner out).

## Requirements *(mandatory)*

### Functional Requirements

Sign-in and claim

- **FR-001**: `/admin` MUST show the sign-in card to anyone without the owner session. On an
  unclaimed site the card MUST use the claim wording (first sign-in becomes the owner; can't be
  changed later from the web); on a claimed site it MUST use the ordinary sign-in wording.
- **FR-002**: The sign-in card MUST sit over the home page's first cover photo when one exists,
  and render without a photo otherwise (D-23). **AMENDED by 011**: "home page" here means the galleries listing at `/g`.
- **FR-003**: Sign-in MUST use Google as the only identity provider. No passwords, no other
  providers.
- **FR-004**: The first completed Google sign-in on an unclaimed site MUST claim it in a single
  atomic step: when several sign-ins race, exactly one becomes the owner and every other one is
  refused (Constitution I.1, E-01).
- **FR-005**: Ownership MUST be recorded by the Google account's immutable subject identifier,
  never by email address (I.2). The owner record MUST also store the claim time and client IP.
- **FR-006**: Every claim MUST emit exactly one claim log event with subject identifier, client
  IP, and timestamp (I.5).
- **FR-007**: After the claim, every other identity MUST be refused: shown the refused card, given
  no session, and nothing about them stored (I.3).
- **FR-008**: No web request MUST be able to change, transfer, or reset the owner once claimed
  (I.4). Recovery MUST be an out-of-band operator action, documented in a runbook.
- **FR-009**: The refused card MUST offer a way back to the gallery and a way to sign in with a
  different Google account, where Google asks which account to use.
- **FR-010**: A cancelled, failed, tampered, replayed, or expired sign-in MUST return the visitor to
  the sign-in card with a short message, issue no session, and change nothing.

Session

- **FR-011**: A successful owner sign-in MUST issue a session that lasts 7 days from the last
  request and renews while in use.
- **FR-012**: The session credential MUST be unreadable by page scripts, sent only over secure
  connections, bound to this site's exact host, and not sent on cross-site sub-requests.
- **FR-013**: The owner MUST be able to sign out from the owner shell; signing out ends the
  session immediately.
- **FR-014**: Every request that presents a session MUST be checked against the current owner
  record's subject identifier; a session for any other subject MUST be treated as no session.

Access control and forgery protection

- **FR-015**: Every `/admin` route except the sign-in flow, and every admin action, MUST require
  the owner session. Without it, pages MUST show (or redirect to) the sign-in card and actions MUST
  be refused with no change.
- **FR-016**: Every admin state-changing request, including sign-in start and sign-out, MUST carry
  a valid anti-forgery token and MUST be refused without one (I.6). The mechanism MUST cover both
  form posts and the script-sent requests later admin features will add (D-06).
- **FR-017**: Admin responses MUST be marked not storable by shared caches and not indexable.
- **FR-018**: Public pages MUST render the same markup with or without the owner session, and
  public caching behaviour MUST be unchanged by this feature (IV.18, IV.20, D-23).

Owner shell

- **FR-019**: A signed-in owner MUST land on an owner shell at `/admin`: the admin layout with the
  owner bar (brand, admin navigation, a link to view the public site) and a sign-out control, and
  an otherwise empty working area stating that upload and management arrive in later features.
- **FR-020**: The owner shell MUST add no scripts to public pages and MUST work without scripting
  for sign-in, sign-out, and viewing the shell.

Component catalogue

- **FR-021**: The `/ui/` catalogue MUST be reachable only with the owner session; without it, no
  catalogue content is returned (D-21).

Observability

- **FR-022**: Every admin request MUST emit the standard request timing event (IV.1).
- **FR-023**: Sign-in outcomes MUST each emit a named structured event: claimed, signed in,
  refused (not the owner / lost the race), failed, and signed out, each carrying subject identifier
  when known, client IP, and outcome. The owner-record read and the claim write MUST emit named
  timing events with duration and outcome (IV.2).
- **FR-024**: The client IP recorded in the owner record and events MUST be the connecting
  client's address as the app sees it locally; reading it from the edge's forwarded header arrives
  with the hosting feature (PC.9).

Evidence

- **FR-025**: An automated test MUST prove that two concurrent claims against the real datastore
  produce exactly one owner and one refusal.
- **FR-026**: Automated tests MUST prove that, without the owner session, no `/admin` route or
  `/ui/` page returns admin or catalogue content and no admin action changes anything; and that a
  state-changing request without a valid anti-forgery token is refused even with the owner session.
- **FR-027**: A runbook MUST cover creating the Google sign-in client for local development
  (localhost redirect address, where its secret is kept outside source control) and the
  out-of-band owner reset.

### Key Entities

- **Owner record**: the single site record. Holds the owner's subject identifier (empty means
  unclaimed), claim time, and claim client IP. Written once by the claim, never by any web request
  after that. Already exists with these fields empty; the dev seed creates it without touching
  them.
- **Owner session**: proof that the current browser signed in as the owner. Carries the subject
  identifier, renews on use, expires 7 days after last use, ends on sign-out.
- **Sign-in event**: a log record of one sign-in outcome (claimed, signed in, refused, failed,
  signed out) with subject identifier when known, client IP, time, and reason.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Starting from an unclaimed site, the owner reaches the owner shell in one sign-in
  with no other setup steps in the app.
- **SC-002**: Across 50 concurrent claim attempts by distinct accounts against an unclaimed site,
  exactly 1 succeeds and 49 are refused, with exactly 1 claim event logged.
- **SC-003**: 100% of `/admin` and `/ui/` routes, requested without the owner session, return no
  admin or catalogue content; 100% of admin actions without the session or without a valid
  anti-forgery token change nothing.
- **SC-004**: A non-owner sign-in leaves zero trace in stored data and receives zero session
  credentials.
- **SC-005**: An owner session used at least once every 7 days never asks for sign-in again; one
  idle for more than 7 days always does.
- **SC-006**: Checking and recording ownership during a sign-in (owner-record read plus claim
  write) takes under 50 ms at p95, and the whole return-from-Google step until the shell is shown
  takes under 1 second at p95, both verified from the logged timing events alone.
- **SC-007**: Public page markup is byte-identical with and without the owner session.

## Assumptions

- Planning inputs only (from the design record, not requirements): Google OIDC through the
  framework's built-in handler; a host-prefixed cookie (Secure, HttpOnly, SameSite=Lax) with
  7-day sliding expiry; the claim as one conditional update on the `site` document; the built-in
  anti-forgery service for forms plus a header token for script-sent requests (D-04, D-06; the
  plan amends D-06's mechanism to explicit validation, research R-04); the
  `SignInCard`, `OwnerBar`, and `AdminLayout` components built in 005; structured JSON console
  logs as in 006. The app stays .NET.
- The existing `SignInCard` already posts sign-in to `/admin/signin` and links "Try another
  account" to it; its wording is the D-23 design and is reused as is. The refused-state message for
  a lost race uses the same "Not the owner" card.
- Runs locally only. A host-prefixed secure cookie needs HTTPS, so local development uses the
  framework's development certificate on localhost. The Google client's redirect address is a
  localhost address; the production client arrives with hosting.
- Client IP comes from the direct connection locally. Reading `CF-Connecting-IP` (PC.9) and the
  unclaimed-window risk on first public deploy (R-01) belong to the hosting feature, per the
  roadmap.
- No claim token (D-04 rejected it). The unclaimed window is not exposed while the site runs only
  on the owner's machine.
- Owner reset is a documented direct datastore change, not a command shipped in the app. A reset
  command is cuttable scope that can be added later if wanted.
- The Google client secret lives in local user-secrets, never in the repo (same handling as the R2
  keys).
- Sign-out, the `/ui/` owner-only gate (US-5, FR-021), and the runbook (FR-027) are not named in
  the roadmap's one-line summary. They are included because a session without sign-out can't be
  ended, D-21 assigns the catalogue gate to this moment, and the roadmap says "runbook first".
  Each is severable (see checklist notes).
- The owner bar's links to Upload, Library, and Galleries point at routes that 008 and 009 create;
  they stay as built in 005 and answer not found until then.
- Owner prerequisite before implement (done 2026-10-31): a Google Cloud project with an OAuth
  client (web application type, consent screen External in Testing, the owner's account plus a
  second account as test users). Registered redirect addresses:
  `https://localhost:5081/signin-oidc` and `http://localhost:5080/signin-oidc`; the plan uses
  these exactly. Client id and secret are in user-secrets as `Authentication:Google:ClientId` and
  `Authentication:Google:ClientSecret`. The development certificate is trusted. No Google API is
  enabled.
