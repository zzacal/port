#!/usr/bin/env bash
# Generates the p95.sh self-test logs (tasks.md T020) and runs p95.sh on each, checking that every
# log passes or fails exactly as expected. Run from the repo root:
#   bash specs/014-gallery-combobox/evidence/p95-selftest.sh | tee specs/014-gallery-combobox/evidence/p95-selftest.txt
set -uo pipefail
E=specs/014-gallery-combobox/evidence
D=$E/p95-selftest
mkdir -p "$D"

# gen FILE MEMBERSHIP_N SAVE_SLOW_COUNT
gen() {
  local f=$1 mn=$2 slow=$3 i
  : > "$f"
  for i in $(seq 1 "$mn"); do echo "op.timing library.image.memberships 2$((i % 10)).5 ok" >> "$f"; done
  echo "op.timing library.image.memberships 4000 refused" >> "$f"   # not ok: ignored
  for i in $(seq 1 100); do echo "http.request POST /admin/library/images/{iid} 303 3$((i % 10)).1 0" >> "$f"; done
  for i in $(seq 1 "$slow"); do echo "http.request POST /admin/library/images/{iid} 303 1500 0" >> "$f"; done
  echo "http.request POST /admin/library/images/{iid} 422 9000 0" >> "$f"  # non-303 ignored
  echo "op.timing db.gallery.create 1.2 ok" >> "$f"
}
gen "$D/pass-plain.log"        100 0
gen "$D/save-slow.log"         100 10   # 10 of 110 over the limit: p95 is 1500, save row fails only
gen "$D/memberships-99.log"    99  0    # under-sampled: memberships row fails only
: > "$D/empty.log"
gen "$D/.tmp.log" 100 0
sed -e 's/"/\\"/g' -e 's/^/{"Timestamp":"2026-11-06T00:00:00Z","LogLevel":"Information","Category":"Gallery.Web","Message":"/' -e 's/$/"}/' "$D/.tmp.log" > "$D/pass-json.log"
rm "$D/.tmp.log"

bad=0
expect() {
  local f=$1 want=$2 needle=${3:-} out code
  out=$(bash "$E/p95.sh" "$D/$f" 2>&1); code=$?
  if [ "$code" -ne "$want" ]; then echo "SELFTEST FAIL $f: exit $code, wanted $want"; bad=$((bad+1)); printf '%s\n' "$out"; return; fi
  if [ -n "$needle" ] && ! printf '%s\n' "$out" | grep -q "^FAIL $needle"; then echo "SELFTEST FAIL $f: no 'FAIL $needle' row"; bad=$((bad+1)); printf '%s\n' "$out"; return; fi
  echo "SELFTEST OK   $f: exit $code${needle:+, FAIL $needle}"
}
expect pass-plain.log      0
expect pass-json.log       0
expect save-slow.log       1 "SC-002 save"
expect memberships-99.log  1 "SC-002 memberships"
expect empty.log           1 "missing"
expect does-not-exist.log  1 "missing"
echo
[ "$bad" -eq 0 ] && echo "SELFTEST ALL OK" || echo "SELFTEST $bad FAILED"
exit "$bad"
