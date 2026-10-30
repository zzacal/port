# Quickstart: Validating the Design Document

This feature produces documents only. "Running" it means checking `docs/design.md` against
[contracts/design-doc-contract.md](contracts/design-doc-contract.md) and checking its sources.

## Prerequisites

- bash, grep, awk, and curl (all standard on macOS and Linux).
- Run every command from the repository root.

## 1. Structural validation (FR-002 to FR-008, SC-001 to SC-003)

```bash
bash specs/001-architecture-stack-design/evidence/validate.sh \
  | tee specs/001-architecture-stack-design/evidence/validate-output.txt
```

Expected: the script exits 0 and every line reads `PASS`. The script checks contract items C1
through C9:

| Check | Expected |
|---|---|
| C1 section headings | 11 headings, in order |
| C2 decision entries | 17 required area keys, each exactly once; field counts equal the `### D-` count |
| C3 diagrams | 4 diagram headings, each followed by a mermaid block |
| C4 lifecycle | 17 `### L-` entries × 4 labeled fields |
| C5 edge cases | 19 `### E-` entries × 2 labeled fields |
| C6 matrix | every rule id from the current constitution appears exactly once; no row has an empty Status |
| C7 cost | `TOTAL fixed` and `TOTAL metered` rows are present, and the metered total is below $7; every service in section 2 has a row |
| C8 risks | all 7 keyword risks are present, each with 4 labeled fields |
| C9 hygiene | no TBD, TODO, NEEDS CLARIFICATION, or `??` |

For check C6, the script extracts rule ids from `.specify/memory/constitution.md` itself
(numbered list items under each `### <Roman>.` heading). Because of that, a later amendment makes
the check fail until the matrix is updated.

## 2. Source links (C9)

```bash
bash specs/001-architecture-stack-design/evidence/links.sh \
  | tee specs/001-architecture-stack-design/evidence/links-output.txt
```

Expected: every URL listed in `docs/design.md` section 11 returns 2xx or 3xx. The script uses
`curl -sIL` with a browser User-Agent.

## 3. Owner review (SC-006)

The owner reads section 2 and marks each `### D-NN` entry approved or rejected. Rejections go
into `decisions.md` as new rows. Each rejection must map to a single entry. A rejection that
forces edits across several entries is a design defect, so record it as one.

## 4. Next-feature readiness (SC-005)

When the first build feature runs `/speckit-plan`, its Constitution Check must contain zero rows
saying "undecided by design". Any such row is a gap in `docs/design.md`, and the gap is fixed in
this feature's document per VII.2, not in the build feature.
