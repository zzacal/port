#!/usr/bin/env bash
# 015 SC-005 from logged events alone (tasks.md T005; adapted from 014's p95.sh).
# Usage, from the repo root:
#   bash specs/015-tile-icon-controls/evidence/p95.sh <pre.log> <post.log> | tee specs/015-tile-icon-controls/evidence/p95-output.txt
#   (defaults: evidence/pre-timings.log evidence/tile-timings.log)
# Each log is the plain test log (one formatted message per line, e.g.
#   http.request GET /admin/library 200 12.30 4
# ) or the app's JSON console log (lines starting with '{' are converted with jq -r '.Message').
# Rows, per route (library = GET /admin/library 200, builder = GET /admin/galleries/{gid} 200):
#   n        both logs need n >= MIN_N (default 100)
#   p95      post p95 <= pre p95 * 1.10 + 1 ms   (p95 = nearest-rank, ceil(0.95 n))
#   reads    every post line's db_reads EQUALS the pre value (the pre log must have one value)
# A missing, empty or unreadable log is a FAIL. Exit status = number of failed rows.
set -uo pipefail
MIN_N=${MIN_N:-100}
E=specs/015-tile-icon-controls/evidence
PRE=${1:-$E/pre-timings.log}
POST=${2:-$E/tile-timings.log}

messages() {  # $1 = log; prints message lines, or returns 1
  local log=$1
  [ -r "$log" ] && [ -s "$log" ] || return 1
  if grep -q '^{' "$log"; then grep '^{' "$log" | jq -r '.Message // empty' 2>/dev/null || return 1
  else cat "$log"; fi
}

echo "== pre:  $PRE"
echo "== post: $POST"
fails=0
pre=$(messages "$PRE") || { echo "FAIL missing, unreadable or empty log: $PRE"; fails=$((fails+1)); }
post=$(messages "$POST") || { echo "FAIL missing, unreadable or empty log: $POST"; fails=$((fails+1)); }
if [ "$fails" -gt 0 ]; then echo; echo "$fails FAILED"; exit "$fails"; fi

{ printf '%s\n' "$pre" | sed 's/^/PRE /'; printf '%s\n' "$post" | sed 's/^/POST /'; } | awk -v minn="$MIN_N" '
  function p95(key, n,   i, j, v, a, k) {
    for (i = 1; i <= n; i++) a[i] = d[key, i] + 0
    for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
    k = int(0.95 * n); if (k < 0.95 * n) k++
    return a[k]
  }
  $2 == "http.request" && $3 == "GET" && $5 == "200" && ($4 == "/admin/library" || $4 == "/admin/galleries/{gid}") {
    r = ($4 == "/admin/library") ? "library" : "builder"
    key = $1 SUBSEP r
    c[key]++; d[key, c[key]] = $6
    rd[key, $7] = 1
    if (!((key) in rds)) rds[key] = ""
    if (index(" " rds[key] " ", " " $7 " ") == 0) rds[key] = rds[key] (rds[key] == "" ? "" : " ") $7
  }
  END {
    fails = 0
    split("library builder", R, " ")
    for (i = 1; i <= 2; i++) {
      r = R[i]; pk = "PRE" SUBSEP r; qk = "POST" SUBSEP r
      np = c[pk] + 0; nq = c[qk] + 0
      if (np < minn || nq < minn) { printf "FAIL SC-005 %s n pre=%d post=%d (need %d each)\n", r, np, nq, minn; fails++; continue }
      printf "PASS SC-005 %s n pre=%d post=%d\n", r, np, nq
      pp = p95(pk, np); qp = p95(qk, nq); lim = pp * 1.10 + 1
      if (qp <= lim) printf "PASS SC-005 %s p95 post=%.2f ms <= pre %.2f * 1.10 + 1 = %.2f\n", r, qp, pp, lim
      else { printf "FAIL SC-005 %s p95 post=%.2f ms > pre %.2f * 1.10 + 1 = %.2f\n", r, qp, pp, lim; fails++ }
      if (split(rds[pk], P, " ") != 1) { printf "FAIL SC-005 %s db_reads pre log has values {%s}, expected one\n", r, rds[pk]; fails++; continue }
      if (rds[qk] == rds[pk]) printf "PASS SC-005 %s db_reads every post line == %s\n", r, rds[pk]
      else { printf "FAIL SC-005 %s db_reads post {%s} != pre %s\n", r, rds[qk], rds[pk]; fails++ }
    }
    exit fails
  }'
fails=$?
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
