using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

public class GameSimulationTests
{
    [Fact]
    public void CurrentTick_StartsAtZeroAndIncrementsByOne()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());

        Assert.Equal(0, simulation.CurrentTick);
        for (var i = 1; i <= 5; i++)
        {
            simulation.Tick();
            Assert.Equal(i, simulation.CurrentTick);
        }
    }

    [Fact]
    public void SameSettings_GiveSameFingerprintAfterTicks()
    {
        var a = GameSimulation.Create(SimulationTestScenarios.Small());
        var b = GameSimulation.Create(SimulationTestScenarios.Small());

        Assert.Equal(a.ComputeFingerprint(), b.ComputeFingerprint());
        for (var i = 0; i < 300; i++)
        {
            a.Tick();
            b.Tick();
        }

        Assert.Equal(a.ComputeFingerprint(), b.ComputeFingerprint());
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentFingerprints()
    {
        Assert.NotEqual(
            GameSimulation.Create(SimulationTestScenarios.Small(seed: 1)).ComputeFingerprint(),
            GameSimulation.Create(SimulationTestScenarios.Small(seed: 2)).ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_IsPure()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Small());
        simulation.Tick();

        Assert.Equal(simulation.ComputeFingerprint(), simulation.ComputeFingerprint());
        Assert.Equal(1, simulation.CurrentTick);
    }

    [Fact]
    public void Fingerprint_ChangesEveryTick()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Small());
        var previous = simulation.ComputeFingerprint();
        simulation.Tick();
        Assert.NotEqual(previous, simulation.ComputeFingerprint());
    }

    [Fact]
    public void InvalidSettings_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => GameSimulation.Create(new ScenarioSettings(StartYear: 0)));
        Assert.Throws<ArgumentException>(() => GameSimulation.Create(new ScenarioSettings(MapSizeTiles: 100)));
        Assert.Throws<ArgumentException>(() => GameSimulation.Create(new ScenarioSettings(RoadRemovalChancePerMille: 950)));
        Assert.Throws<ArgumentException>(() => GameSimulation.Create(new ScenarioSettings(VehicleCount: -1)));
    }
}
