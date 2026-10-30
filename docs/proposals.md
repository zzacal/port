# Proposals (not yet accepted)

Proposals that are written down but not decided. They are not part of the design record in
`docs/design.md`. When one is accepted, it moves into `specs/<feature>/draft/` as a numbered
D-entry (the assembled design doc is regenerated from the drafts by `evidence/assemble.sh`) and
gets a row in that feature's `decisions.md`. Until then, nothing here binds a plan.

Status values: `proposed` (awaiting owner), `accepted` (moved to design.md), `rejected`.

---

## P-01: Solution and project layout

**Status**: accepted (2026-10-27): now D-22 in docs/design.md, restricted to the four projects; dependencies arrive with the features that need them
**Resolves**: design.md section 10, "Application scaffolding"

Four .NET projects. They're kept small because the server never decodes pixels (D-09, D-10).

```
Gallery.slnx
  src/Gallery.Web            dotnet new web
  src/Gallery.Core           dotnet new classlib
  tests/Gallery.Core.Tests   dotnet new xunit
  tests/Gallery.Web.Tests    dotnet new xunit
```

- **Gallery.Web**: the single deployable app. Contains:
  - minimal API endpoints
  - Google OIDC and the claim-once logic
  - Razor components (D-21 atoms through pages, plus `/styleguide`)
  - Mongo and Blob access (MongoDB.Driver, Azure.Storage.Blobs, Azure.Identity)
  - telemetry (Microsoft.OpenTelemetry)
  - edge cache purge

  The admin upload script, the Web Worker, and the jSquash, thumbhash, and htmx assets live in
  `wwwroot`. Owner recovery is a command-line verb on the same assembly
  (`dotnet Gallery.Web.dll reset-owner`), not a separate tool.
- **Gallery.Core**: pure logic with no I/O and no web, Mongo, or Azure packages. Contains:
  - the AVIF/WebP structure parsers (D-10)
  - EXIF allowlist and formatting (MetadataExtractor)
  - id and slug rules, the cover rule, and the visibility rule
  - derivative ladder and `sizes` math

  This is the code with the most rules, including the Principle III GPS rejection, and it can be
  tested without containers. Because it only has these dependencies, its project references stop
  web or infrastructure code from creeping in.
- **Gallery.Core.Tests**: fast unit tests covering GPS-injected fixtures, EXIF formatting,
  slug/visibility rules, and `sizes` generation.
- **Gallery.Web.Tests**: WebApplicationFactory + Testcontainers (Mongo, Azurite) integration tests
  covering the claim race, CSRF, private/public listing, cache headers, and the traceability-matrix
  markup assertions. It also holds Microsoft.Playwright tests for what only a browser shows:
  IV.5 perceived-performance checks, View Transitions, and contact-sheet geometry.

Outside the solution:
- `docker-compose.yml`: Mongo and Azurite for local development.
- `package.json` at the root only pins the npm assets (@jsquash/avif, @jsquash/webp, thumbhash,
  htmx). A copy script places them in `wwwroot/lib`, so no bundler is needed.
- `workers/img-proxy` (`npm create cloudflare`), the roughly 20-line D-19 fallback. Only needed if
  Cloud Connector is unavailable on the zone.

Rejected:
- A separate Storage/Infrastructure project: two thin classes with one consumer.
- A Razor class library for the components: there is only one app.
- A separate E2E project: Playwright fits in Web.Tests unless its run time hurts.
- A JS bundler: the packages already ship as ES modules plus WASM.

**Pivot cost**: low before code exists. Splitting a project out later means moving files plus
`dotnet sln add`.

---

## P-02: Infrastructure as code

**Status**: proposed (2026-10-27)
**Resolves**: design.md section 10, "Provisioning" and "Infrastructure-as-code"

### Resources to provision

- **Azure**:
  - resource group
  - B1 Linux plan and Web App (Always On, system managed identity, app settings, custom domain)
  - Origin CA cert upload and SNI binding
  - Access Restrictions limited to Cloudflare ranges
  - GPv2 LRS storage with `public` and `private` containers
  - lifecycle rule: delete staging after 7 days
  - CORS for direct browser upload
  - role assignments for the app identity (Storage Blob Data Contributor, Storage Blob Delegator)
  - Log Analytics and Application Insights with a 150 MB/day cap
  - subscription budget with alerts at $7 and $10
