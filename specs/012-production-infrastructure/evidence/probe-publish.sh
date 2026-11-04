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
