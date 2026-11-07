# Quickstart: 016 validation

## Prerequisites

- Docker running (Mongo and MinIO test containers). Port 5080 free.
- Commands run from the repository root.

## Gate

```sh
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts
bash specs/016-galleries-list-icons/evidence/gate.sh final
```

Expected: 0 warnings; Core and Web pass. The only test changes are those in
[contracts/test-impact.md](contracts/test-impact.md).

## Baselines

```sh
uv run --no-project python specs/016-galleries-list-icons/evidence/baseline-diff.py
```

Expected: only B1–B4 differ.

## Hand walk (owner session, local)

1. Open `/admin/galleries` at 1440 px with public, private, empty-public and empty-private
   galleries. Expected:
   - each card shows its title on the cover (or on the blank cell), and one row: move, eye, view,
     edit;
   - edit is right-most;
   - no status words.
2. Click the eye-off on a private gallery that has photos. It becomes public: the eye shows, the
   card is no longer dimmed and the result line appears. Click the eye: it becomes private again.
3. With JavaScript off, repeat step 2. It behaves the same.
4. On the empty private card, the eye-off is greyed out and hovering shows the reason. Its view
   icon is greyed out too, in the same place.
5. Resize to 700 and 900 px. The list, the library and a builder show 2 columns, each row sits
   on one line, and every icon is full size.
6. At 1300 px, open a library photo's panel, then open a builder. The grid shows 2 columns while
   the panel leaves it too narrow for 4; at 1920 px it shows 4.
7. At 390 px: one column, each row on one line.

## Timing

```sh
bash specs/016-galleries-list-icons/evidence/p95.sh \
  specs/016-galleries-list-icons/evidence/pre-timings.log \
  specs/016-galleries-list-icons/evidence/post-timings.log
```

Expected: p95 ≤ pre × 1.10 + 1 ms, with `db_reads` equal on every line.
