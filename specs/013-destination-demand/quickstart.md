# Quickstart: validating destination demand

## Tests

```
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Demand"
dotnet test --filter "Category!=Gpu"
pwsh ci/check-fingerprints.ps1        # map-less goldens and windows-reference unchanged
```

Expected: destination tables match hand computations (10/8/5/2 tiles → 10.8/13.5/21.6/54 %); three towns, two lines:
A→C passengers transfer at B and are paid once at C; conservation over a year to the unit; background job, inline and
sequential runs give equal fingerprints; no allocation per tick in steady state.

## Benchmarks

```
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Demand*" --job short
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*RoadVehicleBenchmarks*" --job short
```

Expected: route job on the 50 000-vehicle reference scenario well under its window; tick averages within 012 budget
plus < 1 ms; no tick above 8 ms.

## In the game (developer checks by hand)

```
dotnet run --project src/Talt.Client -c Release -- --map 1024x1024 --seed 7
```

1. Build bus stops in three towns, a depot, two lines A–B and B–C sharing the stop in B, buy buses.
2. Open the A stop window: passengers waiting by destination (B, C, A) with next stop.
3. Open a bus window: passengers on board by destination.
4. Open town A's window: top destinations with demand and served share.
5. Toggle the unserved-demand overlay (O): dotted lines between unserved town pairs; they thin once served.
