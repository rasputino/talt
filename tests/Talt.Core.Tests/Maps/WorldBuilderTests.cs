using Talt.Core.Maps;
using Talt.Core.Meshing;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Maps;

public class WorldBuilderTests
{
    private static readonly MapSettings Tall = new(256, 256, Seed: 6, Roughness: 70, MaxHeight: 120, WaterAmount: 30, SeaLevel: 10);

    [Fact]
    public void Voxels_MatchTheTileData()
    {
        var map = MapTestSettings.Generate(Tall);
        var world = WorldBuilder.Build(map);

        for (var z = 0; z < map.Depth; z += 5)
        {
            for (var x = 0; x < map.Width; x += 5)
            {
                var height = map.HeightAt(x, z);
                var water = map.WaterLevelAt(x, z);
                Assert.True(world.GetVoxel(x, height - 1, z).IsSolid());
                Assert.Equal(VoxelMaterial.Air, world.GetVoxel(x, Math.Max(height, water), z));
                if (water > height)
                {
                    Assert.Equal(VoxelMaterial.Water, world.GetVoxel(x, water - 1, z));
                }
            }
        }
    }

    [Fact]
    public void HeightmapFromMapData_EqualsTheOneWalkedFromVoxels()
    {
        var map = MapTestSettings.Generate(Tall);
        var world = WorldBuilder.Build(map);
        var fromData = Heightmap.FromMapData(map);
        var fromWorld = Heightmap.FromWorld(world, map.Width, map.Depth);
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                Assert.Equal(fromWorld.Get(x, z), fromData.Get(x, z));
            }
        }
    }

    [Fact]
    public void TallTerrain_UsesSeveralVerticalChunks_AndFitsOneRegionColumn()
    {
        var map = MapTestSettings.Generate(Tall);
        var world = WorldBuilder.Build(map);
        Assert.True(world.TopChunkY >= 1);
        Assert.True(world.TopChunkY <= 3);
    }

    [Fact]
    public void TallTerrain_MeshesAtEveryLevelOfDetail_WithoutErrors()
    {
        var map = MapTestSettings.Generate(Tall);
        var world = WorldBuilder.Build(map);
        var meshed = 0;
        foreach (var chunk in world.Chunks)
        {
            meshed += GreedyMesher.BuildChunk(world, chunk.Coord).QuadCount;
        }

        Assert.True(meshed > 0);
        var region = new RegionCoord(0, 0, 0);
        for (var lod = 1; lod <= GreedyMesher.MaxLod; lod++)
        {
            Assert.True(GreedyMesher.BuildRegionLod(world, region, lod).QuadCount > 0);
        }
    }

    [Fact]
    public void SurfaceHeight_FindsColumnsAboveTheFirstChunk()
    {
        var map = MapTestSettings.Generate(Tall);
        var world = WorldBuilder.Build(map);
        for (var x = 0; x < map.Width; x += 7)
        {
            Assert.Equal(map.SurfaceHeightAt(x, 40), world.GetSurfaceHeight(x, 40));
        }
    }

    [Fact]
    public void RebuildColumn_OfAnUnchangedColumn_ChangesNothing()
    {
        var map = MapTestSettings.Generate(Tall);
        var world = WorldBuilder.Build(map);
        var fresh = WorldBuilder.Build(map);
        for (var x = 0; x < map.Width; x += 9)
        {
            WorldBuilder.RebuildColumn(map, world, x, 40, map.SurfaceHeightAt(x, 40));
            for (var y = 0; y < 160; y++)
            {
                Assert.Equal(fresh.GetVoxel(x, y, 40), world.GetVoxel(x, y, 40));
            }
        }
    }

    [Theory]
    [InlineData(+1)]
    [InlineData(+20)]
    [InlineData(-3)]
    public void RebuildColumn_AfterAHeightChange_MatchesAFreshWorld(int delta)
    {
        // Feature 006: terraforming edits the tile data, then rebuilds the column; growing into the chunk
        // above creates it.
        var map = MapGenerator.Generate(Tall);
        var world = WorldBuilder.Build(map);
        var (x, z) = FindTallColumn(map);
        var before = map.SurfaceHeightAt(x, z);
        map.SetHeight(map.Index(x, z), map.HeightAt(x, z) + delta);

        var (minY, maxY) = WorldBuilder.RebuildColumn(map, world, x, z, before);
        var fresh = WorldBuilder.Build(map);

        for (var y = 0; y < 160; y++)
        {
            Assert.Equal(fresh.GetVoxel(x, y, z), world.GetVoxel(x, y, z));
        }

        Assert.True(minY <= Math.Min(before, before + delta) - 1);
        Assert.True(maxY >= Math.Max(before, before + delta) - 1);
        var heightmap = Heightmap.FromMapData(MapTestSettings.Generate(Tall));
        heightmap.Refresh(map, x, z);
        Assert.Equal(before + delta, heightmap.Get(x, z));
    }

    private static (int X, int Z) FindTallColumn(MapData map)
    {
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map.WaterLevelAt(x, z) == 0 && map.HeightAt(x, z) > Chunk.Size + 5 && map.HeightAt(x, z) % Chunk.Size != 0)
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("The tall test map has no high ground.");
    }
}
