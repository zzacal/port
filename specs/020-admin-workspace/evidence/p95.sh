#!/usr/bin/env bash
# 020 SC-005, SC-006, SC-013 and R-02 read counts from logged events alone (tasks.md T006, C-08, C-17;
# adapted from 017's p95.sh).
# Usage (from specs/020-admin-workspace, or with repo-root paths):
#   bash evidence/p95.sh evidence/pre-timings evidence/workspace-timings.log | tee evidence/p95-output.txt
#   (defaults: <this dir>/pre-timings <this dir>/workspace-timings.log)
# PRE is the DIRECTORY of per-source pre logs (C-17): tile.log, library.log, membership.log are read
# (list.log and photo.log are not needed by any row). POST is the T059 log.
# Each log is the plain test log (one formatted message per line), or the app's JSON console log
# (when any line starts with '{', only those lines are read, converted with jq -r '.Message').
# Request line (C-08): http.request {method} {route-template} {status} {duration_ms} {db_reads}.
#
# W1 route: the middleware logs RoutePattern.RawText. A group-relative MapGet("/") on the /admin
# group logs "/admin/" (the pre logs show exactly that for today's admin root), MapGet("") logs
# "/admin". Both spellings count as W1; nothing else does.
#
# Rows (p95 = nearest-rank, ceil(0.95 n); pass: post p95 <= pre p95 * 1.10 + 1 ms):
#   SC-005  pre = tile.log `GET /admin/library 200` (n >= MIN_N), post GET 200 lines (n >= MIN_N each):
#           W1 full (db_reads 4), W3 full `/admin/g/{gid}` (db_reads 5), W1 main (2), W3 main (2).
#           INFO (no pass/fail): W3 full vs tile.log builder `GET /admin/galleries/{gid} 200`.
#   R-02    every GET/HEAD 200 line: W1 and W2 (`/admin/unplaced`) db_reads in {4,2,1}; W3 in {5,2};
#           W4 `/admin/tiles/{iid}` == 1. W1, W3, W4 must have lines; W2 with no lines is INFO only
#           (T059 drives no W2 request).
#   SC-006  post 200 lines (n >= MIN_W) vs the pre 303 lines they replace (pre n as captured, >= 1,
#           printed): A1 `/admin/api/photos/{iid}` vs library.log `POST /admin/library/images/{iid}`;
#           A5 `/admin/api/galleries/{gid}` vs library.log `POST /admin/galleries/{gid}`;
#           A7 `/admin/api/galleries/{gid}/photos` vs membership.log `POST /admin/library/images/{iid}`;
#           A8 `.../photos/remove` vs `POST /admin/galleries/{gid}/remove`; A9 `.../order` vs
#           `POST /admin/galleries/{gid}/order`; A10 `.../cover` vs `POST /admin/galleries/{gid}/cover`.
#   SC-013  exactly JOINS (9) `upload.joined` lines; an `evidence.forced-failure {item}` line exists
#           and no `upload.joined` names that item; every `op.timing upload.join .. ok` <= 2000 ms,
#           with at least JOINS such lines (an absent timing is not a pass).
# A missing, empty or unreadable log is a FAIL. Exit status = number of failed rows.
set -uo pipefail
MIN_N=${MIN_N:-100}
MIN_W=${MIN_W:-30}
JOINS=${JOINS:-9}
E=$(cd "$(dirname "$0")" && pwd)
PRE=${1:-$E/pre-timings}
POST=${2:-$E/workspace-timings.log}

messages() {  # $1 = log; prints message lines, or returns 1
  local log=$1
  [ -f "$log" ] && [ -r "$log" ] && [ -s "$log" ] || return 1
  if grep -q '^{' "$log"; then grep '^{' "$log" | jq -r '.Message // empty' 2>/dev/null || return 1
  else cat "$log"; fi
}

echo "== pre:  $PRE/{tile,library,membership}.log"
echo "== post: $POST"
fails=0
tile=$(messages "$PRE/tile.log") || { echo "FAIL missing, unreadable or empty log: $PRE/tile.log"; fails=$((fails+1)); }
lib=$(messages "$PRE/library.log") || { echo "FAIL missing, unreadable or empty log: $PRE/library.log"; fails=$((fails+1)); }
mem=$(messages "$PRE/membership.log") || { echo "FAIL missing, unreadable or empty log: $PRE/membership.log"; fails=$((fails+1)); }
post=$(messages "$POST") || { echo "FAIL missing, unreadable or empty log: $POST"; fails=$((fails+1)); }
if [ "$fails" -gt 0 ]; then echo; echo "$fails FAILED"; exit "$fails"; fi

