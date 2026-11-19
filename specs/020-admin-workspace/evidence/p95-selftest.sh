#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T006) into p95-selftest/ and runs p95.sh on each,
# checking that every log passes or fails exactly as expected (exit code AND the named FAIL row).
# Synthetic logs only: the real post log (workspace-timings.log) does not exist before T059.
#   bash evidence/p95-selftest.sh | tee evidence/p95-selftest.txt
# Exit status = number of broken expectations.
set -uo pipefail
E=$(cd "$(dirname "$0")" && pwd)
D=$E/p95-selftest
rm -rf "$D"; mkdir -p "$D/pre" "$D/pre-99" "$D/pre-empty"

# blk METHOD ROUTE STATUS N MS READS: N request lines, durations MS.1 .. MS.9, MS.0 (route template)
blk() {
  awk -v m="$1" -v r="$2" -v s="$3" -v n="$4" -v ms="$5" -v rd="$6" \
    'BEGIN { for (i = 1; i <= n; i++) printf "http.request %s %s %s %d.%d %s\n", m, r, s, ms, i % 10, rd }'
}

# ---- synthetic pre dir (same shape as evidence/pre-timings: C-17 per-source logs) ----
pre() {  # $1 dir; TILE_N overrides the library sample size
  local d=$1
  { echo "Now listening on: http://127.0.0.1:1"
    blk GET /admin/ 200 1 1 0
    blk GET /admin/library 200 "${TILE_N:-100}" 10 4
    blk GET '/admin/galleries/{gid}' 200 100 10 1
    echo "op.timing db.library.page 1.2 ok"; } > "$d/tile.log"
  { blk GET /admin/library 200 30 10 4
    blk POST '/admin/library/images/{iid}' 303 30 10 0
    blk POST '/admin/galleries/{gid}' 303 30 10 0
    blk POST '/admin/galleries/{gid}/remove' 303 30 10 0
    blk POST '/admin/galleries/{gid}/order' 303 30 10 0
    blk POST '/admin/galleries/{gid}/cover' 303 30 10 0
    blk POST '/admin/galleries/{gid}/visibility' 303 30 90 0   # not a row: ignored
    blk POST '/admin/library/images/{iid}' 400 3 900 0; } > "$d/library.log"  # non-303: ignored
  { blk POST '/admin/library/images/{iid}' 303 100 10 0; } > "$d/membership.log"
}
pre "$D/pre"
TILE_N=99 pre "$D/pre-99"
pre "$D/pre-empty"; : > "$D/pre-empty/tile.log"

# ---- post log; every knob defaults to the passing value ----
post() {  # $1 file
  { echo "Now listening on: http://127.0.0.1:2"
    blk GET "${W1R:-/admin}" 200 "${W1F_N:-100}" "${W1F_MS:-10}" 4
    blk GET "${W1R:-/admin}" 200 100 "${W1M_MS:-10}" 2
    blk GET "${W1R:-/admin}" 200 3 10 "${W1_X:-1}"                  # chunk/`more` lines
    blk GET /admin/unplaced 200 2 10 4; blk GET /admin/unplaced 200 2 10 "${W2_X:-1}"
    blk GET '/admin/g/{gid}' 200 100 "${W3F_MS:-10}" 5
    blk GET '/admin/g/{gid}' 200 100 "${W3M_MS:-10}" "${W3M_RD:-2}"
    blk GET '/admin/g/{gid}' 404 2 9000 1                            # non-200: ignored
    blk GET '/admin/tiles/{iid}' 200 100 3 "${W4_RD:-1}"
    blk POST '/admin/api/photos/{iid}' 200 "${A1_N:-30}" "${A1_MS:-10}" 1
    blk POST '/admin/api/galleries/{gid}' 200 30 "${A5_MS:-10}" 1
    blk POST '/admin/api/galleries/{gid}/photos' 200 30 "${A7_MS:-10}" 1
    blk POST '/admin/api/galleries/{gid}/photos/remove' 200 30 "${A8_MS:-10}" 1
    blk POST '/admin/api/galleries/{gid}/order' 200 30 "${A9_MS:-10}" 1
    blk POST '/admin/api/galleries/{gid}/cover' 200 30 "${A10_MS:-10}" 1
    blk POST '/admin/api/photos/{iid}' 400 2 9000 0                  # non-200: ignored
    echo "op.timing db.library.page 1.2 ok"
    [ "${NO_FORCED:-0}" = 1 ] || echo "evidence.forced-failure it10"
    local i n=${JOINED_N:-9} item
    for i in $(seq 1 "$n"); do
      item=it$i; [ "$i" = 9 ] && [ "${JOIN_FORCED:-0}" = 1 ] && item=it10
      echo "upload.joined b1 $item ${JG:--} ${JP:--} 120.5"
      echo "op.timing upload.join $([ "$i" = 1 ] && echo "${JOIN_MS:-130.2}" || echo 130.2) ok"
    done
    echo "upload.join-refused b1 it10 changed after validation"
    echo "op.timing upload.join 5000.1 fail"; } > "$1"           # non-ok outcome: ignored
}
post "$D/pass.log"
W1R=/admin/ post "$D/pass-slash.log"       # W1 logged as "/admin/" (group MapGet("/")) also counts
W1F_MS=1 W1M_MS=1 W3F_MS=1 W3M_MS=1 A1_MS=1 A5_MS=1 A7_MS=1 A8_MS=1 A9_MS=1 A10_MS=1 post "$D/fast.log"
W1F_MS=20 post "$D/slow-w1-full.log"
W3F_MS=20 post "$D/slow-w3-full.log"
W1M_MS=20 post "$D/slow-w1-main.log"
W3M_MS=20 post "$D/slow-w3-main.log"
A1_MS=20  post "$D/slow-a1.log"
A5_MS=20  post "$D/slow-a5.log"
A7_MS=20  post "$D/slow-a7.log"
A8_MS=20  post "$D/slow-a8.log"
A9_MS=20  post "$D/slow-a9.log"
A10_MS=20 post "$D/slow-a10.log"
W1_X=3    post "$D/reads-w1.log"           # a W1 line with 3 reads
W2_X=5    post "$D/reads-w2.log"
W3M_RD=4  post "$D/reads-w3.log"           # W3 main at 4 reads: W3-main n row AND W3 reads row fail
W4_RD=2   post "$D/reads-w4.log"
W1F_N=99  post "$D/w1-full-99.log"         # under-sampled read row
A1_N=29   post "$D/a1-29.log"              # under-sampled write row
JOINED_N=8 post "$D/joined-8.log"          # 8 joins: joined row AND join-ms (n < 9) fail
JOIN_FORCED=1 post "$D/joined-forced.log"  # the forced item joined
JOIN_MS=2000.5 post "$D/join-slow.log"
NO_FORCED=1 post "$D/no-forced.log"
: > "$D/empty.log"
sed -e 's/"/\\"/g' -e 's/^/{"LogLevel":"Information","Category":"Gallery.Web","Message":"/' -e 's/$/"}/' \
  "$D/pass.log" > "$D/pass-json.log"

