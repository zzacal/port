#!/usr/bin/env bash
# 012 T028: local walkthrough of verify-prod.sh against the guarded Release publish from
# probe-publish.sh (/tmp/012-publish), serving the dev datastore read-only (GETs only) on :5193.
# The origin secret is generated here, passed to the app and to verify-prod.sh, never printed.
cd /Users/zzacal/repos/gallery || exit 1
E=specs/012-production-infrastructure/evidence
[ -f /tmp/012-publish/Gallery.Web.dll ] || { echo "run probe-publish.sh first"; exit 1; }
SECRET=$(openssl rand -hex 32)
B=http://127.0.0.1:5193
( cd /tmp/012-publish && Hosting__Edge=cloudflare Hosting__OriginSecret="$SECRET" \
  ASPNETCORE_ENVIRONMENT=Production Mongo__ConnectionString=mongodb://localhost:27017 \
  Mongo__Database=gallery_dev Storage__ImageBaseUrl=https://img-dev.zacal.me \
  exec dotnet Gallery.Web.dll --urls $B > /tmp/012-walk.log 2>&1 ) &
pid=$!
for i in $(seq 1 60); do curl -s -o /dev/null -H "X-Gallery-Origin: $SECRET" $B/ && break; sleep 0.25; done
ORIGIN_SECRET="$SECRET" IMG_BASE=https://img-dev.zacal.me/ bash $E/verify-prod.sh $B $B
rc=$?
kill $pid; wait $pid 2>/dev/null
echo "== app log lines containing the secret: $(grep -cF "$SECRET" /tmp/012-walk.log)"
echo "== origin.refused lines: $(grep -c 'origin.refused' /tmp/012-walk.log)"
exit $rc
