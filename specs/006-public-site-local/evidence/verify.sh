#!/usr/bin/env bash
# Live verification for 006-public-site-local. Run from the repo root, after the seed (T033):
#   bash specs/006-public-site-local/evidence/verify.sh | tee specs/006-public-site-local/evidence/verify-output.txt
# Needs: Docker (gallery-mongo), the owner's user-secrets, .dev-prepared/, seed/manifest.json.
# Never prints a secret: values are read into variables and compared, only file names are printed.
# The owner keeps `dotnet watch run` on :5080; this script builds into ./artifacts, runs its own
# instance on :5090, and stops only the PID it started.
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/006-public-site-local/evidence"
ART="artifacts"
BASE="http://localhost:5090"
IMG="https://img-dev.zacal.me/"
fails=0
pass() { echo "PASS $1 $2"; }
fail() { echo "FAIL $1 $2"; fails=$((fails+1)); }
APP_PID=""
stop_app() { [ -n "$APP_PID" ] && { pkill -P "$APP_PID" 2>/dev/null; kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null; }; APP_PID=""; sleep 1; }
trap stop_app EXIT
wait_up() { for _ in $(seq 1 60); do curl -s -o /dev/null "$1" && return 0; sleep 0.5; done; return 1; }
mongo() { docker exec gallery-mongo mongosh gallery_dev --quiet --eval "$1"; }
seed() { ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-build --no-launch-profile --artifacts-path "$ART" -- seed 2>&1 | grep -v '^{'; }

# 1. gates
out=$(dotnet build Gallery.slnx -warnaserror --artifacts-path "$ART" 2>&1)
echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
  && pass gate "build: 0 warnings, 0 errors" || fail gate "build"
out=$(dotnet test Gallery.slnx --no-build --artifacts-path "$ART" 2>&1)
[ "$(echo "$out" | grep -c '^Passed!')" = 2 ] && ! echo "$out" | grep -q '^Failed!' \
  && pass gate "tests: $(echo "$out" | grep -oE 'Passed: +[0-9]+' | awk '{s+=$2} END{print s}') passed in 2 assemblies" \
  || fail gate "tests: $(echo "$out" | grep -E 'Passed!|Failed!' | tr '\n' ' ')"
bash scripts/dev-mongo.sh >/dev/null || { fail start "gallery-mongo not available"; exit 1; }

# 2. SC-006 reseed idempotency (before the server starts, so the output cache cannot mask it)
dump() { mongo 'for (const c of ["site","galleries","images"]) db[c].find({}, {updatedAt:0}).sort({_id:1}).forEach(d => print(EJSON.stringify(d)))'; }
dump > /tmp/006-before.txt
s=$(seed)
dump > /tmp/006-after.txt
echo "$s" | grep -qx 'uploaded: 0' && cmp -s /tmp/006-before.txt /tmp/006-after.txt \
  && pass SC-006 "reseed: uploaded 0, $(wc -l < /tmp/006-after.txt | tr -d ' ') documents unchanged" \
  || fail SC-006 "reseed: $(echo "$s" | grep -E 'uploaded|rejected' | tr '\n' ' ') dumps-equal=$(cmp -s /tmp/006-before.txt /tmp/006-after.txt && echo yes || echo no)"

# 3. SC-011 metadata: first reader over every prepared file, second reader in the seed, no location keys
files=$(ls .dev-prepared/*/*.avif .dev-prepared/*/*.webp | wc -l | tr -d ' ')
bash scripts/check-metadata.sh .dev-prepared/*/*.avif .dev-prepared/*/*.webp >/dev/null 2>&1 \
  && pass SC-011 "check-metadata.sh: $files prepared files clean" || fail SC-011 "check-metadata.sh failed over .dev-prepared"
echo "$s" | grep -qx 'rejected: 0' && pass SC-011 "seed second reader (DerivativeValidator): rejected 0" || fail SC-011 "seed: $(echo "$s" | grep rejected)"
# Field NAMES, not text: base64 previews contain "gps" by chance.
keys=$(mongo 'let n=0; const w=o=>{for(const k in o){ if(/gps|latitude|longitude|altitude/i.test(k)) n++; if(o[k]&&typeof o[k]==="object") w(o[k]); }}; db.images.find().forEach(w); print(n)')
[ "$keys" = 0 ] && pass SC-011 "images: 0 location field names" || fail SC-011 "images: $keys location field names"

