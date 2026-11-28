using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Rails;

public sealed class RailPlannerTests
{
    [Fact]
    public void RulesLoadFromTheEmbeddedFile()
    {
        var rules = RailRules.Default;
        Assert.Equal("normal", rules.Types[0].Id);
        Assert.True(rules.Types[rules.ElectrifiedType()].Electrified);
        Assert.Contains(rules.Models, m => m.Kind == RollingStockKind.Locomotive && m.FirstYear <= 1850);
        Assert.True(rules.Braking > 0);
    }

    [Fact]
    public void InvalidRulesNameTheField()
    {
        var json = RailRules.ReadResource().Replace("\"maxParts\": 24", "\"maxParts\": 0", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => RailRules.FromJson(json, Content.GameContent.Default));
        Assert.Contains("maxParts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StraightTrackIsBuiltAndCharged()
    {
        var kit = new RailKit();
        var before = kit.State.Player.Balance;
        Assert.True(kit.Track(5, 10, 20, 10).Succeeded, kit.Last.ReasonCode);
        for (var x = 5; x <= 20; x++)
        {
            Assert.True(kit.Rails.HasRail(kit.Tile(x, 10)));
            Assert.Equal(TileObjectKind.Rail, kit.State.Objects.KindAt(kit.Tile(x, 10)));
        }

        Assert.Equal(Dir8.Bit(Dir8.E), kit.Rails.LinksAt(kit.Tile(5, 10)));
        Assert.Equal((byte)(Dir8.Bit(Dir8.E) | Dir8.Bit(Dir8.W)), kit.Rails.LinksAt(kit.Tile(12, 10)));
        Assert.True(kit.State.Player.Balance < before);
    }

    [Fact]
    public void DiagonalDragMakesGentleCurves()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(5, 5, 25, 15).Succeeded, kit.Last.ReasonCode);
        for (var tile = 0; tile < kit.Rails.Width * kit.Rails.Depth; tile++)
        {
            Assert.True(RailGeometry.IsValidShape(kit.Rails.LinksAt(tile)));
        }

        Assert.Null(kit.Rails.Validate());
    }

