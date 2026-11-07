#!/usr/bin/env bash
# 016 T028 production probes (read-only, no sign-in, no data change).
set -u
H=https://port.zacal.me
echo "== $(date -u +%FT%TZ) deploy run 42412530059 (build, test, deploy: success) at main 031a350"
for p in /admin/galleries /ui/molecules/gallery-actions; do
  printf '%s ' "$p"; curl -s -o /dev/null -w '%{http_code} -> %{redirect_url}\n' "$H$p"
done
printf '/g '; curl -s -o /tmp/016-prod-g.html -w '%{http_code}\n' "$H/g"
echo "/g contains admin markers (gallery-actions, ga-vis, gal-band, eye-off): $(grep -cE 'gallery-actions|ga-vis|gal-band|data-icon="eye' /tmp/016-prod-g.html)"
echo "/g grid class: $(grep -oE 'class="cs cs-l[0-9] cs-m[0-9] cs-s[0-9]"' /tmp/016-prod-g.html | sort -u)"
