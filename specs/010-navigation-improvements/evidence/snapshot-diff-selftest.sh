#!/usr/bin/env bash
# Self-test for snapshot-diff.sh: intended edits must be EQUAL, a stray edit must be RESIDUE.
set -uo pipefail
E=specs/010-navigation-improvements/evidence
B=specs/006-public-site-local/evidence/ui-baseline
T=$(mktemp -d); mkdir -p "$T/old" "$T/new"
cp -R "$B/." "$T/old/"; cp -R "$B/." "$T/new/"
perl -0pi -e 's/class="text-link"/class="text-link tap"/g; s/(<\/main>)/<section class="specimen" b-x><h2 class="specimen-label" b-x>Tap area<\/h2><div>x<\/div><\/section>$1/' "$T/new/molecules/pager.html"
perl -0pi -e 's/\s*<nav class="nav" aria-label="Site".*?<\/nav>//s' "$T/new/templates/public-layout.html"
perl -0pi -e 's/(<li[^>]*><a href="\/ui\/atoms\/title")/<li b-7><a href="\/ui\/molecules\/gallery-link" b-7>GalleryLink<\/a><\/li>$1/' "$T/new/index.html"
echo "== intended edits only (want 0 residue)"; bash "$E/snapshot-diff.sh" "$T/old" "$T/new" | grep -vE '^SAME'; r1=${PIPESTATUS[0]}
perl -0pi -e 's/>Next</>Onward</' "$T/new/molecules/pager.html"
perl -0pi -e 's/\s*<nav class="nav" aria-label="Site".*?<\/nav>//s' "$T/new/organisms/site-header.html"
echo "== plus stray edits in pager and site-header (want 2 residue)"; bash "$E/snapshot-diff.sh" "$T/old" "$T/new" | grep -E '^(RESIDUE|ALL|[0-9])'; r2=${PIPESTATUS[0]}
rm -rf "$T"
[ "$r1" = 0 ] && [ "$r2" = 2 ] && echo "SELFTEST PASS" || { echo "SELFTEST FAIL r1=$r1 r2=$r2"; exit 1; }
