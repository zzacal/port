#!/usr/bin/env bash
# 018 T019 non-vacuity battery (from 017's): one deliberate break per guard; rebuild; the named test
# must go red ON ITS ASSERTION; restore + touch. Run from the repo root:
#   bash specs/018-local-dev-signin/evidence/non-vacuity.sh | tee specs/018-local-dev-signin/evidence/non-vacuity.txt
# NV_DRYRUN=1: only check that each substitution matches (temp copies, no build, no clean-tree check).
# NV_ONLY="a b": run only those break ids.
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

LS=src/Gallery.Web/Admin/Auth/LocalSignIn.cs
PG=src/Gallery.Web/Hosting/ProductionGuard.cs
EP=src/Gallery.Web/Admin/AdminEndpoints.cs
FILES="$LS $PG $EP"
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

# [SAFETY I.7a] Evaluate skips the development-host check.
brk devhost $LS 's/if \(DevTargetGuard\.Check\(environmentName, storage, mongo\)\.Count != 0\)/if (false)/' \
  'FullyQualifiedName~LocalSignInRuleTests.Evaluate_follows_the_truth_table|FullyQualifiedName~LocalSignInGuardTests.Disabled_host_has_no_local_form_no_route_no_session' \
  'Evaluate ignores DevTargetGuard'
# [SAFETY I.7a] the minimum length drops to 1.
brk minlen $LS 's/public const int MinLength = 16;/public const int MinLength = 1;/' \
  'FullyQualifiedName~LocalSignInRuleTests.Evaluate_follows_the_truth_table|FullyQualifiedName~LocalSignInGuardTests.Disabled_host_has_no_local_form_no_route_no_session' \
  'MinLength 16 -> 1'
# [SAFETY I.7c] the ProductionGuard rule is gone.
brk prodguard $PG 's/if \(c\[LocalSignIn\.PasswordKey\] is not null\)\n\s+failed\.Add\(\$"\{LocalSignIn\.PasswordKey\} must not be set"\);\n//' \
  'FullyQualifiedName~LocalSignInRuleTests.ProductionGuard_refuses_the_key_naming_it_never_the_value|FullyQualifiedName~LocalSignInGuardTests.Production_target_host_with_the_key_refuses_to_build_naming_key_not_value' \
  'production accepts LocalSignIn:Password'
# [SAFETY I.7b] Matches accepts anything.
brk matches $LS 's/return CryptographicOperations\.FixedTimeEquals\(candidate, _hash\);/return true;/' \
  'FullyQualifiedName~LocalSignInTests.Bad_password_answers_401_with_notice_and_no_session' \
  'Matches returns true'
# [SAFETY I.7a] H7 mapped on every host.
brk unmapped $EP 's/if \(local\.Enabled\)\n(\s+app\.MapPost\("\/admin\/signin\/local")/$1/' \
  'FullyQualifiedName~LocalSignInGuardTests.Disabled_host_has_no_local_form_no_route_no_session' \
  'H7 mapped without if (local.Enabled)'
# [SAFETY I.6] H7 skips the forgery check.
brk csrf $EP 's/if \(!await antiforgery\.IsRequestValidAsync\(ctx\)\)\n(\s+\{\n\s+logger\.LogInformation\("auth\.local\.refused)/if (false)\n$1/' \
  'FullyQualifiedName~LocalSignInTests.No_antiforgery_token_answers_400_with_notice_and_no_session' \
  'H7 skips the antiforgery check'
# [SAFETY I.7d] H7 signs in as local-owner without reading the stored owner.
brk owner $EP 's/var sub = await ownerStore\.ReadOwnerAsync\(\);/string? sub = LocalSignIn.LocalOwner;/' \
  'FullyQualifiedName~LocalSignInTests.Right_password_on_claimed_site_signs_in_as_the_stored_owner' \
  'H7 ignores the stored owner'
# [SAFETY I.7b] the refusal log line carries the submitted password.
brk leak $EP 's/logger\.LogInformation\("auth\.local\.refused \{ip\} \{reason\}", ip, "bad_password"\);/logger.LogInformation("auth.local.refused {ip} {reason} {pw}", ip, "bad_password", form["password"].ToString());/' \
  'FullyQualifiedName~LocalSignInTests.No_response_or_log_line_contains_the_configured_or_a_submitted_password' \
  'auth.local.refused logs the submitted password'

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
