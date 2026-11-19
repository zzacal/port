#!/usr/bin/env bash
# 020 T008/T068 non-vacuity battery (from 017's): one deliberate break per [SAFETY] guard; rebuild;
# the named test must go red ON ITS ASSERTION; restore + touch. Run from anywhere (cd's to the repo root):
#   bash specs/020-admin-workspace/evidence/non-vacuity.sh | tee specs/020-admin-workspace/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree
#   check). A file that does not exist yet, or a pattern that finds nothing, prints
#   "DRY MISS (id) ... matched nothing" (code not written yet: re-dry-run after every checkpoint).
#   Dry-run exit status = number of misses.
# NV_ONLY="1 7 20": run only those break ids.  ARTIFACTS=<dir> overrides artifacts/exec-nv.
# Break ids 1-21 are T068's list, in order. Each substitution is written from tasks.md and the
# contracts BEFORE the code exists, so each is anchored on a name the task fixes; re-check every
# "matched" diff by eye after its batch lands (a break that compiles but misses the guard shows as
# "guard stayed GREEN" in the real run: fix the BREAK, never the guard).
set -uo pipefail
export DOTNET_NOLOGO=1
E=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$E/../../.." && pwd)
# NV_ROOT=<dir> (dry-run only): dry-run the breaks against another tree with the same layout (used to
# prove the patterns on synthetic copies of not-yet-written code). NV_SHOWDIFF=1: print each dry diff.
[ "${NV_DRYRUN:-0}" = 1 ] && [ -n "${NV_ROOT:-}" ] && ROOT=$NV_ROOT
cd "$ROOT" || exit 1
ART=${ARTIFACTS:-artifacts/exec-nv}
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0
miss=0
F='FullyQualifiedName~'

UE=src/Gallery.Web/Upload/UploadEndpoints.cs
CS=src/Gallery.Web/Upload/ConfirmService.cs
BS=src/Gallery.Web/Upload/BatchStore.cs
ES=src/Gallery.Web/Upload/EffectiveState.cs
TRAY=src/Gallery.Web/Components/Organisms/UploadTray.razor
UJS=src/Gallery.Web/wwwroot/admin-assets/upload.js
WJS=src/Gallery.Web/wwwroot/admin-assets/workspace.js
API=src/Gallery.Web/Workspace/WorkspaceApi.cs
WEP=src/Gallery.Web/Workspace/WorkspaceEndpoints.cs
AAF=src/Gallery.Web/Admin/AdminAntiforgeryFilter.cs
PL=src/Gallery.Web/Components/Templates/PublicLayout.razor
FILES="$UE $CS $BS $ES $TRAY $UJS $WJS $API $WEP $AAF $PL"
if [ "${NV_DRYRUN:-0}" != 1 ]; then
  for f in $FILES; do
    [ -f "$f" ] || { echo "ABORT $f does not exist (its batch has not landed)"; exit 1; }
    [ -z "$(git status --porcelain -- "$f")" ] || { echo "ABORT $f has uncommitted changes"; exit 1; }
  done
fi

# brk <id> <file> <perl -0 code> <dotnet test filter> <description> [extra shell check that must succeed]
brk() {
  local id="$1" file="$2" expr="$3" filter="$4" what="$5" extra="${6:-}"
  if [ -n "${NV_ONLY:-}" ] && ! echo " $NV_ONLY " | grep -q " $id "; then return; fi
  if [ "${NV_DRYRUN:-0}" = 1 ]; then
    if [ ! -f "$file" ]; then
      echo "DRY MISS ($id) $file does not exist yet: matched nothing | $what"; miss=$((miss+1)); return
    fi
    local t; t=$(mktemp); cp "$file" "$t"
    if ! perl -0pi -e "$expr" "$t" 2>/tmp/nv-perl.txt; then
      echo "DRY FAIL ($id) perl error in the break: $(head -1 /tmp/nv-perl.txt)"; fails=$((fails+1)); rm -f "$t"; return
    fi
    if cmp -s "$file" "$t"; then echo "DRY MISS ($id) substitution matched nothing in $file | $what"; miss=$((miss+1))
    else echo "DRY OK   ($id) $file: $(diff "$file" "$t" | grep -c '^[<>]') changed lines | $what"
      [ "${NV_SHOWDIFF:-0}" = 1 ] && diff "$file" "$t" | sed 's/^/      /'; fi
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
      elif [ -n "$extra" ] && ! (eval "$extra"); then
        echo "FAIL ($id) $what -> red: $name, but the extra check failed: $extra"; fails=$((fails+1))
      else
        echo "PASS ($id) $what -> red: $name | $msg${extra:+ | extra check held}"
      fi
    elif echo "$out" | grep -q '^Passed!'; then
      echo "FAIL ($id) $what -> guard stayed GREEN"; fails=$((fails+1))
    else
      echo "FAIL ($id) $what -> no test ran for filter: $filter"; fails=$((fails+1))
    fi
  fi
  git checkout -q -- "$file" && touch "$file"
}

