#!/usr/bin/env bash
# 010 T046 non-vacuity battery: one deliberate break per guard, rebuild, confirm the named test
# goes red ON ITS ASSERTION, restore from git and touch the file. Run from the repo root:
#   bash specs/010-navigation-improvements/evidence/non-vacuity.sh | tee specs/010-navigation-improvements/evidence/non-vacuity.txt
# Needs a clean tree for every file it breaks and builds into ./artifacts (never the owner's watch).
# Set NV_DRYRUN=1 to only check that every substitution matches (temp copies, no build).
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

FILES="src/Gallery.Web/Components/Molecules/Pager.razor.css src/Gallery.Web/Components/Organisms/OwnerBar.razor src/Gallery.Web/Library/SiteProjector.cs src/Gallery.Web/Site/PublicEndpoints.cs src/Gallery.Web/Library/GalleryService.cs src/Gallery.Web/Library/OperationResult.cs src/Gallery.Web/Edge/PurgeSets.cs src/Gallery.Web/Components/Templates/PublicLayout.razor"
if [ "${NV_DRYRUN:-0}" != 1 ]; then
  for f in $FILES; do
    git diff --quiet -- "$f" || { echo "ABORT $f has uncommitted changes"; exit 1; }
  done
fi

# brk <id> <file> <perl -0 substitution> <dotnet test filter> <description>
brk() {
  local id="$1" file="$2" expr="$3" filter="$4" what="$5"
  if [ "${NV_DRYRUN:-0}" = 1 ]; then
    local t; t=$(mktemp); cp "$file" "$t"; perl -0pi -e "$expr" "$t"
    if cmp -s "$file" "$t"; then echo "DRY FAIL ($id) substitution matched nothing in $file"; fails=$((fails+1))
    else echo "DRY OK   ($id) $file: $(diff "$file" "$t" | grep -c '^[<>]') changed lines"; fi
    rm -f "$t"; return
  fi
  perl -0pi -e "$expr" "$file"
  if git diff --quiet -- "$file"; then
    echo "FAIL ($id) break did not change $file"; fails=$((fails+1)); return
  fi
  if ! dotnet build "$SLN" --artifacts-path "$ART" -nologo -v q >/tmp/nv-build.txt 2>&1; then
    echo "FAIL ($id) broken build did not compile:"; grep -m3 'error' /tmp/nv-build.txt; fails=$((fails+1))
  else
    out=$(dotnet test "$TESTS" --no-build --artifacts-path "$ART" --blame-hang-timeout 120s --filter "$filter" 2>&1)
    if echo "$out" | grep -q '^Failed!'; then
      name=$(echo "$out" | grep -m1 -E '^\s+Failed ' | sed -E 's/^\s+Failed //; s/ \[.*//')
      msg=$(echo "$out" | grep -m1 -A2 'Error Message:' | tail -2 | tr -s ' ' | tr '\n' ' ' \
            | sed 's/ Stack Trace:.*//' | cut -c1-220)
      if echo "$msg" | grep -qE '[A-Za-z]+Exception:'; then
        echo "FAIL ($id) $what -> red on an EXCEPTION, not an assertion: $name | $msg"; fails=$((fails+1))
      else
        echo "PASS ($id) $what -> red: $name | $msg"
      fi
    else
      echo "FAIL ($id) $what -> guard stayed GREEN"; fails=$((fails+1))
    fi
  fi
  git checkout -q -- "$file" && touch "$file"
}

# (1) [SAFETY] II.2: the listed sequence stops coming from Cards (private/empty galleries become neighbours).
brk 1 src/Gallery.Web/Library/SiteProjector.cs \
  's/var sequence = Cards\(order, byId\.Values\)\.Select\(c => c\.GalleryId\)\.ToList\(\);/var sequence = order.Where(byId.ContainsKey).Distinct().ToList();/' \
  'FullyQualifiedName~NavProjectionTests' 'SiteProjector.Nav uses every homeOrder gallery instead of Cards'

# (2) [SAFETY] II.2: the gallery route drops the public check (planted stale nav on a private gallery renders).
brk 2 src/Gallery.Web/Site/PublicEndpoints.cs \
  's/var nav = gallery\.Visibility == "public" \? gallery\.Nav : null;/var nav = gallery.Nav;/' \
  'FullyQualifiedName~NavRoutesTests' 'gallery route renders Nav without the public check'

# (3) [SAFETY] single writer: a second nav writer appears in GalleryService.
brk 3 src/Gallery.Web/Library/GalleryService.cs \
  's/\}\s*\z/    internal static readonly UpdateDefinition<GalleryDoc> NvBreak = Builders<GalleryDoc>.Update.Unset(g => g.Nav);\n}\n/' \
  'FullyQualifiedName~NavSourceGuardTests' 'GalleryService gains Update.Unset(g => g.Nav)'

# (4) P-NAV: CompleteAsync stops adding PurgeSets.Nav.
brk 4 src/Gallery.Web/Library/OperationResult.cs \
  's/if \(_navChanged\.Count > 0\)\n/if (false \&\& _navChanged.Count > 0)\n/' \
  'FullyQualifiedName~NavPurgeTests' 'CompleteAsync omits PurgeSets.Nav'

# (5) E1 rule 4: Changed stops marking every id when the count changes.
brk 5 src/Gallery.Web/Edge/PurgeSets.cs \
  's/\n\s*else if \(countChanged\)\n\s*changed\.Add\(id\);//' \
  'FullyQualifiedName~NavPurgeTests' 'Changed drops rule 4 (count change)'

# (7) review 1 B4: Pager side tracks back to minmax(44px) -> "Previous" truncates on the library pager.
brk 7 src/Gallery.Web/Components/Molecules/Pager.razor.css \
  's/minmax\(max-content, 1fr\) auto minmax\(max-content, 1fr\)/minmax(44px, 1fr) auto minmax(44px, 1fr)/' \
  'FullyQualifiedName~AdminNavBrowserTests.Library_pager' 'Pager side tracks can shrink below the word'

# (8) review 1 B6: a page action back in the owner bar breaks the one-bar guard.
brk 8 src/Gallery.Web/Components/Organisms/OwnerBar.razor \
  's/        \@SignOutForm\n/        <Button Variant="ButtonVariant.Primary" Type="button">New gallery<\/Button>\@SignOutForm\n/' \
  'FullyQualifiedName~AdminNavConsistencyTests.Every_admin_page' 'owner bar carries a page action'

# (6) keys survive Save-Data: move the key handler after the Save-Data return.
brk 6 src/Gallery.Web/Components/Templates/PublicLayout.razor \
  's/(document\.addEventListener\(.keydown.*?\n\}\);\n)(.*?if \(navigator\.connection\?\.saveData\) return;\n)/$2$1/s' \
  'FullyQualifiedName~NavBrowserTests.Keys_work_with_save_data' 'key handler registered after the Save-Data return'

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
