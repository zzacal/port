# Feature Specification: Local App Scaffold

**Feature Branch**: `002-local-app-scaffold`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "create the projects to run the app and use dotnet cli commands instead of writing out the files yourself. make sure we have the latest dotnet that is in release. if you don't have the latest dotnet, let me know."

## Clarifications

### Session 2026-10-27

- Q: What does "the projects" include? → A: The projects only. No added dependencies: no
  packages beyond what the official templates generate, no supporting services, no health checks,
  no test containers. (This cut a first draft that also had local services, a health address,
  request timing, and an integration-test harness. Those move to the features that need them.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The projects exist and the app runs (Priority: P1)

The owner clones the repository, builds the solution, and runs the web app. A browser at the
app's local address shows the template's default response.

**Why this priority**: Every later feature is built inside these projects.

**Independent Test**: On a machine with only the pinned SDK installed, build the solution, run the
web app, and load its local address.

**Acceptance Scenarios**:

1. **Given** a fresh clone and the pinned SDK, **When** the owner builds the solution, **Then** it
   builds with zero errors and zero warnings.
2. **Given** a successful build, **When** the owner runs the web app, **Then** its local address
   returns the template's default response.

---

### User Story 2 - The test projects run with one command (Priority: P2)

The owner runs one command at the repository root, and both test projects build and run their
template-generated tests.

**Why this priority**: Later features add tests into these projects; the command has to work first.

**Independent Test**: Run the test command at the repository root and confirm both test projects
are discovered and pass.

**Acceptance Scenarios**:

1. **Given** a successful build, **When** the owner runs the test command, **Then** both test
   projects run and pass.

---

### Edge Cases

- The installed SDK is older than the pinned release: the build refuses to run and names the
  required version.
- Only a preview or release-candidate SDK of a newer major version is installed: it does not
  satisfy the pin.
- The template assigns port 5000 to the web app: that port is taken by macOS AirPlay, so the
  launch address is changed to a free port.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain one solution with four projects: one deployable web app,
  one class library, and two test projects (one for the library, one for the web app).
- **FR-002**: Project references MUST let the web app and the tests depend on the library, and the
  library MUST depend on no other project.
- **FR-003**: The repository MUST pin the SDK to the latest released (non-preview) patch of the
  constitution's pinned .NET major version, and building with an older SDK MUST fail with a message
  naming the required version.
- **FR-004**: Every project, solution entry, project reference, and generated configuration file
  MUST be created by the official command-line tools, and the exact command sequence MUST be
  recorded so it can be re-run (Constitution VI).
- **FR-005**: The feature MUST NOT add any package beyond those the official templates generate,
  and MUST NOT add supporting services.
- **FR-006**: Template output MUST be kept as generated, apart from the local launch port when the
  template picks a port reserved by macOS.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Re-running the recorded command sequence in an empty directory produces a solution
  that builds with zero errors and zero warnings.
- **SC-002**: The single test command runs both test projects and they pass.
- **SC-003**: Building with an SDK below the pinned version fails every time, with a message naming
  the required version.
- **SC-004**: Running the web app returns the template's default response at its local address.

## Assumptions

- **Planning inputs only**: .NET 10 (LTS) per the constitution's Platform Constraints; the
  four-project layout from D-22 (Gallery.Web, Gallery.Core, Gallery.Core.Tests,
  Gallery.Web.Tests); the SDK's bundled `web`, `classlib`, and `xunit` templates; `global.json`
  for the SDK pin.
- **Latest released .NET (checked 2026-10-27 against the official release index)**: .NET 10 (LTS,
  "active"), SDK 10.0.403, runtime 10.0.14. .NET 11 is 11.0.0-rc.3, a preview, and is excluded.
  The owner installed 10.0.403 after the specify phase.
- The `xunit` template brings its own test-framework packages (xunit, the test runner, the test
  SDK, coverage collector). These are template output, not added dependencies.
- Local services (Mongo, the storage emulator), configuration, health checks, request timing, and
  integration-test infrastructure are out of scope. Each arrives with the feature that needs it.
