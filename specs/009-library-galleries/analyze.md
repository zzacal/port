# Specification Analysis Report: 009 Library and Gallery Management

Artifacts: spec.md (50 FRs, 12 SCs, 6 stories), plan.md, research.md, data-model.md,
contracts/{http,edge,components,log-events,removal-command}.md, quickstart.md, tasks.md (T001–T060,
O-01–O-04), constitution v4.2.0. No `.specify/extensions.yml`: no hooks configured.

Two passes were run in this unattended session. Pass 1 ran on the tasks commit (90fea47). Every
CRITICAL, HIGH and MEDIUM finding and the trivial LOWs were fixed in the artifacts, with
decisions.md rows 42–47. Pass 2, below, is the final state.

## Pass 1 findings (all remediated)

HIGH
- H1 Coverage, FR-039 (warm the files the purged pages show first). The worker warmed
  `EdgeWork.WarmImages`, but no task computed it. Fix: `PurgeSets.WarmFiles` (T013) with its
  unit cases (T014); every write puts it into `EdgeWork.WarmFiles` (T021); data-model updated;
  decisions row 45.
- H2 Underspecification, T045 vs contracts/removal-command.md. The command builds a bare service
  provider, so `EdgeWorker` never runs, the purge is never sent, and `PageCacheEvictor` cannot
  resolve. The rewritten 008 coexistence facts also used `gallery_test_*` databases, which the
  guard refuses. Fix: a generic host that starts the worker and drains it through
  `EdgeQueue.WhenIdleAsync`, the guard run before the host, a `RemoveAsync(IServiceProvider)` test
  entry point, `_dev` database names in T046, and the T047 rewrites call `RemoveAsync`. The
  contract and decisions row 43 were updated.
- H3 Inconsistency, a stated requirement change missing from plan/contracts. Moving
  `DerivativeValidator`/`CameraFields` forces edits to `CameraFieldsTests` (its `using`, plus the
  source path in `Source_CameraFields_does_not_contain_Gps`), `DerivativeValidatorTests` and
  `UploadValidatorTests`, but plan.md "Test changes" claimed everything else stays unmodified.
  Fix: plan Test changes item 6, the plan structure tree, the removal-command contract, and
  quickstart Part A now point at T047. Decisions row 34.

MEDIUM
- M1 Coverage, FR-050. The owner-action events were emitted but never asserted. Fix: assertions
  in T040, T042, T043 and T046.
- M2 Coverage, FR-049. No task checked that every named op is emitted. Fix: T057 asserts at
  least one `op.timing` per Rules op (exceptions named).
- M3 Coverage, FR-044 end to end. Eviction was only tested at the evictor. Fix: T033 checks
  that an L2 save makes the photo page re-read while `/` stays cached.
- M4 Inconsistency, UI-3 one primary. contracts C1 made both the selection bar's "Add to
  gallery…" and the panel's Save Primary. Fix: Secondary while the panel is open (contract, T031,
  T032 and T051 assertions). Decisions row 46.
- M5 Coverage, IV.10 page warm when `PurgePages=true`. Fix: a T017 case.
- M6 Inconsistency, quickstart Part B. It named `perf.log` and a curl loop, while tasks use
  `library-timings.log` from an evidence test plus `verify.sh`. It named `non-vacuity-output.txt`
  where tasks use `non-vacuity.txt`. Fix: quickstart aligned with T057, T058 and T060.
- M7 Inconsistency, route count. The plan said 14 owner routes; contracts/http.md lists 15. Fix:
  the plan now says 15.
- M8 Underspecification, `OperationScope`. It was used by T021–T024 and defined nowhere. Fix:
  defined in T021.
- M9 Ambiguity, `db.gallery.read`. It was reused for owner reads while T002, T019 and T033
  count it to detect public cache hits. Fix: owner op `db.gallery.load` (tasks Rules,
  log-events contract). Decisions row 42.
