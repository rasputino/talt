# Quickstart: Cargo Types and Industries

Validation guide for feature 009. It assumes the repository root and the .NET 10 SDK; no GPU is needed
except for the client steps.

## 1. Build and unit tests

```text
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Industries"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~GoldenFingerprint"
```

Expected: warnings are errors and the build is clean; the empty-industry golden test still matches
its pre-009 values; the populated-map golden test matches the new recorded value.

## 2. Data files load and validate

```text
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~GameContentTests"
```

Expected: the default `cargo.json` and `industries.json` load; each invalid fixture reports the file
and the offending id/field (unknown cargo in a recipe, duplicated id, negative rate, footprint too
large, missing stock cap, bad economy range).

Manual check: edit a copy of the embedded JSON, call `GameContent.FromJson` from a test or scratch
program, and confirm the message names the file; the client also stops before creating a world.

## 3. Industries on a generated map

Headless:

```text
dotnet run --project tools/Talt.Headless -c Release -- map --map 512x512 --seed 7
dotnet run --project tools/Talt.Headless -c Release -- run --ticks 4000 --map-text T1-512x512-s7-r50-h28-w25-l7
```

Expected: the `map` command still reports the same sites as before (feature 004 is unchanged); the
`run` summary shows `industries:` at about 75 % of the industry sites. Running twice gives the same
fingerprint. Replaying a recorded session with a map text reproduces the same industry count and
fingerprint.

Core test view: `IndustryPlacementTests` asserts density, site-kind suitability, era, no overlap,
footprint bounds and determinism over several seeds and a non-square map.

## 4. Production and processing

```text
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~IndustryProductionTests"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~IndustryDeliveryTests"
```

Expected:

- Advancing through exactly one month produces `perMonth * rate / 1000` of each output, split across
  days, never negative, never above the stock cap.
- Delivering 4 wood to a sawmill makes 3 lumber available at once; 5 wood leaves 1 pending.
- Delivering coal to a power station consumes it and increases the accepted counter.
- Delivering cargo the kind does not accept changes nothing.
- Filling the output store postpones processing instead of destroying input.
- Monthly statistics rotate on month end.

## 5. Lifecycle

```text
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~IndustryLifecycleTests"
```

Expected: regular deliveries grow the rate; long neglect triggers decline and seeded closures; free
sites receive seeded openings, never occupied sites; a reopened site has a new id and no old state;
two runs with the same seed produce identical histories, parallel and sequential.

## 6. Performance

```text
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Industry*" --job short
```

Expected on the reference machine: day update for 1 000 industries well under 1 ms, month update well
under 5 ms, no allocation per operation. Record the measured numbers in `docs/ARCHITECTURE.md`.

## 7. Client (developer check)

```text
dotnet run --project src/Talt.Client -c Release -- --map 512x512 --seed 7 --water 35
```

Synthetic key presses do not reach the GLFW window, so ask the developer to check:

1. Industries are visible as distinct coloured buildings on the reserved sites, not on water.
2. Clicking one opens the industry window with production, acceptance, recipe, available and last
   month's transport figures; the hover highlight matches the building.
3. Two industries can be opened at once and are tracked separately.
4. With `--stats`, frame time stays within the budget while moving the camera over the map.
5. Spanish and English show translated names, categories and units (language setting).

## 8. Recorded session

```text
dotnet run --project src/Talt.Client -c Release -- --map 256x256 --seed 3 --vehicle 0 --record session.json
dotnet run --project tools/Talt.Headless -c Release -- replay session.json --checkpoint-every 1000
```

Expected: the session contains `"map": "T1-256x256-s3-..."`; replay reports every checkpoint OK and
the same industry count. A session recorded before this feature (no `"map"`) still replays with the
old fingerprints.
