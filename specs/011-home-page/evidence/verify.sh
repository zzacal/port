#!/usr/bin/env bash
# 011 T024: read-only live check of the built app. Starts its own instance from the build output on
# 127.0.0.1:5092 (plain http, so no certificate; never 5080/5081, the owner's watch), runs GETs only
# against the owner's dev Mongo (appsettings.Development.json), and stops only the pid it started.
# Run from the repo root:
#   bash specs/011-home-page/evidence/verify.sh [<repo-root-to-test>] | tee specs/011-home-page/evidence/verify-output.txt
# The optional argument is the checkout whose artifacts/ build and src/Gallery.Web content root are
# used (the pre-011 worktree for the non-vacuity run). Defaults to the current directory.
set -uo pipefail
ROOT=$(cd "${1:-.}" && pwd)
DLL="$ROOT/artifacts/bin/Gallery.Web/debug/Gallery.Web.dll"
B=http://127.0.0.1:5092
T=$(mktemp -d /tmp/011-verify.XXXXXX)
[ -f "$DLL" ] || { echo "FAIL no build at $DLL"; exit 1; }

ASPNETCORE_ENVIRONMENT=Development dotnet "$DLL" --contentRoot "$ROOT/src/Gallery.Web" --urls "$B" > "$T/app.log" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null' EXIT
up=0; for i in $(seq 1 40); do curl -s -o /dev/null "$B/" && { up=1; break; }; sleep 0.5; done
[ "$up" = 1 ] || { echo "FAIL app did not start on $B"; tail -20 "$T/app.log"; exit 1; }
echo "testing $ROOT (pid $APP_PID)"

fails=0
ok()  { echo "PASS $*"; }
bad() { echo "FAIL $*"; fails=$((fails+1)); }
# get NAME PATH: body to $T/NAME.html, headers to $T/NAME.h, prints the status
get() { curl -s -o "$T/$1.html" -D "$T/$1.h" "$B$2"; head -1 "$T/$1.h" | awk '{print $2}'; }
has()  { [ -f "$T/$1.html" ] && grep -q -- "$2" "$T/$1.html"; }
# hasnt: the file must exist (a missing body is not "absent"), then the element must not be in it
hasnt() { [ -f "$T/$1.html" ] && [ -s "$T/$1.html" ] && ! grep -q -- "$2" "$T/$1.html"; }
hdr()  { [ -f "$T/$1.h" ] && grep -qi "^$2" "$T/$1.h"; }
# root NAME: label of the first breadcrumb item (scoped b-xxxx attributes stripped)
root() { [ -f "$T/$1.html" ] && perl -0ne 's/ b-[a-z0-9]{10}//g; print $1 if /<nav class="breadcrumb"[^>]*>.*?<li[^>]*>(?:<[^>]+>)*([^<]+)</s' "$T/$1.html"; }

s=$(get home /)
[ "$s" = 200 ] && ok "GET / 200" || bad "GET / $s"
has home 'class="nav nav--home"' && ok "/ has nav.nav--home" || bad "/ has no nav.nav--home"
has home '<a href="/g"[^>]*>Galleries</a>\|<a href="/g">Galleries</a>' && ok "/ links Galleries to /g" || bad "/ has no Galleries link to /g"
hasnt home '<div class="cs ' && ok "/ has no ContactSheet element" || bad "/ renders a ContactSheet"
hdr home 'Server-Timing' && ok "/ has Server-Timing" || bad "/ has no Server-Timing"

s=$(get listing /g)
[ "$s" = 200 ] && ok "GET /g 200" || bad "GET /g $s"
has listing 'class="tile"' && ok "/g lists tiles ($(grep -o 'class="tile"' "$T/listing.html" | wc -l | tr -d ' '))" || bad "/g has no tiles"
hdr listing 'Server-Timing' && [ "$s" = 200 ] && ok "/g has Server-Timing" || bad "/g has no Server-Timing on a 200"

s=$(get slash /g/)
loc=$(grep -i '^Location:' "$T/slash.h" 2>/dev/null | tr -d '\r' | awk '{print $2}')
[ "$s" = 301 ] && [ "$loc" = /g ] && ok "GET /g/ 301 -> /g" || bad "GET /g/ $s -> ${loc:-none}"

g=$(grep -oE 'href="/g/[a-z2-7]{26}/[^"/#]+"' "$T/listing.html" 2>/dev/null | head -1 | sed -E 's/href="([^"]*)"/\1/')
if [ -n "$g" ]; then
  get gallery "$g" > /dev/null
  p=$(grep -oE "href=\"$g/[a-z2-7]{26}\"" "$T/gallery.html" | head -1 | sed -E 's/href="([^"]*)"/\1/')
  [ -n "$p" ] && get photo "$p" > /dev/null
fi
get missing /g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none > /dev/null
for pg in home listing gallery photo missing; do
  r=$(root "$pg" 2>/dev/null)
  [ "$r" = Home ] && ok "breadcrumb root on $pg is Home" || bad "breadcrumb root on $pg is '${r:-none}'"
done

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
rm -rf "$T"
exit "$fails"
