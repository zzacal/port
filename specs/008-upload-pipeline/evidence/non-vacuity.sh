#!/usr/bin/env bash
# T054 non-vacuity: for each 008 [SAFETY] guard, apply a deliberate break to the source, rebuild,
# and confirm the named test FAILS ON ITS ASSERTION (first error message printed), then restore the
# file from git and touch it. Run from the repo root on a clean tree for the touched files:
#   bash specs/008-upload-pipeline/evidence/non-vacuity.sh | tee specs/008-upload-pipeline/evidence/non-vacuity.txt
# Builds into ./artifacts (never the owner's watch output); starts no server on 5080/5081.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="${ART:-artifacts}"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

FILES="src/Gallery.Web/Upload/ContainerParser.cs src/Gallery.Web/Upload/UploadValidator.cs
src/Gallery.Web/Upload/UploadEndpoints.cs src/Gallery.Web/Storage/ImageStore.cs
src/Gallery.Web/Admin/Pages/UploadPage.razor src/Gallery.Web/Admin/AdminEndpoints.cs
src/Gallery.Web/Upload/ConfirmService.cs src/Gallery.Web/Data/Documents.cs
src/Gallery.Web/Seed/Seeder.cs src/Gallery.Web/wwwroot/admin-assets/upload.js"
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

brk a src/Gallery.Web/Upload/ContainerParser.cs \
  's/if \(itemType\.SequenceEqual\("Exif"u8\)\) return "AVIF item Exif";//' \
  'FullyQualifiedName~ContainerParserTests.Reader1_alone_rejects_gps_avif' 'reader 1 accepts an AVIF Exif item'

brk b src/Gallery.Web/Upload/ContainerParser.cs \
  's/if \(id\.SequenceEqual\("EXIF"u8\)\) return ParseResult\.Fail\("WebP EXIF chunk"\);//; s/if \(\(flags & FlagExif\) != 0\) return ParseResult\.Fail\("VP8X EXIF flag"\);//' \
  'FullyQualifiedName~ContainerParserTests.Reader1_alone_rejects_gps_webp' 'reader 1 ignores the VP8X EXIF flag and EXIF chunk'

brk c src/Gallery.Web/Upload/UploadValidator.cs \
  's/reason is null, r1\.Ok, r2\.IsValid,/reason is null, r1.Ok, true,/' \
  'FullyQualifiedName~UploadValidatorTests.Combined_rejects_gps_webp' 'reader 2 verdict replaced by true'

brk d src/Gallery.Web/Upload/UploadEndpoints.cs \
  's/(\.Set\("items\.\$\.heartbeatAt", d\.Now\)\);)\n\s*await d\.Images\.DeletePrefixAsync\(d\.Priv, StagingKeys\.AttemptPrefix\(batchId, item\.ItemId, item\.AttemptId\), d\.Log\);/$1/' \
  'FullyQualifiedName~UploadFlowTests.Rejects_gps_avif_stored_as_320_avif' 'rejection keeps the staged files'

brk e src/Gallery.Web/Storage/ImageStore.cs \
  's/(Protocol = _isHttps \? Protocol\.HTTPS : Protocol\.HTTP,)/$1\n                Headers = { CacheControl = "no-cache" },/' \
  'FullyQualifiedName~UploadStorageTests&FullyQualifiedName~Presign_signs_content_type_and_host_only' 'presign signs an extra Cache-Control header'

brk f src/Gallery.Web/Admin/Pages/UploadPage.razor \
  's/<upload-batch hidden /<upload-batch hidden data-k="localsecret" /' \
  'FullyQualifiedName~UploadSecretScanTests' 'upload page renders the storage secret'

brk g src/Gallery.Web/Admin/AdminEndpoints.cs \
  's/admin\.MapUpload\(\);/app.MapGroup("\/admin").MapUpload();/' \
  'FullyQualifiedName~UploadAccessTests' 'upload routes mapped outside the protected admin group'

