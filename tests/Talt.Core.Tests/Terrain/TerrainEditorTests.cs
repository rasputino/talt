using Talt.Core.Meshing;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Terrain;

public class TerrainEditorTests
{
    private static (VoxelWorld World, Heightmap Map) CreateTerrain()
    {
        var world = TerrainGenerator.Generate(new TerrainSettings(Seed: 9, ChunksX: 2, ChunksZ: 2));
        return (world, Heightmap.FromWorld(world, 64, 64));
    }

    [Fact]
    public void Raise_AddsOneVoxel_KeepsTheSurfaceMaterial_AndRefreshesTheHeightmap()
    {
        var (world, map) = CreateTerrain();
        var before = map.Get(10, 10);
        var top = world.GetVoxel(10, before - 1, 10);

        var changed = TerrainEditor.Raise(world, map, 10, 10);

        Assert.Equal(before + 1, map.Get(10, 10));
        Assert.Equal(top, world.GetVoxel(10, before, 10));
        Assert.Equal(2, changed.Count);
    }

    [Fact]
    public void Lower_RemovesOneVoxel_KeepsTheSurfaceMaterial()
    {
        var (world, map) = CreateTerrain();
        var before = map.Get(20, 20);
        var top = world.GetVoxel(20, before - 1, 20);

        TerrainEditor.Lower(world, map, 20, 20);

        Assert.Equal(before - 1, map.Get(20, 20));
        Assert.Equal(top, world.GetVoxel(20, before - 2, 20));
        Assert.Equal(VoxelMaterial.Air, world.GetVoxel(20, before - 1, 20));
    }

    [Fact]
    public void Heights_StayBetweenOneAndTheChunkTop()
    {
        var (world, map) = CreateTerrain();
        for (var i = 0; i < 40; i++)
        {
            TerrainEditor.Lower(world, map, 5, 5);
            TerrainEditor.Raise(world, map, 6, 6);
        }

        Assert.Equal(1, map.Get(5, 5));
        Assert.Equal(Chunk.Size, map.Get(6, 6));
        Assert.Empty(TerrainEditor.Lower(world, map, 5, 5));
        Assert.Empty(TerrainEditor.Raise(world, map, 6, 6));
        Assert.Equal(4, world.ChunkCount); // No chunk was created.
    }

    [Fact]
    public void Edits_OutsideTheMap_DoNothing()
    {
        var (world, map) = CreateTerrain();

        Assert.Empty(TerrainEditor.Raise(world, map, -1, 5));
        Assert.Empty(TerrainEditor.Lower(world, map, 64, 5));
    }

    [Fact]
    public void BorderEdit_RemeshingTouchedChunks_EqualsRemeshingEverything()
    {
        var (world, map) = CreateTerrain();
        var meshes = world.Chunks.ToDictionary(c => c.Coord, c => GreedyMesher.BuildChunk(world, c.Coord));

        // Corner of four chunks.
        var changed = TerrainEditor.Raise(world, map, 31, 31).Concat(TerrainEditor.Lower(world, map, 32, 32)).ToList();
        foreach (var coord in changed.SelectMany(p => ChunkCoord.TouchedBy(p.X, p.Y, p.Z)).Distinct())
        {
            if (world.GetChunk(coord) is not null)
            {
                meshes[coord] = GreedyMesher.BuildChunk(world, coord);
            }
        }

        foreach (var chunk in world.Chunks)
        {
            var fresh = GreedyMesher.BuildChunk(world, chunk.Coord);
            Assert.Equal(fresh.Vertices, meshes[chunk.Coord].Vertices);
        }
    }
}
