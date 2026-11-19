#!/usr/bin/env bash
# 018 T021 production probe (signed out, read-only): the local sign-in does not exist on port.zacal.me.
H=${1:-https://port.zacal.me}
echo "018 production probe against $H at $(date -u +%FT%TZ)"
fail=0
p=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$H/admin/signin/local")
u=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$H/admin/signin/nonexistent")
echo "POST /admin/signin/local -> $p (unmapped /admin/signin/nonexistent -> $u)"
[ "$p" = 401 ] && [ "$p" = "$u" ] || fail=1
g=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' "$H/admin/signin/local")
echo "GET /admin/signin/local -> $g"
s=$(curl -s "$H/admin/signin")
# The element, not the bare word: the page inlines SignInCard's scoped CSS, which names .si-local.
n=$(printf '%s' "$s" | grep -c '<form class="si-local"')
c=$(printf '%s' "$s" | grep -c '\.si-local\[b-')
a=$(printf '%s' "$s" | grep -c 'action="/admin/signin/local"')
gf=$(printf '%s' "$s" | grep -c 'action="/admin/signin"')
echo "/admin/signin: si-local form elements=$n, local action lines=$a, Google form lines=$gf (inlined .si-local CSS rule lines=$c)"
[ "$n" = 0 ] && [ "$a" = 0 ] && [ "$gf" -ge 1 ] || fail=1
[ $fail = 0 ] && echo "PASS" || echo "FAIL"
exit $fail
