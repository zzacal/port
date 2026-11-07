#!/usr/bin/env bash
# 014 T024 non-vacuity battery (from speckit-orchestration templates/non-vacuity.sh): one deliberate
# break per [SAFETY] guard and per pinned rule; rebuild; the named test must go red ON ITS ASSERTION;
# restore + touch. Run from the repo root:
#   bash specs/014-gallery-combobox/evidence/non-vacuity.sh | tee specs/014-gallery-combobox/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree check).
# NV_ONLY="a b": run only those break ids.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

IS=src/Gallery.Web/Library/ImageService.cs
GS=src/Gallery.Web/Library/GalleryService.cs
AF=src/Gallery.Web/Admin/AdminAntiforgeryFilter.cs
TC=src/Gallery.Web/Components/Molecules/TokenCombobox.cs
JS=src/Gallery.Web/wwwroot/admin-assets/library.js
FILES="$IS $GS $AF $TC $JS"
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

# [SAFETY FR-014] validate-before-write: the gallery-existence check is skipped, so an unknown add no
# longer stops the save before the create writes.
brk order $IS 's/\n\s*if \(!found\.ToHashSet\(StringComparer\.Ordinal\)\.SetEquals\(named\)\) return NoGallery\(\);//' \
  'FullyQualifiedName~LibraryMembershipEditTests.An_unknown_add_id_with_a_create_is_404_and_creates_nothing' \
  'SavePanelAsync skips the gallery-existence check (writes before refusing)'
# [SAFETY FR-014] the create-title check (step 4) is dropped.
brk title $IS 's|// 4\. Every create title\..*?// 5\.|// 5.|s' \
  'FullyQualifiedName~LibraryMembershipEditTests.A_201_char_create_title_is_422_and_writes_nothing' \
  'SavePanelAsync drops the create-title check'
# [SAFETY II.5] a created gallery is public.
brk private $GS 's/Visibility = "private",/Visibility = "public",/' \
  'FullyQualifiedName~LibraryMembershipEditTests.Create_makes_one_private_gallery' \
  'CreateAsync makes the gallery public'
# [SAFETY I.6] antiforgery skipped when the form carries an add field (every other POST still checked).
brk csrf $AF 's/(if \(!SafeMethods\.Contains\(httpContext\.Request\.Method\)\)\n\s*\{\n)/$1            if (httpContext.Request.HasFormContentType \&\& (await httpContext.Request.ReadFormAsync()).ContainsKey("add")) return await next(context);\n/' \
  'FullyQualifiedName~LibraryMembershipEditTests.With_a_session_and_no_antiforgery_token' \
  'AdminAntiforgeryFilter skips validation for forms with an add field'
# Match rule: taken ids are not excluded (C# side).
brk taken $TC 's/ && !taken\.Contains\(o\.Id\)//' \
  'FullyQualifiedName~TokenComboboxTests' \
  'Match ignores taken ids'
# library.js gains an injection sink.
brk sink $JS 's/\z/\ndocument.createElement("i").innerHTML = "";\n/' \
  'FullyQualifiedName~LibrarySourceGuardTests' \
  'library.js uses innerHTML'
# JS-side breaks (parity test, Batch C) are appended after Batch C lands.

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
