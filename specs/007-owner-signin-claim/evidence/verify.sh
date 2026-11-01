#!/usr/bin/env bash
# Live verification for 007-owner-signin-claim. Run from the repo root:
#   bash specs/007-owner-signin-claim/evidence/verify.sh | tee specs/007-owner-signin-claim/evidence/verify-output.txt
# Needs: Docker (gallery-mongo), the owner's user-secrets (Authentication:Google:*), trusted dev cert.
# Never prints a secret: values are read into variables and compared; only file names are printed,
# and every Location header is printed with its query values redacted.
# The owner may keep `dotnet watch run` on :5080/:5081; this script builds into ./artifacts, runs its
# own instance on :5090 (http) + :5091 (https), and stops only the PID it started.
set -uo pipefail
export DOTNET_NOLOGO=1
EV="specs/007-owner-signin-claim/evidence"
ART="artifacts"
HTTP="http://localhost:5090"
HTTPS="https://localhost:5091"
fails=0
pass() { echo "PASS $1 $2"; }
fail() { echo "FAIL $1 $2"; fails=$((fails+1)); }
APP_PID=""
TMP=$(mktemp -d)
stop_app() { [ -n "$APP_PID" ] && { pkill -P "$APP_PID" 2>/dev/null; kill "$APP_PID" 2>/dev/null; wait "$APP_PID" 2>/dev/null; }; APP_PID=""; }
trap 'stop_app; rm -rf "$TMP"' EXIT
wait_up() { for _ in $(seq 1 60); do curl -s -o /dev/null "$1" && return 0; sleep 0.5; done; return 1; }
hdr() { grep -i "^$1:" "$2" | head -1 | cut -d' ' -f2- | tr -d '\r'; }
status() { head -1 "$1" | awk '{print $2}'; }

# 1. gates
out=$(dotnet build Gallery.slnx -warnaserror --artifacts-path "$ART" 2>&1)
echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
  && pass gate "build: 0 warnings, 0 errors" || fail gate "build"
out=$(dotnet test Gallery.slnx --no-build --artifacts-path "$ART" 2>&1)
[ "$(echo "$out" | grep -c '^Passed!')" = 2 ] && ! echo "$out" | grep -q '^Failed!' \
  && pass gate "tests: $(echo "$out" | grep -oE 'Passed: +[0-9]+' | awk '{s+=$2} END{print s}') passed in 2 assemblies" \
  || fail gate "tests: $(echo "$out" | grep -E 'Passed!|Failed!' | tr '\n' ' ')"
bash scripts/dev-mongo.sh >/dev/null || { fail start "gallery-mongo not available"; exit 1; }

# The dev certificate, exported for curl (macOS curl does not read the login keychain).
dotnet dev-certs https --export-path "$TMP/dev.pem" --format PEM --no-password >/dev/null 2>&1
C=(curl -s --cacert "$TMP/dev.pem")

# 2. live server (Development: user-secrets loaded)
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gallery.Web --no-build --no-launch-profile \
  --artifacts-path "$ART" --urls "$HTTP;$HTTPS" > "$EV/verify-run.log" 2>&1 &
APP_PID=$!
wait_up "$HTTP/" || { fail start "app did not start on :5090"; exit 1; }

# FR-018 / SC-007: public page sets no cookie
"${C[@]}" -D "$TMP/h" -o /dev/null "$HTTP/"
[ "$(status "$TMP/h")" = 200 ] && ! grep -qi '^set-cookie:' "$TMP/h" \
  && pass FR-018 "/ 200, no Set-Cookie" || fail FR-018 "/ status=$(status "$TMP/h") set-cookie=$(grep -ci '^set-cookie:' "$TMP/h")"

