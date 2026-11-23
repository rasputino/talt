using Talt.Core.Voxels;

namespace Talt.Core.Meshing;

/// <summary>
/// Builds compact meshes (<see cref="PackedMesh"/>) for the renderer: full-detail chunk meshes and
/// simplified region meshes, merging neighbouring faces into larger quads where the result looks
/// exactly the same.
/// </summary>
/// <remarks>
/// <para>Faces exist only between a solid voxel and air, as in <see cref="ChunkMesher"/>, with the same
/// per-vertex ambient occlusion (AO) and the same choice of quad diagonal. Below the world counts as
/// solid, so the bottom of the world is never drawn.</para>
/// <para>Merging works per face direction and slice on a 2D mask of faces keyed by material and the
/// four corner AO levels. The GPU interpolates AO linearly across a quad, so two faces are merged only
/// when that interpolation stays exact: faces with the same AO at all four corners merge in both
/// directions; faces whose AO only changes along V merge along U (and vice versa); any other face stays
/// a single quad. Per-voxel colour variation is applied by the shader from the voxel position, so it
/// does not prevent merging.</para>
/// <para>Thread-safe: scratch buffers are per thread. The world must not gain or lose chunks while a
/// mesh is being built; voxel writes inside existing chunks at most produce a stale mesh.</para>
/// </remarks>
public static class GreedyMesher
{
    /// <summary>Coarsest level of detail (cells of 2^MaxLod voxels).</summary>
    public const int MaxLod = 3;

    /// <summary>
    /// Marker stored in mesher volumes below the world: it hides the faces that point down at the
    /// bottom of the world, but does not darken ambient occlusion, so walls at the map edges keep the
    /// same shading as before the bottom faces were removed.
    /// </summary>
    internal const VoxelMaterial BelowWorld = (VoxelMaterial)0xff;

    private const int ChunkPadded = Chunk.Size + 2;

    [ThreadStatic]
    private static VoxelMaterial[]? t_chunkVolume;

    [ThreadStatic]
    private static int[]? t_mask;

    [ThreadStatic]
    private static List<PackedVertex>? t_vertices;

    /// <summary>
    /// Full-detail mesh of one chunk, with positions relative to the origin of the chunk's region.
    /// </summary>
    /// <param name="world">The world, read for the chunk and a one-voxel border around it.</param>
    /// <param name="coord">The chunk to mesh (a missing chunk is all air).</param>
    /// <param name="merge">False builds one quad per face (the naive reference, for tests).</param>
    public static PackedMesh BuildChunk(VoxelWorld world, ChunkCoord coord, bool merge = true)
    {
        ArgumentNullException.ThrowIfNull(world);

        var volume = t_chunkVolume ??= new VoxelMaterial[ChunkPadded * ChunkPadded * ChunkPadded];
        if (!CopyChunkPadded(world, coord, volume))
        {
            return PackedMesh.Empty;
        }

        var (lx, ly, lz) = RegionCoord.LocalChunk(coord);
        var offset = (lx * Chunk.Size, ly * Chunk.Size, lz * Chunk.Size);
        return Mesh(volume, Chunk.Size, Chunk.Size, Chunk.Size, scale: 1, offset, merge);
    }

    /// <summary>Simplified mesh of a whole region at level of detail 1 to <see cref="MaxLod"/>.</summary>
    public static PackedMesh BuildRegionLod(VoxelWorld world, RegionCoord region, int lod)
    {
        var volume = LodVolume.Build(world, region, lod);
        return volume.SizeY == 0
            ? PackedMesh.Empty
            : Mesh(volume.Cells, volume.SizeX, volume.SizeY, volume.SizeZ, volume.Scale, (0, 0, 0), merge: true);
    }

    /// <summary>
    /// Copies the chunk plus a one-voxel border into <paramref name="volume"/>. Returns false when the
    /// chunk has no solid voxel (nothing to draw).
    /// </summary>
    [ThreadStatic]
    private static Chunk?[]? t_neighbours;