# 1. T043 II.5a: the destination-public branch in Register never runs (its guard is made false, so
#    the branch is deleted in effect and the build stays clean). Anchored on the error code literal
#    and the nearest `if (...)` above it with no other `if` in between.
brk 1 $UE 's/\bif\s*(\((?:[^()]++|(?1))*\))((?:(?!\bif\s*\().)*?"destination-public")/if (Environment.ProcessorCount < 0 && $1)$2/s' \
  "${F}UploadJoinTests.Register_into_a_public_gallery_without_the_flag_is_409_destination_public" \
  'T043 II.5a: Register has no destination-public refusal'
# 2. T025 II.5a notice: the ws-notice paragraph renders only in the Empty state.
brk 2 $TRAY 's/\@if\s*\(\s*DestinationPublic\s*\)(\s*\{?\s*<p class="ws-notice")/\@if (DestinationPublic && State == TrayState.Empty)$1/' \
  "${F}WorkspaceComponentTests.UploadTray_public_notice_is_plain_text_in_every_state|${F}WorkspaceUploadBrowserTests.Public_destination_notice_is_visible_before_picking_and_while_working" \
  'T025 II.5a: public notice only when the tray is Empty'
# 3. T046 II.5a flag (applies only once upload.js sends visibleToVisitors): U3 always sends visibleToVisitors: true, and the script never finds the
#    notice to clone (its selector/template name is renamed away).
brk 3 $UJS 'if (/visibleToVisitors/) { s/visibleToVisitors\s*:\s*[^,\n}]+/visibleToVisitors: true/g; s/ws-notice/ws-notice-nv-dropped/g }' \
  "${F}WorkspaceUploadBrowserTests.Public_destination_notice_is_visible_before_picking_and_while_working" \
  'T046 II.5a: upload.js always consents and drops the notice clone'
# 4. T042 II.5b: after CompleteJoinAsync, a join with no destination adds the photo to the first
#    gallery of site.homeOrder through the GalleryService field JoinAsync already uses for AddAsync.
brk 4 $CS 'my ($g) = /await\s+(_\w+)\.AddAsync\(/; s/(await\s+_\w+\.CompleteJoinAsync\(\s*([\w.]+)\s*,\s*([\w.!]+)\s*,\s*([\w.!]+)\s*\);)/$1\n        { var nvSite = await _imageDocs.Database.GetCollection<SiteDoc>("site").Find(FilterDefinition<SiteDoc>.Empty).FirstOrDefaultAsync(); if (nvSite is { HomeOrder.Count: > 0 }) await $g.AddAsync(nvSite.HomeOrder[0], [$4]); }/ if $g' \
  "${F}UploadJoinTests.Join_with_no_destination_calls_no_gallery_method_and_enqueues_no_edge_work|${F}Public_pages_byte_identical_around_a_join_with_no_destination" \
  'T042 II.5b: a join with no destination adds to the first home gallery'
