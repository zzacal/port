#!/usr/bin/env bash
# 008 timing criteria from logged events alone (contracts/log-events.md). Usage, from the repo root:
#   bash specs/008-upload-pipeline/evidence/p95.sh <log> [<log> ...]
# Each log is either
#   - the plain test log: one formatted message per line, e.g.
#       op.timing s3.presign 1.84 ok
#       upload.confirmed 3kq9x2m4 40 5123.4
#       http.request GET /admin/upload 200 41.7 1
#   - or the app's JSON console log (AddJsonConsole). When any line starts with '{', the '{' lines
#     are converted with jq -r '.Message' and every other line (dotnet banners) is dropped.
# Rows (p95 = nearest-rank, ceil(0.95 n)):
#   SC-006 op.timing s3.presign            p95 <= 200 ms
#   SC-007 op.timing upload.validate       p95 <= 2000 ms
#   SC-008 upload.confirmed, count >= 40   p95 duration_ms <= 60000 ms
#   SC-009 op.timing browser.total         p95 <= 60000 ms; n=0 prints "n/a (owner live run)",
#                                          which is neither a pass nor a failure
#   SC-010 http.request GET /admin/upload 200  p95 <= 500 ms
# n=0 on SC-006/007/008/010 is a FAIL (format mismatch, not a fast app). A missing, empty or
# unreadable log is a FAIL. Exit status = number of failed rows over all logs.
set -uo pipefail
[ $# -ge 1 ] || { echo "usage: $0 <log> [<log> ...]" >&2; exit 2; }

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

  printf '%s\n' "$lines" | awk '
    function p95(arr, n,   i, j, v, a) {
      for (i = 1; i <= n; i++) a[i] = arr[i] + 0
      for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      k = int(0.95 * n); if (k < 0.95 * n) k++
      return a[k]
    }
    function row(id, label, arr, n, limit, optional,   p) {
      if (n == 0) {
        if (optional) { printf "n/a  %s %s n=0 (owner live run)\n", id, label; return }
        printf "FAIL %s %s n=0 (no matching events)\n", id, label; fails++; return
      }
      p = p95(arr, n)
      if (p <= limit) printf "PASS %s %s n=%d p95=%.2f ms <= %d\n", id, label, n, p, limit
      else { printf "FAIL %s %s n=%d p95=%.2f ms > %d\n", id, label, n, p, limit; fails++ }
    }
    $1 == "op.timing" && NF >= 4 && $2 == "s3.presign"      { a6[++n6] = $3; next }
    $1 == "op.timing" && NF >= 4 && $2 == "upload.validate" { a7[++n7] = $3; next }
    $1 == "op.timing" && NF >= 4 && $2 == "browser.total"   { a9[++n9] = $3; next }
    $1 == "upload.confirmed" && NF >= 4 {
      if ($3 + 0 >= 40) a8[++n8] = $4; else small8++
      next
    }
    $1 == "http.request" && NF >= 6 && $2 == "GET" && $3 == "/admin/upload" && $4 == "200" { a10[++n10] = $5; next }
    END {
      fails = 0
      row("SC-006", "op.timing s3.presign", a6, n6, 200, 0)
      row("SC-007", "op.timing upload.validate", a7, n7, 2000, 0)
      row("SC-008", "upload.confirmed count>=40", a8, n8, 60000, 0)
      if (small8 > 0) printf "     (SC-008 ignored %d upload.confirmed with count < 40)\n", small8
      row("SC-009", "op.timing browser.total", a9, n9, 60000, 1)
      row("SC-010", "http.request GET /admin/upload 200", a10, n10, 500, 0)
      exit fails
    }'
  total_fails=$((total_fails + $?))
done
echo
[ "$total_fails" -eq 0 ] && echo "ALL PASS" || echo "$total_fails FAILED"
exit "$total_fails"
