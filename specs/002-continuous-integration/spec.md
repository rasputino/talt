# Feature Specification: Continuous integration and quality gates

**Feature Branch**: `002-continuous-integration`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Continuous integration and quality gates. The project targets Windows, Linux and macOS but is developed on Windows. Every change must be checked automatically on all three platforms so problems are found immediately, and performance regressions must be visible before they reach the main branch. P1: every push and pull request builds and tests on Windows, Linux and macOS with a clear pass/fail result. P2: an on-demand benchmark run compared against the previous baseline, regressions above 10% highlighted. P3: each successful build of the main branch produces downloadable game builds for the three platforms. Out of scope: store releases, installers, code signing (feature 022). The headless determinism fingerprint check must equal the golden values on every platform, covering macOS and ARM64 (closing the remaining part of task T062 of feature 001)."

## Clarifications

### Session 2026-11-22

- Q: What happens when the benchmark detects a regression above 10%? → A: Warning only; the run stays green and the report highlights regressions.
- Q: Where is the benchmark baseline stored? → A: In the repository under `benchmarks/baselines/`, updated only by deliberate, reviewable change.
- Q: What if merge blocking cannot be enforced on this private repository and plan? → A: Publish the checks anyway and document the gap (merge discipline falls to the developer).
- Q: Are downloadable builds artifacts only, or also a release? → A: Artifacts plus a prerelease "nightly" release that is overwritten on each successful main build (unsigned, testing only).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automatic multi-platform verification of every change (Priority: P1)

As the developer, every push and every pull request automatically builds the whole solution and runs all automated tests on Windows, Linux and macOS, and I see one clear pass/fail result per platform. Besides the tests, the simulation's determinism fingerprints (a default scenario run and the reference-load run, plus replay of recorded sessions) are computed on each platform and compared against the recorded golden values, so a platform-dependent difference in simulation results is detected immediately. Where the hosting service offers ARM64 machines to this repository, the same checks also run on ARM64.

**Why this priority**: The project is developed only on Windows. Without automatic checks, Linux/macOS/ARM64 breakage (including the determinism guarantee the whole game depends on) can go unnoticed for months. This is also the only way to verify macOS and ARM64 at all, since the developer owns no such machines.

**Independent Test**: Open a pull request with a trivial change and confirm that one result per platform appears; then open a pull request with an intentionally failing test (and, separately, one that changes a fingerprint) and confirm the corresponding platform result fails and merging is blocked.

**Acceptance Scenarios**:

1. **Given** a pull request with a correct change, **When** it is pushed, **Then** a build-and-test result appears for Windows, Linux and macOS, and all are green.
2. **Given** a change that breaks the build or a test only on one platform, **When** it is pushed, **Then** that platform's result is red, the others are unaffected, and the failing step and its output are visible without extra tooling.
3. **Given** a change that alters simulation output on one platform only, **When** the fingerprint check runs, **Then** that platform fails and the report shows expected and actual fingerprint values.
4. **Given** a failing result on any covered platform, **When** the pull request is considered for merging into the main branch, **Then** merging is blocked until the result is green.
5. **Given** a successful run, **When** the developer opens the results, **Then** test results are available as downloadable reports.

---

### User Story 2 - On-demand and nightly benchmark comparison (Priority: P2)

As the developer, I can start a benchmark run on demand (and it also runs nightly), and the results are compared against the stored previous baseline. Any measurement that is more than 10% worse than the baseline is highlighted in a readable report. Benchmarks are not run on every push because shared machines produce noisy timings.

**Why this priority**: Performance is a headline goal of the project (very large maps, tens of thousands of vehicles). Regressions must be visible before they reach main, but this is less urgent than basic correctness checks.

**Independent Test**: Trigger the benchmark run manually and confirm a comparison report is produced; then compare against a deliberately degraded baseline (or degraded result) and confirm the regression is flagged.

**Acceptance Scenarios**:

1. **Given** a stored baseline, **When** the developer triggers a benchmark run, **Then** a report lists each benchmark with baseline value, new value, relative change, and highlights those regressed by more than 10%.
2. **Given** the nightly schedule, **When** the night comes, **Then** the same run executes without manual action and its report is retained.
3. **Given** no baseline exists yet, **When** a benchmark run completes, **Then** the report says there is nothing to compare and the run offers to record its results as the first baseline.
4. **Given** a regression above 10%, **When** the run completes, **Then** the run stays green (warning only) but the report and run summary prominently flag each regression, never silently omitting it.

---

### User Story 3 - Downloadable game builds from main (Priority: P3)

As the developer, each successful build of the main branch produces downloadable, ready-to-run game builds for Windows, Linux and macOS, so I (or a tester) can try the latest game without installing a development toolchain. Each build bundles the native libraries the game needs for graphics and windowing, and the pipeline verifies that they are present.

**Why this priority**: Valuable for testing on machines that cannot build the game, but it depends on User Story 1 and does not guard correctness.

**Independent Test**: Merge a change to main, download each platform's build, and confirm it starts on the matching platform.

**Acceptance Scenarios**:

1. **Given** a successful main-branch verification, **When** the publishing step runs, **Then** one downloadable build per platform is available as a run artifact and as assets of a prerelease "nightly" release replaced on each successful main build.
2. **Given** a build missing a required native library, **When** the verification step runs, **Then** the publishing step fails and names the missing library and platform.
3. **Given** a downloaded build, **When** it is started on the matching operating system, **Then** the game launches (manual check on Linux and macOS at least once).

---

### Edge Cases

