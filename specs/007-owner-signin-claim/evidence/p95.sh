#!/usr/bin/env bash
# SC-006 from logged timing events alone (research R-11). Usage, from the repo root:
#   bash specs/007-owner-signin-claim/evidence/p95.sh <log> [<log> ...]
# Each log is either the plain test log (one formatted message per line, e.g.
#   op.timing db.site.claim 3.21 won
#   http.request POST /signin-oidc 302 41.7 0
# ) or the app's JSON console log (lines starting with '{' are converted with jq -r '.Message';
# non-JSON lines such as dotnet-run banners are dropped in that case).
#
# Pairing: http.request is logged when a request ENDS, and requests are sequential in both
# sources, so a callback's ownership events are the db.site.owner.read and db.site.claim op.timing
# lines after the previous http.request line (any route) and before its own.
# Reports per log: callback count, p95 of ownership work (< 50 ms), p95 of the /signin-oidc
# request duration (< 1000 ms), auth.claim count. Exit status = number of failed checks; a log
# with zero callbacks is a FAIL (format mismatch, not a fast app).
set -uo pipefail
[ $# -ge 1 ] || { echo "usage: $0 <log> [<log> ...]" >&2; exit 2; }

total_fails=0
for LOG in "$@"; do
  echo "== $LOG"
  if [ ! -s "$LOG" ]; then echo "FAIL missing or empty log"; total_fails=$((total_fails + 1)); continue; fi

  first=$(grep -m1 -v '^[[:space:]]*$' "$LOG")
  if [[ "$first" == \{* ]] || grep -q '^{' "$LOG"; then
    lines=$(grep '^{' "$LOG" | jq -r '.Message // empty')
  else
    lines=$(cat "$LOG")
  fi

  printf '%s\n' "$lines" | awk '
    function p95(arr, n,   i, j, v, a) {
      for (i = 1; i <= n; i++) a[i] = arr[i]
      for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      return a[int(0.95 * n + 0.999999)]
    }
    $1 == "op.timing" && ($2 == "db.site.owner.read" || $2 == "db.site.claim") { acc += $3; next }
    $1 == "auth.claim" { claims++; next }
    $1 == "http.request" {
      if ($3 == "/signin-oidc") { n++; own[n] = acc; dur[n] = $5 }
      acc = 0; next
    }
    END {
      fails = 0
      if (n == 0) { print "FAIL 0 /signin-oidc callbacks found (format mismatch?)"; exit 1 }
      po = p95(own, n); pd = p95(dur, n)
      printf "callbacks=%d  auth.claim=%d\n", n, claims
      printf "%s SC-006 ownership work p95 %.2f ms < 50\n", (po < 50 ? "PASS" : "FAIL"), po; if (po >= 50) fails++
      printf "%s SC-006 callback duration p95 %.2f ms < 1000\n", (pd < 1000 ? "PASS" : "FAIL"), pd; if (pd >= 1000) fails++
      exit fails
    }'
  total_fails=$((total_fails + $?))
done
echo
[ "$total_fails" -eq 0 ] && echo "ALL PASS" || echo "$total_fails FAILED"
exit "$total_fails"
