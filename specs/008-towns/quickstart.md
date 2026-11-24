# Quickstart: validating towns (feature 008)

## Automated

```
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Towns"
pwsh ci/check-fingerprints.ps1          # map-less goldens unchanged
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*TownBenchmarks*"
```

Expected: all tests pass; `TownBenchmarks.MonthlyUpdateServed` < 50 ms (measured 15.5 ms with 534 towns; it allocates only when storage grows).

## Headless

```
dotnet run --project tools/Talt.Headless -c Release -- run --map-text T1-1024x1024-s7-r50-h28-w25-l7 --vehicles 0 --ticks 146000 --town-service 60
```

Expected: the summary lists towns with names and a total population that grew over the ten years;
with `--town-service 0` the population is unchanged after founding.

## Client (developer, by hand)

```
dotnet run --project src/Talt.Client -c Release -- 32 0 --map 1024x1024 --seed 7 --town-service 80 --stats
```

1. Towns with labels (name and population) appear on the town sites; buildings of several sizes line
   the streets, taller in the centres of large towns.
2. Click a building: the town window shows population, buildings, passengers and mail (generated and
   transported last month), accepted goods/food and the rating of the company.
3. Clear trees near a town and demolish a building with the clear tool: cost and rating change are in
   the preview; after confirming, the rating falls in the window.
4. Run fast for a few in-game years: towns gain buildings and streets.
5. On a 4096 map (`--map 4096x4096`) check the FPS in the title bar stays at 60 with towns in view
   (`--stats`).
