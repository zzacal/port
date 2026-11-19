#!/usr/bin/env bash
# 020 T007: public pages byte-identical to the pre capture (T004/T005; FR public isolation, TI-36).
# Usage (any cwd; paths resolve from this script):
#   bash specs/020-admin-workspace/evidence/compare-public.sh
#     builds tests/Gallery.Web.Tests (-warnaserror) into ${ARTIFACTS:-artifacts/exec-ev}, runs
#     PublicCaptureEvidenceTests with PUBLIC_CAPTURE_DIR=${CP_POST:-/tmp/020-post-public} (emptied
#     first), then diffs evidence/pre-public against it.
#   CP_SKIP_RUN=1 CP_POST=<dir> bash .../compare-public.sh
#     diff only (no build, no test): the self-test mode, safe while src/ is being edited.
#   CP_PRE=<dir> overrides the pre capture (default evidence/pre-public).
# Prints `EQUAL n files`, or one `DIFF <file>` / `ONLY-PRE <file>` / `ONLY-POST <file>` line per
# differing file. Exit status = number of differing files (capped at 254); 255 = the build or the
# capture run failed (no comparison made).
set -uo pipefail
export DOTNET_NOLOGO=1
E=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$E/../../.." && pwd)
PRE=${CP_PRE:-$E/pre-public}
POST=${CP_POST:-/tmp/020-post-public}
ART=${ARTIFACTS:-artifacts/exec-ev}

if [ "${CP_SKIP_RUN:-0}" != 1 ]; then
  cd "$ROOT" || exit 255
  rm -rf "$POST"; mkdir -p "$POST"
  if ! dotnet build tests/Gallery.Web.Tests -warnaserror --artifacts-path "$ART" -nologo -v q >/tmp/020-cp-build.txt 2>&1; then
    echo "FAIL build (see /tmp/020-cp-build.txt):"; grep -m5 -E ' error ' /tmp/020-cp-build.txt; exit 255
  fi
  if ! PUBLIC_CAPTURE_DIR="$POST" dotnet test tests/Gallery.Web.Tests --no-build --artifacts-path "$ART" \
        --blame-hang-timeout 120s --filter "FullyQualifiedName~PublicCaptureEvidenceTests" >/tmp/020-cp-test.txt 2>&1; then
    echo "FAIL capture run (see /tmp/020-cp-test.txt):"; grep -E '^(Failed|Passed)!' /tmp/020-cp-test.txt; exit 255
  fi
  grep -E '^(Failed|Passed)!' /tmp/020-cp-test.txt
fi

[ -d "$PRE" ] || { echo "FAIL no pre capture dir: $PRE"; exit 255; }
[ -d "$POST" ] || { echo "FAIL no post capture dir: $POST"; exit 255; }
echo "== pre:  $PRE"
echo "== post: $POST"
n=0
while IFS= read -r line; do
  case "$line" in
    "Files "*" differ") f=${line#Files "$PRE"/}; f=${f%% and *}; echo "DIFF $f" ;;
    "Only in $PRE"*) d=${line#Only in }; d="${d%%: *}/${d#*: }"; echo "ONLY-PRE ${d#"$PRE"/}" ;;
    "Only in "*) d=${line#Only in }; d="${d%%: *}/${d#*: }"; echo "ONLY-POST ${d#"$POST"/}" ;;
    *) echo "DIFF? $line" ;;
  esac
  n=$((n+1))
done < <(diff -rq "$PRE" "$POST" 2>&1)
if [ "$n" -eq 0 ]; then
  echo "EQUAL $(find "$PRE" -type f | wc -l | tr -d ' ') files"
fi
[ "$n" -gt 254 ] && n=254
exit "$n"
