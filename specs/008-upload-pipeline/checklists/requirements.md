# Specification Quality Checklist: Upload Pipeline into the Library

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-01
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

- Validation pass 1 (specify): FR-024 originally named the AVIF box and WebP chunk names; moved to
  Assumptions as planning inputs, the FR keeps the behaviour ("two independent readers", "no
  embedded metadata of any kind"). SC-007 named the storage target; reworded to the timing events.
  Pass 2: all items pass.
- Clarify (5 questions, owner absent, every recommended option taken, decisions.md rows 1–5):
  post-confirm landing (FR-034), 100-photo batch limit (FR-002, FR-004, FR-005), adding to an open
  batch (FR-007), waiting photos go interrupted (FR-010), narrow sources get one own-width size
  (FR-015). Size delta: small. Each answer was folded into existing FRs and stories (FR-002/004/005
  gain the batch limit, US-3 scenarios 3–4, US-5 scenarios 1–2, one edge case); no story was added
  or cut. No constitution conflict.
- Owner update during the run (decisions.md rows 6–7, OWNER): the roadmap's pre-plan spike is
  deferred to the owner's post-build live tests (SC-009 and the size estimates stay unverified
  until then). The R2 CORS rule is now set and verified by the owner (row 7), which adds FR-044:
  upload permissions may sign no header except content-type (row 20). Roadmap prerequisite ticked.
- Implementation names (jSquash, Web Worker, presigned PUT, MetadataExtractor, `batches`, R2
  bucket names, 005 component names, file paths) appear only in Assumptions as planning inputs or
  reuse notes, per this project's convention. FR-018 says "upload permission"; SC-011 likewise.
- Severable seams (not in the roadmap's one-line summary; cut any before /speckit-plan):
  - Placeholder validation (FR-025): the design validates sizes only, but the preview is inlined in
    public pages, so III.2 reaches it. Cutting it leaves a browser-made image unchecked on every
    public page.
  - Adding to an open batch (FR-007 second half, Clarification 3): cutting it means the owner
    confirms or discards before adding more.
  - The 100-photo batch limit (Clarification 2): a new limit the design did not name; it bounds
    page size and request sizing.
  - Credential scan of served upload pages (FR-043, SC-012): a guard, not behaviour.
  - Dev-R2 run of the permission-misuse tests (FR-042 second half): on demand when keys are
    present, per the roadmap's storage-test rule; never blocks a run without keys.
  - Upload-vs-seed separation (FR-035): needed only while seeded content exists (009 removes it).
- Highest-consequence defaults: decisions.md rows 1 (post-confirm landing), 4 (waiting photos go
  interrupted, a design-record change), 6 (spike deferred: encoder memory on iOS unproven before
  build), 9 (colour-profile rule stays strict), and 20 (content-type is the only signed header).