    private static bool CopyChunkPadded(VoxelWorld world, ChunkCoord coord, VoxelMaterial[] volume)
    {
        var chunk = world.GetChunk(coord);
        if (chunk is null)
        {
            return false;
        }

        var (ox, oy, oz) = coord.Origin;
        var voxels = chunk.Voxels;
        var anySolid = false;

        // The 26 neighbours are looked up once per chunk, not once per border voxel (feature 006: the
        // chunk dictionary became concurrent, which made each lookup dearer).
        var neighbours = t_neighbours ??= new Chunk?[27];
        for (var n = 0; n < 27; n++)
        {
            neighbours[n] = n == 13 ? chunk : world.GetChunk(new ChunkCoord(coord.X + (n % 3) - 1, coord.Y + ((n / 3) % 3) - 1, coord.Z + (n / 9) - 1));
        }

        for (var py = 0; py < ChunkPadded; py++)
        {
            int y = py - 1;
            var insideY = (uint)y < Chunk.Size;
            for (var pz = 0; pz < ChunkPadded; pz++)
            {
                int z = pz - 1;
                var insideYZ = insideY && (uint)z < Chunk.Size;
                var row = pz * ChunkPadded + py * ChunkPadded * ChunkPadded;
                for (var px = 0; px < ChunkPadded; px++)
                {
                    int x = px - 1;
                    VoxelMaterial material;
                    if (insideYZ && (uint)x < Chunk.Size)
                    {
                        material = voxels[x + (z << Chunk.SizeBits) + (y << (Chunk.SizeBits * 2))];
                        anySolid |= material != VoxelMaterial.Air;
                    }
                    else if (oy + y < 0)
                    {
                        material = BelowWorld; // Hides bottom faces; never drawn itself.
                    }
                    else
                    {
                        var n = (x < 0 ? 0 : x < Chunk.Size ? 1 : 2) + ((y < 0 ? 0 : y < Chunk.Size ? 1 : 2) * 3) + ((z < 0 ? 0 : z < Chunk.Size ? 1 : 2) * 9);
                        material = neighbours[n]?.Get(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask) ?? VoxelMaterial.Air;
                    }

                    volume[px + row] = material;
                }
            }
        }

        return anySolid;
    }

