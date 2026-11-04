#!/usr/bin/env bash
# Probe: option surface of Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0 (sampling + log sampling).
set -e
rm -rf /tmp/otelprobe2 && mkdir /tmp/otelprobe2 && cd /tmp/otelprobe2
dotnet new web -o p >/dev/null
cd p
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.6.0 >/dev/null
dotnet build -o out >/dev/null
echo "== assemblies"; ls out | grep -i -E "opentelemetry|monitor|azure"
cat > /tmp/otelprobe2/Dump.cs <<'EOF'
EOF
mkdir -p /tmp/otelprobe2/d && cd /tmp/otelprobe2/d
dotnet new console >/dev/null
cat > Program.cs <<'EOF'
using System.Reflection;
var dir = args[0];
foreach (var f in Directory.GetFiles(dir, "Azure.Monitor.OpenTelemetry*.dll"))
{
    var asm = Assembly.LoadFrom(f);
    Type[] types;
    try { types = asm.GetExportedTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
    foreach (var t in types.Where(t => t.Name.EndsWith("Options")))
        foreach (var p in t.GetProperties())
            Console.WriteLine($"{t.FullName}.{p.Name}: {p.PropertyType.Name}");
}
EOF
dotnet run -- /tmp/otelprobe2/p/out 2>&1 | grep -v "Diagnostics\|Retry\|Transport\|Policy" | sort -u
