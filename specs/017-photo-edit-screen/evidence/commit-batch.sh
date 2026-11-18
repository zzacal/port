#!/usr/bin/env bash
# Parent commit helper: tick tasks, stage an explicit path list, commit, show the result.
# Usage: bash specs/017-photo-edit-screen/evidence/commit-batch.sh '<task-id ERE>' <msgfile> <path>...
# Example: commit-batch.sh 'T00[1-5]|T018' /tmp/msg.txt specs/017-photo-edit-screen tests/... src/...
set -eu
cd "$(git rev-parse --show-toplevel)"
ids=$1; msg=$2; shift 2
F=specs/017-photo-edit-screen/tasks.md
sed -E -i '' "s/^- \[ \] (($ids)) /- [X] \1 /" "$F"
paths=()
for p in "$@" "$F"; do
  if [ -e "$p" ] || git ls-files --error-unmatch "$p" >/dev/null 2>&1; then paths+=("$p"); else echo "skip missing path: $p"; fi
done
git add -A -- "${paths[@]}"
for f in specs/017-photo-edit-screen/evidence/*.log; do [ -e "$f" ] && git add -f -- "$f"; done
git commit -q -F "$msg"
git log --oneline -1
git show --stat --format= HEAD
