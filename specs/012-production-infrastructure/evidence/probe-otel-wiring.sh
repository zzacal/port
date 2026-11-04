#!/usr/bin/env bash
# Probe 2: the planned Azure Monitor wiring compiles on net10 and starts with NO connection string
# (local/test hosts) and WITH a syntactically valid fake one (hosted shape), logging an ILogger event.
set -e
P=/tmp/otelprobe3
rm -rf $P && mkdir $P && cd $P
cp /Users/zzacal/repos/gallery/global.json .
dotnet new web -o p >/dev/null
cd p
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.6.0 >/dev/null
cat > Program.cs <<'EOF'
using Azure.Monitor.OpenTelemetry.AspNetCore;
var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(cs))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
    {
        o.ConnectionString = cs;
        o.SamplingRatio = 1.0F;
        o.TracesPerSecond = null;
        o.EnableTraceBasedLogsSampler = false;
        o.EnableLiveMetrics = false;
    });
}
var app = builder.Build();
app.MapGet("/", (ILogger<Program> log) => { log.LogInformation("op.timing {op} {duration_ms} {outcome}", "probe", 1.5, "ok"); return "ok"; });
app.Run();
EOF
dotnet build -o out 2>&1 | grep -E "error|Warn|Build succeeded" | head -5
for mode in none fake; do
  if [ $mode = fake ]; then export APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/"; else unset APPLICATIONINSIGHTS_CONNECTION_STRING; fi
  dotnet out/p.dll --urls http://127.0.0.1:5199 > run-$mode.log 2>&1 &
  pid=$!
  for i in $(seq 1 40); do curl -s -o /dev/null http://127.0.0.1:5199/ && break; sleep 0.25; done
  echo "mode=$mode GET / -> $(curl -s http://127.0.0.1:5199/)"
  kill $pid; wait $pid 2>/dev/null || true
done
echo "probe done"
