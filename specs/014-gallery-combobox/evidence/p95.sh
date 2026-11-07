#!/usr/bin/env bash
# 014 SC-002 from logged events alone (tasks.md T020; adapted from 013's p95.sh).
# Usage, from the repo root:
#   bash specs/014-gallery-combobox/evidence/p95.sh [<log> ...] | tee specs/014-gallery-combobox/evidence/p95-output.txt
#   (default log: evidence/membership-timings.log)
# Each log is either the plain test log (one formatted message per line, e.g.
#   op.timing library.image.memberships 12.3 ok
#   http.request POST /admin/library/images/{iid} 303 24.59 0
# ) or the app's JSON console log (lines starting with '{' are converted with jq -r '.Message').
# Rows (p95 = nearest-rank, ceil(0.95 n)), each needs n >= MIN_N (default 100):
#   memberships  op.timing library.image.memberships ... ok      p95 < 1000 ms
#   save         http.request POST /admin/library/images/{iid} 303  p95 < 1000 ms
# A missing, empty or unreadable log is a FAIL. Exit status = number of failed rows over all logs.
set -uo pipefail
MIN_N=${MIN_N:-100}
LIMIT=${LIMIT:-1000}
[ $# -ge 1 ] || set -- specs/014-gallery-combobox/evidence/membership-timings.log

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

  printf '%s\n' "$lines" | awk -v minn="$MIN_N" -v lim="$LIMIT" '
    function p95(key, n,   i, j, v, a, k) {
      for (i = 1; i <= n; i++) a[i] = d[key, i] + 0
      for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      k = int(0.95 * n); if (k < 0.95 * n) k++
      return a[k]
    }
    $1 == "op.timing" && $2 == "library.image.memberships" && $4 == "ok" { c["m"]++; d["m", c["m"]] = $3; next }
    $1 == "http.request" && $2 == "POST" && $3 == "/admin/library/images/{iid}" && $4 == "303" { c["s"]++; d["s", c["s"]] = $5; next }
    END {
      fails = 0
      split("m s", K, " ")
      N["m"] = "memberships op.timing library.image.memberships ok"
      N["s"] = "save http.request POST /admin/library/images/{iid} 303"
      for (i = 1; i <= 2; i++) {
        k = K[i]; n = c[k] + 0
        if (n < minn) { printf "FAIL SC-002 %s n=%d (need %d)\n", N[k], n, minn; fails++; continue }
        p = p95(k, n)
        if (p < lim) printf "PASS SC-002 %s n=%d p95=%.2f ms < %d\n", N[k], n, p, lim
        else { printf "FAIL SC-002 %s n=%d p95=%.2f ms >= %d\n", N[k], n, p, lim; fails++ }
      }
      exit fails
    }'
  total_fails=$((total_fails + $?))
done
echo
[ "$total_fails" -eq 0 ] && echo "ALL PASS" || echo "$total_fails FAILED"
exit "$total_fails"
