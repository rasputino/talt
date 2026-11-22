# Quickstart: Validating continuous integration

## Local script checks (no GitHub needed)

```powershell
dotnet build -c Release
pwsh ci/check-fingerprints.ps1
pwsh ci/compare-benchmarks.ps1 -Baseline benchmarks/baselines/ubuntu-latest.json -Current <results.json> -ReportPath report.md
dotnet publish src/Talt.Client -c Release -r win-x64 --self-contained -o artifacts/win-x64
pwsh ci/verify-publish.ps1 -Directory artifacts/win-x64 -Rid win-x64
```

Expected: all PASS; the compare script flags a deliberately slowed copy of the baseline.

## On GitHub

1. Push the branch or open a PR: `build-and-test` appears for each platform and all pass; run time under 15 minutes.
2. Introduce a failing test or change a golden value in a throwaway PR: that platform fails and `ci-result` fails.
3. Run **Benchmarks** manually (Actions tab): a report appears in the run summary.
4. Merge to `main`: `publish` artifacts and the `nightly` prerelease appear; download, extract and start the game (Linux and macOS manually at least once).
