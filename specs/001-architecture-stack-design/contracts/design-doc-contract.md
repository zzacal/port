# Contract: Design Document Structure

The deliverable is `docs/design.md`. Every requirement in spec 001 is checked by counting
against this contract, using `evidence/validate.sh`, before any human reading (FR-005). Any
heading or label that deviates from the patterns below is a contract violation.

## C1. Top-level sections (exact headings, in this order)

```
# Gallery Architecture and Stack Design
## 1. Overview
## 2. Decision Record
## 3. System Diagrams
## 4. Content Model
## 5. Lifecycle
## 6. Edge Cases
## 7. Traceability Matrix
## 8. Cost and Free-Tier Budget
## 9. Accepted-Risk Register
## 10. Deferred to Later Features
## 11. Sources
```

## C2. Decision entries (FR-002, FR-003)

Each decision entry has a heading and exactly these labeled lines, in this order:

```
### D-NN: <Title>
**Area**: <area-key>
**Status**: given | chosen | requires-amendment
**Choice**: ...
**Rationale**: ...
**Alternatives**: ... (each rejected option with the reason it was rejected)
**Pivot cost**: low | medium | high: <what reversing it would take>
```

- `given` means the constitution fixes the decision. The Rationale MUST then cite the principle,
  for example `(Constitution VIII.1)`.
- `requires-amendment` means the decision conflicts with the current constitution (FR-021). The
  Rationale names the rule and the proposed amendment text.
- Each of the following area keys MUST appear in exactly one `**Area**:` line. Additional entries
  may use the key `other`.

```
hosting-runtime          edge-dns-tls             origin-lockdown
identity-claim           datastore                storage-layout
upload-intake            client-derivatives       server-validation
upload-batch             content-model            visibility-addressing
public-rendering         edge-cache               predictive-loading
observability            csrf
```

That is 17 keys. Check 1: `grep -c '^\*\*Area\*\*: <key>$'` returns 1 for every key.
Check 2: the number of `### D-` headings equals the count of each labeled field.

## C3. Diagrams (FR-008)

There are four mermaid diagrams, each under one of these exact headings:

```
### Diagram: Visitor public page
### Diagram: Cache miss to origin
### Diagram: Owner upload to library
### Diagram: Owner sign-in and claim
```

Each heading is followed by a fenced block that starts with `mermaid`.

## C4. Lifecycle events (FR-010)

There are 17 entries, using exactly these titles in this order:

```
### L-01: Image succeeded in batch
### L-02: Image failed or interrupted in batch
### L-03: Image restarted
### L-04: Image deleted from batch
### L-05: Batch confirmed
### L-06: Batch discarded
### L-07: Image added to gallery
### L-08: Image removed from gallery
### L-09: Image deleted
### L-10: Gallery created
### L-11: Gallery made public
### L-12: Gallery made private
### L-13: Gallery renamed
### L-14: Gallery reordered
### L-15: Images reordered
### L-16: Gallery cover changed
### L-17: Gallery deleted
```

Each entry has these four labeled lines: `**Records**:`, `**Storage**:`, `**Pages and caches**:`,
and `**Visitor sees**:`.

## C5. Edge cases (SC-003)

There are 19 entries, `### E-01:` through `### E-19:`. Their order matches the edge cases in
spec.md. Each entry has `**Outcome**:` and `**Mechanism**:`.

## C6. Traceability matrix (FR-004, SC-002)

This is one table with the header row:

```
| Rule | Mechanism | Verification | Status |
```

Rule ids are listed exactly once, in the order of the constitution version that is current when
the design is written:

- I.1–I.6, II.1–II.5, and III.1–III.8 (plus any rules added by amendment)
- IV.1–IV.22, V.1–V.3, VI.1, VII.1–VII.3, and VIII.1–VIII.3
- PC.1–PC.7, the Platform Constraints bullets in order

Status is one of `designed`, `deferred:<feature>`, or `n/a:<reason>`. Rows IV.5 and IV.11–IV.22
MUST name `markup/header assertion` or `browser measurement` in the Verification column.

## C7. Cost table (FR-006, SC-004)

This is one table with the header row:

```
| Service | Dimension | Free limit | Projected | Overage behaviour |
```

- Every external service named in section 2 appears in at least one row.
- A final row with Service = `TOTAL fixed` gives the monthly fixed cost.
- The Projected column states the scale assumption it is based on.

## C8. Accepted-risk register (FR-007)

Each risk is an entry `### R-NN: <title>` with the labeled lines `**Failure**:`,
`**Detection**:`, `**Recovery**:`, and `**Reversal cost**:`. These six risks MUST be present, and
each must contain its keyword in the title:

- `claim token`
- `backups`
- `log retention`
- `unlisted`
- `browser cache`
- `client-generated`

## C9. Hygiene

- The document contains no `TBD`, `TODO`, `NEEDS CLARIFICATION`, or `??`.
- Every URL in section 11 returns HTTP 2xx or 3xx when fetched with a browser User-Agent. The
  results are recorded in `evidence/links-output.txt`.
