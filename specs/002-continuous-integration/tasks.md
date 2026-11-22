# Tasks: Continuous integration and quality gates

**Input**: Design documents in `/specs/002-continuous-integration/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: No separate test tasks were requested; validation is done by running the scripts locally and by workflow runs on GitHub (see quickstart).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies)
- **[Story]**: user story (US1 = build/test/fingerprints, US2 = benchmarks, US3 = builds)

## Phase 1: Setup

- [X] T001 Create the `ci/`, `ci/sessions/` and `benchmarks/baselines/` directories with a short `ci/README.md` explaining that workflows only call these scripts
- [X] T002 Check on the local SDK that a `dotnet publish src/Talt.Client -c Release -r win-x64 --self-contained` output lists the wgpu-native and GLFW native files; record the exact file names per rid (win-x64, linux-x64, osx-arm64) in `specs/002-continuous-integration/research.md` under R7

## Phase 2: Foundational (blocks all stories)

- [X] T003 Record `ci/sessions/windows-reference.json` with `dotnet run -c Release --project tools/Talt.Headless -- run --ticks 5000 --record ci/sessions/windows-reference.json` on Windows and confirm `replay` returns exit code 0
- [X] T004 [P] Create `ci/golden-fingerprints.json` per [data-model.md](data-model.md) (Golden fingerprints) using the verified values `C9F573FD7DE5CBE3` (default 5000 ticks) and `C0C656C5AB2D3302` (reference load), confirming them locally
- [X] T005 Implement `ci/check-fingerprints.ps1` per [contracts/ci-scripts.md](contracts/ci-scripts.md) and run it on Windows (PASS expected)

**Checkpoint**: fingerprints are checkable by one script on any OS.

## Phase 3: User Story 1 - Multi-platform build, test and fingerprint check (P1) 🎯 MVP

**Goal**: every push and PR shows a pass/fail result on Windows, Linux, macOS (and ARM64 where available).

**Independent Test**: push the branch; three platform checks plus `ci-result` appear and pass; a deliberately failing test fails its platform.

- [X] T006 [US1] Create `.github/workflows/ci.yml` with triggers, concurrency cancellation, `permissions: contents: read`, and the `build-and-test` matrix (windows-latest, ubuntu-latest, macos-latest), `fail-fast: false`, timeouts, steps: checkout, setup-dotnet 10.0.x with NuGet cache, environment log (`dotnet --info`, OS version), restore, build Release, `dotnet test -c Release --no-build --filter "Category!=Gpu"` with TRX logger, always-upload TRX artifact, `ci/check-fingerprints.ps1`
- [X] T007 [US1] Add the `ci-result` aggregate job (`if: always()`, fails when any `build-and-test` job did not succeed) to `.github/workflows/ci.yml`
- [X] T008 [US1] Ask the user before committing and pushing; with approval push the branch, open the run and fix failures until the three platforms are green; record run times against the 15 minute budget (SC-003)
- [X] T009 [US1] Probe ARM64 availability: add `ubuntu-24.04-arm` and `windows-11-arm` matrix entries to `.github/workflows/ci.yml`; keep those that schedule on this private repository, remove the rest and note the gap
- [X] T010 [US1] Prove blocking: push a throwaway change with a failing test (and one with a changed golden value) to confirm the platform and `ci-result` fail with expected/actual values visible; revert it
- [X] T011 [US1] Try enabling branch protection or a ruleset requiring `ci-result` on `main` with `gh api`; if the plan refuses, record the gap for the docs

**Checkpoint**: US1 delivers verification on all available platforms, including the macOS/ARM64 results.

## Phase 4: User Story 2 - Benchmark comparison (P2)

**Goal**: manual and nightly benchmark runs produce a comparison report flagging regressions above 10%.

**Independent Test**: run the workflow manually; compare against a degraded baseline copy and see the flag.

- [X] T012 [P] [US2] Implement `ci/compare-benchmarks.ps1` per [contracts/ci-scripts.md](contracts/ci-scripts.md) (markdown report, `::warning::` lines, exit 0 on regression, handles missing baseline)
- [X] T013 [US2] Run the benchmarks locally with JSON export (`--exporters json --job short`) and verify the script on the output, including a deliberately slowed copy (SC-005); store the degraded sample only in the scratchpad, not in the repo
- [X] T014 [US2] Create `.github/workflows/benchmarks.yml` (`workflow_dispatch` with `job` input, nightly `schedule`, ubuntu-latest, run benchmarks, compare, write `$GITHUB_STEP_SUMMARY`, upload results and report artifacts)
- [X] T015 [US2] GitHub only offers manual (`workflow_dispatch`) and scheduled runs for workflows present on the default branch, so before the merge add a temporary `push` trigger limited to the feature branch in `.github/workflows/benchmarks.yml` (remove it before the final commit). Run the Benchmarks workflow once without a baseline, download its JSON, commit it deliberately as `benchmarks/baselines/ubuntu-latest.json` (with user approval) and write `benchmarks/baselines/README.md` documenting the update procedure; run again to see a comparison report

## Phase 5: User Story 3 - Downloadable builds from main (P3)

**Goal**: each successful main build publishes win-x64, linux-x64 and osx-arm64 builds.

**Independent Test**: after merging to main, download each build and start it.

- [X] T016 [P] [US3] Implement `ci/verify-publish.ps1` per [contracts/ci-scripts.md](contracts/ci-scripts.md) using the native file names recorded in T002 and test it locally against the win-x64 publish output and against an output with a library removed
- [X] T017 [US3] Add the `publish` matrix job (push to `main` only, needs `ci-result`) to `.github/workflows/ci.yml`: publish self-contained per rid on the matching runner, run `ci/verify-publish.ps1`, archive as zip or tar.gz, upload artifact
- [X] T018 [US3] Add the `nightly-release` job to `.github/workflows/ci.yml` (`contents: write` only for this job) that downloads the three archives and replaces the assets of the prerelease `nightly` using `gh release`, notes stating unsigned and for testing only
- [X] T019 [US3] Verify on GitHub: the `publish` jobs only run on `main`, so temporarily allow the feature branch in their `if` (and make `nightly-release` create a differently named prerelease, `nightly-test`, which is deleted afterwards); revert both changes after the check. Then confirm: artifacts and release assets exist; ask the user to start the Linux and macOS builds manually once and record the outcome

## Phase 6: Polish and documentation

- [X] T020 Add a "Continuous integration" section to `docs/ARCHITECTURE.md` (workflows, scripts, golden file regeneration rule, baseline update procedure, covered and not covered platforms, branch-protection status)
- [X] T021 Update "Verification status" in `docs/ARCHITECTURE.md` with the macOS and ARM64 CI results (fingerprints, test counts, run date) and any gap
- [X] T022 Update the T062 status note in `specs/001-simulation-foundation/tasks.md` (and the cross-platform step in `specs/001-simulation-foundation/quickstart.md`) with the CI results; mark T062 done only if macOS and ARM64 are covered or the gap is explicitly accepted
- [X] T023 Add a CI badge or short CI note to `README.md` if it documents building; run the full quickstart validation and mark this feature's tasks complete
- [ ] T024 Ask the user whether to commit and whether to open a pull request (no commit, push or PR without their yes)

## Dependencies and execution order

- Setup (T001-T002) → Foundational (T003-T005) → US1 (T006-T011).
- US2 (T012-T015) needs only T001; T012 and T013 can start in parallel with US1, T014 and later need the workflow running on GitHub.
- US3 (T016-T019) needs T002 and a green `ci.yml` from US1.
- Polish (T020-T024) after the stories whose results it documents.
- [P] tasks: T004, T012, T016.

## Implementation strategy

MVP is Phase 1-3 (US1): it already closes the macOS/ARM64 verification gap. Then US2, then US3. Any commit, push or PR step requires the user's explicit approval at that moment.

## Implementation notes

- T002: names confirmed with a local cross-publish: win-x64 `wgpu_native.dll`, `glfw3.dll`, `Talt.Client.exe`; linux-x64 `libwgpu_native.so`, `libglfw.so.3`, `Talt.Client`; osx-arm64 `libwgpu_native.dylib`, `libglfw.3.dylib`, `Talt.Client`.
- T009 design change: ARM64 lives in `ci-arm64.yml` (not in the required matrix), because a runner unavailable to a private repo queues instead of failing. Shared steps are in the composite action `.github/actions/build-test`.
- T020 done with a "Continuous integration" section; T021, T022 and the GitHub-run tasks (T008-T011, T015, T019) wait for the first pushed runs.
- T008 result (run 45055058151): windows-x64, linux-x64 and macos-arm64 all green in under 3 minutes; fingerprints equal the golden values on all three. T021 recorded macOS arm64; ARM64 Linux/Windows, T010, T011, T015, T019 and T022 (final T062 status) remain.
- T009 result: both ARM64 runners are available and pass with identical fingerprints, so they were added to the required matrix of `ci.yml` and `ci-arm64.yml` was removed.
- T011 result: branch protection and rulesets return 403 on this plan (private repository, Free); gap documented in ARCHITECTURE.md.
- T015 result: the first benchmark run took about 9.5 minutes; its JSON is committed as the baseline; a second run produced the comparison report (3 of 12 rows over 10% with no code change, i.e. noise).
- T019 result: publish artifacts verified and a test prerelease received three assets; the start-by-hand check of the Linux and macOS builds remains with the user.
- T010 result: a throwaway branch with a failing test turned all five platforms and `ci-result` red and skipped `publish`; a wrong golden value makes `ci/check-fingerprints.ps1` exit 1 (checked locally). Branch deleted.
- T023 result: CI badge added to `README.md`. Follow-up (not in the original plan): `--exit-after-fingerprint` in the client and a `client-smoke` job that starts the real client on every platform with a virtual display and a software GPU driver.
