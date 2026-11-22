using Talt.Core.Voxels;

namespace Talt.Core.Meshing;

/// <summary>
/// A region of the world downsampled for a simplified level of detail: every block of
/// <see cref="Scale"/>³ voxels becomes one cell. Stored with a one-cell border so the mesher can read
/// neighbours without bounds checks.
/// </summary>
/// <remarks>
/// <para>A block is solid when at least half of its voxels are solid, which keeps the terrain's
/// average height.</para>
/// <para>The material of a block on the surface (no solid block above it) comes from the real surface
/// voxels of its columns (the topmost solid voxel in the block and the block above it). Roads win
/// when at least half a road width of columns (<see cref="Scale"/> / 2) are road: a one-tile road
/// crossing a block always covers <see cref="Scale"/> columns, so the road network stays continuous
/// at every level. Otherwise the most frequent surface material wins. Blocks below the surface (seen
/// only on cliffs and the region's side walls) take the most frequent material of their voxels.</para>
/// <para>Everything outside the region is air, so simplified meshes carry their own side walls
/// ("skirts") that close any gap to a neighbour drawn at another level. Below the world is solid, so
/// the bottom of the world is never drawn.</para>
/// </remarks>
public sealed class LodVolume
{
    private const int MaterialCount = 8;

    private readonly VoxelMaterial[] _cells;

    private LodVolume(int lod, int sizeX, int sizeY, int sizeZ, VoxelMaterial[] cells)
    {
        Lod = lod;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _cells = cells;
    }

    /// <summary>Level of detail, 1 to <see cref="GreedyMesher.MaxLod"/>.</summary>
    public int Lod { get; }

    /// <summary>Voxels per cell along each axis (2^<see cref="Lod"/>).</summary>
    public int Scale => 1 << Lod;

    /// <summary>Cells along X (without the border).</summary>
    public int SizeX { get; }

    /// <summary>Cells along Y (without the border); covers the region's existing chunks only.</summary>
    public int SizeY { get; }

    /// <summary>Cells along Z (without the border).</summary>
    public int SizeZ { get; }

    /// <summary>Cells with a one-cell border, indexed like <see cref="PaddedIndex"/>.</summary>
    internal VoxelMaterial[] Cells => _cells;

    /// <summary>Cell at (x, y, z); each coordinate may be −1 or the size (border).</summary>
    public VoxelMaterial Get(int x, int y, int z) => _cells[PaddedIndex(x, y, z)];

    internal int PaddedIndex(int x, int y, int z) => (x + 1) + (z + 1) * (SizeX + 2) + (y + 1) * (SizeX + 2) * (SizeZ + 2);

    /// <summary>Builds the downsampled volume of a region at the given level of detail.</summary>
    public static LodVolume Build(VoxelWorld world, RegionCoord region, int lod)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfLessThan(lod, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lod, GreedyMesher.MaxLod);

        var reader = new RegionReader(world, region);
        var scale = 1 << lod;
        var sizeXZ = RegionCoord.Size / scale;
        var sizeY = reader.ExtentY / scale;
        var volume = new LodVolume(lod, sizeXZ, sizeY, sizeXZ, new VoxelMaterial[(sizeXZ + 2) * (sizeY + 2) * (sizeXZ + 2)]);

        if (region.Y == 0)
        {
            // Below the world counts as solid, so no faces are built at the bottom of the world.
            for (var z = -1; z <= sizeXZ; z++)
            {
                for (var x = -1; x <= sizeXZ; x++)
                {
                    volume._cells[volume.PaddedIndex(x, -1, z)] = GreedyMesher.BelowWorld;
                }
            }
        }

        if (sizeY == 0)
        {
            return volume;
        }

        // Pass 1: count solid voxels and materials per block.
        var blockCount = sizeXZ * sizeY * sizeXZ;
        var solidCounts = new int[blockCount];
        var materialCounts = new int[blockCount * MaterialCount];
        for (var ly = 0; ly < reader.ExtentY; ly++)
        {
            for (var lz = 0; lz < RegionCoord.Size; lz++)
            {
                for (var lx = 0; lx < RegionCoord.Size; lx++)
                {
                    var material = reader.Get(lx, ly, lz);
                    if (material == VoxelMaterial.Air)
                    {
                        continue;
                    }

                    var block = (lx >> lod) + (lz >> lod) * sizeXZ + (ly >> lod) * sizeXZ * sizeXZ;
                    solidCounts[block]++;
                    materialCounts[block * MaterialCount + (int)material]++;
                }
            }
        }

