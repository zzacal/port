## 10. Deferred to Later Features

This design decides *what* and *why*. The items below are owned by build features (FR-022; order and
ownership in `docs/roadmap.md`) or are facts that can only be confirmed at provisioning time. None of them reopens a
decision. If one turns out differently than assumed, the dependent decision is updated here first
(Constitution VII.2).

- **Provisioning, infrastructure-as-code, and deployment**: closed by feature 012. D-24 is
  withdrawn: production is made by hand from the runbook `infra/README.md` and checked by the
  read-only `infra/check.sh`. D-25 is built as `.github/workflows/ci.yml`. The remaining owner-only
  steps are listed in `specs/012-production-infrastructure/tasks.md`.
- **Application scaffolding**: `dotnet new` solution and projects, `dotnet add package` for the
  packages justified in research.md (Constitution VI, V.2). Depends on D-01 and D-10.
- **Verify unit prices**: closed by feature 012: B2 at US$26.28/month list price from the owner's
  portal summary; Application Insights stays inside the free 5 GB/month under the daily cap. Depends on D-18.
- **Choose the Atlas region**: closed by feature 012: Azure `westus`, the App Service region,
  where Atlas Free is supported (012 `evidence/probe-atlas-region.txt`). Depends on D-05.
- **Measure real sizes**: the ThumbHash data-URI bytes, which must stay at or under about 1 KB, and
  the real derivative bytes per image, which must be checked against the 2.2 MB estimate behind
  section 8. Depends on D-09 and D-18.
- **Saved KQL evidence queries**: closed by feature 012: one committed query per time-bound success
  criterion in `specs/012-production-infrastructure/evidence/kql/`, run by `evidence/kql.sh` with
  `az monitor app-insights query`; the exact `customDimensions` column names are confirmed on the
  first production query (owner step). Depends on D-12.
- **Server-validation fixtures**: an AVIF and a WebP file with injected GPS, both of which must be
  rejected (Constitution III rationale). Depends on D-10.
