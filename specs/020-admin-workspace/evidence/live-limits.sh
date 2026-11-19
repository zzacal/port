#!/usr/bin/env bash
# 020 T067 (quickstart Live walk step 9): body limits on a REAL Kestrel host, invisible to in-process tests.
# Usage: LIVE=https://localhost:5092 PWFILE=<file holding LocalSignIn:Password> bash live-limits.sh
# Runs against the owner's dev data, so it WRITES NOTHING: every write targets an unknown gallery or
# unknown photo ids (answer 404/422, never a change), and the 100-file register sends files the
# per-file checks refuse (nothing registered). Prints status and body size per request; FAIL when a
# body under its limit answers 413 or the over-limit body does not.
set -uo pipefail
LIVE=${LIVE:-https://localhost:5092}
J=$(mktemp); trap 'rm -f "$J"' EXIT
C=(curl -sk -c "$J" -b "$J")
tok() { "${C[@]}" "$1" | sed -nE 's/.*name="__RequestVerificationToken"[^>]*value="([^"]+)".*/\1/p' | head -1; }

t=$(tok "$LIVE/admin/signin")
P=$(mktemp); tr -d '\n' < "$PWFILE" > "$P"; trap 'rm -f "$J" "$P"' EXIT
code=$("${C[@]}" -o /dev/null -w '%{http_code}' -X POST "$LIVE/admin/signin/local" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "password@$P")
echo "sign-in: $code (expect 302)"
csrf=$("${C[@]}" "$LIVE/admin" | sed -nE 's/.*data-csrf="([^"]+)".*/\1/p' | head -1)
[ -n "$csrf" ] || { echo "FAIL no data-csrf on /admin (not signed in?)"; exit 1; }

ids() { python3 -c "import sys;n=int(sys.argv[1]);print(','.join('\"%s\"'%(('z'*24)+'%02d'%(i%100) if i<100 else 'y'*23+'%03d'%i) for i in range(n)))" "$1"; }
G=zzzzzzzzzzzzzzzzzzzzzzzzz2   # valid opaque id, no such gallery
fails=0
post() {  # $1 label, $2 path, $3 body file, $4 expect: ok|413
  local size code; size=$(wc -c < "$3" | tr -d ' ')
  code=$("${C[@]}" -o /dev/null -w '%{http_code}' -X POST "$LIVE$2" -H "X-CSRF-TOKEN: $csrf" \
    -H 'Content-Type: application/json' --data-binary "@$3")
  if [ "$4" = 413 ]; then [ "$code" = 413 ] && r=PASS || { r=FAIL; fails=$((fails+1)); }
  else [ "$code" != 413 ] && r=PASS || { r=FAIL; fails=$((fails+1)); }; fi
  printf '%s %-34s %6s bytes -> %s (expect %s)\n' "$r" "$1" "$size" "$code" "$( [ "$4" = 413 ] && echo 413 || echo 'not 413')"
}
B=$(mktemp -d)
# U3: 100 files, each with a name of 900 characters and a size of 0 (refused per file, nothing registered).
python3 - "$B/u3.json" <<'EOF'
import json,sys
files=[{"name":("p%03d-"%i)+"x"*894+".jpg","size":0,"type":"image/jpeg","lastModified":0} for i in range(100)]
open(sys.argv[1],"w").write(json.dumps({"files":files}))
EOF
echo "{\"order\":[$(ids 300)]}" > "$B/a9.json"
echo "{\"ids\":[$(ids 100)]}" > "$B/ids.json"
python3 -c "import sys;open(sys.argv[1],'w').write('{\"order\":[\"'+'a'*(64*1024)+'\"]}')" "$B/a9-big.json"
post "U3 register 100 files"           /admin/upload/items                   "$B/u3.json"     ok
post "A9 order of 300 photos"          "/admin/api/galleries/$G/order"       "$B/a9.json"     ok
post "A2 delete 100 ids"               /admin/api/photos/delete              "$B/ids.json"    ok
post "A7 add 100 ids"                  "/admin/api/galleries/$G/photos"      "$B/ids.json"    ok
post "A8 remove 100 ids"               "/admin/api/galleries/$G/photos/remove" "$B/ids.json"  ok
post "A9 at 64 KiB + overhead"         "/admin/api/galleries/$G/order"       "$B/a9-big.json" 413
rm -rf "$B"
echo; [ $fails -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"; exit $fails