        var half = scale * scale * scale / 2;
        bool IsSolidBlock(int bx, int by, int bz) =>
            by < sizeY && solidCounts[bx + bz * sizeXZ + by * sizeXZ * sizeXZ] >= half;

        // Pass 2: choose the material of every solid block.
        Span<int> surfaceCounts = stackalloc int[MaterialCount];
        for (var by = 0; by < sizeY; by++)
        {
            for (var bz = 0; bz < sizeXZ; bz++)
            {
                for (var bx = 0; bx < sizeXZ; bx++)
                {
                    if (!IsSolidBlock(bx, by, bz))
                    {
                        continue;
                    }

                    var block = bx + bz * sizeXZ + by * sizeXZ * sizeXZ;
                    var material = IsSolidBlock(bx, by + 1, bz)
                        ? MostFrequent(materialCounts.AsSpan(block * MaterialCount, MaterialCount))
                        : SurfaceMaterial(reader, bx, by, bz, scale, surfaceCounts);
                    volume._cells[volume.PaddedIndex(bx, by, bz)] = material;
                }
            }
        }

        return volume;
    }

    private static VoxelMaterial SurfaceMaterial(RegionReader reader, int bx, int by, int bz, int scale, Span<int> counts)
    {
        counts.Clear();
        var y0 = by * scale;
        var yTop = Math.Min(y0 + 2 * scale, reader.ExtentY) - 1;
        for (var dz = 0; dz < scale; dz++)
        {
            for (var dx = 0; dx < scale; dx++)
            {
                for (var y = yTop; y >= y0; y--)
                {
                    var material = reader.Get(bx * scale + dx, y, bz * scale + dz);
                    if (material != VoxelMaterial.Air)
                    {
                        counts[(int)material]++;
                        break;
                    }
                }
            }
        }

        return counts[(int)VoxelMaterial.Road] >= Math.Max(1, scale / 2) ? VoxelMaterial.Road : MostFrequent(counts);
    }

    /// <summary>Most frequent solid material; ties go to the lower material value. Stone when empty.</summary>
    private static VoxelMaterial MostFrequent(ReadOnlySpan<int> counts)
    {
        var best = VoxelMaterial.Stone;
        var bestCount = 0;
        for (var m = 1; m < counts.Length; m++)
        {
            if (counts[m] > bestCount)
            {
                best = (VoxelMaterial)m;
                bestCount = counts[m];
            }
        }

        return best;
    }

    /// <summary>Fast voxel reads inside one region, straight from the chunk arrays.</summary>
    private readonly struct RegionReader
    {
        private const int Side = RegionCoord.ChunksPerSide;

        private readonly Chunk?[] _chunks;

        public RegionReader(VoxelWorld world, RegionCoord region)
        {
            _chunks = new Chunk?[Side * Side * Side];
            var top = 0;
            foreach (var coord in region.Chunks())
            {
                var chunk = world.GetChunk(coord);
                if (chunk is null)
                {
                    continue;
                }

                var (x, y, z) = RegionCoord.LocalChunk(coord);
                _chunks[x + z * Side + y * Side * Side] = chunk;
                top = Math.Max(top, (y + 1) * Chunk.Size);
            }

            ExtentY = top;
        }

        /// <summary>Height (voxels, region-relative) up to which the region has chunks.</summary>
        public int ExtentY { get; }

        public VoxelMaterial Get(int x, int y, int z)
        {
            var chunk = _chunks[(x >> Chunk.SizeBits) + (z >> Chunk.SizeBits) * Side + (y >> Chunk.SizeBits) * Side * Side];
            return chunk is null
                ? VoxelMaterial.Air
                : chunk.Voxels[(x & Chunk.Mask) + ((z & Chunk.Mask) << Chunk.SizeBits) + ((y & Chunk.Mask) << (Chunk.SizeBits * 2))];
        }
    }
}
