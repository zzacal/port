#!/usr/bin/env bash
# 012 T017 (FR-019, contracts/evidence.md "kql.sh"): run the hosted-log evidence queries.
# Read-only: only `az monitor app-insights query`, signed in as the owner (`az login` first).
#
#   bash specs/012-production-infrastructure/evidence/kql.sh <name|all> [--since <ISO time>]
#
# <name>  one of: claim edge reads p95 signin cachehits (file kql/<name>.kql next to this script),
#         or `all` for every one in that order.
# --since start of the window, ISO 8601 (e.g. 2026-11-04T06:42:52Z); the window ends now
#         (az --start-time/--end-time). Without it the window is the last KQL_OFFSET (default 1d).
# Each query's result is printed as TSV (header row first) and appended to kql-output.txt next to
# this script, after a header line "== <name> run=<now> window=<...>".
# Exit status: number of queries that failed to run (a query that ran but reports FAIL rows still
# exits 0; read the verdict columns).
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
OUT="$HERE/kql-output.txt"
NAMES="claim edge reads p95 signin cachehits"
APP=portfolio-appinsights
RG=portfolio
SUB=c28725a0-a61d-4a6c-999a-0a8980111fbf

usage() { echo "usage: $0 <$(echo $NAMES | tr ' ' '|')|all> [--since <ISO time>]" >&2; exit 2; }
[ $# -ge 1 ] || usage
target=$1; shift
since=""
while [ $# -gt 0 ]; do
  case "$1" in
    --since) [ $# -ge 2 ] || usage; since=$2; shift 2 ;;
    --since=*) since=${1#--since=}; shift ;;
    *) usage ;;
  esac
done
if [ "$target" = all ]; then list=$NAMES
else
  case " $NAMES " in *" $target "*) list=$target ;; *) echo "unknown query '$target'" >&2; usage ;; esac
fi
command -v az >/dev/null || { echo "FAIL az CLI not found" >&2; exit 1; }
command -v jq >/dev/null || { echo "FAIL jq not found" >&2; exit 1; }

now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
if [ -n "$since" ]; then
  range=(--start-time "$since" --end-time "$now"); window="$since..$now"
else
  range=(--offset "${KQL_OFFSET:-1d}"); window="last ${KQL_OFFSET:-1d}"
fi

fails=0
for name in $list; do
  f="$HERE/kql/$name.kql"
  hdr="== $name run=$now window=$window file=kql/$name.kql"
  echo "$hdr" | tee -a "$OUT"
  if [ ! -f "$f" ]; then echo "FAIL missing $f" | tee -a "$OUT"; fails=$((fails+1)); continue; fi
  if json=$(az monitor app-insights query --app "$APP" -g "$RG" --subscription "$SUB" \
              --analytics-query "@$f" "${range[@]}" -o json 2>"$HERE/.kql-err.$$"); then
    printf '%s' "$json" | jq -r '.tables[0] | ((.columns | map(.name)), (.rows[] | map(if type == "object" or type == "array" then tojson else . end))) | @tsv' \
      | tee -a "$OUT"
    rows=$(printf '%s' "$json" | jq '.tables[0].rows | length')
    echo "-- $rows row(s)" | tee -a "$OUT"
  else
    echo "FAIL query did not run: $(head -3 "$HERE/.kql-err.$$" | tr '\n' ' ')" | tee -a "$OUT"
    fails=$((fails+1))
  fi
  rm -f "$HERE/.kql-err.$$"
  echo | tee -a "$OUT"
done
exit "$fails"