{ printf '%s\n' "$tile" | sed 's/^/TILE /'
  printf '%s\n' "$lib"  | sed 's/^/LIB /'
  printf '%s\n' "$mem"  | sed 's/^/MEM /'
  printf '%s\n' "$post" | sed 's/^/POST /'; } | awk -v minn="$MIN_N" -v minw="$MIN_W" -v joins="$JOINS" '
  function add(key, ms) { c[key]++; d[key, c[key]] = ms + 0 }
  function p95(key, n,   i, j, v, a, k) {
    for (i = 1; i <= n; i++) a[i] = d[key, i]
    for (i = 2; i <= n; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
    k = int(0.95 * n); if (k < 0.95 * n) k++
    return a[k]
  }
  function rset(w, rd) { rn[w]++; if (index(" " rs[w] " ", " " rd " ") == 0) rs[w] = rs[w] (rs[w] == "" ? "" : " ") rd }
  # cmp(sc, label, prekey, postkey, minpre, minpost, info)
  function cmp(sc, label, pk, qk, minpre, minpost, info,   np, nq, pp, qp, lim) {
    np = c[pk] + 0; nq = c[qk] + 0
    if (np < minpre || nq < minpost) {
      if (info) { printf "INFO %s %s n pre=%d post=%d (too few to compare)\n", sc, label, np, nq; return }
      printf "FAIL %s %s n pre=%d post=%d (need pre >= %d, post >= %d)\n", sc, label, np, nq, minpre, minpost; fails++; return
    }
    pp = p95(pk, np); qp = p95(qk, nq); lim = pp * 1.10 + 1
    if (info) { printf "INFO %s %s n pre=%d post=%d p95 post=%.2f ms vs pre %.2f * 1.10 + 1 = %.2f\n", sc, label, np, nq, qp, pp, lim; return }
    printf "PASS %s %s n pre=%d post=%d\n", sc, label, np, nq
    if (qp <= lim) printf "PASS %s %s p95 post=%.2f ms <= pre %.2f * 1.10 + 1 = %.2f\n", sc, label, qp, pp, lim
    else { printf "FAIL %s %s p95 post=%.2f ms > pre %.2f * 1.10 + 1 = %.2f\n", sc, label, qp, pp, lim; fails++ }
  }
  # reads(w, allowed, required)
  function reads(w, allowed, required,   n, i, V, bad) {
    if (rn[w] + 0 == 0) {
      if (required) { printf "FAIL R-02 %s db_reads no GET 200 lines\n", w; fails++ }
      else printf "INFO R-02 %s db_reads no GET 200 lines\n", w
      return
    }
    bad = ""; n = split(rs[w], V, " ")
    for (i = 1; i <= n; i++) if (index(" " allowed " ", " " V[i] " ") == 0) bad = bad (bad == "" ? "" : " ") V[i]
    if (bad == "") printf "PASS R-02 %s db_reads every line (n=%d) in {%s}: seen {%s}\n", w, rn[w], allowed, rs[w]
    else { printf "FAIL R-02 %s db_reads seen {%s}, not in {%s}: {%s}\n", w, rs[w], allowed, bad; fails++ }
  }
  $2 == "http.request" {
    src = $1; m = $3; r = $4; s = $5; ms = $6; rd = $7
    if (src == "TILE" && m == "GET" && s == "200") {
      if (r == "/admin/library") add("pre.lib", ms)
      else if (r == "/admin/galleries/{gid}") add("pre.builder", ms)
    } else if (src == "LIB" && m == "POST" && s == "303") {
      if (r == "/admin/library/images/{iid}") add("pre.A1", ms)
      else if (r == "/admin/galleries/{gid}") add("pre.A5", ms)
      else if (r == "/admin/galleries/{gid}/remove") add("pre.A8", ms)
      else if (r == "/admin/galleries/{gid}/order") add("pre.A9", ms)
      else if (r == "/admin/galleries/{gid}/cover") add("pre.A10", ms)
    } else if (src == "MEM" && m == "POST" && s == "303") {
      if (r == "/admin/library/images/{iid}") add("pre.A7", ms)
    } else if (src == "POST") {
      w = ""
      if (r == "/admin" || r == "/admin/") w = "W1"
      else if (r == "/admin/unplaced") w = "W2"
      else if (r == "/admin/g/{gid}") w = "W3"
      else if (r == "/admin/tiles/{iid}") w = "W4"
      if (w != "" && (m == "GET" || m == "HEAD") && s == "200") rset(w, rd)
      if (w != "" && m == "GET" && s == "200") {
        if (w == "W1" && rd == "4") add("post.W1full", ms)
        if (w == "W1" && rd == "2") add("post.W1main", ms)
        if (w == "W3" && rd == "5") add("post.W3full", ms)
        if (w == "W3" && rd == "2") add("post.W3main", ms)
      }
      if (m == "POST" && s == "200") {
        if (r == "/admin/api/photos/{iid}") add("post.A1", ms)
        else if (r == "/admin/api/galleries/{gid}") add("post.A5", ms)
        else if (r == "/admin/api/galleries/{gid}/photos") add("post.A7", ms)
        else if (r == "/admin/api/galleries/{gid}/photos/remove") add("post.A8", ms)
        else if (r == "/admin/api/galleries/{gid}/order") add("post.A9", ms)
        else if (r == "/admin/api/galleries/{gid}/cover") add("post.A10", ms)
      }
    }
    next
  }
  $1 == "POST" && $2 == "upload.joined" { joined++; jitem[$4] = 1; next }
  $1 == "POST" && $2 == "op.timing" && $3 == "upload.join" && $5 == "ok" {
    jn++; if ($4 + 0 > jmax + 0) jmax = $4 + 0; if ($4 + 0 > 2000) jslow++; next
  }
  $1 == "POST" && $2 == "evidence.forced-failure" { forced = $3; next }
  END {
    fails = 0
    cmp("SC-005", "W1-full(reads 4)", "pre.lib", "post.W1full", minn, minn, 0)
    cmp("SC-005", "W3-full(reads 5)", "pre.lib", "post.W3full", minn, minn, 0)
    cmp("SC-005", "W1-main(reads 2)", "pre.lib", "post.W1main", minn, minn, 0)
    cmp("SC-005", "W3-main(reads 2)", "pre.lib", "post.W3main", minn, minn, 0)
    cmp("SC-005", "W3-full-vs-builder", "pre.builder", "post.W3full", minn, minn, 1)
    reads("W1", "4 2 1", 1)
    reads("W2", "4 2 1", 0)
    reads("W3", "5 2", 1)
    reads("W4", "1", 1)
    cmp("SC-006", "A1", "pre.A1", "post.A1", 1, minw, 0)
    cmp("SC-006", "A5", "pre.A5", "post.A5", 1, minw, 0)
    cmp("SC-006", "A7", "pre.A7", "post.A7", 1, minw, 0)
    cmp("SC-006", "A8", "pre.A8", "post.A8", 1, minw, 0)
    cmp("SC-006", "A9", "pre.A9", "post.A9", 1, minw, 0)
    cmp("SC-006", "A10", "pre.A10", "post.A10", 1, minw, 0)
    if (joined + 0 == joins) printf "PASS SC-013 joined %d upload.joined lines\n", joined
    else { printf "FAIL SC-013 joined %d upload.joined lines, expected %d\n", joined, joins; fails++ }
    if (forced == "") { printf "FAIL SC-013 forced no evidence.forced-failure line\n"; fails++ }
    else if (forced in jitem) { printf "FAIL SC-013 forced item %s has an upload.joined line\n", forced; fails++ }
    else printf "PASS SC-013 forced item %s has no upload.joined line\n", forced
    if (jn + 0 < joins) { printf "FAIL SC-013 join-ms %d op.timing upload.join ok lines, need >= %d\n", jn, joins; fails++ }
    else if (jslow + 0 > 0) { printf "FAIL SC-013 join-ms %d of %d upload.join ok > 2000 ms (max %.2f)\n", jslow, jn, jmax; fails++ }
    else printf "PASS SC-013 join-ms every upload.join ok (n=%d) <= 2000 ms (max %.2f)\n", jn, jmax
    exit fails
  }'
fails=$?
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
