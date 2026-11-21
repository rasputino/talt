using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Terrain;

public class TerrainGeneratorTests
{
    private static readonly TerrainSettings Small = new(Seed: 42, ChunksX: 2, ChunksZ: 2);

    [Fact]
    public void SameSeed_ProducesSameTerrain()
    {
        var a = TerrainGenerator.Generate(Small);
        var b = TerrainGenerator.Generate(Small);

        for (var x = 0; x < 64; x += 7)
        {
            for (var z = 0; z < 64; z += 5)
            {
                for (var y = 0; y < Chunk.Size; y++)
                {
                    Assert.Equal(a.GetVoxel(x, y, z), b.GetVoxel(x, y, z));
                }
            }
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentTerrain()
    {
        var noiseA = new ValueNoise(1);
        var noiseB = new ValueNoise(2);

        var differences = 0;
        for (var x = 0; x < 256; x += 8)
        {
            if (TerrainGenerator.SurfaceHeight(noiseA, x, 0, 28) != TerrainGenerator.SurfaceHeight(noiseB, x, 0, 28))
            {
                differences++;
            }
        }

        Assert.True(differences > 0);
    }

    [Fact]
    public void EveryColumn_HasSolidGroundAndAirAbove()
    {
        var world = TerrainGenerator.Generate(Small);

        for (var x = 0; x < 64; x++)
        {
            for (var z = 0; z < 64; z++)
            {
                Assert.True(world.GetVoxel(x, 0, z).IsSolid());
                Assert.Equal(VoxelMaterial.Air, world.GetVoxel(x, Chunk.Size - 1, z));
            }
        }
    }

    [Fact]
    public void ValueNoise_StaysInUnitRange()
    {
        var noise = new ValueNoise(7);

        for (var i = 0; i < 1000; i++)
        {
            var value = noise.Fractal(i * 0.37f, i * 0.11f, octaves: 4);
            Assert.InRange(value, 0f, 1f);
        }
    }
}
