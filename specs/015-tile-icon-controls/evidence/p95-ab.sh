#!/usr/bin/env bash
# 015 T028 diagnosis: is the SC-005 p95 miss a real cost or run-to-run noise? Runs the timing
# evidence test on the pre-change tree (b2d261f: setup only, src identical to 40e5f10) and on HEAD,
# alternating, N rounds each, same machine and load, and prints p95 per run.
# Run from the repo root:  bash specs/015-tile-icon-controls/evidence/p95-ab.sh | tee specs/015-tile-icon-controls/evidence/p95-ab.txt
set -uo pipefail
ROOT=$(git rev-parse --show-toplevel)
E=$ROOT/specs/015-tile-icon-controls/evidence
OUT=$E/p95-ab
N=${N:-3}
PRE_SHA=b2d261f
WT=$(cd /tmp && pwd -P)/015-pre-wt
mkdir -p "$OUT"
[ -d "$WT" ] || git worktree add -q --detach "$WT" "$PRE_SHA"
( cd "$WT" && dotnet build Gallery.slnx --artifacts-path "$WT/artifacts" -nologo -v q >/dev/null 2>&1 ) || { echo "pre build failed"; exit 1; }
( cd "$ROOT" && dotnet build Gallery.slnx --artifacts-path "$ROOT/artifacts" -nologo -v q >/dev/null 2>&1 ) || { echo "post build failed"; exit 1; }

p95() {  # $1 log, $2 route -> nearest-rank p95 of duration_ms
  grep "^http.request GET $2 200 " "$1" | awk '{print $5}' | sort -n | awk '{a[NR]=$1} END {k=int(0.95*NR); if (k<0.95*NR) k++; print a[k]}'
}
run() {  # $1 label, $2 tree
  local log="$OUT/$1.log"
  ( cd "$2" && TILE_TIMING_LOG="$log" dotnet test tests/Gallery.Web.Tests --no-build --artifacts-path "$2/artifacts" \
      --blame-hang-timeout 120s --filter FullyQualifiedName~TileTimingEvidenceTests >/dev/null 2>&1 ) || { echo "$1 test failed"; return; }
  printf '%-7s library p95 %6s ms   builder p95 %6s ms\n' "$1" "$(p95 "$log" /admin/library)" "$(p95 "$log" '/admin/galleries/{gid}')"
}
for i in $(seq 1 "$N"); do
  run "pre-$i" "$WT"
  run "post-$i" "$ROOT"
done
git worktree remove --force "$WT"
