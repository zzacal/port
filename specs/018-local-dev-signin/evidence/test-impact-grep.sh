#!/usr/bin/env bash
# 018 test-impact grep (contracts/test-impact.md header). Source-only; read-only.
cd "$(git rev-parse --show-toplevel)"
g() { grep -rn --include='*.cs' --include='*.razor' --exclude-dir=bin --exclude-dir=obj --exclude-dir=artifacts "$@" tests/; }
for p in 'signin/local' 'LocalSignIn' 'local-owner' 'type=\\"password\\"' 'Password' 'EndpointDataSource' 'RouteEndpoint' "isn't set up" 'si-notice' 'Sign in with Google' 'DevTargetGuard' 'appsettings' 'si-local' 'auth\.local'; do
  echo "== $p"
  g -e "$p" | grep -v '/LocalSignIn[A-Za-z]*Tests\.cs:' | cut -c1-200
done
