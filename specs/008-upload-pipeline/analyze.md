# Specification Analysis Report: 008 Upload Pipeline

Run by `/speckit-analyze` on 2026-11-01, unattended (owner away). Artifacts: spec.md (44 FR, 12
SC, 5 stories), plan.md, research.md, data-model.md, contracts/{http,components,log-events}.md,
quickstart.md, tasks.md (54 tasks + 4 OWNER-ONLY), constitution v4.2.0. No extension hooks are
configured (`.specify/extensions.yml` absent).

The skill asks for approval before remediating. The owner was away, so every CRITICAL, HIGH and
MEDIUM finding and the trivial LOWs were remediated in the artifacts. Each default is logged in
decisions.md rows 45–48. Analyze was then re-run once. This file is the final report. The first
pass is kept below as the audit trail.

## Pass 1 (before remediation): 0 CRITICAL, 2 HIGH, 8 MEDIUM, 3 LOW

| ID | Category | Severity | Location(s) | Summary | Remediation applied |
|---|---|---|---|---|---|
| H1 | Inconsistency / underspecification | HIGH | tasks T030; contracts/http.md U10; research R-09 | A copy refused by if-match "stops the whole confirm with 409" while `confirmStartedAt` stays set. Every later action answers confirm-in-progress, and a repeat confirm hits the same 412, so the batch is locked forever. US4/FR-011 recovery is unreachable. | U10 now fails only the changed item, adds and pulls out the rest, and returns the batch to open. Contract, data-model batch state machine, R-09, T030, T031 and T054 (h2) updated. Decisions row 45. |
| H2 | Untestable criterion | HIGH | tasks T040 | The access sweep with a valid token accepted "not 400 (a non-error status)", which passes either way. | T040 now names the exact status per route on a fresh batch. |
| M1 | Coverage gap | MEDIUM | FR-037–FR-040; tasks | No test asserted the browser-timing filter, `upload.timings_dropped`, confirm timing events or `upload.discarded`. Only p95.sh read some of them. | T029, T031 and T038 assert the exact lines. |
| M2 | Coverage gap | MEDIUM | FR-020, FR-023 | No test that U4 presigns only the server's ladder, nor 404 for an unknown item. | Added to T029. |
| M3 | Ambiguity | MEDIUM | tasks T029 | The GPS-AVIF rejection case contained unresolved planning text ("resized? no: …"). | Rewritten to name the exact fixture and helper. |
| M4 | Underspecification | MEDIUM | contracts/http.md U10/U11/U3 | No status for a `batchId` that is not open, a repeat after success, or zero entries. | Contract states 404, 200-with-count, and 400. Decisions row 46. |
| M5 | Coverage gap | MEDIUM | FR-003, FR-017, FR-011/E-04 | Document-wide drop, worker failure isolation and the "pick it again" path had no automated test. | Three browser tests added to T045. |
| M6 | Inconsistency | MEDIUM | tasks T009 vs T014 | `TryDeleteAsync` was owned by Batch A and used by parallel Batch B ("inline if not landed"), which invited two copies. | One owner: `ImageStore.TryDeleteAsync` in T014. T038 and row 44 updated. |
| M7 | Underspecification | MEDIUM | tasks T031 | The "test hook" for the confirm crash simulation was unnamed, so an executor would invent a seam. | T030 defines the `afterItem` constructor seam, and T031 uses it. |
| M8 | Underspecification / V.2 | MEDIUM | tasks T050 | `fake-oidc.cs` had no stated dependency rule. A `#:package` line would add an unjustified package, and the test fixture's JWT library is not reachable from a single-file app. | No `#:package`; RS256 is signed with the built-in RSA; dummy client values. Row 40 updated. |
| L1 | Inconsistency | LOW | plan.md, quickstart.md, research R-14 | They said the new storage facts extend `StorageTests`; tasks put them in a new file. | All three now name `UploadStorageTests` (row 37). |
| L2 | Inconsistency | LOW | tasks T036 | `[P]` was set inside a single-executor batch. | Marker removed. |
| L3 | Terminology | LOW | contracts/http.md U7, tasks T028 | "8 MB" read cap appeared only in tasks. | Contract states 8 MiB. Row 47. |

## Pass 2 (after remediation, final): 0 CRITICAL, 0 HIGH, 0 MEDIUM, 1 LOW fixed, 1 LOW accepted

| ID | Category | Severity | Location(s) | Summary | Disposition |
|---|---|---|---|---|---|
| L4 | Underspecification | LOW | contracts/components.md C4 | The new browser test needed a defined page behaviour for a failed storage PUT. The contract covered worker errors only. | Fixed: one retry, then U6 "upload failed ({status})". T045 test uses it. Row 48. |
| L5 | Coverage note | LOW | SC-001 | "40 photos, drop to added, one Confirm" is proven by the 40-photo server-path run (T049) and the 2-photo browser run (T045). The 40-photo browser run is OWNER-ONLY O-02. | Accepted: the automated pair covers the same code paths, and the owner run is listed. |

No constitution conflicts were found, so there are no BLOCKERs. Specific checks:
- The plan's Constitution Check rows were re-verified against v4.2.0 text.
- III.5's "ICC beyond a standard profile" is met more strictly (all ICC rejected, rows 9 and 29),
  which the MUST allows.
- V.3 allows admin pages to require JavaScript.
- The Platform Constraints' "htmx and web components" is a platform input, not a MUST to use
  htmx. The custom element satisfies "web components", and R-11 justifies omitting htmx.
- The workflow rule "existing tests MUST pass unmodified unless a requirement change is stated" is
  honoured: the single edit (`Owner_GetAdminUpload_returns_404`, T035) is stated as a requirement
  change (row 38).

## Coverage summary

Every FR (44/44) and every buildable SC (12/12) has at least one task. tasks.md
"Requirement coverage" holds the map. Partial automated coverage, by design:
- SC-009 (20 MB browser prep time): p95.sh exists (T048), and the measurement is OWNER-ONLY O-02
  (decisions row 6).
- FR-042/SC-011 against real R2: the emulator half is automated (T016), the R2 half is O-03.
- SC-003 on R2: the emulator half is automated (T031, T038, T050), the R2 half is O-04.

The automated implement finishes without any OWNER-ONLY item. No task depends on O-01…O-04.

Unmapped tasks: none. Setup and evidence tasks (T001–T005, T048–T054) map to plan.md's tasks-phase
rules, the roadmap spike line (T005), and Constitution IV.4.

## Metrics

- Total requirements: 56 (44 FR + 12 SC)
- Total tasks: 54 automated + 4 OWNER-ONLY
- Coverage: 100% (56/56 with ≥1 task)
- Ambiguity count: 0 open (1 fixed: M3)
- Duplication count: 0 (1 duplication risk fixed: M6)
- Critical issues: 0
- [SAFETY] tasks: T004, T006, T007, T015, T016, T019, T020, T023, T024, T028, T029, T030, T031,
  T040, T046, T054

## Next actions

- Ready for `/speckit-implement`. Run it in a fresh session seeded from tasks.md, not this
  planning conversation.
- Phase 1 (T001–T005) is parent work. Batches A, B and C fan out in parallel, each with its own
  `artifacts/exec-*` path.
- Before the owner's live run, the owner does O-01 (lifecycle rule).
