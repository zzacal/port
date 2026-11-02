#!/usr/bin/env bash
# 010 SC-006 from logged events alone (contracts/log-events.md). Usage, from the repo root:
#   bash specs/010-navigation-improvements/evidence/p95.sh <log> [<log> ...]
# Each log is either
#   - the plain test log: one formatted message per line, e.g.
#       http.request GET /g/{id}/{slug} 200 4.2 2
#   - or the app's JSON console log (AddJsonConsole). When any line starts with '{', the '{' lines
#     are converted with jq -r '.Message' and every other line is dropped.
# Rows (p95 = nearest-rank, ceil(0.95 n)); n < 100 on any row FAILS it:
#   SC-006 home reads     http.request GET / 200                        every db_reads == 1
#   SC-006 gallery reads  http.request GET /g/{id}/{slug} 200           every db_reads <= 2
#   SC-006 photo reads    http.request GET /g/{id}/{slug}/{imageId} 200 every db_reads <= 2
#   SC-006 home/gallery/photo time                                      p95 duration_ms < 50
# A missing, empty or unreadable log is a FAIL. Exit status = number of failed rows over all logs.
set -uo pipefail
[ $# -ge 1 ] || { echo "usage: $0 <log> [<log> ...]" >&2; exit 2; }

ROUTES="/ /g/{id}/{slug} /g/{id}/{slug}/{imageId}"
NAMES="home gallery photo"
MAXREADS="1 2 2"

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

  printf '%s\n' "$lines" | awk -v routes="$ROUTES" -v names="$NAMES" -v maxreads="$MAXREADS" '
    function p95(arr, n,   i, j, v, a, k) {
      for (i = 1; i <= n; i++) a[i] = arr[i] + 0
      for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      k = int(0.95 * n); if (k < 0.95 * n) k++
      return a[k]
    }
    BEGIN {
      nr = split(routes, R, " "); split(names, N, " "); split(maxreads, M, " ")
      for (i = 1; i <= nr; i++) idx[R[i]] = i
    }
    $1 == "http.request" && NF >= 6 && $2 == "GET" && $4 == "200" && ($3 in idx) {
      i = idx[$3]; c[i]++; d[i, c[i]] = $5
      # home: exactly M (1); the others: at most M.
      if ($6 + 0 > M[i] + 0 || (i == 1 && $6 + 0 != M[i] + 0)) { bad[i]++; if ($6 + 0 > worst[i] + 0) worst[i] = $6 + 0 }
      next
    }
    END {
      fails = 0
      for (i = 1; i <= nr; i++) {
        n = c[i] + 0
        if (n < 100) { printf "FAIL SC-006 %s reads http.request GET %s 200 n=%d < 100\n", N[i], R[i], n; fails++ }
        else if (bad[i] + 0 > 0) { printf "FAIL SC-006 %s reads n=%d: %d lines break db_reads %s%s (max %d)\n", N[i], n, bad[i], (i == 1 ? "== " : "<= "), M[i], worst[i]; fails++ }
        else printf "PASS SC-006 %s reads n=%d every db_reads <= %s%s\n", N[i], n, M[i], (M[i] == 1 ? " (exactly 1)" : "")
      }
      for (i = 1; i <= nr; i++) {
        n = c[i] + 0
        if (n < 100) { printf "FAIL SC-006 %s time http.request GET %s 200 n=%d < 100\n", N[i], R[i], n; fails++; continue }
        delete t; for (j = 1; j <= n; j++) t[j] = d[i, j]
        p = p95(t, n)
        if (p < 50) printf "PASS SC-006 %s time n=%d p95=%.2f ms < 50\n", N[i], n, p
        else { printf "FAIL SC-006 %s time n=%d p95=%.2f ms >= 50\n", N[i], n, p; fails++ }
      }
      exit fails
    }'
  total_fails=$((total_fails + $?))
done
echo
[ "$total_fails" -eq 0 ] && echo "ALL PASS" || echo "$total_fails FAILED"
exit "$total_fails"
