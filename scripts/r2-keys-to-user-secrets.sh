#!/usr/bin/env bash
# One-time: moves the R2 credentials from .r2-keys-copy into .NET user-secrets (research R-10).
# Never prints a value. The owner deletes .r2-keys-copy afterwards.
set -euo pipefail

cd "$(dirname "$0")/.."
FILE=.r2-keys-copy
PROJECT=src/Gallery.Web

if [ ! -f "$FILE" ]; then
  echo "missing $FILE" >&2
  exit 1
fi

# The value on the first non-blank line after the line containing the label.
value_after() {
  awk -v label="$1" '
    found && NF { gsub(/^[ \t]+|[ \t\r]+$/, ""); print; exit }
    index($0, label) { found = 1 }
  ' "$FILE"
}

KEY_ID="$(value_after "Access Key ID")"
SECRET="$(value_after "Secret Access Key")"
ENDPOINT="$(grep -oE 'https://[a-f0-9]{32}\.r2\.cloudflarestorage\.com' "$FILE" | head -n1 || true)"

missing=()
[ -n "$KEY_ID" ] || missing+=("Access Key ID")
[ -n "$SECRET" ] || missing+=("Secret Access Key")
[ -n "$ENDPOINT" ] || missing+=("endpoint")
if [ ${#missing[@]} -gt 0 ]; then
  echo "could not find in $FILE: ${missing[*]}" >&2
  exit 1
fi

dotnet user-secrets set --project "$PROJECT" "Storage:AccessKeyId" "$KEY_ID" >/dev/null
dotnet user-secrets set --project "$PROJECT" "Storage:SecretAccessKey" "$SECRET" >/dev/null
dotnet user-secrets set --project "$PROJECT" "Storage:ServiceUrl" "$ENDPOINT" >/dev/null

echo "set 3 secrets; delete .r2-keys-copy when done"
