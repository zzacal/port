# Feature Specification: Local Sign-In Without Google

**Feature Branch**: `018-local-dev-signin`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "a way for local stack to be logged in without a real auth"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in locally with one click (Priority: P1)

The owner runs the site on their own machine against the development data. Instead of a round
trip to Google (account chooser, network, a registered callback address), the sign-in page offers
a local sign-in that makes them the site's owner straight away. They land on the admin page they
asked for, exactly as after a Google sign-in, with the same session length and the same owner-only
pages and protections.

**Why this priority**: It is the whole request. Every local review of an owner page (and every
automated browser pass against the local stack) currently needs a working Google sign-in first.

**Independent Test**: Start the local stack against the development data, open `/admin/library`
signed out, use the local sign-in, and confirm the library opens as the owner with no request to
Google.

**Acceptance Scenarios**:

1. **Given** the local stack with development data and an owner already claimed, **When** the
   owner opens `/admin/signin` and uses the local sign-in, **Then** they are signed in as that
   owner and land on `/admin` (or the page they were sent from), with no request to Google.
2. **Given** a local sign-in session, **When** the owner submits any admin form, **Then** it
   behaves exactly as with a Google session: the same forgery protection, results and timing
   events.
3. **Given** a local sign-in session, **When** the owner signs out, **Then** the session ends as
   it does for a Google session.
4. **Given** the local stack, **When** the sign-in page renders, **Then** the Google sign-in stays
   available beside the local one (when Google is configured), so the real flow can still be
   tried locally.

---

### User Story 2 - It can never exist outside the local development stack (Priority: P1)

The local sign-in is a deliberate hole in "only Google proves who the owner is", so it must be
impossible to reach anywhere else: not on the production site, not on a machine pointed at
production data or storage, and not from another device on the network.

**Why this priority**: A sign-in without credentials on the live site would hand the gallery to
anyone. This story is the condition for allowing Story 1 at all.

**Independent Test**: Start the app in each non-qualifying shape (production environment;
development environment with production-like data or storage names; a request that does not come
from the machine itself) and confirm the local sign-in is absent from the page and its address
answers as if it did not exist; and confirm a production start with it switched on refuses to
start.

**Acceptance Scenarios**:

1. **Given** the app running in any environment other than development, **When** anyone requests
   the sign-in page or posts to the local sign-in address, **Then** no local sign-in is offered and
   the address answers not found, with no session created.
2. **Given** the development environment but a datastore or storage that is not marked as
   development, **When** the same requests are made, **Then** the same refusal happens.
3. **Given** the local stack, **When** a request for the local sign-in arrives from any address
   other than the machine itself, **Then** it is refused with no session created.
4. **Given** a production configuration, **When** the app starts, **Then** nothing in the
   production configuration can turn the local sign-in on, and the existing production
   configuration check still passes or fails exactly as today.

---

### Edge Cases

- **Unclaimed development site** (fresh development data, no owner yet): the local sign-in does
  not claim the site. The page says the site has no owner yet and offers only the Google claim, so
  the claimed owner of the development data is always a real Google account (default; see
  Assumptions).
- **Session minted locally, then the claimed owner changes** (the development data is reset and
  claimed by another account): the session is rejected on the next request, as any session whose
  owner no longer matches is today.
- **Local stack started without Google configured**: the local sign-in still works (it is the
  point of the feature); the Google button is absent, as today.
- **A local session cookie copied to another host**: it is useless there, because session cookies
  are only accepted by the instance whose keys signed them, and the owner check on every request
  still applies.
- **The owner's automation browser pointed at the local stack**: it signs in through the same
  local sign-in; no special path for tools.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On the local development stack, the sign-in page MUST offer a local sign-in that
  signs the visitor in as the site's already-claimed owner without contacting any identity
  provider.
