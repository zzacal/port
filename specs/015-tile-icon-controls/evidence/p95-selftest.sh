#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T005) and runs p95.sh on each, checking that every
# pair passes or fails exactly as expected. Run from the repo root:
#   bash specs/015-tile-icon-controls/evidence/p95-selftest.sh | tee specs/015-tile-icon-controls/evidence/p95-selftest.txt
set -uo pipefail
E=specs/015-tile-icon-controls/evidence
D=$E/p95-selftest
mkdir -p "$D"

# gen FILE LIB_N LIB_MS LIB_READS BUILDER_N
gen() {
  local f=$1 ln=$2 ms=$3 reads=$4 bn=$5 i
  : > "$f"
  for i in $(seq 1 "$ln"); do echo "http.request GET /admin/library 200 $ms.$((i % 10)) $reads" >> "$f"; done
  for i in $(seq 1 "$bn"); do echo "http.request GET /admin/galleries/{gid} 200 3.$((i % 10)) 1" >> "$f"; done
  echo "http.request GET /admin/library 404 9000 0" >> "$f"   # non-200 ignored
  echo "op.timing db.library.page 1.2 ok" >> "$f"
}
gen "$D/pre.log"        100 10 4 100
gen "$D/pass.log"       100 10 4 100
gen "$D/slow.log"       100 20 4 100   # library p95 20.9 > 10.9*1.1+1: p95 row fails only
gen "$D/extra-read.log" 100 10 5 100   # one more read on every line: reads row fails only
gen "$D/builder-99.log" 100 10 4 99    # under-sampled builder: n row fails only
: > "$D/empty.log"
sed -e 's/"/\\"/g' -e 's/^/{"LogLevel":"Information","Category":"Gallery.Web","Message":"/' -e 's/$/"}/' "$D/pass.log" > "$D/pass-json.log"

bad=0
expect() {
  local pre=$1 post=$2 want=$3 needle=${4:-} out code
  out=$(bash "$E/p95.sh" "$D/$pre" "$D/$post" 2>&1); code=$?
  if [ "$code" -ne "$want" ]; then echo "SELFTEST FAIL $post: exit $code, wanted $want"; bad=$((bad+1)); printf '%s\n' "$out"; return; fi
  if [ -n "$needle" ] && ! printf '%s\n' "$out" | grep -q "^FAIL $needle"; then echo "SELFTEST FAIL $post: no 'FAIL $needle' row"; bad=$((bad+1)); printf '%s\n' "$out"; return; fi
  echo "SELFTEST OK   $post: exit $code${needle:+, FAIL $needle}"
}
expect pre.log pass.log           0
expect pre.log pass-json.log      0
expect pre.log slow.log           1 "SC-005 library p95"
expect pre.log extra-read.log     1 "SC-005 library db_reads"
expect pre.log builder-99.log     1 "SC-005 builder n"
expect pre.log empty.log          1 "missing"
expect pre.log does-not-exist.log 1 "missing"
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad FAILED"
exit "$bad"
