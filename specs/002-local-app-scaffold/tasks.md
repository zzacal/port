# Tasks: Local App Scaffold

**Input**: Design documents from `/specs/002-local-app-scaffold/`
**Prerequisites**: plan.md, spec.md, research.md, quickstart.md

**Tests**: No new tests are written. The spec only requires the template-generated tests to run
(US2), so there are no test-authoring tasks.

**Scope guard (applies to every task)**: Only the `dotnet` CLI commands listed here, run from the
repo root `/Users/zzacal/repos/gallery`. Do NOT run `dotnet add package`, do NOT create
docker-compose, `.editorconfig`, `Directory.Build.props`, README changes, or any source file, and
do NOT edit template output, except for the single port value in T010 and only if it is 5000
(spec FR-005, FR-006). Set `DOTNET_NOLOGO=1` for every command.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (US1, US2)

---

## Phase 1: Setup

**Purpose**: Confirm the SDK, pin it, and create the repository-level generated files.

- [ ] T001 Verify `dotnet --list-sdks` includes `10.0.403`. If it doesn't, stop and report; do not install anything.
- [ ] T002 Create `global.json` at the repo root with `dotnet new globaljson --sdk-version 10.0.403 --roll-forward latestPatch`, then confirm `dotnet --version` prints `10.0.403` (research R-01).
- [ ] T003 Regenerate `.gitignore` at the repo root, keeping the existing entry: `cp .gitignore /tmp/gallery.gitignore.bak && dotnet new gitignore --force && cat /tmp/gallery.gitignore.bak >> .gitignore`. Then confirm `.gitignore` still contains `.specify/feature.json` and now contains `[Bb]in/` and `[Oo]bj/` (research R-03).
- [ ] T004 Create `Gallery.slnx` at the repo root with `dotnet new sln --format slnx -n Gallery`.

---

## Phase 2: Foundational

**Purpose**: Create the class library, which both stories reference.

- [ ] T005 Create `src/Gallery.Core/Gallery.Core.csproj` with `dotnet new classlib -o src/Gallery.Core -n Gallery.Core`. Leave the generated `Class1.cs` in place.

**Checkpoint**: The solution and Core exist. US1 and US2 can proceed.

---

## Phase 3: User Story 1 - The projects exist and the app runs (Priority: P1) 🎯 MVP

**Goal**: The web app exists, references Core, builds cleanly, and serves the template response.

**Independent Test**: `dotnet build Gallery.slnx -warnaserror` gives 0 errors and 0 warnings; `dotnet run --project src/Gallery.Web` serves "Hello World!" at the printed address.

- [ ] T006 [US1] Create `src/Gallery.Web/Gallery.Web.csproj` with `dotnet new web -o src/Gallery.Web -n Gallery.Web --no-https`.
- [ ] T007 [US1] Add both src projects to the solution under a `src` folder: `dotnet sln Gallery.slnx add --solution-folder src src/Gallery.Web src/Gallery.Core`.
- [ ] T008 [US1] Add the Web → Core reference: `dotnet add src/Gallery.Web reference src/Gallery.Core`.
- [ ] T009 [US1] Confirm Core references no project: `dotnet list src/Gallery.Core reference` prints "There are no Project to Project references" (FR-002).
- [ ] T010 [US1] Read `applicationUrl` in `src/Gallery.Web/Properties/launchSettings.json`. If its port is 5000, change only that port number to 5090. Otherwise make no edit, and record the port the template chose.
- [ ] T011 [US1] Run `dotnet build Gallery.slnx -warnaserror` and require 0 errors and 0 warnings (SC-001).
- [ ] T012 [US1] Run the app in the background (`dotnet run --project src/Gallery.Web`), request the address from T010 with `curl -s`, and require the body `Hello World!` (SC-004). Stop the process afterwards.

**Checkpoint**: US1 is done and the app runs.

---

