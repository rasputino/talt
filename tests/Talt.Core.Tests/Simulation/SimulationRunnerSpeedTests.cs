using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

/// <summary>Speeds, pause and the unlockable fast forward (FR-006 to FR-011, SC-003 to SC-005).</summary>
public class SimulationRunnerSpeedTests
{
    [Theory]
    [InlineData(GameSpeed.Normal, 20)]
    [InlineData(GameSpeed.Fast, 60)]
    [InlineData(GameSpeed.VeryFast, 160)]
    public void StandardSpeeds_RunTheirMultiplierOfTicksPerSecond(GameSpeed speed, int expectedTicks)
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        runner.SetStandardSpeed(speed);

        RunFrames(runner, simulation, seconds: 1.0, frameSeconds: 1.0 / 60);

        Assert.InRange(simulation.CurrentTick, expectedTicks - 1, expectedTicks);
    }

    [Fact]
    public void Pause_RunsNoTicksAndResumesWithoutAJump()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        runner.Advance(0.04, simulation, new FakeTimeSource()); // 0.8 tick owed.

        runner.TogglePause();
        RunFrames(runner, simulation, seconds: 5, frameSeconds: 1.0 / 60);
        var fingerprint = simulation.ComputeFingerprint();
        Assert.Equal(0, simulation.CurrentTick);
        Assert.True(runner.IsPaused);

        runner.TogglePause();
        var stats = runner.Advance(1.0 / 60, simulation, new FakeTimeSource());
        Assert.Equal(0, stats.TicksRun); // Accumulator was reset: no burst after resuming.
        Assert.Equal(fingerprint, simulation.ComputeFingerprint());
        Assert.Equal(GameSpeed.Normal, runner.Speed);
    }

    [Fact]
    public void SetStandardSpeed_RejectsPausedAndFastForward()
    {
        var runner = new SimulationRunner();
        Assert.Throws<ArgumentException>(() => runner.SetStandardSpeed(GameSpeed.FastForward));
        Assert.Throws<ArgumentException>(() => runner.SetStandardSpeed(GameSpeed.Paused));
    }

    [Fact]
    public void FastForward_IsLockedByDefault()
    {
        var runner = new SimulationRunner();
        runner.SetStandardSpeed(GameSpeed.Fast);

        Assert.False(runner.FastForwardUnlocked);
        Assert.False(runner.ToggleFastForward());
        Assert.Equal(GameSpeed.Fast, runner.Speed);
    }

    [Fact]
    public void UnlockedFastForward_TogglesAndRestoresPreviousSpeed()
    {
        var runner = new SimulationRunner();
        runner.SetFastForwardUnlocked(true);
        runner.SetStandardSpeed(GameSpeed.VeryFast);

        Assert.True(runner.ToggleFastForward());
        Assert.Equal(GameSpeed.FastForward, runner.Speed);
        Assert.True(runner.ToggleFastForward());
        Assert.Equal(GameSpeed.VeryFast, runner.Speed);
        Assert.Equal(GameSpeed.VeryFast, runner.LastStandardSpeed);
    }

    [Fact]
    public void PauseDuringFastForward_ResumesInFastForward()
    {
        var runner = new SimulationRunner();
        runner.SetFastForwardUnlocked(true);
        runner.ToggleFastForward();

        runner.TogglePause();
        Assert.Equal(GameSpeed.Paused, runner.Speed);
        runner.TogglePause();
        Assert.Equal(GameSpeed.FastForward, runner.Speed);
    }

    [Fact]
    public void LockingWhileActive_RestoresLastStandardSpeed()
    {
        var runner = new SimulationRunner();
        runner.SetFastForwardUnlocked(true);
        runner.SetStandardSpeed(GameSpeed.Fast);
        runner.ToggleFastForward();

        runner.SetFastForwardUnlocked(false);

        Assert.Equal(GameSpeed.Fast, runner.Speed);
        Assert.False(runner.ToggleFastForward());
    }

    [Fact]
    public void LockingWhilePausedInFastForward_ResumesStandardSpeed()
    {
        var runner = new SimulationRunner();
        runner.SetFastForwardUnlocked(true);
        runner.ToggleFastForward();
        runner.TogglePause();

        runner.SetFastForwardUnlocked(false);
        runner.TogglePause();

        Assert.Equal(GameSpeed.Normal, runner.Speed);
    }

    [Fact]
    public void FastForward_RespectsTheFrameBudget()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner { FastForwardBudget = TimeSpan.FromMilliseconds(25) };
        runner.SetFastForwardUnlocked(true);
        runner.ToggleFastForward();
        var time = new FakeTimeSource { CostPerReadMicroseconds = 1_000 }; // Each tick "costs" ~1 ms.

        var stats = runner.Advance(1.0 / 60, simulation, time);

        Assert.True(stats.FastForward);
        Assert.InRange(stats.TicksRun, 12, 25);
        Assert.InRange(stats.TickMilliseconds, 20, 40);
        Assert.Equal(1f, runner.InterpolationFactor);
    }

    [Fact]
    public void FastForward_AlwaysRunsAtLeastOneTick()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner { FastForwardBudget = TimeSpan.Zero };
        runner.SetFastForwardUnlocked(true);
        runner.ToggleFastForward();

        Assert.Equal(1, runner.Advance(0, simulation, new FakeTimeSource()).TicksRun);
    }

    [Fact]
    public void TenMinutesWithIrregularFrames_AdvanceThreeHundredDaysWithinTwoPercent()
    {
        // SC-004: 30 days per minute at normal speed, drift under 2 % over 10 minutes.
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        var time = new FakeTimeSource();
        var random = new Random(3);
        var elapsed = 0.0;

        while (elapsed < 600)
        {
            var frame = (8 + random.Next(43)) / 1000.0; // 8–50 ms frames.
            runner.Advance(frame, simulation, time);
            elapsed += frame;
        }

        var days = simulation.CurrentTick / (double)GameClock.TicksPerDay;
        Assert.InRange(days, 294, 306);
    }

    private static void RunFrames(SimulationRunner runner, GameSimulation simulation, double seconds, double frameSeconds)
    {
        var time = new FakeTimeSource();
        for (var t = 0.0; t < seconds - 1e-9; t += frameSeconds)
        {
            runner.Advance(frameSeconds, simulation, time);
        }
    }
}
