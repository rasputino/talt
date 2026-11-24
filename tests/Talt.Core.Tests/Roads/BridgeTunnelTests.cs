using Talt.Core.Construction;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Roads;

/// <summary>User story 3: bridges and tunnels (FR-020..FR-023).</summary>
public class BridgeTunnelTests
{
    /// <summary>A river along Z covering x = 20..23 (bed 7, water at 9) on land at height 10.</summary>
    private static TestMap River(int fromX = 20, int toX = 23)
    {
        var map = new TestMap();
        for (var z = 0; z < 64; z++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                map.Water(x, z, 7, 9);
            }
        }

        return map;
    }

    /// <summary>A hill (height 16) covering x = 20..25 on land at height 10.</summary>
    private static TestMap Hill(int toX = 25)
    {
        var map = new TestMap();
        for (var z = 0; z < 64; z++)
        {
            for (var x = 20; x <= toX; x++)
            {
                map.Height(x, z, 16);
            }
        }

        return map;
    }

    [Fact]
    public void BridgeAcrossARiver_ConnectsBothBanks_AndLeavesTheRiver()
    {
        var kit = new RoadKit(River());
        kit.Road(10, 10, 19, 10);
        kit.Road(24, 10, 34, 10);

        var result = kit.Run(new BuildBridgeCommand(Company.PlayerId, "paved", 19, 10, 24, 10));

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.True(kit.Piece(19, 10).IsBridgeEnd);
        Assert.Equal(5, kit.Piece(19, 10).Span);
        Assert.True(kit.State.Map.IsWater(kit.Tile(21, 10)));
        Assert.Equal(RoadFeatures.OverBridgeX, kit.Piece(21, 10).Flags);
        Assert.Equal(24L * 65_536, kit.Route(10, 10, 34, 10));
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void RoadTool_BridgesShortWaterCrossingsByItself()
    {
        var kit = new RoadKit(River());

        Assert.True(kit.Road(10, 10, 34, 10).Succeeded, kit.Last.ReasonCode);

        Assert.True(kit.Route(10, 10, 34, 10) > 0);
        Assert.Contains(Enumerable.Range(15, 10), x => kit.Piece(x, 10).IsBridgeEnd);
    }

    [Fact]
    public void BridgeLongerThanTheEraAllows_IsRefused()
    {
        var kit = new RoadKit(River(20, 40), startYear: 1900);

        var result = kit.Run(new BuildBridgeCommand(Company.PlayerId, "dirt", 19, 10, 41, 10));

        Assert.Equal(ConstructionReasons.BridgeTooLong, result.ReasonCode);
    }

    [Fact]
    public void BridgeOverARoad_LeavesBothWorking_AndUnconnected()
    {
        var kit = new RoadKit();
        kit.Road(20, 2, 20, 30);
        var below = kit.Piece(20, 10);

        var result = kit.Run(new BuildBridgeCommand(Company.PlayerId, "paved", 17, 10, 23, 10));

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal(below.Links, kit.Piece(20, 10).Links);
        Assert.Equal(RoadFeatures.OverBridgeX, kit.Piece(20, 10).Flags);
        Assert.True(kit.Route(20, 2, 20, 30) > 0);
        Assert.True(kit.Route(17, 10, 23, 10) > 0);
        Assert.Equal(-1, kit.Route(17, 10, 20, 2));
    }

    [Fact]
    public void BridgeEndsAtDifferentHeights_AreRefused()
    {
        var kit = new RoadKit(new TestMap().Height(25, 10, 11));

        Assert.Equal(ConstructionReasons.BridgeEnds, kit.Run(new BuildBridgeCommand(Company.PlayerId, "paved", 20, 10, 25, 10)).ReasonCode);
    }

    [Fact]
    public void TunnelThroughAHill_ConnectsBothSides()
    {
        var kit = new RoadKit(Hill());
        kit.Road(5, 10, 19, 10);
        kit.Road(26, 10, 40, 10);

        var result = kit.Run(new BuildTunnelCommand(Company.PlayerId, "paved", 19, 10, Dir8.E));

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.True(kit.Piece(19, 10).IsTunnelPortal);
        Assert.True(kit.Piece(26, 10).IsTunnelPortal);
        Assert.Equal(RoadFeatures.UnderTunnelX, kit.Piece(22, 10).Flags);
        Assert.Equal(16, kit.State.Map.HeightAt(22, 10));
        Assert.Equal(35L * 65_536, kit.Route(5, 10, 40, 10));
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void TunnelWithoutAnExit_IsRefused()
    {
        var kit = new RoadKit(Hill(toX: 63));

        Assert.Equal(ConstructionReasons.TunnelNoExit, kit.Run(new BuildTunnelCommand(Company.PlayerId, "paved", 19, 10, -1)).ReasonCode);
    }

    [Fact]
    public void TerraformingAboveATunnelOrUnderABridge_IsRefused()
    {
        var kit = new RoadKit(Hill());
        kit.Run(new BuildTunnelCommand(Company.PlayerId, "paved", 19, 10, Dir8.E));
        kit.Run(new BuildBridgeCommand(Company.PlayerId, "paved", 5, 30, 12, 30));

        Assert.Equal(ConstructionReasons.ObjectInTheWay, kit.Run(new TerraformCommand(Company.PlayerId, TerraformMode.Lower, 22, 10, 22, 10)).ReasonCode);
        Assert.Equal(ConstructionReasons.ObjectInTheWay, kit.Run(new TerraformCommand(Company.PlayerId, TerraformMode.Lower, 8, 30, 8, 30)).ReasonCode);
    }
}
