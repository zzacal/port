"""012 analyze remediation (owner approved; M1 = option a). Asserts counts before writing."""
import pathlib, sys
b = pathlib.Path(__file__).resolve().parents[1]
E = {}
E["spec.md"] = [
# H1
("""  order the owner creates them, with the exact names and values. Production is created by hand;
  no tool creates or changes resources (Clarification 2026-11-02).""",
"""  order the owner creates them, with the exact names and values. Production is created by hand,
  except the MongoDB Atlas rollout, which the owner runs with the official Atlas CLI script
  `infra/atlas.sh` (owner, 2026-11-02). No other tool creates or changes resources.""", 1),
# M1 (a)
("""- **FR-003**: The app MUST stay warm: no request may wait on a cold start of the app, and the
  datastore connection MUST stay open so the free datastore never pauses.""",
"""- **FR-003**: The app MUST stay warm: no request may wait on a cold start of the app (Always On).
  The free datastore may pause after long inactivity; this is an accepted risk (owner, 2026-11-02):
  the next page view or admin visit reconnects.""", 1),
("""- The datastore's free tier pauses after long inactivity: the always-on host keeps a connection
  open, so it does not.""",
"""- The datastore's free tier pauses after long inactivity (accepted risk, FR-003): the first
  request after the pause reconnects; no keep-alive job is built.""", 1),
# C2 / H2
("""- **SC-004**: After the owner publishes a gallery, it appears on the public listing for an
  anonymous visitor within 60 seconds.""",
"""- **SC-004**: After the owner publishes a gallery, the purge and warm of the affected pages
  completes within 60 seconds of the save, read from the existing `edge.lag {op}` event for
  `gallery.publish` in the hosted logs, with every `edge.purge` line of that change `ok`.""", 1),
("""- **SC-005**: The claim event for the owner is queryable in the hosted logs within 5 minutes of
  the first sign-in, showing the owner's identity and their real client address.""",
"""- **SC-005**: The claim event for the owner is in the hosted logs with the owner's identity and
  their real client address (not a platform or Cloudflare address), and its ingestion delay
  (ingestion time minus event time) is at most 5 minutes.""", 1),
("""- **SC-006**: Each prior time-bound success criterion that applies to public pages is reproduced
  from hosted-log queries alone, and its p95 is no worse than the local evidence by more than the
  network distance to the host.""",
"""- **SC-006**: From hosted-log queries alone, every public route keeps the existing server-side
  bounds: p95 `duration_ms` under 50 ms, and the 011 read counts under the production rule in
  contracts/evidence.md (origin output cache hits log 0 reads).""", 1),
# L3
("""  commit deployed. The pipeline MUST NOT be able""",
"""  commit deployed. Tests that need live R2 keys skip there, as they do locally without keys. The
  pipeline MUST NOT be able""", 1),
# L4
("""- **Runbook**: the ordered owner-only steps that have no management interface (domain and account
  setup, identity console, first sign-in and claim check, credential rotation).""",
"""- **Runbook**: the ordered owner steps that create every production resource and setting, plus
  the first sign-in and claim check and credential rotation.""", 1),
# clarification record
("""  FR-020, SC-008 and the severable-seams list.""",
"""  FR-020, SC-008 and the severable-seams list.
- Analyze remediation (owner, 2026-11-02): datastore keep-alive not built ("do not overengineer
  marginal benefits"); FR-003 and the edge case accept the pause. SC-004/005/006 restated to be
  log-verifiable (IV.4). FR-022 names the Atlas script exception.""", 1),
]
E["contracts/hosting.md"] = [
("""   next middleware. Log `origin.refused {reason}` with reason `missing` or `mismatch`, never the
   value received.""",
"""   next middleware. Log `origin.refused {method} {path} {status} {duration_ms} {reason}` (IV.1:
   every request logs route, status and duration; `path` is the request path, `duration_ms` is
   timed inside the guard), reason `missing` or `mismatch`, never the value received.""", 1),
("""it is, so refused requests are not logged as `http.request`; they are logged only as
`origin.refused`. Rationale: refused probes must not pollute the public-page timing evidence.""",
"""it is, so refused requests are not logged as `http.request`; they are logged only as
`origin.refused` (which carries the IV.1 fields). Rationale: refused probes, including Always On's
pings, must not pollute the public-page timing evidence.""", 1),
("""origin.refused {reason}            reason ∈ missing | mismatch""",
"""origin.refused {method} {path} {status} {duration_ms} {reason}    reason ∈ missing | mismatch; status 403""", 1),
("""  - `CF-Connecting-IP: 203.0.113.9` with the correct secret → the `auth.failed`/request IP seen by
    the app is `203.0.113.9` (assert through a test endpoint-free route: hit `/signin-oidc` with no
    state and read the `auth.failed {ip}` log line)""",
"""  - `CF-Connecting-IP: 203.0.113.9` with the correct secret → the IP the app logs is
    `203.0.113.9`: host configured with the existing `Fixtures/FakeGoogle.cs` authority (as
    AdminAccessTests does), GET `/signin-oidc` with no state, read the `auth.failed {ip}` line""", 1),
("""| `Hosting:DataProtectionKeysPath` | present |""",
"""| `Hosting:DataProtectionKeysPath` | present |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | present (FR-018) |""", 1),
]
E["contracts/evidence.md"] = [
("""`bash evidence/verify-prod.sh https://port.zacal.me | tee evidence/verify-prod-output.txt`""",
"""`bash evidence/verify-prod.sh https://port.zacal.me <origin-default-hostname> | tee evidence/verify-prod-output.txt`

Optional env `ORIGIN_SECRET`: when set, every request also sends `X-Gallery-Origin: $ORIGIN_SECRET`.
Only for the local self-test against a guarded build; never set for production runs.""", 1),
("""claim.kql       auth.claim events: time, sub, ip (012 SC-005)""",
"""claim.kql       auth.claim events: time, sub, ip, ingestion delay = ingestion_time() - timestamp (SC-005: <= 5 min)""", 1),
("""edge.kql        op.timing edge.purge.call / edge.warm.request outcomes (SC-004)""",
"""edge.kql        edge.lag {op} {duration_ms} for gallery.publish (SC-004: <= 60000) and the edge.purge outcomes of the same change""", 1),
("""## SC-004 publish latency

Owner-only: publish a gallery, then `evidence/publish-wait.sh <gallery-url>` polls
`https://port.zacal.me/g` anonymously every 5 s until the gallery's title appears and prints the
elapsed seconds; PASS ≤ 60.""",
"""## SC-004 publish latency

Evidence is `edge.kql` (log-only, IV.4). `evidence/publish-wait.sh <title>` stays as a visitor-side
sanity check of the same publish, not as evidence.""", 1),
]
E["research.md"] = [
("""  which is all Always On is for. The 403s are logged as `http.request GET / 403` from the
  platform's address; evidence queries filter them by status (R-09).""",
"""  which is all Always On is for. The 403s are logged as `origin.refused GET / 403 ...`, never as
  `http.request`, so evidence queries are unaffected (R-09).""", 1),
]
E["tasks.md"] = [
("""- **Log templates, verbatim**: `origin.refused {reason}`, with reason `missing` or `mismatch`. No""",
"""- **Log templates, verbatim**: `origin.refused {method} {path} {status} {duration_ms} {reason}`,
  with reason `missing` or `mismatch`. No""", 1),
("""  - on refusal: 403, empty body, `Cache-Control: no-store`, `origin.refused {reason}`""",
"""  - on refusal: 403, empty body, `Cache-Control: no-store`, `origin.refused {method} {path} {status} {duration_ms} {reason}`""", 1),
("""  "http://127.0.0.1:0"], overrides)`. Use a fixed 40-char test secret, and assert it is absent
  from every captured log line.""",
"""  "http://127.0.0.1:0"], overrides)`. The forwarded-IP case configures the existing
  `Fixtures/FakeGoogle.cs` authority (copy how AdminAccessTests sets `Authentication:Google:*`).
  Assert the refused-request line carries method, path, 403 and a duration. Use a fixed 40-char test
  secret, and assert it is absent from every captured log line.""", 1),
("""  Self-test it against a local Release run on :5193 with the guard on, by passing the local URL as
  both arguments. TLS/edge rows report `SKIP (no edge)` there.""",
"""  Optional env `ORIGIN_SECRET` adds `X-Gallery-Origin` to every request (local only). Self-test it
  against a local Release run on :5193 with the guard on and `ORIGIN_SECRET` set, passing the
  local URL as both arguments; TLS/edge rows report `SKIP (no edge)` and the origin-refusal row
  sends its 20 requests without the header.""", 1),
("""  prints the elapsed seconds and `PASS` if ≤ 60 (SC-004).""",
"""  prints the elapsed seconds. Visitor-side sanity check only; SC-004 evidence is `edge.kql`.""", 1),
("""  - [SAFETY] mechanism exists: `FixedTimeEquals`, `"origin.refused`,""",
"""  - [SAFETY] mechanism exists: `FixedTimeEquals`, `"origin.refused {method}`,""", 0),
]
E["plan.md"] = [
("- About 5 new source files in `Hosting/`", "- 4 new source files in `Hosting/`", 1),
("""Forwarded headers and `CF-Connecting-IP` (PC.9, now delivered). |""",
"""Forwarded headers and `CF-Connecting-IP` (PC.9, now delivered). Exemption, stated: the deploy site (`*.scm.azurewebsites.net`) is outside the Cloudflare-only restriction so GitHub-hosted runners can deploy; it accepts Entra sign-in only (basic auth off), checked by AZ-07/AZ-09. |""", 1),
]
E["decisions.md"] = [
("""| 23 | **FINDING**:""",
"""| 24 | **OWNER**: no datastore keep-alive (analyze M1, option a) | "Do not overengineer marginal benefits." The first request after an Atlas Free pause reconnects. | Low: a timer later if pauses bite. |
| 25 | **DEFAULT**: SC-004 evidence = existing `edge.lag` event (save → purge+warm done), not a visitor poll | IV.4 log-only; no new code. publish-wait.sh kept as a sanity check. | Low. |
| 26 | **FINDING**: `origin.refused` carries method/path/status/duration (IV.1) | Refused requests bypass RequestTimingMiddleware; IV.1 needs every request logged with route, status, duration. | — |
| 23 | **FINDING**:""", 1),
]
texts = {k: (b / k).read_text() for k in E}
bad = [(k, o[:50], texts[k].count(o)) for k, r in E.items() for o, _, n in r if n and texts[k].count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for k, r in E.items():
    t = texts[k]
    for o, nw, n in r:
        if n: t = t.replace(o, nw)
    (b / k).write_text(t)
print("OK", sum(1 for r in E.values() for x in r if x[2]))
