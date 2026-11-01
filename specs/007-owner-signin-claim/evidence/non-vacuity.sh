#!/usr/bin/env bash
# T034 non-vacuity: for each [SAFETY] guard, apply a deliberate break to the source, rebuild, and
# confirm the named test FAILS ON ITS ASSERTION (the first error message is printed), then restore
# the file from git and touch it. Run from the repo root with a clean tree for the touched files:
#   bash specs/007-owner-signin-claim/evidence/non-vacuity.sh | tee specs/007-owner-signin-claim/evidence/non-vacuity.txt
# Builds into ./artifacts (never the owner's watch output) and never starts a server on 5080/5081.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
fails=0

for f in src/Gallery.Web/Data/OwnerStore.cs src/Gallery.Web/Admin/Auth/OidcEvents.cs \
         src/Gallery.Web/Admin/AdminEndpoints.cs src/Gallery.Web/Admin/Auth/AuthSetup.cs \
         src/Gallery.Web/Ui/UiEndpoints.cs src/Gallery.Web/Admin/AdminHeaders.cs; do
  git diff --quiet -- "$f" || { echo "ABORT $f has uncommitted changes"; exit 1; }
done

# break <id> <file> <perl -0 substitution> <test filter> <description>
brk() {
  local id="$1" file="$2" expr="$3" filter="$4" what="$5"
  perl -0pi -e "$expr" "$file"
  if git diff --quiet -- "$file"; then
    echo "FAIL ($id) break did not change $file — fix the break, not the guard"; fails=$((fails+1)); return
  fi
  if ! dotnet build Gallery.slnx --artifacts-path "$ART" -nologo -v q >/tmp/nv-build.txt 2>&1; then
    echo "FAIL ($id) broken build did not compile:"; grep -m3 'error' /tmp/nv-build.txt; fails=$((fails+1))
  else
    out=$(dotnet test tests/Gallery.Web.Tests --no-build --artifacts-path "$ART" --filter "$filter" 2>&1)
    if echo "$out" | grep -q '^Failed!'; then
      name=$(echo "$out" | grep -m1 -E '^\s+Failed ' | sed -E 's/^\s+Failed //; s/ \[.*//')
      msg=$(echo "$out" | grep -m1 -A2 'Error Message:' | tail -2 | tr -s ' ' | tr '\n' ' ' | sed 's/ Stack Trace:.*//' | cut -c1-220)
      if echo "$msg" | grep -qE '[A-Za-z]+Exception:'; then
        echo "FAIL ($id) $what -> red, but on an EXCEPTION, not an assertion: $name | $msg"; fails=$((fails+1))
      else
        echo "PASS ($id) $what -> red: $name | $msg"
      fi
    else
      echo "FAIL ($id) $what -> guard stayed GREEN ($(echo "$out" | grep -E '^Passed!' | cut -c1-80))"; fails=$((fails+1))
    fi
  fi
  git checkout -q -- "$file" && touch "$file"
}

brk a src/Gallery.Web/Data/OwnerStore.cs \
  's/Builders<BsonDocument>\.Filter\.Or\(.*?Exists\("claimedSubject", false\)\s*\)/Builders<BsonDocument>.Filter.Empty/s' \
  'FullyQualifiedName~ClaimRaceTests' 'claim filter without claimedSubject:null'

brk b src/Gallery.Web/Admin/Auth/OidcEvents.cs \
  's/context\.HandleResponse\(\);\s*context\.Response\.Redirect\("\/admin\/signin\?r=refused"\);//' \
  'FullyQualifiedName~RefusalTests.Refused_intruder_gets_no_session_and_site_doc_unchanged' 'refusal without HandleResponse'

brk c src/Gallery.Web/Admin/AdminEndpoints.cs \
  's/\n\s*\.AddEndpointFilter<AdminAntiforgeryFilter>\(\);/;/' \
  'FullyQualifiedName~AdminAccessTests.I4_WithSession_NoToken_POST_returns_400' 'admin group without the anti-forgery filter'

brk d src/Gallery.Web/Admin/Auth/AuthSetup.cs \
  's/"Microsoft\.AspNetCore\.Authentication\.SuppressAutoDefaultScheme", true\)/"Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", false)/' \
  'FullyQualifiedName~AdminAccessTests.I6_NoConfigHost_PublicPages_ByteIdentical_With_MintedSession' 'SuppressAutoDefaultScheme switched off'

brk e src/Gallery.Web/Admin/Auth/AuthSetup.cs \
  's/if \(storedOwner is null \|\| sub is null \|\| storedOwner != sub\)/if (sub is null)/' \
  'FullyQualifiedName~OwnerSessionTests.Out_of_band' 'OnValidatePrincipal without the owner comparison'

brk f src/Gallery.Web/Ui/UiEndpoints.cs \
  's/\.RequireAuthorization\("owner"\)//' \
  'FullyQualifiedName~UiOwnerOnlyTests' '/ui without RequireAuthorization'

brk g src/Gallery.Web/Admin/AdminHeaders.cs \
  's/\z/\n\/\/ claimedSubject written here\n/' \
  'FullyQualifiedName~OwnerWriteGuardTests' 'owner field named outside OwnerStore'

brk h src/Gallery.Web/Admin/Auth/AuthSetup.cs \
  's/(o\.Cookie\.Name = "__Host-af";)\s*o\.Cookie\.SecurePolicy = CookieSecurePolicy\.Always;/$1/' \
  'FullyQualifiedName~OwnerClaimTests' 'anti-forgery cookie without SecurePolicy=Always'

# (i) the p95 script must find callbacks in every log it is given (0 = format mismatch).
for log in specs/007-owner-signin-claim/evidence/sc006-callbacks.log specs/007-owner-signin-claim/evidence/live-run.log; do
  if [ ! -s "$log" ]; then echo "SKIP (i) $log not captured yet"; continue; fi
  n=$(bash specs/007-owner-signin-claim/evidence/p95.sh "$log" | sed -n 's/^callbacks=\([0-9]*\).*/\1/p')
  [ "${n:-0}" -gt 0 ] && echo "PASS (i) p95.sh finds $n callbacks in $log" || { echo "FAIL (i) p95.sh finds 0 callbacks in $log"; fails=$((fails+1)); }
done

# Restore: rebuild the unbroken tree so later runs do not test stale output.
dotnet build Gallery.slnx -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
  && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
