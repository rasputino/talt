# Tasks: Map Generation and New Game

**Input**: design documents in `specs/004-map-generation/` (plan, spec, research R1–R13, data-model, contracts, quickstart)

**Tests**: required (constitution IV): written with or before the code they cover, in `tests/Talt.Core.Tests/Maps`.

**Format**: `- [ ] T### [P] [US#] description with path` — `[P]` = can run in parallel (different files, no unfinished dependency).

**Conflict rule (feature 005 runs in parallel)**: client edits are limited to `src/Talt.Client/Program.cs` and `src/Talt.Client/Localization/*`. Do not edit `GameWindow.cs`, input or UI files unless T009 proves it is unavoidable, and then minimally.

**Deferred (recorded, not built here)**: in-window new-game screen and overview preview (User Story 5, P3), until feature 005 provides the UI framework; drawing trees (no instance rendering yet).

## Phase 1: Setup

- [x] T001 Create folders `src/Talt.Core/Maps/`, `src/Talt.Core/Maps/Stages/`, `tests/Talt.Core.Tests/Maps/` and confirm the solution still builds with warnings as errors (`dotnet build`)
- [x] T002 [P] Add the shared test helper `tests/Talt.Core.Tests/Maps/MapTestSettings.cs` (a matrix of seeds and sizes: 256², 512×256, 256×1024 and extremes) used by all map tests

## Phase 2: Foundational (blocks all stories)

- [x] T003 [P] Implement `MapSettings` (record, defaults, `Validate`, `ToText`, `TryParse`, `GeneratorVersion`, `PlayerMinimumSize`) implementing data-model.md `MapSettings.Width`, `MapSettings.Depth`, `MapSettings.Seed`, `MapSettings.Roughness`, `MapSettings.MaxHeight`, `MapSettings.WaterAmount`, `MapSettings.SeaLevel` (ranges and defaults as defined there) and research R11 in `src/Talt.Core/Maps/MapSettings.cs`, with tests in `tests/Talt.Core.Tests/Maps/MapSettingsTests.cs` (ranges, error messages name the setting, text round-trip, negative/zero/max seed, unknown version rejected)
- [x] T004 [P] Implement `TerrainType`, `TileFlags`, `Scenery` encoding, `SiteKind`, `Site` in `src/Talt.Core/Maps/` per data-model.md (`MapData.Terrain`, `MapData.Flags`, `MapData.Scenery` bit layout, `Site.FreeRadius`, `Site.Score`)
- [x] T005 [P] Implement `MapGenerationTuning` with the defaults of research R3, R5–R7, R10 in `src/Talt.Core/Maps/MapGenerationTuning.cs` and `MapGenerationProgress` + `MapGenerationStage` in `src/Talt.Core/Maps/MapGenerationProgress.cs` (data-model `MapGenerationProgress`)
- [x] T006 Implement `MapData` (data-model.md `MapData.Height`, `MapData.Terrain`, `MapData.WaterLevel`, `MapData.Flags`, `MapData.Scenery`, accessors, `Fingerprint()` using `StateHasher`) in `src/Talt.Core/Maps/MapData.cs` with tests `MapDataTests.cs` (fingerprint changes when any array or site changes; bounds checks)
- [x] T007 Add vertical support to the world: `VoxelWorld.TopChunkY`/surface search up to the highest chunk, internal bulk column writer in `Chunk`, in `src/Talt.Core/Voxels/VoxelWorld.cs` and `Chunk.cs`; tests in `tests/Talt.Core.Tests/Voxels/` for heights above 32 (research R8)
- [x] T008 Implement `WorldBuilder.Build(MapData)` (sequential chunk creation, parallel fill per chunk, materials Water/Sand/Grass+Dirt/Stone/Snow) in `src/Talt.Core/Maps/WorldBuilder.cs` and `Heightmap.FromMapData` in `src/Talt.Core/Terrain/Heightmap.cs`; tests `WorldBuilderTests.cs` (voxel columns match tile data, heightmap equals `Heightmap.FromWorld`)
- [x] T009 Verify rendering with heights up to 127: generate a hand-built `MapData` with a 127-voxel mountain, mesh it with `GreedyMesher`/`LodVolume` in a Core test `tests/Talt.Core.Tests/Meshing/TallTerrainMeshTests.cs` (no cracks/exceptions, LOD volumes cover the full height); record any needed renderer fix and keep it minimal
- [x] T010 Create `MapGenerator.Generate` skeleton (validation, progress plumbing with throttling, cancellation checks, stage list, `maxDegreeOfParallelism`) in `src/Talt.Core/Maps/MapGenerator.cs`; tests `MapGeneratorProgressTests.cs` (monotonic 0→1, last = 1.0, ≤ ~100 reports, cancellation throws and returns nothing, invalid settings throw before work)