- **FR-002**: A local sign-in MUST produce the same owner session as a Google sign-in: the same
  identity (the claimed owner's subject), the same lifetime and renewal, the same per-request owner
  check, the same sign-out, and the same forgery protection on every admin form.
- **FR-003**: After a local sign-in the owner MUST land where a Google sign-in would have taken
  them.
- **FR-004**: The local sign-in MUST be available only when ALL of these hold: the app runs in the
  development environment; the datastore and the storage are the ones marked as development (the
  same markers the existing development-only maintenance command requires); and the request comes
  from the machine the app runs on. If any one fails, the sign-in page MUST NOT offer it and its
  address MUST answer not found without creating a session.
- **FR-005**: The local sign-in MUST NOT be switchable on by any configuration setting alone; no
  setting may override the conditions of FR-004.
- **FR-006**: The production configuration check MUST continue to refuse a production start
  exactly as today, and an automated check MUST prove that a production-shaped host offers no
  local sign-in and answers not found at its address.
- **FR-007**: The local sign-in MUST NOT claim an unclaimed site. On an unclaimed development site
  it MUST NOT be offered, and the page MUST keep offering only the Google claim.
- **FR-008**: The local sign-in MUST be a deliberate action (a form submission protected against
  forgery), never automatic on page load, so a signed-out state can still be seen and tested
  locally.
- **FR-009**: Every local sign-in MUST be logged as a distinct sign-in event, naming it as local,
  with the owner identity, client address and time, so it is never mistaken for a Google sign-in.
- **FR-010**: Each refused local sign-in attempt (FR-004 failing at request time) MUST be logged
  with the failed condition and client address, never with any secret value.
- **FR-011**: The Google sign-in, claim, refusal and sign-out behaviour MUST be unchanged; existing
  sign-in tests MUST pass unmodified.
- **FR-012** *(blocking, before planning)*: The constitution's ownership rules (I.1 "ownership is
  established by Google sign-in", I.3 "every other identity is refused and gets no session") MUST
  be amended with a narrow development-only exemption that states the FR-004 conditions and the
  guards of FR-005/FR-006/FR-007, before `/speckit-plan`.
- **FR-013**: The design record's identity decision MUST be amended to describe the local sign-in
  and its conditions (design record is the source of truth).

### Key Entities

- **Local sign-in**: an owner sign-in performed without an identity provider, available only on
  the local development stack. It produces an ordinary owner session for the subject already
  stored as the site's owner; it never creates or changes ownership.
- **Development target**: the combination that qualifies a running app as local development: the
  development environment, development-marked datastore and storage, and a request from the same
  machine.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the local stack, a signed-out owner reaches `/admin/library` as the owner in one
  action on the sign-in page, in under 2 seconds, with zero requests to any identity provider.
- **SC-002**: In 100% of the automated checks across every non-qualifying shape (non-development
  environment, non-development datastore, non-development storage, non-local request), the local
  sign-in is absent from the page, its address answers not found, and no session is created.
- **SC-003**: Every existing sign-in, claim, refusal and owner-access test passes unmodified.
- **SC-004**: Every local sign-in and every refused attempt is identifiable from the log alone
  (distinct event name, outcome, address), with no secret values in any line.
- **SC-005**: For each guard condition, a deliberate break that removes it turns at least one
  automated check red.

## Assumptions

- Current surface (read 2026-11-18 on main at 945bcd6): owner sign-in is Google only, through
  `/admin/signin` (GET page, POST starts the Google flow), with the per-request owner check against
  the stored claimed subject (`src/Gallery.Web/Admin/Auth/AuthSetup.cs`). Google is registered only
  when configured. The production configuration check is `ProductionGuard` (runs when the host
  target is production); the existing development-target check is `DevTargetGuard`
  (Development environment, `_dev` database suffix, `gallery-dev-` bucket prefix), used by the
  removal command. The local launch profile binds to localhost only (5080/5081).
- The owner's development database is already claimed by their real Google account (checked
  2026-11-18: the site document carries a claim). So the default is "sign in as the stored owner",
  which needs no new identity and leaves ownership untouched.
- Default (overturnable in clarify): on an unclaimed development site the local sign-in is not
  offered (FR-007); the owner claims once with Google, then uses the local sign-in from then on.
  Alternative: the local sign-in claims a fixed local identity; that would make the development
  data's owner a fake account, so a later Google sign-in there would be refused.
- Default (overturnable in clarify): a visible button on the existing sign-in page, used on
  purpose (FR-008), rather than signing in automatically on every request. Automatic sign-in would
  make signed-out pages impossible to see locally.
- Default: no extra on/off setting beyond the FR-004 conditions (FR-005). Severable seam: an
  explicit opt-in setting in local configuration, if the owner wants the button hidden by default.
- Tests already mint owner sessions directly in their own fixtures; this feature does not change
  them. The automation browser against the local stack is one consumer of the new sign-in.
- Accepted risk (constitution I): anyone with a shell on the owner's machine can sign in to the
  local stack as the owner. They could already read the development database and secrets there,
  so this adds no new exposure. Reversal: delete the feature; no stored data depends on it.
- Planning inputs only: .NET 10 minimal APIs, the existing cookie scheme and Razor sign-in page;
  the sign-in card is a catalogue component, so a new state may move its `/ui` page (plan
  decision).
- Out of scope: any production or staging test login; multiple local identities; changing how
  tests mint sessions.
