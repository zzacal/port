#!/usr/bin/env bash
# Live-server curl walkthrough for 009-library-galleries (T058). From the repo root:
#   bash specs/009-library-galleries/evidence/verify.sh | tee specs/009-library-galleries/evidence/verify-output.txt
# Optional env: ART (artifacts path, default "artifacts"), TEST_FILTER (default "Category!=Evidence":
# the evidence tests rewrite committed evidence logs and are run by their own tasks; set TEST_FILTER=
# for the whole suite), SKIP_GATES=1 (skip build+test, reuse the last build in $ART).
#
# Isolation (never touches the owner's data or keys):
#   - storage: a throwaway S3Proxy container (dummy identity local/localsecret, transient), removed by
#     id at exit, with buckets gallery-dev-public-verify / gallery-dev-private-verify; R2 is never
#     contacted;
#   - Mongo: a fresh database gallery_verify009_<epoch>_dev on the dev container gallery-mongo,
#     dropped at exit; the gallery and gallery_dev databases are never named. The guard-refusal run
#     names gallery_verify (no _dev) and must create or change nothing;
#   - sign-in: the fake provider specs/008-upload-pipeline/evidence/fake-oidc.cs on
#     http://127.0.0.1:5095 with the literal dummies verify-client/verify-secret; every Storage/Mongo/
#     Google key the app reads is overridden by env var (the web host puts env vars after user-secrets);
#   - remove-seeded: its configuration adds user-secrets AFTER env vars (contracts/removal-command.md),
#     so the owner's Storage keys would win over the overrides. The command is therefore run with
#     HOME pointed at an empty temp dir, so no user-secrets file is found and only the overrides apply;
#   - no Edge:* setting anywhere: purges are log-only;
#   - the app runs from its built dll on https://localhost:5094 (antiforgery and __Host- cookies need
#     https; curl trusts the dev cert through its public part, read from the login keychain by
#     thumbprint). It never binds :5080/:5081.
# Each server is one pid started here and stopped with kill <pid>.
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/009-library-galleries/evidence"
EV8="specs/008-upload-pipeline/evidence"
FIX="tests/fixtures"
ART="${ART:-artifacts}"
TEST_FILTER="${TEST_FILTER-Category!=Evidence}"
APP="https://localhost:5094"
IDP="http://127.0.0.1:5095"
S3IMG="andrewgaul/s3proxy@sha256:87662b2a5afcdfa5f478a1c61650bae4c87bbd15f6ff2823236bcfd66ef7fe04"
DB="gallery_verify009_$(date +%s)_dev"
GUARD_DB="gallery_verify"
PUB="gallery-dev-public-verify"
PRIV="gallery-dev-private-verify"
fails=0
pass() { echo "PASS $1 $2"; }
fail() { echo "FAIL $1 $2"; fails=$((fails+1)); }
APP_PID=""; IDP_PID=""; CID=""
TMP=$(mktemp -d)
stop_app() { [ -n "$APP_PID" ] && { kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null; }; APP_PID=""; }
cleanup() {
  stop_app
  [ -n "$IDP_PID" ] && { kill "$IDP_PID" 2>/dev/null; wait "$IDP_PID" 2>/dev/null; }
  [ -n "$CID" ] && docker rm -f "$CID" >/dev/null 2>&1
  docker exec gallery-mongo mongosh --quiet --eval "db.getSiblingDB('$DB').dropDatabase()" >/dev/null 2>&1
  rm -rf "$TMP"
}
trap cleanup EXIT
redact_url() { sed -E 's/([?&])(X-Amz-SignedHeaders|X-Amz-Expires)=/\1\2~/g; s/([?&][^=&~]+)=[^&]*/\1=<v>/g; s/~/=/g'; }
hdr() { grep -i "^$1:" "$TMP/h" | head -1 | cut -d' ' -f2- | tr -d '\r'; }
status() { head -1 "$TMP/h" | awk '{print $2}'; }
mq() { docker exec gallery-mongo mongosh --quiet --eval "const d = db.getSiblingDB('$DB'); $1" 2>&1 | tr -d '\r'; }
# The antiforgery hidden field of the page in $TMP/b, read from the real markup.
form_token() { grep -oE 'name="__RequestVerificationToken" value="[^"]+"' "$TMP/b" | head -1 | sed -E 's/.*value="([^"]+)"/\1/'; }

