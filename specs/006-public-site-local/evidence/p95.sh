#!/usr/bin/env bash
# SC-003 / SC-004 (FR-031) from the request log alone. Run from the repo root after the seed:
#   bash specs/006-public-site-local/evidence/p95.sh | tee specs/006-public-site-local/evidence/p95-output.txt
# Starts its own :5090 instance with the output cache OFF (so every request reads the datastore),
# warms each route with 10 requests, sends 100 per route, then reads http.request events from
# evidence/perf-run.log: State.route, State.duration_ms, State.db_reads. No client-side timing.
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/006-public-site-local/evidence"
LOG="$EV/perf-run.log"
BASE="http://localhost:5090"
APP_PID=""
stop_app() { [ -n "$APP_PID" ] && { pkill -P "$APP_PID" 2>/dev/null; kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null; }; APP_PID=""; }
trap stop_app EXIT

bash scripts/dev-mongo.sh >/dev/null
read -r gid gslug <<< "$(docker exec gallery-mongo mongosh gallery_dev --quiet --eval \
  'const g=db.galleries.find({visibility:"public","items.0":{$exists:true}}).sort({_id:1}).limit(1).next(); print(g._id+" "+g.slug)')"
img=$(docker exec gallery-mongo mongosh gallery_dev --quiet --eval "print(db.galleries.findOne({_id:'$gid'}).items[1])")
GALLERY="/g/$gid/$gslug"
PHOTO="/g/$gid/$gslug/$img"

Site__OutputCache=false ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-build \
  --no-launch-profile --artifacts-path artifacts --urls "$BASE" > "$LOG" 2>&1 &
APP_PID=$!
for _ in $(seq 1 60); do curl -s -o /dev/null "$BASE/" && break; sleep 0.5; done

for p in / "$GALLERY" "$PHOTO"; do
  for _ in $(seq 1 10); do curl -s -o /dev/null "$BASE$p"; done
done
start_line=$(wc -l < "$LOG")
for p in / "$GALLERY" "$PHOTO"; do
  for _ in $(seq 1 100); do curl -s -o /dev/null "$BASE$p"; done
done
sleep 1
stop_app

echo "routes: / | $GALLERY | $PHOTO (output cache off, 10 warm-up + 100 measured each)"
tail -n +"$((start_line + 1))" "$LOG" | grep '"Message":"http.request ' | jq -r '[.State.route, .State.duration_ms, .State.db_reads] | @tsv' \
| awk -F'\t' '
  { n[$1]++; d[$1, n[$1]] = $2; r[$1, $3]++; if (!($1 in maxr) || $3 > maxr[$1]) maxr[$1] = $3 }
  END {
    fails = 0
    for (route in n) {
      c = n[route]
      for (i = 1; i <= c; i++) a[i] = d[route, i]
      for (i = 2; i <= c; i++) { v = a[i]; j = i - 1; while (j > 0 && a[j] > v) { a[j+1] = a[j]; j-- } a[j+1] = v }
      idx = int(0.95 * c + 0.999999); p95 = a[idx]
      dist = ""; for (k = 0; k <= 5; k++) if ((route, k) in r) dist = dist " db_reads=" k ":" r[route, k]
      printf "%s  n=%d  p50=%.2f ms  p95=%.2f ms %s\n", route, c, a[int(0.5 * c + 0.999999)], p95, dist
      ok3 = (c == 100 && p95 < 50)
      printf "%s SC-003 %s p95 %.2f ms < 50 (n=%d)\n", ok3 ? "PASS" : "FAIL", route, p95, c; if (!ok3) fails++
      limit = (route == "/") ? 1 : 2
      if (route == "/") ok4 = (r[route, 1] == c); else ok4 = (maxr[route] <= 2)
      printf "%s SC-004 %s db_reads %s %d on 100%%\n", ok4 ? "PASS" : "FAIL", route, (route == "/") ? "=" : "<=", limit; if (!ok4) fails++
    }
    print ""; print (fails == 0 ? "ALL PASS" : fails " FAILED")
    exit fails
  }'
