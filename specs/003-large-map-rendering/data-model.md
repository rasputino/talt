# Data Model: Large-Map Rendering

All entities are rendering data derived from the voxel world; none is simulation state, so
determinism and state fingerprints are unaffected (FR-013).

## Core (`Talt.Core`, no GPU dependency)

### PackedVertex (`Talt.Core.Meshing`)

8-byte vertex, layout shared with `voxel.wgsl`.

| Field | Bits | Meaning |
|---|---|---|
| `Word0` bits 0–7 | x | Position relative to the region origin, voxels, 0–128 |
| `Word0` bits 8–15 | y | Same, vertical |
| `Word0` bits 16–23 | z | Same |
| `Word0` bits 24–26 | normal | Face direction index 0–5 (+X, −X, +Y, −Y, +Z, −Z) |
| `Word1` bits 0–7 | material | `VoxelMaterial` value (never Air) |
| `Word1` bits 8–9 | ao | Ambient occlusion level 0–3 (0 darkest) |

Validation: positions ≤ 128; normal ≤ 5; material ≠ Air. Constructed only by the mesher.

### PackedMesh

| Field | Type | Meaning |
|---|---|---|
| `Vertices` | `PackedVertex[]` | 4 per quad; quad i uses vertices 4i..4i+3 with indices 0-1-2, 0-2-3 |
| `QuadCount` | int | `Vertices.Length / 4` |
| `BoundsMin`, `BoundsMax` | voxel box, region-relative | Tight box of all vertices (culling) |
| `IsEmpty` | bool | No quads |

### RegionCoord (`Talt.Core.Voxels`)

Position of a region in region units: 4 × 4 × 4 chunks = 128³ voxels. `Origin` (voxels),
`FromChunk(ChunkCoord)`, `Chunks` (the 64 chunk coordinates it covers).

### GreedyMesher

- `BuildChunk(world, chunkCoord, merge = true)` → `PackedMesh` of one chunk at full detail,
  positions relative to the chunk's region origin. `merge = false` gives one quad per face (naive
  reference with the same output format, for tests).
- `BuildRegionLod(world, regionCoord, lod)` → `PackedMesh` of a whole region at LOD 1–3.

Rules: faces only between solid and air; no face pointing down at y = 0 (below the world counts as
solid); AO and diagonal choice identical to the reference `ChunkMesher`; merge only when
interpolation stays exact (research R1).

### LodVolume

Downsampled copy of a region (plus one block of border) for LOD k, scale s = 2^k. Block solid when
≥ half its voxels are solid; material per research R5 (road wins at ≥ s / 2 road columns). Outside
the region: air (skirts); below the world: solid.

### Terrain edits

- `ChunkCoord.TouchedBy(x, y, z)`: every chunk whose mesh can change when voxel (x, y, z) changes —
  the voxel's chunk and any neighbour within one voxel, up to 8 chunks.
- `TerrainEditor.Raise(world, heightmap, x, z)` / `Lower(...)`: debug edits that keep the column a
  valid terrain column and refresh the heightmap; return the changed voxel positions.
- `Heightmap.Refresh(world, x, z)`: recompute one column.

## Client (`Talt.Client.Rendering`)

### RegionRenderState

Per region: GPU vertex buffer and quad count per LOD (0–3, missing = not built), byte offset and
quad count of each chunk inside the LOD 0 buffer, world-space bounding box per LOD, chunk versions
that the uploaded LOD 0 data was built from, current displayed LOD.

State transitions per LOD: `Missing → Queued → Built` (uploaded); an edit moves `Built → Stale`
(still drawn) `→ Queued → Built`.

### MeshJob / MeshResult

`MeshJob` = (kind: chunk LOD 0 | region LOD k, coordinate, version snapshot, priority).
`MeshResult` = job + `PackedMesh` + build time. Produced on worker threads, consumed on the render
thread.

### ChunkMeshScheduler

Dirty chunk set, dirty region-LOD set, in-flight count, worker threads, result queue. Priority =
distance from the camera target to the job's box, visible jobs first.

### Frustum and LodSelector

Six planes from the view-projection matrix; box test. LOD = largest k with
2^k × pixelsPerUnit(nearest distance) < 2 px, with 10 % hysteresis.

### RenderStatistics

Per frame: draw calls, triangles, visible regions per LOD, regions culled, world GPU bytes, pending
and in-flight jobs. Window side: frame time average and worst over the last second; last edit
latency.
