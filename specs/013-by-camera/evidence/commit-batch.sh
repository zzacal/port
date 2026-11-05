#!/usr/bin/env bash
# Parent batch commit: append decision rows, tick tasks, stage, commit. Run from repo root:
#   bash specs/013-by-camera/evidence/commit-batch.sh <rows.txt> '<task-id ERE>' <msgfile>
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
python3 specs/013-by-camera/evidence/add-decisions.py "$1"
sed -i '' -E "s/^- \[ \] ($2) /- [X] \1 /" specs/013-by-camera/tasks.md
echo "tasks done: $(grep -c '^- \[X\]' specs/013-by-camera/tasks.md)"
git add -A -- src tests specs/013-by-camera docs specs/001-architecture-stack-design/draft
git status --short
git commit -q -F "$3"
git log --oneline -1
git show --stat HEAD | tail -3
