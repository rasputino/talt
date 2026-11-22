# Benchmark baselines

`ubuntu-latest.json` holds the BenchmarkDotNet results (merged JSON, one entry per benchmark) that the
**Benchmarks** workflow compares against. Only `ubuntu-latest` is used because timings of different
runner types are not comparable.

## Updating the baseline (deliberate, never automatic)

1. Run the **Benchmarks** workflow manually (Actions tab, choose job `default` for precise numbers).
2. Download the `benchmark-results` artifact and copy `current.json` to `benchmarks/baselines/ubuntu-latest.json`.
3. Commit it, saying why the baseline changed (new benchmark, intended speed change, new runner image).

Without a baseline file, the workflow still runs and the report says there is nothing to compare.