# Every request to the app goes through req(), so REQS counts them for the http.request check.
# Output: headers in $TMP/h, body in $TMP/b.
REQS=0
C=(curl -s --cacert "$TMP/dev.pem" -b "$TMP/jar" -c "$TMP/jar" -D "$TMP/h" -o "$TMP/b")
req() { local m=$1 p=$2; shift 2; REQS=$((REQS+1)); "${C[@]}" -X "$m" "$@" "$APP$p"; echo "$m $(status)" >> "$TMP/sent"; }
post_json() { local p=$1 body=$2; shift 2; req POST "$p" -H 'Content-Type: application/json' --data-binary "$body" "$@"; }
csrf() { printf 'X-CSRF-TOKEN: %s' "$CSRF"; }
put_presigned() { curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Content-Type: $3" -H 'Expect:' --data-binary "@$2" "$1"; }
s3() { curl -s --aws-sigv4 "aws:amz:auto:s3" -u local:localsecret "$@"; }
# Purge lines the worker logged so far: "op count outcome" per edge.purge line.
purges() { grep '^{' "$EV/verify-run.log" | jq -r '.Message // empty' | grep '^edge\.purge ' ; }
# Waits (≤ 10 s) for a log-only skipped purge of op $1 with count ≥ 1 beyond the $2 already seen.
expect_purge() {
  local op=$1 before=$2 label=$3 n=0
  for _ in $(seq 1 40); do
    n=$(purges | awk -v op="$op" '$2==op && $3=="log-only" && $4>=1 && $6=="skipped"' | wc -l | tr -d ' ')
    [ "$n" -gt "$before" ] && break; sleep 0.25
  done
  local line; line=$(purges | awk -v op="$op" '$2==op && $3=="log-only" && $4>=1 && $6=="skipped"' | tail -1 | cut -c1-200)
  [ "$n" -gt "$before" ] && pass edge "$label: $line" || fail edge "$label: no new 'edge.purge $op log-only N … skipped' line (seen $n, before $before)"
}
purge_count() { purges | awk -v op="$1" '$2==op && $3=="log-only" && $4>=1 && $6=="skipped"' | wc -l | tr -d ' '; }

# ── 1. gates ────────────────────────────────────────────────────────────────────────────────────
if [ "${SKIP_GATES:-0}" = 1 ]; then
  echo "SKIP gate build+test (SKIP_GATES=1, reusing $ART)"
else
  out=$(dotnet build Gallery.slnx -warnaserror --artifacts-path "$ART" 2>&1)
  echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
    && pass gate "build: 0 warnings, 0 errors ($ART)" || { fail gate "build"; echo "$out" | grep -E 'error|warning' | sort -u | head -20; exit 1; }
  targs=(); [ -n "$TEST_FILTER" ] && targs=(--filter "$TEST_FILTER")
  out=$(dotnet test Gallery.slnx --no-build --artifacts-path "$ART" --blame-hang-timeout 120s "${targs[@]}" 2>&1)
  [ "$(echo "$out" | grep -c '^Passed!')" = 2 ] && ! echo "$out" | grep -q '^Failed!' \
    && pass gate "tests: $(echo "$out" | grep -oE 'Passed: +[0-9]+' | awk '{s+=$2} END{print s}') passed in 2 assemblies (filter: ${TEST_FILTER:-none})" \
    || { fail gate "tests: $(echo "$out" | grep -E 'Passed!|Failed!' | tr '\n' ' ')"; echo "$out" | grep -E '^\s+Failed ' | head -20; }
fi
DLL="$ART/bin/Gallery.Web/debug/Gallery.Web.dll"
[ -f "$DLL" ] || { fail start "$DLL not built"; exit 1; }

# ── 2. throwaway storage, fresh database, fake provider ─────────────────────────────────────────
docker info >/dev/null 2>&1 || { fail start "docker is not running"; exit 1; }
[ "$(docker container inspect -f '{{.State.Running}}' gallery-mongo 2>/dev/null)" = true ] \
  || { fail start "dev Mongo container gallery-mongo is not running"; exit 1; }
CID=$(docker run -d --rm -p 127.0.0.1::80 -e S3PROXY_AUTHORIZATION=aws-v2-or-v4 -e S3PROXY_IDENTITY=local \
  -e S3PROXY_CREDENTIAL=localsecret -e JCLOUDS_PROVIDER=transient "$S3IMG")
S3PORT=$(docker port "$CID" 80/tcp | head -1 | sed -E 's/.*:([0-9]+)$/\1/')
S3="http://localhost:$S3PORT"
for _ in $(seq 1 60); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$S3/")" = 403 ] && break; sleep 0.5; done
b1=$(s3 -o /dev/null -w '%{http_code}' -X PUT "$S3/$PUB"); b2=$(s3 -o /dev/null -w '%{http_code}' -X PUT "$S3/$PRIV")
[ "$b1" = 200 ] && [ "$b2" = 200 ] && pass setup "S3Proxy ${CID:0:12} on :$S3PORT, buckets $PUB/$PRIV created; Mongo db $DB" \
  || { fail setup "S3Proxy buckets: $PUB=$b1 $PRIV=$b2"; exit 1; }

# curl trusts the dev certificate through its PUBLIC part only, read from the login keychain by
# thumbprint. (`dotnet dev-certs https --export-path` also exports the private key, which raises a
# keychain access prompt and hangs an unattended run.)
THUMB=$(dotnet dev-certs https --check 2>/dev/null | grep -oE 'certificate was found: [0-9A-F]{40}' | awk '{print $4}' | head -1)
security find-certificate -a -c localhost -Z -p login.keychain-db 2>/dev/null \
  | awk -v t="$THUMB" '/^SHA-1 hash:/ {keep = ($3 == t); next} keep && /BEGIN CERTIFICATE/ {inb = 1} keep && inb {print} /END CERTIFICATE/ {inb = 0; if (keep) exit}' > "$TMP/dev.pem"
[ -n "$THUMB" ] && grep -q 'END CERTIFICATE' "$TMP/dev.pem" \
  || { fail setup "dev certificate not found (dotnet dev-certs https --trust)"; exit 1; }