# H4 / FR-017: /admin without session → 302 /admin/signin, no-store
"${C[@]}" -D "$TMP/h" -o /dev/null "$HTTPS/admin"
[ "$(status "$TMP/h")" = 302 ] && [ "$(hdr location "$TMP/h")" = "/admin/signin" ] \
  && hdr cache-control "$TMP/h" | grep -q 'no-store' && hdr x-robots-tag "$TMP/h" | grep -q noindex \
  && pass H4 "/admin 302 /admin/signin, no-store, noindex" \
  || fail H4 "/admin status=$(status "$TMP/h") location=$(hdr location "$TMP/h") cc=$(hdr cache-control "$TMP/h")"

# R-09: /admin over plain http → redirect to https
"${C[@]}" -D "$TMP/h" -o /dev/null "$HTTP/admin"
s=$(status "$TMP/h"); loc=$(hdr location "$TMP/h")
[[ "$s" =~ ^30[78]$ ]] && [[ "$loc" == https://* ]] \
  && pass R-09 "http /admin $s to https" || fail R-09 "http /admin status=$s location=$loc"

# H7 / FR-021: /ui/ without session → 302 /admin/signin
"${C[@]}" -D "$TMP/h" -o /dev/null "$HTTPS/ui/"
[ "$(status "$TMP/h")" = 302 ] && [ "$(hdr location "$TMP/h")" = "/admin/signin" ] \
  && pass H7 "/ui/ 302 /admin/signin" || fail H7 "/ui/ status=$(status "$TMP/h") location=$(hdr location "$TMP/h")"

# H1 + H2: sign-in page, then POST with the token → Google authorize URL
"${C[@]}" -c "$TMP/jar" -D "$TMP/h" -o "$TMP/signin.html" "$HTTPS/admin/signin"
tok=$(grep -oE 'name="__RequestVerificationToken" value="[^"]+"' "$TMP/signin.html" | sed -E 's/.*value="([^"]+)"/\1/' | head -1)
[ "$(status "$TMP/h")" = 200 ] && [ -n "$tok" ] && grep -qE 'Claim this site|Sign in' "$TMP/signin.html" \
  && pass H1 "/admin/signin 200 with anti-forgery token" || fail H1 "/admin/signin status=$(status "$TMP/h") token=${tok:+present}"
"${C[@]}" -b "$TMP/jar" -D "$TMP/h" -o /dev/null --data-urlencode "__RequestVerificationToken=$tok" "$HTTPS/admin/signin"
loc=$(hdr location "$TMP/h"); unset tok
host=$(echo "$loc" | sed -E 's#^https://([^/]+)/.*#\1#')
[ "$(status "$TMP/h")" = 302 ] && [ "$host" = accounts.google.com ] \
  && echo "$loc" | grep -q 'prompt=select_account' && echo "$loc" | grep -qE '[?&]scope=openid(&|$)' \
  && pass H2 "POST /admin/signin 302 accounts.google.com, prompt=select_account, scope=openid" \
  || fail H2 "status=$(status "$TMP/h") location=$(echo "$loc" | sed -E 's/=[^&]*/=<v>/g')"

# H2 without token → 400
"${C[@]}" -b "$TMP/jar" -D "$TMP/h" -o /dev/null -X POST "$HTTPS/admin/signin"
[ "$(status "$TMP/h")" = 400 ] && pass H2 "POST /admin/signin without token 400" || fail H2 "no-token status=$(status "$TMP/h")"

stop_app

# 3. committed-tree secret scan (values, not shapes)
s=$(dotnet user-secrets list --project src/Gallery.Web 2>/dev/null)
GSECRET=$(echo "$s" | sed -n 's/^Authentication:Google:ClientSecret = //p')
unset s
if [ -z "$GSECRET" ]; then
  fail secrets "could not load Authentication:Google:ClientSecret"
else
  hits=$(git ls-files -z | xargs -0 grep -lF -e "$GSECRET" 2>/dev/null)
  [ -z "$hits" ] && pass secrets "$(git ls-files | wc -l | tr -d ' ') committed files: Google client secret absent" \
    || fail secrets "client secret found in: $hits"
fi
unset GSECRET

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
