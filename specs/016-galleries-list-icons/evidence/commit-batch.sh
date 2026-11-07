#!/usr/bin/env bash
# Parent batch commit: append decision rows, tick tasks, stage an explicit path list, commit.
# Usage: bash commit-batch.sh <rows.txt|-> '<task-id ERE>' <msgfile> <path>...
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
F=specs/016-galleries-list-icons
rows=$1; ids=$2; msg=$3; shift 3
[ "$rows" = "-" ] || uv run --no-project python $F/evidence/add-decisions.py "$rows"
[ -z "$ids" ] || sed -i '' -E "s/^- \[ \] (${ids}) /- [X] \1 /" $F/tasks.md
paths=()
for p in "$@" $F/tasks.md $F/decisions.md; do
  if [ -e "$p" ] || git ls-files --error-unmatch "$p" >/dev/null 2>&1; then paths+=("$p"); else echo "skip missing $p"; fi
done
git add -A -- "${paths[@]}"
echo "== staged:"; git diff --cached --name-only
git commit -q -F "$msg"
git log --oneline -1
git show --stat HEAD | tail -1
