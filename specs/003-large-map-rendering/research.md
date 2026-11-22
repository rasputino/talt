# Research: Large-Map Rendering

Decisions for feature 003. Baseline (see `docs/ARCHITECTURE.md`, "Verification status"): the
prototype meshes every chunk naively (one quad per visible voxel face, 36-byte `VoxelVertex`, 32-bit
indices, 168 bytes per face), uploads one vertex and one index buffer per chunk and draws all of
them every frame. 4096 × 4096 = 16 384 chunks, 38 M faces, about 6.4 GB.

## R1 — Greedy meshing that keeps the exact look

- **Decision**: Per chunk, for each of the six face directions and each slice, build a 2D mask of
  visible faces keyed by (material, four corner AO values) and merge rectangles greedily. Faces merge
  only when the merged quad interpolates to exactly the per-face result: faces with uniform AO merge
  in both directions; faces whose AO is constant along one axis merge only along that axis; other
  faces stay 1 × 1. The ±5 % per-voxel colour variation moves from vertex colour to the fragment
  shader, which recomputes the same integer hash from the voxel coordinate
  (`floor(worldPosition − normal × 0.5)`), so faces with different variation can still merge.
- **Rationale**: Merging on "equal AO tuple" alone (common shortcut) stretches one face's gradient
  over the whole quad and changes the look; merging on exact colour would merge almost nothing
  because every voxel has its own variation. With the variation in the shader, flat ground and water
  (most of the map) merge into large quads while shading stays identical (FR-001, FR-002).
- **Alternatives**: Keep per-vertex colour (merges ~nothing); drop the variation (changes the look,
  rejected by clarify guidance); AO in a 3D texture sampled per fragment (bigger change, more memory).

## R2 — Packed region-relative vertices

- **Decision**: 8-byte vertex = two `uint32`: word 0 = x | y << 8 | z << 16 | normal index << 24
  (positions 0–128 relative to the region origin, in voxels); word 1 = material | AO level << 8.
  Quads always use the index pattern 0-1-2 / 0-2-3; the mesher rotates the four vertices when the
  other diagonal is needed for smooth AO, so one shared, static index buffer serves every draw.
  The region origin comes from a per-instance vertex attribute (`firstInstance` = region slot).
  Materials resolve to colours through a palette in the uniform buffer.
- **Rationale**: 32 bytes per quad including indices vs 168 today (5.25 × smaller, FR-011 ≥ 3 ×).
  Plain vertex attributes (`Uint32x2`) are easy to read in the shader and debug. 8 bits per axis
  covers a 128-voxel region including its far edge.
- **Alternatives**: one `uint32` per vertex (fits today with 3 material bits but leaves no room for
  more materials); one 64-bit record per quad with vertex pulling from a storage buffer (4 × smaller
  again but harder to debug; not needed for the 2 GB budget).

## R3 — Regions as the unit of drawing and LOD

- **Decision**: A region is 4 × 4 × 4 chunks (128³ voxels). Full-detail (LOD 0) meshes are built per
  chunk (the unit of edits) and concatenated into one GPU vertex buffer per region; simplified meshes
  (LOD 1–3) are built per region. One draw call per visible region. When one chunk changes, the new
  region buffer is assembled on the GPU: unchanged chunk ranges are copied buffer-to-buffer from the
  old region buffer and only the changed chunk is uploaded from the CPU, so no CPU copy of the meshes
  is kept.
- **Rationale**: 1 024 regions on a 4096 map (16 on 512) bound draw calls to about 1 000 even with
  the whole map on screen; 16 384 per-chunk draws would cost several milliseconds of driver time on
  the reference machine, too much for the 8 ms fast-forward budget. Per-chunk meshing keeps edits
  cheap (FR-007).
- **Alternatives**: per-chunk buffers and draws (draw-call bound); one global buffer with indirect
  multi-draw (needs a native-only wgpu feature and a sub-allocator; revisit if profiling demands);
  keeping CPU copies (doubles memory).

## R4 — Culling

- **Decision**: On the CPU each frame: extract six planes from the view-projection matrix and test
  each region's tight bounding box (from its mesh); skip regions with no geometry; skip regions whose
  nearest point is beyond the full-fog distance (they are drawn entirely in sky colour today, so the
  image does not change). The mesher never emits faces pointing down at the bottom of the world
  (y = 0) and treats voxels below the world as solid.
- **Rationale**: ~1 000 box tests per frame cost microseconds; fog culling is free and invisible.
- **Alternatives**: GPU culling with compute (unneeded at this region count); occlusion queries
  (terrain is mostly a heightfield seen from above, little to gain).

## R5 — Level of detail

