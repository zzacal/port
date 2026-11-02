#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T022) and runs p95.sh on each, checking that every
# log passes or fails exactly as expected. Run from the repo root:
#   bash specs/011-home-page/evidence/p95-selftest.sh | tee specs/011-home-page/evidence/p95-selftest.txt
set -uo pipefail
E=specs/011-home-page/evidence
D=$E/p95-selftest
mkdir -p "$D"
H='/'; L='/g'; G='/g/{id}/{slug}'; P='/g/{id}/{slug}/{imageId}'

# gen FILE HOME_READS LISTING_READS PHOTO_MS GALLERY_N
gen() {
  local f=$1 hr=$2 lr=$3 pms=$4 gn=$5 i
  : > "$f"
  for i in $(seq 1 100); do echo "http.request GET $H 200 1.$((i % 10)) 0" >> "$f"; done
  [ "$hr" != 0 ] && echo "http.request GET $H 200 1.1 $hr" >> "$f"
  for i in $(seq 1 100); do echo "http.request GET $L 200 3.$((i % 10)) 1" >> "$f"; done
  [ "$lr" != 1 ] && echo "http.request GET $L 200 3.1 $lr" >> "$f"
  for i in $(seq 1 "$gn"); do echo "http.request GET $G 200 4.$((i % 10)) 2" >> "$f"; done
  for i in $(seq 1 100); do echo "http.request GET $P 200 $pms 2" >> "$f"; done
  echo "op.timing db.site.read 0.8 ok" >> "$f"
}
gen "$D/pass-plain.log"        0 1 5.0 100
gen "$D/home-1-read.log"       1 1 5.0 100   # home must be == 0
gen "$D/listing-0-reads.log"   0 0 5.0 100   # listing must be == 1 (a skipped read fails)
gen "$D/listing-2-reads.log"   0 2 5.0 100
gen "$D/photo-p95-60.log"      0 1 60.0 100  # over-limit time: fails its own row only
gen "$D/gallery-99-lines.log"  0 1 5.0 99    # under-sampled route
: > "$D/empty.log"
gen "$D/.tmp.log" 0 1 5.0 100
sed -e 's/"/\\"/g' -e 's/^/{"Timestamp":"2026-11-02T00:00:00Z","LogLevel":"Information","Category":"Gallery.Web","Message":"/' -e 's/$/"}/' "$D/.tmp.log" > "$D/pass-json.log"
rm "$D/.tmp.log"

# expect FILE EXIT_CODE REQUIRED_FAIL_SUBSTRING
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
expect home-1-read.log 1 "SC-005 home reads"
expect listing-0-reads.log 1 "SC-005 listing reads"
expect listing-2-reads.log 1 "SC-005 listing reads"
expect photo-p95-60.log 1 "SC-006 photo time"
expect gallery-99-lines.log 2 "SC-005 gallery reads"
expect empty.log 1 "missing, unreadable or empty log"
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad BROKEN"
exit "$bad"
