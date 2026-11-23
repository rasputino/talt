using System.Diagnostics;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client;

/// <summary>
/// Options of a new game (the new-game screen or the command line). <paramref name="Map"/> holds the
/// full map generation settings (feature 004); when null they derive from the map size and seed.
/// The new-game screen builds this from <see cref="Screens.INewGameOptionsSource"/>.
/// </summary>
internal sealed record NewGameOptions(int ChunksPerSide = 16, int VehicleCount = 20_000, int Seed = 2026, int StartYear = 1950, MapSettings? Map = null)
{
    public int MapSizeTiles => Map is null ? ChunksPerSide * Chunk.Size : Math.Min(Map.Width, Map.Depth);

    /// <summary>The map settings: the given ones, or the defaults for the chosen size and seed.</summary>
    public MapSettings MapSettings => Map ?? new MapSettings(ChunksPerSide * Chunk.Size, ChunksPerSide * Chunk.Size, Seed);

    /// <summary>The scenario, map included: the simulation owns the map's tile data (feature 006).</summary>
    public ScenarioSettings ToScenario() => new(
        Seed: Seed,
        StartYear: StartYear,
        MapSizeTiles: MapSizeTiles,
        VehicleCount: VehicleCount,
        Map: MapSettings);
}

/// <summary>A freshly generated game: simulation, map data, voxel world and heightmap.</summary>
internal sealed record GeneratedGame(NewGameOptions Options, ScenarioSettings Scenario, GameSimulation Simulation, MapData Map, VoxelWorld World, Heightmap Heightmap);

/// <summary>Creates the simulation and the world of a new game (runs on a background thread).</summary>
internal static class GameSetup
{
    /// <param name="options">What to generate.</param>
    /// <param name="report">Console lines (timings, map text), optional.</param>
    /// <param name="progress">Map generation progress for the new-game screen (feature 005 T069), optional.</param>
    public static GeneratedGame Create(NewGameOptions options, Action<string>? report = null, IProgress<MapGenerationProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.ToScenario();
        settings.Validate();
        var stopwatch = Stopwatch.StartNew();

        var mapSettings = settings.Map!;
        report?.Invoke($"Map settings: {mapSettings.ToText()}");
        // The screen's bar: map generation fills 0–80 %, building the voxel world 80–100 %
        // (WorldBuilder reports a fraction of its own step).
        var map = MapGenerator.Generate(mapSettings, Combine(report is null ? null : new StageReporter(report), Scaled(progress, 0f, MapShare)));
        report?.Invoke($"Generated the {map.Width}×{map.Depth} map in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        stopwatch.Restart();

        // The simulation takes the map: industries use its reserved sites (feature 009) and construction
        // commands change its tile data (feature 006).
        var simulation = GameSimulation.Create(settings, map);
        report?.Invoke($"Created simulation ({simulation.Network.NodeCount:N0} junctions, {simulation.Vehicles.LineCount:N0} lines, {simulation.Vehicles.VehicleCount:N0} vehicles, {simulation.Industries.Count:N0} industries) in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        stopwatch.Restart();

        var world = WorldBuilder.Build(map, Scaled(progress, MapShare, 1f - MapShare));
        NetworkTerrainPainter.PaintRoads(world, simulation.Network);
        var heightmap = Heightmap.FromMapData(map);
        report?.Invoke(
            $"Built the world ({world.ChunkCount} chunks, {map.Statistics.RiverTiles:N0} river tiles, {map.TownSites.Count()} town and {map.IndustrySites.Count()} industry sites) in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");

        // No meshing here: the renderer fills the map in, nearest first (feature 003 FR-012).
        return new GeneratedGame(options, settings, simulation, map, world, heightmap);
    }

    /// <summary>Share of the progress bar taken by map generation; the rest is building the world.</summary>
    private const float MapShare = 0.8f;

    private static ScaledProgress? Scaled(IProgress<MapGenerationProgress>? progress, float offset, float scale) =>
        progress is null ? null : new ScaledProgress(progress, offset, scale);

    /// <summary>Maps a step's 0–1 fraction into its part of the overall bar.</summary>
    private sealed class ScaledProgress(IProgress<MapGenerationProgress> target, float offset, float scale) : IProgress<MapGenerationProgress>
    {
        public void Report(MapGenerationProgress value) =>
            target.Report(value with { Fraction = offset + (scale * Math.Clamp(value.Fraction, 0f, 1f)) });
    }

    private static IProgress<MapGenerationProgress>? Combine(IProgress<MapGenerationProgress>? first, IProgress<MapGenerationProgress>? second) =>
        first is null ? second : second is null ? first : new BothProgress(first, second);

    /// <summary>Sends progress to two receivers (console and screen).</summary>
    private sealed class BothProgress(IProgress<MapGenerationProgress> first, IProgress<MapGenerationProgress> second) : IProgress<MapGenerationProgress>
    {
        public void Report(MapGenerationProgress value)
        {
            first.Report(value);
            second.Report(value);
        }
    }

    /// <summary>Prints one console line each time generation enters a new stage.</summary>
    private sealed class StageReporter(Action<string> report) : IProgress<MapGenerationProgress>
    {
        private MapGenerationStage? _last;

        public void Report(MapGenerationProgress value)
        {
            if (_last != value.Stage)
            {
                _last = value.Stage;
                report($"[{(int)(value.Fraction * 100),3}%] {value.Stage}");
            }
        }
    }
}
