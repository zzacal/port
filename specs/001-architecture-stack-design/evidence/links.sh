#!/usr/bin/env bash
# Check every URL in section 11 of docs/design.md (contract C9; Tasks T004). Run from repo root.
#
# Classification:
#   2xx/3xx        OK
#   402/403/429    BOT-WALL: the server answered but refuses automated clients (GitHub rate limit,
#                  npm or Stack Overflow bot checks, metacpan). The page exists. This is reported
#                  as WARN and does not fail the run; links-output.txt records which URLs were
#                  affected.
#   anything else  FAIL (404/410/5xx/timeouts = dead or moved link)
# Some servers answer HEAD wrongly (NuGet returns 404 for HEAD and 200 for GET), so any non-2xx/3xx
# HEAD result is retried once with GET before it is classified.
set -uo pipefail
DOC="docs/design.md"
UA='Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36'
urls=$(awk '/^## 11\. /{on=1; next} on && /^## /{exit} on{print}' "$DOC" \
  | grep -oE 'https?://[^ )>`"]+' | sed -E 's/[.,;]+$//' | LC_ALL=C sort -u)
bad=0; warn=0; total=0
for u in $urls; do
  total=$((total+1))
  code=$(curl -sIL -o /dev/null -w '%{http_code}' -A "$UA" --max-time 20 "$u" || echo 000)
  if ! { [ "$code" -ge 200 ] && [ "$code" -le 399 ]; } 2>/dev/null; then
    code=$(curl -sL -X GET -o /dev/null -w '%{http_code}' -A "$UA" --max-time 20 "$u" || echo 000)
  fi
  case "$code" in
    2??|3??) echo "OK   $code $u" ;;
    402|403|429) echo "WARN $code $u (bot wall / rate limit)"; warn=$((warn+1)) ;;
    *) echo "FAIL $code $u"; bad=$((bad+1)) ;;
  esac
done
echo "---"
echo "checked: $total  failing: $bad  bot-walled: $warn"
[ "$total" -gt 0 ] && [ "$bad" = 0 ]
