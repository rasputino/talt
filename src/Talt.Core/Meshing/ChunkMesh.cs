namespace Talt.Core.Meshing;

/// <summary>
/// Triangle mesh of a chunk: 4 vertices and 6 indices per visible face.
/// </summary>
public sealed class ChunkMesh
{
    public ChunkMesh(VoxelVertex[] vertices, uint[] indices)
    {
        Vertices = vertices;
        Indices = indices;
    }

    public VoxelVertex[] Vertices { get; }

    public uint[] Indices { get; }

    public int FaceCount => Vertices.Length / 4;

    public bool IsEmpty => Indices.Length == 0;
}