- M10 Underspecification, index creation. Only `ConfirmService` ensured the partial `seedKey`
  index, so an owner-created gallery on a legacy database could hit DuplicateKey. Fix:
  `Indexes.EnsureOnceAsync`, called by every 009 write service (T005). Decisions row 44.

LOW (trivial ones fixed)
- L1 T047 hard-coded "32 deleted facts". Changed to a per-class count from the T001 run, with
  theories expanded.
- L2 T036 and T047 had no story label. Added [US1] and [US6].
- L3 FR-002 `aria-current` was not asserted. Added to T032 and T038.
- L4 FR-019 live-region announcement and ghost slot were not asserted. Added to T051.
- L5 T044 copy versus move of the guard checks. The coexistence until T047 is now stated.
- L6 T002's third fact needs `PageTag` before the red run. The fact order is now stated.
- L7 The `refused`/`stale`/`skipped` outcomes had no emitting rule. Rule added (tasks Rules,
  log-events contract).
- L8 FR-032 "keeps its home position" was not asserted. Added to T040.

## Pass 2 (final): findings

| ID | Category | Severity | Location | Summary | Disposition |
|----|----------|----------|----------|---------|-------------|
| P1 | Constitution | LOW | Platform Constraints vs research R-08 | Admin interactivity uses web components without htmx, while Platform Constraints name "htmx and web components" | Accepted. The constraint is satisfied by web components, the same choice as 008. Decisions rows 17 and 47. |
| P2 | Constitution | LOW | constitution III.10, III.2, IV.6 | The seeding permission text is dead after retirement | Accepted. It is a permission, so no MUST is affected and no amendment is required. An optional PATCH is left to the owner. Decisions rows 28 and 47. |
| P3 | Inconsistency | LOW | T044 and T047 | Two copies of the target checks exist between Batch G and T047 | Accepted. T047 deletes the old copy, and T052 guards against its return. |

There are no CRITICAL, HIGH or MEDIUM findings, and no BLOCKER. Nothing needs a constitution
amendment.

## Coverage summary

Every FR-001 to FR-050 and SC-001 to SC-012 maps to at least one task in the tasks.md
"Requirement coverage" table. The table uses ranges: FR-014–FR-017, FR-021–FR-028 and
FR-040–FR-042. Two items are partly owner-only:

- SC-002, SC-003 and SC-005 are proven on S3Proxy and local Mongo by T057. The R2 bulk-delete
  timing is O-03, and decisions row 22 covers it.
- SC-010 against the real dev database is O-02. It is proven on test databases by T046.

There are no unmapped tasks: Setup, polish and evidence tasks all trace to FRs or SCs, or to the
constitution workflow (baseline, verify, non-vacuity).

Constitution alignment:

- I.3 and I.6: T003, T035, T060(g).
- II.5: T022, T023, T052, T060(j).
- II.6: T022, T041, T060(a,b).
- III.9-style token handling: T016, T018, T054, T060(h).
- IV.1, IV.2 and IV.4: Rules, T056, T057.
- IV.9: T009, T055.
- IV.10 and IV.18: T013–T019.
- IV.22 and FR-003: T051, T052, T060(m).
- VI: `git mv`/`git rm`, `dotnet` CLIs only.
- VII.2: the design record is already committed.
- Workflow: T058 and T059 cover the containers and the live-server walkthrough.

## Metrics

- Total requirements: 62 (50 FR + 12 SC)
- Total tasks: 60 automated (T001–T060) plus 4 OWNER-ONLY (O-01–O-04)
- [SAFETY] tasks: 17
- Coverage: 100% of requirements have at least one task
- Ambiguity count: 0 open (1 fixed)
- Duplication count: 0
- Critical issues: 0 (pass 1: 0 CRITICAL, 3 HIGH, 10 MEDIUM, 8 LOW; pass 2: 3 LOW accepted)

## Next actions

- Ready for `/speckit-implement`, in a fresh session seeded from the artifacts. Run Phase 1 first;
  T002, the output-cache check, is the first implement task after the baseline.
- After the build, the owner runs O-01 to O-04.