**Checkpoint**: settings, data, world building and the generator shell exist; stages are added next.

## Phase 3: User Story 1 — Generate a map from chosen settings (P1) 🎯 MVP

**Goal**: any valid size and settings produce a map whose look responds to the settings; client command line and console progress work.

**Independent test**: generate with different sizes/settings and check extents, water share, relief; launch the client with the new options.

- [x] T011 [P] [US1] Tests first: `tests/Talt.Core.Tests/Maps/HeightFieldTests.cs` — extents exactly Width×Depth (square and 1024×512), water share within ±1% of `WaterAmount`, higher roughness gives larger relief variation, higher `MaxHeight` gives higher peaks, higher `SeaLevel` with same quantile keeps water share, extremes (water 0, 90, roughness 0/100, sea level min/max) stay valid
- [x] T012 [US1] Implement `HeightField` (continent mask, fBm hills driven by roughness, ridged mountains, domain warp using only IEEE-exact ops, 16-bit fixed point, histogram quantile remap to sea level and max height; peak scaled by shorter side) in `src/Talt.Core/Maps/Stages/HeightField.cs` (research R1, R2); extend `ValueNoise` helpers if needed
- [x] T013 [P] [US1] Tests first: `SlopeLimiterTests.cs` — after limiting, all non-cliff 4-neighbour steps ≤ 1, cliffs only in cliff zones, ≥ 90% of land buildable with default settings on the test matrix
- [x] T014 [US1] Implement `SlopeLimiter` (smoothing + chamfer limiter with cliff zones, `Cliff`/`Buildable` flags) in `src/Talt.Core/Maps/Stages/SlopeLimiter.cs` (research R3)
- [x] T015 [US1] Wire stages `Terrain` and `Slopes` into `MapGenerator` with minimal sea/land terrain classification so a playable map exists (water below sea level, grass above) in `src/Talt.Core/Maps/MapGenerator.cs`
- [x] T016 [P] [US1] Replace prototype uses: update `tests/Talt.Core.Tests/Meshing/MeshTestWorlds.cs`, `LodVolumeTests.cs`, `Terrain/TerrainEditorTests.cs`, `benchmarks/Talt.Benchmarks/ChunkMeshingBenchmarks.cs` to `MapGenerator` + `WorldBuilder`; delete `TerrainGenerator.cs`, `TerrainSettings` and `tests/Talt.Core.Tests/Terrain/TerrainGeneratorTests.cs` (replaced by Maps tests)
- [x] T017 [US1] Client: add new-game options `--map`, `--seed`, `--roughness`, `--max-height`, `--water`, `--sea-level`, `--map-text`, keep positional form, print settings text and localized stage progress, replace the `TerrainGenerator` call, validation errors with exit code 2, in `src/Talt.Client/Program.cs` (contracts/cli.md, research R12)
- [x] T018 [P] [US1] Add localized strings (stage names `stage.map.*`, `map.settings`, `map.generated`, option errors) to `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json` as one contiguous block of `stage.map.*`/`map.*` keys appended at the end (feature 005 also edits these files, so keep the diff additive); run the existing localization completeness test
- [x] T019 [US1] Run the client on 256, 1024×512 and `--max-height 96` maps, check it opens and renders, and fix issues (screenshots reserved for Phase 8)

