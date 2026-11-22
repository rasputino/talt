# Contract: CI scripts

All scripts are PowerShell 7 (`pwsh`), run from the repository root and exit 0 on success.

## `ci/check-fingerprints.ps1`

- Params: `-Configuration Release` (default), `-Golden ci/golden-fingerprints.json`, `-SkipReference` (skip the large run).
- Runs headless `run` for each golden entry, parses the `fingerprint:` line, compares; replays each listed session and requires exit code 0.
- Output: one line per check `PASS|FAIL <name> expected=<..> actual=<..>`. Exit 1 on any mismatch, 2 on usage or missing files.

## `ci/compare-benchmarks.ps1`

- Params: `-Baseline <json>`, `-Current <json>`, `-Threshold 0.10`, `-ReportPath <md>`.
- Matches benchmarks by `FullName`; flags `(current - baseline) / baseline > threshold`. Missing baseline file: the report says so, exit 0. Regressions never change the exit code (warning only); emits `::warning::` lines when `GITHUB_ACTIONS` is set. Exit 2 only for unreadable input.

## `ci/verify-publish.ps1`

- Params: `-Directory <publish dir>`, `-Rid <win-x64|linux-x64|osx-arm64>`.
- Verifies that the wgpu-native and GLFW native libraries and the client executable exist. Exit 1 listing each missing item with the rid.
