#!/usr/bin/env bash
# Live verification for 005-admin-ui-components. Run from the repo root:
#   bash specs/005-admin-ui-components/evidence/verify.sh | tee specs/005-admin-ui-components/evidence/verify-output.txt
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/005-admin-ui-components/evidence"
# The owner keeps `dotnet watch run` on :5080. This script never touches it: it builds into
# ./artifacts (separate bin/obj), runs its own instances on :5082/:5083, and stops only the PIDs
# it started. dotnet watch also injects a browser-refresh <script> into HTML served to browsers,
# so :5080 is not a clean target for the no-script check.
ART="artifacts"
BASE="http://localhost:5082"
fails=0
pass() { echo "PASS $1 $2"; }
fail() { echo "FAIL $1 $2"; fails=$((fails+1)); }
APP_PID=""
stop_app() { [ -n "$APP_PID" ] && { pkill -P "$APP_PID" 2>/dev/null; kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null; }; APP_PID=""; sleep 1; }
wait_up() { for _ in $(seq 1 40); do curl -s -o /dev/null "$1" && return 0; sleep 0.5; done; return 1; }

# 1. gates
out=$(dotnet build Gallery.slnx -warnaserror --artifacts-path "$ART" 2>&1)
echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
  && pass gate "build: 0 warnings, 0 errors" || fail gate "build"
out=$(dotnet test Gallery.slnx --no-build --artifacts-path "$ART" 2>&1)
[ "$(echo "$out" | grep -c '^Passed!')" = 2 ] && ! echo "$out" | grep -q '^Failed!' \
  && pass gate "tests: $(echo "$out" | grep -oE 'Passed: +[0-9]+' | awk '{s+=$2} END{print s}') passed in 2 assemblies" \
  || fail gate "tests: $(echo "$out" | grep -E 'Passed!|Failed!' | tr '\n' ' ')"

# 2. Development run
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-build --no-launch-profile --artifacts-path "$ART" --urls "$BASE" > "$EV/dev-run.log" 2>&1 &
APP_PID=$!
wait_up "$BASE/" || { fail start "dev app did not start"; exit 1; }

index=$(curl -s "$BASE/ui/")
slugs=$(echo "$index" | grep -oE 'href="/ui/[^"]+"' | sed -E 's#href="/ui/(.*)"#\1#')
n=$(echo "$slugs" | grep -c .)
na=$(echo "$slugs" | grep -c '^admin/')
[ "$n" = 32 ] && [ "$na" = 18 ] && pass SC-001 "index lists 32 pages, 18 admin" || fail SC-001 "index lists $n pages, $na admin"
for h in "Admin atoms" "Admin molecules" "Admin organisms" "Admin templates"; do
  echo "$index" | grep -q ">$h</h2>" || fail FR-007 "missing heading $h"
done

bad=""
for s in "" $slugs; do
  hdr=$(curl -s -D - -o /tmp/ui-body.html "$BASE/ui/$s")
  code=$(echo "$hdr" | head -1 | awk '{print $2}')
  [ "$code" = 200 ] || bad="$bad /ui/$s($code)"
  echo "$hdr" | grep -qi '^x-robots-tag: noindex' || bad="$bad /ui/$s(no-robots-header)"
  echo "$hdr" | grep -qiE '^server-timing: app;dur=[0-9.]+' || bad="$bad /ui/$s(no-server-timing)"
  grep -qi '<script' /tmp/ui-body.html && bad="$bad /ui/$s(script)"
  grep -qiE '<[^>]*[[:space:]]style[[:space:]]*=' /tmp/ui-body.html && bad="$bad /ui/$s(inline-style)"
  grep -qiE '<meta name="robots" content="noindex"' /tmp/ui-body.html || bad="$bad /ui/$s(no-robots-meta)"
  grep -qiE '<link[^>]+rel="stylesheet"' /tmp/ui-body.html && bad="$bad /ui/$s(external-css)"
done
[ -z "$bad" ] && pass FR-009/SC-003 "33 pages: 200, noindex header+meta, Server-Timing, no script, no inline style, no external CSS" || fail FR-009/SC-003 "$bad"

code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/ui/admin/nope")
[ "$code" = 404 ] && pass contract "unknown /ui/admin path -> 404" || fail contract "unknown /ui/admin path -> $code"
code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/admin/library")
[ "$code" = 404 ] && pass scope "no /admin route mapped -> 404" || fail scope "/admin/library -> $code"

# 3. SC-008 one request event per request
before=$(grep -c '"http.request ' "$EV/dev-run.log")
for _ in $(seq 1 20); do curl -s -o /dev/null "$BASE/ui/admin/organisms/gallery-builder"; done
sleep 1
after=$(grep -c '"http.request ' "$EV/dev-run.log")
[ $((after - before)) = 20 ] && pass SC-008 "20 requests -> 20 http.request events" || fail SC-008 "20 requests -> $((after - before)) events"
stop_app

# 4. Production: /ui (including admin pages) not mapped
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Gallery.Web --no-build --no-launch-profile --artifacts-path "$ART" --urls http://localhost:5083 > "$EV/prod-run.log" 2>&1 &
APP_PID=$!
wait_up "http://localhost:5083/" || { fail start "prod app did not start"; exit 1; }
codes=""
for s in "" admin/atoms/button admin/organisms/gallery-builder admin/templates/admin-layout; do codes="$codes $(curl -s -o /dev/null -w '%{http_code}' "http://localhost:5083/ui/$s")"; done
[ "$(echo $codes | tr ' ' '\n' | sort -u)" = 404 ] && pass FR-009 "Production /ui ->$codes" || fail FR-009 "Production /ui ->$codes"
stop_app

echo "---"
[ "$fails" = 0 ] && echo "ALL PASS" || echo "FAILED $fails"
