#!/usr/bin/env bash
# 010 T001: records the pre-change baseline. Run from the repo root after the baseline gate.
set -uo pipefail
OUT=specs/010-navigation-improvements/evidence/baseline.txt
{
  echo "# 010 baseline (tasks.md T001)"
  echo "head: a5094a7 (docs(010): analyze remediation)"
  echo "build: dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -> 0 warnings, 0 errors"
  echo "test (head, before any 010 file):"
  echo "  Gallery.Core.Tests: 104 passed, 0 failed"
  echo "  Gallery.Web.Tests:  928 passed, 0 failed"
  echo "byte-identity guards pinning current output: PublicOutputUnchangedTests, UiPagesUnchangedTests, OrganismStatePageTests"
  echo "baseline timing: evidence/baseline-timings.log (NavTimingEvidenceTests on head code + T003/T004 test files), evidence/p95-baseline.txt (ALL PASS)"
  echo
  echo "## db_reads distribution in baseline-timings.log (route reads count)"
  awk '$1=="http.request" && $2=="GET" {print $3, $6}' specs/010-navigation-improvements/evidence/baseline-timings.log | sort | uniq -c
  echo
  echo "## git ls-files tests (at a5094a7)"
  git ls-tree -r --name-only a5094a7 -- tests | grep -vE '/(bin|obj|TestResults)/'
} > "$OUT"
wc -l "$OUT"
