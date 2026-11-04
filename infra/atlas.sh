#!/usr/bin/env bash
# 012: creates the production MongoDB Atlas resources with the official Atlas CLI (decisions row 19).
# Usage: bash infra/atlas.sh [--dry-run] <outbound-ip> [<outbound-ip> ...]
# Installs its own tools on macOS (Homebrew: mongodb-atlas-cli, jq) and runs `atlas auth login`
# (browser) when not signed in. Needs Homebrew itself.
# Idempotent: each step lists before it creates and skips what exists.
set -euo pipefail

PROJECT=gallery-prod
CLUSTER=gallery-prod
TIER=M0
PROVIDER=AZURE
REGION=US_WEST
DB=gallery
DB_USER=gallery-app
ROLE="readWrite@${DB}"

DRY=0
if [ "${1:-}" = "--dry-run" ]; then DRY=1; shift; fi
if [ "$#" -lt 1 ]; then
  echo "usage: bash infra/atlas.sh [--dry-run] <outbound-ip> [<outbound-ip> ...]" >&2
  exit 2
fi
IPS=()
# Accept the portal's comma-separated list as pasted ("1.2.3.4, 5.6.7.8") or space-separated.
for a in "$@"; do
  for ip in $(printf '%s' "$a" | tr ',' ' '); do IPS+=("$ip"); done
