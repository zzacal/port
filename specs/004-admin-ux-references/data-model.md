# Data Model: Admin UX References

There is no persisted data. These are the record shapes used in the notes files and on the
board, and the contract makes them greppable.

## Admin page (fixed set, spec FR-001)

| id | Page |
|---|---|
| PG-signin | Sign-in, first-time claim, non-owner refusal |
| PG-batch | Upload batch |
| PG-library | Library (title/caption editing, hard delete) |
| PG-galleries | Gallery list (home order, public/private) |
| PG-builder | Gallery builder (add/remove, reorder, cover, title/description, publish) |

## Admin component (fixed set, spec FR-002) with required states

| id | Component | States |
|---|---|---|
| CP-button | Button | primary, secondary, danger, disabled, busy |
| CP-input | Input | empty, filled, too-long, saving, saved |
| CP-dropzone | Upload drop zone | idle, drag-over, refused |
| CP-batchitem | Upload batch item | queued, processing, uploading, succeeded, failed, interrupted |
| CP-batchsummary | Batch summary | in-progress, ready, blocked, discard |
| CP-selectgrid | Library grid with selection | none, one, many |
| CP-reorder | Reorder list/grid | rest, dragging, keyboard |
| CP-visibility | Visibility switch | public, private, publish-warning |
| CP-cover | Cover marker | pinned, unpinned |
| CP-confirm | Destructive confirmation | delete-image, delete-gallery |
| CP-shell | Admin shell | signed-in, public-link |

## Reference

One screenshot-backed example. In the notes files (`admin-candidates-{a,b}.md`), each reference
is a block with one field per line, starting at column 0:

```
### <Product> — <subject>
URL: <source address>
Access: public | vendor-demo-login | help-docs
Taken: YYYY-MM-DD
Covers: PG-xxx[, CP-xxx:state ...]
Shot: shots/admin/<file>.png
Borrow: <one line>
Fit: <P-ids agree/conflict, one line>
Fetch: OK
```

Validation rules:
- `Shot` must exist on disk.
- `Covers` uses only the ids above.
- `Access: vendor-demo-login` is allowed only under research R-03.

## Rejected product

`- <Product> (<URL>): <one-line reason>`. These are listed in each notes file under
`## Rejected` and on the board under "Checked and set aside".

## Gap

`- CP-xxx:state — no public example found (<sources checked>)`. These are listed under `## Gaps`
in the notes files and on the board.

## Pattern

Name, the sources it was seen on (at least 2 products), and a fit verdict citing Constitution
rule ids and/or P-ids.

## Direction

A plain-words name with its definition, the source products, one line for each of the 5 PG ids,
a relation to the public contact sheet, and a pros/cons line. Exactly one direction is marked
`Recommended`.
