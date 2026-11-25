# Quickstart: validating feature 018

Prerequisites: .NET 10 SDK, a GPU (reference: integrated AMD Radeon, Vulkan).

## Build and tests

```text
dotnet build
dotnet test --filter "Category!=Gpu"     # CI set: vox, mesher, batching, cascades, settings, decoration
dotnet test --filter "Category=Gpu"      # shader compilation on a real GPU
pwsh ci/check-fingerprints.ps1           # goldens unchanged (FR-021, SC-006)
```

## Models

```text
dotnet run --project tools/Talt.ModelGen -c Release   # regenerates src/Talt.Client/Assets/Models/*.vox
git status src/Talt.Client/Assets/Models              # no change when the generator is unchanged
```

Expected: every key in `models.json` loads; the start-up log shows model count, triangles and load
time (< 1 s, SC-005).

## Look (developer approval, SC-003)

Before screenshots come from `main`; after from this branch, same seed and zoom:

```text
dotnet run --project src/Talt.Client -c Release -- --map 2048x2048 --seed 7 --zoom 60 --graphics high
dotnet run --project src/Talt.Client -c Release -- --map 2048x2048 --seed 7 --zoom 600 --graphics high
dotnet run --project src/Talt.Client -c Release -- --map 2048x2048 --seed 7 --zoom 60 --day-night --time-of-day 0.88
```

Check: shadows from hills, buildings, trees and vehicles; animated water with foam on shores; bushes
and rocks; models for buildings, industries, vehicles and trees; no shimmering while the camera
moves; F3 shows the graphics settings.

## Budgets (SC-001, SC-002, SC-004)

```text
dotnet run --project src/Talt.Client -c Release -- --map 2048x2048 --graphics high --stats
dotnet run --project src/Talt.Client -c Release -- --map 4096x4096 --graphics medium --stats
dotnet run --project src/Talt.Client -c Release -- --map 2048x2048 --graphics high --measure-effects
dotnet run --project src/Talt.Client -c Release -- --map 4096x4096 --graphics medium --measure-effects
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Model*" --job short
```

Expected: 60 FPS at default, close and far zoom; the per-effect table is recorded in
`docs/ARCHITECTURE.md`.

## Settings

Open Settings → Graphics in a game: switching presets changes the look within a second (SC-007);
changing one effect shows Custom; restart keeps the choice.
