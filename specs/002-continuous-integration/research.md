# Research: Continuous integration and quality gates

## R1: CI service
- **Decision**: GitHub Actions; repository `rasputino/talt` is private on GitHub.
- **Rationale**: Native to the host, hosted Windows/Linux/macOS runners, artifacts and releases built in.
- **Alternatives**: Azure Pipelines, self-hosted runners (rejected: extra setup, no macOS hardware).

## R2: Platform matrix
- **Decision**: `windows-latest` (x64), `ubuntu-latest` (x64), `macos-latest` (arm64) are required. `ubuntu-24.04-arm` and `windows-11-arm` are added as matrix extensions; their availability to private repositories is detected on the first run (jobs that cannot be scheduled are removed and the gap documented). macOS Intel is not covered; documented.
- **Rationale**: The developer owns no macOS/ARM machines; CI is the only way to close T062 of feature 001. ARM64 runner availability for private repositories depends on the plan, so it is verified empirically.
- **Alternatives**: Skip ARM (rejected: determinism across CPU architectures is the main risk of the simulation).

## R3: Build and test
- **Decision**: `actions/setup-dotnet` with `dotnet-version: 10.0.x` and NuGet cache keyed on `Directory.Packages.props` and project files; `dotnet restore`, `dotnet build -c Release --no-restore` (warnings are already errors via `Directory.Build.props`), `dotnet test -c Release --no-build --logger trx`; TRX files uploaded as artifacts (always).
- **Rationale**: Matches local commands; TRX gives downloadable reports (FR-002); a separate restore step separates restore failures from test failures.
- **GPU tests**: none exist today (Client.Tests only checks localization). Any future GPU test must carry a `Category=Gpu` trait and CI uses `--filter "Category!=Gpu"` now.

## R4: Determinism check on CI
- **Decision**: `ci/check-fingerprints.ps1` runs `Talt.Headless run` for the default scenario (5000 ticks) and the reference load (`--map 4096 --vehicles 50000 --ticks 10000`), compares with `ci/golden-fingerprints.json`, and replays `ci/sessions/windows-reference.json` (recorded on Windows) expecting every checkpoint OK. Golden values come from feature 001 verification (`C9F573FD7DE5CBE3`, `C0C656C5AB2D3302`). If the reference load threatens the 15 minute budget on some runner, it moves to a reduced map (decided from measured timing). `GoldenFingerprintTests` already runs inside `dotnet test`.
- **Rationale**: One golden source for all platforms; a Windows-recorded session proves cross-platform replay.

## R5: Merge blocking
- **Decision**: Stable check names (`build-and-test (<os>)`) aggregated by a final `ci-result` job so protection needs a single required check. Try enabling a ruleset or branch protection via `gh api`; if the plan refuses, document the gap (clarification Q3).
- **Rationale**: One aggregate check keeps protection valid when the matrix changes.

## R6: Benchmarks
- **Decision**: `benchmarks.yml` on `workflow_dispatch` and nightly `schedule`, on `ubuntu-latest` only, `dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter "*" --exporters json --job short` (job selectable by input). `ci/compare-benchmarks.ps1` compares mean times with `benchmarks/baselines/ubuntu-latest.json`: threshold 10%, markdown report to the step summary and artifact, `::warning` per regression, exit code 0. Missing baseline: the report says so. Baselines are updated by committing the artifact JSON deliberately; the workflow never commits.
- **Rationale**: Clarifications Q1/Q2; a single runner type avoids cross-OS timing differences.
- **Alternatives**: Auto-commit baseline (rejected: noisy), third-party benchmark actions (rejected: extra dependency).

## R7: Publish
- **Decision**: Job `publish` (needs `ci-result`, only on push to `main`), matrix rid to runner: `win-x64` on windows-latest, `linux-x64` on ubuntu-latest, `osx-arm64` on macos-latest. `dotnet publish src/Talt.Client -c Release -r <rid> --self-contained`, then `ci/verify-publish.ps1` checks the wgpu-native and GLFW native libraries and the client executable; archive; upload artifact; a final job updates the prerelease `nightly` with `gh release`, marked prerelease, notes saying unsigned/testing only.
- **Rationale**: Publishing natively on the matching OS avoids cross-compile surprises; exact native file names are confirmed by a local publish during implementation.

## R8: Speed and robustness
- Concurrency group per ref with `cancel-in-progress` (FR-014); `fail-fast: false`; job timeouts; a step prints `dotnet --info` and runner OS version (FR-015).
