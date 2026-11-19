#!/usr/bin/env bash
# Parent merged-batch gate: build -warnaserror then the full suite into ./artifacts.
# Usage: bash specs/018-local-dev-signin/evidence/gate.sh <label>   (output in /tmp/018-gate-<label>.txt)
# The suite rewrites earlier features' timing logs; they are restored on exit. 018 adds no timing log; 017's own timing
# test writes to /tmp during a gate (PHOTO_TIMING_LOG), so the committed captures, which are taken
# with the evidence test running alone, are never overwritten by a loaded full-suite run.
set -u
cd "$(git rev-parse --show-toplevel)"
restore() {
  local d
  for d in specs/007-owner-signin-claim/evidence specs/008-upload-pipeline/evidence \
    specs/009-library-galleries/evidence specs/010-navigation-improvements/evidence specs/011-home-page/evidence \
    specs/013-by-camera/evidence specs/014-gallery-combobox/evidence specs/015-tile-icon-controls/evidence \
    specs/016-galleries-list-icons/evidence specs/017-photo-edit-screen/evidence; do
    git checkout -q -- "$d" 2>/dev/null
  done
}
trap restore EXIT
export PHOTO_TIMING_LOG=/tmp/018-gate-photo-timings.log
L=${1:-run}
O=/tmp/018-gate-$L.txt
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -nologo -v q > "$O" 2>&1
b=$?
echo "build exit $b" >> "$O"
[ $b -eq 0 ] || { grep -E 'error|warning' "$O" | head -20; exit 1; }
dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s >> "$O" 2>&1
t=$?
echo "test exit $t" >> "$O"
grep -E '^(Passed!|Failed!)|^\s+Failed ' "$O"
exit $t
