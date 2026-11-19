# Feature Specification: Local Sign-In Without Google

**Feature Branch**: `018-local-dev-signin`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "a way for local stack to be logged in without a real auth"

## Clarifications

### Session 2026-11-18

- Q: How is the local sign-in switched on, and who may use it? → A: By a password the person
  starting the app sets in an environment variable; the sign-in form asks for it. Unset means no
  local sign-in. The password is the gate, so the button may be visible to anyone who can reach
  the dev stack, and the "request from the same machine" condition is dropped (it also has to work
  for a self-contained CI browser suite). The Development-environment and dev-marked data
  conditions stay, and a production start refuses to run when the variable is set.
- Q: On a dev stack whose site has no owner yet, should the local sign-in (with the right
  password) claim the site? → A: B. It claims the site for a fixed local identity (`local-owner`)
  and signs in, so a self-contained CI browser suite can start from an empty database. A later
  Google sign-in on that database is refused (claim-once still holds).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in locally with one click (Priority: P1)

The owner (or an automated browser suite) runs the site's development stack, on their own machine
or on a CI runner, and set a local sign-in password when starting it. Instead of a round trip to
Google (account chooser, network, a registered callback address), the sign-in page offers a local
sign-in: they enter that password and are the site's owner straight away. They land on the admin page they
asked for, exactly as after a Google sign-in, with the same session length and the same owner-only
pages and protections.

**Why this priority**: It is the whole request. Every local review of an owner page (and every
automated browser pass against the local stack) currently needs a working Google sign-in first.

**Independent Test**: Start the dev stack with a local sign-in password set, open
`/admin/library` signed out, enter the password in the local sign-in, and confirm the library
opens as the owner with no request to Google.

**Acceptance Scenarios**:

1. **Given** the local stack with development data and an owner already claimed (by Google or
   locally), **When** the
   owner opens `/admin/signin` and submits the local sign-in with the configured password,
   **Then** they are signed in as that owner and land on `/admin` (or the page they were sent from), with no request to Google.
2. **Given** a local sign-in session, **When** the owner submits any admin form, **Then** it
   behaves exactly as with a Google session: the same forgery protection, results and timing
   events.
3. **Given** a local sign-in session, **When** the owner signs out, **Then** the session ends as
   it does for a Google session.
4. **Given** the local stack, **When** the sign-in page renders, **Then** the Google sign-in stays
   available beside the local one (when Google is configured), so the real flow can still be
   tried locally.
5. **Given** a dev stack on an empty database (no owner yet), **When** the local sign-in is
   submitted with the configured password, **Then** the site is claimed for `local-owner`, the
   claim is logged as local, and the visitor lands on the admin page signed in.
6. **Given** the local stack, **When** someone submits the local sign-in with a wrong or empty
   password, **Then** no session is created and the page says the password was not accepted,
   without revealing anything about the configured one.

---

### User Story 2 - It can never exist outside the development stack (Priority: P1)

The local sign-in is a deliberate hole in "only Google proves who the owner is", so it must be
impossible to reach anywhere else: not on the production site, and not on any host pointed at
production data or storage, whatever its settings say.

**Why this priority**: A sign-in without credentials on the live site would hand the gallery to
anyone. This story is the condition for allowing Story 1 at all.

**Independent Test**: Start the app in each non-qualifying shape (production environment;
development environment with production-like data or storage names; no password set), each with
the password variable set where the shape allows it, and confirm the local sign-in is absent from
the page and its address answers as if it did not exist; and confirm a production start with the
password variable set refuses to start.

**Acceptance Scenarios**:

1. **Given** the app running in any environment other than development, **When** anyone requests
   the sign-in page or posts to the local sign-in address, **Then** no local sign-in is offered and
   the address answers not found, with no session created.
2. **Given** the development environment but a datastore or storage that is not marked as
   development, **When** the same requests are made, **Then** the same refusal happens.
3. **Given** the dev stack started without a local sign-in password, **When** the sign-in page
   renders or the local sign-in address is posted to, **Then** no local sign-in is offered and the
   address answers not found.
