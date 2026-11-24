using Talt.Core.Construction;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Roads;

/// <summary>User story 2: roads follow gentle slopes; steep terrain is refused with a reason.</summary>
public class RoadSlopeTests
{
    /// <summary>A ridge along Z rising one step per tile from x = 20 to x = 24 and back down to x = 28.</summary>
    internal static TestMap Hill()
    {
        var map = new TestMap();
        for (var z = 0; z < 64; z++)
        {
            for (var x = 21; x <= 27; x++)
            {
                map.Height(x, z, 10 + (4 - Math.Abs(x - 24)));
            }
        }

        return map;
    }

    [Fact]
    public void GentleHill_IsCrossedWithRampPieces()
    {
        var kit = new RoadKit(Hill());

        Assert.True(kit.Road(10, 10, 40, 10).Succeeded, kit.Last.ReasonCode);

        Assert.True(kit.Route(10, 10, 40, 10) > 0);
        for (var x = 20; x <= 28; x++)
        {
            var piece = kit.Piece(x, 10);
            Assert.True(piece.HasLink(Dir8.E) && piece.HasLink(Dir8.W), $"tile {x}");
            Assert.Equal(2, piece.LinkCount);
        }
    }

    [Fact]
    public void Cliff_IsRefusedAsTooSteep()
    {
        var map = new TestMap();
        for (var z = 0; z < 64; z++)
        {
            for (var x = 30; x < 64; x++)
            {
                map.Height(x, z, 14);
            }
        }

        var kit = new RoadKit(map);

        var result = kit.Road(20, 10, 40, 10);

        Assert.Equal(ConstructionReasons.TooSteep, result.ReasonCode);
        Assert.Equal(0, kit.Roads.RoadTileCount);
    }

    [Fact]
    public void JunctionsAreNotMadeOnRamps()
    {
        var kit = new RoadKit(Hill());
        kit.Road(10, 10, 40, 10);

        // A road along the slope towards a ramp tile of the first road may not join there.
        kit.Road(22, 2, 22, 10);

        Assert.Equal(2, kit.Piece(22, 10).LinkCount);
        Assert.Null(kit.Roads.Validate());
    }

    [Fact]
    public void TerraformingUnderARoad_IsRefused()
    {
        var kit = new RoadKit();
        kit.Road(5, 5, 20, 5);

        var result = kit.Run(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 10, 5, 10, 5));

        Assert.Equal(ConstructionReasons.ObjectInTheWay, result.ReasonCode);
    }

    [Fact]
    public void RaisingATileUnderADiagonalRoadsCorner_IsRefused()
    {
        var kit = new RoadKit();
        kit.Road(5, 5, 15, 15);

        // The diagonal from (8, 8) to (9, 9) cuts across (9, 8) and (8, 9).
        var result = kit.Run(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 9, 8, 9, 8));

        Assert.Equal(ConstructionReasons.ObjectInTheWay, result.ReasonCode);
    }
}