idp_build=$(dotnet build "$EV8/fake-oidc.cs" 2>&1)
IDP_DLL=$(echo "$idp_build" | sed -n 's/^ *fake-oidc.cs -> //p' | tail -1)
[ -f "$IDP_DLL" ] || { fail setup "fake-oidc.cs did not build"; echo "$idp_build" | tail -5; exit 1; }
dotnet "$IDP_DLL" --urls "$IDP" > "$TMP/idp.log" 2>&1 &
IDP_PID=$!

# The env overrides shared by the app and the removal command (no Edge:* key: log-only purges).
ENVS=(ASPNETCORE_ENVIRONMENT=Development
  Mongo__ConnectionString="mongodb://localhost:27017" Mongo__Database="$DB"
  Storage__ServiceUrl="$S3" Storage__AccessKeyId=local Storage__SecretAccessKey=localsecret
  Storage__PublicBucket="$PUB" Storage__PrivateBucket="$PRIV" Storage__ImageBaseUrl="https://img.test"
  Authentication__Google__ClientId=verify-client Authentication__Google__ClientSecret=verify-secret
  Authentication__Google__Authority="$IDP" Authentication__Google__RequireHttpsMetadata=false)
env "${ENVS[@]}" dotnet "$DLL" --contentRoot "$PWD/src/Gallery.Web" --urls "$APP" > "$EV/verify-run.log" 2>&1 &
APP_PID=$!
up=0
for _ in $(seq 1 80); do
  curl -s -o /dev/null "$IDP/.well-known/openid-configuration" && curl -s -o /dev/null --cacert "$TMP/dev.pem" "$APP/" && { up=1; REQS=1; echo "GET 200" > "$TMP/sent"; break; }
  sleep 0.5
done
[ "$up" = 1 ] && pass setup "app pid $APP_PID on $APP, fake provider pid $IDP_PID on $IDP" \
  || { fail start "servers did not start"; tail -5 "$EV/verify-run.log"; exit 1; }
grep -q '"Edge' "$EV/verify-run.log" && echo "     note: an Edge entry appears in the startup log"

# ── 3. sign in through the fake provider (curl cookie jar) ──────────────────────────────────────
req GET /admin/signin
tok=$(form_token)
req POST /admin/signin --data-urlencode "__RequestVerificationToken=$tok"
loc=$(hdr location); unset tok
[ "$(status)" = 302 ] && [[ "$loc" == "$IDP/authorize?"* ]] \
  && pass signin "POST /admin/signin 302 to the fake provider: $(echo "$loc" | redact_url)" \
  || fail signin "challenge status=$(status) location=$(echo "$loc" | redact_url)"
curl -s -o "$TMP/idp.html" "$loc"
code=$(grep -oE 'name="code" value="[^"]+"' "$TMP/idp.html" | sed -E 's/.*value="([^"]+)"/\1/')
state=$(grep -oE 'name="state" value="[^"]+"' "$TMP/idp.html" | sed -E 's/.*value="([^"]+)"/\1/')
req POST /signin-oidc --data-urlencode "code=$code" --data-urlencode "state=$state"
cb_status=$(status); cb_loc=$(hdr location); unset code state
req GET /admin/upload
CSRF=$(grep -oE 'data-csrf="[^"]+"' "$TMP/b" | head -1 | sed -E 's/data-csrf="([^"]+)"/\1/')
[ "$cb_status" = 302 ] && [[ "$cb_loc" != *"r=refused"* && "$cb_loc" != *"r=failed"* ]] && [ "$(status)" = 200 ] && [ -n "$CSRF" ] \
  && pass signin "POST /signin-oidc $cb_status to $cb_loc; GET /admin/upload 200 with data-csrf" \
  || fail signin "callback=$cb_status location=$cb_loc upload=$(status) csrf=${CSRF:+present}"

# ── 4. two photos through the upload API (committed fixtures, as 008's verify) ──────────────────
PHOTOS=("IMG_0001.jpg|$FIX/gps-camera.jpg|$FIX/upload/gps-320.avif|$FIX/upload/gps-320.webp|$FIX/upload/gps-placeholder.webp"
        "Harbour at dusk.jpg|$FIX/orientation-6.jpg|$FIX/upload/orient6-200.avif|$FIX/upload/orient6-200.webp|$FIX/upload/orient6-placeholder.webp")
