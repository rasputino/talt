# Architecture

This document describes how the code is organized today. Update it whenever the structure changes.

## Solution layout

```
Talt.slnx
├── src/
│   ├── Talt.Core/          Game logic. No graphics, no window, no platform code.
│   │   ├── Voxels/         Chunk, ChunkCoord, VoxelWorld, VoxelMaterial
│   │   ├── Terrain/        ValueNoise, TerrainGenerator, Heightmap
│   │   ├── Meshing/        ChunkMesher, ChunkMesh, VoxelVertex, MaterialPalette
│   │   ├── Network/        TransportNetwork (+ builder), PathFinder (A*), GridNetworkGenerator,
│   │   │                   NetworkTerrainPainter
│   │   └── Simulation/     VehicleFleet, SyntheticTraffic
│   └── Talt.Client/        Executable: window, input, rendering and the simulation loop.
│       ├── GameWindow.cs   Window lifecycle, camera controls, simulation tick, stats in the title bar
│       └── Rendering/      WebGpuRenderer, OrbitCamera, UnitCube, Shaders/*.wgsl
├── tests/
│   └── Talt.Core.Tests/    xUnit tests for Talt.Core
└── benchmarks/
    └── Talt.Benchmarks/    BenchmarkDotNet: vehicle tick and pathfinding
```

**Rule:** `Talt.Core` must never reference graphics or windowing libraries. Everything in it must be
testable without a GPU. The client depends on the core, never the other way around.

## Build configuration

- `Directory.Build.props`: shared settings for every project (.NET 10, nullable, warnings as errors,
  recommended analyzers).
- `Directory.Packages.props`: NuGet versions are managed centrally. Projects reference packages
  without a version.

## World model

- The world is made of **voxels**, one byte each (`VoxelMaterial`).
- Voxels are grouped into **chunks** of 32 × 32 × 32, stored as a flat array
  (`index = x + z * 32 + y * 32²`). Y is the vertical axis.
- `VoxelWorld` is a sparse dictionary of chunks. Missing chunks are air.
- `ChunkCoord.FromVoxel` uses floor division, so negative coordinates work.

## Terrain generation

`TerrainGenerator` builds a heightmap from fractal value noise (`ValueNoise`) and fills columns:
grass, snow or sand on top, dirt below, stone underneath, water up to the water level.
Same seed, same terrain. This is prototype quality and will be replaced by a proper map generator.

## Meshing

`ChunkMesher` turns a chunk into triangles:

1. Copies the chunk plus a one-voxel border from neighbours into a padded 34³ buffer, so
   neighbour lookups are plain array reads and faces on chunk borders are culled correctly.
2. Emits a quad only where a solid voxel touches air (face culling).
3. Computes per-vertex **ambient occlusion** from the three voxels around each corner and splits
   each quad along its brighter diagonal to avoid artifacts.
4. Applies a deterministic ±5% brightness variation per voxel for the voxel art look.

Vertices are in world space (`VoxelVertex`: position, normal, color = 36 bytes). Chunks can be meshed
in parallel because meshing only reads the world.

Possible future optimizations: greedy meshing (merge coplanar faces), chunk-relative positions with
compressed vertices, LOD meshes for distant chunks.

## Transport network

- `TransportNetwork` is an immutable directed graph. Nodes are junctions or stations with a ground
  position (in tiles); edges are one-way straight segments. A two-way road is two edges.
- Adjacency is stored in compressed sparse row (CSR) form: flat arrays, no per-node objects, so
  traversals are cache friendly and allocation free. `TransportNetworkBuilder` assembles it.
- `PathFinder` runs A* with a straight-line heuristic. It reuses its buffers between searches (a
  "visit stamp" avoids clearing them) and is not thread safe: one instance per thread.
- `GridNetworkGenerator` builds a synthetic road grid with some roads removed, for tests,
  benchmarks and the prototype. `NetworkTerrainPainter` turns the top voxel under each road into
  road material so the terrain mesher draws it.

## Vehicle simulation

