# Implementation Plan: Map Generation and New Game

**Branch**: `004-map-generation` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-map-generation/spec.md`

## Summary

Replace the prototype `TerrainGenerator` with a staged, deterministic map generator in `Talt.Core`
(`Talt.Core.Maps`). A `MapSettings` record (size, seed, roughness, maximum height up to 127, water
amount, sea level) drives a pipeline that produces `MapData`: compact per-tile arrays (height,
terrain type, water level, flags, scenery) plus reserved town and industry sites. Stages: seeded
fractal noise with continent shaping, a quantile remap that hits the requested water share, a
one-voxel-per-tile slope limiter with deliberate cliffs, depression filling that yields lakes and a
drainage forest, rivers carved along that forest (so they always reach the sea or a lake), beaches,
snow line, forest density and tree placement as scenery data, and Poisson-disc site selection on
flat connected land. Stages run in parallel by row bands with a fixed partitioning so results do not
depend on thread count; progress is reported through `IProgress<MapGenerationProgress>` and
cancellation through `CancellationToken`. A `WorldBuilder` fills the render voxels from `MapData`
(up to four stacked 32-voxel chunks) and `Heightmap` is built straight from `MapData`. The client
only gains command-line options and console progress in `Program.cs` and the call that swaps the
generator; the in-window new-game screen and the overview preview are deferred until after feature
005 and recorded in `docs/ARCHITECTURE.md`. The headless tool gains a `map` command that prints
timing and a content fingerprint.

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: none new (noise and sampling are written from scratch)

**Storage**: N/A (maps are regenerated from the shareable settings text; no save format yet)

**Testing**: xUnit in `tests/Talt.Core.Tests/Maps` (determinism per seed and thread count, structural
invariants over many seeds and sizes, sites, settings text, extremes); golden map fingerprints run on
every CI platform; BenchmarkDotNet generation benchmarks per size

**Target Platform**: Windows, Linux x64/arm64, macOS arm64 (identical results on all)

**Project Type**: Desktop game (core library + client executable + headless tool)

**Performance Goals**: 4096 × 4096 generated in < 20 s including building the voxel world (target
~10 s); 256 × 256 < 1 s; 60 FPS rendering unchanged (feature 003)

**Constraints**: determinism across platforms and thread counts (floating point limited to
`+ - * /`, `Math.Sqrt` and floor; no `Pow`, `Exp`, trigonometry); peak memory for 4096 × 4096
generation ≤ 1.5 GB over the existing world budget; no unsafe code; warnings are errors

**Scale/Scope**: sizes 64 to 16 384 tiles per side (multiples of 32; player-facing minimum 256);
~10 000 sites on a 4096 map; 5 bytes of tile data per tile (80 MB at 4096²)

## Constitution Check

*Initial gate, re-checked after design: PASS, no violations to justify.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I Core/Client separation | PASS | all generation, data and tests in `Talt.Core`; client only parses options, prints progress, swaps the call |
| II Performance | PASS | budget 4096 < 20 s enforced by a benchmark and a recorded measurement; no per-frame cost added; memory measured (R9) |
| III Deterministic | PASS | pure function of settings; parallel = sequential tested; float restricted to IEEE-exact ops; golden fingerprints on all CI platforms |
| IV Tested increments | PASS | tests first per story; invariants (rivers, water, slope, sites) checked on many seeds |
| V Readable C# | PASS | one file per stage, small static classes, XML docs; no unsafe |
| VI Original content | PASS | noise, priority-flood and Poisson-disc are published public algorithms, implemented from their descriptions (R1, R3, R7) |
| VII Data-driven | PASS | spacing, densities and thresholds live in one `MapGenerationTuning` record (defaults in code, overridable in tests), not scattered constants |
| VIII Docs in English, localizable | PASS | console texts through `Strings` en/es; architecture doc updated |

## Project Structure

### Documentation (this feature)

```text
specs/004-map-generation/
├── plan.md
├── research.md          # decisions R1-R12
├── data-model.md        # MapSettings, MapData, tile encoding, Site, progress
├── quickstart.md        # how to generate, check and look at maps
├── contracts/
│   ├── map-generator-api.md   # Talt.Core public API
│   └── cli.md                 # client and headless command lines, settings text
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Maps/                         # NEW
│   ├── MapSettings.cs            # record, validation, settings text (parse/format), versioning
│   ├── MapGenerationTuning.cs    # data-driven thresholds and densities
│   ├── MapGenerationProgress.cs  # stage + fraction (struct)
│   ├── MapData.cs                # compact tile arrays + sites + fingerprint
│   ├── TileFlags.cs, TerrainType.cs, Scenery.cs
│   ├── MapGenerator.cs           # orchestrates stages, progress, cancellation
│   ├── Stages/
│   │   ├── HeightField.cs        # noise, continents, quantile remap to sea level
│   │   ├── SlopeLimiter.cs       # chamfer limiter, cliffs
│   │   ├── DrainageFill.cs       # priority flood: lakes + drainage forest
│   │   ├── RiverCarver.cs        # accumulation, springs, carving, bank repair
│   │   ├── TerrainClassifier.cs  # beaches, rock, snow, buildable flag
│   │   ├── ScenerySeeder.cs      # forest density, trees
│   │   └── SiteSelector.cs       # components, Poisson disc, site kinds
│   ├── Sites.cs                  # Site, SiteKind
│   └── WorldBuilder.cs           # MapData -> VoxelWorld
├── Terrain/
│   ├── ValueNoise.cs             # kept
│   ├── Heightmap.cs              # + FromMapData
│   ├── TerrainGenerator.cs       # REMOVED
├── Voxels/VoxelWorld.cs, Chunk.cs  # small: vertical extent, bulk column fill
tools/Talt.Headless/              # `map` command
benchmarks/Talt.Benchmarks/MapGenerationBenchmarks.cs   # NEW
tests/Talt.Core.Tests/Maps/       # NEW
src/Talt.Client/Program.cs        # options, progress, generator swap (only client edit besides Strings)
src/Talt.Client/Localization/     # Strings.en/es.json: new keys
docs/ARCHITECTURE.md              # map generation section, deferred UI, results
```

**Structure Decision**: a new `Maps` namespace in the existing `Talt.Core` project; no new projects.
Client edits are limited to `Program.cs` and the localization files so feature 005 (which owns
`GameWindow.cs`, input and the UI layer) does not conflict; `GameWindow.cs` and rendering files are
touched only if verification shows heights above 32 voxels need it (R8), and then minimally.

## Complexity Tracking

No constitution violations. Two deliberate complexities, justified in research: the priority-flood
drainage (R4) because it is what makes "rivers always reach water" true by construction, and the
chamfer slope limiter (R3) because iterative smoothing would not guarantee the slope rule.