# 4. live server
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-build --no-launch-profile --artifacts-path "$ART" --urls "$BASE" > "$EV/dev-run.log" 2>&1 &
APP_PID=$!
wait_up "$BASE/" || { fail start "app did not start on :5090"; exit 1; }

home=$(curl -s "$BASE/")
pubids=$(mongo 'db.galleries.find({visibility:"public"}).forEach(g=>print(g._id+" "+g.slug))')
privids=$(mongo 'db.galleries.find({visibility:{$ne:"public"}}).forEach(g=>print(g._id+" "+g.slug+" "+(g.items[0]||"")))')
privtitles=$(mongo 'db.galleries.find({visibility:{$ne:"public"}}).forEach(g=>print(g.title))')

# SC-002 private galleries
bad=""
while read -r id slug img; do
  [ -z "$id" ] && continue
  echo "$home" | grep -q "$id" && bad="$bad listed:$id"
  for p in "/g/$id/$slug" ${img:+"/g/$id/$slug/$img"}; do
    h=$(curl -s -D - -o /tmp/006-p.html "$BASE$p")
    echo "$h" | head -1 | grep -q ' 200' || bad="$bad $p(status)"
    echo "$h" | grep -qi '^x-robots-tag: noindex' || bad="$bad $p(no-header)"
    grep -qE '<meta name="robots" content="noindex"' /tmp/006-p.html || bad="$bad $p(no-meta)"
  done
done <<< "$privids"
while read -r t; do [ -n "$t" ] && echo "$home" | grep -qF ">$t<" && bad="$bad title:$t"; done <<< "$privtitles"
np=$(echo "$privids" | grep -c .)
[ -z "$bad" ] && [ "$np" -ge 1 ] && pass SC-002 "$np private gallery: not on /, noindex header+meta on gallery and photo page" || fail SC-002 "private=$np $bad"
# negative case: public pages are indexable
read -r pid pslug <<< "$(echo "$pubids" | head -1)"
h=$(curl -s -D - -o /tmp/006-p.html "$BASE/g/$pid/$pslug")
! echo "$h" | grep -qi '^x-robots-tag' && ! grep -q 'content="noindex"' /tmp/006-p.html \
  && pass SC-002 "public gallery page carries no noindex" || fail SC-002 "public gallery page is noindex"

# SC-007 / SC-012 every image url on the image host; every image id reachable
allhtml=/tmp/006-all.html; : > "$allhtml"
echo "$home" >> "$allhtml"
seen=/tmp/006-seen.txt; : > "$seen"
while read -r id slug; do
  [ -z "$id" ] && continue
  g=$(curl -s "$BASE/g/$id/$slug"); echo "$g" >> "$allhtml"
  echo "$g" | grep -oE "href=\"/g/$id/$slug/[a-z2-7]{26}\"" | grep -oE '[a-z2-7]{26}"$' | tr -d '"' >> "$seen"
done <<< "$(mongo 'db.galleries.find().forEach(g=>print(g._id+" "+g.slug))')"
first_photo=$(grep -oE 'href="/g/[a-z2-7]{26}/[a-z0-9-]+/[a-z2-7]{26}"' "$allhtml" | head -1 | sed -E 's/href="(.*)"/\1/')
curl -s "$BASE$first_photo" >> "$allhtml"
urls=$(grep -oE '(src|srcset)="[^"]+"' "$allhtml" | sed -E 's/^(src|srcset)="//; s/"$//' | grep -v '^data:' | tr ',' '\n' | awk '{print $1}' | grep . | sort -u)
off=$(printf '%s\n' "$urls" | grep -vc "^$IMG")
nu=$(printf '%s\n' "$urls" | grep -c .)
[ "$off" = 0 ] && [ "$nu" -gt 0 ] && pass SC-007 "$nu distinct image URLs, all on $IMG (data: placeholders excluded)" || fail SC-007 "$off of $nu image URLs off $IMG"
imm=0; for u in $(printf '%s\n' "$urls" | sed -n 1,5p); do curl -sI "$u" | grep -qi '^cache-control: .*immutable' && imm=$((imm+1)); done
[ "$imm" = 5 ] && pass SC-007 "5 sampled image HEADs: Cache-Control immutable" || fail SC-007 "$imm/5 sampled HEADs immutable"
missing=0; total=0
for i in $(mongo 'db.images.find({},{_id:1}).forEach(d=>print(d._id))'); do total=$((total+1)); grep -qx "$i" "$seen" || missing=$((missing+1)); done
[ "$missing" = 0 ] && pass SC-012 "all $total images appear on a gallery page" || fail SC-012 "$missing of $total images on no gallery page"