- **Cloudflare**:
  - DNS: site CNAME, `asuid` TXT, `img.<domain>`
  - SSL Full (strict) and Always Use HTTPS
  - Authenticated Origin Pulls
  - Origin CA certificate
  - Cache Rules: cache HTML, bypass when the owner cookie is present
  - Smart Tiered Cache and Early Hints
  - Cloud Connector rule (or the Worker fallback)
  - a purge-only runtime API token
- **MongoDB Atlas**: M0 cluster in the matching Azure region, database user, and an IP access list
  containing the Web App's possible outbound IPs.
- **Google**: OAuth consent screen and web client with the redirect URI.

### Cross-provider wiring (the deciding factor)

1. The Web App hostname and verification id go into the Cloudflare CNAME and TXT records. The
   Azure custom-domain binding comes after those records, then the certificate binding.
2. Cloudflare's published IP ranges become Azure Access Restrictions, which must stay in sync.
3. The Web App's outbound IPs go into the Atlas access list.
4. The Atlas connection string, Cloudflare purge token, and Google client secret go into the Web
   App's app settings.

### Options

- **A. Terraform/OpenTofu, one root module, three providers (azurerm, cloudflare, mongodbatlas).
  Recommended.**
  - Wires all four dependencies in one plan/apply. The `cloudflare_ip_ranges` data source keeps
    the access rules current.
  - Shows changes in `plan` and detects drift when a dashboard setting is edited.
  - Every resource above has a provider resource, including the Atlas M0 tenant cluster and the
    Origin CA certificate.
  - Cost: one extra tool. State lives in a separate `tfstate` container in the same storage
    account, with blob-lease locking (pennies, inside the VIII.1 cap).
- **B. Bicep (via az) for Azure, plus a curl script against the Cloudflare API, plus the atlas
  CLI.** Native and stateless, but the cross-provider wiring becomes hand glue between three
  imperative scripts. Cloudflare changes can't be previewed, and nothing re-syncs the IP ranges.
- **C. Dashboards and a checklist.** Fastest the first time. There's nothing to rebuild from,
  nothing to diff, and everything has to be redone for disaster recovery or a second environment.

Terraform files are source code, not generated scaffolding, and they run through the official
`terraform init/plan/apply` CLI (Constitution VI).

### Proposed layout (option A)

```
infra/
  main.tf          providers, azurerm state backend
  azure.tf         plan, app, storage, insights, budget, access restrictions
  cloudflare.tf    DNS, TLS, AOP, cache rules, origin cert, image route
  atlas.tf         cluster, user, access list
  variables.tf
  outputs.tf
```

Single root module, no sub-modules, one environment (production). Local development uses
`docker-compose.yml`.

### Manual one-time steps (runbook)

- Add the domain to Cloudflare.
- Create the Google OAuth client. Google has no public API for standard web OAuth clients.
- Bootstrap credentials: `az login`, a Cloudflare API token for provisioning, and Atlas
  programmatic API keys.
- Create the state container with one `az storage container create` before the first
  `terraform init`.

### Secrets

App Service app settings (encrypted at rest), set by Terraform. Key Vault is skipped because it is
metered per operation and adds a component for one app with three secrets. Provisioning
credentials stay on the owner's machine and never reach the app.

**Pivot cost**: low before provisioning. Moving existing resources into Terraform later is a
`terraform import` per resource, which takes about an hour for this resource count.

---

## P-03: Deployment pipeline

**Status**: proposed (2026-10-27)
**Resolves**: design.md section 10, "Deployment and CI/CD"

The pipeline is GitHub Actions. It signs in to Azure with OIDC federation, so no stored
credential is needed; Terraform (P-02) creates the federated credential on an app registration.
On every push it builds and runs both test projects. On `main` it also deploys to the Web App
(`azure/webapps-deploy`) after the tests pass. There is one environment and no staging slot,
because B1 has no deployment slots; that would need Standard (S1) and is outside VIII.1.

**Pivot cost**: low. The workflow is one file, and swapping to `az webapp deploy` from a local
machine needs no infrastructure change.
