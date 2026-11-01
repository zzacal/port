#!/usr/bin/env bash
# Live-server curl walkthrough for 008-upload-pipeline (T050; quickstart Part B). From the repo root:
#   bash specs/008-upload-pipeline/evidence/verify.sh | tee specs/008-upload-pipeline/evidence/verify-output.txt
# Optional env: ART (artifacts path, default "artifacts"), TEST_FILTER (default "Category!=Evidence":
# the evidence tests rewrite committed evidence logs and are run by their own tasks; set TEST_FILTER=
# for the whole suite), SKIP_GATES=1 (skip build+test, reuse the last build in $ART).
#
# Isolation (never touches the owner's data or keys):
#   - storage: a throwaway S3Proxy container (dummy identity local/localsecret), removed by id at exit;
#   - Mongo: a fresh database gallery-verify-008-<epoch> on the dev container gallery-mongo, dropped
#     at exit; the gallery and gallery_dev databases are never named;
#   - sign-in: the fake provider evidence/fake-oidc.cs on http://127.0.0.1:5093, with the literal
#     dummies verify-client/verify-secret; every Storage/Mongo/Google key the app reads is overridden
#     by env var, so values from user-secrets are never used (and never read by this script);
#   - the app runs from its built dll on https://localhost:5092 (antiforgery and __Host- cookies need
#     https; the dev cert is exported to a temp PEM for curl). It never binds :5080/:5081.
# Each server is one pid started here and stopped with kill <pid>. Presigned URLs are printed with
# query values redacted (only X-Amz-SignedHeaders and X-Amz-Expires are shown).
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/008-upload-pipeline/evidence"
FIX="tests/fixtures"
ART="${ART:-artifacts}"
TEST_FILTER="${TEST_FILTER-Category!=Evidence}"
APP="https://localhost:5092"
IDP="http://127.0.0.1:5093"
S3IMG="andrewgaul/s3proxy@sha256:87662b2a5afcdfa5f478a1c61650bae4c87bbd15f6ff2823236bcfd66ef7fe04"
DB="gallery-verify-008-$(date +%s)"
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

# Every request to the app goes through req(), so REQS counts them for the http.request check.
# Output: headers in $TMP/h, body in $TMP/b.
REQS=0
C=(curl -s --cacert "$TMP/dev.pem" -b "$TMP/jar" -c "$TMP/jar" -D "$TMP/h" -o "$TMP/b")
req() { local m=$1 p=$2; shift 2; REQS=$((REQS+1)); "${C[@]}" -X "$m" "$@" "$APP$p"; echo "$m $(status)" >> "$TMP/sent"; }
post_json() { local p=$1 body=$2; shift 2; req POST "$p" -H 'Content-Type: application/json' --data-binary "$body" "$@"; }
csrf() { printf 'X-CSRF-TOKEN: %s' "$CSRF"; }
put_presigned() { # url file content-type -> http status of the S3 PUT (headers sent: Content-Type + curl defaults)
  curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Content-Type: $3" -H 'Expect:' --data-binary "@$2" "$1"
}
s3() { curl -s --aws-sigv4 "aws:amz:auto:s3" -u local:localsecret "$@"; }

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
b1=$(s3 -o /dev/null -w '%{http_code}' -X PUT "$S3/pub"); b2=$(s3 -o /dev/null -w '%{http_code}' -X PUT "$S3/priv")
[ "$b1" = 200 ] && [ "$b2" = 200 ] && pass setup "S3Proxy ${CID:0:12} on :$S3PORT, buckets pub/priv created; Mongo db $DB" \
  || { fail setup "S3Proxy buckets: pub=$b1 priv=$b2"; exit 1; }

# curl trusts the dev certificate through its PUBLIC part only, read from the login keychain by
# thumbprint. (`dotnet dev-certs https --export-path` also exports the private key, which raises a
# keychain access prompt and hangs an unattended run.)
THUMB=$(dotnet dev-certs https --check 2>/dev/null | grep -oE 'certificate was found: [0-9A-F]{40}' | awk '{print $4}' | head -1)
security find-certificate -a -c localhost -Z -p login.keychain-db 2>/dev/null \
  | awk -v t="$THUMB" '/^SHA-1 hash:/ {keep = ($3 == t); next} keep && /BEGIN CERTIFICATE/ {inb = 1} keep && inb {print} /END CERTIFICATE/ {inb = 0; if (keep) exit}' > "$TMP/dev.pem"
[ -n "$THUMB" ] && grep -q 'END CERTIFICATE' "$TMP/dev.pem" \
  || { fail setup "dev certificate not found (dotnet dev-certs https --trust)"; exit 1; }

