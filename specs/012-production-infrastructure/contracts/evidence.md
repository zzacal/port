# Contract: Production evidence (012, FR-019, FR-025, SC-001..SC-009)

All scripts in `specs/012-production-infrastructure/evidence/`, read-only against production, run
from the owner's machine.

## verify-prod.sh (FR-025)

`bash evidence/verify-prod.sh https://port.zacal.me | tee evidence/verify-prod-output.txt`

- Ports the GET-only public checks of 011 `verify.sh` (home, listing, breadcrumbs, `/g/` redirect,
  404 rules, no-script assertions) and the 006/010 public-page header checks (Cache-Control,
  Server-Timing, Speculation Rules, view-transition CSS, photo-page nav links) into one script
  that takes the base URL as its argument. Prior features' scripts are not edited. Checks that need
  local datastore access (006 seed lookups) are replaced by reading ids from the live listing.
- 012-specific checks:
  - SC-001 TLS: `curl -sv` handshake has no certificate error; certificate subject covers the host.
  - HTTP → HTTPS: `http://port.zacal.me/g` answers 301/308 to `https://`.
  - SC-002 origin refusal: 20 GETs to `https://<default-hostname>/` with no secret all answer 403;
    20 GETs with the public `Host: port.zacal.me` header to the origin address also 403.
  - Images: every `<img>`/`<source>` URL on the listing and one gallery starts with
    `https://img.zacal.me/`; none contains `r2.cloudflarestorage.com` or `r2.dev`.
  - SC-003 edge cache: for each public page type, two sequential GETs; the second has
    `cf-cache-status: HIT`. The KQL row then shows no second `http.request` (R-09).
  - Owner bypass: a GET with a dummy `Cookie: __Host-gallery=x` answers with `cf-cache-status`
    `BYPASS` or `DYNAMIC`.
- SC-009 secret scan: fetch every public page type plus `/admin/signin` and grep the bodies and
  headers for the real secret values, loaded from the owner's `infra/.secrets.local` (gitignored,
  read inside the script, never printed); plus `git log -p --all` scanned the same way.

## kql.sh and kql/*.kql (FR-019)

`bash evidence/kql.sh <name|all> [--since <ISO time>]` → `az monitor app-insights query --app
portfolio-appinsights -g portfolio --analytics-query @kql/<name>.kql --offset <...>`, output
appended to `evidence/kql-output.txt`.

Queries (each filters out origin-refused traffic and the Always On pings):

```
reads.kql       per public route: count, and the distribution of db_reads. Production runs the
                60 s origin output cache (IV.21), so cache hits log db_reads 0. Rule: / has
                every line == 0; /g has every non-zero line == 1; gallery and photo pages have
                every line <= 2; and each route has at least one non-zero line (proves the
                read happened at all). 011 SC-005 is restated this way for production.
p95.kql         per public route: count, p95 duration_ms (bound 50 ms server-side, 006/011)
claim.kql       auth.claim events: time, sub, ip (012 SC-005)
signin.kql      007 SC-006: callback and ownership-work p95
edge.kql        op.timing edge.purge.call / edge.warm.request outcomes (SC-004)
cachehits.kql   http.request count per route in a time window (SC-003 cross-check)
```

The first production run confirms the `customDimensions` field names; if they differ from the
template names, only the `.kql` files change (research R-06).

## SC-004 publish latency

Owner-only: publish a gallery, then `evidence/publish-wait.sh <gallery-url>` polls
`https://port.zacal.me/g` anonymously every 5 s until the gallery's title appears and prints the
elapsed seconds; PASS ≤ 60.
