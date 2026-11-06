#!/usr/bin/env bash
# T001: record the pre-feature baseline (HEAD, full-suite counts, test file count).
set -u
cd "$(git rev-parse --show-toplevel)"
out=specs/013-by-camera/evidence/baseline.txt
{
  echo "HEAD $(git rev-parse HEAD)"
  echo "web test files: $(ls tests/Gallery.Web.Tests/*.cs | wc -l | tr -d ' ')"
} > "$out"
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts > /tmp/013-baseline-build.txt 2>&1
echo "build exit $?" >> "$out"
dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s > /tmp/013-baseline-test.txt 2>&1
echo "test exit $?" >> "$out"
grep -E '^(Passed!|Failed!|Skipped!)|Total tests|Passed:|Failed:|Skipped:' /tmp/013-baseline-test.txt >> "$out"
cat "$out"
