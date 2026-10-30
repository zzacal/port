# Quickstart: validating the admin reference board

Run all commands from the repo root.

## 1. Structural check (contract C1–C10)

```sh
bash specs/004-admin-ux-references/evidence/validate.sh | tee specs/004-admin-ux-references/evidence/validate-output.txt
```

Expected: every line `PASS`, and the final line `ALL PASS`. Before research starts, the same
script run against the empty skeleton must FAIL on C2–C8. That run is saved as
`evidence/validate-baseline.txt`.

## 2. Source reachability

```sh
bash specs/004-admin-ux-references/evidence/anchors.sh | tee specs/004-admin-ux-references/evidence/anchors-output.txt
```

Expected: every source used on the board is OK or WARN. A WARN means it was reached in a real
browser instead of curl. No used source may be DEAD.

## 3. Visual review (high tier, not delegated)

```sh
open docs/ux-research/admin-moodboard.html
```

Check each item:
- Every caption describes its screenshot, including left/right and the state named.
- No shot shows a real person's private data (FR-006). Demo content only.
- Directions define their names in plain words (FR-011), and each covers the five pages.
- The board reads in one sitting with no need to open source sites (SC-005).

## 4. Scope guard (FR-013)

```sh
git diff --stat main -- src docs/design.md
```

Expected: no output.