    [Fact]
    public void BranchOffATrackMakesAJunction()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(5, 20, 30, 20).Succeeded);
        Assert.True(kit.Track(15, 20, 25, 30).Succeeded, kit.Last.ReasonCode);
        var junctions = 0;
        for (var x = 5; x <= 30; x++)
        {
            if (Dir8.Count(kit.Rails.LinksAt(kit.Tile(x, 20))) > 2)
            {
                junctions++;
            }
        }

        Assert.Equal(1, junctions);
        Assert.Null(kit.Rails.Validate());
    }

    [Fact]
    public void NinetyDegreeKinkIsNeverBuilt()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(10, 10, 20, 10).Succeeded);
        // A track ending at the end tile heading straight north would kink 90°: the planner curves instead or refuses.
        var result = kit.Track(20, 10, 20, 0);
        Assert.Null(kit.Rails.Validate());
        if (result.Succeeded)
        {
            Assert.True(RailGeometry.IsValidShape(kit.Rails.LinksAt(kit.Tile(20, 10))));
        }
    }

    [Fact]
    public void JoiningAtARightAngleIsRefusedInsteadOfLooping()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(30, 10, 30, 40).Succeeded);
        // From the tile beside the track straight into its side: only a loop could arrive at 45°.
        var result = kit.Track(31, 25, 30, 25);
        Assert.False(result.Succeeded);
        Assert.Equal(ConstructionReasons.TrackLoop, result.ReasonCode);
        Assert.Equal(31, kit.Rails.RailTileCount);
    }

    [Fact]
    public void TrackClimbsGentleSlopesOnly()
    {
        var map = new TestMap();
        for (var x = 20; x < 64; x++)
        {
            for (var z = 0; z < 64; z++)
            {
                map.Height(x, z, 10 + Math.Min(x - 19, 3));
            }
        }

        var kit = new RailKit(map);
        Assert.True(kit.Track(10, 30, 30, 30).Succeeded, kit.Last.ReasonCode);
        Assert.Null(kit.Rails.Validate());
    }

    [Fact]
    public void BridgeSpansWater()
    {
        var map = new TestMap();
        for (var x = 20; x <= 24; x++)
        {
            for (var z = 0; z < 64; z++)
            {
                map.Water(x, z, 7, 9);
            }
        }

        var kit = new RailKit(map);
        Assert.False(kit.Track(10, 30, 30, 30).Succeeded);
        Assert.True(kit.Run(new BuildRailBridgeCommand(Company.PlayerId, "normal", 19, 30, 25, 30)).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Rails.PieceAt(kit.Tile(19, 30)).IsBridgeEnd);
        Assert.Equal(kit.Tile(25, 30), kit.Rails.Follow(kit.Tile(19, 30), Dir8.E));
        Assert.True(kit.Track(10, 30, 19, 30).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Track(25, 30, 35, 30).Succeeded, kit.Last.ReasonCode);
        Assert.Null(kit.Rails.Validate());
    }

    [Fact]
    public void TunnelGoesUnderAHill()
    {
        var map = new TestMap();
        for (var x = 20; x <= 26; x++)
        {
            for (var z = 0; z < 64; z++)
            {
                map.Height(x, z, 14);
            }
        }

        var kit = new RailKit(map);
        Assert.True(kit.Run(new BuildRailTunnelCommand(Company.PlayerId, "normal", 19, 30, Dir8.E)).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Rails.PieceAt(kit.Tile(19, 30)).IsTunnelPortal);
        Assert.Equal(kit.Tile(27, 30), kit.Rails.Follow(kit.Tile(19, 30), Dir8.E));
    }

    [Fact]
    public void LevelCrossingOnAStraightRoad()
    {
        var kit = new RailKit();
        Assert.True(kit.Run(new BuildRoadCommand(Company.PlayerId, "paved", 20, 5, 20, 40)).Succeeded);
        Assert.True(kit.Track(10, 20, 30, 20).Succeeded, kit.Last.ReasonCode);
        var crossing = kit.Tile(20, 20);
        Assert.True(kit.Rails.HasRail(crossing));
        Assert.Equal(TileObjectKind.Road, kit.State.Objects.KindAt(crossing));
        // The road on a crossing may not change while the track is there.
        Assert.False(kit.Run(new BuildRoadCommand(Company.PlayerId, "paved", 20, 20, 25, 25)).Succeeded);
    }

    [Fact]
    public void RemovalRefundsAndUnlinksNeighbours()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(5, 10, 20, 10).Succeeded);
        Assert.True(kit.Run(new RemoveRailCommand(Company.PlayerId, 10, 10, 12, 10)).Succeeded, kit.Last.ReasonCode);
        Assert.False(kit.Rails.HasRail(kit.Tile(11, 10)));
        Assert.Equal(Dir8.Bit(Dir8.W), kit.Rails.LinksAt(kit.Tile(9, 10)));
        Assert.Equal(TileObjectKind.None, kit.State.Objects.KindAt(kit.Tile(11, 10)));
        Assert.Null(kit.Rails.Validate());
    }

    [Fact]
    public void UndoRestoresTrack()
    {
        var kit = new RailKit();
        var before = kit.State.Player.Balance;
        Assert.True(kit.Track(5, 10, 20, 10).Succeeded);
        Assert.True(kit.Run(new UndoConstructionCommand(Company.PlayerId)).Succeeded, kit.Last.ReasonCode);
        Assert.Equal(0, kit.Rails.RailTileCount);
        Assert.Equal(before, kit.State.Player.Balance);
    }

    [Fact]
    public void SignalsOnlyOnPlainTrack()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(5, 20, 30, 20).Succeeded);
        Assert.True(kit.Track(15, 20, 25, 30).Succeeded);
        Assert.True(kit.Signal(10, 20, SignalKind.Block, RailSignal.FaceHigh).Succeeded, kit.Last.ReasonCode);
        Assert.Equal(SignalKind.Block, RailSignal.Kind(kit.Rails.SignalAt(kit.Tile(10, 20))));
        var junction = Enumerable.Range(5, 26).First(x => Dir8.Count(kit.Rails.LinksAt(kit.Tile(x, 20))) > 2);
        Assert.Equal(ConstructionReasons.SignalPlace, kit.Signal(junction, 20, SignalKind.Block, 1).ReasonCode);
        Assert.True(kit.Signal(10, 20, SignalKind.None, 0).Succeeded);
        Assert.Equal(0, kit.Rails.SignalAt(kit.Tile(10, 20)));
    }

    [Fact]
    public void ElectrificationIsUnavailableIn1959()
    {
        var kit = new RailKit(startYear: 1959);
        Assert.False(kit.Track(5, 10, 20, 10, "electrified").Succeeded);
        kit.Path((5, 10), (20, 10));
        Assert.False(kit.Run(new ConvertRailCommand(Company.PlayerId, "electrified", 5, 10, 20, 10)).Succeeded);
    }

    [Fact]
    public void ElectrifyConvertsTrack()
    {
        var kit = new RailKit(startYear: 1960);
        Assert.True(kit.Track(5, 10, 20, 10).Succeeded);
        Assert.True(kit.Run(new ConvertRailCommand(Company.PlayerId, "electrified", 5, 10, 20, 10)).Succeeded, kit.Last.ReasonCode);
        Assert.Equal(kit.State.RailRules.ElectrifiedType(), kit.Rails.TypeAt(kit.Tile(12, 10)));
    }

    [Fact]
    public void StationAndDepotAreBuiltAndJoinTrack()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(5, 20, 14, 20).Succeeded);
        Assert.True(kit.Run(new BuildRailStationCommand(Company.PlayerId, 15, 19, 0, 2, 5)).Succeeded, kit.Last.ReasonCode);
        Assert.Equal(TileObjectKind.Station, kit.State.Objects.KindAt(kit.Tile(15, 20)));
        Assert.True(kit.Rails.PieceAt(kit.Tile(14, 20)).HasLink(Dir8.E)); // The track joined the platform's end.
        Assert.True(kit.State.Stations!.TryGetStationAt(17, 20, out var station));
        Assert.Equal(Talt.Core.Stations.StationKind.Rail, kit.State.Stations.KindAt(kit.State.Stations.SlotOf(station)));
        Assert.True(kit.Run(new BuildRailDepotCommand(Company.PlayerId, 4, 20, Dir8.E)).Succeeded, kit.Last.ReasonCode);
        Assert.Equal(1, kit.State.RailDepots.Count);
        Assert.True(kit.Rails.PieceAt(kit.Tile(5, 20)).HasLink(Dir8.W));
        Assert.Null(kit.Rails.Validate());
        Assert.True(kit.Run(new Talt.Core.Stations.RemoveStationCommand(Company.PlayerId, 17, 20)).Succeeded, kit.Last.ReasonCode);
        Assert.False(kit.Rails.HasRail(kit.Tile(17, 20)));
        Assert.Null(kit.Rails.Validate());
    }

    [Fact]
    public void GamesWithoutRailKeepTheirFingerprint()
    {
        var a = new RailKit();
        var b = new RailKit();
        Assert.Equal(a.Simulation.ComputeFingerprint(), b.Simulation.ComputeFingerprint());
        Assert.True(a.Track(5, 10, 20, 10).Succeeded);
        Assert.NotEqual(a.Simulation.ComputeFingerprint(), b.Simulation.ComputeFingerprint());
    }
}