- A platform's runner image is updated by the hosting service and breaks the build: the failure must be attributable to the environment change (tool versions are logged) rather than appearing as a mystery.
- A hosted ARM64 runner is unavailable to a private repository on the current plan: the gap is documented, and the workflows still run on the available platforms.
- Tests requiring a GPU or display cannot run on CI machines: the graphical client is built but its GPU-dependent tests are excluded or skipped explicitly, never silently.
- A flaky or noisy benchmark result: a single noisy run must not be treated as an authoritative baseline; baselines are only updated deliberately.
- Concurrent pushes to the same branch: older in-progress runs for the same branch are cancelled to save time.
- A run exceeds the time budget: the slowest stage is identifiable from the run's timing.
- Dependency restore fails transiently (network): the run reports a restore failure distinct from a test failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On every push and every pull request, the system MUST build the whole solution in release configuration with warnings treated as errors on Windows, Linux and macOS.
- **FR-002**: On every push and pull request, the system MUST run all automated tests on Windows, Linux and macOS and publish the test results as downloadable reports.
- **FR-003**: The system MUST present a distinct pass/fail result per platform, and a failure on any platform MUST block merging into the main branch.
- **FR-004**: On every platform, the system MUST run the headless simulation tool to compute the default-scenario and reference-load fingerprints and replay the recorded sessions, and MUST fail if any fingerprint differs from the recorded golden values.
- **FR-005**: The golden fingerprint values MUST be stored in one place under version control and used by all platforms; changing them MUST be a deliberate, reviewable change.
- **FR-006**: Where ARM64 machines are available to the repository on its current hosting plan, the build, tests and fingerprint checks MUST also run on ARM64 for Windows and Linux; where they are not available, the gap MUST be documented.
- **FR-007**: GPU-dependent tests MUST NOT run in CI; the graphical client MUST still be compiled on every platform.
- **FR-008**: The system MUST cache downloaded dependencies between runs to keep run time low.
- **FR-009**: The system MUST allow starting a benchmark run manually and MUST run it automatically every night, not on every push.
- **FR-010**: The benchmark run MUST export machine-readable results and compare them with a stored baseline, producing a human-readable report that highlights every measurement regressed by more than 10%.
- **FR-011**: Baselines MUST be stored in the repository under `benchmarks/baselines/`; the update procedure MUST be documented and baselines MUST only change by deliberate, reviewable action.
- **FR-018**: A regression above 10% MUST produce a warning in the report and run summary but MUST NOT fail the benchmark run.
- **FR-019**: Each successful main build MUST also update a prerelease "nightly" release with the three builds, clearly marked unsigned and for testing.
- **FR-012**: On every successful main-branch verification, the system MUST produce self-contained downloadable game builds for Windows x64, Linux x64 and macOS ARM64.
- **FR-013**: The publishing step MUST verify that the graphics and windowing native libraries are present in each build and fail naming the missing item otherwise.
- **FR-014**: In-progress runs superseded by a newer push to the same branch MUST be cancelled.
- **FR-015**: The workflows MUST log tool and runner environment versions so environment changes can be diagnosed.
- **FR-016**: The continuous integration setup MUST be documented in the architecture documentation, including what is and is not covered per platform.
- **FR-017**: The first successful run results MUST be recorded in the architecture documentation's verification status and in the status of the remaining cross-platform verification task of feature 001 (macOS and ARM64).

### Key Entities

- **Platform check**: one build-and-test-and-fingerprint run on a given operating system and CPU architecture, with a pass/fail result.
- **Golden fingerprints**: version-controlled expected simulation fingerprints against which every platform is compared.
- **Benchmark baseline**: the stored previous benchmark results used for comparison.
- **Benchmark report**: per-benchmark comparison of baseline vs. new values with regressions highlighted.
- **Game build**: a self-contained downloadable package of the game for one platform.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of pushes and pull requests receive a pass/fail result for each of Windows, Linux and macOS without any manual action.
- **SC-002**: An intentionally failing test or build on any one platform blocks merging 100% of the time (verified by a deliberate failing pull request).
- **SC-003**: A full verification run (build and tests on the three platforms) completes in under 15 minutes.
- **SC-004**: The simulation fingerprints computed on every covered platform are identical to the golden values, demonstrating cross-platform determinism including macOS (and ARM64 where available).
- **SC-005**: A deliberate 10%+ slowdown is highlighted in the benchmark report 100% of the time, and a run without regression reports no highlights.
- **SC-006**: After each successful change to the main branch, a downloadable build for each of the three platforms is available within 20 minutes, and each starts on its platform.
- **SC-007**: The developer can identify the cause of a failed run (which platform, which step, expected vs. actual values) from the run page alone, within 2 minutes.

## Assumptions

- The repository is private on GitHub and the continuous integration service is GitHub Actions; hosted runners for Windows, Linux and macOS are available on the developer's plan. ARM64 runners may or may not be available to a private repository; this will be determined during implementation.
- Benchmarks run only on demand and nightly because shared runners are noisy.
- Merge blocking is achieved through branch protection that requires the platform checks; if it is unavailable on the developer's plan, the checks are still published and the gap is documented.
- The graphical client is built but not executed in CI; manual verification of a downloaded build on Linux and macOS is performed at least once by the developer or a tester.
- Store releases, installers and code signing are out of scope (feature 022). Builds produced by P3 are unsigned and for testing only.
- Existing dependencies: the headless simulation tool, golden fingerprints and replay recordings from feature 001; the benchmark project already in the repository.