**Checkpoint**: US1 delivers new games with chosen settings and console progress.

## Phase 4: User Story 2 — Same settings and seed, same map (P1)

**Goal**: bit-identical maps across platforms and thread counts.

**Independent test**: fingerprints equal across runs, thread counts and CI platforms.

- [x] T020 [P] [US2] Tests: `MapDeterminismTests.cs` — same settings twice equal; `maxDegreeOfParallelism` 1 vs default vs 3 equal fingerprints (also after all stages exist); different seeds differ substantially (> 30% of tiles differ in height); same seed different size valid; the full-pipeline run is repeated in T034
- [x] T021 [US2] Make every parallel stage use fixed 16-row bands and ordered merges (`src/Talt.Core/Maps/Stages/*`, helper `src/Talt.Core/Maps/RowBands.cs`); forbid `Math.Pow/Exp/Sin/Cos` in `Talt.Core.Maps` with a unit test that scans the sources (`MapDeterminismRulesTests.cs`)
- [x] T022 [P] [US2] Add golden map fingerprints for fixed settings (256², 512×256, with water/roughness variants) in `tests/Talt.Core.Tests/Maps/MapGoldenTests.cs` (generated on this machine, verified on all five CI platforms via the existing CI matrix; regenerate deliberately only with a `GeneratorVersion` bump and a commit-message note)
- [x] T023 [US2] Headless `map` command (`--size`, `--seed`, `--roughness`, `--max-height`, `--water`, `--sea-level`, `--text`, `--threads`, `--build-world`; prints settings text, stage timings, counts, `Fingerprint:`) in `tools/Talt.Headless/Program.cs` and `HeadlessArguments.cs` with tests in the existing headless test project
- [x] T024 [US2] No separate CI step needed: `MapGoldenTests` run inside the existing `build-and-test` matrix on all five platforms (`ci-result` unchanged)

**Checkpoint**: determinism proven by tests and by CI on all platforms.

## Phase 5: User Story 3 — Natural terrain: water, rivers, coasts, scenery (P2)

**Goal**: seas, lakes, rivers that always reach water, beaches, snow, trees.

**Independent test**: invariant checks over the seed/size matrix.

- [x] T025 [P] [US3] Tests first: `DrainageFillTests.cs` — every land tile has a parent chain ending at sea/lake/root; lake surfaces level and enclosed; depressions below depth threshold levelled; no-sea case creates a pond root
- [x] T026 [US3] Implement `DrainageFill` (bucket-queue priority flood seeded from sea tiles, parent pointers, pop order, lakes) in `src/Talt.Core/Maps/Stages/DrainageFill.cs` (research R4)
- [x] T027 [P] [US3] Tests first: `RiverTests.cs` — every river tile reaches Sea/Lake; bed never rises downstream; no water tile has a lower 4-neighbour land tile (no floating water); count scales with water amount; no rivers on flat maps
- [x] T028 [US3] Implement `RiverCarver` (accumulation, springs, carving, bank repair; no second slope-limiter pass, the bank repair alone guarantees no floating water) in `src/Talt.Core/Maps/Stages/RiverCarver.cs` (research R5)
- [x] T029 [P] [US3] Tests first: `TerrainClassifierTests.cs` — beaches only within 2 tiles of water and low/gentle; snow only at/above snow line and none on low maps; rock on cliffs; `Buildable` consistent with slope rule; `WorldBuilder` materials match types
- [x] T030 [US3] Implement `TerrainClassifier` (Water, Beach, Rock, Snow, Grass, Coast flag, Buildable) in `src/Talt.Core/Maps/Stages/TerrainClassifier.cs` (research R6) and wire `Water`, `Rivers`, `Surface` stages into `MapGenerator`
- [x] T031 [P] [US3] Tests first: `ScenerySeederTests.cs` — trees only on Grass, buildable-slope, not near water/snow; forest tiles denser than open land; tree kinds within encoding; deterministic
- [x] T032 [US3] Implement `ScenerySeeder` (forest density map, per-tile tree hash, kinds by height band) in `src/Talt.Core/Maps/Stages/ScenerySeeder.cs` (research R7) and wire the `Scenery` stage
- [x] T033 [US3] Property test over the seed/size matrix asserting all data-model invariants 1–5 together in `tests/Talt.Core.Tests/Maps/MapInvariantTests.cs`
- [x] T034 [US3] Re-run determinism tests (T020) and refresh goldens deliberately (T022) now that the full terrain pipeline exists

