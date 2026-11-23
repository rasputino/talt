using Talt.Core.Meshing;
using Talt.Core.Network;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Meshing;

public class LodVolumeTests
{
    private static VoxelWorld FlatWorld(int height, VoxelMaterial top, int chunks = 4)
    {
        var world = new VoxelWorld();
        for (var z = 0; z < chunks * Chunk.Size; z++)
        {
            for (var x = 0; x < chunks * Chunk.Size; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    world.SetVoxel(x, y, z, y == height - 1 ? top : VoxelMaterial.Stone);
                }
            }
        }

        return world;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Sizes_FollowTheScale_AndCoverExistingChunksOnly(int lod)
    {
        var volume = LodVolume.Build(FlatWorld(5, VoxelMaterial.Grass), new RegionCoord(0, 0, 0), lod);

        Assert.Equal(1 << lod, volume.Scale);
        Assert.Equal(RegionCoord.Size >> lod, volume.SizeX);
        Assert.Equal(RegionCoord.Size >> lod, volume.SizeZ);
        Assert.Equal(Chunk.Size >> lod, volume.SizeY);
    }

    [Fact]
    public void Block_IsSolid_WhenAtLeastHalfIsSolid()
    {
        // Height 4 at LOD 3 (8³ blocks): half of the bottom block is solid.
        Assert.NotEqual(VoxelMaterial.Air, LodVolume.Build(FlatWorld(4, VoxelMaterial.Grass), new RegionCoord(0, 0, 0), 3).Get(3, 0, 3));
        Assert.Equal(VoxelMaterial.Air, LodVolume.Build(FlatWorld(3, VoxelMaterial.Grass), new RegionCoord(0, 0, 0), 3).Get(3, 0, 3));
    }

    [Fact]
    public void SurfaceBlock_TakesTheSurfaceMaterial_NotTheMajorityBelow()
    {
        var volume = LodVolume.Build(FlatWorld(8, VoxelMaterial.Grass), new RegionCoord(0, 0, 0), 2);

        Assert.Equal(VoxelMaterial.Grass, volume.Get(5, 1, 5)); // Top block (y 4..7).
        Assert.Equal(VoxelMaterial.Stone, volume.Get(5, 0, 5)); // Below the surface.
    }

    [Fact]
    public void OutsideTheRegion_IsAir_AndBelowTheWorld_IsSolid()
    {
        var volume = LodVolume.Build(FlatWorld(8, VoxelMaterial.Grass, chunks: 8), new RegionCoord(0, 0, 0), 1);

        Assert.Equal(VoxelMaterial.Air, volume.Get(-1, 0, 5));
        Assert.Equal(VoxelMaterial.Air, volume.Get(volume.SizeX, 0, 5));
        Assert.NotEqual(VoxelMaterial.Air, volume.Get(5, -1, 5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RoadGrid_StaysContinuous_AtEveryLevel(int lod)
    {
        var world = Maps.TestWorlds.Square(4, 5);
        var network = GridNetworkGenerator.Generate(8, 8, spacingTiles: 16, seed: 1, removalChancePerMille: 0, originTileX: 8, originTileZ: 8);
        NetworkTerrainPainter.PaintRoads(world, network);
        var volume = LodVolume.Build(world, new RegionCoord(0, 0, 0), lod);
        var scale = volume.Scale;

        // The road along z = 8 (and x = 8) must be road at the surface of every block it crosses.
        for (var b = 8 / scale; b < 120 / scale; b++)
        {
            Assert.Equal(VoxelMaterial.Road, SurfaceOf(volume, b, 8 / scale));
            Assert.Equal(VoxelMaterial.Road, SurfaceOf(volume, 8 / scale, b));
        }
    }

    [Fact]
    public void Water_IsKept()
    {
        var volume = LodVolume.Build(FlatWorld(7, VoxelMaterial.Water), new RegionCoord(0, 0, 0), 2);

        Assert.Equal(VoxelMaterial.Water, SurfaceOf(volume, 10, 10));
    }

    private static VoxelMaterial SurfaceOf(LodVolume volume, int x, int z)
    {
        for (var y = volume.SizeY - 1; y >= 0; y--)
        {
            var material = volume.Get(x, y, z);
            if (material != VoxelMaterial.Air)
            {
                return material;
            }
        }

        return VoxelMaterial.Air;
    }
}
