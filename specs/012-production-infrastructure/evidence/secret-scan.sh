#!/usr/bin/env bash
# 012 T029 (SC-009): look for the REAL production secret values in the branch history and in a
# Release publish output. Values are read inside this script from the owner's local files and
# never printed; each source prints only "found"/"not found" per secret and a count.
# Run from the repo root:  bash specs/012-production-infrastructure/evidence/secret-scan.sh | tee specs/012-production-infrastructure/evidence/secret-scan.txt
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
SOURCES=".r2-keys-prod-copy infra/.secrets.local"
VALS=$(mktemp); trap 'rm -f "$VALS" "$HIST"' EXIT
HIST=$(mktemp)

# Every "key = value" / "key: value" / bare token line; keep values of 20+ characters (shorter
# values are names or ids, not credentials). Quotes and surrounding spaces are stripped.
for f in $SOURCES; do
  if [ -f "$f" ]; then
    n=$(sed -E 's/^[^=:]*[=:][[:space:]]*//; s/^["'\'']//; s/["'\'']$//; s/[[:space:]]+$//' "$f" \
        | awk 'length($0) >= 20' | tee -a "$VALS" | wc -l | tr -d ' ')
    echo "source $f: present, $n candidate values"
  else
    echo "source $f: absent"
  fi
done
total=$(sort -u "$VALS" | grep -c . || true)
if [ "$total" -eq 0 ]; then echo "SKIP no secret values to scan for"; echo "RESULT: SKIP"; exit 0; fi

base=$(git merge-base HEAD main)
git log -p --all > "$HIST"
echo "history: git log -p --all ($(wc -l < "$HIST" | tr -d ' ') lines; branch base $base)"

OUT=/tmp/012-secretscan-publish
rm -rf "$OUT"
dotnet publish src/Gallery.Web -c Release -o "$OUT" --artifacts-path /tmp/012-secretscan-art >/dev/null 2>&1 \
  || { echo "FAIL publish did not build"; exit 1; }

found=0; i=0
while IFS= read -r v; do
  [ -n "$v" ] || continue
  i=$((i+1))
  h=$(grep -cF -- "$v" "$HIST" || true)
  p=$(grep -rlF -- "$v" "$OUT" 2>/dev/null | wc -l | tr -d ' ')
  if [ "$h" -gt 0 ] || [ "$p" -gt 0 ]; then
    echo "FOUND secret #$i (len ${#v}): history lines=$h publish files=$p"; found=$((found+1))
  else
    echo "not found secret #$i (len ${#v})"
  fi
done < <(sort -u "$VALS")
echo "RESULT: $found FOUND of $i"
exit $((found > 0))
