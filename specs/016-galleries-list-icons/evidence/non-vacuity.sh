#!/usr/bin/env bash
# 016 T026 non-vacuity battery (from 015's): one deliberate break per guard; rebuild; the named test
# must go red ON ITS ASSERTION; restore + touch. Run from the repo root:
#   bash specs/016-galleries-list-icons/evidence/non-vacuity.sh | tee specs/016-galleries-list-icons/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree check).
# NV_ONLY="a b": run only those break ids.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

GA=src/Gallery.Web/Components/Molecules/GalleryActions.razor
GACS=src/Gallery.Web/Components/Molecules/GalleryActions.cs
GACSS=src/Gallery.Web/Components/Molecules/GalleryActions.razor.css
LIB=src/Gallery.Web/Admin/Pages/LibraryPage.razor
GB=src/Gallery.Web/Components/Organisms/GalleryBuilder.razor.css
ICONS=src/Gallery.Web/Components/Atoms/Icons.cs
FILES="$GA $GACS $GACSS $LIB $GB $ICONS"
if [ "${NV_DRYRUN:-0}" != 1 ]; then
  for f in $FILES; do
    git diff --quiet -- "$f" || { echo "ABORT $f has uncommitted changes"; exit 1; }
  done
fi

# brk <id> <file> <perl -0 substitution> <dotnet test filter> <description>
brk() {
  local id="$1" file="$2" expr="$3" filter="$4" what="$5"
  if [ -n "${NV_ONLY:-}" ] && ! echo " $NV_ONLY " | grep -q " $id "; then return; fi
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
      if echo "$msg" | grep -qE '[A-Za-z]+Exception:' && ! echo "$msg" | grep -qE 'Assert\.|XunitException|PlaywrightAssertions'; then
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

# [SAFETY II.5-6] the private-with-photos eye omits name="public": clicking it would unpublish, never publish.
brk publish $GA 's/ Name="public" Value="on"//' \
  'FullyQualifiedName~GalleryListBrowserTests' \
  'the private eye omits name=public'
# [SAFETY II.6] the empty-private control renders as a button (it becomes clickable).
brk emptybtn $GA 's/(\@<IconButton Icon="eye-off" Label="\@VisLabel" )Unavailable="true"/${1}Type="submit" FormId="\@VisibilityForm" Name="public" Value="on"/' \
  'FullyQualifiedName~GalleryActionsTests' \
  'the empty-private control is a submit button'
# FR-004 edit last: edit is moved before view.
brk order $GA 's/    \@View\n(    <IconButton Icon="pencil"[^\n]*\n)/$1    \@View\n/' \
  'FullyQualifiedName~GalleryActionsTests|FullyQualifiedName~GalleryListBrowserTests' \
  'edit renders before view'
# SC-006 names: VisibilityLabel swaps the public and private names.
brk names $GACS 's/\(true, true\) => "Public\. Make private",/(true, true) => "Private. Make public",/' \
  'FullyQualifiedName~GalleryActionsTests|FullyQualifiedName~GalleryListBrowserTests' \
  'VisibilityLabel names a public gallery private'
# SC-001 one row: the row wraps.
brk wrap $GACSS 's/flex-wrap: nowrap;/flex-wrap: wrap; width: 100px;/' \
  'FullyQualifiedName~GalleryListBrowserTests' \
  'the gallery-actions row wraps'
# SC-007 library medium back to 3 columns.
brk libmed $LIB 's/<ContactSheet Large="4" Medium="2" Small="1">/<ContactSheet Large="4" Medium="3" Small="1">/' \
  'FullyQualifiedName~GridWidthBrowserTests' \
  'the library shows 3 columns at medium widths'
# SC-007 builder panel rule removed.
brk builderq $GB 's/\@container \(max-width: 991\.98px\)/\@container (max-width: 1px)/' \
  'FullyQualifiedName~GridWidthBrowserTests' \
  'the builder container query never applies'
# FR-018 nothing unused shipped: an extra unused key.
brk registry $ICONS 's/(\["pencil"\] = )/["unused-x"] = "<path d=\\"M0 0\\" \/>",\n        $1/' \
  'FullyQualifiedName~IconRegistryGuardTests' \
  'Icons.Paths gains an unused key'

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
