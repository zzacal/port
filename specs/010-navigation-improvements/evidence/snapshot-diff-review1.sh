#!/usr/bin/env bash
# 010 owner review 1: word-level diff of the regenerated snapshots, after removing scoped-CSS ids
# and collapsing whitespace, so every remaining hunk is a reviewed, intended change.
# Usage (repo root): bash specs/010-navigation-improvements/evidence/snapshot-diff-review1.sh <old-dir> <new-dir>
set -uo pipefail
OLD="$1"; NEW="$2"
norm() { perl -0777 -pe 's/ b-[a-z0-9]{10}//g; s/<style>.*?<\/style>/<style><\/style>/gs; s/\s+/ /g; s/>\s*</>\n</g' "$1"; }
for rel in index.html molecules/pager.html organisms/owner-bar.html; do
  echo "=== $rel"
  diff <(norm "$OLD/$rel") <(norm "$NEW/$rel") | grep '^[<>]' | cut -c1-220
done
