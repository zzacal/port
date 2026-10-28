#!/usr/bin/env bash
# Check every URL in section 11 of docs/design.md (contract C9; Tasks T004). Run from repo root.
set -uo pipefail
DOC="docs/design.md"
UA='Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36'
urls=$(awk '/^## 11\. /{on=1; next} on && /^## /{exit} on{print}' "$DOC" \
  | grep -oE 'https?://[^ )>`"]+' | sed -E 's/[.,;]+$//' | LC_ALL=C sort -u)
bad=0; total=0
for u in $urls; do
  total=$((total+1))
  code=$(curl -sIL -o /dev/null -w '%{http_code}' -A "$UA" --max-time 20 "$u" || echo 000)
  if [ "$code" = 403 ] || [ "$code" = 405 ] || [ "$code" = 000 ]; then
    code=$(curl -sL -X GET -o /dev/null -w '%{http_code}' -A "$UA" --max-time 20 "$u" || echo 000)
  fi
  echo "$code $u"
  { [ "$code" -ge 200 ] && [ "$code" -le 399 ]; } 2>/dev/null || bad=$((bad+1))
done
echo "---"
echo "checked: $total  failing: $bad"
[ "$total" -gt 0 ] && [ "$bad" = 0 ]
