#!/usr/bin/env bash
# 020 T007 self-test of compare-public.sh in diff-only mode (CP_SKIP_RUN=1: no build, so it is safe
# while src/ is being edited). Run:
#   bash specs/020-admin-workspace/evidence/compare-public-selftest.sh | tee specs/020-admin-workspace/evidence/compare-public-selftest.txt
# Exit status = number of broken expectations.
set -uo pipefail
E=$(cd "$(dirname "$0")" && pwd)
T=$(mktemp -d "${TMPDIR:-/tmp}/020-cp-selftest.XXXXXX")
trap 'rm -rf "$T"' EXIT
bad=0
expect() {  # expect LABEL POSTDIR WANT_EXIT [NEEDLE]
  local label=$1 post=$2 want=$3 needle=${4:-} out code
  out=$(CP_SKIP_RUN=1 CP_POST="$post" bash "$E/compare-public.sh" 2>&1); code=$?
  if [ "$code" -ne "$want" ]; then echo "SELFTEST FAIL $label: expected exit $want, actual $code"; printf '%s\n' "$out"; bad=$((bad+1)); return; fi
  if [ -n "$needle" ] && ! printf '%s\n' "$out" | grep -qx "$needle"; then echo "SELFTEST FAIL $label: exit $code but no line '$needle'"; printf '%s\n' "$out"; bad=$((bad+1)); return; fi
  echo "SELFTEST OK   $label: expected exit $want, actual $code; $(printf '%s\n' "$out" | grep -vE '^== ' | tr '\n' ' ')"
}
n=$(find "$E/pre-public" -type f | wc -l | tr -d ' ')

expect "pre vs itself" "$E/pre-public" 0 "EQUAL $n files"

cp -R "$E/pre-public" "$T/same"
expect "pre vs an exact copy" "$T/same" 0 "EQUAL $n files"

cp -R "$E/pre-public" "$T/onebyte"
# change exactly one byte of home.html (its first byte: '<' -> '[')
f="$T/onebyte/home.html"
{ printf '['; tail -c +2 "$E/pre-public/home.html"; } > "$f"
[ "$(cmp -l "$E/pre-public/home.html" "$f" | wc -l | tr -d ' ')" = 1 ] || { echo "SELFTEST FAIL setup: not exactly one byte changed"; bad=$((bad+1)); }
expect "copy with one byte changed" "$T/onebyte" 1 "DIFF home.html"

cp -R "$E/pre-public" "$T/missing"; rm "$T/missing/index.txt"
expect "copy with one file missing" "$T/missing" 1 "ONLY-PRE index.txt"

cp -R "$E/pre-public" "$T/extra"; echo x > "$T/extra/zz-extra.html"
expect "copy with one extra file" "$T/extra" 1 "ONLY-POST zz-extra.html"

expect "post dir absent" "$T/nope" 255
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad FAILED"
exit "$bad"
