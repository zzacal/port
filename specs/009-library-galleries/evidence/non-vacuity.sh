#!/usr/bin/env bash
# T060 non-vacuity: for each 009 [SAFETY] guard, apply a deliberate break to the source, rebuild,
# and confirm the named test FAILS ON ITS ASSERTION (first error message printed), then restore the
# file from git and touch it. Run from the repo root on a clean committed tree:
#   bash specs/009-library-galleries/evidence/non-vacuity.sh | tee specs/009-library-galleries/evidence/non-vacuity.txt
# Builds into ./artifacts (never the owner's watch output); starts no server on 5080/5081.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="${ART:-artifacts}"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

FILES="src/Gallery.Web/Library/GalleryService.cs src/Gallery.Web/Library/ImageDeleteService.cs
src/Gallery.Web/Maintenance/DevTargetGuard.cs src/Gallery.Web/Maintenance/RemoveSeededCommand.cs
src/Gallery.Web/Admin/AdminEndpoints.cs src/Gallery.Web/Edge/CloudflareEdgeProvider.cs
src/Gallery.Web/Data/Documents.cs src/Gallery.Web/Components/Atoms/Switch.razor
src/Gallery.Web/Components/Templates/PublicLayout.razor"
for f in $FILES; do
  git diff --quiet -- "$f" || { echo "ABORT $f has uncommitted changes"; exit 1; }
done

# brk <id> <file> <perl -0 substitution> <dotnet test filter> <description>
brk() {
  local id="$1" file="$2" expr="$3" filter="$4" what="$5"
  perl -0pi -e "$expr" "$file"
  if git diff --quiet -- "$file"; then
    echo "FAIL ($id) break did not change $file - fix the break, not the guard"; fails=$((fails+1)); return
  fi
  if ! dotnet build "$SLN" --artifacts-path "$ART" -nologo -v q >/tmp/nv-build.txt 2>&1; then
    echo "FAIL ($id) broken build did not compile:"; grep -m3 'error' /tmp/nv-build.txt; fails=$((fails+1))
  else
    out=$(dotnet test "$TESTS" --no-build --artifacts-path "$ART" --filter "$filter" 2>&1)
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
      echo "FAIL ($id) $what -> guard stayed GREEN ($(echo "$out" | grep -E '^Passed!' | cut -c1-80))"; fails=$((fails+1))
    fi
  fi
  git checkout -q -- "$file" && touch "$file"   # touch: restored mtime can predate build output
}

GS=src/Gallery.Web/Library/GalleryService.cs
IDS=src/Gallery.Web/Library/ImageDeleteService.cs

brk a "$GS" \
  's/if \(gallery\.Items\.Count == 0\) return Refuse\("db\.gallery\.update", 409, EmptyPublishReason\);//' \
  'FullyQualifiedName~PublishRefusalTests.An_empty_gallery_is_refused_409_for_every_request_shape_and_nothing_changes' 'publish without the empty check'

brk b "$GS" \
  's/ & Builders<GalleryDoc>\.Filter\.Exists\("items\.0"\)//' \
  'FullyQualifiedName~GalleryServiceTests.Publish_races_a_remove_and_the_conditional_write_refuses' 'publish write without the items.0 filter'

brk c "$IDS" \
  's/var shared = await SharedKeysAsync\(iid, keys\);/var shared = new HashSet<string>(StringComparer.Ordinal);/' \
  'FullyQualifiedName~ImageDeleteTests.Shared_public_file_is_kept' 'delete skips the shared-file check'

brk d "$IDS" \
  's/(        \/\/ 2\. leave every gallery)/        await _images.DeleteOneAsync(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid));\n$1/' \
  'FullyQualifiedName~ImageDeleteTests.Crash_after_galleries_then_repeat' 'record deleted before leaving galleries'

brk e src/Gallery.Web/Maintenance/DevTargetGuard.cs \
  's/        return failed;/        return new List<string>();/' \
  'FullyQualifiedName~RemoveSeededTests.Guard_refuses_non_development_environment' 'target guard always passes'

brk f src/Gallery.Web/Maintenance/RemoveSeededCommand.cs \
  's/,\n\s*\{ "\$nin", new BsonArray \{ BsonNull\.Value, "" \} \},//' \
  'FullyQualifiedName~RemoveSeededTests.Removes_seeded_and_never_touches_owner_content' 'seed filter {$exists:true} only (seedKey "" removed)'

