using System.Numerics;
using Talt.Core.Meshing;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Meshing;

public class ChunkMesherTests
{
    [Fact]
    public void EmptyChunk_ProducesEmptyMesh()
    {
        var world = new VoxelWorld();
        var chunk = world.GetOrCreateChunk(new ChunkCoord(0, 0, 0));

        var mesh = ChunkMesher.Build(world, chunk);

        Assert.True(mesh.IsEmpty);
    }

    [Fact]
    public void SingleVoxel_ProducesSixFaces()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, VoxelMaterial.Stone);

        var mesh = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);

        Assert.Equal(6, mesh.FaceCount);
        Assert.Equal(24, mesh.Vertices.Length);
        Assert.Equal(36, mesh.Indices.Length);
    }

    [Fact]
    public void TwoAdjacentVoxels_HideTheSharedFaces()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, VoxelMaterial.Stone);
        world.SetVoxel(6, 5, 5, VoxelMaterial.Stone);

        var mesh = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);

        Assert.Equal(10, mesh.FaceCount);
    }

    [Fact]
    public void FacesOnChunkBorder_AreCulledByNeighbourChunk()
    {
        var world = new VoxelWorld();
        world.SetVoxel(31, 0, 0, VoxelMaterial.Stone);
        world.SetVoxel(32, 0, 0, VoxelMaterial.Stone);

        var left = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);
        var right = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(1, 0, 0))!);

        Assert.Equal(5, left.FaceCount);
        Assert.Equal(5, right.FaceCount);
    }

    [Fact]
    public void AllTriangles_AreCounterClockwiseSeenFromOutside()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, VoxelMaterial.Stone);

        var mesh = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);

        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]];
            var b = mesh.Vertices[mesh.Indices[i + 1]];
            var c = mesh.Vertices[mesh.Indices[i + 2]];
            var geometricNormal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);

            Assert.True(Vector3.Dot(geometricNormal, a.Normal) > 0, $"Triangle {i / 3} faces inward.");
        }
    }

    [Fact]
    public void VertexPositions_AreInWorldSpace()
    {
        var world = new VoxelWorld();
        world.SetVoxel(40, 1, 70, VoxelMaterial.Grass);

        var mesh = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(1, 0, 2))!);

        Assert.All(mesh.Vertices, v =>
        {
            Assert.InRange(v.Position.X, 40, 41);
            Assert.InRange(v.Position.Y, 1, 2);
            Assert.InRange(v.Position.Z, 70, 71);
        });
    }

    [Fact]
    public void VertexNextToWall_IsDarkerThanOpenVertex()
    {
        var world = new VoxelWorld();
        // Floor voxel with a wall voxel on top of its +X neighbour.
        world.SetVoxel(5, 5, 5, VoxelMaterial.Stone);
        world.SetVoxel(6, 6, 5, VoxelMaterial.Stone);

        var mesh = ChunkMesher.Build(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);
        var top = mesh.Vertices.Where(v => v.Normal == Vector3.UnitY && v.Position.Y == 6).ToList();
        var nearWall = top.Where(v => v.Position.X == 6).Max(v => v.Color.X);
        var open = top.Where(v => v.Position.X == 5).Max(v => v.Color.X);

        Assert.True(nearWall < open);
    }

    [Fact]
    public void VertexLayout_MatchesDeclaredSize()
    {
        Assert.Equal(VoxelVertex.SizeInBytes, System.Runtime.InteropServices.Marshal.SizeOf<VoxelVertex>());
    }
}
