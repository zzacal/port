#!/usr/bin/env bash
# Verification for 002-local-app-scaffold (tasks T011, T017-T020). Run from the repo root.
# Usage: bash specs/002-local-app-scaffold/evidence/verify.sh | tee specs/002-local-app-scaffold/evidence/verify-output.txt
set -uo pipefail
export DOTNET_NOLOGO=1
ROOT="$(pwd)"
fails=0
pass() { echo "PASS $1 $2"; }
fail() { echo "FAIL $1 $2"; fails=$((fails+1)); }

echo "sdk: $(dotnet --version)"

# T011 / SC-001 (in place)
out=$(dotnet build Gallery.slnx -warnaserror 2>&1)
echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
  && pass SC-001 "build: 0 warnings, 0 errors" || fail SC-001 "build: $(echo "$out" | tail -3 | tr '\n' ' ')"

# T017 / SC-002
out=$(dotnet test 2>&1)
n=$(echo "$out" | grep -c '^Passed!')
echo "$out" | grep -q 'Gallery.Core.Tests.dll' && echo "$out" | grep -q 'Gallery.Web.Tests.dll' && [ "$n" = 2 ] \
  && pass SC-002 "both test assemblies passed" || fail SC-002 "passed-lines=$n"

# T018 / FR-005: no added packages
for p in src/Gallery.Web/Gallery.Web.csproj src/Gallery.Core/Gallery.Core.csproj; do
  c=$(grep -c PackageReference "$p" || true)
  [ "$c" = 0 ] && pass FR-005 "$p has 0 PackageReference" || fail FR-005 "$p has $c PackageReference"
done
expected="coverlet.collector Microsoft.NET.Test.Sdk xunit xunit.runner.visualstudio"
for p in tests/Gallery.Core.Tests/Gallery.Core.Tests.csproj tests/Gallery.Web.Tests/Gallery.Web.Tests.csproj; do
  got=$(grep -oE 'PackageReference Include="[^"]+"' "$p" | sed -E 's/.*"(.*)"/\1/' | sort | tr '\n' ' ' | sed 's/ $//')
  [ "$got" = "$expected" ] && pass FR-005 "$p template packages only" || fail FR-005 "$p has: $got"
done

# FR-002: Core references nothing
dotnet list src/Gallery.Core reference | grep -q 'no Project to Project references' \
  && pass FR-002 "Gallery.Core has no project references" || fail FR-002 "Gallery.Core has references"

# T019 / SC-003: an SDK pin that isn't installed must fail and name the version
tmp=$(mktemp -d)
rsync -a --exclude bin --exclude obj --exclude .git "$ROOT/" "$tmp/"
( cd "$tmp" && dotnet new globaljson --sdk-version 10.0.404 --roll-forward latestPatch --force >/dev/null 2>&1 )
out=$(cd "$tmp" && dotnet build 2>&1)
echo "$out" | grep -q 'Requested SDK version: 10.0.404' \
  && pass SC-003 "older/missing SDK rejected, names 10.0.404" || fail SC-003 "$(echo "$out" | head -3 | tr '\n' ' ')"
rm -rf "$tmp"

# T020 / SC-001 (fresh re-run of the quickstart scaffolding)
tmp=$(mktemp -d)
(
  cd "$tmp" && set -e
  dotnet new globaljson --sdk-version 10.0.403 --roll-forward latestPatch
  dotnet new gitignore
  dotnet new sln --format slnx -n Gallery
  dotnet new web      -o src/Gallery.Web            -n Gallery.Web --no-https
  dotnet new classlib -o src/Gallery.Core           -n Gallery.Core
  dotnet new xunit    -o tests/Gallery.Core.Tests   -n Gallery.Core.Tests
  dotnet new xunit    -o tests/Gallery.Web.Tests    -n Gallery.Web.Tests
  dotnet sln Gallery.slnx add --solution-folder src   src/Gallery.Web src/Gallery.Core
  dotnet sln Gallery.slnx add --solution-folder tests tests/Gallery.Core.Tests tests/Gallery.Web.Tests
  dotnet add src/Gallery.Web          reference src/Gallery.Core
  dotnet add tests/Gallery.Core.Tests reference src/Gallery.Core
  dotnet add tests/Gallery.Web.Tests  reference src/Gallery.Web src/Gallery.Core
) >/dev/null 2>&1
out=$(cd "$tmp" && dotnet build Gallery.slnx -warnaserror 2>&1)
echo "$out" | grep -qE '^ +0 Warning\(s\)' && echo "$out" | grep -qE '^ +0 Error\(s\)' \
  && pass SC-001 "fresh scaffold re-run builds: 0 warnings, 0 errors" || fail SC-001 "fresh: $(echo "$out" | tail -3 | tr '\n' ' ')"
rm -rf "$tmp"

echo "---"
echo "failures: $fails"
exit $fails