files=$(for p in "${PHOTOS[@]}"; do IFS='|' read -r n m _ _ _ <<< "$p"; jq -cn --arg n "$n" --argjson s "$(wc -c < "$m" | tr -d ' ')" '{name:$n,size:$s,type:"image/jpeg"}'; done | jq -cs '{files:.}')
post_json /admin/upload/items "$files" -H "$(csrf)"
cp "$TMP/b" "$TMP/reg.json"
BATCH=$(jq -r '.batchId' "$TMP/reg.json")
added_ok=0
for i in 0 1; do
  IFS='|' read -r name master avif webp ph <<< "${PHOTOS[$i]}"
  item=$(jq -r ".accepted[$i].itemId" "$TMP/reg.json"); att=$(jq -r ".accepted[$i].attemptId" "$TMP/reg.json")
  s_m=$(put_presigned "$(jq -r ".accepted[$i].master.url" "$TMP/reg.json")" "$master" image/jpeg)
  post_json "/admin/upload/items/$item/master" "$(jq -cn --arg a "$att" '{attemptId:$a}')" -H "$(csrf)"
  cp "$TMP/b" "$TMP/u4.json"
  s_a=$(put_presigned "$(jq -r '.sizes[0].avif.url' "$TMP/u4.json")" "$avif" image/avif)
  s_w=$(put_presigned "$(jq -r '.sizes[0].webp.url' "$TMP/u4.json")" "$webp" image/webp)
  body=$(jq -cn --arg a "$att" --arg p "data:image/webp;base64,$(base64 -i "$ph" | tr -d '\n')" \
    '{attemptId:$a, placeholder:{color:"#a1b2c3", preview:$p}, timings:[{op:"browser.decode-orient", ms:12.5}]}')
  post_json "/admin/upload/items/$item/complete" "$body" -H "$(csrf)"
  st=$(jq -r '.state' "$TMP/b" 2>/dev/null)
  [ "$s_m" = 200 ] && [ "$s_a" = 200 ] && [ "$s_w" = 200 ] && [ "$st" = succeeded ] && added_ok=$((added_ok+1)) \
    || echo "     upload $name: PUT master=$s_m avif=$s_a webp=$s_w state=$st"
done
post_json /admin/upload/confirm "$(jq -cn --arg b "$BATCH" '{batchId:$b}')" -H "$(csrf)"
cnt=$(jq -r '.count' "$TMP/b" 2>/dev/null)
[ "$added_ok" = 2 ] && [ "$(status)" = 200 ] && [ "$cnt" = 2 ] \
  && pass upload "2 photos registered, uploaded, completed and confirmed (count 2)" \
  || fail upload "completed=$added_ok confirm status=$(status) count=$cnt"

# ── 5. L1 library ───────────────────────────────────────────────────────────────────────────────
req GET /admin/library
IDS=($(grep -oE 'name="ids" value="[a-z2-7]{26}"' "$TMP/b" | sed -E 's/.*value="([a-z2-7]{26})"/\1/' | awk '!s[$0]++'))
TOK=$(form_token)
[ "$(status)" = 200 ] && [ "${#IDS[@]}" = 2 ] && [ -n "$TOK" ] && hdr cache-control | grep -q no-store \
  && pass L1 "GET /admin/library 200 no-store, 2 tiles (ids ${IDS[0]:0:6}… ${IDS[1]:0:6}…), __RequestVerificationToken field present" \
  || fail L1 "status=$(status) tiles=${#IDS[@]} token=${TOK:+present} cc=$(hdr cache-control)"

# ── 6. L3 add both to a new gallery ─────────────────────────────────────────────────────────────
req POST /admin/library/add --data-urlencode "__RequestVerificationToken=$TOK" --data-urlencode "page=1" \
  --data-urlencode "ids=${IDS[0]}" --data-urlencode "ids=${IDS[1]}" --data-urlencode "gallery=new" --data-urlencode "newTitle=Harbour walk"
loc=$(hdr location)
GID=$(echo "$loc" | sed -nE 's#^/admin/galleries/([a-z2-7]{26})\?added=2$#\1#p')
gdoc=$(mq "const g = d.galleries.findOne({_id:'$GID'}); print(g.visibility + ' ' + g.slug + ' ' + g.items.join(','))")
[ "$(status)" = 303 ] && [ -n "$GID" ] && [ "$gdoc" = "private harbour-walk ${IDS[0]},${IDS[1]}" ] \
  && pass L3 "POST /admin/library/add (gallery=new) 303 $loc; stored: $gdoc" \
  || fail L3 "status=$(status) location=$loc stored=$gdoc"

# ── 7. B1 builder + B3 reorder ──────────────────────────────────────────────────────────────────
req GET "/admin/galleries/$GID"
TOK=$(form_token)
ORDER=($(grep -oE 'name="order" value="[a-z2-7]{26}"' "$TMP/b" | sed -E 's/.*value="([a-z2-7]{26})"/\1/'))
[ "$(status)" = 200 ] && [ "${ORDER[*]}" = "${IDS[0]} ${IDS[1]}" ] && grep -q 'action="/admin/galleries/'"$GID"'/order"' "$TMP/b" \
  && pass B1 "GET /admin/galleries/{gid} 200, b-order form lists the 2 members in stored order" \
  || fail B1 "status=$(status) order=${ORDER[*]}"
req POST "/admin/galleries/$GID/order" --data-urlencode "__RequestVerificationToken=$TOK" \
  --data-urlencode "order=${ORDER[1]}" --data-urlencode "order=${ORDER[0]}"
loc=$(hdr location); items=$(mq "print(d.galleries.findOne({_id:'$GID'}).items.join(','))")
[ "$(status)" = 303 ] && [ "$loc" = "/admin/galleries/$GID?ordered=1" ] && [ "$items" = "${ORDER[1]},${ORDER[0]}" ] \
  && pass B3 "POST …/order (reversed) 303 $loc; items now reversed" \
  || fail B3 "status=$(status) location=$loc items=$items"
req GET "$loc"   # follow the PRG redirect
grep -q 'Order saved' "$TMP/b" && pass B3 "builder after PRG shows \"Order saved\"" || fail B3 "no 'Order saved' status after the redirect"
TOK=$(form_token)

# ── 8. G4 publish: without confirm (warning, no write), then with confirm=1 ─────────────────────
req POST "/admin/galleries/$GID/visibility" --data-urlencode "__RequestVerificationToken=$TOK" \
  --data-urlencode "from=builder" --data-urlencode "public=on"
