using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Saves;

/// <summary>US1: a loaded game continues exactly like the saved one (FR-003, SC-003, research R13).</summary>
public class ContinuationTests
{
    [Fact]
    public void Rich_game_uses_every_system()
    {
        var game = SaveTestGames.Rich();
        Assert.True(game.RoadVehicles!.Count > 0, "buses");
        Assert.True(game.Trains!.Count > 0, "trains");
        Assert.True(game.Construction!.Rails.EverUsed, "track");
        Assert.True(game.Stations!.Count > 0, "stations");
        Assert.True(game.Router!.EverStarted, "destination routing");
        Assert.True(game.Construction.Player.Loan > 0, "loan");
        Assert.True(game.Towns!.Count > 0 && game.Industries.Count > 0, "towns and industries");
    }

    [Fact]
    public void Rich_game_round_trips_and_continues_identically()
    {
        var original = SaveTestGames.Rich();
        var loaded = RoundTrip.SaveAndLoad(original);
        RoundTrip.AssertContinuesIdentically(original, loaded, 2000, every: 50);
    }

    [Fact]
    public void Saves_at_a_mid_day_tick_and_at_a_month_boundary_continue_identically()
    {
        var original = SaveTestGames.Rich(ticks: 0);
        SaveTestGames.RunUntil(original, s => s.CurrentTick % GameClock.TicksPerDay == 17);
        RoundTrip.AssertContinuesIdentically(original, RoundTrip.SaveAndLoad(original), 300, every: 10);

        SaveTestGames.RunUntil(original, s => s.CurrentTick % GameClock.TicksPerDay == 0 && s.Date.Day == 1);
        RoundTrip.AssertContinuesIdentically(original, RoundTrip.SaveAndLoad(original), 300, every: 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Saves_during_each_route_build_stage_continue_identically(int stage)
    {
        var original = SaveTestGames.Rich(ticks: 0);
        SaveTestGames.RunUntil(original, s => s.Router!.BuildStage == stage);
        var loaded = RoundTrip.SaveAndLoad(original);
        RoundTrip.AssertContinuesIdentically(original, loaded, 800, every: 20);
    }

    [Fact]
    public void Save_with_a_route_result_pending_continues_identically()
    {
        var original = SaveTestGames.Rich(ticks: 0);
        SaveTestGames.RunUntil(original, s => s.Router!.PendingApplyTick > s.CurrentTick + 5);
        var loaded = RoundTrip.SaveAndLoad(original);
        RoundTrip.AssertContinuesIdentically(original, loaded, 800, every: 20);
    }

    [Fact]
    public void Parallel_ticks_after_loading_equal_sequential_ticks()
    {
        var original = SaveTestGames.Rich(parallel: true);
        var bytes = RoundTrip.SaveBytes(original);
        var sequential = RoundTrip.Load(bytes);
        var parallel = RoundTrip.Load(bytes);
        for (var t = 0; t < 1000; t++)
        {
            sequential.Tick(parallel: false);
            parallel.Tick(parallel: true);
        }

        Assert.Equal(sequential.ComputeFingerprint(), parallel.ComputeFingerprint());
        RoundTrip.AssertSameSections(sequential, parallel);
    }

    [Fact]
    public void Construction_after_loading_matches_the_original()
    {
        var original = SaveTestGames.Rich();
        var loaded = RoundTrip.SaveAndLoad(original);
        void Build(GameSimulation s)
        {
            if (s.CurrentTick % 97 == 0)
            {
                var x = (int)(s.CurrentTick % 200) + 20;
                s.Submit(new global::Talt.Core.Roads.BuildRoadCommand(Company.PlayerId, "paved", x, 30, x, 60));
            }
            else if (s.CurrentTick % 151 == 0)
            {
                s.Submit(new UndoConstructionCommand(Company.PlayerId));
            }
        }

        RoundTrip.AssertContinuesIdentically(original, loaded, 1500, every: 25, commands: Build);
    }

    [Fact]
    public void Game_over_survives_a_round_trip()
    {
        var original = SaveTestGames.Rich(ticks: 0);
        original.Construction!.Player.IsGameOver = true;
        var loaded = RoundTrip.SaveAndLoad(original);
        Assert.True(loaded.Finance!.IsGameOver(Company.PlayerId));
        loaded.Submit(new ChangeLoanCommand(Company.PlayerId, LoanMode.Borrow));
        var rejected = 0;
        loaded.CommandProcessed += p => rejected += p.Result.Succeeded ? 0 : 1;
        loaded.Tick();
        Assert.Equal(1, rejected);
    }

    [Fact]
    [Trait("Category", "Long")]
    public void Rich_game_continues_identically_for_ten_thousand_ticks()
    {
        var original = SaveTestGames.Rich(ticks: 1200, parallel: true);
        var loaded = RoundTrip.SaveAndLoad(original);
        RoundTrip.AssertContinuesIdentically(original, loaded, 10_000, every: 500, parallel: true);
    }
}
