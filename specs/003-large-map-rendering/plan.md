# Implementation Plan: Large-Map Rendering

**Branch**: `003-large-map-rendering` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-large-map-rendering/spec.md`

## Summary

Make the 4096 × 4096 map render at 60 FPS within 2 GB while looking the same. `Talt.Core.Meshing`
gains a greedy mesher that merges faces only where the result is pixel-identical (the per-voxel
colour variation moves into the fragment shader), 8-byte packed region-relative vertices with a
shared quad index buffer (32 bytes per quad instead of 168), no bottom-of-world faces, and
simplified LOD 1–3 region meshes (2×, 4×, 8×, roads kept, skirts against cracks). The client groups
chunks into 128³ regions (one draw per visible region, about 1 000 at most), culls regions by
frustum and fog on the CPU, picks LOD by screen-space error, and builds meshes on background worker
threads with nearest-first priority and a per-frame upload budget; edits rebuild only touched
chunks. The window opens before meshing, the camera can zoom out to the whole map, F3 shows render
statistics in the title and console, and PageUp/PageDown edit terrain to measure edit latency.

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: Silk.NET 2.23 Windowing/Input/WebGPU + wgpu-native (client only); no new
packages

**Storage**: N/A

**Testing**: xUnit (Core meshing, LOD, edits; Client pure math: frustum, LOD selection, statistics
formatting); BenchmarkDotNet (meshing)

**Target Platform**: Windows, Linux, macOS (WebGPU over Vulkan, Metal, DirectX 12)

**Project Type**: Desktop game (core library + client executable)

**Performance Goals**: 4096 map with 50 000 vehicles: frame ≤ 16.6 ms at every zoom, worst ≤ 33 ms
during camera movement; world drawing ≤ 8 ms per frame so fast forward keeps ≥ ×20 at ≥ 30 FPS;
single-tile edit visible < 100 ms; greedy chunk mesh ≲ 1 ms

**Constraints**: world GPU memory < 2 GB on 4096; no per-frame allocations in the draw path (fixed
arrays reused); Core free of GPU code; simulation untouched (golden fingerprints unchanged)

**Scale/Scope**: up to 16 384 chunks / 1 024 regions; ~38 M naive faces reduced by greedy merging;
4 LOD levels

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | How this plan complies |
|---|---|---|
| I. Core/Client separation | ✅ Pass | Mesher, LOD volume, packing, edit helpers and region coordinates in `Talt.Core` (pure C#, tested without GPU). Buffers, culling, scheduling, shaders in `Talt.Client`. |
| II. Performance is a feature | ✅ Pass | `ChunkMeshingBenchmarks` (naive vs greedy, LOD); render statistics; 512/2048/4096 numbers in ARCHITECTURE.md; draw path reuses arrays (no per-frame allocation). |
| III. Deterministic simulation | ✅ Pass (N/A) | No simulation change; terrain edits are rendering-side debug tools outside simulation state; golden fingerprint tests unchanged. |
| IV. Tested increments | ✅ Pass | Each story has tests: surface equality (US1), touched chunks/edits (US2), LOD roads/skirts (US3), LOD selection/fog cull (US4). |
| V. Readable C# | ✅ Pass | Plain `uint` bit packing with named helpers; greedy merge rules documented; unsafe code only in the renderer. Added complexity (regions, LOD, workers) justified below. |
| VI. Original content | ✅ Pass | Greedy meshing, AO and skirts are long-public techniques implemented independently; no assets. |
| VII. Data-driven content | ✅ Pass (N/A) | No game content; material colours stay in `MaterialPalette` (rendering constants). |
| VIII. Living docs | ✅ Pass | ARCHITECTURE.md gains "World rendering" and "Rendering performance"; new strings in en/es. |

**Post-design re-check:** ✅ all gates pass.

## Project Structure

### Documentation (this feature)

```text
specs/003-large-map-rendering/
├── spec.md
├── plan.md              # This file
├── research.md          # Decisions R1–R10
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── meshing-api.md   # Core meshing API and shader input
│   └── controls.md      # Keys, statistics, console output
├── checklists/
│   ├── requirements.md
│   ├── performance.md
│   └── rendering.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Meshing/
│   ├── ChunkMesher.cs          # reference naive mesher (kept for tests/benchmarks)
│   ├── PackedVertex.cs         # new
│   ├── PackedMesh.cs           # new
│   ├── GreedyMesher.cs         # new: chunk LOD 0 + region LOD 1–3
│   ├── LodVolume.cs            # new: downsampling rules
│   ├── FaceDirections.cs       # new: shared face table (normal, U, V)
│   └── MaterialPalette.cs      # + Variation()
├── Voxels/
│   ├── RegionCoord.cs          # new
│   ├── ChunkCoord.cs           # + TouchedBy()
│   └── VoxelWorld.cs           # threading note
└── Terrain/
    ├── Heightmap.cs            # + Refresh()
    ├── TerrainEditor.cs        # new: debug raise/lower
    └── TerrainGenerator.cs     # faster column fill if needed (R9)

src/Talt.Client/
├── Program.cs                  # no pre-meshing
├── GameWindow.cs               # F3, PageUp/PageDown, frame-time tracking, camera reach
├── Localization/Strings.*.json # stats strings
└── Rendering/
    ├── OrbitCamera.cs          # MaxDistance per map, near plane
    ├── WebGpuRenderer.cs       # device, pipelines, frame; delegates world to WorldRenderer
    ├── WorldRenderer.cs        # new: region buffers, culling, LOD choice, draws, GPU copies
    ├── ChunkMeshScheduler.cs   # new: dirty sets, workers, results
    ├── Frustum.cs              # new
    ├── LodSelector.cs          # new
    ├── RenderStatistics.cs     # new (+ FrameTimeTracker)
    └── Shaders/voxel.wgsl      # packed vertices, palette, variation hash

tests/Talt.Core.Tests/Meshing/  # GreedyMesherTests, LodVolumeTests, PackedVertexTests
tests/Talt.Core.Tests/Terrain/  # TerrainEditorTests, Heightmap refresh
tests/Talt.Core.Tests/Voxels/   # RegionCoord, TouchedBy
tests/Talt.Client.Tests/Rendering/ # FrustumTests, LodSelectorTests, FrameTimeTrackerTests
benchmarks/Talt.Benchmarks/ChunkMeshingBenchmarks.cs
```

**Structure Decision**: existing projects only; Core gets the CPU-side geometry, Client the GPU side.

## Complexity Tracking

| Added complexity | Why needed | Simpler alternative rejected because |
|---|---|---|
| Regions of 4×4×4 chunks with GPU-side buffer assembly | Bounds draw calls to ~1 000 while edits stay per chunk without CPU mesh copies | Per-chunk draws: up to 16 384 draws, too slow; CPU copies: doubles memory |
| Background worker threads + scheduler | Long frames otherwise (FR-008), nearest-first fill-in (FR-012) | Meshing on the render thread blocks frames; thread pool competes with simulation |
| LOD 1–3 with skirts | Whole 4096 map on screen at 60 FPS (US4) | Full detail everywhere: tens of millions of triangles at far zoom |
