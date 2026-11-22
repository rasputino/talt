using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

/// <summary>Accumulator, catch-up cap and interpolation at normal speed.</summary>
public class SimulationRunnerTests
{
    [Fact]
    public void NormalSpeed_RunsTwentyTicksPerSecond()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        var time = new FakeTimeSource();

        for (var frame = 0; frame < 60; frame++)
        {
            runner.Advance(1.0 / 60, simulation, time);
        }

        Assert.InRange(simulation.CurrentTick, 19, 20); // One second of real time.
    }

    [Fact]
    public void LongFrame_CatchUpIsCapped()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();

        var stats = runner.Advance(1.0, simulation, new FakeTimeSource());

        Assert.Equal(5, stats.TicksRun); // 0.25 s × 20 ticks (SC-007), not 20.
        var next = runner.Advance(1.0 / 60, simulation, new FakeTimeSource());
        Assert.InRange(next.TicksRun, 0, 1); // Excess time was dropped, no second burst.
    }

    [Fact]
    public void InterpolationFactor_IsFractionOfATick()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();

        runner.Advance(0.025, simulation, new FakeTimeSource()); // Half a tick.

        Assert.Equal(0, simulation.CurrentTick);
        Assert.Equal(0.5f, runner.InterpolationFactor, 3);
    }

    [Fact]
    public void BeforeLastTick_IsCalledOncePerFrameWithTicks()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        var calls = 0;
        long tickAtCall = -1;

        runner.Advance(0.2, simulation, new FakeTimeSource(), () => { calls++; tickAtCall = simulation.CurrentTick; });

        Assert.Equal(1, calls);
        Assert.Equal(3, tickAtCall); // Called before the 4th and last tick.
        Assert.Equal(4, simulation.CurrentTick);

        runner.Advance(0.01, simulation, new FakeTimeSource(), () => calls++);
        Assert.Equal(1, calls); // No tick, no snapshot.
    }

    [Fact]
    public void AfterEachTick_SeesEveryTick()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        var seen = new List<long>();

        runner.Advance(0.2, simulation, new FakeTimeSource(), afterEachTick: s => seen.Add(s.CurrentTick));

        Assert.Equal([1, 2, 3, 4], seen);
    }
}