# 5. T042 II.5b edge (applies only once JoinAsync calls CompleteJoinAsync): ConfirmService gets an IEdgeCache (optional ctor param, DI fills it) and
#    enqueues EdgeWork("upload.join", [], ["/g"], [], now) after the join completes.
brk 5 $CS 'if (/await\s+_\w+\.CompleteJoinAsync\(/) { s/(Func<string, Task>\?\s+afterItem\s*=\s*null)\)/$1, Gallery.Web.Edge.IEdgeCache? nvEdge = null)/; s/(_afterItem = afterItem;)/$1\n        _nvEdge = nvEdge;/; s/(private readonly Func<string, Task>\? _afterItem;)/$1\n    private readonly Gallery.Web.Edge.IEdgeCache? _nvEdge;/; s/(await\s+_\w+\.CompleteJoinAsync\([^;]*\);)/$1\n        _nvEdge?.Enqueue(new Gallery.Web.Edge.EdgeWork("upload.join", [], ["\/g"], [], _clock.GetUtcNow()));/ }' \
  "${F}UploadJoinTests.Join_with_no_destination_" \
  'T042 II.5b: a join with no destination enqueues edge work'
# 6. T042 II.5c: right after the destination AddAsync, publish the gallery.
brk 6 $CS 's/(await\s+(_\w+)\.AddAsync\(\s*([\w.!]+)\s*,[^;]*\);)/$1\n            await $2.PublishAsync($3, confirmed: true);/' \
  "${F}UploadJoinTests.Join_never_changes_visibility" \
  'T042 II.5c: the join publishes its destination'
# 7. T053 II.5c scan: a visibility write with the literal "public" inside Upload/ConfirmService.cs.
brk 7 $CS 's/(\n(\s*)public static string MasterKey\()/\n$2private static UpdateDefinition<GalleryDoc> NvPublish() => Builders<GalleryDoc>.Update.Set(g => g.Visibility, "public");\n$1/' \
  "${F}WorkspaceRemovalGuardTests" \
  'T053 II.5c: ConfirmService writes visibility "public"'
# 8. T041 II.5d: the claim filter also accepts `uploading` (state list inside TryClaimJoinAsync).
brk 8 $BS 's/(TryClaimJoinAsync\((?:(?!\n    (?:public|private|internal) ).)*?)ItemStates\.Joining/$1ItemStates.Joining, ItemStates.Uploading/s' \
  "${F}UploadJoinTests.Join_of_an_item_that_is_not_validated_is_409_not_active" \
  'T041 II.5d: the join claim accepts an uploading item'
# 9. T043 claim: U9 Delete no longer recognises `joining` (its check compares against a value no
#    item ever has).
brk 9 $UE 's/(Task<IResult> Delete\((?:(?!\n    (?:public|private|internal) ).)*?)ItemStates\.Joining/$1"nv-never-joining"/s' \
  "${F}UploadJoinTests.Delete_and_restart_of_a_joining_item_are_409_not_active" \
  'T043 claim: U9 deletes a joining item'
# 10. T041/T042 exactly-once (C-22 decisions row): a join that lost the atomic pull in
#     CompleteJoinAsync carries on with a fresh read of the batch instead of answering 404, so a
#     losing overlapping join also adds and logs upload.joined. (Run 15x if flaky: NV_ONLY=10.)
brk 10 $CS 's/var after = await _store\.CompleteJoinAsync\(batchId, itemId, itemId\);/var after = await _store.CompleteJoinAsync(batchId, itemId, itemId) ?? await _store.GetByIdAsync(batchId);/' \
  "${F}UploadJoinTests.Fifty_concurrent_claims_of_one_item_let_exactly_one_join" \
  'T042 exactly-once: a losing join continues after the atomic pull missed'
# 11. T042 III.6: the join copies with no ETag precondition (5th argument of CopyIfMatchAsync is null).
brk 11 $CS 's/(CopyIfMatchAsync\((?:\s*[^,;()]+,){4})\s*[^,;()]+,/$1 null!,/' \
  "${F}UploadJoinTests.Reput_after_validation_publishes_nothing" \
  'T042 III.6: copy without if-match'
# 12. T014 II.6: A6 `public: false` on an empty private gallery answers 200 (emptiness test never true).
brk 12 $API 's/\.Items\.Count\s*==\s*0((?:(?!\n\s*\n).){0,300}?EmptyPublishReason)/.Items.Count < 0$1/s' \
  "${F}WorkspaceApiTests" \
  'T014 II.6: A6 skips the empty-private refusal'