4. **Given** a production configuration with the local sign-in password variable set, **When**
   the app starts, **Then** it refuses to start and names the variable (never its value); without
   the variable, the existing production configuration check passes or fails exactly as today.

---

### Edge Cases

- **Unclaimed development site** (fresh development data, no owner yet, e.g. a CI run): the local
  sign-in with the right password claims the site for `local-owner` and signs in (FR-007). The
  Google claim stays offered beside it; whichever is used first owns that database for good.
- **A locally claimed database later used with Google**: the Google sign-in is refused, as for any
  second identity. To go back to a Google owner, reset the development data (out-of-band, as the
  constitution's recovery rule already says).
- **Session minted locally, then the claimed owner changes** (the development data is reset and
  claimed by another account): the session is rejected on the next request, as any session whose
  owner no longer matches is today.
- **Local stack started without Google configured**: the local sign-in still works (it is the
  point of the feature); the Google button is absent, as today.
- **Password variable set but too short** (under 16 characters): treated as unset, so no local
  sign-in, and startup logs a warning naming the variable and the minimum length (never the value).
- **Repeated wrong passwords**: each is refused and logged; no lockout (a dev stack has one user).
- **A local session cookie copied to another host**: it is useless there, because session cookies
  are only accepted by the instance whose keys signed them, and the owner check on every request
  still applies.
- **The owner's automation browser, or a CI browser suite, pointed at a dev stack**: it signs in
  through the same local sign-in with the password its runner set; no special path for tools.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On the development stack, when a local sign-in password is configured, the sign-in
  page MUST offer a local sign-in (a password field and a button) that, given that password, signs
  the visitor in as the site's owner without contacting any identity provider: as the
  already-claimed owner when there is one, otherwise by claiming the site (FR-007).
- **FR-002**: A local sign-in MUST produce the same owner session as a Google sign-in: the same
  identity (the claimed owner's subject), the same lifetime and renewal, the same per-request owner
  check, the same sign-out, and the same forgery protection on every admin form.
- **FR-003**: After a local sign-in the owner MUST land where a Google sign-in would have taken
  them.
- **FR-004**: The local sign-in MUST be available only when ALL of these hold: the app runs in the
  development environment; the datastore and the storage are the ones marked as development (the
  same markers the existing development-only maintenance command requires); and a local sign-in
  password of at least 16 characters is configured through the environment at startup. If any one
  fails, the sign-in page MUST NOT offer it and its address MUST answer not found without creating
  a session. It MUST NOT depend on where the request comes from.
- **FR-005**: The password setting is the only on-switch, and it MUST NOT override the environment
  and data conditions of FR-004: with the setting present on a non-qualifying host, the local
  sign-in stays absent.
- **FR-006**: The production configuration check MUST also refuse a production start when the
  local sign-in password variable is present (naming the variable, never its value), and otherwise
  behave exactly as today. An automated check MUST prove that a production-shaped host offers no
  local sign-in and answers not found at its address.
- **FR-007**: On an unclaimed development site, a local sign-in with the right password MUST claim
  the site for one fixed local identity (`local-owner`) and sign in as it. The claim MUST go
  through the same atomic claim-once rule as a Google claim (a racing Google or local claim: exactly
  one wins), and MUST be logged as a claim event naming it as local. Once claimed, by either route,
  the claim-once rule is unchanged: a Google sign-in on a locally claimed site is refused, and the
  local sign-in on a Google-claimed site signs in as the Google owner.
- **FR-008**: The local sign-in MUST be a deliberate action (a form submission with the password,
  protected against forgery), never automatic on page load, so a signed-out state can still be
  seen and tested locally. A wrong or empty password MUST create no session and show a generic
  "not accepted" message.
- **FR-014**: The configured password MUST never appear in any page, response, log line or error;
  the submitted password MUST be compared in constant time and never logged.
- **FR-009**: Every local sign-in MUST be logged as a distinct sign-in event, naming it as local,
  with the owner identity, client address and time, so it is never mistaken for a Google sign-in.
- **FR-010**: Each refused local sign-in attempt (wrong or empty password, or FR-004 failing at
  request time) MUST be logged with the reason and client address, never with the submitted or
  configured password.
- **FR-011**: The Google sign-in, claim, refusal and sign-out behaviour MUST be unchanged; existing
  sign-in tests MUST pass unmodified.
- **FR-012** *(blocking, before planning; amendment DONE: constitution 6.2.0, rule I.7)*: The constitution's ownership rules (I.1 "ownership is
  established by Google sign-in", I.3 "every other identity is refused and gets no session") MUST
  be amended with a narrow development-only exemption that states the FR-004 conditions (including
  the password), the local claim of FR-007 (a dev-only identity besides Google, under the same
  atomic claim-once rule), and the guards of FR-005/FR-006/FR-014, before `/speckit-plan`.
- **FR-013**: The design record's identity decision MUST be amended to describe the local sign-in
  and its conditions (design record is the source of truth).

### Key Entities

- **Local sign-in**: an owner sign-in performed with a startup-configured password instead of an
  identity provider, available only on the development stack. It produces an ordinary owner session
  for the subject stored as the site's owner; on an unclaimed site it first claims the site for the
  fixed local identity `local-owner`. It never changes an existing owner.
- **Development target**: the combination that qualifies a running app for the local sign-in: the
  development environment, development-marked datastore and storage, and a configured local
  sign-in password.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a dev stack with a password set, a signed-out owner reaches `/admin/library` as
  the owner by entering the password once on the sign-in page, in under 2 seconds, with zero
  requests to any identity provider.
- **SC-002**: In 100% of the automated checks across every non-qualifying shape (non-development
  environment, non-development datastore, non-development storage, no or too-short password), the
  local sign-in is absent from the page, its address answers not found, and no session is created;
  a wrong password creates no session.
- **SC-003**: Every existing sign-in, claim, refusal and owner-access test passes unmodified.
- **SC-006**: A browser suite starting from an empty dev database with only the password reaches
  an owner page in one sign-in, in 100% of runs.
- **SC-004**: Every local sign-in and every refused attempt is identifiable from the log alone
  (distinct event name, outcome, address), and no page, response or log line contains the
  configured password.
- **SC-005**: For each guard condition, a deliberate break that removes it turns at least one
  automated check red.

## Assumptions

- Current surface (read 2026-11-18 on main at 945bcd6): owner sign-in is Google only, through
  `/admin/signin` (GET page, POST starts the Google flow), with the per-request owner check against
  the stored claimed subject (`src/Gallery.Web/Admin/Auth/AuthSetup.cs`). Google is registered only
  when configured. The production configuration check is `ProductionGuard` (runs when the host
  target is production); the existing development-target check is `DevTargetGuard`
  (Development environment, `_dev` database suffix, `gallery-dev-` bucket prefix), used by the
  removal command. The local launch profile binds to localhost only (5080/5081); with the password
  gate this no longer matters to the feature.
- The owner's development database is already claimed by their real Google account (checked
  2026-11-18: the site document carries a claim), so there the local sign-in signs in as that
  Google owner and never claims. Claiming for `local-owner` only happens on empty databases (CI
  runs, fresh local resets).
- Default (overturnable in clarify): a visible button on the existing sign-in page, used on
  purpose (FR-008), rather than signing in automatically on every request. Automatic sign-in would
  make signed-out pages impossible to see locally.
- Where the password lives (planning input): on the owner's machine in user-secrets or the
  launch environment, never committed; on CI as a value the workflow generates per run (or a
  repository secret) and hands to both the app and the browser suite.
- Tests already mint owner sessions directly in their own fixtures; this feature does not change
  them. The automation browser against the local stack is one consumer of the new sign-in.
- Accepted risk (constitution I): anyone who knows the local password and can reach a dev stack
  can sign in to it as the owner. A dev stack holds only development data and its own dev
  credentials, and whoever set the password started the process. Production is excluded by FR-004
  and FR-006, not by the password. Reversal: delete the feature; no stored data depends on it.
- Planning inputs only: .NET 10 minimal APIs, the existing cookie scheme and Razor sign-in page;
  the sign-in card is a catalogue component, so a new state may move its `/ui` page (plan
  decision).
- Out of scope: any production or staging test login; multiple local identities; changing how
  tests mint sessions.