idp_build=$(dotnet build "$EV/fake-oidc.cs" 2>&1)
IDP_DLL=$(echo "$idp_build" | sed -n 's/^ *fake-oidc.cs -> //p' | tail -1)
[ -f "$IDP_DLL" ] || { fail setup "fake-oidc.cs did not build"; echo "$idp_build" | tail -5; exit 1; }
dotnet "$IDP_DLL" --urls "$IDP" > "$TMP/idp.log" 2>&1 &
IDP_PID=$!

ASPNETCORE_ENVIRONMENT=Development \
Mongo__ConnectionString="mongodb://localhost:27017" Mongo__Database="$DB" \
Storage__ServiceUrl="$S3" Storage__AccessKeyId=local Storage__SecretAccessKey=localsecret \
Storage__PublicBucket=pub Storage__PrivateBucket=priv Storage__ImageBaseUrl="https://img.test" \
Authentication__Google__ClientId=verify-client Authentication__Google__ClientSecret=verify-secret \
Authentication__Google__Authority="$IDP" Authentication__Google__RequireHttpsMetadata=false \
  dotnet "$DLL" --contentRoot "$PWD/src/Gallery.Web" --urls "$APP" > "$EV/verify-run.log" 2>&1 &
APP_PID=$!
up=0
for _ in $(seq 1 80); do
  curl -s -o /dev/null "$IDP/.well-known/openid-configuration" && curl -s -o /dev/null --cacert "$TMP/dev.pem" "$APP/" && { up=1; REQS=1; echo "GET 200" > "$TMP/sent"; break; }
  sleep 0.5
done
[ "$up" = 1 ] && pass setup "app pid $APP_PID on $APP, fake provider pid $IDP_PID on $IDP" \
  || { fail start "servers did not start"; tail -5 "$EV/verify-run.log"; exit 1; }

# ── 3. sign in through the fake provider (curl cookie jar) ──────────────────────────────────────
req GET /admin/signin
tok=$(grep -oE 'name="__RequestVerificationToken" value="[^"]+"' "$TMP/b" | sed -E 's/.*value="([^"]+)"/\1/' | head -1)
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
SCRIPT=$(grep -oE '<script type="module" src="[^"]+"' "$TMP/b" | head -1 | sed -E 's/.*src="([^"]+)"/\1/')
page_presigned=$(grep -c 'X-Amz-' "$TMP/b")
[ "$cb_status" = 302 ] && [[ "$cb_loc" != *"r=refused"* && "$cb_loc" != *"r=failed"* ]] && [ "$(status)" = 200 ] && [ -n "$CSRF" ] \
  && pass signin "POST /signin-oidc $cb_status to $cb_loc; GET /admin/upload 200 with data-csrf" \
  || fail signin "callback=$cb_status location=$cb_loc upload=$(status) csrf=${CSRF:+present}"
hdr cache-control | grep -q no-store && [ "$page_presigned" = 0 ] \
  && pass U1 "/admin/upload no-store, no presigned URL in the page" \
  || fail U1 "/admin/upload cache-control=$(hdr cache-control) X-Amz-count=$page_presigned"

# ── 4. checks before the flow ───────────────────────────────────────────────────────────────────
req GET /admin/upload/state
hdr cache-control | grep -q no-store && [ "$(jq -c '.batch' "$TMP/b")" = null ] \
  && pass U2 "/admin/upload/state 200 no-store, batch null" \
  || fail U2 "/admin/upload/state status=$(status) cc=$(hdr cache-control) body=$(head -c 200 "$TMP/b")"

req GET "$SCRIPT"
[ "$(status)" = 200 ] && [[ "$SCRIPT" == /admin-assets/upload.js\?v=* ]] && hdr cache-control | grep -q immutable \
  && pass assets "$SCRIPT 200 $(hdr cache-control)" \
  || fail assets "script=$SCRIPT status=$(status) cc=$(hdr cache-control)"

one='{"files":[{"name":"a.jpg","size":1000,"type":"image/jpeg"}]}'
post_json /admin/upload/items "$one"
[ "$(status)" = 400 ] && pass csrf "POST /admin/upload/items without X-CSRF-TOKEN 400" || fail csrf "no-token status=$(status)"

jq -cn '{files:[range(0;1300)|{name:(("x"*130)+".jpg"),size:1000,type:"image/jpeg"}]}' > "$TMP/big.json"
bigsize=$(wc -c < "$TMP/big.json" | tr -d ' ')
post_json /admin/upload/items "@$TMP/big.json" -H "$(csrf)"
[ "$(status)" = 413 ] && pass limits "register body of $bigsize bytes 413 ($(jq -r '.error' "$TMP/b" 2>/dev/null))" \
  || fail limits "register body of $bigsize bytes status=$(status)"