    /// <summary>
    /// Meshes a padded volume of <paramref name="sizeX"/> × <paramref name="sizeY"/> × <paramref name="sizeZ"/>
    /// cells (plus a one-cell border), each cell <paramref name="scale"/> voxels wide, placed at
    /// <paramref name="offset"/> voxels from the region origin.
    /// </summary>
    private static PackedMesh Mesh(
        VoxelMaterial[] volume, int sizeX, int sizeY, int sizeZ, int scale, (int X, int Y, int Z) offset, bool merge)
    {
        var paddedX = sizeX + 2;
        var paddedZ = sizeZ + 2;
        Span<int> sizes = [sizeX, sizeY, sizeZ];
        Span<int> strides = [1, paddedX * paddedZ, paddedX];
        Span<int> position = stackalloc int[3];
        Span<int> ao = stackalloc int[4];

        var vertices = t_vertices ??= new List<PackedVertex>(4096);
        vertices.Clear();
        var min = (X: int.MaxValue, Y: int.MaxValue, Z: int.MaxValue);
        var max = (X: int.MinValue, Y: int.MinValue, Z: int.MinValue);

        for (var faceIndex = 0; faceIndex < FaceDirections.Count; faceIndex++)
        {
            var face = FaceDirections.Faces[faceIndex];
            int normalAxis = face.Normal.Axis, uAxis = face.U.Axis, vAxis = face.V.Axis;
            var normalSign = face.Normal.Sign;
            int sizeN = sizes[normalAxis], sizeU = sizes[uAxis], sizeV = sizes[vAxis];
            var normalStep = normalSign * strides[normalAxis];
            int uStep = strides[uAxis], vStep = strides[vAxis];
            var first = strides[0] + strides[1] + strides[2]; // Cell (0, 0, 0) inside the border.

            var maskLength = sizeU * sizeV;
            if (t_mask is null || t_mask.Length < maskLength)
            {
                t_mask = new int[Math.Max(maskLength, 128 * 128)];
            }

            var mask = t_mask;

            for (var n = 0; n < sizeN; n++)
            {
                // Build the mask of visible faces in this slice.
                var any = false;
                for (var v = 0; v < sizeV; v++)
                {
                    for (var u = 0; u < sizeU; u++)
                    {
                        var cell = first + n * strides[normalAxis] + u * uStep + v * vStep;
                        var material = volume[cell];
                        var air = cell + normalStep;
                        if (material is VoxelMaterial.Air or BelowWorld || volume[air] != VoxelMaterial.Air)
                        {
                            mask[u + v * sizeU] = 0;
                            continue;
                        }

                        for (var i = 0; i < 4; i++)
                        {
                            var (cu, cv) = FaceDirections.Corners[i];
                            var du = (cu == 1 ? 1 : -1) * uStep;
                            var dv = (cv == 1 ? 1 : -1) * vStep;
                            var side1 = Occludes(volume[air + du]);
                            var side2 = Occludes(volume[air + dv]);
                            var corner = Occludes(volume[air + du + dv]);
                            ao[i] = side1 && side2 ? 0 : 3 - (B(side1) + B(side2) + B(corner));
                        }

                        mask[u + v * sizeU] = 1 << 16 | (int)material | ao[0] << 8 | ao[1] << 10 | ao[2] << 12 | ao[3] << 14;
                        any = true;
                    }
                }

                if (!any)
                {
                    continue;
                }

                // Merge rectangles of equal faces and emit them.
                for (var v = 0; v < sizeV; v++)
                {
                    for (var u = 0; u < sizeU; u++)
                    {
                        var key = mask[u + v * sizeU];
                        if (key == 0)
                        {
                            continue;
                        }

                        int ao0 = (key >> 8) & 3, ao1 = (key >> 10) & 3, ao2 = (key >> 12) & 3, ao3 = (key >> 14) & 3;
                        var constantAlongU = ao0 == ao1 && ao3 == ao2;
                        var constantAlongV = ao0 == ao3 && ao1 == ao2;

                        var width = 1;
                        if (merge && constantAlongU)
                        {
                            while (u + width < sizeU && mask[u + width + v * sizeU] == key)
                            {
                                width++;
                            }
                        }

                        var height = 1;
                        if (merge && constantAlongV)
                        {
                            while (v + height < sizeV && RowMatches(mask, u, v + height, width, sizeU, key))
                            {
                                height++;
                            }
                        }

                        for (var dv = 0; dv < height; dv++)
                        {
                            mask.AsSpan(u + (v + dv) * sizeU, width).Clear();
                        }

                        // Emit the quad's corners in (U, V) order 0..3, rotated so the shared index
                        // pattern 0-1-2 / 0-2-3 splits it along the brighter diagonal.
                        ao[0] = ao0;
                        ao[1] = ao1;
                        ao[2] = ao2;
                        ao[3] = ao3;
                        var start = ao0 + ao2 >= ao1 + ao3 ? 0 : 1;
                        var material = (VoxelMaterial)(key & 0xff);
                        for (var k = 0; k < 4; k++)
                        {
                            var i = (start + k) & 3;
                            var (cu, cv) = FaceDirections.Corners[i];
                            position[normalAxis] = n + (normalSign > 0 ? 1 : 0);
                            position[uAxis] = u + cu * width;
                            position[vAxis] = v + cv * height;
                            var x = offset.X + position[0] * scale;
                            var y = offset.Y + position[1] * scale;
                            var z = offset.Z + position[2] * scale;
                            vertices.Add(PackedVertex.Create(x, y, z, faceIndex, material, ao[i]));
                            min = (Math.Min(min.X, x), Math.Min(min.Y, y), Math.Min(min.Z, z));
                            max = (Math.Max(max.X, x), Math.Max(max.Y, y), Math.Max(max.Z, z));
                        }
                    }
                }
            }
        }

        return vertices.Count == 0 ? PackedMesh.Empty : new PackedMesh([.. vertices], min, max);
    }

    private static bool RowMatches(int[] mask, int u, int v, int width, int sizeU, int key)
    {
        var row = mask.AsSpan(u + v * sizeU, width);
        foreach (var value in row)
        {
            if (value != key)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Occludes(VoxelMaterial material) => material is not (VoxelMaterial.Air or BelowWorld);

    private static int B(bool value) => value ? 1 : 0;
}