vis=$(mq "print(d.galleries.findOne({_id:'$GID'}).visibility)")
[ "$(status)" = 200 ] && grep -q 'name="confirm" value="1"' "$TMP/b" && grep -q 'Make public' "$TMP/b" && [ "$vis" = private ] \
  && pass G4 "public=on without confirm: 200 re-render with the publish warning (Make public, name=confirm), stored visibility still private" \
  || fail G4 "no-confirm status=$(status) confirm-button=$(grep -c 'name="confirm"' "$TMP/b") stored=$vis"
TOK=$(form_token)
p_before=$(purge_count gallery.publish)
req POST "/admin/galleries/$GID/visibility" --data-urlencode "__RequestVerificationToken=$TOK" \
  --data-urlencode "from=builder" --data-urlencode "public=on" --data-urlencode "confirm=1"
loc=$(hdr location); vis=$(mq "print(d.galleries.findOne({_id:'$GID'}).visibility)")
[ "$(status)" = 303 ] && [ "$loc" = "/admin/galleries/$GID?visibility=public" ] && [ "$vis" = public ] \
  && pass G4 "public=on confirm=1: 303 $loc, stored visibility public" \
  || fail G4 "confirm status=$(status) location=$loc stored=$vis"
expect_purge gallery.publish "$p_before" "after G4 publish"

# ── 9. GET / lists it; the gallery page is cached before the rename ─────────────────────────────
req GET /
[ "$(status)" = 200 ] && grep -q "/g/$GID/harbour-walk" "$TMP/b" && grep -q 'Harbour walk' "$TMP/b" \
  && pass home "GET / 200 lists \"Harbour walk\" (/g/{gid}/harbour-walk)" \
  || fail home "status=$(status) has-link=$(grep -c "/g/$GID/harbour-walk" "$TMP/b")"
req GET "/g/$GID/harbour-walk"
[ "$(status)" = 200 ] && pass public "GET /g/{gid}/harbour-walk 200 (page now in the output cache)" || fail public "gallery page status=$(status)"

# ── 10. B2 rename → old URL 301 ─────────────────────────────────────────────────────────────────
req GET "/admin/galleries/$GID"; TOK=$(form_token)
r_before=$(purge_count gallery.update)
req POST "/admin/galleries/$GID" --data-urlencode "__RequestVerificationToken=$TOK" \
  --data-urlencode "title=Harbour at night" --data-urlencode "description=Lights on the water."
loc=$(hdr location); gdoc=$(mq "const g = d.galleries.findOne({_id:'$GID'}); print(g.slug + '|' + g.title + '|' + g.description)")
[ "$(status)" = 303 ] && [ "$loc" = "/admin/galleries/$GID?saved=1" ] && [ "$gdoc" = "harbour-at-night|Harbour at night|Lights on the water." ] \
  && pass B2 "POST /admin/galleries/{gid} 303 $loc; stored $gdoc" \
  || fail B2 "status=$(status) location=$loc stored=$gdoc"
expect_purge gallery.update "$r_before" "after B2 rename"
req GET "/g/$GID/harbour-walk"
loc=$(hdr location)
[ "$(status)" = 301 ] && [ "$loc" = "/g/$GID/harbour-at-night" ] \
  && pass B2 "old URL /g/{gid}/harbour-walk 301 → $loc (cached 200 evicted)" \
  || fail B2 "old URL status=$(status) location=$loc"
req GET "/g/$GID/harbour-at-night"
[ "$(status)" = 200 ] && grep -q 'Harbour at night' "$TMP/b" && pass B2 "new URL 200 with the new title" || fail B2 "new URL status=$(status)"

# ── 11. L2 save, then a BARE L2 POST keeps the values ───────────────────────────────────────────
IID=${IDS[0]}
req GET "/admin/library?page=1&open=$IID"; TOK=$(form_token)
grep -q "action=\"/admin/library/images/$IID\"" "$TMP/b" && grep -q 'name="caption"' "$TMP/b" \
  || fail L2 "the side panel form for $IID (title/caption) is not in the page"
t_before=$(purge_count image.update)
req POST "/admin/library/images/$IID" --data-urlencode "__RequestVerificationToken=$TOK" --data-urlencode "page=1" \
  --data-urlencode "title=Quay lamps" --data-urlencode "caption=Taken from the north pier."
loc=$(hdr location); idoc=$(mq "const i = d.images.findOne({_id:'$IID'}); print(i.title + '|' + i.caption)")
[ "$(status)" = 303 ] && [ "$loc" = "/admin/library?page=1&open=$IID&saved=1" ] && [ "$idoc" = "Quay lamps|Taken from the north pier." ] \
  && pass L2 "POST /admin/library/images/{iid} 303 $loc; stored $idoc" \
  || fail L2 "status=$(status) location=$loc stored=$idoc"
expect_purge image.update "$t_before" "after L2 save"
req GET "/g/$GID/harbour-at-night/$IID"
[ "$(status)" = 200 ] && grep -q 'Quay lamps' "$TMP/b" && grep -q 'Taken from the north pier.' "$TMP/b" \
  && pass L2 "photo page shows the new title and caption at once" || fail L2 "photo page status=$(status) has-title=$(grep -c 'Quay lamps' "$TMP/b")"
