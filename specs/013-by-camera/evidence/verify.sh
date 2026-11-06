#!/usr/bin/env bash
# 013 T032: read-only live check of the built app (quickstart.md §2 verify.sh).
# 1. Copies the owner's dev database gallery_dev into a scratch database gallery_013verify
#    (mongodump | mongorestore inside the gallery-mongo container; gallery_dev is only read) and
#    runs the app's own SiteProjector once on the copy (/tmp/013-project, a throwaway console app
#    referencing Gallery.Web), because cameras are written only by an owner write.
# 2. Starts its own instance from <root>/artifacts on 127.0.0.1:5093 (never 5080/5081, the owner's
#    watch), Development config with Mongo:Database overridden to the scratch copy, GETs only, and
#    stops only the pid it started.
# Usage, from the repo root:
#   bash specs/013-by-camera/evidence/verify.sh [<repo-root-to-test>] | tee specs/013-by-camera/evidence/verify-output.txt
# The optional argument is the checkout whose artifacts/ build and src/Gallery.Web content root are
# used (the pre-013 worktree for the non-vacuity run). Every new-mechanism check must FAIL there.
set -uo pipefail
ROOT=$(cd "${1:-.}" && pwd)
DLL="$ROOT/artifacts/bin/Gallery.Web/debug/Gallery.Web.dll"
B=http://127.0.0.1:5093
SCRATCH=gallery_013verify
T=$(mktemp -d /tmp/013-verify.XXXXXX)
[ -f "$DLL" ] || { echo "FAIL no build at $DLL"; exit 1; }

docker exec gallery-mongo sh -c "mongodump --quiet --db gallery_dev --archive | mongorestore --quiet --drop --archive --nsFrom 'gallery_dev.*' --nsTo '$SCRATCH.*'" \
  || { echo "FAIL could not copy gallery_dev to $SCRATCH"; exit 1; }
HAS_CAMERAS=0
grep -qs CameraSlugIndex "$ROOT/src/Gallery.Web/Data/Indexes.cs" && HAS_CAMERAS=1
if [ "$HAS_CAMERAS" = 1 ]; then
  dotnet run --project /tmp/013-project -- mongodb://localhost:27017 "$SCRATCH" | sed 's/^/projector: /'
else
  echo "projector: skipped (pre-013 tree has no camera projection)"
fi

ASPNETCORE_ENVIRONMENT=Development Mongo__Database=$SCRATCH dotnet "$DLL" --contentRoot "$ROOT/src/Gallery.Web" --urls "$B" > "$T/app.log" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null; rm -rf "$T"' EXIT
up=0; for i in $(seq 1 40); do curl -s -o /dev/null "$B/" && { up=1; break; }; sleep 0.5; done
[ "$up" = 1 ] || { echo "FAIL app did not start on $B"; tail -20 "$T/app.log"; exit 1; }
echo "testing $ROOT (pid $APP_PID, database $SCRATCH)"

fails=0
ok()  { echo "PASS $*"; }
bad() { echo "FAIL $*"; fails=$((fails+1)); }
get() { curl -s -o "$T/$1.html" -D "$T/$1.h" "$B$2"; head -1 "$T/$1.h" | awk '{print $2}'; }
strip() { perl -pe 's/ b-[a-z0-9]{10}//g' "$T/$1.html"; }
has()  { [ -s "$T/$1.html" ] && strip "$1" | grep -q -- "$2"; }
hasnt() { [ -s "$T/$1.html" ] && ! strip "$1" | grep -q -- "$2"; }
crumbs() { strip "$1" | perl -0ne 'if (/<nav class="breadcrumb"[^>]*>(.*?)<\/nav>/s) { $n=$1; my @l = $n =~ /<li[^>]*>(?:<[^>]+>)*([^<]+)</g; print join(" \/ ", @l) }'; }

# / has two home links, in order
s=$(get home /)
[ "$s" = 200 ] && ok "GET / 200" || bad "GET / $s"
links=$(strip home | perl -0ne 'if (/<nav class="nav nav--home"[^>]*>(.*?)<\/nav>/s) { print join(" ", $1 =~ /<a href="([^"]*)"/g) }')
[ "$links" = "/g /c" ] && ok "/ home links in order: $links" || bad "/ home links '$links', expected '/g /c'"

# /c is 200 with K tiles (K = stored cameras)
K=$(docker exec gallery-mongo mongosh --quiet $SCRATCH --eval 'db.cameras.countDocuments({})' 2>/dev/null | tr -d '\r')
s=$(get cams /c)
n=$(strip cams 2>/dev/null | grep -o '<a class="tile" href="/c/[a-z0-9-]*"' | wc -l | tr -d ' ')
[ "$s" = 200 ] && [ "${K:-0}" -ge 1 ] && [ "$n" = "$K" ] && ok "GET /c 200 with $n tiles == $K stored cameras" || bad "GET /c $s, $n tiles, $K stored cameras"

