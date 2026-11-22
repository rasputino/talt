using Talt.Core.Meshing;
using Talt.Core.Voxels;
using static Talt.Core.Tests.Meshing.MeshTestWorlds;

namespace Talt.Core.Tests.Meshing;

public class GreedyMesherTests
{
    public static TheoryData<string> WorldNames => ["hills", "roads", "scattered", "single", "border"];

    private static VoxelWorld CreateWorld(string name)
    {
        switch (name)
        {
            case "hills":
                return Hills();
            case "roads":
                return HillsWithRoads();
            case "scattered":
                return Scattered();
            case "single":
            {
                var world = new VoxelWorld();
                world.SetVoxel(5, 5, 5, VoxelMaterial.Stone);
                return world;
            }

            default:
            {
                // Voxels straddling chunk and region borders in every direction.
                var world = new VoxelWorld();
                for (var x = 120; x < 136; x++)
                {
                    for (var z = 28; z < 36; z++)
                    {
                        world.SetVoxel(x, (x + z) % 3, z, x % 2 == 0 ? VoxelMaterial.Grass : VoxelMaterial.Water);
                    }
                }

                return world;
            }
        }
    }

    [Theory]
    [MemberData(nameof(WorldNames))]
    public void GreedyAndNaive_CoverExactlyTheSameFaces(string name)
    {
        var world = CreateWorld(name);
        foreach (var chunk in world.Chunks)
        {
            var region = RegionCoord.FromChunk(chunk.Coord);
            var greedy = UnitFaces(GreedyMesher.BuildChunk(world, chunk.Coord, merge: true), region);
            var naive = UnitFaces(GreedyMesher.BuildChunk(world, chunk.Coord, merge: false), region);

            Assert.Equal(naive.Count, greedy.Count);
            Assert.True(naive.SetEquals(greedy), $"Chunk {chunk.Coord}: greedy and naive surfaces differ.");
        }
    }

    [Theory]
    [MemberData(nameof(WorldNames))]
    public void NaivePackedMesh_MatchesReferenceMesher_WithoutBottomFaces(string name)
    {
        var world = CreateWorld(name);
        foreach (var chunk in world.Chunks)
        {
            var region = RegionCoord.FromChunk(chunk.Coord);
            var packed = UnitFaces(GreedyMesher.BuildChunk(world, chunk.Coord, merge: false), region);
            var reference = ReferenceFaces(world, ChunkMesher.Build(world, chunk));

            var onlyRef = string.Join("; ", reference.Except(packed).Take(3));
            var onlyPacked = string.Join("; ", packed.Except(reference).Take(3));
            Assert.True(reference.SetEquals(packed), $"Chunk {chunk.Coord}: ref only {onlyRef} / packed only {onlyPacked}");
        }
    }

    [Fact]
    public void NoFacePointsDown_AtTheBottomOfTheWorld()
    {
        var world = Hills();
        foreach (var chunk in world.Chunks)
        {
            var mesh = GreedyMesher.BuildChunk(world, chunk.Coord);
            Assert.DoesNotContain(mesh.Vertices, v => v.FaceIndex == FaceDirections.Down && v.Y == 0);
        }
    }

