# Specification Quality Checklist: Owner Sign-In and Claim

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-31
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Validation pass 1: SC-006 originally said "excluding time at Google", which is unmeasurable
  because the return step includes the app's own server-to-server exchange with Google. Split into
  an ownership-check bound (50 ms p95) and a whole-callback bound (1 s p95). A stray "US-6"
  reference in Edge Cases removed. Pass 2: all items pass.
- Google, the `site` record, cookie attributes, and the 005 component names appear only in
  Assumptions as planning inputs from the design record (D-04, D-06, D-23), which is this project's
  convention. Google as sole provider is a constitution rule (I.1), so FR-003 names it.
- Severable seams (not in the roadmap's one-line summary; cut any before /speckit-plan):
  - Sign-out (FR-013, US-2 scenario 5): a 7-day session with no way to end it.
  - `/ui/` owner-only gate (US-5, FR-021): D-21 says the catalogue becomes owner-only once sign-in
    exists. Cutting it keeps `/ui/` Development-only.
  - Runbook (FR-027): the roadmap says "runbook first" for the Google client; the owner-reset half
    covers I.4's out-of-band recovery.
  - Refusal and failure log events (FR-023 beyond the claim event): I.5 only requires the claim
    event; the rest is detection for E-02.
- Defaults most likely to be overturned in clarify: owner reset as a documented datastore edit
  rather than a shipped command; HTTPS with the dev certificate locally (needed for the
  host-prefixed cookie); unbuilt owner-bar links answering not found until 008/009.
- Moved out by the roadmap, not here: PC.9 forwarded headers and R-01 unclaimed window (hosting).
