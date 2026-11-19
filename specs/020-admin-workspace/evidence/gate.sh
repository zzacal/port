#!/usr/bin/env bash
# Parent merged-batch gate: build -warnaserror then the full suite into ./artifacts.
# Usage: bash specs/020-admin-workspace/evidence/gate.sh <label>   (output in /tmp/020-gate-<label>.txt)
# The suite rewrites earlier features' timing logs; they are restored on exit (one checkout per
# directory, so a directory with no tracked log cannot abort the rest). Every evidence log and
# capture this feature owns is pointed at /tmp during a gate, so a loaded full-suite run never
# overwrites the committed captures, which are taken with the evidence tests running alone.
# specs/005-admin-ui-components/evidence/baseline is NOT restored: the parent regenerates it on purpose.
set -u
cd "$(git rev-parse --show-toplevel)"
restore() {
  local d
  for d in specs/007-owner-signin-claim/evidence specs/008-upload-pipeline/evidence \
    specs/009-library-galleries/evidence specs/010-navigation-improvements/evidence specs/011-home-page/evidence \
    specs/013-by-camera/evidence specs/014-gallery-combobox/evidence specs/015-tile-icon-controls/evidence \
    specs/016-galleries-list-icons/evidence specs/017-photo-edit-screen/evidence \
    specs/018-local-dev-signin/evidence specs/019-selection-bar-layering/evidence; do
    git checkout -q -- "$d" 2>/dev/null
  done
}
trap restore EXIT
export LIST_TIMING_LOG=/tmp/020-gate-list-timings.log
export PHOTO_TIMING_LOG=/tmp/020-gate-photo-timings.log
export TILE_TIMING_LOG=/tmp/020-gate-tile-timings.log
export WORKSPACE_TIMING_LOG=/tmp/020-gate-workspace-timings.log
export PUBLIC_CAPTURE_DIR=/tmp/020-gate-public
L=${1:-run}
O=/tmp/020-gate-$L.txt
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -nologo -v q > "$O" 2>&1
b=$?
echo "build exit $b" >> "$O"
[ $b -eq 0 ] || { grep -E 'error|warning' "$O" | head -20; exit 1; }
dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s >> "$O" 2>&1
t=$?
echo "test exit $t" >> "$O"
grep -E '^(Passed!|Failed!)|^\s+Failed ' "$O"
grep -A3 'Error Message' "$O" | head -80
exit $t
