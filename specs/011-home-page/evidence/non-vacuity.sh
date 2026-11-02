#!/usr/bin/env bash
# Non-vacuity battery: apply one deliberate break per [SAFETY] guard, rebuild, confirm the named
# test goes red ON ITS ASSERTION, restore from git and touch the file. Copy into
# <feature>/evidence/non-vacuity.sh, fill in the brk lines, run from the repo root:
#   bash <feature>/evidence/non-vacuity.sh | tee <feature>/evidence/non-vacuity.txt
# Needs a clean tree for every file it breaks (it aborts otherwise) and builds into ./artifacts,
# never the owner's watch output.
# NV_DRYRUN=1: only check each substitution matches (temp copies, no build, no clean-tree check).
# Safe while executors are running; breaks on code a later batch writes match nothing until then.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"                 # solution file
TESTS="tests/Gallery.Web.Tests"    # test project the filters run against
fails=0

# List every file a break touches; refuse to run over uncommitted work.
FILES="src/Gallery.Web/Site/PublicEndpoints.cs src/Gallery.Web/Edge/PurgeSets.cs src/Gallery.Web/Site/Trails.cs src/Gallery.Web/Site/Pages/NotFoundPage.razor src/Gallery.Web/Components/Molecules/Nav.razor.css"
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
  # A substitution that matched nothing is a broken break, not a working guard.
  if git diff --quiet -- "$file"; then
    echo "FAIL ($id) break did not change $file"; fails=$((fails+1)); return
  fi
  if ! dotnet build "$SLN" --artifacts-path "$ART" -nologo -v q >/tmp/nv-build.txt 2>&1; then
    echo "FAIL ($id) broken build did not compile:"; grep -m3 'error' /tmp/nv-build.txt; fails=$((fails+1))
  else
    out=$(dotnet test "$TESTS" --no-build --artifacts-path "$ART" --blame-hang-timeout 120s --filter "$filter" 2>&1)
    if echo "$out" | grep -q '^Failed!'; then
      name=$(echo "$out" | grep -m1 -E '^\s+Failed ' | sed -E 's/^\s+Failed //; s/ \[.*//')
      # Cut at "Stack Trace:" BEFORE classifying, or every assertion failure looks like an exception.
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
  git checkout -q -- "$file" && touch "$file"   # touch: restored mtime can predate build output
}

# 011 T025: one deliberate break per [SAFETY] guard (tasks.md T025).
W=src/Gallery.Web
brk a $W/Site/PublicEndpoints.cs 's/\(\) =>\n(\s+)\(IResult\)(new StatusAndHeadersResult\(200,.*?PublicCache\.PolicyName : null\))\);/async (ContentReader nvr) => { await nvr.HomeAsync(); return (IResult)$2; });/s' \
  'FullyQualifiedName~HomePageTests.Home_reads_nothing_and_listing_reads_the_site_once' '/ handler given a HomeAsync() read -> T014(b)'
brk b $W/Site/PublicEndpoints.cs 's/\.Where\(c => c\.Cover is not null\)/.Where(c => true)/' \
  'FullyQualifiedName~HomePageTests.Listing_shows_exactly' 'listing II.6 cover filter removed -> T014(c)'
brk c $W/Edge/PurgeSets.cs 's/public const string ListingPath = "\/g";/public const string ListingPath = "\/";/' \
  'FullyQualifiedName~HomePageTests.No_purge_set_ever_contains_the_home_page' 'ListingPath set to "/" -> T014(f)'
brk d1 $W/Site/Trails.cs 's/new\(ListingLabel, ListingHref\), \.\. tail\]/new(ListingLabel, ListingHref), new("Other", "\/g\/zz\/other"), .. tail]/' \
  'FullyQualifiedName~TrailsTests.Under_links_only_home_listing_and_its_own_tail' 'Trails.Under links another gallery -> T005'
brk d2 $W/Site/Trails.cs 's/new\(ListingLabel, ListingHref\), \.\. tail\]/new(ListingLabel, ListingHref), new("Other", "\/g\/zz\/other"), .. tail]/' \
  'FullyQualifiedName~TrailRoutesTests.A_private_gallery_trail_links_no_other_gallery' 'Trails.Under links another gallery -> T018 (private trail)'
brk e $W/Site/Pages/NotFoundPage.razor 's/\z/\@* "Galleries" *\@\n/' \
  'FullyQualifiedName~TrailSourceGuardTests' 'a "Galleries" literal in NotFoundPage.razor -> T019'
brk f $W/Components/Molecules/Nav.razor.css 's/font-size: var\(--type-display\);/font-size: var(--type-brand);/' \
  'FullyQualifiedName~HomeBrowserTests.Home_link_is_centred_unscrolled_largest_and_tappable' 'nav--home font-size var(--type-brand) -> T020(d)'


if [ "${NV_DRYRUN:-0}" != 1 ]; then
  # Rebuild the unbroken tree so later runs do not test stale output.
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
