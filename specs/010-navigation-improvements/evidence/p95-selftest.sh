#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T002). Run from the repo root.
set -euo pipefail
D=specs/010-navigation-improvements/evidence/p95-selftest
mkdir -p "$D"
H='/'; G='/g/{id}/{slug}'; P='/g/{id}/{slug}/{imageId}'

# plain_line ROUTE MS READS
gen() { # gen FILE HOME_READS PHOTO_MS GALLERY_N
  local f=$1 hr=$2 pms=$3 gn=$4 i
  : > "$f"
  for i in $(seq 1 100); do echo "http.request GET $H 200 3.$((i % 10)) 1" >> "$f"; done
  [ "$hr" = 2 ] && echo "http.request GET $H 200 3.1 2" >> "$f"
  for i in $(seq 1 "$gn"); do echo "http.request GET $G 200 4.$((i % 10)) 2" >> "$f"; done
  for i in $(seq 1 100); do echo "http.request GET $P 200 $pms 2" >> "$f"; done
  echo "op.timing db.site.read 0.8 ok" >> "$f"
}
gen "$D/pass-plain.log" 1 5.0 100
gen "$D/home-2-reads.log" 2 5.0 100
gen "$D/photo-p95-60.log" 1 60.0 100
gen "$D/gallery-99-lines.log" 1 5.0 99
: > "$D/empty.log"
# JSON console shape: every line is a JSON object with .Message
gen "$D/.tmp.log" 1 5.0 100
sed -e 's/"/\\"/g' -e 's/^/{"Timestamp":"2026-11-01T00:00:00Z","LogLevel":"Information","Category":"Gallery.Web","Message":"/' -e 's/$/"}/' "$D/.tmp.log" > "$D/pass-json.log"
rm "$D/.tmp.log"
ls "$D"
