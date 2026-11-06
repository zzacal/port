#!/usr/bin/env bash
# 013 T033 non-vacuity battery (from speckit-orchestration templates/non-vacuity.sh): one deliberate
# break per [SAFETY] guard; rebuild; the named test must go red ON ITS ASSERTION; restore + touch.
# Run from the repo root:
#   bash specs/013-by-camera/evidence/non-vacuity.sh | tee specs/013-by-camera/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree check).
# NV_ONLY="a b": run only those break ids.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

P=src/Gallery.Web/Library/SiteProjector.cs
IS=src/Gallery.Web/Library/ImageService.cs
EP=src/Gallery.Web/Site/PublicEndpoints.cs
PS=src/Gallery.Web/Edge/PurgeSets.cs
OR=src/Gallery.Web/Library/OperationResult.cs
IC=tests/Gallery.Web.Tests/Fixtures/InvariantChecker.cs
FILES="$P $IS $EP $PS $OR $IC"
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
      if echo "$msg" | grep -qE '[A-Za-z]+Exception:' && ! echo "$msg" | grep -qE 'InvariantViolation|Assert\.|XunitException'; then
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

# Batch A guards (T007, T008)
brk vis  $P 's/\n\s*if \(!visible\.Contains\(image\.Id\)\) continue;//' \
  'FullyQualifiedName~CameraProjectionTests' 'Cameras() ignores visibility (every gallery counts)'
brk tomb $P 's/\n\s*if \(image\.DeletingAt is not null\) continue;//' \
  'FullyQualifiedName~CameraProjectionTests' 'Cameras() ignores DeletingAt'
brk writer $IS 's/\}\s*\z/}\n\nfile static class NvCameraWriter\n{\n    public static Task W(IMongoDatabase db) => db.GetCollection<CameraGroupDoc>("cameras").DeleteManyAsync(FilterDefinition<CameraGroupDoc>.Empty);\n}\n/' \
  'FullyQualifiedName~CameraSourceGuardTests' 'a cameras write planted in ImageService.cs'

# Batch B guards (T016, T017)
brk c2nf $EP 's/if \(camera is null \|\| camera\.Items\.Count == 0\)\n(\s*)return NotFoundResult\(\);/camera ??= new CameraGroupDoc { Name = slug };/' \
  'FullyQualifiedName~CameraRoutesTests.A_private_only_camera' 'C2 drops its not-found branch (private-only slug renders)'
brk c1read $EP 's/(var docs = await reader\.CamerasAsync\(\);)/$1\n                _ = await reader.ImagesAsync(docs.Select(d => d.Card.Cover.ImageId).ToList(), full: false);/' \
  'FullyQualifiedName~CameraRoutesTests.Camera_routes_keep_their_exact_read_budget' 'an extra ImagesAsync read in C1'
# Batch C guards (T022, T023, InvariantChecker)
brk slug $PS 's/foreach \(var c in new\[\] \{ before, after \}\)/foreach (var c in new[] { after })/' \
  'FullyQualifiedName~CameraPurgeSetTests' 'PurgeSets.Cameras drops the before-slug side'
brk scope $OR 's/if \(camChanges\.Count > 0\)\n(\s*)sets\.Add\(PurgeSets\.Cameras\(/if (camChanges.Count < 0)\n$1sets.Add(PurgeSets.Cameras(/' \
  'FullyQualifiedName~CameraFreshnessTests' 'the scope skips PurgeSets.Cameras'
brk inv $IC 's/var expectedCameras = new BsonArray\(SiteProjector\.Cameras\(listed, images,/var expectedCameras = new BsonArray(SiteProjector.Cameras([], images,/' \
  'FullyQualifiedName~CameraFreshnessTests' 'the InvariantChecker camera rule compares against an empty projection'
# Batch D guard (T027): added after Batch D lands.

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