# ── 5. U3 → PUT master → U4 → PUT sizes → U7 → U10 (two photos, committed fixtures) ──────────────
# name|master|avif|webp|placeholder (gps-camera.jpg 400x200 -> [320]; orientation-6.jpg upright 200x400 -> [200])
PHOTOS=("IMG_0001.jpg|$FIX/gps-camera.jpg|$FIX/upload/gps-320.avif|$FIX/upload/gps-320.webp|$FIX/upload/gps-placeholder.webp"
        "Harbour at dusk.jpg|$FIX/orientation-6.jpg|$FIX/upload/orient6-200.avif|$FIX/upload/orient6-200.webp|$FIX/upload/orient6-placeholder.webp")
files=$(for p in "${PHOTOS[@]}"; do IFS='|' read -r n m _ _ _ <<< "$p"; jq -cn --arg n "$n" --argjson s "$(wc -c < "$m" | tr -d ' ')" '{name:$n,size:$s,type:"image/jpeg"}'; done | jq -cs '{files:.}')
post_json /admin/upload/items "$files" -H "$(csrf)"
cp "$TMP/b" "$TMP/reg.json"
BATCH=$(jq -r '.batchId' "$TMP/reg.json")
nacc=$(jq '.accepted|length' "$TMP/reg.json"); nref=$(jq '.refused|length' "$TMP/reg.json")
[ "$(status)" = 200 ] && [ "$nacc" = 2 ] && [ "$nref" = 0 ] \
  && pass U3 "register 2 photos 200, batch $BATCH, accepted 2, refused 0" \
  || fail U3 "register status=$(status) accepted=$nacc refused=$nref"

urls=0; badurls=0
check_url() { urls=$((urls+1)); echo "     presigned $(echo "$1" | redact_url)"; [[ "$1" == *"X-Amz-SignedHeaders=content-type%3Bhost"* && "$1" != *"X-Amz-SignedHeaders=content-type%3Bhost%3B"* ]] || badurls=$((badurls+1)); }
added_ok=0
for i in 0 1; do
  IFS='|' read -r name master avif webp ph <<< "${PHOTOS[$i]}"
  item=$(jq -r ".accepted[$i].itemId" "$TMP/reg.json"); att=$(jq -r ".accepted[$i].attemptId" "$TMP/reg.json")
  murl=$(jq -r ".accepted[$i].master.url" "$TMP/reg.json"); check_url "$murl"
  s_m=$(put_presigned "$murl" "$master" image/jpeg)
  post_json "/admin/upload/items/$item/master" "$(jq -cn --arg a "$att" '{attemptId:$a}')" -H "$(csrf)"
  u4=$(status); cp "$TMP/b" "$TMP/u4.json"
  w=$(jq -r '.sizes[0].w' "$TMP/u4.json"); nsz=$(jq '.sizes|length' "$TMP/u4.json")
  aurl=$(jq -r '.sizes[0].avif.url' "$TMP/u4.json"); wurl=$(jq -r '.sizes[0].webp.url' "$TMP/u4.json")
  check_url "$aurl"; check_url "$wurl"
  s_a=$(put_presigned "$aurl" "$avif" image/avif); s_w=$(put_presigned "$wurl" "$webp" image/webp)
  body=$(jq -cn --arg a "$att" --arg p "data:image/webp;base64,$(base64 -i "$ph" | tr -d '\n')" \
    '{attemptId:$a, placeholder:{color:"#a1b2c3", preview:$p}, timings:[{op:"browser.decode-orient", ms:12.5}]}')
  post_json "/admin/upload/items/$item/complete" "$body" -H "$(csrf)"
  u7=$(status); st=$(jq -r '.state' "$TMP/b" 2>/dev/null)
  [ "$s_m" = 200 ] && [ "$u4" = 200 ] && [ "$nsz" = 1 ] && [ "$s_a" = 200 ] && [ "$s_w" = 200 ] && [ "$u7" = 200 ] && [ "$st" = succeeded ] \
    && { pass flow "$name: PUT master 200, U4 200 ladder [$w], PUT avif+webp 200, U7 200 succeeded"; added_ok=$((added_ok+1)); } \
    || fail flow "$name: PUT master=$s_m U4=$u4 sizes=$nsz PUT avif=$s_a webp=$s_w U7=$u7 state=$st body=$(head -c 300 "$TMP/b")"