done
for ip in "${IPS[@]}"; do
  [[ "$ip" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || { echo "not an IPv4 address: '$ip'" >&2; exit 2; }
done

# run CMD...: prints the command; runs it unless --dry-run.
run() {
  echo "+ $*"
  if [ "$DRY" -eq 0 ]; then "$@"; fi
}
# read CMD...: a list/describe call whose JSON output is parsed; in --dry-run prints it and yields '{}'.
read_json() {
  if [ "$DRY" -eq 1 ]; then echo "+ $*" >&2; echo '{}'; else "$@"; fi
}

# need CMD BREW_FORMULA: install the tool with Homebrew when it is missing (not in --dry-run).
need() {
  command -v "$1" >/dev/null && return 0
  if [ "$DRY" -eq 1 ]; then echo "+ brew install $2"; return 0; fi
  command -v brew >/dev/null || { echo "$1 not found and Homebrew is missing: install it from https://brew.sh, then re-run" >&2; exit 2; }
  echo "== installing $2"
  brew install "$2"
}
need jq jq
need atlas mongodb-atlas-cli
need openssl openssl

echo "== signed in"
if [ "$DRY" -eq 1 ]; then
  echo "+ atlas auth whoami || atlas auth login"
elif ! atlas auth whoami >/dev/null 2>&1; then
  echo "not signed in to Atlas: opening the browser sign-in"
  atlas auth login
fi
run atlas auth whoami

echo "== project ${PROJECT}"
PROJECT_ID=$(read_json atlas projects list --output json \
  | jq -r --arg n "$PROJECT" '[(.results // [])[] | select(.name == $n) | .id][0] // empty')
if [ -n "$PROJECT_ID" ]; then
  echo "exists: project ${PROJECT} id=${PROJECT_ID}"
else
  # projects create needs the organisation: use the only one, or ATLAS_ORG_ID when there are several.
  if [ "$DRY" -eq 1 ]; then
    ORG_ID="<orgId>"; echo "+ atlas organizations list --output json"
  elif [ -n "${ATLAS_ORG_ID:-}" ]; then
    ORG_ID="$ATLAS_ORG_ID"
  else
    ORGS=$(atlas organizations list --output json | jq -r '(.results // [])[] | "\(.id) \(.name)"')
    if [ "$(printf '%s\n' "$ORGS" | grep -c .)" -ne 1 ]; then
      echo "found $(printf '%s\n' "$ORGS" | grep -c .) Atlas organisations; re-run with ATLAS_ORG_ID=<id> set:" >&2
      printf '%s\n' "$ORGS" >&2
      exit 2
    fi
    ORG_ID=${ORGS%% *}
    echo "organisation: ${ORGS#* } (${ORG_ID})"
  fi
  run atlas projects create "$PROJECT" --orgId "$ORG_ID"
  if [ "$DRY" -eq 1 ]; then
    PROJECT_ID="<projectId>"
  else
    PROJECT_ID=$(atlas projects list --output json \
      | jq -r --arg n "$PROJECT" '[(.results // [])[] | select(.name == $n) | .id][0] // empty')
    [ -n "$PROJECT_ID" ] || { echo "project ${PROJECT} not found after create" >&2; exit 1; }
    echo "created: project ${PROJECT} id=${PROJECT_ID}"
  fi
fi

echo "== cluster ${CLUSTER}"
HAVE_CLUSTER=$(read_json atlas clusters list --projectId "$PROJECT_ID" --output json \
  | jq -r --arg n "$CLUSTER" '[(.results // [])[] | select(.name == $n)] | length')
if [ "$HAVE_CLUSTER" != "0" ]; then
  echo "exists: cluster ${CLUSTER}"
else
  run atlas clusters create "$CLUSTER" --projectId "$PROJECT_ID" \
    --provider "$PROVIDER" --region "$REGION" --tier "$TIER"
fi
run atlas clusters watch "$CLUSTER" --projectId "$PROJECT_ID"

echo "== database user ${DB_USER}"
HAVE_USER=$(read_json atlas dbusers list --projectId "$PROJECT_ID" --output json \
  | jq -r --arg u "$DB_USER" '[((.results // .) | if type == "array" then .[] else empty end) | select(.username == $u)] | length')
PASSWORD=""
if [ "$HAVE_USER" != "0" ]; then
  echo "exists: user ${DB_USER} (password unchanged, not shown)"
else
  if [ "$DRY" -eq 1 ]; then
    PASSWORD="<generated-by-openssl-rand-base64-24-in-real-run>"
    echo "+ openssl rand -base64 24"
  else
    PASSWORD=$(openssl rand -base64 24)
  fi
  # The password is passed as an argument, never echoed: print the command with it masked.
  echo "+ atlas dbusers create --username ${DB_USER} --password <hidden> --role ${ROLE} --projectId ${PROJECT_ID}"
  if [ "$DRY" -eq 0 ]; then
    atlas dbusers create --username "$DB_USER" --password "$PASSWORD" --role "$ROLE" --projectId "$PROJECT_ID"
  fi
fi

echo "== network access"
EXISTING=$(read_json atlas accessLists list --projectId "$PROJECT_ID" --output json \
  | jq -r '(.results // [])[] | (.ipAddress // empty), (.cidrBlock // empty)')
for ip in "${IPS[@]}"; do
  if printf '%s\n' "$EXISTING" | grep -qxF -e "$ip" -e "${ip}/32"; then
    echo "exists: access ${ip}"
  else
    run atlas accessLists create "$ip" --type ipAddress --projectId "$PROJECT_ID" --comment "portfolio-web outbound"
  fi
done

echo "== connection"
if [ "$DRY" -eq 1 ]; then
  SRV_HOST="<srv-host>"
  echo "+ atlas clusters connectionStrings describe ${CLUSTER} --projectId ${PROJECT_ID} --output json"
else
  SRV_HOST=$(atlas clusters connectionStrings describe "$CLUSTER" --projectId "$PROJECT_ID" --output json \
    | jq -r '.standardSrv // empty' | sed 's#^mongodb+srv://##; s#/.*$##')
fi
echo "SRV host: ${SRV_HOST}"

if [ -n "$PASSWORD" ]; then
  # URI userinfo must percent-encode + / = (base64 alphabet).
  ENC=$(printf '%s' "$PASSWORD" | sed 's/%/%25/g; s/+/%2B/g; s#/#%2F#g; s/=/%3D/g')
  echo
  echo "paste into Mongo__ConnectionString (shown once):"
  echo "mongodb+srv://${DB_USER}:${ENC}@${SRV_HOST}/?retryWrites=true&w=majority&appName=${CLUSTER}"
fi
