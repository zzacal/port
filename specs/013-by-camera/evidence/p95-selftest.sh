#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T030) and runs p95.sh on each, checking that every
# log passes or fails exactly as expected. Run from the repo root:
#   bash specs/013-by-camera/evidence/p95-selftest.sh | tee specs/013-by-camera/evidence/p95-selftest.txt
set -uo pipefail
E=specs/013-by-camera/evidence
D=$E/p95-selftest
mkdir -p "$D"

# gen FILE CAMLIST_READS CAMPHOTO_MS CAMERA_N
gen() {
  local f=$1 lr=$2 pms=$3 cn=$4 i
  : > "$f"
  for i in $(seq 1 100); do echo "http.request GET /g 200 3.$((i % 10)) 1" >> "$f"; done
  for i in $(seq 1 100); do echo "http.request GET /g/{id}/{slug} 200 4.$((i % 10)) 2" >> "$f"; done
  for i in $(seq 1 100); do echo "http.request GET /g/{id}/{slug}/{imageId} 200 5.$((i % 10)) 2" >> "$f"; done
  for i in $(seq 1 100); do echo "http.request GET /c 200 3.$((i % 10)) 1" >> "$f"; done
  [ "$lr" != 1 ] && echo "http.request GET /c 200 3.1 $lr" >> "$f"
  for i in $(seq 1 "$cn"); do echo "http.request GET /c/{slug} 200 4.$((i % 10)) 2" >> "$f"; done
  for i in $(seq 1 100); do echo "http.request GET /c/{slug}/{iid} 200 $pms 2" >> "$f"; done
  echo "http.request GET /c/{slug} 404 0.4 1" >> "$f"   # non-200 lines are ignored
  echo "op.timing db.cameras.list 0.8 ok" >> "$f"
}
gen "$D/pass-plain.log"        1 5.0 100
gen "$D/listing-0-reads.log"   0 5.0 100  # == 1: a skipped read fails
gen "$D/listing-2-reads.log"   2 5.0 100
gen "$D/photo-slow.log"        1 9.0 100  # 9.0 > 5.9*1.1+1: fails its own time row only
gen "$D/camera-99-lines.log"   1 5.0 99   # under-sampled route: both camera rows fail
: > "$D/empty.log"
gen "$D/.tmp.log" 1 5.0 100
sed -e 's/"/\\"/g' -e 's/^/{"Timestamp":"2026-11-05T00:00:00Z","LogLevel":"Information","Category":"Gallery.Web","Message":"/' -e 's/$/"}/' "$D/.tmp.log" > "$D/pass-json.log"
rm "$D/.tmp.log"

bad=0
expect() {
  local f=$1 want=$2 needle=${3:-} out code
  out=$(bash "$E/p95.sh" "$D/$f" 2>&1); code=$?
  if [ "$code" -ne "$want" ]; then echo "SELFTEST FAIL $f: exit $code, wanted $want"; bad=$((bad+1)); printf '%s\n' "$out"; return; fi
  if [ -n "$needle" ] && ! printf '%s\n' "$out" | grep -q "^FAIL $needle"; then echo "SELFTEST FAIL $f: no 'FAIL $needle' row"; bad=$((bad+1)); printf '%s\n' "$out"; return; fi
  echo "SELFTEST OK   $f: exit $code${needle:+, FAIL $needle}"
}
expect pass-plain.log 0
expect pass-json.log 0
expect listing-0-reads.log 1 "SC-004 listing reads"
expect listing-2-reads.log 1 "SC-004 listing reads"
expect photo-slow.log 1 "SC-004 photo time"
expect camera-99-lines.log 2 "SC-004 camera n too small"
expect empty.log 1 "missing, unreadable or empty log"
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad BROKEN"
exit "$bad"
