using Talt.Core.Determinism;
using Talt.Core.Terrain;

namespace Talt.Core.Maps;

/// <summary>
/// Working state shared by the generation stages: settings, tuning, the output arrays being filled
/// and the execution options. Internal to the generator.
/// </summary>
internal sealed class MapBuilder
{
    public MapBuilder(MapSettings settings, MapGenerationTuning tuning, int? maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        Settings = settings;
        Tuning = tuning;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;
        CancellationToken = cancellationToken;
        Width = settings.Width;
        Depth = settings.Depth;
        var count = Width * Depth;
        Height = new byte[count];
        Terrain = new byte[count];
        WaterLevel = new byte[count];
        Flags = new byte[count];
        Scenery = new byte[count];
    }

    public MapSettings Settings { get; }

    public MapGenerationTuning Tuning { get; }

    public int? MaxDegreeOfParallelism { get; }

    public CancellationToken CancellationToken { get; }

    public int Width { get; }

    public int Depth { get; }

    public int TileCount => Width * Depth;

    public byte[] Height { get; }

    public byte[] Terrain { get; }

    public byte[] WaterLevel { get; }

    public byte[] Flags { get; }

    public byte[] Scenery { get; }

    public IReadOnlyList<Site> Sites { get; set; } = [];

    public MapStatistics Statistics { get; set; }

    public int SeaLevel => Settings.SeaLevel;

    /// <summary>Independent noise for a stream of the generator, derived from the seed.</summary>
    public ValueNoise Noise(int stream) =>
        new((int)new DeterministicRandom(unchecked((ulong)Settings.Seed)).Fork((ulong)stream).NextUInt64());

    /// <summary>Runs <paramref name="body"/> over every band of rows in parallel (see <see cref="RowBands"/>).</summary>
    public void ForBands(Action<int, int, int> body) =>
        RowBands.For(Depth, Tuning.BandRows, MaxDegreeOfParallelism, body, CancellationToken);

    public MapData ToMapData() => new(Settings, Height, Terrain, WaterLevel, Flags, Scenery, Sites, Statistics);
}
