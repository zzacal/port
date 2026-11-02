#!/usr/bin/env bash
# Self-test for snapshot-diff.sh (011 T021). Run from the repo root:
#   bash specs/011-home-page/evidence/snapshot-diff-selftest.sh | tee specs/011-home-page/evidence/snapshot-diff-selftest.txt
#  1. every regenerated page DIFFERS from its frozen copy before stripping (the strips do real work)
#  2. a temp copy of the regenerated pages (intended edits only) reports ALL EQUAL
#  3. plus a stray edit in tokens.html and one in site-header.html (a page that has strips of its
#     own) reports exactly 2 RESIDUE
set -uo pipefail
E=specs/011-home-page/evidence
OLD=$E/pre-011-specimens
T=$(mktemp -d)
mkdir -p "$T/006" "$T/005"
cp -R specs/006-public-site-local/evidence/ui-baseline/. "$T/006/"
cp specs/005-admin-ui-components/evidence/baseline/PublicLayoutPage.html "$T/005/"
norm() { perl -0777 -pe 's/ b-[a-z0-9]{10}//g; s/<style>.*?<\/style>/<style><\/style>/gs; s/\s+/ /g; s/>\s*</>\n</g' "$1"; }
bad=0
echo "== 1. unstripped, every page must differ"
for p in 006/tokens.html 006/molecules/nav.html 006/molecules/breadcrumb.html 006/organisms/site-header.html 006/templates/public-layout.html 005/PublicLayoutPage.html; do
  if diff -q <(norm "$OLD/$p") <(norm "$T/$p") >/dev/null; then echo "SELFTEST FAIL $p identical before stripping (a strip would be vacuous)"; bad=$((bad+1)); else echo "ok $p differs ($(diff <(norm "$OLD/$p") <(norm "$T/$p") | grep -c '^[<>]') lines)"; fi
done
echo "== 2. intended edits only (want ALL EQUAL)"
bash "$E/snapshot-diff.sh" "$OLD" "$T"; r1=$?
perl -0pi -e 's/(<dt[^>]*>)TypeBody</${1}TypeBodyX</' "$T/006/tokens.html"
perl -0pi -e 's/>September 6</>September 7</' "$T/006/organisms/site-header.html"
echo "== 3. plus stray edits in tokens and site-header (want 2 RESIDUE)"
bash "$E/snapshot-diff.sh" "$OLD" "$T" | grep -E '^(RESIDUE|SAME|ALL|[0-9])'; r2=${PIPESTATUS[0]}
rm -rf "$T"
[ "$bad" = 0 ] && [ "$r1" = 0 ] && [ "$r2" = 2 ] && echo "SELFTEST PASS" || { echo "SELFTEST FAIL bad=$bad r1=$r1 r2=$r2"; exit 1; }
