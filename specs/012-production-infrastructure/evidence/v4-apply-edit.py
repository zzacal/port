"""Apply constitution V.4 to 012: drop marginal work, add the V.4 benefit/cost table. Asserts counts."""
import pathlib, sys
b = pathlib.Path(__file__).resolve().parents[1]
E = {}
E["plan.md"] = [
("| V.2 dependencies | PASS | One package, justified by FR-018 (R-06). |",
 "| V.2 dependencies | PASS | One package, justified by FR-018 (R-06). |\n| V.4 proportional effort | PASS | See \"Effort vs benefit\" below; three marginal items dropped. |", 1),
("## Project Structure",
"""## Effort vs benefit (V.4)

Every addition beyond the literal request, with its benefit and cost. Dropped rows are not built.

| Addition | Benefit | Cost | Verdict |
|---|---|---|---|
| Origin guard + forwarded headers | Platform Constraint (origin only from Cloudflare); /admin otherwise 500s; I.5 claim IP | ~60 lines + tests | keep (constitution MUST) |
| Data Protection keys on /home | Without it every deploy signs the owner out and breaks sign-in | 3 lines + 1 test | keep |
| Production startup guard (FR-015) | Stops a mis-set app publishing into dev buckets | ~60 lines + 1 test | keep (owner kept) |
| Hosted-log queries (FR-019) | IV.4 evidence on the real site | 6 small .kql files + runner | keep (owner kept) |
| Production purge/warm (FR-011) | Changes visible in seconds, not a day | configuration only | keep (owner kept) |
| Settings check `infra/check.sh` (FR-023) | Detects drift in origin lock-down, TLS, Always On | Azure `az show` rows + outcome probes | keep, trimmed (below) |
| check.sh Cloudflare API rows (CF-01..CF-12) + read-only CF token | Same facts the outcome probes already prove (TLS, redirect, cache bypass, direct-origin 403, r2.dev off, CORS preflight) | a second token, 12 API response shapes | DROP: replaced by NET outcome rows |
| check.sh `--selftest` fixture set | Proves the check can fail before production exists | ~30 JSON fixtures | DROP: owner step O-08 (deliberate drift, expect FAIL) proves it on the real thing |
| `publish-wait.sh` | Visitor-side view of SC-004 | 1 script | DROP: SC-004 evidence is the existing `edge.lag` event |
| Datastore keep-alive job | Avoids a rare reconnect after a long Atlas pause | job + test | DROPPED earlier (decisions 24) |

## Project Structure""", 1),
("""├── check.sh                      # read-only settings check
└── check-fixtures/{good,drift}/*.json""",
"""└── check.sh                      # read-only settings check (az rows + outcome probes)""", 1),
]
E["contracts/runbook-check.md"] = [
("""Usage: `bash infra/check.sh` (live) or `bash infra/check.sh --selftest`.

Live mode requires `az` signed in and `CF_READ_TOKEN` exported (a token with Zone Read, DNS Read,
Zone Settings Read, Cache Rules Read, Transform Rules Read, and Account R2 Read). It is pinned to:""",
"""Usage: `bash infra/check.sh <origin-default-hostname>`.

Requires `az` signed in. No Cloudflare token: Cloudflare and R2 settings are checked by their
observable outcome (V.4). It is pinned to:""", 1),
("""CF-01 dns port                         CNAME proxied to the app's default hostname
CF-02 dns img                          R2 custom domain on gallery-prod-public, active
CF-03 ssl mode                         configuration rule on port.zacal.me sets ssl = strict
CF-04 https redirect                   redirect rule http://port.zacal.me/* -> https, 301
CF-05 tiered cache                     smart on
CF-06 early hints                      on
CF-07 transform rule                   sets X-Gallery-Origin on host port.zacal.me (value: present only)
CF-08 cache rules                      port: eligible, respect origin, bypass cookie __Host-gallery and /admin,/signin-oidc,/signout-callback-oidc,/ui; img: eligible, edge TTL 31536000
CF-09 r2 buckets                       gallery-prod-private, gallery-prod-public exist
CF-10 r2.dev                           disabled on both
CF-11 r2 cors (private)                exactly the R-11 rule
CF-12 r2 lifecycle                     staging/ 7-day delete; masters/ IA after 30 days
NET-01 origin direct                   https://<default-hostname>/ answers 403 (from this machine; not through Cloudflare)
NET-02 site through edge               https://port.zacal.me/ answers 200
NET-03 img through edge                https://img.zacal.me/ answers via Cloudflare (cf-ray header present)""",
"""NET-01 origin direct                   https://<default-hostname>/ answers 403 (secret header missing)
NET-02 site through edge               https://port.zacal.me/ answers 200 via Cloudflare (cf-ray), cert verifies (proves DNS, TLS strict, transform rule)
NET-03 https redirect                  http://port.zacal.me/g -> 301 https://port.zacal.me/g
NET-04 owner bypass                    GET / with Cookie __Host-gallery=x -> cf-cache-status BYPASS or DYNAMIC; GET /admin -> not HIT
NET-05 page cache                      second GET /g -> cf-cache-status HIT
NET-06 img through edge                https://img.zacal.me/ answers via Cloudflare (cf-ray)
NET-07 r2.dev off                      no pub-*.r2.dev URL appears in any public page (and the bucket's r2.dev host, if known, does not answer 200)
NET-08 r2 cors                         OPTIONS preflight PUT from https://port.zacal.me with content-type -> allowed; from https://example.com -> refused
NET-09 tiered cache / early hints      not observable from outside: listed as MANUAL (runbook toggles), not checked""", 1),
("""Self-test mode reads `infra/check-fixtures/good/*.json` and `infra/check-fixtures/drift/*.json`
(recorded shapes of each `az`/API response, with secrets replaced by placeholders) instead of calling
anything, runs the same comparison functions, and must print `RESULT: 0 FAIL` for `good` and one
FAIL per drifted file for `drift`. The selftest output is committed as
`specs/012-production-infrastructure/evidence/check-selftest.txt`.
""",
"""No self-test fixtures (V.4): owner step O-08 changes a real setting and must see its row FAIL. The
R2 lifecycle rule is a runbook step only (not observable; the 7-day backstop is already covered by
008's explicit staging deletes).
""", 1),
]
E["contracts/evidence.md"] = [
("""Evidence is `edge.kql` (log-only, IV.4). `evidence/publish-wait.sh <title>` stays as a visitor-side
sanity check of the same publish, not as evidence.""",
"""Evidence is `edge.kql` (log-only, IV.4). No visitor-side poll script (V.4).""", 1),
]
E["tasks.md"] = [
("""- [ ] T018 [P] [US2] Create `specs/012-production-infrastructure/evidence/publish-wait.sh <title>`:
  polls `https://port.zacal.me/g` every 5 s for up to 120 s until the HTML-decoded title appears;
  prints the elapsed seconds. Visitor-side sanity check only; SC-004 evidence is `edge.kql`.""",
"""- [ ] T018 [US2] Removed under constitution V.4 (publish-wait.sh): SC-004 evidence is the existing
  `edge.lag` event in `edge.kql` (T016). Nothing to build; tick it when T016 lands.""", 1),
("""- [ ] T023 [P] [US5] Create `infra/check.sh` and `infra/check-fixtures/{good,drift}/*.json` per
  contracts/runbook-check.md:""",
"""- [ ] T023 [P] [US5] Create `infra/check.sh` per contracts/runbook-check.md (no fixtures, no
  Cloudflare token: V.4):""", 1),
("""  - row ids AZ-01..AZ-16, CF-01..CF-12, NET-01..NET-03; AZ-17 was removed under constitution 6.0.0""",
"""  - row ids AZ-01..AZ-16 and NET-01..NET-09 (no CF-* rows; AZ-17 removed under 6.0.0)""", 1),
("""  - `--selftest` reads fixtures through the same comparison functions
  - `jq` is the only parser
  Run `bash infra/check.sh --selftest` and save the output to
  `specs/012-production-infrastructure/evidence/check-selftest.txt`. It must show 0 FAIL for good
  and exactly one FAIL per drift file.""",
"""  - `jq` is the only parser
  Gate: `bash -n infra/check.sh`, and a run without `az` login prints `SKIP` for every AZ row and
  runs the NET rows (they work today against the live Cloudflare setup; save the output to
  `specs/012-production-infrastructure/evidence/check-net-only.txt`).""", 1),
("""**Independent test**: `infra/check.sh --selftest` prints `RESULT: 0 FAIL` for good and one FAIL per
drift fixture; `WorkflowGuardTests` green;""",
"""**Independent test**: `infra/check.sh` NET rows run against the live Cloudflare setup; `WorkflowGuardTests` green;""", 1),
("""Batch C**: one executor, parallel to Batch B. It owns `infra/README.md`, `infra/check.sh`,
`infra/atlas.sh` and `infra/check-fixtures/**`.""",
"""Batch C**: one executor, parallel to Batch B. It owns `infra/README.md`, `infra/check.sh` and
`infra/atlas.sh`.""", 1),
("""- [ ] O-03 Run `bash infra/check.sh` with `CF_READ_TOKEN` exported, and save
  `evidence/check-output.txt`. Target: 0 FAIL.""",
"""- [ ] O-03 Run `bash infra/check.sh <default hostname>` (az signed in), and save
  `evidence/check-output.txt`. Target: 0 FAIL.""", 1),
("""- [ ] O-06 Upload a few photos, publish a gallery, and run `publish-wait.sh "<title>"` (SC-004).""",
"""- [ ] O-06 Upload a few photos and publish a gallery; then `kql.sh edge` (SC-004).""", 1),
]
E["quickstart.md"] = [
("""bash infra/check.sh --selftest | tee specs/012-production-infrastructure/evidence/check-selftest.txt
""", "", 1),
]
E["decisions.md"] = [
("""| 23 | **FINDING**:""",
"""| 27 | **OWNER** (constitution 6.1.0, V.4 proportional effort): dropped check.sh Cloudflare API rows + read-only CF token (replaced by outcome probes), check.sh self-test fixtures (O-08 proves drift detection live), publish-wait.sh (SC-004 from edge.lag) | "High effort should yield high benefits." Each was a second proof of something already proven. | Low: each can be added back alone. |
| 23 | **FINDING**:""", 1),
]
texts = {k: (b / k).read_text() for k in E}
bad = [(k, o[:50], texts[k].count(o)) for k, r in E.items() for o, _, n in r if texts[k].count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for k, r in E.items():
    t = texts[k]
    for o, nw, _ in r: t = t.replace(o, nw)
    (b / k).write_text(t)
print("OK", sum(map(len, E.values())))
