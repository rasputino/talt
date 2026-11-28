using Talt.Core.Maps;
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
    public void Map_sites_ignore_legacy_padding_and_write_a_canonical_kind()
    {
        var settings = SaveTestGames.Settings.Map!;
        using var legacy = new SaveBuffer();
        using var writer = new SaveWriter(legacy);
        writer.WriteString(settings.ToText());
        writer.Write(default(MapStatistics));
        writer.WriteInt(1);
        writer.WriteInt(10);
        writer.WriteInt(11);
        writer.WriteInt(unchecked((int)0xABCDEF01));
        writer.WriteInt(2);
        writer.WriteInt(100);
        var tiles = new byte[settings.Width * settings.Depth];
        for (var layer = 0; layer < 5; layer++)
        {
            writer.WriteArray(tiles);
        }

        var map = MapData.ReadState(new SaveReader(legacy, SaveSections.Map), settings);
        Assert.Equal(new Site(10, 11, SiteKind.IndustryCoast, 2, 100), Assert.Single(map.Sites));
        using var canonical = new SaveBuffer();
        using var output = new SaveWriter(canonical);
        map.WriteState(output, settings.ToText());
        var reader = new SaveReader(canonical, SaveSections.Map);
        Assert.Equal(settings.ToText(), reader.ReadString());
        Assert.Equal(default, reader.Read<MapStatistics>());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(10, reader.ReadInt());
        Assert.Equal(11, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
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
