# Contract: Workflows

## `ci.yml`

- Triggers: `push` (all branches), `pull_request`, `workflow_dispatch`. Concurrency cancels superseded runs per ref.
- Job `build-and-test` matrix: windows-latest, ubuntu-latest, macos-latest (+ ARM64 entries if available). Steps: checkout, setup-dotnet 10.x with NuGet cache, log environment, restore, build Release, `dotnet test` with TRX upload, `ci/check-fingerprints.ps1`.
- Job `ci-result` (`if: always()`): fails if any matrix job failed; the single required check.
- Job `publish` (push to `main` only, after `ci-result`): matrix win-x64/linux-x64/osx-arm64, publish self-contained, `ci/verify-publish.ps1`, archive, upload artifact. Job `nightly-release` updates the prerelease `nightly`.
- Permissions: `contents: read`; only `nightly-release` gets `contents: write`.

## `benchmarks.yml`

- Triggers: `workflow_dispatch` (input `job`: short or default) and a nightly `schedule`.
- One job on ubuntu-latest: run benchmarks with JSON export, compare with the baseline, write the step summary, upload results and report. Never fails on regression.
