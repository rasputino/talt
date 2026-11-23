using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Golden fingerprints for a game that has industries (feature 009). Generated on Windows x64; every
/// CI platform must produce the same values. A map-less game keeps the values of
/// <see cref="GoldenFingerprintTests"/> because an empty industry fleet hashes nothing.
/// </summary>
public class IndustryGoldenFingerprintTests
{
    private const string ExpectedAtTick1000 = "09349CCB0EFF258D";
    private const string ExpectedAtTick4000 = "A41DDDB0E33AEE0F";

    [Fact]
    public void GeneratedMapWithIndustries_MatchesTheGoldenFingerprints()
    {
        var map = MapGenerator.Generate(new MapSettings(256, 256, Seed: 7));
        var settings = new ScenarioSettings(Seed: 7, MapSizeTiles: 256, VehicleCount: 0, LineCount: 1);
        var simulation = GameSimulation.Create(settings, map);
        string? atTick1000 = null;

        for (var tick = 0; tick < 4_000; tick++)
        {
            simulation.Tick();
            if (simulation.CurrentTick == 1_000)
            {
                atTick1000 = StateHasher.Format(simulation.ComputeFingerprint());
            }
        }

        Assert.True(simulation.Industries.Count > 0);
        Assert.Equal(ExpectedAtTick1000, atTick1000);
        Assert.Equal(ExpectedAtTick4000, StateHasher.Format(simulation.ComputeFingerprint()));
    }
}
