#!/usr/bin/env bash
# 012 Checkpoint A: each [SAFETY] mechanism named in tasks.md exists in the merged tree.
cd "$(git rev-parse --show-toplevel)" || exit 1
H=src/Gallery.Web/Hosting
fails=0
row() { if grep -qF -- "$2" "$3"; then echo "PASS $1: '$2' in $3"; else echo "FAIL $1: '$2' missing from $3"; fails=$((fails+1)); fi; }
row origin-compare     'CryptographicOperations.FixedTimeEquals' $H/OriginGuardMiddleware.cs
row origin-log         '"origin.refused'                         $H/OriginGuardMiddleware.cs
row origin-strip       'headers.Remove(HeaderName)'              $H/OriginGuardMiddleware.cs
row forwarded-ip       'ForwardedForHeaderName = "CF-Connecting-IP"' $H/HostingSetup.cs
row forwarded-limit    'ForwardLimit = 1'                        $H/HostingSetup.cs
row prod-bucket        'DevTargetGuard.DevBucketPrefix'          $H/ProductionGuard.cs
row prod-db            'DevTargetGuard.DevDatabaseSuffix'        $H/ProductionGuard.cs
row log-sampling       'EnableTraceBasedLogsSampler = false'     $H/HostingSetup.cs
row sampling           'SamplingRatio = 1.0F'                    $H/HostingSetup.cs
row wiring-add         'AddHosting(builder.Configuration)'       src/Gallery.Web/AppBuilder.cs
row wiring-use         'app.UseHosting();'                       src/Gallery.Web/AppBuilder.cs
# Literal dev markers must not be repeated (call, never copy).
if grep -qE '"gallery-dev-"|"_dev"' $H/*.cs; then echo "FAIL no-copy: dev literal repeated in Hosting/"; fails=$((fails+1)); else echo "PASS no-copy: no dev literal in Hosting/"; fi
# Guard runs before routing: UseHosting precedes UseRouting.
u=$(grep -n 'app.UseHosting();' src/Gallery.Web/AppBuilder.cs | cut -d: -f1); r=$(grep -n 'app.UseRouting();' src/Gallery.Web/AppBuilder.cs | cut -d: -f1)
if [ -n "$u" ] && [ -n "$r" ] && [ "$u" -lt "$r" ]; then echo "PASS order: UseHosting (line $u) before UseRouting (line $r)"; else echo "FAIL order"; fails=$((fails+1)); fi
echo "RESULT: $fails FAIL"
exit $((fails > 0))