# redirects and 404s (contracts/http.md)
red() { curl -s -o /dev/null -D - "$BASE$1"; }
h=$(red "/g/$pid/wrong-slug"); echo "$h" | head -1 | grep -q ' 301' && echo "$h" | grep -qi "^location: /g/$pid/$pslug" \
  && pass FR-009 "wrong slug -> 301 to current slug" || fail FR-009 "wrong slug"
h=$(red "/g/$pid"); echo "$h" | head -1 | grep -q ' 301' && echo "$h" | grep -qi '^cache-control: public, max-age=0, s-maxage=300' \
  && pass FR-009 "/g/{id} -> 301 with short cache" || fail FR-009 "/g/{id}"
code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/g/abc"); [ "$code" = 404 ] && pass FR-010 "malformed id -> 404" || fail FR-010 "malformed id -> $code"
h=$(curl -s -D - -o /tmp/006-p.html "$BASE/g/aaaaaaaaaaaaaaaaaaaaaaaaaa/x")
echo "$h" | head -1 | grep -q ' 404' && echo "$h" | grep -qi '^x-robots-tag: noindex' && grep -q 'Not found' /tmp/006-p.html \
  && pass FR-010 "unknown id -> 404 page, noindex" || fail FR-010 "unknown id"

# cookies, /ui
ck=0; for p in / "/g/$pid/$pslug" "$first_photo"; do curl -s -D - -o /dev/null "$BASE$p" | grep -qi '^set-cookie' && ck=$((ck+1)); done
[ "$ck" = 0 ] && pass II.1 "no Set-Cookie on public routes" || fail II.1 "$ck routes set a cookie"
code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/ui/"); [ "$code" = 200 ] && pass SC-010 "/ui/ still 200 in Development" || fail SC-010 "/ui/ -> $code"

# 5. SC-008 secret scan: the ACTUAL configured values, never printed
secrets=$(dotnet user-secrets list --project src/Gallery.Web 2>/dev/null)
KEY=$(echo "$secrets" | sed -n 's/^Storage:AccessKeyId = //p')
SECRET=$(echo "$secrets" | sed -n 's/^Storage:SecretAccessKey = //p')
ACCOUNT=$(echo "$secrets" | sed -n 's/^Storage:ServiceUrl = //p' | grep -oE '[a-f0-9]{32}')
unset secrets
if [ -z "$KEY" ] || [ -z "$SECRET" ] || [ -z "$ACCOUNT" ]; then
  fail SC-008 "could not load the configured secret values to scan for"
else
  hits=$(git ls-files -z | xargs -0 grep -lF -e "$KEY" -e "$SECRET" -e "$ACCOUNT" 2>/dev/null)
  [ -z "$hits" ] && pass SC-008 "committed tree: 0 files contain the key id, secret, or account id" || fail SC-008 "files: $hits"
  grep -qF -e "$KEY" -e "$SECRET" -e "$ACCOUNT" "$allhtml" && fail SC-008 "served HTML contains a configured secret value" || pass SC-008 "served HTML: no configured secret value"
fi
unset KEY SECRET ACCOUNT
grep -qiE 'r2\.cloudflarestorage\.com|\.r2\.dev|blob\.core' "$allhtml" && fail SC-008 "served HTML names a storage endpoint" || pass SC-008 "served HTML: no storage endpoint"
git check-ignore -q .r2-keys-copy && git check-ignore -q dev-pictures/x && git check-ignore -q .dev-prepared/x \
  && pass SC-008 ".r2-keys-copy, dev-pictures/, .dev-prepared/ are ignored" || fail SC-008 "an ignore rule is missing"

echo
[ "$fails" = 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
