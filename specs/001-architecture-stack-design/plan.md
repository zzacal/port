# Implementation Plan: End-to-End Architecture and Stack Design

**Branch**: `001-architecture-stack-design` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-architecture-stack-design/spec.md`

## Summary

This feature delivers one design document, `docs/design.md`, plus its evidence. It contains no
code (Constitution VII.1). The document fixes every architectural decision for the gallery:

- the content model, which is library first, with galleries as ordered selections
- browser-side derivative production
- server-side structural validation of uploads
- a server-persisted upload batch with Restart, Delete, and gated Confirm
- the edge-cache and purge model
- the cost budget against the free tiers

The document's structure is fixed by [contracts/design-doc-contract.md](contracts/design-doc-contract.md),
so a script checks every spec requirement by counting. The design inputs are verified facts in
`research-notes/` (fetched quotes) consolidated in [research.md](research.md). The entities are
in [data-model.md](data-model.md).

## Technical Context

These describe the system being designed, not this feature's own build.

**Language/Version**: The server is C# on .NET 10, using minimal APIs and server-rendered HTML.
The admin client is browser JavaScript, with WebAssembly codecs running in a Web Worker.

**Primary Dependencies** (each justified per V.2 in research.md):
- Server: ASP.NET Core built-ins (auth cookies, OIDC, antiforgery, output caching, forwarded
  headers), MongoDB.Driver, AWSSDK.S3 (for R2's S3-compatible API), and MetadataExtractor
  (reads EXIF from the master without decoding pixels).
- Admin client: jSquash AVIF and WebP encoders (WASM) and htmx.
- Public pages: no JavaScript dependencies.

**Storage**: MongoDB Atlas free cluster for records. Cloudflare R2 in two buckets: a private one
for staging and a public one for derivatives, served on a custom domain.

**Testing**: The designed system will use xUnit with WebApplicationFactory, plus Testcontainers
for Mongo and an S3-compatible container. For this feature, the testing is `evidence/validate.sh`
(contract counts) and `evidence/links.sh` (source URLs).

**Target Platform**: Azure App Service Linux B1, Always On, behind Cloudflare Free. The admin
page targets current desktop Chrome, Edge, Firefox, and Safari.

**Project Type**: A server-rendered web application with a script-heavy admin page.

**Performance Goals**: These come from Constitution IV. At the origin, the p95 for anonymous
HTML is no more than 150 ms on a cache miss, and edge hits are served without touching the
origin. In the admin browser, a 24 MP image takes at most 15 s for its full ladder and placeholder
(measured via FR-024 timings).

**Constraints**:
- At most one B1 plan of fixed cost; everything else stays on a free tier (VIII).
- The origin never decodes pixels (III.8).
- Public pages work with no JavaScript (V.3).
- Uploads are JPEG only, 20 MB at most.

**Scale/Scope**: About 5,000 images and 50 galleries, with low thousands of page views per
month. There is one owner.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

These rows are checked against constitution v2.0.0.

| Principle | Status | Justification |
|---|---|---|
| I. Single Owner, Claimed Once | PASS | Atomic conditional claim on a single site document (data-model). Claim without a token is the accepted risk in spec Assumptions; I permits an optional token, so omitting it is compliant. |
| II. Anonymous Viewing, Visibility | PASS | Opaque 128-bit ids, `/g/{id}/{slug}` with a 301 redirect for a stale slug, default private, content-hash derivative keys. Unlisted-but-reachable derivatives match II's "private means unlisted". |
| III. Published Images Leak Nothing | PASS, with one open item | III.2 and III.5–III.8 are designed (server-validation contract in research R-06). **Open item: D-masters.** Deleting masters after confirm conflicts with III.1 as worded ("Masters MUST be stored privately"). It is flagged requires-amendment per FR-021 and needs an owner decision before `/speckit-tasks` of the first build feature. It does not block this document-only feature. |
| IV. Performance | PASS | Every rule IV.1–IV.22 is mapped to a mechanism and a verification method in the traceability matrix. Browser timings are logged server-side (IV.2). |
| V. Minimal Scope and Dependencies | PASS | Four server packages and two client codec packages, each justified in research.md. Public pages have no JavaScript dependency. |
| VI. Official CLIs | PASS (justified N/A) | No scaffolding happens in this feature. The document records that the build features scaffold with `dotnet new`, `az`, and `wrangler`. |
| VII. Design Before Code | PASS | This feature is the design. Every decision entry carries Choice, Rationale, Alternatives, and Pivot cost (contract C2). |
| VIII. Fixed Cost Ceiling | PASS, with a stated ceiling | B1 is the only fixed cost. R2 holds about 4,000 images of derivatives free if masters are not kept (D-masters). Above that, storage bills about $0.15 per 10 GB-month (D-capacity, research R-02). The design records this ceiling and its reversals. It does not hide it. |
| Platform Constraints | PASS | All seven bullets are designed as given. |
| Workflow: timing logs | PASS (justified N/A) | This feature has no runtime. Its substitute is the recorded validation output (`evidence/validate-output.txt`). |

**Gate result**: PASS. Two owner decisions are outstanding, D-masters and D-capacity (see
decisions.md). Each has a default chosen and is flagged instead of resolved silently. Neither
blocks this document-only feature, but both MUST be settled before the first build feature's
`/speckit-plan`.

**Post-design re-check (after Phase 1)**: PASS, and nothing changed. data-model.md and the
contract introduce no new constitution conflicts. The contract's C6 check counts rule ids from
the live constitution, so any later amendment is enforced automatically.

## Project Structure

### Documentation (this feature)

```text
specs/001-architecture-stack-design/
├── spec.md
├── plan.md                 # this file
├── research.md             # Phase 0: consolidated decisions (R-01 …)
├── research-notes/         # Phase 0 evidence: fetched quotes per area (committed)
├── data-model.md           # Phase 1: entities, state machines, R2 layout
├── contracts/
│   └── design-doc-contract.md   # Phase 1: machine-checkable structure of docs/design.md
├── quickstart.md           # Phase 1: how to validate the design document
├── decisions.md            # running decisions log (decision, rationale, pivot cost)
├── evidence/               # validate.sh, links.sh and their recorded outputs (implement phase)
└── tasks.md                # /speckit-tasks
```

### Deliverable (repository root)

```text
docs/
└── design.md               # the design document; source of truth for later features (VII.2)
```

**Structure Decision**: This feature produces documents only. The application source tree is
designed inside `docs/design.md` (section 10, "Deferred to Later Features") and is created by the
first build feature with the official CLIs (VI).

## Complexity Tracking

There are no constitution violations needing justification. The one requires-amendment item
(D-masters) is an owner decision, not a complexity trade-off.