req GET "/admin/library?page=1&open=$IID"; TOK=$(form_token)
req POST "/admin/library/images/$IID" -H "X-CSRF-TOKEN: $TOK"
loc=$(hdr location); idoc=$(mq "const i = d.images.findOne({_id:'$IID'}); print(i.title + '|' + i.caption)")
[ "$(status)" = 303 ] && [ "$loc" = "/admin/library?page=1&open=$IID&saved=1" ] && [ "$idoc" = "Quay lamps|Taken from the north pier." ] \
  && pass L2 "BARE POST (no body, token in X-CSRF-TOKEN) 303 $loc; values kept: $idoc" \
  || fail L2 "bare status=$(status) location=$loc stored=$idoc body=$(head -c 200 "$TMP/b")"

# ── 12. G2 create an empty gallery, G4 on it → 409 ──────────────────────────────────────────────
req GET /admin/galleries; TOK=$(form_token)
grep -q 'action="/admin/galleries"' "$TMP/b" || fail G2 "no create form on /admin/galleries"
req POST /admin/galleries --data-urlencode "__RequestVerificationToken=$TOK" --data-urlencode "title=Empty for now"
loc=$(hdr location); EGID=$(echo "$loc" | sed -nE 's#^/admin/galleries/([a-z2-7]{26})$#\1#p')
[ "$(status)" = 303 ] && [ -n "$EGID" ] && pass G2 "POST /admin/galleries 303 $loc" || fail G2 "status=$(status) location=$loc"
req GET "/admin/galleries/$EGID"; TOK=$(form_token)
req POST "/admin/galleries/$EGID/visibility" --data-urlencode "__RequestVerificationToken=$TOK" \
  --data-urlencode "from=builder" --data-urlencode "public=on" --data-urlencode "confirm=1"
vis=$(mq "print(d.galleries.findOne({_id:'$EGID'}).visibility)")
[ "$(status)" = 409 ] && grep -q 'Add a photo before making this gallery public' "$TMP/b" && [ "$vis" = private ] \
  && pass G4 "empty gallery public=on confirm=1: 409 \"Add a photo before making this gallery public\", stored still private" \
  || fail G4 "empty status=$(status) stored=$vis"

# ── 13. a POST without the token → 400 ──────────────────────────────────────────────────────────
ng_before=$(mq "print(d.galleries.countDocuments({}))")
req POST /admin/galleries --data-urlencode "title=No token"
ng_after=$(mq "print(d.galleries.countDocuments({}))")
[ "$(status)" = 400 ] && [ "$ng_before" = "$ng_after" ] \
  && pass csrf "POST /admin/galleries without __RequestVerificationToken 400, galleries $ng_before → $ng_after" \
  || fail csrf "status=$(status) galleries $ng_before → $ng_after"

# ── 14. L5 delete one photo ─────────────────────────────────────────────────────────────────────
DIID=${IDS[1]}
keys=$(mq "const i = d.images.findOne({_id:'$DIID'}); print(i.derivatives.flatMap(x => [x.avif, x.webp]).join(' '))")
req GET "/admin/library?page=1&open=$DIID"; TOK=$(form_token)
grep -q "action=\"/admin/library/images/$DIID/delete\"" "$TMP/b" || fail L5 "no lib-del-one form for the open photo"
d_before=$(purge_count image.delete)
req POST "/admin/library/images/$DIID/delete" --data-urlencode "__RequestVerificationToken=$TOK" --data-urlencode "page=1"
loc=$(hdr location)
left=$(mq "print(d.images.countDocuments({_id:'$DIID'}) + ' ' + d.galleries.findOne({_id:'$GID'}).items.join(','))")
gone=0; for k in $keys; do [ "$(s3 -o /dev/null -w '%{http_code}' -I "$S3/$PUB/$k")" = 404 ] && gone=$((gone+1)); done
nkeys=$(echo "$keys" | wc -w | tr -d ' ')
[ "$(status)" = 303 ] && [ "$loc" = "/admin/library?page=1&deleted=1&leftovers=0" ] && [ "$left" = "0 $IID" ] && [ "$gone" = "$nkeys" ] && [ "$nkeys" -gt 0 ] \
  && pass L5 "POST /admin/library/images/{iid}/delete 303 $loc; record gone, left the gallery, $gone/$nkeys public files gone" \
  || fail L5 "status=$(status) location=$loc left=$left files-gone=$gone/$nkeys"
expect_purge image.delete "$d_before" "after L5 delete"

# ── 15. remove-seeded on the verify database ────────────────────────────────────────────────────
# Two seed-shaped documents (tests/Gallery.Web.Tests/Fixtures/SeedShapes.cs): a seeded image and a
# seeded public gallery holding it, linked through galleryIds/items, listed in site.homeOrder, with
# its 8 public files (1280 ladder × avif+webp) put in the public bucket.
SIID=seedverifyimageaaaaaaaaaaa   # 26 chars of [a-z2-7] (OpaqueId shape)
SGID=seedverifygalleryaaaaaaaaa
mq "
const iid = '$SIID', gid = '$SGID';
const ph = {color:'#8899aa', preview:'data:image/webp;base64,UklGRhIAAABXRUJQVlA4TAYAAAAvAAAAAAfQ//73v/+BiOh/AAA='};
const ders = [320,640,960,1280].map(w => ({w:w, avif:'seedverify'+w+'abcd.avif', webp:'seedverify'+w+'abcd.webp', bytesAvif:NumberLong(String(1000+w)), bytesWebp:NumberLong(String(1200+w))}));
d.images.insertOne({_id:iid, seedKey:'seed-verify.jpg', sourceHash:'5eed'.repeat(16), title:'Seeded verify', caption:null,
  width:1280, height:960, placeholder:ph, camera:null, derivatives:ders, galleryIds:[gid], createdAt:new Date()});
