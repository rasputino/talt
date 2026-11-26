using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;

namespace Talt.Core.Tests.Rails;

public sealed class TrainMovementTests
{
    /// <summary>Track x 5–50 on z 20, stations at x 10–14 and 40–44, a depot at x 4 facing east.</summary>
    internal static (RailKit Kit, int A, int B, int Depot) Corridor()
    {
        var kit = new RailKit();
        Assert.True(kit.Track(5, 20, 50, 20).Succeeded, kit.Last.ReasonCode);
        var a = kit.Station(10, 20, 0, 1, 5);
        var b = kit.Station(40, 20, 0, 1, 5);
        var depot = kit.Depot(4, 20, Dir8.E);
        return (kit, a, b, depot);
    }

    [Fact]
    public void BuyingComposesATrainInTheDepot()
    {
        var (kit, _, _, depot) = Corridor();
        var before = kit.State.Player.Balance;
        var id = kit.Train(depot);
        var train = kit.Trains.Find(id)!;
        Assert.Equal(4, train.Parts.Count);
        Assert.Equal(TrainMode.InDepot, train.Mode);
        Assert.True(train.PowerKw > 0 && train.Length > 0);
        Assert.True(kit.State.Player.Balance < before);
        Assert.Equal(ConstructionReasons.NeedsLocomotive,
            kit.Run(new BuyRailVehicleCommand(Company.PlayerId, depot, "wagon-coach")).ReasonCode);
    }

    [Fact]
    public void TrainRunsBetweenTwoStationsAndBack()
    {
        var (kit, a, b, depot) = Corridor();
        var id = kit.Train(depot);
        kit.Line([a, b], id);
        var train = kit.Trains.Find(id)!;
        var visitedB = false;
        var backAtA = false;
        kit.Ticks(6000, each: () =>
        {
            if (train.Mode == TrainMode.Loading && train.TargetId == b)
            {
                visitedB = true;
            }

            if (visitedB && train.Mode == TrainMode.Loading && train.TargetId == a)
            {
                backAtA = true;
            }
        });

        Assert.True(visitedB, $"status {TrainSystem.StatusOf(train)}");
        Assert.True(backAtA, $"status {TrainSystem.StatusOf(train)}");
    }

    [Fact]
    public void SellingInTheDepotRefundsTheValue()
    {
        var (kit, _, _, depot) = Corridor();
        var before = kit.State.Player.Balance;
        var id = kit.Train(depot);
        kit.Ok(new SellRailVehicleCommand(Company.PlayerId, id));
        Assert.Null(kit.Trains.Find(id));
        Assert.Equal(before, kit.State.Player.Balance);
    }

    [Fact]
    public void TrackUnderATrainCannotBeRemoved()
    {
        var (kit, a, b, depot) = Corridor();
        var id = kit.Train(depot);
        kit.Line([a, b], id);
        kit.Ticks(200);
        var train = kit.Trains.Find(id)!;
        Assert.Equal(TrainMode.Running, train.Mode);
        var tile = -1;
        for (var i = 0; i < train.StepCount && tile < 0; i++)
        {
            tile = kit.State.Objects.KindAt(train.StepFrom(i)) == TileObjectKind.Rail ? train.StepFrom(i) : -1;
        }

        Assert.True(tile >= 0);
        Assert.Equal(ConstructionReasons.TrainInTheWay,
            kit.Run(new RemoveRailCommand(Company.PlayerId, tile % kit.Rails.Width, tile / kit.Rails.Width, tile % kit.Rails.Width, tile / kit.Rails.Width)).ReasonCode);
    }
}
