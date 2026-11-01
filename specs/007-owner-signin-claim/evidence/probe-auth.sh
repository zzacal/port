#!/usr/bin/env bash
# Probe: session cookie attributes and antiforgery on bare POSTs (007 research R-03/R-04).
B=https://localhost:5191
J=$(mktemp)
echo "== session cookie attributes"
curl -sS --cacert /dev/null -k -c "$J" -D - -o /dev/null $B/fake | grep -i set-cookie | sed -E 's/=CfDJ[^;]*/=<redacted>/'
echo "== bare POSTs, no token, with session"
for p in bare meta manual; do
  printf '%-7s no-body: ' $p; curl -sk -b "$J" -o /dev/null -w '%{http_code}\n' -X POST $B/$p
  printf '%-7s form   : ' $p; curl -sk -b "$J" -o /dev/null -w '%{http_code}\n' -X POST -d 'x=1' $B/$p
done
echo "== with valid token (form field / header)"
T=$(curl -sk -b "$J" -c "$J" $B/token)
F=${T%%|*}; V=${T#*|}
for p in meta manual; do
  printf '%-7s form-field: ' $p; curl -sk -b "$J" -o /dev/null -w '%{http_code}\n' -X POST --data-urlencode "$F=$V" $B/$p
  printf '%-7s header    : ' $p; curl -sk -b "$J" -o /dev/null -w '%{http_code}\n' -X POST -H "X-CSRF-TOKEN: $V" $B/$p
done
echo "== antiforgery cookie attributes"
grep -i "__Host-af" "$J" | awk '{print "domain="$1, "secure="$4, "path="$3}'
rm -f "$J"
