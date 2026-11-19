#!/usr/bin/env bash
# 020 T005 (quickstart Part A): pre-change captures, taken on the UNCHANGED source tree.
# Usage, from the repo root: bash specs/020-admin-workspace/evidence/capture-pre.sh
# 1. the five timing evidence tests, run alone, into evidence/pre-timings/ (C-17: one log per source)
# 2. the public capture twice (pre-public/ and /tmp/020-pre-public-2), which must be identical
# 3. frozen copies of the /ui baseline and the 005 ContactSheetPage baseline
# (4. the bare sign-in layout capture is taken by a temporary test, see tasks.md T005; not here)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
E=specs/020-admin-workspace/evidence
A=artifacts/parent-pre
mkdir -p "$E/pre-timings"

dotnet build tests/Gallery.Web.Tests -warnaserror --artifacts-path "$A" -nologo -v q > /tmp/020-capture-pre-build.txt 2>&1 \
  || { tail -20 /tmp/020-capture-pre-build.txt; exit 1; }

run() {  # $1 = test class filter
  dotnet test tests/Gallery.Web.Tests --no-build --artifacts-path "$A" --blame-hang-timeout 120s \
    --filter "Category=Evidence&FullyQualifiedName~$1" -nologo 2>&1 | grep -E '^(Passed!|Failed!)|^\s+Failed '
}

echo "== timings"
LIST_TIMING_LOG="$PWD/$E/pre-timings/list.log" run GalleryListTimingEvidenceTests
PHOTO_TIMING_LOG="$PWD/$E/pre-timings/photo.log" run PhotoEditTimingEvidenceTests
TILE_TIMING_LOG="$PWD/$E/pre-timings/tile.log" run TileTimingEvidenceTests
run LibraryTimingEvidenceTests
cp specs/009-library-galleries/evidence/library-timings.log "$E/pre-timings/library.log"
run MembershipTimingEvidenceTests
cp specs/014-gallery-combobox/evidence/membership-timings.log "$E/pre-timings/membership.log"
git checkout -q -- specs/009-library-galleries/evidence/library-timings.log specs/014-gallery-combobox/evidence/membership-timings.log
cat "$E/pre-timings/library.log" "$E/pre-timings/tile.log" > "$E/pre-timings.log"

{
  for f in library list photo membership tile; do
    echo "== $f.log"
    grep -oE 'http\.request (GET|POST|HEAD) [^ ]+ [0-9]{3}' "$E/pre-timings/$f.log" | sort | uniq -c | sort -k2
  done
} > "$E/pre-timings-counts.txt"
cat "$E/pre-timings-counts.txt"

echo "== public capture"
rm -rf "$E/pre-public" /tmp/020-pre-public-2
PUBLIC_CAPTURE_DIR="$PWD/$E/pre-public" run PublicCaptureEvidenceTests
PUBLIC_CAPTURE_DIR=/tmp/020-pre-public-2 run PublicCaptureEvidenceTests
if diff -r "$E/pre-public" /tmp/020-pre-public-2 > /dev/null; then
  echo "public capture repeatable: $(ls "$E/pre-public" | wc -l | tr -d ' ') files identical"
else
  echo "FAIL public capture differs between two runs"; diff -r "$E/pre-public" /tmp/020-pre-public-2 | head -20; exit 1
fi

echo "== frozen copies"
rm -rf "$E/pre-ui-baseline"
cp -R specs/006-public-site-local/evidence/ui-baseline "$E/pre-ui-baseline"
cp specs/005-admin-ui-components/evidence/baseline/ContactSheetPage.html "$E/pre-005-ContactSheetPage.html"
echo "pre-ui-baseline: $(find "$E/pre-ui-baseline" -type f | wc -l | tr -d ' ') files"
