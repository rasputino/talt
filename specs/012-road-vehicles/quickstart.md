# Quickstart: validating road vehicles

## Automated

```
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Vehicles"
dotnet test --filter "Category!=Gpu"
pwsh ci/check-fingerprints.ps1          # map-less goldens and windows-reference unchanged
```

Expected: every test passes, including `RoadVehicleDeterminismTests` (parallel = sequential tick by tick),
`NoOverlapPropertyTests` (randomized scenarios, `CheckInvariants` every tick), `JunctionDeadlockTests` (a crossing and a
T fed from every side for a month) and the allocation tests.

## Headless

```
dotnet run --project tools/Talt.Headless -c Release -- run --map 1024 --seed 7 --seed-stations 200 --seed-vehicles 2000 --ticks 20000 --check-overlaps
dotnet run --project tools/Talt.Headless -c Release -- run --map 1024 --seed 7 --seed-stations 200 --seed-vehicles 2000 --ticks 20000 --sequential
```

Expected: both print the same fingerprint, deliveries and income above zero, no overlap failure.

## Benchmarks

```
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*RoadVehicle*" "*Route*" --job short
```

Expected: 50 000 vehicles ≤ 4 ms per tick (parallel), 0 B allocated per tick apart from `Parallel.For` bookkeeping.

## By hand (client)

```
dotnet run --project src/Talt.Client -c Release -- --map 1024 --seed 7 --water 35 --start-year 1990
```

1. Build a road between two towns (or use town streets), a bus stop in each town, a depot beside a road.
2. Click the depot, buy a bus, press "New line" in its window, click both bus stops, Esc; start the bus.
3. The bus leaves the depot, drives on the right, stops, loads passengers, drives to the other town and "+money"
   appears above it. Buy two more by cloning; watch them queue at the stops and wait at junctions.
4. Set "full load" at one stop; send a bus to the depot and sell it; remove a road on the route and watch it reroute.

Dense check: `-- --map 2048 --seed-stations 1000 --seed-vehicles 20000 --stats` keeps 60 FPS.
