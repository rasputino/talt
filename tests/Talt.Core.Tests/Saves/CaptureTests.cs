using Talt.Core.Saves;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Saves;

/// <summary>Saving never changes the game (SC-007) and capture allocates nothing once warm (constitution II, research R7).</summary>
public class CaptureTests
{
    [Fact]
    public void Capturing_does_not_change_the_game()
    {
        var saved = SaveTestGames.Rich(ticks: 300);
        var untouched = SaveTestGames.Rich(ticks: 300);
        Assert.Equal(untouched.ComputeFingerprint(), saved.ComputeFingerprint());
        for (var t = 0; t < 600; t++)
        {
            if (t % 150 == 0)
            {
                using var snapshot = SaveGame.Capture(saved, RoundTrip.Details);
            }

            saved.Tick(parallel: false);
            untouched.Tick(parallel: false);
        }

        Assert.Equal(untouched.ComputeFingerprint(), saved.ComputeFingerprint());
        RoundTrip.AssertSameSections(untouched, saved);
    }

    [Fact]
    public void Warm_capture_of_a_rich_game_allocates_nothing()
    {
        var game = SaveTestGames.Rich(ticks: 200);
        using var snapshot = new SaveSnapshot();
        AllocationAssert.NoSteadyStateAllocations(() =>
        {
            SaveGame.CaptureInto(snapshot, game, RoundTrip.Details);
        }, iterationsPerRound: 3, "capturing the sections of a rich game");
    }

    [Fact]
    public void Ticks_of_a_loaded_game_allocate_nothing_once_warm()
    {
        // The steady state of the router allocation test (feature 013), saved and loaded first.
        var d = Demand.DemandKit.ThreeTowns();
        d.Supply(d.A, d.Kit.Stations.Demand!.ExpiryDays + 10);
        var loaded = RoundTrip.SaveAndLoad(d.Kit.Simulation);
        var period = loaded.Router!.Rules.UpdateIntervalDays * global::Talt.Core.Simulation.GameClock.TicksPerDay;
        AllocationAssert.NoSteadyStateAllocations(() => loaded.Tick(parallel: false), period, "ticks of a loaded game with destination routing");
    }
}
