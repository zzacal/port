#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T005) and runs p95.sh on each, checking that every
# pair passes or fails exactly as expected. Run from the repo root:
#   bash specs/016-galleries-list-icons/evidence/p95-selftest.sh | tee specs/016-galleries-list-icons/evidence/p95-selftest.txt
set -uo pipefail
E=specs/016-galleries-list-icons/evidence
D=$E/p95-selftest
mkdir -p "$D"

# gen FILE N MS READS
gen() {
  local f=$1 n=$2 ms=$3 reads=$4 i
  : > "$f"
  for i in $(seq 1 "$n"); do echo "http.request GET /admin/galleries 200 $ms.$((i % 10)) $reads" >> "$f"; done
  echo "http.request GET /admin/galleries 404 9000 0" >> "$f"       # non-200 ignored
  echo "http.request GET /admin/galleries/{gid} 200 9000 9" >> "$f" # other route ignored
  echo "op.timing db.galleries.list 1.2 ok" >> "$f"
}
gen "$D/pre.log"        100 10 3
gen "$D/pass.log"       100 10 3
gen "$D/slow.log"       100 20 3   # p95 20.9 > 10.9*1.1+1: p95 row fails only
gen "$D/extra-read.log" 100 10 4   # one more read on every line: reads row fails only
gen "$D/list-99.log"     99 10 3   # under-sampled: n row fails
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
expect pre.log slow.log           1 "SC-005 list p95"
expect pre.log extra-read.log     1 "SC-005 list db_reads"
expect pre.log list-99.log        1 "SC-005 list n"
expect pre.log empty.log          1 "missing"
expect pre.log does-not-exist.log 1 "missing"
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad FAILED"
exit "$bad"
