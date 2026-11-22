using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Cross-platform determinism check (research R12, FR-013, SC-002). These fingerprints were generated
/// on Windows x64; Linux and macOS (x64 and ARM64) must produce exactly the same values.
/// </summary>
/// <remarks>
/// Regenerate only when simulation rules change on purpose, and say so in the commit message.
/// </remarks>
public class GoldenFingerprintTests
{
    private const string ExpectedAtTick1000 = "5E17D5475F797428";
    private const string ExpectedAtTick10000 = "80932AEEC77E8EA7";

    private static readonly ScenarioSettings Settings = new(Seed: 42, MapSizeTiles: 256, RoadSpacing: 16, VehicleCount: 2_000, LineCount: 200);

    [Fact]
    public void FixedScenarioAndScript_MatchTheGoldenFingerprints()
    {
        var simulation = GameSimulation.Create(Settings);
        string? atTick1000 = null;

        for (var t = 0; t < 10_000; t++)
        {
            if (t % 250 == 0)
            {
                simulation.Submit(new AddVehicleCommand(t % simulation.Vehicles.LineCount, SyntheticTraffic.MinCruiseSpeed + t));
            }

            if (t % 400 == 0)
            {
                simulation.Submit(new RemoveVehicleCommand(simulation.Vehicles.VehicleIdAt(t % simulation.Vehicles.VehicleCount)));
            }

            simulation.Tick();
            if (simulation.CurrentTick == 1_000)
            {
                atTick1000 = StateHasher.Format(simulation.ComputeFingerprint());
            }
        }

        var atTick10000 = StateHasher.Format(simulation.ComputeFingerprint());
        Assert.Equal(ExpectedAtTick1000, atTick1000);
        Assert.Equal(ExpectedAtTick10000, atTick10000);
    }
}
