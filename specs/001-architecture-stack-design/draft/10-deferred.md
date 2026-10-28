## 10. Deferred to Later Features

This design decides *what* and *why*. The items below are owned by the first build feature
(FR-022) or are facts that can only be confirmed at provisioning time. None of them reopens a
decision. If one turns out differently than assumed, the dependent decision is updated here first
(Constitution VII.2).

- **Provisioning**: creating the App Service plan, the storage account and containers, the
  Application Insights resource, the Atlas cluster, and the Cloudflare zone settings. Depends on
  D-01, D-02, D-05, D-07, and D-12.
- **Deployment and CI/CD**: build, test, and release pipeline, and how the app reaches App
  Service. Depends on D-01.
- **Infrastructure-as-code**: whether and how resources are scripted. Scaffolding uses the official
  CLIs (`az`, `wrangler`, `dotnet new`) per Constitution VI. Depends on D-01, D-07, and D-16.
- **Application scaffolding**: `dotnet new` solution and projects, `dotnet add package` for the
  packages justified in research.md (Constitution VI, V.2). Depends on D-01 and D-10.
- **Verify unit prices**: B1 for the chosen region, Blob Hot and Cool storage and operations, and
  Application Insights ingestion, in the Azure Pricing Calculator. The pricing pages render with
  JavaScript, so section 8 figures are indicative. Depends on D-18.
- **Confirm Cloud Connector on the zone**: check Rules → Cloud Connector in the Cloudflare
  dashboard. If it is absent, use the approved Worker fallback, which must use the Cache API.
  Depends on D-16 and D-19.
- **Choose the Atlas region**: pick the Free-eligible Azure region nearest the App Service region
  from the Atlas region table. Depends on D-05.
- **Measure real sizes**: the ThumbHash data-URI bytes, which must stay at or under about 1 KB, and
  the real derivative bytes per image, which must be checked against the 2.2 MB estimate behind
  section 8. Depends on D-09 and D-18.
- **Saved KQL evidence queries**: one committed query per time-bound success criterion, run with
  `az monitor app-insights query`, with output kept in each feature's `evidence/`. Depends on D-12.
- **Server-validation fixtures**: an AVIF and a WebP file with injected GPS, both of which must be
  rejected (Constitution III rationale). Depends on D-10.