brk h src/Gallery.Web/Storage/ImageStore.cs \
  's/ETagToMatch = etag,//' \
  'FullyQualifiedName~UploadConfirmTests.Reput_after_validation_publishes_nothing' 'confirm copies without if-match'

brk h2 src/Gallery.Web/Upload/ConfirmService.cs \
  's/(if \(results\.Any\(r => r\.Outcome == CopyOutcome\.PreconditionFailed\)\)\n\s*\{)/$1\n            await _timer.Time("db.image.upsert", () => UpsertImageAsync(item, v));/' \
  'FullyQualifiedName~UploadConfirmTests.Reput_after_validation_publishes_nothing' 'refused copy still creates the image'

brk i src/Gallery.Web/Upload/ConfirmService.cs \
  's/(await _timer\.Time\("db\.image\.upsert", \(\) => UpsertImageAsync\(item, v\)\);)/$1\n        await _imageDocs.Database.GetCollection<MongoDB.Bson.BsonDocument>("galleries").UpdateManyAsync(new MongoDB.Bson.BsonDocument("visibility", "public"), new MongoDB.Bson.BsonDocument("\$push", new MongoDB.Bson.BsonDocument("items", item.ItemId)));/' \
  'FullyQualifiedName~UploadConfirmTests.Public_pages_byte_identical_around_confirm' 'confirm adds the photo to a public gallery'

brk j src/Gallery.Web/Data/Documents.cs \
  's/(\[BsonIgnoreIfNull\] public string\? SeedKey    \{ get; set; \})/$1 = string.Empty;/' \
  'FullyQualifiedName~ImageDoc_without_seed_fields_serialises_no_seedKey|FullyQualifiedName~Seed_run_after_confirm_leaves_uploaded_images' 'uploaded images carry seedKey ""'

brk j2 src/Gallery.Web/Seed/Seeder.cs \
  's/PartialFilterExpression = Builders<BsonDocument>\.Filter\.Exists\("seedKey"\),//' \
  'FullyQualifiedName~Seed_run_after_confirm_leaves_uploaded_images|FullyQualifiedName~Confirm_after_a_seed_run_adds_both_images' 'seedKey index not partial (row 60)'

brk k src/Gallery.Web/Upload/UploadEndpoints.cs \
  's/public sealed record CompleteRequest\(string\? AttemptId, PlaceholderInput\? Placeholder, List<TimingInput>\? Timings\);/public sealed record CompleteRequest(string? AttemptId, PlaceholderInput? Placeholder, List<TimingInput>? Timings, CameraDoc? Camera = null);/; s/Camera = ToCameraDoc\(verdict\.Camera\),/Camera = body.Camera ?? ToCameraDoc(verdict.Camera),/' \
  'FullyQualifiedName~UploadFlowTests.Complete_ignores_page_camera_and_dimensions' 'complete trusts the page camera'

brk l src/Gallery.Web/wwwroot/admin-assets/upload.js \
  "s/headers: \{ 'Content-Type': contentType \}/headers: { 'Content-Type': contentType, 'x-amz-meta-a': '1' }/" \
  'FullyQualifiedName~UploadBrowserTests.Storage_PUTs_carry_only_content_type' 'storage PUT sends an extra x-amz header'

# (m) p95.sh must find events in the evidence log (0 = format mismatch, not a fast app).
log=specs/008-upload-pipeline/evidence/upload-timings.log
if [ ! -s "$log" ]; then
  echo "FAIL (m) $log missing"; fails=$((fails+1))
else
  zero=$(bash specs/008-upload-pipeline/evidence/p95.sh "$log" | grep -E 'SC-00(6|7|8)|SC-010' | grep -c ' n=0 ')
  [ "$zero" -eq 0 ] && echo "PASS (m) p95.sh finds non-zero n for SC-006/007/008/010 in $log" \
    || { echo "FAIL (m) p95.sh reports n=0 for $zero criteria"; fails=$((fails+1)); }
fi

dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
  && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