# 13. T014 I.3: A1 answers without a session.
brk 13 $API 's/(Map(?:Post|Methods)\(\s*"(?:\/api)?\/photos\/\{iid\}"[^;]*);/$1.AllowAnonymous();/' \
  "${F}WorkspaceAccessTests" \
  'T014 I.3: A1 is AllowAnonymous'
# 14. T014 I.6: A7 skips the token check. MapWorkspaceApi only sees the protected group, so the
#     bypass is made where the token is checked: AdminAntiforgeryFilter exempts paths ending
#     "/photos" (= A7 POST /admin/api/galleries/{gid}/photos).
brk 14 $AAF 's/if \(!SafeMethods\.Contains\(httpContext\.Request\.Method\)\)/if (!SafeMethods.Contains(httpContext.Request.Method) \&\& !(httpContext.Request.Path.Value ?? "").EndsWith("\/photos", StringComparison.Ordinal))/' \
  "${F}WorkspaceAccessTests" \
  'T014 I.6: A7 accepted without an antiforgery token'
# 15. T033 I.3: W4 answers without a session.
brk 15 $WEP 's/(Map(?:Get|Methods)\(\s*"\/tiles\/\{iid\}"[^;]*);/$1.AllowAnonymous();/' \
  "${F}WorkspaceAccessTests" \
  'T033 I.3: W4 is AllowAnonymous'
# 16. T043 I.3/I.6: U12 answers without a session.
brk 16 $UE 's/(MapPost\(\s*"\/upload\/items\/\{itemId\}\/join"[^;]*);/$1.AllowAnonymous();/' \
  "${F}UploadAccessTests" \
  'T043 I.3: U12 is AllowAnonymous'
# 17. T053 removed routes: GET /admin/library answers 200 again.
brk 17 $WEP 's/(MapWorkspace\(this RouteGroupBuilder admin\)\s*\{)/$1\n        admin.MapGet("\/library", () => Results.Ok());/' \
  "${F}WorkspaceRemovalGuardTests" \
  'T053: /admin/library mapped again'
# 18. T036/T053: a second DOMParser outside swap.
brk 18 $WJS 's/\z/\nexport const nvParser = new DOMParser();\n/' \
  "${F}WorkspaceRemovalGuardTests" \
  'T036/T053: DOMParser outside swap'
# 19. T053 sinks: innerHTML in upload.js.
brk 19 $UJS 's/\z/\nexport function nvSink(el) { el.innerHTML = \x27\x27; }\n/' \
  "${F}WorkspaceRemovalGuardTests|${F}UploadSourceGuardTests.Upload_script_has_no_html_injection_sink" \
  'T053: innerHTML sink in upload.js'
# 20. TI-36 public isolation: owner markup on every public page; compare-public.sh must also go red
#     (exit = number of differing pages, >= 1: every public page renders PublicLayout).
brk 20 $PL 's/(<main class="public-main">)/$1<span class="ws-mark">x<\/span>/' \
  "${F}LibrarySourceGuardTests.Public_pages_carry_no_owner_markup_" \
  'TI-36: ws-mark on the public layout' \
  'ARTIFACTS="$ART" bash "$E/compare-public.sh" >/tmp/nv-compare-public.txt 2>&1; c=$?; echo "   compare-public.sh exit $c"; [ "$c" -ge 1 ] && [ "$c" -le 254 ]'
# 21. T043 joining never ages: EffectiveState.Of treats joining as active.
brk 21 $ES 's/(ItemStates\.Queued or ItemStates\.Processing or ItemStates\.Uploading)(\s*;)/$1 or ItemStates.Joining$2/' \
  "${F}UploadJoinTests.Joining_is_never_reported_interrupted" \
  'T043: joining ages to interrupted'

if [ "${NV_DRYRUN:-0}" = 1 ]; then
  echo
  echo "DRY RUN: $miss matched nothing (code not written yet), $fails broken"
  exit $((miss + fails))
fi
dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
  && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
