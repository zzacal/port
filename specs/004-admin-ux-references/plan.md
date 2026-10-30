# Implementation Plan: Admin UX References

**Branch**: `004-admin-ux-references` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-admin-ux-references/spec.md`

## Summary

Produce `docs/ux-research/admin-moodboard.html`: a screenshot-backed reference board for the five
admin pages and eleven admin components in the design, followed by 2–3 named directions and one
recommendation. Two cheap researchers write notes, split by source type (photo-specific vs general
admin and design systems). The high tier takes every screenshot in a real browser, writes the
board, and reviews the captions. The board's structure is fixed by a contract so coverage
(SC-001 to SC-004, SC-006) is checked by a grep script, not by re-reading.

## Technical Context

**Language/Version**: None. The deliverables are HTML, Markdown, and PNG.
**Primary Dependencies**: The Hermes browser tool (CDP screenshots), curl, and bash. All are
already in use here. No packages.
**Storage**: Files in the repo (`docs/ux-research/`, `specs/004-admin-ux-references/`)
**Testing**: `evidence/validate.sh` (contract C1–C10) and `evidence/anchors.sh` (source
reachability). The outputs are tee'd into `evidence/`.
**Target Platform**: The owner's desktop browser, opening a local file
**Project Type**: Documentation / research deliverable
**Performance Goals**: The board opens locally with no network. Shots are PNGs at 1440 px wide
or smaller, plus element crops.
**Constraints**: No sign-in except vendor-published demo logins (research R-03). No `src/` or
`docs/design.md` changes (FR-013). The board is self-contained (C9).
**Scale/Scope**: 5 pages × at least 3 references, 11 components × at least 2 references, at
least 8 products. That is about 40–60 shots.

## Constitution Check

*Gate before Phase 0; re-checked after Phase 1.*

| Principle | Verdict | Notes |
|---|---|---|
| I. Single owner | PASS (N/A) | No identity work. The sign-in references inform a later feature only. |
| II. Visibility | PASS (N/A) | No public surface is added. The board is not linked from the site. |
| III. Images leak nothing | PASS (N/A) | No uploads. Screenshots are third-party demo content, stored in `docs/` only. |
| IV. Performance / timing logs | PASS (justified N/A) | No requests or operations to instrument. Substitute (R-06): recorded `validate-output.txt` and `anchors-output.txt` make every countable SC verifiable from saved output alone. |
| V.1 Minimal scope | PASS | Research only. Pages and components come from the accepted design, with no new admin capability. Tags are excluded (D-2). |
| V.2 Minimal dependencies | PASS | Zero packages (R-08). |
| V.3 Public no-JS | PASS (N/A) | Admin may need scripts. Script-heavy references are allowed and are flagged in their Fit line. |
| VI. Official CLIs | PASS (N/A) | Nothing is scaffolded. |
| VII. Design before code | PASS | `docs/design.md` is not edited (FR-013). A chosen direction becomes a D-entry in a later step, under VII.3. |
| VIII. Cost | PASS (N/A) | No hosted resources. |
| Workflow: decisions.md | PASS | `decisions.md` is started with D-1 to D-5. |
| Workflow: cheap-executor tasks | PASS | Researcher notes use the fixed record shape in data-model.md. Screenshots, synthesis, and caption review stay with the high tier (R-01). |

Post-Phase 1 re-check: unchanged, all PASS. The contract adds no dependency, and D-1 (demo
logins) is within FR-006 as interpreted in R-03.

## Project Structure

### Documentation (this feature)

```text
specs/004-admin-ux-references/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── decisions.md
├── contracts/board-contract.md
├── checklists/requirements.md
└── evidence/
    ├── anchors.sh, anchors-output.txt
    └── validate.sh, validate-baseline.txt, validate-output.txt   (tasks phase)
```

### Deliverable (repository root)

```text
docs/ux-research/
├── admin-moodboard.html
├── admin-candidates-a.md      # photo-specific sources (researcher A)
├── admin-candidates-b.md      # general admin + design systems (researcher B)
└── shots/admin/<product>-<subject>-<state>.png
```

**Structure Decision**: The board and shots go beside the existing public board (FR-012).
Verification scripts live in the feature dir. Nothing goes under `src/`.

## Execution outline (for /speckit-tasks)

1. Write `validate.sh` from the contract, run it on an empty skeleton board, and save the
   failing baseline.
2. Dispatch researchers A and B in parallel (low tier). Each one gets the data-model record
   shape, the PG/CP ids, the P1–P7 list, the R-03 access rule, and a required `## Rejected` and
   `## Gaps` section. Notes only: no screenshots and no board.
3. High tier: capture shots per the notes (R-04), crop components, review on a contact sheet,
   and re-shoot blanks.
4. High tier: write the board, patterns, and directions, and pick the recommendation.
5. Run validate.sh and anchors.sh, do the visual review (quickstart §3), run the scope guard,
   delete the contact sheets, and commit.

## Complexity Tracking

No constitution violations to justify.