# /c/ redirects 301 to /c
s=$(get slash /c/)
loc=$(grep -i '^Location:' "$T/slash.h" 2>/dev/null | tr -d '\r' | awk '{print $2}')
[ "$s" = 301 ] && [ "$loc" = /c ] && ok "GET /c/ 301 -> /c" || bad "GET /c/ $s -> ${loc:-none}"

# a private-only camera and an unknown slug answer byte-identical 404s. The dev data has no
# private-only photo, so the copy gets one: a private gallery holding a photo of a made-up camera
# (ids opaque-shaped), then the projector runs again; its slug must still be not found.
c=$(strip cams 2>/dev/null | grep -o '<a class="tile" href="/c/[a-z0-9-]*"' | head -1 | sed -E 's/.*href="([^"]*)"/\1/')
if [ -n "$c" ]; then
  docker exec gallery-mongo mongosh --quiet $SCRATCH --eval '
    const img = db.images.findOne({}); img._id = "zzzzzzzzzzzzzzzzzzzzzzzzpv"; img.camera = { make: "PRIVCAM", model: "P9" }; img.galleryIds = ["zzzzzzzzzzzzzzzzzzzzzzzzpg"];
    db.images.replaceOne({_id: img._id}, img, {upsert: true});
    db.galleries.replaceOne({_id: "zzzzzzzzzzzzzzzzzzzzzzzzpg"}, {_id: "zzzzzzzzzzzzzzzzzzzzzzzzpg", slug: "priv", title: "Priv", visibility: "private", items: [img._id], updatedAt: new Date()}, {upsert: true});' >/dev/null
  [ "$HAS_CAMERAS" = 1 ] \
    && dotnet run --project /tmp/013-project -- mongodb://localhost:27017 "$SCRATCH" > /dev/null
  get priv /c/privcam-p9 > /dev/null; get unk /c/no-such-camera > /dev/null
  hp=$(grep -iE '^(HTTP|cache-control|x-robots-tag)' "$T/priv.h" | tr -d '\r'); hu=$(grep -iE '^(HTTP|cache-control|x-robots-tag)' "$T/unk.h" | tr -d '\r')
  st=$(head -1 "$T/priv.h" | awk '{print $2}')
  [ "$st" = 404 ] && [ "$hp" = "$hu" ] && cmp -s "$T/priv.html" "$T/unk.html" \
    && ok "private-only camera /c/privcam-p9 and unknown slug: identical 404 (status, headers, body)" \
    || bad "private-only camera answered $st; headers/body differ from the unknown slug"

  # a camera page has no href="/g/
  s=$(get cam "$c")
  [ "$s" = 200 ] && hasnt cam 'href="/g/' && ok "GET $c 200, no href=\"/g/\"" || bad "GET $c $s or it links a gallery"
  tr=$(crumbs cam)
  case "$tr" in "Home / By Camera / "*) ok "camera page crumbs: $tr";; *) bad "camera page crumbs: '$tr'";; esac

  # camera photo page: crumbs Home / By Camera / {camera} / {photo}; prev/next stay inside /c/{slug}/
  p=$(strip cam | grep -oE "<a class=\"tile\" href=\"$c/[a-z2-7]{26}\"" | sed -n 2p | sed -E 's/.*href="([^"]*)"/\1/')
  if [ -n "$p" ]; then
    s=$(get photo "$p")
    tr=$(crumbs photo)
    n4=$(echo "$tr" | awk -F' / ' '{print NF}')
    case "$tr" in "Home / By Camera / "*) [ "$s" = 200 ] && [ "$n4" = 4 ] && ok "camera photo crumbs: $tr" || bad "camera photo $s crumbs '$tr'";; *) bad "camera photo crumbs: '$tr'";; esac
    pn=$(strip photo | grep -oE '<a [^>]*rel="(prev|next)"[^>]*>|<a href="[^"]*" rel="(prev|next)"' | grep -oE 'href="[^"]*"' | sed -E 's/href="([^"]*)"/\1/')
    outside=$(echo "$pn" | grep -v "^$c/" | grep -c . || true)
    [ -n "$pn" ] && [ "$outside" = 0 ] && ok "prev/next stay inside $c/ ($(echo "$pn" | wc -l | tr -d ' ') links)" || bad "prev/next leave the camera: $(echo $pn)"
    hasnt photo 'href="/g/' && ok "camera photo page: no href=\"/g/\"" || bad "camera photo page links a gallery"
  else
    bad "no second tile on $c to open"
  fi

  # no <script src> on any of the three
  for pg in cams cam photo; do
    hasnt "$pg" '<script[^>]* src=' && ok "$pg: no <script src>" || bad "$pg: has a <script src> (or no body)"
  done
else
  bad "no camera tile on /c to follow"
fi

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
