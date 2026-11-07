#!/usr/bin/env bash
# 015 T029 non-vacuity battery (from 014's): one deliberate break per guard; rebuild; the named test
# must go red ON ITS ASSERTION; restore + touch. Run from the repo root:
#   bash specs/015-tile-icon-controls/evidence/non-vacuity.sh | tee specs/015-tile-icon-controls/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree check).
# NV_ONLY="a b": run only those break ids.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

TILE=src/Gallery.Web/Components/Molecules/Tile.razor
TILECSS=src/Gallery.Web/Components/Molecules/Tile.razor.css
PA=src/Gallery.Web/Components/Molecules/PhotoActions.razor
PACS=src/Gallery.Web/Components/Molecules/PhotoActions.cs
PACSS=src/Gallery.Web/Components/Molecules/PhotoActions.razor.css
LIB=src/Gallery.Web/Admin/Pages/LibraryPage.razor
ICONS=src/Gallery.Web/Components/Atoms/Icons.cs
CM=src/Gallery.Web/Components/Molecules/CoverMarker.razor
FILES="$TILE $TILECSS $PA $PACS $PACSS $LIB $ICONS $CM"
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

# [SAFETY FR-004, II/IV.22] public tiles byte-identical: TitleOnPhoto defaults to true.
brk safety $TILE 's/public bool TitleOnPhoto \{ get; set; \}/public bool TitleOnPhoto { get; set; } = true;/' \
  'FullyQualifiedName~PublicOutputUnchangedTests' \
  'Tile.TitleOnPhoto defaults to true'
# FR-012 one active photo: the edit icon is active on every tile.
brk active $PA 's/(Href="\@EditHref" )Active="Active"/${1}Active="true"/' \
  'FullyQualifiedName~PhotoActionsTests' \
  'PhotoActions marks the edit icon active on every tile'
# R-03 toggle: the close href keeps open=.
brk close $LIB 's/var href = open \? \$"\?page=\{Data\.Page\}#t-\{id\}"/var href = open ? \$"?page={Data.Page}\&open={id}#t-{id}"/' \
  'FullyQualifiedName~PhotoActionsTests' \
  'the open tile'"'"'s edit href keeps open= (no close)'
# FR-006 glyphs: one gallery shows "folders".
brk glyph $PACS 's/Membership\.One => "folder",/Membership.One => "folders",/' \
  'FullyQualifiedName~PhotoActionsTests' \
  'IconOf(One) returns folders'
# IX.3 unavailable view: renders a link instead of a span.
brk unavail $PA 's/Label="No public page" Unavailable="true"/Label="No public page" Href="#"/' \
  'FullyQualifiedName~PhotoActionsTests' \
  'an unavailable view renders a link'
# SC-004 legibility: the band background is transparent.
brk band $TILECSS 's/background: rgba\(0, 0, 0, \.6\);/background: transparent;/' \
  'FullyQualifiedName~TileIconBrowserTests' \
  'the title band is transparent'
# SC-001 one row (tasks.md: "flex-wrap: wrap plus a 4th dummy button"; done as one CSS change that
# forces the same symptom, a wrapped row, since a brk edits one file).
brk wrap $PACSS 's/flex-wrap: nowrap;/flex-wrap: wrap; width: 90px;/' \
  'FullyQualifiedName~TileIconBrowserTests' \
  'the photo-actions row wraps'
# FR-018 nothing unused shipped: an extra unused key.
brk registry $ICONS 's/(\["pencil"\] = )/["unused-x"] = "<path d=\\"M0 0\\" \/>",\n        $1/' \
  'FullyQualifiedName~IconRegistryGuardTests' \
  'Icons.Paths gains an unused key'
# R-06 / owner rule: the view icon without target="_blank".
brk newtab $PA 's/ Href="\@ViewHref" Target="_blank"/ Href="\@ViewHref"/' \
  'FullyQualifiedName~NewTabLinkTests' \
  'the view icon opens in the same tab'
# K6 pinned cover is a pressed toggle: aria-pressed dropped.
brk pressed $CM 's/(CoverState\.Pinned => \@<IconButton [^\n]*?)Toggle="true" /$1/' \
  'FullyQualifiedName~BuilderIconTests' \
  'the pinned cover icon drops aria-pressed'

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
