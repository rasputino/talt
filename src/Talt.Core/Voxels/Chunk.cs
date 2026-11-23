namespace Talt.Core.Voxels;

/// <summary>
/// A cube of <see cref="Size"/>³ voxels stored contiguously in memory.
/// Index layout is X-major inside rows, then Z, then Y: index = x + z * Size + y * Size².
/// </summary>
public sealed class Chunk
{
    public const int SizeBits = 5;
    public const int Size = 1 << SizeBits; // 32
    public const int Mask = Size - 1;
    public const int VoxelCount = Size * Size * Size;

    private readonly VoxelMaterial[] _voxels = new VoxelMaterial[VoxelCount];

    public Chunk(ChunkCoord coord)
    {
        Coord = coord;
    }

    public ChunkCoord Coord { get; }

    /// <summary>True when the chunk contains no solid voxels.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var voxel in _voxels)
            {
                if (voxel != VoxelMaterial.Air)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Gets the voxel at local coordinates (0 ≤ x, y, z &lt; Size).</summary>
    public VoxelMaterial Get(int x, int y, int z) => _voxels[Index(x, y, z)];

    /// <summary>Sets the voxel at local coordinates (0 ≤ x, y, z &lt; Size).</summary>
    public void Set(int x, int y, int z, VoxelMaterial material) => _voxels[Index(x, y, z)] = material;

    /// <summary>All voxels in index order (see the class remarks), for fast bulk reads.</summary>
    internal ReadOnlySpan<VoxelMaterial> Voxels => _voxels;

    /// <summary>All voxels for bulk writing by the map builder (same layout as <see cref="Voxels"/>).</summary>
    internal Span<VoxelMaterial> WritableVoxels => _voxels;

    internal static int Index(int x, int y, int z)
    {
        if ((uint)x >= Size || (uint)y >= Size || (uint)z >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Local coordinates ({x}, {y}, {z}) are outside the chunk.");
        }

        return x + (z << SizeBits) + (y << (SizeBits * 2));
    }
}
