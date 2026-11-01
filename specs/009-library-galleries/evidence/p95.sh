#!/usr/bin/env bash
# 009 timing criteria from logged events alone (contracts/log-events.md). Usage, from the repo root:
#   bash specs/009-library-galleries/evidence/p95.sh <log> [<log> ...]
# Each log is either
#   - the plain test log: one formatted message per line, e.g.
#       http.request GET /admin/library 200 41.7 3
#       http.request POST /admin/galleries/{gid}/order 303 12.4 2
#       edge.lag gallery.reorder 812
#   - or the app's JSON console log (AddJsonConsole). When any line starts with '{', the '{' lines
#     are converted with jq -r '.Message' and every other line is dropped.
# Rows (p95 = nearest-rank, ceil(0.95 n)):
#   SC-002 http.request GET /admin/library 200            p95 <= 500 ms
#   SC-003 http.request POST <route>, one row per 009 POST route (any status)   p95 <= 500 ms
#   SC-005 edge.lag                                        max <= 60000 ms
# n=0 on any row is a FAIL (a format mismatch or an unexercised route, not a fast app). A missing,
# empty or unreadable log is a FAIL. Exit status = number of failed rows over all logs.
set -uo pipefail
[ $# -ge 1 ] || { echo "usage: $0 <log> [<log> ...]" >&2; exit 2; }

ROUTES="/admin/library/images/{iid} /admin/library/add /admin/library/delete /admin/library/images/{iid}/delete /admin/galleries /admin/galleries/order /admin/galleries/{gid}/visibility /admin/galleries/{gid} /admin/galleries/{gid}/order /admin/galleries/{gid}/cover /admin/galleries/{gid}/remove /admin/galleries/{gid}/delete"

total_fails=0
for LOG in "$@"; do
  echo "== $LOG"
  if [ ! -r "$LOG" ] || [ ! -s "$LOG" ]; then
    echo "FAIL missing, unreadable or empty log"; total_fails=$((total_fails + 1)); continue
  fi

  if grep -q '^{' "$LOG"; then
    if ! lines=$(grep '^{' "$LOG" | jq -r '.Message // empty' 2>/dev/null); then
      echo "FAIL JSON log does not parse with jq"; total_fails=$((total_fails + 1)); continue
    fi
    echo "format: JSON console log"
  else
    lines=$(cat "$LOG")
    echo "format: plain message lines"
  fi

  printf '%s\n' "$lines" | awk -v routes="$ROUTES" '
    function p95(arr, n,   i, j, v, a, k) {
      for (i = 1; i <= n; i++) a[i] = arr[i] + 0
      for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      k = int(0.95 * n); if (k < 0.95 * n) k++
      return a[k]
    }
    BEGIN { nr = split(routes, R, " "); for (i = 1; i <= nr; i++) want[R[i]] = 1 }
    $1 == "http.request" && NF >= 6 && $2 == "GET" && $3 == "/admin/library" && $4 == "200" { a2[++n2] = $5; next }
    $1 == "http.request" && NF >= 6 && $2 == "POST" && ($3 in want) { c[$3]++; v[$3, c[$3]] = $5; next }
    $1 == "edge.lag" && NF >= 3 { if (!nl || $3 + 0 > lagmax) lagmax = $3 + 0; nl++; next }
    END {
      fails = 0
      if (n2 == 0) { print "FAIL SC-002 http.request GET /admin/library 200 n=0 (no matching events)"; fails++ }
      else { p = p95(a2, n2)
        if (p <= 500) printf "PASS SC-002 http.request GET /admin/library 200 n=%d p95=%.2f ms <= 500\n", n2, p
        else { printf "FAIL SC-002 http.request GET /admin/library 200 n=%d p95=%.2f ms > 500\n", n2, p; fails++ } }
      for (i = 1; i <= nr; i++) {
        r = R[i]; n = c[r] + 0
        if (n == 0) { printf "FAIL SC-003 POST %s n=0 (no matching events)\n", r; fails++; continue }
        delete t; for (j = 1; j <= n; j++) t[j] = v[r, j]
        p = p95(t, n)
        if (p <= 500) printf "PASS SC-003 POST %s n=%d p95=%.2f ms <= 500\n", r, n, p
        else { printf "FAIL SC-003 POST %s n=%d p95=%.2f ms > 500\n", r, n, p; fails++ }
      }
      if (nl == 0) { print "FAIL SC-005 edge.lag n=0 (no matching events)"; fails++ }
      else if (lagmax <= 60000) printf "PASS SC-005 edge.lag n=%d max=%.2f ms <= 60000\n", nl, lagmax
      else { printf "FAIL SC-005 edge.lag n=%d max=%.2f ms > 60000\n", nl, lagmax; fails++ }
      exit fails
    }'
  total_fails=$((total_fails + $?))
done
echo
[ "$total_fails" -eq 0 ] && echo "ALL PASS" || echo "$total_fails FAILED"
exit "$total_fails"
