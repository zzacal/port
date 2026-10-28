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
- the cost budget against the $10/month metered cap (constitution 3.0.0)

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
  headers), MongoDB.Driver, Azure.Storage.Blobs with Azure.Identity (managed identity),
  Microsoft.OpenTelemetry (Application Insights export), and MetadataExtractor (reads EXIF from
  the master without decoding pixels).
- Admin client: jSquash AVIF and WebP encoders (WASM) and htmx.
- Public pages: no JavaScript dependencies.

**Storage**: MongoDB Atlas free cluster for records. One Azure Blob Storage account (GPv2, LRS)
with two containers:
- `public`: Hot tier, holds derivatives, served as `img.<domain>` through Cloudflare Cloud
  Connector (Worker fallback).
- `private`: Cool tier, holds staging and the permanent masters.

**Testing**: The designed system will use xUnit with WebApplicationFactory, plus Testcontainers
for Mongo and Azurite. For this feature, the testing is `evidence/validate.sh`
(contract counts) and `evidence/links.sh` (source URLs).

**Target Platform**: Azure App Service Linux B1, Always On, behind Cloudflare Free. The admin
page targets current desktop Chrome, Edge, Firefox, and Safari.

**Project Type**: A server-rendered web application with a script-heavy admin page.

**Performance Goals**: These come from Constitution IV. At the origin, the p95 for anonymous
HTML is no more than 150 ms on a cache miss, and edge hits are served without touching the
origin. In the admin browser, a 24 MP image takes at most 15 s for its full ladder and placeholder
(measured via FR-024 timings).

**Constraints**:
- At most one B1 plan of fixed cost. Metered spend stays at or under $10/month, with an alert at
  $7; the projection is about $1/month (VIII.1).
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
| I. Single Owner, Claimed Once | PASS | Atomic conditional claim on a single site document (data-model). Claim without a token is the accepted risk in spec Assumptions; I permits an optional token, so omitting it is compliant. The 7-day sliding session is a Platform Constraint. |
| II. Anonymous Viewing, Visibility | PASS | Opaque 128-bit ids, `/g/{id}/{slug}` with a 301 redirect for a stale slug, default private, content-hash derivative names. Unlisted-but-reachable derivatives, including through the raw Blob URL, match "private means unlisted". |
| III. Published Images Leak Nothing | PASS | Masters are kept privately in the `private` container (III.1). III.2 and III.5–III.8 are designed (validation contract in research R-06). The earlier D-masters conflict is gone. |
| IV. Performance | PASS | Every rule IV.1–IV.22 is mapped to a mechanism and a verification method in the traceability matrix. Browser timings are logged to Application Insights (IV.2). Sampling is off, so evidence is complete (IV.4). |
| V. Minimal Scope and Dependencies | PASS | Six server packages and three client packages, each justified in research.md. Public pages have no JavaScript dependency. |
| VI. Official CLIs | PASS (justified N/A) | No scaffolding happens in this feature. The build features use `dotnet new`, `az`, and `wrangler`. |
| VII. Design Before Code | PASS | This feature is the design. Every decision entry carries Choice, Rationale, Alternatives, and Pivot cost (contract C2). |
| VIII. Cost Ceiling | PASS | B1 is the only fixed cost. The projected metered total is about $1/month at 5,000 images, against the $7 alert and the $10 cap (research R-02). The Application Insights daily cap and the Azure budget are the configured hard limits (VIII.4). |
| Platform Constraints | PASS | Every bullet in 3.0.0 is designed as given: Blob, image delivery, observability, and the session lifetime. |
| Workflow: timing logs | PASS (justified N/A) | This feature has no runtime. Its substitute is the recorded validation output (`evidence/validate-output.txt`). |

**Gate result**: PASS against constitution 3.0.0. No owner decisions are outstanding. The
owner still has to confirm that Cloud Connector appears on the Cloudflare zone; if it does not,
the Worker fallback is already approved.

**Post-design re-check (after Phase 1)**: PASS. data-model.md (Blob layout) and the contract
introduce no new conflicts. The contract's C6 check counts rule ids from the live constitution,
so VIII.4 and any later amendment are enforced automatically.

## Project Structure

### Documentation (this feature)

```text
specs/001-architecture-stack-design/
├── spec.md
├── plan.md                 # this file
├── research.md             # Phase 0: consolidated decisions (R-01 …)
├── research-notes/         # Phase 0 evidence: fetched quotes per area (committed)
├── data-model.md           # Phase 1: entities, state machines, Blob layout
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

There are no constitution violations needing justification.
