#!/usr/bin/env bash
# 012 T015: drive anonymous public traffic so the hosted-log queries (kql/reads.kql, kql/p95.kql)
# have >= 100 lines per public route. GETs only, no cookie, never the origin secret.
#
#   bash specs/012-production-infrastructure/evidence/drive-prod.sh https://port.zacal.me
#
# 100 GETs each of /, /g, one gallery and one photo (ids read from the live listing), each with
# ?n=<i> so every request is a distinct edge cache key and reaches the origin. At most 5 requests
# per second overall. Prints a count per route and status; exit 1 when any route got fewer than
# 100 answers of 200.
set -uo pipefail
[ $# -ge 1 ] || { echo "usage: $0 <base-url>" >&2; exit 2; }
B=${1%/}
N=${DRIVE_N:-100}
CURL=(curl -s --max-time 20 --connect-timeout 5)

listing=$("${CURL[@]}" "$B/g" 2>/dev/null || true)
g=$(printf '%s' "$listing" | grep -oE 'href="/g/[a-z2-7]{26}/[^"/#]+"' | head -1 | sed -E 's/href="([^"]*)"/\1/')
[ -n "$g" ] || { echo "FAIL no gallery link on $B/g"; exit 1; }
p=$("${CURL[@]}" "$B$g" 2>/dev/null | grep -oE "href=\"$g/[a-z2-7]{26}\"" | head -1 | sed -E 's/href="([^"]*)"/\1/')
[ -n "$p" ] || { echo "FAIL gallery $g links no photo"; exit 1; }
echo "base $B  gallery $g  photo $p  $N GETs per route, <= 5/s"
echo "started $(date -u +%Y-%m-%dT%H:%M:%SZ) (pass this to kql.sh --since)"

fails=0
for route in / /g "$g" "$p"; do
  : > /tmp/012-drive.$$
  for i in $(seq 1 "$N"); do
    t0=$(perl -MTime::HiRes=time -e 'printf "%.3f", time')
    "${CURL[@]}" -o /dev/null -w '%{http_code}\n' "$B$route?n=$i" >> /tmp/012-drive.$$ 2>/dev/null || echo 000 >> /tmp/012-drive.$$
    # pace to >= 0.2 s per request (5 per second)
    perl -MTime::HiRes=time,sleep -e '$d = 0.2 - (time - $ARGV[0]); sleep $d if $d > 0' "$t0"
  done
  ok200=$(grep -cx 200 /tmp/012-drive.$$ || true)
  dist=$(sort /tmp/012-drive.$$ | uniq -c | awk '{printf "%s x%s ", $2, $1}')
  echo "$route: $ok200/$N 200  ($dist)"
  [ "$ok200" -ge "$N" ] || fails=$((fails+1))
done
rm -f /tmp/012-drive.$$
echo "ended $(date -u +%Y-%m-%dT%H:%M:%SZ)"
[ "$fails" -eq 0 ] && echo "ALL ROUTES $N/$N" || echo "$fails route(s) short of $N"
exit $((fails > 0))
