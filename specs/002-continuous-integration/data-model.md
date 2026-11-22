# Data Model: Continuous integration and quality gates

Files and records only; no runtime data model.

## Golden fingerprints (`ci/golden-fingerprints.json`)

```json
{
  "runs": [
    { "name": "default", "args": ["run", "--ticks", "5000"], "fingerprint": "C9F573FD7DE5CBE3" },
    { "name": "reference", "args": ["run", "--map", "4096", "--vehicles", "50000", "--ticks", "10000"], "fingerprint": "C0C656C5AB2D3302" }
  ],
  "replays": [
    { "name": "windows-reference", "session": "ci/sessions/windows-reference.json" }
  ]
}
```

Changed only deliberately, with the reason in the commit message.

## Benchmark baseline (`benchmarks/baselines/<runner>.json`)

BenchmarkDotNet full JSON export (`Benchmarks[].FullName`, `Statistics.Mean` in nanoseconds). The
commit and date of recording are noted in `benchmarks/baselines/README.md`.

## Benchmark report (markdown, per run)

Table: benchmark, baseline mean, new mean, change %, flag (`REGRESSION` when change is above +10%).

## Platform check

`build-and-test (<os-arch>)`: pass/fail; aggregated by `ci-result`.

## Game build

`talt-<rid>.zip` or `.tar.gz`: self-contained publish output of `src/Talt.Client` including native libraries.
