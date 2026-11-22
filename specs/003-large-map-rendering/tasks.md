# Tasks: Large-Map Rendering

**Input**: Design documents from `specs/003-large-map-rendering/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required by the constitution (principle IV) for all `Talt.Core` logic and the client's
pure math.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4 from spec.md

## Phase 1: Setup

- [X] T001 Measure the baseline: time of terrain generation, heightmap and meshing for `-- 16`, `-- 64`, `-- 128` from the console output of `src/Talt.Client/Program.cs` (Release), and note it for `docs/ARCHITECTURE.md`

## Phase 2: Foundational (blocking)

- [X] T002 [P] Add `PackedVertex` per data-model.md `PackedVertex.Word0`/`PackedVertex.Word1` (bit layout, validation in `Create`) in src/Talt.Core/Meshing/PackedVertex.cs
- [X] T003 [P] Add `PackedMesh` (vertices, quad count, region-relative bounds, `Empty`) per data-model.md `PackedMesh.BoundsMin`/`PackedMesh.BoundsMax` in src/Talt.Core/Meshing/PackedMesh.cs
- [X] T004 [P] Add `RegionCoord` (4×4×4 chunks, `Origin`, `FromChunk`, `FromVoxel`, `Chunks`) in src/Talt.Core/Voxels/RegionCoord.cs
- [X] T005 [P] Extract the face table (normal, U, V, corners, AO brightness) into src/Talt.Core/Meshing/FaceDirections.cs and use it from src/Talt.Core/Meshing/ChunkMesher.cs (no behaviour change)
- [X] T006 [P] Add `MaterialPalette.Variation(x, y, z)` (same hash as `ChunkMesher.VoxelColor`) and use it from ChunkMesher in src/Talt.Core/Meshing/MaterialPalette.cs
- [X] T007 [P] Tests for packing round-trip and validation in tests/Talt.Core.Tests/Meshing/PackedVertexTests.cs and for `RegionCoord` in tests/Talt.Core.Tests/Voxels/RegionCoordTests.cs

## Phase 3: User Story 1 — Smooth camera on a 4096 map (P1) 🎯 MVP

**Goal**: 4096 renders at full detail within memory, culled, batched by region, meshed in the background.

**Independent test**: `-- 128 50000` opens, fills in, F3 shows < 2 GB world memory, camera movement at 60 FPS at close and medium zoom.

### Tests

- [X] T008 [P] [US1] Greedy vs naive surface equality (unit faces with material and per-corner AO, several worlds: flat, noise hills, roads, water, single voxels, chunk borders) in tests/Talt.Core.Tests/Meshing/GreedyMesherTests.cs
- [X] T009 [P] [US1] Naive packed mesh equals the reference `ChunkMesher` face set minus bottom faces at y = 0; no face with normal −Y at y = 0; greedy quad count far below naive on terrain in tests/Talt.Core.Tests/Meshing/GreedyMesherTests.cs
- [X] T010 [P] [US1] Frustum box tests (inside, outside each plane, straddling) in tests/Talt.Client.Tests/Rendering/FrustumTests.cs

### Implementation

- [X] T011 [US1] Implement `GreedyMesher.BuildChunk(world, chunk, merge)` (padded copy, below-world solid, per-direction slice masks keyed by material + AO, merge rules from research R1, diagonal by vertex rotation, region-relative positions, bounds) in src/Talt.Core/Meshing/GreedyMesher.cs
- [X] T012 [P] [US1] Rewrite src/Talt.Client/Rendering/Shaders/voxel.wgsl for `Uint32x2` packed vertices + per-instance region origin, palette uniform, per-voxel variation hash in the fragment shader, AO brightness interpolation; lighting and fog unchanged (contracts/meshing-api.md)
- [X] T013 [P] [US1] Implement `Frustum` (planes from view-projection, `Intersects(min, max)`) in src/Talt.Client/Rendering/Frustum.cs
- [X] T014 [P] [US1] Implement `RenderStatistics` and `FrameTimeTracker` (average and worst over the last second, allocation-free) in src/Talt.Client/Rendering/RenderStatistics.cs, with tests in tests/Talt.Client.Tests/Rendering/FrameTimeTrackerTests.cs
- [X] T015 [US1] Implement `ChunkMeshScheduler` (dirty chunk / region-LOD sets, priority by distance, dedicated below-normal worker threads, in-flight cap, concurrent result queue, chunk versions, shutdown) in src/Talt.Client/Rendering/ChunkMeshScheduler.cs
- [X] T016 [US1] Implement `WorldRenderer` (region slots, region origin instance buffer, shared quad index buffer, LOD 0 region buffers assembled from chunk meshes with GPU buffer-to-buffer copies, per-frame upload budget, frustum + fog culling, one draw per visible region, statistics including managed bytes allocated per frame on the render thread (constitution II check, expected 0 in steady state), release) in src/Talt.Client/Rendering/WorldRenderer.cs
- [X] T017 [US1] Wire `WorldRenderer` into src/Talt.Client/Rendering/WebGpuRenderer.cs (new vertex layouts and pipeline, palette in uniforms, remove per-chunk `UploadMeshes` path) and expose statistics
- [X] T018 [US1] Remove pre-meshing from src/Talt.Client/Program.cs and pass the world to the scene; `PrototypeScene` holds `VoxelWorld` instead of meshes in src/Talt.Client/GameWindow.cs
- [X] T019 [US1] F3 statistics title/console toggle and frame-time tracking in src/Talt.Client/GameWindow.cs; localized strings in src/Talt.Client/Localization/Strings.en.json and Strings.es.json (contracts/controls.md)
- [X] T020 [US1] Run 512/2048/4096 in the window; check look vs before, memory, frame times at close/medium zoom

## Phase 4: User Story 2 — Terrain changes appear almost instantly (P1)

**Goal**: one-tile edit visible < 100 ms; border edits correct; bursts spread over frames.

**Independent test**: PageUp/PageDown in the window print latency < 100 ms; tests for touched chunks and editor.

- [X] T021 [P] [US2] Tests for `ChunkCoord.TouchedBy` (interior, face, edge, corner voxels; negative-free map bounds) in tests/Talt.Core.Tests/Voxels/VoxelWorldTests.cs
- [X] T022 [P] [US2] Tests for `TerrainEditor.Raise/Lower` (column stays valid, height clamps 1..31, heightmap refreshed, returns changed voxels) and that a border edit's remesh equals a full remesh in tests/Talt.Core.Tests/Terrain/TerrainEditorTests.cs
- [X] T023 [US2] Implement `ChunkCoord.TouchedBy` in src/Talt.Core/Voxels/ChunkCoord.cs and `Heightmap.Refresh` in src/Talt.Core/Terrain/Heightmap.cs; threading note on src/Talt.Core/Voxels/VoxelWorld.cs
- [X] T024 [US2] Implement `TerrainEditor` in src/Talt.Core/Terrain/TerrainEditor.cs
- [X] T025 [US2] Edit path: `WorldRenderer.MarkVoxelsChanged`, stale-while-rebuilding, edit latency measurement (edit → first presented frame with all touched meshes) in src/Talt.Client/Rendering/WorldRenderer.cs and ChunkMeshScheduler.cs
- [X] T026 [US2] PageUp/PageDown (+ Shift 32×32) in src/Talt.Client/GameWindow.cs; console latency line; update the controls remark

## Phase 5: User Story 3 — Distant terrain stays readable (P2)

**Goal**: LOD 1–3 region meshes with roads kept and no cracks; LOD picked by screen-space error.

**Independent test**: tests for downsampling, roads, skirts and LOD selection; visual check at medium zoom.

- [X] T027 [P] [US3] Tests for `LodVolume` (majority solid, road wins at ≥ s/2 road columns, road grid continuous at s = 2, 4, 8, water kept, outside region = air, below world solid) in tests/Talt.Core.Tests/Meshing/LodVolumeTests.cs
- [X] T028 [P] [US3] Tests for `GreedyMesher.BuildRegionLod` (skirt walls on all four region sides, no bottom faces, quad count decreases with level, positions multiples of s) in tests/Talt.Core.Tests/Meshing/GreedyMesherTests.cs
- [X] T029 [P] [US3] Tests for `LodSelector` (pixels per unit, level by distance, hysteresis, clamp to max level) in tests/Talt.Client.Tests/Rendering/LodSelectorTests.cs
- [X] T030 [US3] Implement `LodVolume` per data-model.md `LodVolume` in src/Talt.Core/Meshing/LodVolume.cs
- [X] T031 [US3] Implement `GreedyMesher.BuildRegionLod` (mesh the LOD volume with scale s) in src/Talt.Core/Meshing/GreedyMesher.cs
- [X] T032 [US3] Implement `LodSelector` in src/Talt.Client/Rendering/LodSelector.cs
- [X] T033 [US3] Per-region LOD choice, region LOD jobs, fallback to nearest built level, edits invalidate simplified levels, statistics per LOD in src/Talt.Client/Rendering/WorldRenderer.cs

## Phase 6: User Story 4 — Whole map from far away (P3)

**Goal**: farthest zoom shows the whole 4096 map at 60 FPS.

**Independent test**: zoom fully out on `-- 128 50000`; whole map visible, no fog over it, 60 FPS.

- [X] T034 [P] [US4] Tests for camera reach (max distance from map size, near plane scaling) in tests/Talt.Client.Tests/Rendering/OrbitCameraTests.cs
- [X] T035 [US4] `OrbitCamera.MaxDistance` set from map size (max(2000, 2 × side)) and near plane max(0.5, 0.02 × distance) in src/Talt.Client/Rendering/OrbitCamera.cs and src/Talt.Client/GameWindow.cs; check that fog leaves the whole map visible at the farthest zoom (FR-015)

## Phase 7: Polish & cross-cutting

- [X] T036 [P] `ChunkMeshingBenchmarks` (reference naive vs greedy per chunk on flat/hills/road chunks; region LOD 1–3) in benchmarks/Talt.Benchmarks/ChunkMeshingBenchmarks.cs
- [X] T037 (Not needed: 4096 terrain generation takes ~4.7 s, under budget.) Speed up terrain generation if T001 shows > 10 s for 4096 (write chunk columns directly; test same output) in src/Talt.Core/Terrain/TerrainGenerator.cs
- [X] T038 Run full test suite and benchmarks; golden fingerprints unchanged
- [X] T039 User runs the quickstart window checks (512, 2048, 4096; edits; fast forward SC-006) and reports title-bar numbers
- [X] T040 Update docs/ARCHITECTURE.md ("World rendering", "Rendering performance", verification of 001 SC-003) and README controls

## Dependencies

- Phase 2 blocks all stories. US1 blocks US2 (edit path uses the scheduler and region buffers) and
  US3 (LOD uses region slots). US4 needs US3 for performance at far zoom, but its camera change is
  independent. Polish last.

## Parallel examples

- Phase 2: T002, T003, T004, T005, T006 together.
- US1: T008, T009, T010 (tests) together; then T012, T013, T014 in parallel with T011.
- US3: T027, T028, T029 together.

## Implementation strategy

MVP = Phases 1–3 (4096 renders within budget at near/medium zoom). Then US2 (edits), US3 (LOD),
US4 (far zoom), polish with measurements recorded.
