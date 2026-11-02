#!/usr/bin/env bash
# 010 owner review 2: word-level diff of the regenerated snapshots (scoped-CSS ids stripped, style
# blocks blanked, whitespace collapsed), so every remaining hunk is a reviewed, intended change.
# Usage (repo root): bash specs/010-navigation-improvements/evidence/snapshot-diff-review2.sh <old-dir>
#   <old-dir> holds 006/organisms/site-header.html, 006/templates/public-layout.html, 005/PublicLayoutPage.html
set -uo pipefail
OLD="$1"
norm() { perl -0777 -pe 's/ b-[a-z0-9]{10}//g; s/<style>.*?<\/style>/<style><\/style>/gs; s/\s+/ /g; s/>\s*</>\n</g' "$1"; }
pair() { echo "=== $2"; diff <(norm "$OLD/$1") <(norm "$2") | grep '^[<>]' | cut -c1-240; }
pair 006/organisms/site-header.html specs/006-public-site-local/evidence/ui-baseline/organisms/site-header.html
pair 006/templates/public-layout.html specs/006-public-site-local/evidence/ui-baseline/templates/public-layout.html
pair 005/PublicLayoutPage.html specs/005-admin-ui-components/evidence/baseline/PublicLayoutPage.html
