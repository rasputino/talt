using System.Diagnostics;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client;

/// <summary>
/// Options of a new game (the new-game screen or the command line). Feature 004 adds map generation
/// options; the new-game screen builds this from <see cref="Screens.INewGameOptionsSource"/>.
/// </summary>
internal sealed record NewGameOptions(int ChunksPerSide = 16, int VehicleCount = 20_000, int Seed = 2026, int StartYear = 1950)
{
    public int MapSizeTiles => ChunksPerSide * Chunk.Size;

    public ScenarioSettings ToScenario() => new(
        Seed: Seed,
        StartYear: StartYear,
        MapSizeTiles: MapSizeTiles,
        VehicleCount: VehicleCount);
}

/// <summary>A freshly generated game: simulation, voxel world and heightmap.</summary>
internal sealed record GeneratedGame(NewGameOptions Options, ScenarioSettings Scenario, GameSimulation Simulation, VoxelWorld World, Heightmap Heightmap);

/// <summary>Creates the simulation and the world of a new game (runs on a background thread).</summary>
internal static class GameSetup
{
    public static GeneratedGame Create(NewGameOptions options, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.ToScenario();
        settings.Validate();
        var stopwatch = Stopwatch.StartNew();

        var simulation = GameSimulation.Create(settings);
        report?.Invoke($"Created simulation ({simulation.Network.NodeCount:N0} junctions, {simulation.Vehicles.LineCount:N0} lines, {simulation.Vehicles.VehicleCount:N0} vehicles) in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        stopwatch.Restart();

        var world = TerrainGenerator.Generate(new TerrainSettings(Seed: options.Seed, ChunksX: options.ChunksPerSide, ChunksZ: options.ChunksPerSide));
        NetworkTerrainPainter.PaintRoads(world, simulation.Network);
        var heightmap = Heightmap.FromWorld(world, settings.MapSizeTiles, settings.MapSizeTiles);
        report?.Invoke($"Generated {settings.MapSizeTiles}×{settings.MapSizeTiles} terrain ({world.ChunkCount} chunks) in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");

        // No meshing here: the renderer fills the map in, nearest first (feature 003 FR-012).
        return new GeneratedGame(options, settings, simulation, world, heightmap);
    }
}
