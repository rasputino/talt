using System.Numerics;
using Talt.Core.Voxels;

namespace Talt.Core.Meshing;

/// <summary>
/// Builds a <see cref="ChunkMesh"/> from a chunk, emitting only faces between a solid voxel and air.
/// Applies per-vertex ambient occlusion and a small per-voxel color variation.
/// </summary>
/// <remarks>
/// This is the original naive mesher (one quad per visible face, 36-byte vertices). The game draws
/// <see cref="GreedyMesher"/> output; this class is kept as the reference that tests and benchmarks
/// compare against. Before meshing, the chunk plus a one-voxel border from its neighbours is copied into a padded
/// buffer. This makes neighbour lookups cheap array reads and lets faces on chunk borders be culled
/// correctly.
/// </remarks>
public static class ChunkMesher
{
    private const int Padded = Chunk.Size + 2;

    private static readonly FaceDirections.Face[] Faces = FaceDirections.Faces;
    private static readonly (int U, int V)[] Corners = FaceDirections.Corners;
    private static readonly float[] AoBrightness = FaceDirections.AoBrightness;

    public static ChunkMesh Build(VoxelWorld world, Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(chunk);

        var padded = CopyPadded(world, chunk);
        var (ox, oy, oz) = chunk.Coord.Origin;
        var vertices = new List<VoxelVertex>();
        var indices = new List<uint>();
        Span<int> ao = stackalloc int[4];

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var material = padded[PaddedIndex(x, y, z)];
                    if (!material.IsSolid())
                    {
                        continue;
                    }

                    var color = VoxelColor(material, ox + x, oy + y, oz + z);

                    foreach (var face in Faces)
                    {
                        // Voxel on the other side of the face.
                        var ax = x + face.Normal.X;
                        var ay = y + face.Normal.Y;
                        var az = z + face.Normal.Z;
                        if (padded[PaddedIndex(ax, ay, az)].IsSolid())
                        {
                            continue;
                        }

                        AddFace(vertices, indices, padded, face, x, y, z, ax, ay, az, (ox, oy, oz), color, ao);
                    }
                }
            }
        }

        return new ChunkMesh([.. vertices], [.. indices]);
    }

    private static void AddFace(
        List<VoxelVertex> vertices,
        List<uint> indices,
        VoxelMaterial[] padded,
        FaceDirections.Face face,
        int x, int y, int z,
        int ax, int ay, int az,
        (int X, int Y, int Z) origin,
        Vector3 color,
        Span<int> ao)
    {
        // Faces pointing in a positive direction sit on the far side of the voxel.
        var baseX = x + Math.Max(face.Normal.X, 0);
        var baseY = y + Math.Max(face.Normal.Y, 0);
        var baseZ = z + Math.Max(face.Normal.Z, 0);
        var normal = new Vector3(face.Normal.X, face.Normal.Y, face.Normal.Z);
        var first = (uint)vertices.Count;

        for (var i = 0; i < 4; i++)
        {
            var (cu, cv) = Corners[i];
            var su = cu == 1 ? 1 : -1;
            var sv = cv == 1 ? 1 : -1;

            var side1 = IsSolid(padded, ax + face.U.X * su, ay + face.U.Y * su, az + face.U.Z * su);
            var side2 = IsSolid(padded, ax + face.V.X * sv, ay + face.V.Y * sv, az + face.V.Z * sv);
            var corner = IsSolid(padded,
                ax + face.U.X * su + face.V.X * sv,
                ay + face.U.Y * su + face.V.Y * sv,
                az + face.U.Z * su + face.V.Z * sv);
            ao[i] = side1 && side2 ? 0 : 3 - (B(side1) + B(side2) + B(corner));

            var position = new Vector3(
                origin.X + baseX + face.U.X * cu + face.V.X * cv,
                origin.Y + baseY + face.U.Y * cu + face.V.Y * cv,
                origin.Z + baseZ + face.U.Z * cu + face.V.Z * cv);

            vertices.Add(new VoxelVertex(position, normal, color * AoBrightness[ao[i]]));
        }

        // Split the quad along the brighter diagonal so occlusion gradients look smooth.
        if (ao[0] + ao[2] >= ao[1] + ao[3])
        {
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }
        else
        {
            indices.AddRange([first + 1, first + 2, first + 3, first + 1, first + 3, first]);
        }
    }

    private static VoxelMaterial[] CopyPadded(VoxelWorld world, Chunk chunk)
    {
        var padded = new VoxelMaterial[Padded * Padded * Padded];
        var (ox, oy, oz) = chunk.Coord.Origin;

        for (var py = 0; py < Padded; py++)
        {
            for (var pz = 0; pz < Padded; pz++)
            {
                for (var px = 0; px < Padded; px++)
                {
                    int x = px - 1, y = py - 1, z = pz - 1;
                    var inside = (uint)x < Chunk.Size && (uint)y < Chunk.Size && (uint)z < Chunk.Size;
                    padded[px + pz * Padded + py * Padded * Padded] = inside
                        ? chunk.Get(x, y, z)
                        : world.GetVoxel(ox + x, oy + y, oz + z);
                }
            }
        }

        return padded;
    }

    /// <summary>Index into the padded buffer for chunk-local coordinates in the range [-1, Size].</summary>
    private static int PaddedIndex(int x, int y, int z) => (x + 1) + (z + 1) * Padded + (y + 1) * Padded * Padded;

    private static bool IsSolid(VoxelMaterial[] padded, int x, int y, int z) => padded[PaddedIndex(x, y, z)].IsSolid();

    private static int B(bool value) => value ? 1 : 0;

    /// <summary>Base material color with the deterministic per-voxel brightness variation.</summary>
    private static Vector3 VoxelColor(VoxelMaterial material, int x, int y, int z) =>
        MaterialPalette.BaseColor(material) * MaterialPalette.Variation(x, y, z);
}