## Phase 6: User Story 4 — Reserved sites (P2)

**Goal**: town and industry sites on flat, connected, buildable land.

- [x] T035 [P] [US4] Tests first: `SiteSelectorTests.cs` — all sites buildable with free radius clear and in map; min distances hold (town–town, industry–industry, industry–town); ≥ 95% of towns in the largest land component (expect 100%); counts scale with land area; tiny/watery maps yield fewer sites without errors; deterministic order
- [x] T036 [US4] Implement `SiteSelector` (land components, Poisson-disc per kind with `DeterministicRandom`, flatness windows, kinds Coast/Forest/Plain/Hills, scores) in `src/Talt.Core/Maps/Stages/SiteSelector.cs` (research R10) and wire the `Sites` stage
- [x] T037 [US4] Add site counts to the console summary and headless output; extend `MapInvariantTests.cs` with invariant 6

## Phase 7: User Story 5 — Overview preview (P3) — DEFERRED

- [x] T038 [US5] Document the deferral: spec already states it; add the "New-game screen and overview preview (after feature 005)" subsection to `docs/ARCHITECTURE.md` describing the `MapSettings`/`IProgress`/`MapData` interfaces the screen will consume and that `MapData` arrays suffice for an overview image (no code)

## Phase 8: Polish and cross-cutting

- [x] T039 [P] Add `benchmarks/Talt.Benchmarks/MapGenerationBenchmarks.cs` (256, 1024, 2048, 4096 with stage timing; world build separately) and run it in Release
- [x] T040 Measure 4096 × 4096 generation + world build (< 20 s), peak memory, and a 60 FPS check with `--max-height 127 --stats`; if memory or time miss budget apply research R8 mitigation or profile the slowest stage; record numbers in `docs/ARCHITECTURE.md`
- [x] T041 [P] Update `docs/ARCHITECTURE.md` (replace the "Terrain generation" section, project tree, map generation pipeline, settings text, limits, deferrals), `README.md`/`docs/SPECKIT_GUIDE.md` status lines if they list features, and the 004 checklist
- [x] T042 Run the full suite (`dotnet test`, Headless tests, 5-platform CI expectations) and confirm the simulation golden fingerprints; if they changed, regenerate on purpose and note it for the commit message
- [ ] T043 Launch the client in a terminal tab with three different seeds and ask the user for screenshots (synthetic input does not reach the window); list them in the PR body when the user asks for the PR
- [X] T044 Ask the user before any commit, push, PR or merge (project rule); prepare the commit message mentioning golden changes if any

## Dependencies and order

- Phase 1 → Phase 2 → US1 (MVP). US2's determinism rules (T020–T021) can start after T012; goldens and CI step after T034's pipeline.
- US3 depends on US1's stages; US4 depends on T030 (terrain classes) and T014 (`Buildable`).
- T016 must precede deleting `TerrainGenerator` anywhere else; T017 depends on T015.
- Parallel examples: T003, T004, T005 together; T011 and T013 tests while T012 is written; T025, T027, T029, T031 test tasks together; T039 with T041.

## Implementation strategy

MVP = Phase 1–3 (settings, shaped terrain with sea, command line, progress). Then determinism and CI
(US2), natural features (US3), sites (US4), then measure and document. After each phase: build, run
tests, run the client once (constitution workflow).