    [Fact]
    public void ReferenceMesher_DoesDrawBottomFaces_SoTheExclusionIsReal()
    {
        var world = new VoxelWorld();
        world.SetVoxel(3, 0, 3, VoxelMaterial.Stone);

        Assert.Equal(6, ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(0, 0, 0))!).FaceCount);
        Assert.Equal(5, GreedyMesher.BuildChunk(world, new ChunkCoord(0, 0, 0)).QuadCount);
    }

    [Fact]
    public void Greedy_UsesFarFewerQuads_OnTerrain()
    {
        var world = HillsWithRoads();
        int greedy = 0, naive = 0;
        foreach (var chunk in world.Chunks)
        {
            greedy += GreedyMesher.BuildChunk(world, chunk.Coord).QuadCount;
            naive += GreedyMesher.BuildChunk(world, chunk.Coord, merge: false).QuadCount;
        }

        Assert.True(greedy * 2 < naive, $"Greedy {greedy} quads vs naive {naive}.");
    }

    [Fact]
    public void Positions_AreRelativeToTheRegion_AndBoundsAreTight()
    {
        var world = new VoxelWorld();
        world.SetVoxel(130, 4, 260, VoxelMaterial.Grass); // Region (1, 0, 2), local (2, 4, 4).

        var mesh = GreedyMesher.BuildChunk(world, ChunkCoord.FromVoxel(130, 4, 260));

        Assert.Equal((2, 4, 4), mesh.BoundsMin);
        Assert.Equal((3, 5, 5), mesh.BoundsMax);
        Assert.All(mesh.Vertices, v => Assert.InRange(v.X, 2, 3));
    }

    [Fact]
    public void EmptyAndMissingChunks_GiveEmptyMeshes()
    {
        var world = new VoxelWorld();
        world.GetOrCreateChunk(new ChunkCoord(0, 0, 0));

        Assert.True(GreedyMesher.BuildChunk(world, new ChunkCoord(0, 0, 0)).IsEmpty);
        Assert.True(GreedyMesher.BuildChunk(world, new ChunkCoord(5, 0, 5)).IsEmpty);
    }

    [Fact]
    public void Quads_AreCounterClockwiseSeenFromOutside()
    {
        var world = HillsWithRoads(chunks: 2);
        foreach (var chunk in world.Chunks)
        {
            var mesh = GreedyMesher.BuildChunk(world, chunk.Coord);
            for (var q = 0; q < mesh.QuadCount; q++)
            {
                var v = mesh.Vertices.AsSpan(q * 4, 4);
                var (nx, ny, nz) = FaceDirections.Normal(v[0].FaceIndex);
                // Triangle 0-1-2 of the shared index pattern.
                var a = new System.Numerics.Vector3(v[0].X, v[0].Y, v[0].Z);
                var b = new System.Numerics.Vector3(v[1].X, v[1].Y, v[1].Z);
                var c = new System.Numerics.Vector3(v[2].X, v[2].Y, v[2].Z);
                var cross = System.Numerics.Vector3.Cross(b - a, c - a);
                Assert.True(System.Numerics.Vector3.Dot(cross, new(nx, ny, nz)) > 0);
            }
        }
    }

    [Fact]
    public void QuadDiagonal_FollowsTheReferenceRule()
    {
        var world = Hills();
        foreach (var chunk in world.Chunks)
        {
            var mesh = GreedyMesher.BuildChunk(world, chunk.Coord);
            for (var q = 0; q < mesh.QuadCount; q++)
            {
                var v = mesh.Vertices.AsSpan(q * 4, 4);
                // Shared diagonal 0-2 must be the brighter (or equal) one.
                Assert.True(v[0].AmbientOcclusion + v[2].AmbientOcclusion >= v[1].AmbientOcclusion + v[3].AmbientOcclusion);
            }
        }
    }

    [Fact]
    public void RegionLod_HasSkirtsOnAllFourSides_AndNoBottomFaces()
    {
        var world = Hills(chunks: 4);
        for (var lod = 1; lod <= GreedyMesher.MaxLod; lod++)
        {
            var mesh = GreedyMesher.BuildRegionLod(world, new RegionCoord(0, 0, 0), lod);
            var scale = 1 << lod;

            Assert.Contains(mesh.Vertices, v => v.FaceIndex == FaceDirections.IndexOf(-1, 0, 0) && v.X == 0);
            Assert.Contains(mesh.Vertices, v => v.FaceIndex == FaceDirections.IndexOf(1, 0, 0) && v.X == RegionCoord.Size);
            Assert.Contains(mesh.Vertices, v => v.FaceIndex == FaceDirections.IndexOf(0, 0, -1) && v.Z == 0);
            Assert.Contains(mesh.Vertices, v => v.FaceIndex == FaceDirections.IndexOf(0, 0, 1) && v.Z == RegionCoord.Size);
            Assert.DoesNotContain(mesh.Vertices, v => v.FaceIndex == FaceDirections.Down);
            Assert.All(mesh.Vertices, v => Assert.True(v.X % scale == 0 && v.Y % scale == 0 && v.Z % scale == 0));
        }
    }

    [Fact]
    public void RegionLod_QuadCountDecreasesWithLevel()
    {
        var world = HillsWithRoads(chunks: 4);
        var region = new RegionCoord(0, 0, 0);
        var full = region.Chunks().Sum(c => GreedyMesher.BuildChunk(world, c).QuadCount);
        var previous = full;
        for (var lod = 1; lod <= GreedyMesher.MaxLod; lod++)
        {
            var quads = GreedyMesher.BuildRegionLod(world, region, lod).QuadCount;
            Assert.True(quads < previous, $"LOD {lod}: {quads} quads, previous level {previous}.");
            previous = quads;
        }
    }

    [Fact]
    public void RegionLod_OfEmptyRegion_IsEmpty()
    {
        Assert.True(GreedyMesher.BuildRegionLod(new VoxelWorld(), new RegionCoord(3, 0, 3), 2).IsEmpty);
    }

    /// <summary>Unit faces of the reference mesher, excluding faces pointing down at y = 0.</summary>
    private static HashSet<UnitFace> ReferenceFaces(VoxelWorld world, ChunkMesh mesh)
    {
        var faces = new HashSet<UnitFace>();
        for (var f = 0; f < mesh.FaceCount; f++)
        {
            var quad = mesh.Vertices.AsSpan(f * 4, 4);
            var n = quad[0].Normal;
            var faceIndex = FaceDirections.IndexOf((int)n.X, (int)n.Y, (int)n.Z);
            var minX = (int)MathF.Min(MathF.Min(quad[0].Position.X, quad[1].Position.X), MathF.Min(quad[2].Position.X, quad[3].Position.X));
            var minY = (int)MathF.Min(MathF.Min(quad[0].Position.Y, quad[1].Position.Y), MathF.Min(quad[2].Position.Y, quad[3].Position.Y));
            var minZ = (int)MathF.Min(MathF.Min(quad[0].Position.Z, quad[1].Position.Z), MathF.Min(quad[2].Position.Z, quad[3].Position.Z));
            // Faces pointing in a positive direction sit on the far side of their voxel.
            var x = minX - (n.X > 0 ? 1 : 0);
            var y = minY - (n.Y > 0 ? 1 : 0);
            var z = minZ - (n.Z > 0 ? 1 : 0);
            if (faceIndex == FaceDirections.Down && y == 0)
            {
                continue;
            }

            var material = world.GetVoxel(x, y, z);
            var expected = MaterialPalette.BaseColor(material) * MaterialPalette.Variation(x, y, z);
            var ao = new int[4];
            var face = FaceDirections.Faces[faceIndex];
            foreach (var vertex in quad)
            {
                var p = new[] { (int)vertex.Position.X, (int)vertex.Position.Y, (int)vertex.Position.Z };
                var origin = new[] { minX, minY, minZ };
                var cu = p[face.U.Axis] - origin[face.U.Axis];
                var cv = p[face.V.Axis] - origin[face.V.Axis];
                var corner = Array.IndexOf(FaceDirections.Corners, (cu, cv));
                var brightness = vertex.Color.X / expected.X;
                ao[corner] = Enumerable.Range(0, 4).MinBy(i => MathF.Abs(FaceDirections.Brightness(i) - brightness));
            }

            faces.Add(new UnitFace(faceIndex, x, y, z, material, ao[0], ao[1], ao[2], ao[3]));
        }

        return faces;
    }
}
