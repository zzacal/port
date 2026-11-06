#!/usr/bin/env bash
# Re-run one test N times alone (flake check). Usage: bash flake.sh '<filter>' <N>
cd "$(git rev-parse --show-toplevel)"
f=$1; n=${2:-5}; pass=0
for i in $(seq 1 "$n"); do
  if dotnet test tests/Gallery.Web.Tests --no-build --artifacts-path artifacts --blame-hang-timeout 120s --filter "$f" > /tmp/013-flake-$i.txt 2>&1; then pass=$((pass+1)); echo "run $i PASS"; else echo "run $i FAIL"; grep -m2 -A2 'Error Message' /tmp/013-flake-$i.txt; fi
done
echo "$pass/$n passed"
git checkout -q -- specs/007-owner-signin-claim specs/008-upload-pipeline specs/009-library-galleries specs/010-navigation-improvements specs/011-home-page 2>/dev/null
