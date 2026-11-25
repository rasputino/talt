# Quickstart: validating feature 010

## Automated

```
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Stations"
pwsh ci/check-fingerprints.ps1          # map-less goldens unchanged
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Station*" --job short
```

Expected: all tests pass; `StationBenchmarks` daily update of 5 000 stations < 2 ms, 0 B allocated.

## Headless

```
dotnet run --project tools/Talt.Headless -c Release -- run --map 1024x1024 --seed 7 --seed-stations 200 --station-service 60 --ticks 4000
```

Expected: prints `stations: …` with non-zero waiting cargo; running twice prints the same fingerprint.

## By hand (client)

```
dotnet run --project src/Talt.Client -c Release -- --map 1024x1024 --seed 7 --station-service 60
```

1. Pick the bus stop tool; hover a tile next to a town road: catchment tiles are highlighted, the cursor
   label lists "Receives: passengers" and what is accepted. Hover a straight road: the drive-through
   form is previewed on the road.
2. Click to build; the balance falls by the cost; the station is drawn as a placeholder box.
3. Build a truck stop next to a coal mine; after a few days (with `--station-service`) coal waits there.
4. Click the station: the window shows waiting cargo, ratings and last month's figures; toggle the
   catchment; rename it.
5. Build a warehouse whose radius covers two stations: both show the same waiting stock.
6. Remove a station piece with the removal tool.
