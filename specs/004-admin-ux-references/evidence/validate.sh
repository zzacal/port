#!/usr/bin/env bash
# Contract C1-C10 for docs/ux-research/admin-moodboard.html
# (specs/004-admin-ux-references/contracts/board-contract.md).
# PG/CP ids and states are read from data-model.md, not hard-coded.
export LC_ALL=C
ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
BOARD="$ROOT/docs/ux-research/admin-moodboard.html"
DM="$ROOT/specs/004-admin-ux-references/data-model.md"
BDIR=$(dirname "$BOARD")
fails=0
pass() { echo "PASS $1"; }
fail() { echo "FAIL $1: $2"; fails=$((fails+1)); }

PG_IDS=$(grep -oE '^\| PG-[a-z]+' "$DM" | sed 's/^| //')
# "CP-id|state state state"
CP_ROWS=$(grep -E '^\| CP-[a-z]+ ' "$DM" | awk -F'|' '{gsub(/ /,"",$2); s=$4; gsub(/,/," ",s); gsub(/^ +| +$/,"",s); print $2"|"s}')

# one figure per line: flatten board, split on <figure
FIGS=$(tr '\n' ' ' < "$BOARD" | sed 's/<figure /\n<figure /g; s/<\/figure>/<\/figure>\n/g' | grep '^<figure ')
COVERS=$(printf '%s\n' "$FIGS" | grep -oE 'data-covers="[^"]*"' | sed 's/data-covers="//; s/"$//')
GAPS=$(grep -oE 'class="gap"[^>]*data-covers="[^"]*"|data-covers="[^"]*"[^>]*class="gap"' "$BOARD" | grep -oE 'data-covers="[^"]*"' | sed 's/data-covers="//; s/"$//')

# C1 section order
order=$(grep -oE '<section id="[a-z-]+"' "$BOARD" | sed 's/<section id="//; s/"//' | tr '\n' ' ')
[ "$order" = "pages components set-aside patterns directions " ] && pass C1 || fail C1 "order is '$order'"

# C2 each PG on >=3 figures
bad=""
for p in $PG_IDS; do
  n=$(printf '%s\n' "$COVERS" | tr ' ' '\n' | grep -cx "$p")
  [ "$n" -ge 3 ] || bad="$bad $p=$n"
done
[ -z "$bad" ] && pass C2 || fail C2 "under 3 figures:$bad"

# C3 each CP on >=2 figures (CP-x or CP-x:state)
bad=""
for row in $(printf '%s\n' "$CP_ROWS" | cut -d'|' -f1); do
  n=$(printf '%s\n' "$COVERS" | grep -cE "(^| )$row(:| |$)")
  [ "$n" -ge 2 ] || bad="$bad $row=$n"
done
[ -z "$bad" ] && pass C3 || fail C3 "under 2 figures:$bad"

# C4 every CP:state on a figure or a gap
bad=""
ALL=$(printf '%s\n%s\n' "$COVERS" "$GAPS" | tr ' ' '\n')
while IFS='|' read -r cp states; do
  for s in $states; do
    printf '%s\n' "$ALL" | grep -qx "$cp:$s" || bad="$bad $cp:$s"
  done
done <<< "$CP_ROWS"
[ -z "$bad" ] && pass C4 || fail C4 "missing:$bad"

# C5 >=8 distinct products
n=$(printf '%s\n' "$FIGS" | grep -oE 'data-product="[^"]+"' | sort -u | grep -c .)
[ "$n" -ge 8 ] && pass C5 || fail C5 "$n distinct products"

# C6 img files exist; every figure has a source link and Taken date
bad=""
for src in $(printf '%s\n' "$FIGS" | grep -oE '<img [^>]*src="[^"]+"' | sed 's/.*src="//; s/"$//'); do
  [ -f "$BDIR/$src" ] || bad="$bad missing:$src"
done
nf=$(printf '%s\n' "$FIGS" | grep -c '^<figure ')
nl=$(printf '%s\n' "$FIGS" | grep -E '<a [^>]*href="https?://' | grep -c 'Taken: [0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}')
[ "$nf" -gt 0 ] || bad="$bad no-figures"
[ "$nf" -eq "$nl" ] || bad="$bad figures=$nf with-link-and-date=$nl"
[ -z "$bad" ] && pass C6 || fail C6 "$bad"

# C7 every figcaption has Fit:
nc=$(printf '%s\n' "$FIGS" | grep -c '<figcaption')
nfit=$(printf '%s\n' "$FIGS" | grep '<figcaption' | grep -c 'Fit:')
[ "$nc" -gt 0 ] && [ "$nc" -eq "$nfit" ] && [ "$nc" -eq "$nf" ] && pass C7 || fail C7 "figures=$nf captions=$nc with-Fit=$nfit"

# C8 2-3 directions, each with all PG data-page items, exactly one recommended
ARTS=$(tr '\n' ' ' < "$BOARD" | sed 's/<article /\n<article /g; s/<\/article>/<\/article>\n/g' | grep '^<article [^>]*class="direction')
na=$(printf '%s\n' "$ARTS" | grep -c .)
nr=$(printf '%s\n' "$ARTS" | grep -c 'class="direction recommended"')
bad=""
[ "$na" -ge 2 ] && [ "$na" -le 3 ] || bad="$bad directions=$na"
[ "$nr" -eq 1 ] || bad="$bad recommended=$nr"
i=0
while IFS= read -r art; do
  [ -z "$art" ] && continue; i=$((i+1))
  for p in $PG_IDS; do
    printf '%s' "$art" | grep -q "data-page=\"$p\"" || bad="$bad dir$i-missing-$p"
  done
done <<< "$ARTS"
[ -z "$bad" ] && pass C8 || fail C8 "$bad"

# C9 self-contained
bad=""
grep -qi '<script' "$BOARD" && bad="$bad script"
grep -qi '<link[^>]*stylesheet' "$BOARD" && bad="$bad stylesheet"
grep -qiE '<img [^>]*src="https?:' "$BOARD" && bad="$bad remote-img"
[ -z "$bad" ] && pass C9 || fail C9 "$bad"

# C10 set-aside lists >=1 product row
sa=$(tr '\n' ' ' < "$BOARD" | sed 's/.*<section id="set-aside">//; s/<\/section>.*//')
rows=$(printf '%s' "$sa" | grep -o '<tr><td' | grep -c .)
[ "$rows" -ge 1 ] && pass C10 || fail C10 "$rows rows"

if [ "$fails" -eq 0 ]; then echo "ALL PASS"; else echo "FAILED $fails"; exit 1; fi
