# CI scripts

The GitHub Actions workflows in `.github/workflows/` only call these scripts, so every check can be
run locally with PowerShell 7 from the repository root. See "Continuous integration" in
[docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).

| File | Purpose |
|---|---|
| `check-fingerprints.ps1` | Runs `Talt.Headless` and compares fingerprints with `golden-fingerprints.json`; replays `sessions/*.json`. |
| `compare-benchmarks.ps1` | Compares BenchmarkDotNet JSON with `benchmarks/baselines/`, flags regressions above 10%. |
| `verify-publish.ps1` | Checks that a self-contained publish contains the wgpu-native and GLFW libraries. |
| `golden-fingerprints.json` | Expected fingerprints for every platform. Change only when simulation rules change on purpose. |
| `sessions/windows-reference.json` | Session recorded on Windows (5000 ticks), replayed on every platform. |
