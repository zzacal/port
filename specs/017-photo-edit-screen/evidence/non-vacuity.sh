#!/usr/bin/env bash
# 017 T022 non-vacuity battery (from 016's): one deliberate break per guard; rebuild; the named test
# must go red ON ITS ASSERTION; restore + touch. Run from the repo root:
#   bash specs/017-photo-edit-screen/evidence/non-vacuity.sh | tee specs/017-photo-edit-screen/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree check).
# NV_ONLY="a b": run only those break ids.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

EP=src/Gallery.Web/Library/LibraryEndpoints.cs
RD=src/Gallery.Web/Library/LibraryReader.cs
PE=src/Gallery.Web/Admin/Pages/PhotoEditPage.razor
PECSS=src/Gallery.Web/Admin/Pages/PhotoEditPage.razor.css
FILES="$EP $RD $PE $PECSS"
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

# [SAFETY I] L6 loses the owner gate. (tasks.md's "map it on app" isn't expressible inside
# MapLibraryEndpoints(admin), which has no app in scope; AllowAnonymous on the L6 line is the same
# break: the route stays mapped but answers signed-out requests.)
brk l6anon $EP 's/(PhotoEditScreen\));( \/\/ L6)/$1.AllowAnonymous();$2/' \
  'FullyQualifiedName~PhotoEditPageTests.Signed_out_L6_answers_the_same_challenge_as_the_library|FullyQualifiedName~LibraryAccessTests.Without_a_session_GET_redirects_to_signin_and_POST_is_401' \
  'L6 answers without a session'
# [SAFETY I.6] the edit form drops its antiforgery field.
brk token $PE 's/(\n\s+)\@Token(<input type="hidden" name="page" value="\@Model.ReturnPage">\n\s+<SidePanel)/$1$2/' \
  'FullyQualifiedName~PhotoEditPageTests.Edit_and_delete_forms_post_to_the_photo_with_the_return_page_and_the_antiforgery_token' \
  'lib-edit has no antiforgery field'
# FR-003 sizes: the grid's sizes on the edit screen.
brk sizes $PE 's/Sizes\.ForAside\(\)/Sizes.ForGrid(4, 2, 1)/' \
  'FullyQualifiedName~PhotoEditPageTests.L6_shows_one_photo|FullyQualifiedName~PhotoEditBrowserTests.At_1440_the_browser_picks_a_derivative_at_least_960_wide' \
  'the edit screen declares the grid sizes'
# SC-002 the stage cap goes: a portrait photo runs past the viewport.
brk cap $PECSS 's/\.pe-stage ::deep \.fit-contain \.sizer \{ max-height: 80vh; \}\n//' \
  'FullyQualifiedName~PhotoEditBrowserTests.SC002_at_1440_the_photo_is_wholly_visible' \
  'no 80vh cap on the stage'
# FR-005 L2 success lands back on the library with ?open=.
brk landing $EP 's/SeeOther\(\$"\{LibraryPath\}\/images\/\{iid\}\?page=\{page\}&saved=1"\)/SeeOther(\$"{LibraryPath}?page={page}&open={iid}&saved=1")/' \
  'FullyQualifiedName~LibraryEditTests.Save_answers_303_and_the_panel_shows_the_stored_values_and_Saved' \
  'save lands on the library'
# US2 the breadcrumb forgets the page.
brk crumb $PE 's/\/admin\/library\?page=\{Model\.ReturnPage\}#t-/\/admin\/library#t-/' \
  'FullyQualifiedName~PhotoEditBrowserTests.SC004_save_then_breadcrumb_returns_to_library_page_2' \
  'the breadcrumb goes to library page 1'
# FR-012 a tombstoned photo is still editable.
brk live $RD 's/\.Find\(Live & Builders<ImageDoc>\.Filter\.Eq\(i => i\.Id, iid\)\)/.Find(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid))/' \
  'FullyQualifiedName~PhotoEditPageTests.Malformed_unknown_and_tombstoned_ids_are_404|FullyQualifiedName~PhotoReaderTests.Tombstoned_id' \
  'PhotoAsync reads tombstoned images'
# IX.4 the edit screen draws its own gap under the chrome.
brk gap $PECSS 's/\z/\n.pe { margin-top: 40px; }\n/' \
  'FullyQualifiedName~AdminLayoutConsistencyBrowserTests' \
  'the edit screen adds a top margin'

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