- **Decision**: Four levels: LOD k downsamples by s = 2^k (1, 2, 4, 8). The downsampled block is
  solid when at least half its voxels are solid. Surface material uses the true surface voxels of the
  block's columns (the topmost solid voxel within the block and the block above it): **road wins**
  when at least s / 2 columns are road (a 1-tile road crossing a block always covers s columns, so
  the road network stays continuous), otherwise the most frequent material. The simplified volume
  is meshed by the same greedy mesher with a voxel scale s. Level per region per frame: from the
  nearest distance d of its bounding box, pixels per world unit p = screenHeight / (2 d tan(fov/2));
  choose the largest k with s × p < 2 px, with one level of hysteresis margin (10 %) to avoid
  flicker. Seams: simplified meshes treat everything outside the region as air, so they carry their
  own side walls ("skirts") down to the bottom; full-detail meshes keep exact neighbour culling.
  Because detail decreases with distance, the finer side of a boundary faces the camera, and any
  step to a coarser, higher neighbour is closed by that neighbour's skirt.
- **Rationale**: Screen-space error keeps simplification under ~2 px (FR-004); skirts are the
  simplest crack-free approach and cost little after greedy merging (one wall per region side).
  Roads clarified as always visible.
- **Alternatives**: heightmap-only simplification (breaks with future caves/bridges); stitching
  meshes between levels (complex); transvoxel (for smooth terrain, not blocky).
- **Fallback**: while the chosen level of a region is not built yet, draw the nearest available level
  (coarser preferred); if none is ready, the region is not drawn (fills in, FR-012).

## R6 — Asynchronous meshing and edit path

- **Decision**: A `ChunkMeshScheduler` in the client owns the pending work: per-chunk LOD 0 jobs and
  per-region LOD 1–3 jobs. Each frame the render thread queues the most urgent jobs (nearest visible
  first, at most 2 per worker in flight) to dedicated background worker threads (processor count − 2,
  at least 1, below-normal priority so the simulation and render threads keep their cores). Workers
  return packed meshes through a concurrent queue; the render thread uploads them within a per-frame
  budget (4 MB or 2 ms). Edits mark the chunk and every neighbour chunk within one voxel (faces and
  AO read one voxel across borders) dirty, plus the region's simplified levels. Each chunk carries a
  version counter; a result built from an older version is uploaded but the chunk stays dirty.
- **Rationale**: Keeps every frame short (FR-008), gives nearest-first fill-in at start (FR-012),
  and a single-tile edit costs one or a few chunk meshes (~1 ms each) plus one region upload, far
  below 100 ms (FR-007). Version counters avoid locks on the voxel data: a mesh built during an edit
  is simply rebuilt.
- **Alternatives**: `Parallel.For` per frame (blocks the frame); the shared thread pool (competes
  with the simulation's `Parallel.For` in fast forward); locking chunks (contention on the hot path).
- **Thread-safety constraint**: workers only read voxels; edits happen on the main thread inside
  existing chunks; chunk creation is not allowed while the renderer runs (the debug edit stays inside
  the world's height). Documented on `VoxelWorld`.

## R7 — Camera reach and depth precision

- **Decision**: The camera's maximum distance becomes max(2000, 2 × map side) so the whole map fits
  on screen at the farthest zoom (FR-015). The fog formula is unchanged (fog starts at 1.25 ×
  distance and is full at 3.75 × distance), so at the farthest zoom nothing on the map is fogged. The
  near plane scales with the distance (max(0.5, 0.02 × distance)) so 32-bit depth keeps enough
  precision at 7 000+ units; the camera is always at least 0.24 × distance above the target, so
  nothing is closer than the near plane.
- **Rationale**: Clarified: the whole map must be visible. The near plane change is invisible.
- **Alternatives**: reversed-Z depth (better, but changes depth compare and clear; not needed yet).

## R8 — Statistics and edit latency

- **Decision**: The renderer fills a `RenderStatistics` struct each frame (draw calls, triangles,
  visible regions per LOD, world GPU bytes, pending jobs). The window keeps frame-time average and
  worst over the last second. F3 switches the title between game status and these numbers; while
  shown they are also printed to the console every second. PageUp / PageDown raise / lower the tile
  under the camera target (Shift: a 32 × 32 area); the client prints the time from the edit to the
  first presented frame that contains all affected meshes.
- **Rationale**: Clarified: title + console. Edit latency printed by the game makes SC-002
  measurable without a stopwatch.

## R9 — Startup

- **Decision**: `Program` no longer meshes before opening the window; it generates terrain, roads and
  the heightmap, opens the window, and the scheduler fills the world nearest-first. Terrain
  generation of a 4096 map is measured; if it exceeds the SC-007 budget, the generator writes chunk
  arrays directly instead of `SetVoxel` per voxel (same output, verified by a test).
- **Rationale**: Clarified: open and fill in.

## R10 — Benchmarks

- **Decision**: `ChunkMeshingBenchmarks` (BenchmarkDotNet): reference naive mesher vs greedy mesher
  on representative chunks (flat water, hills, road crossing), plus a region LOD 1–3 build. The
  client's render statistics give the end-to-end numbers for 512, 2048 and 4096 maps, recorded by
  hand in `docs/ARCHITECTURE.md`.
- **Rationale**: Constitution II requires a benchmark for rendering changes; GPU timing cannot run in
  BenchmarkDotNet, so the window numbers are recorded manually.
