using Talt.Core.Meshing;
using Talt.Core.Network;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Meshing;

/// <summary>Worlds and helpers shared by the meshing tests.</summary>
internal static class MeshTestWorlds
{
    /// <summary>Unit face: direction, voxel cell (world), material and AO per (U, V) corner.</summary>
    internal readonly record struct UnitFace(int Face, int X, int Y, int Z, VoxelMaterial Material, int Ao0, int Ao1, int Ao2, int Ao3);

    public static VoxelWorld Hills(int chunks = 2, int seed = 7)
    {
        var world = Maps.TestWorlds.Square(chunks, seed);
        return world;
    }

    public static VoxelWorld HillsWithRoads(int chunks = 4, int seed = 3)
    {
        var world = Hills(chunks, seed);
        var network = GridNetworkGenerator.Generate(chunks * 2, chunks * 2, spacingTiles: 16, seed: seed, originTileX: 8, originTileZ: 8);
        NetworkTerrainPainter.PaintRoads(world, network);
        return world;
    }

    public static VoxelWorld Scattered(int seed = 11)
    {
        var world = new VoxelWorld();
        var random = new Random(seed);
        for (var i = 0; i < 3000; i++)
        {
            world.SetVoxel(random.Next(64), random.Next(32), random.Next(64), (VoxelMaterial)random.Next(1, 8));
        }

        return world;
    }

    /// <summary>Expands a packed mesh into unit faces in world coordinates (chunk LOD 0 only).</summary>
    public static HashSet<UnitFace> UnitFaces(PackedMesh mesh, RegionCoord region)
    {
        var (ox, oy, oz) = region.Origin;
        var faces = new HashSet<UnitFace>();
        for (var q = 0; q < mesh.QuadCount; q++)
        {
            var quad = mesh.Vertices.AsSpan(q * 4, 4);
            var faceIndex = quad[0].FaceIndex;
            var face = FaceDirections.Faces[faceIndex];
            var material = quad[0].Material;

            // Recover the (U, V) corner of each vertex from its position.
            int minU = int.MaxValue, minV = int.MaxValue, maxU = int.MinValue, maxV = int.MinValue;
            foreach (var vertex in quad)
            {
                var p = new[] { vertex.X, vertex.Y, vertex.Z };
                minU = Math.Min(minU, p[face.U.Axis]);
                maxU = Math.Max(maxU, p[face.U.Axis]);
                minV = Math.Min(minV, p[face.V.Axis]);
                maxV = Math.Max(maxV, p[face.V.Axis]);
            }

            var cornerAo = new int[4];
            var plane = 0;
            foreach (var vertex in quad)
            {
                Assert.Equal(material, vertex.Material);
                Assert.Equal(faceIndex, vertex.FaceIndex);
                var p = new[] { vertex.X, vertex.Y, vertex.Z };
                plane = p[face.Normal.Axis];
                var cu = p[face.U.Axis] == maxU ? 1 : 0;
                var cv = p[face.V.Axis] == maxV ? 1 : 0;
                var corner = Array.IndexOf(FaceDirections.Corners, (cu, cv));
                cornerAo[corner] = vertex.AmbientOcclusion;
            }

            var cellN = face.Normal.Sign > 0 ? plane - 1 : plane;
            for (var u = minU; u < maxU; u++)
            {
                for (var v = minV; v < maxV; v++)
                {
                    var cell = new int[3];
                    cell[face.Normal.Axis] = cellN;
                    cell[face.U.Axis] = u;
                    cell[face.V.Axis] = v;

                    // Bilinear AO at this unit face's corners; exact merges give integers.
                    var ao = new int[4];
                    for (var i = 0; i < 4; i++)
                    {
                        var (cu, cv) = FaceDirections.Corners[i];
                        var fu = (u + cu - minU) / (float)(maxU - minU);
                        var fv = (v + cv - minV) / (float)(maxV - minV);
                        var value = cornerAo[0] * (1 - fu) * (1 - fv) + cornerAo[1] * fu * (1 - fv)
                            + cornerAo[2] * fu * fv + cornerAo[3] * (1 - fu) * fv;
                        var rounded = (int)MathF.Round(value);
                        Assert.True(MathF.Abs(value - rounded) < 1e-4f, "Merged quad interpolates AO to a value no single face had.");
                        ao[i] = rounded;
                    }

                    Assert.True(faces.Add(new UnitFace(faceIndex, ox + cell[0], oy + cell[1], oz + cell[2], material, ao[0], ao[1], ao[2], ao[3])), "Duplicate face.");
                }
            }
        }

        return faces;
    }
}
