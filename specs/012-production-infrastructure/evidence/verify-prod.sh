#!/usr/bin/env bash
# 012 T014 (FR-025, contracts/evidence.md "verify-prod.sh"): read-only checks of a running site.
# GETs only. Ports the public checks of 011 verify.sh and the 006/010 public-page header checks,
# reads every id from the live listing (no datastore access), and adds the 012 edge checks.
#
#   bash specs/012-production-infrastructure/evidence/verify-prod.sh https://port.zacal.me <origin-default-hostname> \
#     | tee specs/012-production-infrastructure/evidence/verify-prod-output.txt
#
# $1  base URL (https://port.zacal.me, or http://127.0.0.1:<port> for the local self-test)
# $2  origin default hostname (portfolio-web-....azurewebsites.net) or a URL; a bare host gets https://
# Env (all optional):
#   ORIGIN_SECRET  sent as X-Gallery-Origin on every request EXCEPT the SC-002 origin-refusal ones.
#                  Local self-test only; never set it for production runs. Never printed.
#   IMG_BASE       expected image URL prefix (default https://img.zacal.me/).
#   SECRETS_FILE   overrides infra/.secrets.local (self-test of the SC-009 rows only).
# A base URL of http://127.0.0.1:* or http://localhost:* has no edge: the TLS, http->https and
# cf-cache-status rows print "SKIP (no edge)".
# SC-009 loads the secret values from infra/.secrets.local (KEY=VALUE lines, # comments) inside
# this script and prints only the key name and found / not found; absent file -> SKIP.
# Exit status: 0 when no FAIL, else the number of FAIL rows (capped at 125).
set -uo pipefail
[ $# -ge 2 ] || { echo "usage: $0 <base-url> <origin-default-hostname|url>" >&2; exit 2; }

B=${1%/}
O=${2%/}
case "$O" in http://*|https://*) ;; *) O="https://$O" ;; esac
IMG=${IMG_BASE:-https://img.zacal.me/}
PUBHOST=$(printf '%s' "$B" | sed -E 's#^[a-z]+://##; s#/.*$##')
HOSTONLY=${PUBHOST%%:*}
NOEDGE=0
case "$B" in http://127.0.0.1:*|http://localhost:*|http://127.0.0.1|http://localhost) NOEDGE=1 ;; esac

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(git -C "$HERE" rev-parse --show-toplevel 2>/dev/null || (cd "$HERE/../../.." && pwd))
T=$(mktemp -d /tmp/012-verify.XXXXXX)
chmod 700 "$T"
trap 'rm -rf "$T"' EXIT

# Header file for the origin secret: keeps the value off the command line and out of any output.
HF=""
if [ -n "${ORIGIN_SECRET:-}" ]; then
  HF="$T/origin.h"; ( umask 077; printf 'X-Gallery-Origin: %s\n' "$ORIGIN_SECRET" > "$HF" )
fi

fails=0
ok()   { echo "PASS $*"; }
bad()  { echo "FAIL $*"; fails=$((fails+1)); }
skip() { echo "SKIP $*"; }
CURL=(curl -s --max-time 20 --connect-timeout 5)
# c ARGS...: curl with the origin secret header when ORIGIN_SECRET is set
c() { if [ -n "$HF" ]; then "${CURL[@]}" -H "@$HF" "$@"; else "${CURL[@]}" "$@"; fi; }
# get NAME PATH [curl args]: body -> $T/NAME.html, headers -> $T/NAME.h, prints the status (000 = no answer)
get() { local n=$1 p=$2; shift 2; c -o "$T/$n.html" -D "$T/$n.h" -w '%{http_code}' "$@" "$B$p" 2>/dev/null || true; }
has()   { [ -s "$T/$1.html" ] && grep -q -- "$2" "$T/$1.html"; }
hasF()  { [ -s "$T/$1.html" ] && grep -qF -- "$2" "$T/$1.html"; }
# hasnt: the body must exist and be non-empty (a missing body is not "absent"), then lack the pattern
hasnt() { [ -s "$T/$1.html" ] && ! grep -q -- "$2" "$T/$1.html"; }
hdrv()  { [ -f "$T/$1.h" ] && grep -i "^$2:" "$T/$1.h" | head -1 | sed -E 's/^[^:]*:[[:space:]]*//' | tr -d '\r'; }
hdr()   { [ -f "$T/$1.h" ] && grep -qi "^$2:" "$T/$1.h"; }
root()  { [ -s "$T/$1.html" ] && perl -0ne 's/ b-[a-z0-9]{10}//g; print $1 if /<nav class="breadcrumb"[^>]*>.*?<li[^>]*>(?:<[^>]+>)*([^<]+)</s' "$T/$1.html"; }
echo "base $B  origin $O  edge $([ $NOEDGE = 1 ] && echo none || echo yes)  origin-secret $([ -n "$HF" ] && echo set || echo unset)"

# ── 011: home, listing, /g/ redirect ──────────────────────────────────────────
s=$(get home /)
[ "$s" = 200 ] && ok "GET / 200" || bad "GET / $s"
has home 'class="nav nav--home"' && ok "/ has nav.nav--home" || bad "/ has no nav.nav--home"
has home '<a href="/g"[^>]*>Galleries</a>' && ok "/ links Galleries to /g" || bad "/ has no Galleries link to /g"
hasnt home '<div class="cs ' && ok "/ has no ContactSheet element" || bad "/ renders a ContactSheet (or no body)"

s=$(get listing /g)
[ "$s" = 200 ] && ok "GET /g 200" || bad "GET /g $s"
nt=$( [ -s "$T/listing.html" ] && grep -o 'class="tile"' "$T/listing.html" | wc -l | tr -d ' ' || echo 0)
[ "${nt:-0}" -gt 0 ] && ok "/g lists tiles ($nt)" || bad "/g has no tiles"

s=$(get slash /g/)
loc=$(hdrv slash Location)
[ "$s" = 301 ] && { [ "$loc" = /g ] || [ "$loc" = "$B/g" ]; } && ok "GET /g/ 301 -> /g" || bad "GET /g/ $s -> ${loc:-none}"

# ids from the live listing (no datastore lookups)
g=$( [ -s "$T/listing.html" ] && grep -oE 'href="/g/[a-z2-7]{26}/[^"/#]+"' "$T/listing.html" | head -1 | sed -E 's/href="([^"]*)"/\1/')
p=""; gid=""; gslug=""
if [ -n "$g" ]; then
  gid=$(echo "$g" | cut -d/ -f3); gslug=$(echo "$g" | cut -d/ -f4)
  s=$(get gallery "$g")
  [ "$s" = 200 ] && ok "GET gallery $g 200" || bad "GET gallery $g $s"
  p=$( [ -s "$T/gallery.html" ] && grep -oE "href=\"$g/[a-z2-7]{26}\"" "$T/gallery.html" | head -1 | sed -E 's/href="([^"]*)"/\1/')
  if [ -n "$p" ]; then
    s=$(get photo "$p")
    [ "$s" = 200 ] && ok "GET photo $p 200" || bad "GET photo $p $s"
  else
    bad "gallery $g links no photo"
  fi
else
  bad "no gallery link on /g (gallery and photo checks cannot run)"
fi

# ── 006: redirects and 404 rules ──────────────────────────────────────────────
if [ -n "$gid" ]; then
  s=$(get wrongslug "/g/$gid/wrong-slug-012"); loc=$(hdrv wrongslug Location)
  [ "$s" = 301 ] && { [ "$loc" = "$g" ] || [ "$loc" = "$B$g" ]; } && ok "wrong slug -> 301 $g" || bad "wrong slug -> $s ${loc:-none}"
  s=$(get idonly "/g/$gid"); cc=$(hdrv idonly Cache-Control)
  [ "$s" = 301 ] && [ "$cc" = "public, max-age=0, s-maxage=300" ] && ok "/g/{id} -> 301, Cache-Control $cc" || bad "/g/{id} -> $s, Cache-Control '${cc:-none}'"
else
  bad "wrong-slug and /g/{id} redirects not checked (no gallery id)"
fi
s=$(get malformed /g/abc); [ "$s" = 404 ] && ok "malformed id -> 404" || bad "malformed id -> $s"
s=$(get missing /g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none)
[ "$s" = 404 ] && [ "$(hdrv missing X-Robots-Tag)" = noindex ] && hasF missing 'Not found' \
  && ok "unknown id -> 404 page, noindex" || bad "unknown id -> $s, X-Robots-Tag '$(hdrv missing X-Robots-Tag)'"

# ── 011/010: breadcrumbs ──────────────────────────────────────────────────────
for pg in home listing gallery photo missing; do
  r=$(root "$pg" 2>/dev/null)
  [ "$r" = Home ] && ok "breadcrumb root on $pg is Home" || bad "breadcrumb root on $pg is '${r:-none}'"
done

# ── 006/010/011 public-page headers and markup ────────────────────────────────
for pg in home listing gallery photo; do
  cc=$(hdrv "$pg" Cache-Control)
  [ "$cc" = "public, max-age=0, s-maxage=86400" ] && ok "$pg Cache-Control $cc" || bad "$pg Cache-Control '${cc:-none}'"
  [ -f "$T/$pg.h" ] && grep -i '^server-timing:' "$T/$pg.h" | grep -q 'app;dur=' && ok "$pg Server-Timing app;dur" || bad "$pg has no Server-Timing app;dur"
  hasF "$pg" '<script type="speculationrules"' && ok "$pg has Speculation Rules" || bad "$pg has no Speculation Rules"
  hasF "$pg" '@view-transition' && ok "$pg inline CSS has @view-transition" || bad "$pg has no @view-transition"
  # no-script: exactly the two inline scripts (speculationrules + one module), no external script or stylesheet
  ns=$( [ -s "$T/$pg.html" ] && grep -o '<script' "$T/$pg.html" | wc -l | tr -d ' ' || echo none)
  [ "$ns" = 2 ] && hasnt "$pg" '<script[^>]* src=' && hasnt "$pg" '<link[^>]*rel="stylesheet"' \
    && ok "$pg scripts: 2 inline, no external script or stylesheet" || bad "$pg scripts: $ns <script, or an external script/stylesheet"
  if [ ! -s "$T/$pg.h" ]; then bad "$pg no response (cookie check)"
  elif hdr "$pg" Set-Cookie; then bad "$pg sets a cookie"
  else ok "$pg sets no cookie"; fi
done
for pg in gallery photo; do
  hasF "$pg" 'view-transition-name:img-' && ok "$pg names view transitions" || bad "$pg has no view-transition-name"
done
if [ -n "$p" ]; then
  iid=${p##*/}
  hasF photo "href=\"$g#p-$iid\"" && ok "photo links back to $g#p-$iid" || bad "photo has no link back to $g#p-$iid"
  hasF photo '<nav class="pager"' && ok "photo has the pager nav" || bad "photo has no pager nav"
  np=$(grep -oE "href=\"$g/[a-z2-7]{26}\"" "$T/gallery.html" | sort -u | wc -l | tr -d ' ')
  if [ "$np" -ge 2 ]; then
    hasF photo 'rel="next"' && ok "first photo of $np links rel=next" || bad "first photo of $np has no rel=next"
  else
    ok "gallery has $np photo, no next link expected"
  fi
else
  bad "photo nav links not checked (no photo)"
fi

# ── images: every <img>/<source> URL on the image host ───────────────────────
urls=$(cat "$T/listing.html" "$T/gallery.html" 2>/dev/null | grep -oE '<(img|source)[^>]*>' \
  | grep -oE '(src|srcset)="[^"]+"' | sed -E 's/^(src|srcset)="//; s/"$//' | grep -v '^data:' | tr ',' '\n' | awk '{print $1}' | grep . | sort -u)
nu=$(printf '%s\n' "$urls" | grep -c . || true)
off=$(printf '%s\n' "$urls" | grep . | grep -vc "^$IMG" || true)
[ "$nu" -gt 0 ] && [ "$off" = 0 ] && ok "images: $nu distinct URLs on listing+gallery, all start with $IMG" || bad "images: $off of $nu URLs not on $IMG"
cat "$T"/home.html "$T"/listing.html "$T"/gallery.html "$T"/photo.html 2>/dev/null | grep -qiE 'r2\.cloudflarestorage\.com|r2\.dev' \
  && bad "a public page names r2.cloudflarestorage.com or r2.dev" \
  || { [ "$nu" -gt 0 ] && ok "no public page names r2.cloudflarestorage.com or r2.dev" || bad "storage-endpoint scan had no pages"; }

# ── 012 SC-001 TLS and http -> https ──────────────────────────────────────────
if [ "$NOEDGE" = 1 ]; then
  skip "SC-001 TLS (no edge)"
  skip "http -> https redirect (no edge)"
else
  curl -sv --max-time 20 -o /dev/null "https://$HOSTONLY/" > "$T/tls.txt" 2>&1; rc=$?
  ! grep -qiE 'certificate problem|SSL: no alternative|certificate verify failed' "$T/tls.txt" && [ "$rc" = 0 ] \
    && ok "SC-001 TLS to $HOSTONLY: curl verified the chain and host name" || bad "SC-001 TLS to $HOSTONLY: curl exit $rc"
  # LibreSSL (macOS) has no -verify_hostname/-ext: verify the chain here, match the SAN by hand.
  echo | openssl s_client -connect "$HOSTONLY:443" -servername "$HOSTONLY" > "$T/sc.txt" 2>/dev/null
  san=$(openssl x509 -noout -text < "$T/sc.txt" 2>/dev/null | grep -A1 'Subject Alternative Name' | tail -1 | tr -d ' ')
  wild="*.${HOSTONLY#*.}"
  covered=0
  for nm in $(echo "$san" | tr ',' '\n' | sed -n 's/^DNS://p'); do
    { [ "$nm" = "$HOSTONLY" ] || [ "$nm" = "$wild" ]; } && covered=1
  done
  grep -q 'Verify return code: 0 (ok)' "$T/sc.txt" && [ "$covered" = 1 ] \
    && ok "SC-001 certificate chain verifies and SAN covers $HOSTONLY (${san:-?})" || bad "SC-001 certificate for $HOSTONLY: verify '$(grep -o 'Verify return code: [^)]*)' "$T/sc.txt")' SAN '${san:-none}'"
  r=$(curl -s --max-time 20 -o /dev/null -D - "http://$HOSTONLY/g" 2>/dev/null | tr -d '\r')
  rs=$(echo "$r" | head -1 | awk '{print $2}'); rl=$(echo "$r" | grep -i '^location:' | head -1 | awk '{print $2}')
  { [ "$rs" = 301 ] || [ "$rs" = 308 ]; } && [ "$rl" = "https://$HOSTONLY/g" ] && ok "http://$HOSTONLY/g -> $rs $rl" || bad "http://$HOSTONLY/g -> ${rs:-none} ${rl:-none}"
fi

# ── 012 SC-002 origin refusal: never sends the origin secret ──────────────────
n1=0; n2=0; seen1=""; seen2=""
for i in $(seq 1 20); do
  s=$("${CURL[@]}" -o /dev/null -w '%{http_code}' "$O/?sc002=$i" 2>/dev/null || true)
  [ "$s" = 403 ] && n1=$((n1+1)); seen1="$seen1 $s"
  s=$("${CURL[@]}" -o /dev/null -w '%{http_code}' -H "Host: $PUBHOST" "$O/?sc002h=$i" 2>/dev/null || true)
  [ "$s" = 403 ] && n2=$((n2+1)); seen2="$seen2 $s"
done
[ "$n1" = 20 ] && ok "SC-002 20/20 direct GETs to $O/ (no secret) -> 403" || bad "SC-002 $n1/20 direct GETs -> 403 (seen:$(echo $seen1 | tr ' ' '\n' | sort | uniq -c | tr '\n' ' '))"
[ "$n2" = 20 ] && ok "SC-002 20/20 GETs to $O/ with Host: $PUBHOST (no secret) -> 403" || bad "SC-002 $n2/20 Host-header GETs -> 403 (seen:$(echo $seen2 | tr ' ' '\n' | sort | uniq -c | tr '\n' ' '))"

# ── 012 SC-003 edge cache and owner bypass ────────────────────────────────────
if [ "$NOEDGE" = 1 ]; then
  skip "SC-003 cf-cache-status HIT on the second GET (no edge)"
  skip "owner bypass cf-cache-status (no edge)"
else
  ts=$(date -u +%Y%m%dT%H%M%SZ)
  echo "     SC-003 started $ts (use kql.sh cachehits --since <this time> for the R-09 cross-check)"
  for pair in "home /" "listing /g" "gallery ${g:-}" "photo ${p:-}"; do
    set -- $pair; nm=$1; pth=${2:-}
    [ -n "$pth" ] || { bad "SC-003 $nm: no page id from the listing"; continue; }
    u="$pth?sc003=$ts"
    st=$(get "c1-$nm" "$u"); get "c2-$nm" "$u" > /dev/null
    s1=$(hdrv "c1-$nm" cf-cache-status); s2=$(hdrv "c2-$nm" cf-cache-status)
    [ "$st" = 200 ] && [ "$s2" = HIT ] && ok "SC-003 $nm $u: 200 ${s1:-none} then HIT" || bad "SC-003 $nm $u: $st ${s1:-none} then ${s2:-none}"
  done
  get bypass "/?owner=$ts" -H 'Cookie: __Host-gallery=x' > /dev/null
  cs=$(hdrv bypass cf-cache-status)
  { [ "$cs" = BYPASS ] || [ "$cs" = DYNAMIC ]; } && ok "owner cookie GET / -> cf-cache-status $cs" || bad "owner cookie GET / -> cf-cache-status '${cs:-none}'"
fi

# ── SC-009 secret scan: real values, never printed ────────────────────────────
SEC=${SECRETS_FILE:-$ROOT/infra/.secrets.local}   # SECRETS_FILE: self-test override only
if [ ! -f "$SEC" ]; then
  skip "SC-009 secret scan (infra/.secrets.local not found)"
else
  get signin /admin/signin > /dev/null
  cat "$T"/*.html "$T"/*.h > "$T/served.txt" 2>/dev/null
  git -C "$ROOT" log -p --all > "$T/gitlog.txt" 2>/dev/null
  nk=0
  while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in ''|\#*) continue ;; esac
    k=${line%%=*}; v=${line#*=}
    [ "$k" = "$line" ] && continue
    k=$(printf '%s' "$k" | sed -E 's/^export[[:space:]]+//; s/[[:space:]]//g')
    v=$(printf '%s' "$v" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//; s/^"(.*)"$/\1/; s/^'"'"'(.*)'"'"'$/\1/')
    [ -z "$v" ] && continue
    [ "${#v}" -lt 8 ] && { skip "SC-009 $k: value shorter than 8 chars, not scanned (would match by chance)"; continue; }
    nk=$((nk+1))
    grep -qF -- "$v" "$T/served.txt" && bad "SC-009 $k: found in served pages/headers" || ok "SC-009 $k: not found in served pages/headers"
    grep -qF -- "$v" "$T/gitlog.txt" && bad "SC-009 $k: found in git log -p --all" || ok "SC-009 $k: not found in git log -p --all"
  done < "$SEC"
  unset line k v
  [ "$nk" -gt 0 ] || bad "SC-009 infra/.secrets.local has no KEY=VALUE lines"
  if [ -n "${ORIGIN_SECRET:-}" ]; then
    grep -qF -- "$ORIGIN_SECRET" "$T/served.txt" && bad "SC-009 ORIGIN_SECRET(env): found in served pages/headers" || ok "SC-009 ORIGIN_SECRET(env): not found in served pages/headers"
  fi
  rm -f "$T/served.txt" "$T/gitlog.txt"
fi

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
[ "$fails" -gt 125 ] && fails=125
exit "$fails"
