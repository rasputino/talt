# Quickstart: Railways (015)

1. `dotnet test --filter "FullyQualifiedName~Rails"` — layer, planner, blocks, signals, movement, fixtures (single line
   with passing loops, double track, terminus, junction), platform choice, pull-forward, determinism, allocations.
2. `dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Train*" --job short` — 1 k / 5 k trains,
   route 1 000 tiles; compare with docs/ARCHITECTURE.md.
3. `dotnet run --project tools/Talt.Headless -c Release -- run --map 1024x1024 --seed-trains 500 --ticks 4000` —
   no invariant failure, trains deliver.
4. `pwsh ci/check-fingerprints.ps1` — existing goldens unchanged.
5. Hand test (client, `--map 512x512`): build track with a curve, slope and bridge; a 2 × 5 station at each end; a
   depot; signals; buy a locomotive with wagons; create a line; start it; watch it load, run and stop at red signals.
6. `dotnet run --project src/Talt.Client -c Release -- --map 4096x4096 --seed-trains 5000 --stats` — 60 FPS, ×20.
