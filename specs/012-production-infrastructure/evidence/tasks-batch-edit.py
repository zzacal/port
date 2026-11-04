"""Restructure Phase 2 into A1 (code) -> T012 wiring (parent) -> A2 (tests). Asserts counts."""
import pathlib, sys
p = pathlib.Path(__file__).resolve().parents[1] / "tasks.md"
s = p.read_text()
rows = [
("""**Batch A**: one executor. It owns `src/Gallery.Web/Hosting/HostingOptions.cs`,
`src/Gallery.Web/Hosting/HostingSetup.cs`, `src/Gallery.Web/Hosting/OriginGuardMiddleware.cs`,
`src/Gallery.Web/Hosting/ProductionGuard.cs`, and the new test files
`tests/Gallery.Web.Tests/HostingGuardTests.cs`, `ProductionGuardTests.cs`,
`HostingDataProtectionTests.cs` and `HostingTelemetryTests.cs`.
""",
"""**Batch A1**: one executor. It owns `src/Gallery.Web/Hosting/HostingOptions.cs`,
`src/Gallery.Web/Hosting/HostingSetup.cs`, `src/Gallery.Web/Hosting/OriginGuardMiddleware.cs` and
`src/Gallery.Web/Hosting/ProductionGuard.cs`. Gate: build `-warnaserror` and the full suite still
green (nothing is wired yet).
""", 1),
("""- [ ] T008 [SAFETY] `tests/Gallery.Web.Tests/HostingGuardTests.cs`""",
"""**Parent wiring (after Batch A1)**:

- [ ] T012 Edit `src/Gallery.Web/AppBuilder.cs`:
  - add `builder.Services.AddHosting(builder.Configuration);` on the line after `AddEdge(...)`
  - add `app.UseHosting();` as the first line after `var app = builder.Build();`, before `UseRouting()`
  - no other change
  Run the full suite: existing tests unmodified, count equals T001.

**Batch A2**: one executor, after T012. It owns the new test files
`tests/Gallery.Web.Tests/HostingGuardTests.cs`, `ProductionGuardTests.cs`,
`HostingDataProtectionTests.cs` and `HostingTelemetryTests.cs`. It may fix defects its tests find
in the four `Hosting/` files (report each in `decisions_made`); it never edits AppBuilder.cs.

- [ ] T008 [SAFETY] `tests/Gallery.Web.Tests/HostingGuardTests.cs`""", 1),
("""**Parent wiring (after T004–T007 compile)**:

- [ ] T012 Edit `src/Gallery.Web/AppBuilder.cs`:
  - add `builder.Services.AddHosting(builder.Configuration);` on the line after `AddEdge(...)`
  - add `app.UseHosting();` as the first line after `var app = builder.Build();`, before `UseRouting()`
  - no other change
  Then run T008–T011 and the full suite (existing tests unmodified, count = T001 + new).

""", "", 1),
("- T001 → T002 → T003 → Batch A (T004–T011) → T012 → Checkpoint A.",
 "- T001 → T002 → T003 → Batch A1 (T004–T007) → T012 → Batch A2 (T008–T011) → Checkpoint A.", 1),
]
bad = [(o[:50], s.count(o)) for o, _, n in rows if s.count(o) != n]
if bad: print("ABORT", bad); sys.exit(1)
for o, n, _ in rows: s = s.replace(o, n)
p.write_text(s); print("OK", len(rows))
