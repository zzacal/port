#!/usr/bin/env bash
# Parent merged-batch gate: build -warnaserror then the full suite into ./artifacts.
# Usage: bash specs/013-by-camera/evidence/gate.sh <label>   (output in /tmp/013-gate-<label>.txt)
set -u
cd "$(git rev-parse --show-toplevel)"
L=${1:-run}
O=/tmp/013-gate-$L.txt
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -nologo -v q > "$O" 2>&1
b=$?
echo "build exit $b" >> "$O"
[ $b -eq 0 ] || { grep -E 'error|warning' "$O" | head -20; exit 1; }
dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s >> "$O" 2>&1
t=$?
echo "test exit $t" >> "$O"
grep -E '^(Passed!|Failed!)|^\s+Failed ' "$O"
git checkout -q -- specs/007-owner-signin-claim/evidence/sc006-callbacks.log specs/008-upload-pipeline/evidence/upload-timings.log specs/009-library-galleries/evidence/library-timings.log specs/010-navigation-improvements/evidence/nav-timings.log specs/011-home-page/evidence/home-timings.log 2>/dev/null
exit $t
