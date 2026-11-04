#!/usr/bin/env bash
# Probe: does a Release publish of Gallery.Web start in Production (no dev appsettings, no secrets)
# and serve the data-free home page? Which content files does it need? Read-only; own port 5193.
cd /Users/zzacal/repos/gallery || exit 1
OUT=/tmp/012-publish
rm -rf "$OUT"
dotnet publish src/Gallery.Web -c Release -o "$OUT" --artifacts-path /tmp/012-pubart > /tmp/012-publish.log 2>&1 || { tail -5 /tmp/012-publish.log; exit 1; }
echo "== publish top level:"; ls "$OUT" | grep -v '\.dll$' | head -30
echo "== runtimeconfig files:"; ls "$OUT"/*.runtimeconfig.json
echo "== dll count: $(ls "$OUT"/*.dll | wc -l)"
cd "$OUT" || exit 1
ASPNETCORE_ENVIRONMENT=Production dotnet Gallery.Web.dll --urls http://127.0.0.1:5193 > /tmp/012-run.log 2>&1 &
pid=$!
for i in $(seq 1 60); do curl -s -o /dev/null http://127.0.0.1:5193/ && break; sleep 0.25; done
for p in / /g /admin /admin/signin; do
  echo "GET $p -> $(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' http://127.0.0.1:5193$p)"
done
echo "== home has Galleries link: $(curl -s http://127.0.0.1:5193/ | grep -c 'href="/g"')"
kill $pid; wait $pid 2>/dev/null
echo "== startup log lines (first 5, messages only):"
grep -o '"Message":"[^"]*"' /tmp/012-run.log | head -5

# ── Run 2 (012 T013): the same publish behind the origin guard ─────────────────────────────
# Hosting:Edge=cloudflare plus a generated 40-char secret. The secret is never printed.
echo
echo "== run 2: Hosting__Edge=cloudflare, 40-char Hosting__OriginSecret"
SECRET=$(openssl rand -hex 20)
B=http://127.0.0.1:5193
Hosting__Edge=cloudflare Hosting__OriginSecret="$SECRET" ASPNETCORE_ENVIRONMENT=Production \
  dotnet Gallery.Web.dll --urls $B > /tmp/012-run2.log 2>&1 &
pid=$!
for i in $(seq 1 60); do curl -s -o /dev/null -H "X-Gallery-Origin: $SECRET" $B/ && break; sleep 0.25; done
fails=0
check() { if [ "$2" = "$3" ]; then echo "PASS $1 ($3)"; else echo "FAIL $1: expected $2, got $3"; fails=$((fails+1)); fi; }
check "no header GET / -> 403" 403 "$(curl -s -o /dev/null -w '%{http_code}' $B/)"
check "wrong header GET / -> 403" 403 "$(curl -s -o /dev/null -w '%{http_code}' -H 'X-Gallery-Origin: wrong' $B/)"
check "header + X-Forwarded-Proto https GET /admin/signin -> 200" 200 \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "X-Gallery-Origin: $SECRET" -H 'X-Forwarded-Proto: https' $B/admin/signin)"
check "header + CF-Connecting-IP GET /g -> 200" 200 \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "X-Gallery-Origin: $SECRET" -H 'CF-Connecting-IP: 203.0.113.9' $B/g)"
kill $pid; wait $pid 2>/dev/null
check "http.request line for GET /g exists" 1 "$(grep -c 'http.request GET /g 200' /tmp/012-run2.log | awk '{print ($1>0)?1:0}')"
check "origin.refused lines (missing + mismatch)" 2 "$(grep -c 'origin.refused GET / 403' /tmp/012-run2.log)"
check "origin.refused reasons" "missing mismatch" "$(grep -o 'origin.refused GET / 403 [0-9.]* [a-z]*' /tmp/012-run2.log | awk '{print $NF}' | tr '\n' ' ' | sed 's/ $//')"
check "log lines containing the secret" 0 "$(grep -cF "$SECRET" /tmp/012-run2.log)"
echo "RESULT run 2: $fails FAIL"
