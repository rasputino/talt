# Quickstart: Map Generation and New Game

Prerequisites: .NET 10 SDK, repository restored (`dotnet restore`).

## 1. Generate and fingerprint without a window

```powershell
dotnet run -c Release --project tools/Talt.Headless -- map --map 1024x512 --seed 2026
dotnet run -c Release --project tools/Talt.Headless -- map --map 4096 --seed 7 --build-world
```

Expect: stage timings, counts, `Fingerprint:`; running twice (or with `--threads 1`) prints the
same fingerprint; 4096 × 4096 including the world finishes in under 20 s (SC-001).

## 2. See a map

```powershell
dotnet run -c Release --project src/Talt.Client -- --map 1024 --seed 11 --water 35 --roughness 70 --max-height 96
dotnet run -c Release --project src/Talt.Client -- --map-text T1-2048x1024-s5-r60-h64-w30-l9
```

Console shows progress per stage and the settings text; the window then opens as before. Look for
sea and beaches, lakes, rivers ending in water, snow-capped mountains, flat plains. Trees are scenery
data only (not drawn yet). Take screenshots of three seeds for the pull request (SC-007).

## 3. Tests and benchmarks

```powershell
dotnet test
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter *MapGeneration*
```

Key tests (`tests/Talt.Core.Tests/Maps`): determinism and thread independence, settings text
round-trip and validation, slope/water/river/site invariants over a matrix of seeds and sizes,
extreme settings, goldens (also run on every CI platform).

## 4. Check the budgets

Record in `docs/ARCHITECTURE.md`: generation time per size (256, 1024, 2048, 4096; stage shares),
peak memory at 4096 (should stay within the 003 world budget plus ≤ 1.5 GB transient), and the 60 FPS
check on a 4096 map with `--max-height 127` (`--stats`).