d.galleries.insertOne({_id:gid, seedKey:'g-seeded-verify', slug:'seeded-verify', title:'Seeded verify', description:null,
  visibility:'public', items:[iid], pinnedCover:null,
  card:{galleryId:gid, slug:'seeded-verify', title:'Seeded verify', imageCount:1,
        cover:{imageId:iid, width:1280, height:960, placeholder:ph, derivatives:ders}}, updatedAt:new Date()});
d.site.updateOne({_id:'site'}, {\$push:{homeOrder:gid}});
print('ok');" > "$TMP/seedins"
SKEYS="$(for w in 320 640 960 1280; do printf 'seedverify%sabcd.avif seedverify%sabcd.webp ' "$w" "$w"; done)"
printf x > "$TMP/x"   # -T: curl signs a file body correctly (--data-binary gets XAmzContentSHA256Mismatch)
put=0; for k in $SKEYS; do [ "$(s3 -o /dev/null -w '%{http_code}' -T "$TMP/x" "$S3/$PUB/$k")" = 200 ] && put=$((put+1)); done
[ "$(cat "$TMP/seedins")" = ok ] && [ "$put" = 8 ] \
  && pass seed "inserted a seed-shaped image + public gallery (mongosh) and their 8 public files" \
  || fail seed "insert=$(head -c 200 "$TMP/seedins") files=$put/8"

snapshot() { mq "print(JSON.stringify({i:d.images.find({seedKey:{\$exists:false}}).toArray(), g:d.galleries.find({seedKey:{\$exists:false}}).toArray()}))" | shasum | cut -c1-16; }
owner_before=$(snapshot)
counts() { mq "print(d.images.countDocuments({}) + ' images, ' + d.galleries.countDocuments({}) + ' galleries')"; }
echo "     before remove-seeded: $(counts) (owner content hash $owner_before)"
mkdir -p "$TMP/home"
# HOME → empty dir: no user-secrets file, so only the env overrides configure the command.
run_remove() { (cd "$PWD" && env HOME="$TMP/home" "${ENVS[@]}" "$@" dotnet "$DLL" remove-seeded > "$TMP/rm.out" 2>&1); echo $?; }
summary() { grep -v '^{' "$TMP/rm.out"; }
rc=$(run_remove)
expected1="seeded galleries removed: 1
seeded images removed: 1
owner galleries changed: 0
public files deleted: 8
public files kept (shared with an uploaded photo): 0
leftover files (logged): 0
The running app drops its page cache within 60 seconds."
sleft=$(mq "print(d.images.countDocuments({_id:'$SIID'}) + d.galleries.countDocuments({_id:'$SGID'}) + ' ' + d.site.findOne({_id:'site'}).homeOrder.includes('$SGID'))")
sfiles=0; for k in $SKEYS; do [ "$(s3 -o /dev/null -w '%{http_code}' -I "$S3/$PUB/$k")" = 404 ] && sfiles=$((sfiles+1)); done
owner_after=$(snapshot)
summary | sed 's/^/     | /'
[ "$rc" = 0 ] && [ "$(summary)" = "$expected1" ] && [ "$sleft" = "0 false" ] && [ "$sfiles" = 8 ] && [ "$owner_after" = "$owner_before" ] \
  && pass remove "run 1: exit 0, 1 gallery + 1 image removed, 8 public files deleted, homeOrder cleaned, owner content unchanged" \
  || fail remove "run 1: exit=$rc seeded-left=$sleft files-gone=$sfiles/8 owner $owner_before → $owner_after"
grep '^{' "$TMP/rm.out" | jq -r '.Message // empty' | grep -E '^(owner\.action seeded\.remove|edge\.purge |op\.timing maintenance\.remove-seeded)' | cut -c1-200 | sed 's/^/     log: /'
rc=$(run_remove)
expected2=$(echo "$expected1" | sed -E 's/: [0-9]+$/: 0/')
summary | sed 's/^/     | /'
[ "$rc" = 0 ] && [ "$(summary)" = "$expected2" ] && [ "$(snapshot)" = "$owner_before" ] \
  && pass remove "run 2: exit 0, every count 0, owner content unchanged" \
  || fail remove "run 2: exit=$rc"

