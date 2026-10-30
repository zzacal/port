# Implementation Plan: Local App Scaffold

**Branch**: `002-local-app-scaffold` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-local-app-scaffold/spec.md`

## Summary

Create `Gallery.slnx` and the four D-22 projects with `dotnet` CLI commands only, pin the SDK to
10.0.403, and add a generated `.gitignore`. No packages beyond template output, no services, and
no code beyond what the templates generate.

## Technical Context

**Language/Version**: C# on .NET 10, SDK 10.0.403 (latest released, research R-01)

**Primary Dependencies**: none added. Test projects carry the bundled `xunit` template's own
packages (R-02).

**Storage**: N/A

**Testing**: the template-generated xUnit test in each test project

**Target Platform**: macOS for local development

**Project Type**: web app plus class library

**Performance Goals**: N/A (no feature behaviour)

**Constraints**: CLI-only generation, zero build warnings, no port 5000

**Scale/Scope**: 4 projects, 3 project references, 0 added packages

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I–III | PASS (justified N/A) | No identity, galleries, or images. |
| IV. Performance | PASS (justified N/A, with a carry-forward) | The template's single "Hello World" endpoint is a placeholder, not a feature, so there is nothing to instrument yet. **Carry-forward**: the first feature that adds a real endpoint MUST add the IV.1–IV.3 request timing and Server-Timing as part of its own scope. |
| V.1 scope | PASS | FR-005 and FR-006: projects only. |
| V.2 dependencies | PASS | Zero packages added. Template packages are listed in R-02. |
| V.3 no JS on public pages | PASS | No pages. |
| VI. CLI scaffolding | PASS | quickstart.md is pure CLI. The only possible hand edit is one port value, and only if the template picks 5000. |
| VII. Design before code | PASS | P-01 becomes D-22 (R-04). |
| VIII. Cost | PASS (justified N/A) | Local only. |

No violations, so Complexity Tracking is empty. Post-design re-check: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/002-local-app-scaffold/
├── plan.md
├── research.md
├── quickstart.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

There is no data-model.md or contracts/: the feature has no entities and no interface beyond the
template's default response.

### Source Code (repository root)

```text
global.json             dotnet new globaljson
.gitignore              dotnet new gitignore (+ the prior entry re-appended)
Gallery.slnx            dotnet new sln --format slnx
src/
├── Gallery.Web/        dotnet new web --no-https
└── Gallery.Core/       dotnet new classlib
tests/
├── Gallery.Core.Tests/ dotnet new xunit   → references Gallery.Core
└── Gallery.Web.Tests/  dotnet new xunit   → references Gallery.Web, Gallery.Core
```

**Structure Decision**: The D-22 layout, with template output left as generated.

## Complexity Tracking

No entries.
