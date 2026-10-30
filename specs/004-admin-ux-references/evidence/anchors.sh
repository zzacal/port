#!/usr/bin/env bash
# Phase 0 anchor check: is each candidate source reachable without sign-in?
# 2xx/3xx = OK, 402/403/429/489 = WARN (bot wall, check in browser), 404/410/5xx/timeout = DEAD.
# 489 is Piwigo's non-standard anti-bot reply to curl; the same URLs load in a real browser.
UA='Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130 Safari/537.36'
while read -r url; do
  [ -z "$url" ] && continue
  code=$(curl -sL -o /dev/null -w '%{http_code}' -A "$UA" --max-time 20 "$url")
  case "$code" in 2*|3*) v=OK;; 402|403|429|489) v=WARN;; *) v=DEAD;; esac
  printf '%s %s %s\n' "$v" "$code" "$url"
done <<'EOF'
https://atlassian.design/components/button/examples
https://demo.immich.app/albums
https://demo.immich.app/albums/cccdd9f7-6a0f-4ed1-9dc0-ad8774ff9798
https://demo.immich.app/auth/login
https://demo.immich.app/photos
https://demo.lycheeorg.dev/gallery
https://demo.lycheeorg.dev/gallery/W5iBkjcKWHqB3Hd8YJL-d-2t
https://demo.photoprism.app/
https://demo.photoprism.app/library/albums
https://demo.photoprism.app/library/albums/artiskh2amkzizr6/view
https://demo.photoprism.app/library/browse
https://demo.photoprism.app/library/import
https://demo1.piwigo.com/
https://demo1.piwigo.com/admin.php
https://demo1.piwigo.com/identification.php
https://design-system.service.gov.uk/components/button/
https://design-system.service.gov.uk/components/button/#disabled-buttons
https://design-system.service.gov.uk/components/button/#warning-buttons
https://design-system.service.gov.uk/components/file-upload/
https://design-system.service.gov.uk/components/file-upload/#error-messages
https://design-system.service.gov.uk/components/warning-text/
https://ghost.org/help/design-settings/
https://ghost.org/help/publishing-options/
https://helpx.adobe.com/portfolio/
https://identity.flickr.com/login
https://playground.wordpress.net/
https://polaris.shopify.com/components
https://pqina.nl/filepond/
https://primer.style/product/components/button/#loading
https://primer.style/product/components/button/#style-variants
https://primer.style/product/components/checkbox/#indeterminate
https://primer.style/product/components/form-control/#with-validation-message-and-caption-text
https://sortablejs.github.io/Sortable/
https://sortablejs.github.io/Sortable/#grid
https://sortablejs.github.io/Sortable/#handle
https://support.google.com/photos/answer/6128849
https://support.squarespace.com/hc/en-us/articles/360035636332-Gallery-sections
https://uppy.io/examples/
https://vercel.com/geist/button
https://vercel.com/geist/destructive-confirmation-input
https://vercel.com/geist/input
https://vercel.com/geist/progress
https://vercel.com/geist/toggle
https://www.smugmughelp.com/
EOF
