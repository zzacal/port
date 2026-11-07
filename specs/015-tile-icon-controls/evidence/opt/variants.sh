#!/usr/bin/env bash
# 015 optimisation research: which part of the icon row costs the server time?
# Builds each variant in its own worktree, in Debug (what the evidence tests measured) and Release
# (what production runs), and runs evidence/opt/ZzOptProbe.cs.txt: 300 GETs each of the library and
# the builder (100 photos, "Coast" holds 40), after 20 warm-up GETs. Rounds alternate the variants,
# so machine drift hits all of them alike.
#   bash specs/015-tile-icon-controls/evidence/opt/variants.sh   (results: evidence/opt/results.txt)
#
# Variants (only the probe and these edits differ from the named commit):
#   pre          b2d261f: src before 015
#   post         12a812c: 015 as merged (Icon component, inline SVG)
#   none         post, Icon renders nothing: the icon cost's upper bound (the rest of the row remains)
#   cached       post, Icon writes one cached, prebuilt <svg> string (same bytes, ~1 frame not ~13)
#   flat         post, IconButton writes the cached <svg> string itself: no Icon component at all
#   sprite       post, Icon writes <svg><use href="#i-name"/></svg>; AdminLayout carries one sprite
#                (fewer bytes, same components)
#   flat-sprite  flat + sprite: no Icon component and <use> instead of the paths
set -uo pipefail
ROOT=$(git rev-parse --show-toplevel)
O=$ROOT/specs/015-tile-icon-controls/evidence/opt
TMP=$(cd /tmp && pwd -P)/015-opt
ROUNDS=${ROUNDS:-3}
VARIANTS=${VARIANTS:-"pre post none cached flat sprite flat-sprite"}
CONFIGS=${CONFIGS:-"Debug Release"}
RES=$O/results.txt
mkdir -p "$TMP"

prepare() {  # $1 variant
  local v=$1 wt=$TMP/$1 sha=12a812c
  [ "$v" = pre ] && sha=b2d261f
  rm -rf "$wt"; git -C "$ROOT" worktree prune
  git -C "$ROOT" worktree add -q --detach "$wt" "$sha" || return 1
  cp "$O/ZzOptProbe.cs.txt" "$wt/tests/Gallery.Web.Tests/ZzOptProbe.cs"
  [ "$v" = pre ] || [ "$v" = post ] || (cd "$wt" && uv run --no-project python "$O/apply-variant.py" "$v") || return 1
  for c in $CONFIGS; do
    (cd "$wt" && dotnet build Gallery.slnx -c "$c" --artifacts-path "$wt/artifacts" -nologo -v q >"$TMP/$v-$c-build.txt" 2>&1) \
      || { echo "BUILD FAIL $v $c"; grep -m3 ' error ' "$TMP/$v-$c-build.txt"; return 1; }
  done
}

probe() {  # $1 variant $2 config $3 round
  local wt=$TMP/$1 out=$TMP/$1-$2-$3.txt
  (cd "$wt" && OPT_OUT="$out" dotnet test tests/Gallery.Web.Tests -c "$2" --no-build --artifacts-path "$wt/artifacts" \
      --blame-hang-timeout 120s --filter FullyQualifiedName~ZzOptProbe >"$TMP/$1-$2-$3-test.txt" 2>&1) \
    || { echo "PROBE FAIL $1 $2 $3"; grep -m3 -A3 'Error Message' "$TMP/$1-$2-$3-test.txt"; return; }
  sed "s/^/$1 $2 r$3 /" "$out"
}

{
  echo "# 015 opt research $(date -u +%FT%TZ) on $(sysctl -n machdep.cpu.brand_string 2>/dev/null)"
  echo "# variant config round page n p50 p95 mean raw gzip (ms; bytes)"
} > "$RES"
for v in $VARIANTS; do prepare "$v" || exit 1; done
for r in $(seq 1 "$ROUNDS"); do
  for c in $CONFIGS; do
    for v in $VARIANTS; do probe "$v" "$c" "$r" | tee -a "$RES"; done
  done
done
for v in $VARIANTS; do git -C "$ROOT" worktree remove --force "$TMP/$v"; done
uv run --no-project python "$O/summarise.py" "$RES" | tee "$O/summary.txt"
