#!/usr/bin/env bash
# 012 T027 full gate: clean build -warnaserror, full suite, and "existing tests unmodified except T003".
cd /Users/zzacal/repos/gallery || exit 1
base=1af9ffd481432acdc5b9bd249642432d883aee9c   # T001 HEAD (baseline.txt)
echo "== dotnet clean"; dotnet clean Gallery.slnx --artifacts-path artifacts -nologo -v q >/dev/null 2>&1; echo "exit $?"
echo "== build -warnaserror"; dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -nologo 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
echo "== full suite"; dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s 2>&1 | grep -E '^(Passed|Failed)!|^\s+Failed '
echo "== modified existing test files since $base (must be only UploadSourceGuardTests.cs):"
git diff --name-status "$base" -- tests/ | grep -v '^A' || true
echo "== added test files:"; git diff --name-status "$base" -- tests/ | grep '^A' || true
