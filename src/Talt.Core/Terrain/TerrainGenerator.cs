using Talt.Core.Voxels;

namespace Talt.Core.Terrain;

/// <summary>
/// Settings for <see cref="TerrainGenerator"/>.
/// </summary>
/// <param name="Seed">Random seed. Same seed, same terrain.</param>
/// <param name="ChunksX">Map width in chunks.</param>
/// <param name="ChunksZ">Map depth in chunks.</param>
/// <param name="MaxHeight">Highest possible terrain surface, in voxels.</param>
/// <param name="WaterLevel">Height up to which empty space is filled with water.</param>
public sealed record TerrainSettings(
    int Seed = 1,
    int ChunksX = 8,
    int ChunksZ = 8,
    int MaxHeight = 28,
    int WaterLevel = 7);

/// <summary>
/// Generates a heightmap-based voxel terrain. Prototype quality: simple, deterministic, no biomes.
/// </summary>
public static class TerrainGenerator
{
    private const float FeatureSize = 96f;

    public static VoxelWorld Generate(TerrainSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.MaxHeight >= Chunk.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "MaxHeight must fit in a single vertical chunk.");
        }

        var world = new VoxelWorld();
        var noise = new ValueNoise(settings.Seed);
        var width = settings.ChunksX * Chunk.Size;
        var depth = settings.ChunksZ * Chunk.Size;

        for (var z = 0; z < depth; z++)
        {
            for (var x = 0; x < width; x++)
            {
                var height = SurfaceHeight(noise, x, z, settings.MaxHeight);
                FillColumn(world, x, z, height, settings);
            }
        }

        return world;
    }

    /// <summary>Height of the terrain surface at a column, in the range [1, maxHeight].</summary>
    public static int SurfaceHeight(ValueNoise noise, int x, int z, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(noise);

        var n = noise.Fractal(x / FeatureSize, z / FeatureSize, octaves: 4);
        // Push values apart so there are flat lowlands and distinct hills.
        var shaped = MathF.Pow(n, 1.6f);
        return Math.Clamp(1 + (int)(shaped * maxHeight * 1.4f), 1, maxHeight);
    }

    private static void FillColumn(VoxelWorld world, int x, int z, int height, TerrainSettings settings)
    {
        var top = height - 1;
        var nearWater = height <= settings.WaterLevel + 1;
        var snowLine = settings.MaxHeight - 4;

        for (var y = 0; y <= top; y++)
        {
            VoxelMaterial material;
            if (y == top)
            {
                material = nearWater ? VoxelMaterial.Sand : y >= snowLine ? VoxelMaterial.Snow : VoxelMaterial.Grass;
            }
            else if (y >= top - 2)
            {
                material = nearWater ? VoxelMaterial.Sand : VoxelMaterial.Dirt;
            }
            else
            {
                material = VoxelMaterial.Stone;
            }

            world.SetVoxel(x, y, z, material);
        }

        for (var y = height; y < settings.WaterLevel; y++)
        {
            world.SetVoxel(x, y, z, VoxelMaterial.Water);
        }
    }
}
