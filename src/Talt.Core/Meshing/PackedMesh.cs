namespace Talt.Core.Meshing;

/// <summary>
/// Quads built by <see cref="GreedyMesher"/>: four <see cref="PackedVertex"/> per quad, drawn with
/// the index pattern 0-1-2, 0-2-3 (see <see cref="QuadIndices"/>).
/// </summary>
public sealed class PackedMesh
{
    public PackedMesh(PackedVertex[] vertices, (int X, int Y, int Z) boundsMin, (int X, int Y, int Z) boundsMax)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Length % 4 != 0)
        {
            throw new ArgumentException("A packed mesh holds four vertices per quad.", nameof(vertices));
        }

        Vertices = vertices;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>A mesh with no quads.</summary>
    public static PackedMesh Empty { get; } = new([], (0, 0, 0), (0, 0, 0));

    public PackedVertex[] Vertices { get; }

    public int QuadCount => Vertices.Length / 4;

    public bool IsEmpty => Vertices.Length == 0;

    /// <summary>Smallest vertex position, in voxels relative to the region origin.</summary>
    public (int X, int Y, int Z) BoundsMin { get; }

    /// <summary>Largest vertex position, in voxels relative to the region origin.</summary>
    public (int X, int Y, int Z) BoundsMax { get; }

    /// <summary>Size of the vertex data in bytes.</summary>
    public int SizeInBytes => Vertices.Length * PackedVertex.SizeInBytes;

    /// <summary>Indices of the two triangles of a quad, relative to its first vertex.</summary>
    public static ReadOnlySpan<uint> QuadIndices => [0, 1, 2, 0, 2, 3];
}
