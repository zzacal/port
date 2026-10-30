#!/usr/bin/env bash
# Live verification for 003-component-ui-pages. Run from the repo root.
#   bash specs/003-component-ui-pages/evidence/verify.sh > specs/003-component-ui-pages/evidence/verify-output.txt
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/003-component-ui-pages/evidence"
BASE="http://localhost:5080"
fails=0
pass() { echo "PASS $1 $2"; }
fail() { echo "FAIL $1 $2"; fails=$((fails+1)); }

stop_app() { pkill -f "Gallery.Web.dll" 2>/dev/null; pkill -f "src/Gallery.Web" 2>/dev/null; sleep 1; }
wait_up() { for _ in $(seq 1 40); do curl -s -o /dev/null "$1" && return 0; sleep 0.5; done; return 1; }

stop_app

# 1. build + tests
out=$(dotnet build Gallery.slnx -warnaserror 2>&1)
echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
  && pass gate "build: 0 warnings, 0 errors" || fail gate "build"
out=$(dotnet test --no-build 2>&1)
[ "$(echo "$out" | grep -c '^Passed!')" = 2 ] && ! echo "$out" | grep -q '^Failed!' \
  && pass gate "tests: $(echo "$out" | grep -oE 'Passed: +[0-9]+' | awk '{s+=$2} END{print s}') passed in 2 assemblies" \
  || fail gate "tests: $(echo "$out" | grep -E 'Passed!|Failed!' | tr '\n' ' ')"

# 2. Development run
dotnet run --project src/Gallery.Web --no-build > "$EV/dev-run.log" 2>&1 &
wait_up "$BASE/" || { fail start "dev app did not start"; exit 1; }

index=$(curl -s "$BASE/ui/")
slugs=$(echo "$index" | grep -oE 'href="/ui/[^"]+"' | sed -E 's#href="/ui/(.*)"#\1#')
n=$(echo "$slugs" | grep -c .)
[ "$n" = 14 ] && pass SC-001 "index lists 14 pages" || fail SC-001 "index lists $n pages"

bad=""
for s in "" $slugs; do
  hdr=$(curl -s -D - -o /tmp/ui-body.html "$BASE/ui/$s")
  code=$(echo "$hdr" | head -1 | awk '{print $2}')
  [ "$code" = 200 ] || bad="$bad /ui/$s($code)"
  echo "$hdr" | grep -qi '^x-robots-tag: noindex' || bad="$bad /ui/$s(no-robots-header)"
  echo "$hdr" | grep -qiE '^server-timing: app;dur=[0-9.]+' || bad="$bad /ui/$s(no-server-timing)"
  grep -qi '<script' /tmp/ui-body.html && bad="$bad /ui/$s(script)"
  grep -qiE '<meta name="robots" content="noindex"' /tmp/ui-body.html || bad="$bad /ui/$s(no-robots-meta)"
  grep -qiE '<link[^>]+rel="stylesheet"' /tmp/ui-body.html && bad="$bad /ui/$s(external-css)"
done
[ -z "$bad" ] && pass FR-009/011 "15 pages: 200, noindex header+meta, Server-Timing, no script, no external CSS" || fail FR-009/011 "$bad"

code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/ui/nope")
[ "$code" = 404 ] && pass contract "unknown /ui path -> 404" || fail contract "unknown /ui path -> $code"

f=$(echo "$index" | grep -oE '/samples/[0-9a-f]{16}\.avif' | head -1)
[ -z "$f" ] && f=$(curl -s "$BASE/ui/atoms/picture" | grep -oE '/samples/[0-9a-f]{16}\.avif' | head -1)
cc=$(curl -s -D - -o /dev/null "$BASE$f" | grep -i '^cache-control' | tr -d '\r')
echo "$cc" | grep -q 'max-age=31536000, immutable' && pass IV.21 "sample $f: $cc" || fail IV.21 "sample $f: $cc"

# 3. SC-007: exactly one http.request per request
before=$(grep -c '"http.request ' "$EV/dev-run.log")
for _ in $(seq 1 20); do curl -s -o /dev/null "$BASE/ui/tokens"; done
sleep 1
after=$(grep -c '"http.request ' "$EV/dev-run.log")
[ $((after - before)) = 20 ] && pass SC-007 "20 requests -> 20 http.request events" || fail SC-007 "20 requests -> $((after - before)) events"
grep -q '"op":"startup.sample-catalog"' "$EV/dev-run.log" && grep -q '"op":"startup.inline-styles"' "$EV/dev-run.log" \
  && pass IV.2 "startup op.timing events logged" || fail IV.2 "startup op.timing events missing"

stop_app

# 4. SC-006: /ui does not exist outside Development
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Gallery.Web --no-build --no-launch-profile --urls http://localhost:5081 > "$EV/prod-run.log" 2>&1 &
wait_up "http://localhost:5081/" || { fail start "prod app did not start"; exit 1; }
codes=""
for s in "" tokens atoms/picture organisms/contact-sheet; do codes="$codes $(curl -s -o /dev/null -w '%{http_code}' "http://localhost:5081/ui/$s")"; done
[ "$(echo $codes | tr ' ' '\n' | sort -u)" = 404 ] && pass SC-006 "Production /ui ->$codes" || fail SC-006 "Production /ui ->$codes"
stop_app

# 5. FR-008: no EXIF/XMP structures in any sample derivative
if bash scripts/check-metadata.sh src/Gallery.Web/wwwroot/samples/*.avif src/Gallery.Web/wwwroot/samples/*.webp; then
  pass FR-008 "$(ls src/Gallery.Web/wwwroot/samples/*.avif src/Gallery.Web/wwwroot/samples/*.webp | wc -l | tr -d ' ') derivatives, no EXIF/XMP"
else
  fail FR-008 "metadata found"
fi

echo "---"
echo "failures: $fails"
exit $fails
