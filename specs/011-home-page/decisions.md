# Decisions: 011 Home Page

Pivot cost: how expensive the choice is to reverse. Kinds: DEFAULT (chosen in the owner's
place, overturnable), STATED-CHANGE (an existing test or snapshot edited as a stated requirement
change), ACCEPTED / REJECTED (executor decisions), IMPLEMENT (parent call during implement).

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | IMPLEMENT | `006/ui-baseline/tokens.html` was regenerated in the Batch A checkpoint, not T021. | The TypeDisplay token (T004) moves that strict snapshot the moment it lands, so "full suite still unmodified" at the Batch A checkpoint was impossible as written. The regenerated file differs from `evidence/pre-011-specimens/006/tokens.html` only by the TypeDisplay row (diff checked, scoped ids stripped), which is one of T021's four intended strips. T021's snapshot-diff still runs against the frozen copy. | Low: one snapshot file. |
| 2 | IMPLEMENT | Batch A was done by the parent, not an executor. | Two verbatim-contract files plus their tests; cheaper than a delegation round-trip. | None. |
