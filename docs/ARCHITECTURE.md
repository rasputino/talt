# Architecture

This document describes how the code is organized today. Update it whenever the structure changes.

## Solution layout

```
Talt.slnx
├── src/
│   ├── Talt.Core/          Game logic. No graphics, no window, no platform code.
│   │   ├── Voxels/         Chunk, ChunkCoord, VoxelWorld, VoxelMaterial
│   │   ├── Terrain/        ValueNoise, TerrainGenerator
│   │   └── Meshing/        ChunkMesher, ChunkMesh, VoxelVertex, MaterialPalette
│   └── Talt.Client/        Executable: window, input and rendering.
│       ├── GameWindow.cs   Window lifecycle, camera controls, FPS in the title bar
│       └── Rendering/      WebGpuRenderer, OrbitCamera, Shaders/voxel.wgsl
└── tests/
    └── Talt.Core.Tests/    xUnit tests for Talt.Core
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

## Rendering

- **Silk.NET** creates the window (GLFW) and provides the WebGPU bindings.
- **WebGPU** through wgpu-native, which uses Vulkan, Metal or DirectX 12 depending on the platform.
  On the development machine (Windows, AMD) it runs on Vulkan.
- `WebGpuRenderer` owns all GPU objects: one render pipeline, one uniform buffer
  (view-projection, light, camera, sky color), and one vertex + index buffer per chunk.
- `voxel.wgsl`: directional light plus sky light, baked ambient occlusion, distance fog.
- Colors are linear. The renderer prefers an sRGB surface format so the GPU converts them.
- GPU resources are released after the window loop has finished, never inside window events.

## Camera and controls

`OrbitCamera` orbits a target point on the ground.

| Input | Action |
|---|---|
| Left mouse drag | Rotate |
| Right mouse drag, WASD, arrows | Pan |
| Mouse wheel | Zoom |
| Q / E | Rotate |
| Esc | Quit |

## Current numbers (prototype, Windows, AMD Radeon integrated GPU)

| Map | Chunks | Generation | Meshing | Triangles | FPS |
|---|---|---|---|---|---|
| 512 × 512 | 256 | ~190 ms | ~220 ms | ~1.25 M | 60 (VSync) |
