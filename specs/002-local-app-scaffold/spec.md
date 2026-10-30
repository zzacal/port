# Feature Specification: Local App Scaffold

**Feature Branch**: `002-local-app-scaffold`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "create the projects to run the app and use dotnet cli commands instead of writing out the files yourself. make sure we have the latest dotnet that is in release. if you don't have the latest dotnet, let me know."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the app on my machine with one start sequence (Priority: P1)

The owner clones the repository, starts the local supporting services, and starts the app. A
browser pointed at the local address shows a placeholder home page served by the app, and the app
reports in its logs that it reached the local datastore and the local image storage.

**Why this priority**: Every later feature (claim-once sign-in, upload, galleries, public views)
is built and demonstrated on this running skeleton. Without it nothing else can be exercised.

**Independent Test**: On a machine with only the prerequisites installed, follow the documented
start sequence and load the home page; confirm the page renders and the startup logs show both
dependencies reachable.

**Acceptance Scenarios**:

1. **Given** a fresh clone and the documented prerequisites, **When** the owner runs the documented
   start sequence, **Then** the home page loads in a browser at the documented local address.
2. **Given** the app is running, **When** the owner requests the health address, **Then** it
   reports the app, the datastore, and image storage each as healthy.
3. **Given** the local supporting services are stopped, **When** the owner requests the health
   address, **Then** it reports the unreachable dependency as unhealthy by name, and the home page
   still loads.

---

### User Story 2 - Run the whole test suite with one command (Priority: P2)

The owner runs a single command at the repository root and every test project builds and runs,
including tests that need a real datastore and real image storage, which are started and
discarded automatically.

**Why this priority**: The constitution requires build and test gates before work is accepted,
and requires integration tests against real dependencies. Later features cannot meet that without
the harness existing first.

**Independent Test**: Run the single test command on a machine with the container runtime
running; confirm both unit and integration tests are discovered and pass, and no manually started
services were needed.

**Acceptance Scenarios**:

1. **Given** the container runtime is running, **When** the owner runs the test command, **Then**
   unit tests and integration tests both run and pass.
2. **Given** the integration tests have finished, **When** the owner lists running containers,
   **Then** no test containers are left behind.
3. **Given** the container runtime is not running, **When** the owner runs the test command,
   **Then** unit tests still run, and integration tests fail with a message naming the missing
   container runtime rather than an unrelated error.

---

### User Story 3 - Every request is timed from day one (Priority: P3)

While the app runs locally, each request the owner makes produces one structured log line with
the route, status, and duration, and the home page response tells the browser how long the server
spent.

**Why this priority**: Constitution Principle IV requires instrumentation to ship with each
feature, never as follow-up. Putting it in the skeleton means every later feature inherits it.

**Independent Test**: Make ten requests to the home page and the health address; confirm ten
structured request events appear in the console output with route, status, and duration, and that
the home page response carries a server-timing value.

**Acceptance Scenarios**:

1. **Given** the app is running, **When** the owner requests any address, **Then** exactly one
   structured request event is emitted with route, status code, and duration in milliseconds.
2. **Given** the app is running, **When** the owner loads the home page, **Then** the response
   carries server timing visible in the browser's developer tools.
3. **Given** a dependency check runs, **When** it completes, **Then** a named timing event records
   its duration and outcome.

---

### Edge Cases

- The local datastore or image storage port is already taken by another process: the start
  sequence fails with a message naming the port, not a generic container error.
- The installed .NET SDK is older than the latest released patch of the pinned major version: the
  build refuses to run and names the required version (see FR-002).
- Only a preview or release-candidate SDK of a newer major version is installed: the build does not
  silently use it; it requires the pinned released version.
- The app is started without the local supporting services: it still starts and serves the home
  page; only the health report shows the missing dependency (no crash on boot).
- Integration tests are run twice in a row: the second run starts from a clean datastore and empty
  storage, with no state carried over from the first.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain the solution and the four projects accepted in proposal
  P-01 (one deployable web app, one pure-logic library, one unit test project, one integration test
  project), with project references that let the web app and tests depend on the library but never
  the library on the web app.
- **FR-002**: The repository MUST pin the SDK to the latest released (non-preview) patch of the
  constitution's pinned .NET major version, and building with an older SDK MUST fail with a message
  naming the required version. Preview and release-candidate SDKs MUST NOT satisfy the pin.
