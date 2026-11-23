using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

/// <summary>Seeds, sizes and generated maps shared by the map tests (generating is cached per settings).</summary>
internal static class MapTestSettings
{
    private static readonly Dictionary<MapSettings, MapData> Cache = [];

    /// <summary>A spread of sizes, seeds and settings, including extremes.</summary>
    public static IEnumerable<MapSettings> Matrix()
    {
        yield return new MapSettings(256, 256, Seed: 1);
        yield return new MapSettings(256, 256, Seed: 2026, Roughness: 80, MaxHeight: 96, WaterAmount: 35, SeaLevel: 9);
        yield return new MapSettings(512, 256, Seed: -7, Roughness: 20, MaxHeight: 40, WaterAmount: 15, SeaLevel: 6);
        yield return new MapSettings(256, 512, Seed: 0, Roughness: 100, MaxHeight: 127, WaterAmount: 50, SeaLevel: 12);
        yield return new MapSettings(320, 192, Seed: long.MaxValue, Roughness: 50, MaxHeight: 28, WaterAmount: 25, SeaLevel: 7);
    }

    public static IEnumerable<object[]> MatrixData() => Matrix().Select(s => new object[] { s });

    public static MapData Generate(MapSettings settings)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(settings, out var map))
            {
                map = MapGenerator.Generate(settings);
                Cache[settings] = map;
            }

            return map;
        }
    }
}