bad=0
# expect PREDIR POSTLOG WANT_EXIT [NEEDLE...]: every needle must appear as a line starting "FAIL <needle>"
expect() {
  local pre=$1 post=$2 want=$3 out code n; shift 3
  out=$(bash "$E/p95.sh" "$pre" "$post" 2>&1); code=$?
  local label="$(basename "$pre") + $(basename "$post")"
  if [ "$code" -ne "$want" ]; then
    echo "SELFTEST FAIL $label: expected exit $want, actual $code"; bad=$((bad+1)); printf '%s\n' "$out" | grep -E '^(FAIL|INFO)'; return
  fi
  for n in "$@"; do
    if ! printf '%s\n' "$out" | grep -qF "FAIL $n"; then
      echo "SELFTEST FAIL $label: exit $code but no 'FAIL $n' row"; bad=$((bad+1)); return
    fi
  done
  echo "SELFTEST OK   $label: expected exit $want ($([ "$want" = 0 ] && echo pass || echo fail)), actual $code${*:+; FAIL rows: $*}"
}
P=$D/pre
expect "$P" "$D/pass.log"          0
expect "$P" "$D/pass-json.log"     0
expect "$P" "$D/pass-slash.log"    0
expect "$P" "$D/slow-w1-full.log"  1 "SC-005 W1-full(reads 4) p95"
expect "$P" "$D/slow-w3-full.log"  1 "SC-005 W3-full(reads 5) p95"
expect "$P" "$D/slow-w1-main.log"  1 "SC-005 W1-main(reads 2) p95"
expect "$P" "$D/slow-w3-main.log"  1 "SC-005 W3-main(reads 2) p95"
expect "$P" "$D/slow-a1.log"       1 "SC-006 A1 p95"
expect "$P" "$D/slow-a5.log"       1 "SC-006 A5 p95"
expect "$P" "$D/slow-a7.log"       1 "SC-006 A7 p95"
expect "$P" "$D/slow-a8.log"       1 "SC-006 A8 p95"
expect "$P" "$D/slow-a9.log"       1 "SC-006 A9 p95"
expect "$P" "$D/slow-a10.log"      1 "SC-006 A10 p95"
expect "$P" "$D/reads-w1.log"      1 "R-02 W1 db_reads"
expect "$P" "$D/reads-w2.log"      1 "R-02 W2 db_reads"
expect "$P" "$D/reads-w3.log"      2 "SC-005 W3-main(reads 2) n" "R-02 W3 db_reads"
expect "$P" "$D/reads-w4.log"      1 "R-02 W4 db_reads"
expect "$P" "$D/w1-full-99.log"    1 "SC-005 W1-full(reads 4) n"
expect "$P" "$D/a1-29.log"         1 "SC-006 A1 n"
expect "$P" "$D/joined-8.log"      2 "SC-013 joined" "SC-013 join-ms"
expect "$P" "$D/joined-forced.log" 1 "SC-013 forced item it10"
expect "$P" "$D/join-slow.log"     1 "SC-013 join-ms"
expect "$P" "$D/no-forced.log"     1 "SC-013 forced no"
expect "$P" "$D/empty.log"         1 "missing"
expect "$P" "$D/does-not-exist.log" 1 "missing"
expect "$D/pre-99"    "$D/pass.log" 4 "SC-005 W1-full(reads 4) n" "SC-005 W3-full(reads 5) n" "SC-005 W1-main(reads 2) n" "SC-005 W3-main(reads 2) n"
expect "$D/pre-empty" "$D/pass.log" 1 "missing"
# the real captured pre dir parses and supplies every pre row (post durations of 1 ms always pass)
expect "$E/pre-timings" "$D/fast.log" 0
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad FAILED"
exit "$bad"