# Guard refusal: Mongo:Database=gallery_verify (no _dev) → exit 4 and nothing is created or changed.
had_guard_db=$(docker exec gallery-mongo mongosh --quiet --eval "print(db.adminCommand({listDatabases:1}).databases.some(x => x.name == '$GUARD_DB'))" | tr -d '\r')
guard_before=$(docker exec gallery-mongo mongosh --quiet --eval "const g = db.getSiblingDB('$GUARD_DB'); print(g.images.countDocuments({}) + ' ' + g.galleries.countDocuments({}))" | tr -d '\r')
mq "d.images.insertOne(Object.assign(d.images.findOne({_id:'$IID'}), {_id:'$SIID', seedKey:'seed-guard.jpg'}))" >/dev/null
seeded_before=$(mq "print(d.images.countDocuments({seedKey:{\$exists:true}}))")
rc=$(run_remove Mongo__Database="$GUARD_DB")
has_guard_db=$(docker exec gallery-mongo mongosh --quiet --eval "print(db.adminCommand({listDatabases:1}).databases.some(x => x.name == '$GUARD_DB'))" | tr -d '\r')
guard_after=$(docker exec gallery-mongo mongosh --quiet --eval "const g = db.getSiblingDB('$GUARD_DB'); print(g.images.countDocuments({}) + ' ' + g.galleries.countDocuments({}))" | tr -d '\r')
seeded_after=$(mq "print(d.images.countDocuments({seedKey:{\$exists:true}}))")
summary | sed 's/^/     | /'
[ "$rc" = 4 ] && grep -q '^Target guard failed: Mongo:Database must end with _dev (got: gallery_verify)$' "$TMP/rm.out" \
  && [ "$(summary | grep -vc '^Target guard failed: ')" = 0 ] && [ "$has_guard_db" = "$had_guard_db" ] && [ "$guard_after" = "$guard_before" ] \
  && [ "$seeded_before" = 1 ] && [ "$seeded_after" = 1 ] \
  && pass guard "Mongo:Database=gallery_verify: exit 4 'Target guard failed: Mongo:Database must end with _dev', $GUARD_DB exists=$has_guard_db (before $had_guard_db), counts $guard_after; a seeded image in $DB was not touched" \
  || fail guard "exit=$rc db-exists $had_guard_db → $has_guard_db counts $guard_before → $guard_after seeded $seeded_before → $seeded_after"
mq "d.images.deleteOne({_id:'$SIID'})" >/dev/null

# ── 16. the captured server log ─────────────────────────────────────────────────────────────────
sleep 1   # let the edge worker log its last items
stop_app
LOG="$EV/verify-run.log"
msgs=$(grep '^{' "$LOG" | jq -r '.Message // empty')
nreq=$(echo "$msgs" | grep -c '^http\.request ')
echo "$msgs" | grep '^http\.request ' | awk '{print $2, $4}' > "$TMP/logged"
[ "$nreq" = "$REQS" ] && cmp -s "$TMP/sent" "$TMP/logged" \
  && pass FR-049 "$REQS app requests (readiness probe of / included), $nreq http.request lines, same method+status sequence" \
  || { fail FR-049 "$REQS app requests but $nreq http.request lines"; diff "$TMP/sent" "$TMP/logged" | head -10; }
for r in "GET /admin/library " "POST /admin/library/add " "POST /admin/library/images/{iid} " "POST /admin/library/images/{iid}/delete " \
         "POST /admin/galleries " "POST /admin/galleries/{gid} " "POST /admin/galleries/{gid}/order " "POST /admin/galleries/{gid}/visibility " \
         "GET /g/{id}/{slug} "; do
  n=$(echo "$msgs" | grep -c "^http\.request $r")
  [ "$n" -gt 0 ] && pass FR-049 "$n x 'http.request ${r% }' (route template)" || fail FR-049 "no 'http.request ${r% }' line"
done
nonlog=$(echo "$msgs" | grep '^edge\.purge ' | awk '$3!="log-only"' | wc -l | tr -d ' ')
npurge=$(echo "$msgs" | grep -c '^edge\.purge ')
[ "$npurge" -gt 0 ] && [ "$nonlog" = 0 ] && pass edge "$npurge edge.purge lines, every one mode log-only" || fail edge "$npurge edge.purge lines, $nonlog not log-only"
for ev in "owner.action gallery.publish" "owner.action image.delete" "op.timing db.library.page" "op.timing db.site.project" "op.timing library.image.delete" "edge.lag "; do
  n=$(echo "$msgs" | grep -c "^$ev")
  [ "$n" -gt 0 ] && pass FR-050 "$n x '${ev% }'" || fail FR-050 "no '${ev% }' event"
done
PK="p""kill"; grep -q "$PK" "$0" && fail script "$PK appears in $0" || pass script "$PK appears nowhere in this script (servers stopped by kill <pid>)"
long=$(grep -oE '[A-Za-z0-9_-]{32,}' "$LOG" | sort -u | wc -l | tr -d ' ')
echo "     $LOG: $(wc -l < "$LOG" | tr -d ' ') lines, $long distinct 32+-character tokens (redact before committing)"
grep -qE 'X-Amz-Signature|X-Amz-Credential|localsecret|verify-secret' "$LOG" \
  && fail secrets "a presigned URL or a credential value appears in $LOG" || pass secrets "no presigned URL or credential value in $LOG"
werr=$(grep '^{' "$LOG" | jq -r 'select(.LogLevel=="Warning" or .LogLevel=="Error" or .LogLevel=="Critical") | "\(.LogLevel) \(.Category): \(.Message)"' | sort | uniq -c)
[ -z "$werr" ] && echo "     no Warning/Error entries in the log" || { echo "     Warning/Error entries:"; echo "$werr" | sed 's/^/       /' | cut -c1-220; }

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
