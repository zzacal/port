#!/usr/bin/env bash
# 012 T030 non-vacuity battery: apply one deliberate break per [SAFETY] guard, rebuild, confirm the
# named test goes red ON ITS ASSERTION, restore from git and touch the file. Run from the repo root:
#   bash specs/012-production-infrastructure/evidence/non-vacuity.sh | tee specs/012-production-infrastructure/evidence/non-vacuity.txt
# Needs a clean tree for every file it breaks (it aborts otherwise) and builds into ./artifacts,
# never the owner's watch output.
# NV_DRYRUN=1: only check each substitution matches (temp copies, no build, no clean-tree check).
set -uo pipefail
export DOTNET_NOLOGO=1
ART="artifacts"
SLN="Gallery.slnx"
TESTS="tests/Gallery.Web.Tests"
fails=0

H=src/Gallery.Web/Hosting
FILES="$H/OriginGuardMiddleware.cs $H/HostingSetup.cs $H/ProductionGuard.cs .github/workflows/ci.yml"
if [ "${NV_DRYRUN:-0}" != 1 ]; then
  for f in $FILES; do
    git diff --quiet -- "$f" || { echo "ABORT $f has uncommitted changes"; exit 1; }
  done
fi

# brk <id> <file> <perl -0 substitution> <dotnet test filter> <description>
brk() {
  local id="$1" file="$2" expr="$3" filter="$4" what="$5"
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
      if echo "$msg" | grep -qE '[A-Za-z]+Exception:'; then
        echo "FAIL ($id) $what -> red on an EXCEPTION, not an assertion: $name | $msg"; fails=$((fails+1))
      else
        echo "PASS ($id) $what -> red: $name | $msg"
      fi
    else
      echo "FAIL ($id) $what -> guard stayed GREEN"; fails=$((fails+1))
    fi
  fi
  git checkout -q -- "$file" && touch "$file"   # touch: restored mtime can predate build output
}

T=Gallery.Web.Tests
brk a $H/OriginGuardMiddleware.cs 's/else if \(!CryptographicOperations\.FixedTimeEquals\(/else if (!true && !CryptographicOperations.FixedTimeEquals(/' \
  "FullyQualifiedName~$T.HostingGuardTests.Wrong_header_is_refused_403_and_logged_mismatch" 'FixedTimeEquals always true (any header passes)'
brk b $H/HostingSetup.cs 's/ForwardedForHeaderName = "CF-Connecting-IP"/ForwardedForHeaderName = "X-Forwarded-For"/' \
  "FullyQualifiedName~$T.HostingGuardTests.CF_Connecting_IP_behind_guard_is_the_logged_ip" 'ForwardedForHeaderName -> X-Forwarded-For'
brk c $H/ProductionGuard.cs 's/else if \(bucket!\.StartsWith\(DevTargetGuard\.DevBucketPrefix, StringComparison\.Ordinal\)\)/else if (bucket!.Length < 0)/' \
  "FullyQualifiedName~$T.ProductionGuardTests" 'ProductionGuard drops the dev-bucket rule'
brk d .github/workflows/ci.yml 's/(    runs-on: ubuntu-latest\n)/$1    environment: production\n/' \
  "FullyQualifiedName~$T.WorkflowGuardTests.Workflow_declares_no_environment" 'deploy job gets environment: production'
brk e .github/workflows/ci.yml 's/    needs: test\n//' \
  "FullyQualifiedName~$T.WorkflowGuardTests.Deploy_job_needs_test" 'deploy job loses needs: test'

if [ "${NV_DRYRUN:-0}" != 1 ]; then
  dotnet build "$SLN" -warnaserror --artifacts-path "$ART" -nologo -v q >/dev/null 2>&1 \
    && echo "restored: build clean" || { echo "FAIL restored build"; fails=$((fails+1)); }
fi
echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
