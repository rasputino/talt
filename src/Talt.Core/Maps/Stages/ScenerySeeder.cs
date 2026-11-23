namespace Talt.Core.Maps.Stages;

/// <summary>
/// Stage 6: forests and trees as scenery data (research R7). A low-frequency forest map decides
/// where trees are dense; wetter maps have more forest. Each tile holds at most one tree, chosen by
/// a hash of its position so the result does not depend on evaluation order.
/// </summary>
internal static class ScenerySeeder
{
    private const float ForestScale = 64f;

    public static void Run(MapBuilder b)
    {
        var forest = b.Noise(9);
        var seed = (uint)b.Settings.Seed ^ (uint)(b.Settings.Seed >> 32);
        var threshold = Math.Max(300, b.Tuning.ForestThresholdPerMille - b.Settings.WaterAmount * 3) / 1000f;
        var forestChance = b.Tuning.ForestTreesPerMille;
        var openChance = b.Tuning.OpenTreesPerMille;
        var sea = b.SeaLevel;
        var conifers = sea + (b.Settings.MaxHeight - sea) * 40 / 100;

        b.ForBands((_, z0, z1) =>
        {
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < b.Width; x++)
                {
                    var i = x + z * b.Width;
                    if ((TerrainType)b.Terrain[i] != TerrainType.Grass || (b.Flags[i] & (byte)TileTraits.Cliff) != 0)
                    {
                        continue;
                    }

                    var inForest = forest.Fractal(x / ForestScale, z / ForestScale, 2) > threshold;
                    var hash = Hash(x, z, seed);
                    if (hash % 1000 >= (inForest ? forestChance : openChance))
                    {
                        continue;
                    }

                    var kind = b.Height[i] >= conifers
                        ? Scenery.KindConifer
                        : (hash >> 10) % 3 == 0 ? Scenery.KindBirch : Scenery.KindBroadleaf;
                    b.Scenery[i] = Scenery.Pack(kind, (int)((hash >> 16) & 31));
                }
            }
        });
    }

    private static uint Hash(int x, int z, uint seed)
    {
        unchecked
        {
            var h = seed ^ ((uint)x * 0x9E3779B1u) ^ ((uint)z * 0x85EBCA77u);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }
}