`VehicleFleet` simulates vehicles running on **lines**. A line is a closed loop of edges with two
stops: the start of the route and the start of the way back. Vehicles accelerate, cruise, brake so
they stop exactly at the next stop (the remaining distance to each stop is precomputed per line
edge), wait three seconds and continue.

Design choices for performance:

- **Structure of arrays:** one array per field (line, route index, distance, speed, max speed,
  dwell time) instead of one object per vehicle.
- **No allocations per tick:** verified by a unit test and by the benchmark's memory diagnoser
  (only `Parallel.For` itself allocates a few kilobytes per tick, independent of vehicle count).
- **Parallel batches:** vehicles are updated in batches of 4096 with `Parallel.For`. Parallel and
  sequential ticks give identical results (unit test).

**Not modeled yet:** vehicles do not interact. They overlap on roads, ignore each other at
junctions and there are no signals or blocks. Interactions are the next big step and the main
threat to parallelism, because neighbouring vehicles depend on each other.

`SyntheticTraffic` creates random lines between junctions and spreads vehicles evenly along them.

## Benchmarks

`benchmarks/Talt.Benchmarks` uses BenchmarkDotNet on a 4096 × 4096 tile network
(257 × 257 junctions, one every 16 tiles, ~220 000 one-way edges, 2 000 lines).

```
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter * --job short
```

Results on the development machine (AMD Ryzen 7 160, 8 cores / 16 threads, short job):

| Benchmark | Sequential | Parallel |
|---|---|---|
| Tick, 10 000 vehicles | 0.17 ms | 0.11 ms |
| Tick, 100 000 vehicles | 3.7 ms | 0.35 ms |
| Tick, 1 000 000 vehicles | 21.9 ms | 6.0 ms |
| A* across the whole map (random pairs) | 1.27 ms per search | — |

With 1 000 000 vehicles the tick is limited by memory access (random reads of edge and line data),
which is why the parallel speed-up is lower than with 100 000. Even so, one million independent
vehicles fit in a 60 FPS frame. Long A* searches at ~1 ms each mean route recalculation must be
cached and spread over ticks, which the vision already plans (hierarchical, cached pathfinding).

## Rendering

- **Silk.NET** creates the window (GLFW) and provides the WebGPU bindings.
- **WebGPU** through wgpu-native, which uses Vulkan, Metal or DirectX 12 depending on the platform.
  On the development machine (Windows, AMD) it runs on Vulkan.
- `WebGpuRenderer` owns all GPU objects: a terrain pipeline with one vertex + index buffer per
  chunk, a vehicle pipeline, and one uniform buffer (view-projection, light, camera, sky color)
  shared by both.
- `voxel.wgsl`: directional light plus sky light, baked ambient occlusion, distance fog.
- `vehicles.wgsl`: **instancing**. A single unit cube is drawn once per vehicle in one draw call;
  each instance carries position, heading and color, uploaded every frame.
- Colors are linear. The renderer prefers an sRGB surface format so the GPU converts them.
- GPU resources are released after the window loop has finished, never inside window events.

## Camera and controls

`OrbitCamera` orbits a target point on the ground. The target follows the terrain height and stays
inside the map, and the camera tilts up automatically so it never goes inside a hill.

| Input | Action |
|---|---|
| Left mouse drag | Rotate |
| Right mouse drag, WASD, arrows | Pan |
| Mouse wheel | Zoom |
| Q / E | Rotate |
| Space | Pause / resume the simulation |
| + / - | Simulation speed (x0.25 to x16) |
| Esc | Quit |

## Current numbers (prototype, Windows, AMD Radeon integrated GPU)

| Map | Chunks | Generation | Meshing | Triangles | Vehicles | Tick | FPS |
|---|---|---|---|---|---|---|---|
| 512 × 512 | 256 | ~190 ms | ~220 ms | ~1.25 M | 20 000 | ~0.4 ms | 60 (VSync) |

Known issues reported while testing the 2048 × 2048 map: panning is a bit jerky (about 16 M
terrain triangles are drawn every frame with no culling or level of detail yet).