- **FR-003**: Every project, solution entry, project reference, package reference, and generated
  configuration file MUST be created by the official command-line tools, and the exact command
  sequence MUST be recorded in the feature's quickstart so it can be re-run (Constitution VI).
- **FR-004**: The repository MUST provide a single command that starts the local datastore and the
  local image-storage emulator, with data persisted between restarts and a documented command to
  reset it.
- **FR-005**: The app MUST start and serve a placeholder home page at a documented local address
  that does not conflict with ports macOS reserves (port 5000 is taken by AirPlay).
- **FR-006**: The app MUST read its datastore and storage connection settings from configuration
  so the same build runs locally and, later, in the hosted environment without code changes.
  Local development values MUST NOT require any secret.
- **FR-007**: The app MUST expose a health address that reports the app, the datastore, and image
  storage individually, and MUST keep serving pages when a dependency is down.
- **FR-008**: The app MUST emit one structured log event per request with route, status, and
  duration (Constitution IV.1), and named timing events with duration and outcome for each
  dependency check (IV.2).
- **FR-009**: Public page responses MUST carry server timing to the browser (IV.3).
- **FR-010**: A single command at the repository root MUST build and run all tests. Integration
  tests MUST start their own disposable datastore and storage instances and clean them up
  afterwards.
- **FR-011**: The test suite MUST include at least one unit test in the library project and at
  least one integration test proving the app starts, serves the home page, and reports healthy
  dependencies against the disposable instances.
- **FR-012**: The repository MUST include a short "run locally" section in its README covering
  prerequisites, the start sequence, the test command, and the reset command.
- **FR-013**: The skeleton MUST NOT include feature behaviour beyond FR-001 to FR-012 (no sign-in,
  upload, gallery, or image pages), so later features own their scope (Constitution V.1).
- **FR-014**: The skeleton MUST NOT add any third-party package that is not required by an FR in
  this spec; each added package MUST be justified in the plan (Constitution V.2).

### Key Entities

- **Local environment**: the set of supporting services (datastore, image-storage emulator) the app
  needs on the owner's machine, with their addresses, persisted data, and reset path.
- **Health report**: per-dependency status (healthy, unhealthy with reason) plus the time each
  check took.
- **Request event**: one structured record per request with route, status, and duration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh clone with prerequisites installed, the owner reaches a rendered home
  page in under 5 minutes by following only the README.
- **SC-002**: After the local services are running, the app starts and serves its first page in
  under 10 seconds, measured from the app's own startup and request log events.
- **SC-003**: The single test command passes on a clean machine with the container runtime running,
  in under 3 minutes, and leaves zero test containers running.
- **SC-004**: 100% of requests in a 20-request local run produce exactly one request event each,
  verified from the console log alone.
- **SC-005**: Re-running the recorded scaffolding command sequence in an empty directory produces a
  solution that builds with zero errors and zero warnings.
- **SC-006**: Building with an SDK below the pinned version fails 100% of the time with a message
  naming the required version.

## Assumptions

- **Planning inputs only** (they drive the plan, not the requirements above): .NET 10 (LTS) per the
  constitution's Platform Constraints; the four-project layout from proposal P-01 in
  `docs/proposals.md` (Gallery.Web, Gallery.Core, Gallery.Core.Tests, Gallery.Web.Tests); local
  MongoDB and the Azurite storage emulator via Docker Compose; Testcontainers for integration
  tests; xUnit; `global.json` for the SDK pin; console JSON logging locally, with the Application
  Insights exporter left for the hosting feature.
- **Latest released .NET (checked 2026-10-27 against the official release index)**: the newest
  released channel is .NET 10 (LTS, "active"), latest SDK 10.0.403 and runtime 10.0.14. .NET 11 is
  at 11.0.0-rc.3 ("go-live" preview), which is not a release and is excluded. The SDK installed on
  this machine is 10.0.103 (runtime 10.0.3), which is older than the latest release, so the SDK
  must be updated before implementation (FR-002).
- Starting this feature accepts proposal P-01 (project layout). It moves into the design record as
  a D-entry during planning; P-02 (infrastructure) and P-03 (deployment) stay proposed.
- Local only: provisioning, deployment, Cloudflare, Google sign-in, and Application Insights are
  out of scope here.
- The owner's machine is macOS with a container runtime (Docker Desktop) available; it may need
  starting before integration tests.
- The placeholder home page is replaced by the real contact-sheet home in a later feature; its only
  job here is to prove the request pipeline, timing, and server-timing output.
