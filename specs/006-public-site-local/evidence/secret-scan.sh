#!/usr/bin/env bash
# SC-008 committed-tree scan alone (the same check verify.sh runs). Prints file names only.
set -uo pipefail
cd "$(dirname "$0")/../../.."
s=$(dotnet user-secrets list --project src/Gallery.Web 2>/dev/null)
KEY=$(echo "$s" | sed -n 's/^Storage:AccessKeyId = //p')
SECRET=$(echo "$s" | sed -n 's/^Storage:SecretAccessKey = //p')
ACCOUNT=$(echo "$s" | sed -n 's/^Storage:ServiceUrl = //p' | grep -oE '[a-f0-9]{32}')
unset s
[ -n "$KEY" ] && [ -n "$SECRET" ] && [ -n "$ACCOUNT" ] || { echo "FAIL could not load configured values"; exit 1; }
hits=$(git ls-files -z | xargs -0 grep -lF -e "$KEY" -e "$SECRET" -e "$ACCOUNT" 2>/dev/null)
unset KEY SECRET ACCOUNT
[ -z "$hits" ] && echo "PASS SC-008 $(git ls-files | wc -l | tr -d ' ') committed files: no configured secret value" || { echo "FAIL SC-008 in: $hits"; exit 1; }
