#!/usr/bin/env bash
# 011 commit helper: stages from explicit path lists (a missing pathspec aborts the whole add).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
CODE=(src tests specs/005-admin-ui-components/evidence/baseline specs/006-public-site-local/evidence/ui-baseline)
git add -A -- "${CODE[@]}"
git commit -q -F - <<'EOF'
feat(011): home page at /, listing at /g, Home-rooted trails (T006-T020)

- / renders HomePage: PublicLayout Fill + Nav Home, one "Galleries" link, no datastore read,
  HtmlPolicy + output cache. Source guard: the / lambda takes no parameters.
- /g is the old / handler moved verbatim (ListingPage, II.6 filter, one HomeAsync read).
  /g/ answers 301 /g from inside the /g handler (decisions row 3: a separate route is ambiguous).
- PurgeSets.ListingPath "/g" replaces HomePath; "/" is never purged.
- Trails.cs builds every public trail; gallery, photo, 404 and layout default call it.
- Breadcrumb CSS: higher ancestors never shrink (decisions row 10, Tap_boxes at 360 px).
- Speculation rule matches /g and /g/*; admin empty-gallery View falls back to /g (R-10).
- New tests: HomePageTests, TrailRoutesTests, TrailSourceGuardTests, HomeBrowserTests.
- Stated test changes per contracts/test-impact.md (decisions rows 11-12).
- Snapshots regenerated (decisions row 13; snapshot-diff ALL EQUAL).

Verified: build -warnaserror clean; Core 108 passed; Web 1107 passed, 1 skipped (1108).
EOF
git add -A -- specs/011-home-page
git commit -q -F - <<'EOF'
docs(011): evidence scripts and outputs, decisions rows 3-13

snapshot-diff.sh ALL EQUAL vs pre-011-specimens (self-test PASS); p95.sh ALL PASS on
home-timings.log (/ db_reads == 0, /g == 1, / p95 0.13 ms); verify.sh ALL PASS live and
12 of 14 FAIL on the pre-011 build (the 2 passes are pre-existing invariants).
EOF
git log --oneline -3
git status --short
