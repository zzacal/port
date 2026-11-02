#!/usr/bin/env bash
# 010 T044 live walkthrough: read-only GETs against the built app on :5094 (owner's dev Mongo
# gallery_dev, real dev image base URL). No writes, no sign-in. Run from the repo root while the
# app is up:  bash specs/010-navigation-improvements/evidence/live-walkthrough.sh
set -uo pipefail
B=http://127.0.0.1:5094
for i in $(seq 1 30); do curl -s -o /dev/null "$B/" && break; sleep 1; done
fails=0
ok()  { echo "PASS $*"; }
bad() { echo "FAIL $*"; fails=$((fails+1)); }

home=$(curl -s -D /tmp/010-h.txt "$B/")
st=$(head -1 /tmp/010-h.txt | awk '{print $2}')
[ "$st" = 200 ] && ok "GET / 200" || bad "GET / $st"
hdr() { perl -0ne 'print $1 if /<header class="site-header"[^>]*>(.*?)<\/header>/s' ; }
hrefs=$(echo "$home" | hdr | grep -oE 'href="[^"]*"' | sort | uniq -c)
echo "   header hrefs on /: $(echo $hrefs)"
[ "$(echo "$home" | hdr | grep -oE 'href="[^"]*"' | sort -u)" = 'href="/"' ] && ok "header on / has exactly one link, to /" || bad "header on /"

g=$(echo "$home" | grep -oE 'href="/g/[a-z0-9]{26}/[^"/]+"' | head -1 | sed -E 's/href="([^"]*)"/\1/')
[ -n "$g" ] && ok "home lists a gallery: $g" || { bad "no gallery on /"; echo "$fails FAILED"; exit "$fails"; }
gp=$(curl -s -D /tmp/010-g.txt "$B$g")
st=$(head -1 /tmp/010-g.txt | awk '{print $2}')
[ "$st" = 200 ] && ok "GET gallery 200" || bad "GET gallery $st"
[ "$(echo "$gp" | hdr | grep -oE 'href="[^"]*"' | sort -u)" = 'href="/"' ] && ok "header on gallery has exactly one link" || bad "header on gallery"
if echo "$gp" | grep -q 'class="gallery-neighbours"'; then
  ok "gallery shows neighbour links: $(echo "$gp" | perl -0ne 'print $1 if /(<nav class="gallery-neighbours".*?<\/nav>)/s' | grep -oE '>[^<]+</a>' | tr '\n' ' ')"
else
  echo "NOTE gallery shows no neighbour links (expected until the first owner write after 010 fills nav; quickstart Part C step 2, research R-11)"
fi

p=$(echo "$gp" | grep -oE "href=\"$g/[a-z0-9]{26}\"" | head -1 | sed -E 's/href="([^"]*)"/\1/')
pp=$(curl -s -D /tmp/010-p.txt "$B$p")
st=$(head -1 /tmp/010-p.txt | awk '{print $2}')
[ "$st" = 200 ] && ok "GET photo $p 200" || bad "GET photo $st"
row=$(echo "$pp" | perl -0ne 'print $1 if /(<p class="gallery-link".*?<\/p>)/s')
iid=${p##*/}
echo "$row" | grep -q "href=\"$g#p-$iid\"" && ok "photo row links back to $g#p-$iid" || bad "photo gallery link: $row"
cnt=$(echo "$row" | perl -pe 's/<[^>]*>//g; s/&#xB7;|&middot;/·/g')
echo "$cnt" | grep -qE '^← .+ · 1 / [0-9]+$' && ok "photo row reads: $cnt" || bad "photo row text: $cnt"
[ "$(echo "$pp" | hdr | grep -oE 'href="[^"]*"' | sort -u)" = 'href="/"' ] && ok "header on photo has exactly one link" || bad "header on photo"
grep -qi 'server-timing' /tmp/010-p.txt && ok "Server-Timing present on photo" || bad "no Server-Timing"
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
