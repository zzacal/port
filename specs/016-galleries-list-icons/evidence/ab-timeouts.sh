#!/usr/bin/env bash
# 016 drag-fix timeout A/B. Usage: bash ab-timeouts.sh <label> <runs> [drop-new-test]
# Builds HEAD in a throwaway worktree (optionally without the new drag test) and runs the full
# suite <runs> times, printing only the summary and failing-test lines per run.
set -u
L=$1; N=$2; DROP=${3:-}
R=/Users/zzacal/repos/gallery
W=$(cd /tmp && pwd -P)/016-ab-$L
git -C $R worktree remove --force $W 2>/dev/null
git -C $R worktree add --detach $W HEAD -q
cd $W
if [ -n "$DROP" ]; then
  perl -0pi -e 's/\n    \/\/ ── Drag feedback.*?\n    \}\n(\}\n)$/\n$1/s' tests/Gallery.Web.Tests/GalleryListBrowserTests.cs
  grep -c "Drag_a_preview_follows" tests/Gallery.Web.Tests/GalleryListBrowserTests.cs | sed 's/^/new-test occurrences after drop: /'
fi
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -nologo -v q > /tmp/016-ab-$L-build.txt 2>&1 || { echo "BUILD FAILED"; tail -5 /tmp/016-ab-$L-build.txt; exit 1; }
for i in $(seq 1 "$N"); do
  O=/tmp/016-ab-$L-$i.txt
  dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s > $O 2>&1
  echo "== $L run $i"; grep -E '^(Passed!|Failed!)|^\s+Failed ' $O | grep -v Core.Tests
done
cd $R && git worktree remove --force $W
