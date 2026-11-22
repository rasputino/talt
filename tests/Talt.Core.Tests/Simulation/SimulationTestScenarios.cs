using Talt.Core.Scenarios;

namespace Talt.Core.Tests.Simulation;

/// <summary>Small scenarios shared by simulation tests (fast to create and tick).</summary>
internal static class SimulationTestScenarios
{
    public static ScenarioSettings Small(int seed = 42, int vehicles = 2_000) =>
        new(Seed: seed, MapSizeTiles: 256, RoadSpacing: 16, VehicleCount: vehicles, LineCount: 200);

    public static ScenarioSettings Tiny(int seed = 1) =>
        new(Seed: seed, MapSizeTiles: 64, RoadSpacing: 8, VehicleCount: 20, LineCount: 4);
}
