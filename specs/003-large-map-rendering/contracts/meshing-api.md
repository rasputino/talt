# Contract: Meshing API (`Talt.Core.Meshing`)

Public surface used by the client, the tests and the benchmarks. Sketch, not final code.

```csharp
public readonly record struct PackedVertex(uint Word0, uint Word1)
{
    public const int SizeInBytes = 8;
    public static PackedVertex Create(int x, int y, int z, int normalIndex, VoxelMaterial material, int ao);
    public int X { get; } public int Y { get; } public int Z { get; }
    public int NormalIndex { get; } public VoxelMaterial Material { get; } public int AmbientOcclusion { get; }
}

public sealed class PackedMesh
{
    public PackedVertex[] Vertices { get; }
    public int QuadCount { get; }
    public (int X, int Y, int Z) BoundsMin { get; }   // region-relative voxels
    public (int X, int Y, int Z) BoundsMax { get; }
    public bool IsEmpty { get; }
    public static PackedMesh Empty { get; }
}

public static class GreedyMesher
{
    public const int MaxLod = 3;
    public static PackedMesh BuildChunk(VoxelWorld world, ChunkCoord chunk, bool merge = true);
    public static PackedMesh BuildRegionLod(VoxelWorld world, RegionCoord region, int lod); // lod 1..MaxLod
}

public static class MaterialPalette
{
    public static Vector3 BaseColor(VoxelMaterial material);           // unchanged
    public static float Variation(int x, int y, int z);                // ±5 %, same hash as before
}
```

Guarantees:

- `BuildChunk(merge: true)` and `BuildChunk(merge: false)` cover exactly the same set of unit faces
  with the same material and per-corner AO (test).
- `BuildChunk(merge: false)` produces the same faces (positions, normals) as the reference
  `ChunkMesher.Build`, except faces pointing down at y = 0 (test).
- Thread-safe as long as the world is not modified concurrently in a way that creates chunks;
  concurrent voxel writes inside existing chunks produce a possibly stale mesh, never a crash.
- `ChunkMesher` (36-byte `VoxelVertex`) stays as the reference implementation for tests and
  benchmarks; the client no longer uses it.

# Contract: Shader input (`voxel.wgsl`)

- Vertex buffer 0, step vertex, stride 8: `@location(0) packed: vec2<u32>` (`Uint32x2`).
- Vertex buffer 1, step instance, stride 16: `@location(1) region_origin: vec4<f32>` (xyz origin in
  world voxels). Draw with `firstInstance` = region slot.
- Uniforms gain `palette: array<vec4<f32>, 8>` (linear base colours by material).
- Fragment colour = palette[material] × Variation(voxel) × AoBrightness(interpolated ao), then the
  existing lighting and fog, unchanged.
