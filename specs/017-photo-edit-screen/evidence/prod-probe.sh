#!/usr/bin/env bash
# 017 T024 production probes (read-only, signed out, no data change). Run after the deploy:
#   bash specs/017-photo-edit-screen/evidence/prod-probe.sh | tee specs/017-photo-edit-screen/evidence/browser-prod.txt
# The id is well-formed but arbitrary: the owner gate answers before the handler reads anything.
set -u
H=https://port.zacal.me
ID=00000000000000000000000000
echo "== $(date -u +%FT%TZ) signed out, main $(git rev-parse --short origin/main)"
for p in "/admin/library/images/$ID?page=1" "/admin/library/images/$ID" /admin/library; do
  printf '%s ' "$p"; curl -s -o /dev/null -w '%{http_code} -> %{redirect_url}\n' "$H$p"
done
printf 'HEAD /admin/library/images/%s ' "$ID"; curl -s -I -o /dev/null -w '%{http_code}\n' "$H/admin/library/images/$ID"