brk f2 src/Gallery.Web/Maintenance/RemoveSeededCommand.cs \
  's/public static BsonDocument SeededFilter\(\) =>\n\s*new\("seedKey", new BsonDocument\n\s*\{\n\s*\{ "\$exists", true \},\n\s*\{ "\$nin", new BsonArray \{ BsonNull\.Value, "" \} \},\n\s*\}\);/public static BsonDocument SeededFilter() => new();/' \
  'FullyQualifiedName~RemoveSeededTests.Removes_seeded_and_never_touches_owner_content' 'seed filter {} (everything)'

brk g src/Gallery.Web/Admin/AdminEndpoints.cs \
  's/admin\.MapLibrary\(\);/app.MapGroup("\/admin").MapLibrary();/' \
  'FullyQualifiedName~LibraryAccessTests' 'library routes mapped outside the protected admin group'

brk h src/Gallery.Web/Edge/CloudflareEdgeProvider.cs \
  's/(            OpTiming\.Log\(_logger, "edge\.purge\.call", Ms\(start\), outcome\);)/            _logger.LogWarning("edge.debug {t}", _options.ApiToken);\n$1/' \
  'FullyQualifiedName~CloudflareEdgeProviderTests|FullyQualifiedName~EdgeSecretTests' 'purge logs the API token'

brk i src/Gallery.Web/Data/Documents.cs \
  's/(\[BsonElement\("seedKey"\)\]      \[BsonIgnoreIfNull\] public string\? SeedKey \{ get; set; \})/$1 = string.Empty;/' \
  'FullyQualifiedName~IndexesTests.GalleryDoc_without_seed_key_serialises_no_seedKey_element|FullyQualifiedName~GalleryServiceTests.Create_is_private_last_in_homeOrder_has_no_seedKey_and_purges_nothing' 'galleries carry seedKey ""'

brk j "$GS" \
  's/            Visibility = "private",/            Visibility = "public",/' \
  'FullyQualifiedName~GalleryServiceTests.Create_is_private_last_in_homeOrder_has_no_seedKey_and_purges_nothing|FullyQualifiedName~LibrarySourceGuardTests' 'create makes the gallery public'

brk k "$GS" \
  's/var outcome = await AddOneAsync\(gid, iid, scope\);/var outcome = AddOutcome.Added; await _galleries.UpdateOneAsync(Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid), Builders<GalleryDoc>.Update.Push(g => g.Items, iid));/' \
  'FullyQualifiedName~GalleryServiceTests.Add_40_then_40_with_5_overlapping_counts_exactly_and_keeps_the_given_order' 'bulk add appends without the single-item gate'

brk l src/Gallery.Web/Components/Atoms/Switch.razor \
  's/checked="\@Checked"/checked="\@(Checked ? "checked" : "False")"/' \
  'FullyQualifiedName~LibraryComponentAdditionsTests.Switch_Checked_false_renders_no_checked_attribute_at_all' 'switch renders checked="False"'

brk m src/Gallery.Web/Components/Templates/PublicLayout.razor \
  's/<\/body>/<script src="\/admin-assets\/library.js"><\/script>\n<\/body>/' \
  'FullyQualifiedName~LibrarySourceGuardTests' 'public layout loads the admin script'

# (n) p95.sh must find events in the evidence log (0 = format mismatch, not a fast app).
log=specs/009-library-galleries/evidence/library-timings.log
if [ ! -s "$log" ]; then
  echo "FAIL (n) $log missing"; fails=$((fails+1))
else
  zero=$(bash specs/009-library-galleries/evidence/p95.sh "$log" | grep -E '^(PASS|FAIL) SC-' | grep -c ' n=0 ')
  rows=$(bash specs/009-library-galleries/evidence/p95.sh "$log" | grep -cE '^(PASS|FAIL) SC-')
  [ "$zero" -eq 0 ] && [ "$rows" -eq 14 ] && echo "PASS (n) p95.sh finds non-zero n for all $rows rows in $log" \
    || { echo "FAIL (n) p95.sh reports n=0 for $zero of $rows rows"; fails=$((fails+1)); }
fi

dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
  && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
