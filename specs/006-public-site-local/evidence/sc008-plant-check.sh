#!/usr/bin/env bash
# Non-vacuity check for verify.sh's SC-008 committed-tree scan. Plants the real account id in a
# temporary file added to the index (never committed), confirms the scan's exact command finds it,
# then removes it. Prints only the outcome, never the value.
set -uo pipefail
cd "$(dirname "$0")/../../.."
ACCOUNT=$(dotnet user-secrets list --project src/Gallery.Web 2>/dev/null | sed -n 's/^Storage:ServiceUrl = //p' | grep -oE '[a-f0-9]{32}')
[ -n "$ACCOUNT" ] || { echo "no account id configured"; exit 1; }
F=specs/006-public-site-local/evidence/.sc008-plant.tmp
printf 'x %s x\n' "$ACCOUNT" > "$F"
git add -f "$F"
hits=$(git ls-files -z | xargs -0 grep -lF -e "$ACCOUNT" 2>/dev/null)
git rm -q --cached "$F"; rm -f "$F"
unset ACCOUNT
[ "$hits" = "$F" ] && echo "SC-008 scan non-vacuous: planted value found in exactly the planted file" || { echo "SC-008 scan did NOT find the planted value"; exit 1; }
