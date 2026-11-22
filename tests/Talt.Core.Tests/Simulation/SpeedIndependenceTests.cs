using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

/// <summary>US2 scenario 3 / FR-008: results are identical at every speed.</summary>
public class SpeedIndependenceTests
{
    private const long TargetTicks = 2_400;

    [Fact]
    public void AllSpeeds_ReachTheSameStateAndArrivalTicks()
    {
        var reference = Run(runner => { });
        var fast = Run(runner => runner.SetStandardSpeed(GameSpeed.Fast));
        var veryFast = Run(runner => runner.SetStandardSpeed(GameSpeed.VeryFast));
        var fastForward = Run(runner =>
        {
            runner.SetFastForwardUnlocked(true);
            runner.ToggleFastForward();
        });

        Assert.Equal(reference, fast);
        Assert.Equal(reference, veryFast);
        Assert.Equal(reference, fastForward);
    }

    private static (ulong Fingerprint, string Arrivals) Run(Action<SimulationRunner> configure)
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Small(vehicles: 500));
        var runner = new SimulationRunner();
        configure(runner);
        var time = new FakeTimeSource { CostPerReadMicroseconds = 100 };
        var arrivals = new List<long>();
        var wasAtStop = simulation.Vehicles.IsAtStop(0);
        ulong fingerprintAtTarget = 0;

        // Frames run a different number of ticks at each speed, so capture the state exactly at
        // TargetTicks from inside the per-tick callback.
        while (simulation.CurrentTick < TargetTicks)
        {
            runner.Advance(1.0 / 60, simulation, time, afterEachTick: s =>
            {
                if (s.CurrentTick > TargetTicks)
                {
                    return;
                }

                var atStop = s.Vehicles.IsAtStop(0);
                if (atStop && !wasAtStop)
                {
                    arrivals.Add(s.CurrentTick);
                }

                wasAtStop = atStop;
                if (s.CurrentTick == TargetTicks)
                {
                    fingerprintAtTarget = s.ComputeFingerprint();
                }
            });
        }

        return (fingerprintAtTarget, string.Join(",", arrivals));
    }
}