## Phase 4: User Story 2 - The test projects run with one command (Priority: P2)

**Goal**: Two test projects exist, reference the right projects, and `dotnet test` runs both.

**Independent Test**: `dotnet test` at the repo root reports both test assemblies, all passing.

- [ ] T013 [P] [US2] Create `tests/Gallery.Core.Tests/Gallery.Core.Tests.csproj` with `dotnet new xunit -o tests/Gallery.Core.Tests -n Gallery.Core.Tests`.
- [ ] T014 [P] [US2] Create `tests/Gallery.Web.Tests/Gallery.Web.Tests.csproj` with `dotnet new xunit -o tests/Gallery.Web.Tests -n Gallery.Web.Tests`.
- [ ] T015 [US2] Add both test projects to the solution under a `tests` folder: `dotnet sln Gallery.slnx add --solution-folder tests tests/Gallery.Core.Tests tests/Gallery.Web.Tests` (depends on T013, T014).
- [ ] T016 [US2] Add references: `dotnet add tests/Gallery.Core.Tests reference src/Gallery.Core`, then `dotnet add tests/Gallery.Web.Tests reference src/Gallery.Web src/Gallery.Core` (depends on T006, T015).
- [ ] T017 [US2] Run `dotnet test` at the repo root and require both `Gallery.Core.Tests` and `Gallery.Web.Tests` to be reported with all tests passing (SC-002).

**Checkpoint**: US2 is done.

---

## Phase 5: Polish & Verification

**Purpose**: Prove the success criteria and that nothing beyond scope was added.

- [ ] T018 Verify no packages were added: in `src/Gallery.Web/Gallery.Web.csproj` and `src/Gallery.Core/Gallery.Core.csproj`, `grep -c PackageReference` must be 0. In both test `.csproj` files, the only `PackageReference` entries must be the four template ones (coverlet.collector, Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio), per research R-02 (FR-005).
- [ ] T019 Verify SC-003: copy the repo to a temp directory (excluding `bin/` and `obj/`), set `global.json` `version` to `10.0.404` with `dotnet new globaljson --sdk-version 10.0.404 --roll-forward latestPatch --force`, run `dotnet build`, and require it to fail with text containing `Requested SDK version: 10.0.404`. Delete the temp copy.
- [ ] T020 Verify SC-001: re-run the quickstart.md scaffolding commands in an empty temp directory, then run `dotnet build Gallery.slnx -warnaserror` there and require 0 errors and 0 warnings. Delete the temp directory.
- [ ] T021 Record the evidence: write the outputs of T011, T012, T017, T019 and T020 to `specs/002-local-app-scaffold/evidence/verify-output.txt`.
- [ ] T022 Commit the scaffold, then `git status --short` must show no `bin/` or `obj/` paths. Push the branch `002-local-app-scaffold`.

---

## Dependencies & Execution Order

- Setup (T001–T004), then Foundational (T005), then US1 (T006–T012) and US2 (T013–T017), then Polish (T018–T022).
- US2's T016 needs T006 (Gallery.Web must exist before Web.Tests can reference it). Apart from that, US2 does not depend on US1.
- Within US1, tasks run in order. In US2, T013 and T014 can run in parallel; the rest are sequential.
- `dotnet sln add` and `dotnet add reference` both edit shared files (`Gallery.slnx`, `.csproj`), so they are never parallel.

## Parallel Example: User Story 2

```bash
dotnet new xunit -o tests/Gallery.Core.Tests -n Gallery.Core.Tests &
dotnet new xunit -o tests/Gallery.Web.Tests  -n Gallery.Web.Tests  &
wait
```

## Implementation Strategy

- **MVP**: Phases 1–3 (through T012). The solution builds and the app runs.
- Then Phase 4 for the test projects, then Phase 5 for verification and the commit.
- Every task is a single CLI command plus its check, so the whole feature is mechanical and fits
  one executor pass or a direct run. There are 22 tasks.
