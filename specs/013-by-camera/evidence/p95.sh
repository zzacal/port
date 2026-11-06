#!/usr/bin/env bash
# 013 SC-004 from logged events alone (tasks.md T030; adapted from 011's p95.sh).
# Usage, from the repo root:
#   bash specs/013-by-camera/evidence/p95.sh [<log> ...]   (default: evidence/camera-timings.log)
# Each log is either the plain test log (one formatted message per line, e.g.
#   http.request GET /c 200 4.2 1
# ) or the app's JSON console log (lines starting with '{' are converted with jq -r '.Message').
# Each camera route is paired with the gallery route it mirrors:
#   /c                 <-> /g                          db_reads == 1
#   /c/{slug}          <-> /g/{id}/{slug}              db_reads == 2
#   /c/{slug}/{iid}    <-> /g/{id}/{slug}/{imageId}    db_reads == 2
# Rows per pair (p95 = nearest-rank, ceil(0.95 n)):
#   reads  every camera line's db_reads equals the budget (an equality: a skipped read fails too)
#   time   camera p95 <= mirror p95 * 1.10 + 1 ms (jitter allowance)
# Any route in a pair with n < MIN_N (default 100, as in 011) fails that pair's rows.
# A missing, empty or unreadable log is a FAIL. Exit status = number of failed rows over all logs.
set -uo pipefail
MIN_N=${MIN_N:-100}
[ $# -ge 1 ] || set -- specs/013-by-camera/evidence/camera-timings.log

CAM="/c /c/{slug} /c/{slug}/{iid}"
MIR="/g /g/{id}/{slug} /g/{id}/{slug}/{imageId}"
NAMES="listing camera photo"
READS="1 2 2"

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

  printf '%s\n' "$lines" | awk -v cam="$CAM" -v mir="$MIR" -v names="$NAMES" -v reads="$READS" -v minn="$MIN_N" '
    function p95(key, n,   i, j, v, a, k) {
      for (i = 1; i <= n; i++) a[i] = d[key, i] + 0
      for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      k = int(0.95 * n); if (k < 0.95 * n) k++
      return a[k]
    }
    BEGIN {
      np = split(cam, C, " "); split(mir, M, " "); split(names, N, " "); split(reads, B, " ")
      for (i = 1; i <= np; i++) { want[C[i]] = 1; want[M[i]] = 1 }
    }
    $1 == "http.request" && NF >= 6 && $2 == "GET" && $4 == "200" && ($3 in want) {
      c[$3]++; d[$3, c[$3]] = $5; r[$3, c[$3]] = $6
      next
    }
    END {
      fails = 0
      for (i = 1; i <= np; i++) {
        nc = c[C[i]] + 0; nm = c[M[i]] + 0
        if (nc < minn || nm < minn) {
          printf "FAIL SC-004 %s n too small: %s n=%d, %s n=%d (need %d each)\n", N[i], C[i], nc, M[i], nm, minn
          fails += 2; continue
        }
        bad = 0; worst = -1
        for (j = 1; j <= nc; j++) { v = r[C[i], j] + 0; if (v != B[i] + 0) { bad++; if (v > worst) worst = v } }
        if (bad > 0) { printf "FAIL SC-004 %s reads %s n=%d: %d lines with db_reads != %d (worst %d)\n", N[i], C[i], nc, bad, B[i], worst; fails++ }
        else printf "PASS SC-004 %s reads %s n=%d every db_reads == %d\n", N[i], C[i], nc, B[i]
        pc = p95(C[i], nc); pm = p95(M[i], nm); lim = pm * 1.10 + 1
        if (pc <= lim) printf "PASS SC-004 %s time %s p95=%.2f ms <= %s p95=%.2f x1.10 +1 = %.2f\n", N[i], C[i], pc, M[i], pm, lim
        else { printf "FAIL SC-004 %s time %s p95=%.2f ms > %s p95=%.2f x1.10 +1 = %.2f\n", N[i], C[i], pc, M[i], pm, lim; fails++ }
      }
      exit fails
    }'
  total_fails=$((total_fails + $?))
done
echo
[ "$total_fails" -eq 0 ] && echo "ALL PASS" || echo "$total_fails FAILED"
exit "$total_fails"