done
[ "$urls" = 6 ] && [ "$badurls" = 0 ] && pass FR-044 "all $urls presigned URLs sign exactly content-type;host" \
  || fail FR-044 "$badurls of $urls presigned URLs do not carry X-Amz-SignedHeaders=content-type%3Bhost"

req GET /admin/upload/state
ready=$(jq '[.batch.items[]|select(.state=="succeeded")]|length' "$TMP/b" 2>/dev/null)
[ "$ready" = 2 ] && pass U2 "state after U7: 2 of 2 succeeded" || fail U2 "state after U7: succeeded=$ready"

pre_staging=$(s3 "$S3/priv?list-type=2&prefix=staging/$BATCH/" | grep -o '<Key>' | wc -l | tr -d ' ')
post_json /admin/upload/confirm "$(jq -cn --arg b "$BATCH" '{batchId:$b}')" -H "$(csrf)"
cnt=$(jq -r '.count' "$TMP/b" 2>/dev/null); red=$(jq -r '.redirect' "$TMP/b" 2>/dev/null)
[ "$(status)" = 200 ] && [ "$cnt" = 2 ] && [ "$red" = "/admin/upload?confirmed=$BATCH" ] \
  && pass U10 "confirm 200, count 2, redirect $red" \
  || fail U10 "confirm status=$(status) body=$(head -c 300 "$TMP/b")"

post_staging=$(s3 "$S3/priv?list-type=2&prefix=staging/$BATCH/" | grep -o '<Key>' | wc -l | tr -d ' ')
masters=$(s3 "$S3/priv?list-type=2&prefix=masters/" | grep -o '<Key>' | wc -l | tr -d ' ')
public=$(s3 "$S3/pub?list-type=2" | grep -o '<Key>' | wc -l | tr -d ' ')
[ "$pre_staging" -gt 0 ] && [ "$post_staging" = 0 ] && [ "$masters" = 2 ] && [ "$public" = 4 ] \
  && pass SC-003 "staging/$BATCH/ held $pre_staging objects before confirm, 0 after; masters/ 2, public bucket 4 (2 photos x avif+webp)" \
  || fail SC-003 "staging before=$pre_staging after=$post_staging masters=$masters public=$public"

req GET "/admin/upload?confirmed=$BATCH"
grep -q '2 photos added to your library' "$TMP/b" && pass U1 "result page: \"2 photos added to your library\"" \
  || fail U1 "result page status=$(status) has no '2 photos added' text"

# ── 6. the captured server log ──────────────────────────────────────────────────────────────────
stop_app
LOG="$EV/verify-run.log"
msgs=$(grep '^{' "$LOG" | jq -r '.Message // empty')
nreq=$(echo "$msgs" | grep -c '^http\.request ')
echo "$msgs" | grep '^http\.request ' | awk '{print $2, $4}' > "$TMP/logged"
[ "$nreq" = "$REQS" ] && cmp -s "$TMP/sent" "$TMP/logged" \
  && pass FR-037 "$REQS app requests (readiness probe of / included), $nreq http.request lines, same method+status sequence" \
  || { fail FR-037 "$REQS app requests but $nreq http.request lines"; diff "$TMP/sent" "$TMP/logged" | head -10; }
for ev in "op.timing s3.presign" "op.timing upload.master-check" "op.timing upload.validate" "op.timing upload.confirm " \
          "op.timing browser.decode-orient" "upload.confirmed $BATCH 2 "; do
  n=$(echo "$msgs" | grep -c "^$ev")
  [ "$n" -gt 0 ] && pass FR-038 "$n x '${ev% }'" || fail FR-038 "no '${ev% }' event"
done
long=$(grep -oE '[A-Za-z0-9_-]{32,}' "$LOG" | sort -u | wc -l | tr -d ' ')
echo "     $LOG: $(wc -l < "$LOG" | tr -d ' ') lines, $long distinct 32+-character tokens (redact before committing)"
grep -qE 'X-Amz-Signature|X-Amz-Credential|localsecret|verify-secret' "$LOG" \
  && fail secrets "a presigned URL or a credential value appears in $LOG" || pass secrets "no presigned URL or credential value in $LOG"
werr=$(grep '^{' "$LOG" | jq -r 'select(.LogLevel=="Warning" or .LogLevel=="Error" or .LogLevel=="Critical") | "\(.LogLevel) \(.Category): \(.Message)"' | sort | uniq -c)
[ -z "$werr" ] && echo "     no Warning/Error entries in the log" || { echo "     Warning/Error entries:"; echo "$werr" | sed 's/^/       /' | cut -c1-220; }

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
