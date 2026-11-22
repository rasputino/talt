# Implementation Plan: Continuous integration and quality gates

**Branch**: `002-continuous-integration` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-continuous-integration/spec.md`

## Summary

Add GitHub Actions workflows that build, test and fingerprint-check the solution on Windows, Linux and
macOS (plus ARM64 runners where the private repository can use them), a benchmark workflow (manual and
nightly) that compares BenchmarkDotNet JSON results with a baseline stored in `benchmarks/baselines/`
and only warns on regressions above 10%, and a publish job on `main` that produces self-contained game
builds (win-x64, linux-x64, osx-arm64) as artifacts and as an overwritten prerelease "nightly" release.
Check logic lives in small cross-platform PowerShell scripts under `ci/` so it can be run locally and
reused by all workflows. Golden fingerprints and a Windows-recorded replay session are versioned in
`ci/`. See [research.md](research.md) for decisions.

## Technical Context

**Language/Version**: C# on .NET 10 (existing); workflows in YAML; helper scripts in PowerShell 7 (preinstalled on GitHub-hosted runners for all OSes)

**Primary Dependencies**: GitHub Actions (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact`); `gh` CLI for the nightly release; BenchmarkDotNet 0.15.8 (existing)

**Storage**: Files in the repository: `ci/golden-fingerprints.json`, `ci/sessions/`, `benchmarks/baselines/*.json`

**Testing**: xUnit (existing) via `dotnet test`; the benchmark comparer is checked with a deliberately degraded input

**Target Platform**: Runners `windows-latest`, `ubuntu-latest`, `macos-latest` (arm64), optionally `ubuntu-24.04-arm`, `windows-11-arm`; macOS Intel not covered

**Project Type**: Infrastructure for a desktop game (no new product code)

**Performance Goals**: Full build + test + fingerprint run under 15 minutes (parallel matrix)

**Constraints**: No GPU on runners (client built, not run); private repository (billing minutes, ARM64 availability unknown, branch protection may be unavailable); warnings are errors; central package management

**Scale/Scope**: 2 workflow files, 3 scripts, 1 golden file, 1 recorded session, 1 baseline file, docs updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Core and client separation | Pass. No product code changes; headless checks run on Talt.Core only. |
| II. Performance is a feature | Pass. Directly supports it: benchmark regression visibility. |
| III. Deterministic simulation | Pass. Enforces it across OS and CPU architectures with golden fingerprints and replay. |
| IV. Tested increments | Pass. CI becomes the test gate; the comparer has a degraded-input self-check. |
| V. Readable C# | N/A (scripts are short and commented). |
| VI. Original content / legal | Pass. Only first-party actions, pinned by major version. |
| VII. Data-driven content | N/A. |
| VIII. Living documentation in English | Pass. ARCHITECTURE.md gains a "Continuous integration" section and updated verification status. |
| Technology constraints | Pass. .NET 10, no new runtime dependencies in the game. |

No violations. Post-design re-check: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/002-continuous-integration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── workflows.md
│   └── ci-scripts.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
.github/workflows/
├── ci.yml               # build-and-test matrix + fingerprints + publish on main
└── benchmarks.yml       # workflow_dispatch + nightly
ci/
├── check-fingerprints.ps1
├── compare-benchmarks.ps1
├── verify-publish.ps1
├── golden-fingerprints.json
└── sessions/windows-reference.json
benchmarks/baselines/    # committed baseline JSON + README
docs/ARCHITECTURE.md     # new "Continuous integration" section, verification status
specs/001-simulation-foundation/tasks.md  # T062 status
```

**Structure Decision**: Workflows stay thin and call `ci/*.ps1`, so every check is reproducible locally and identical across platforms. Publish is a job of `ci.yml` (needs the matrix, runs only on push to `main`).

## Complexity Tracking

No violations to justify.
