using Talt.Core.Stations;

namespace Talt.Core.Tests.Demand;

/// <summary>User story 2: destination cargo travels over the lines, with a transfer at a shared stop, and is paid once.</summary>
public sealed class TransferTests
{
    private sealed class Recorder : IDeliveryListener
    {
        private readonly IDeliveryListener? _inner;

        public Recorder(IDeliveryListener? inner) => _inner = inner;

        public List<CargoDelivery> Deliveries { get; } = [];

        public void OnDelivered(in CargoDelivery delivery)
        {
            Deliveries.Add(delivery);
            _inner?.OnDelivered(delivery);
        }
    }

    [Fact]
    public void PassengersAtA_WaitByDestination_WithTheirNextStop()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 20);
        Assert.True(d.Waiting(d.A, d.TownB, d.B) > 0, "passengers for B wait for B");
        Assert.True(d.Waiting(d.A, d.TownC, d.B) > 0, "passengers for C go through B");
        Assert.Equal(0, d.Waiting(d.A, d.TownC, d.C)); // there is no direct line
        Assert.Equal(0, d.Waiting(d.A, d.TownA)); // no other stop of town A on the lines
        Assert.True(d.Stations.DirectedUnserved > 0, "passengers for towns the lines do not reach are unserved");
    }

    [Fact]
    public void AtoC_TransfersAtB_AndIsPaidOnceAtC_ByTheWholeDistance()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 20);
        var recorder = new Recorder(d.Stations.DeliveryListener);
        d.Stations.DeliveryListener = recorder;
        var forC = d.Waiting(d.A, d.TownC);
        var forB = d.Waiting(d.A, d.TownB);
        Assert.True(forC > 0);

        // A bus of line A–B loads at A (next stop B) and unloads at B (next stop A).
        var hold = new CargoHold();
        var loaded = d.Stations.Load(d.A, d.Passengers, 10_000, hold, 60, nextStationId: d.B);
        Assert.Equal(forB + forC, loaded);
        var atB = d.Stations.Unload(d.B, hold, UnloadMode.Normal, nextStationId: d.A);
        Assert.Equal(forB, recorder.Deliveries.Where(x => x.Cargo == d.Passengers).Sum(x => x.Amount));
        Assert.Equal(0, hold.Total);
        Assert.Equal(forC, d.Waiting(d.B, d.TownC, d.C));
        Assert.Equal(forB + forC, atB.Delivered + atB.Transferred);
        Assert.True(atB.CreditEarned > 0, "the first leg earns a transfer credit");

        // A bus of line B–C takes them on to C.
        recorder.Deliveries.Clear();
        var second = new CargoHold();
        Assert.Equal(forC, d.Stations.Load(d.B, d.Passengers, 10_000, second, 60, nextStationId: d.C));
        var atC = d.Stations.Unload(d.C, second, UnloadMode.Normal, nextStationId: d.B);
        Assert.Equal(forC, atC.Delivered);
        Assert.InRange(atC.CreditDelivered, atB.CreditEarned - forC, atB.CreditEarned); // proportional splits round down
        var delivery = Assert.Single(recorder.Deliveries);
        Assert.Equal(d.A, delivery.OriginStation); // paid by the distance from A
        Assert.Equal(d.C, delivery.DestinationStation);
        Assert.Equal(d.TownC, delivery.ConsumerId);
    }

    [Fact]
    public void AVehicleOfAnotherLine_LeavesTheCargoWaiting()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 20);
        var hold = new CargoHold();

        // A vehicle at A whose next stop is C (no route A→C exists) takes no destination cargo.
        Assert.Equal(0, d.Stations.Load(d.A, d.Passengers, 10_000, hold, 60, nextStationId: d.C));
        Assert.Equal(0, d.Stations.Load(d.A, d.Passengers, 10_000, hold, 60, nextStationId: -1));
    }

    [Fact]
    public void CargoRidesOn_WhenItsRouteContinuesWithTheVehicle()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 20);
        var hold = new CargoHold();
        d.Stations.Load(d.A, d.Passengers, 10_000, hold, 60, nextStationId: d.B);
        var forC = hold.Destination(d.TownC);

        // At B, a vehicle continuing to C (a line A–B–C) keeps the passengers for C on board.
        var result = d.Stations.Unload(d.B, hold, UnloadMode.Normal, nextStationId: d.C);
        Assert.Equal(forC, result.Kept);
        Assert.Equal(forC, hold.Total);

        // With the transfer rule everything not delivered is left at B, routed on.
        var transfer = d.Stations.Unload(d.B, hold, UnloadMode.Transfer, nextStationId: d.C);
        Assert.Equal(forC, transfer.Transferred);
        Assert.Equal(forC, d.Waiting(d.B, d.TownC, d.C));
    }

    [Fact]
    public void DestinationCargo_IsNotDeliveredInAnotherAcceptingTown()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 20);
        var hold = new CargoHold();
        d.Stations.Load(d.A, d.Passengers, 10_000, hold, 60, nextStationId: d.B);
        var forC = hold.Destination(d.TownC);
        var forB = hold.Destination(d.TownB);
        var before = d.Stations.DirectedDelivered;
        d.Stations.Unload(d.B, hold, UnloadMode.Normal, nextStationId: d.A);
        Assert.Equal(before + forB, d.Stations.DirectedDelivered);
        Assert.Equal(forC, d.Waiting(d.B, d.TownC)); // waits for C, not delivered in B although B accepts passengers
    }
}

internal static class HoldExtensions
{
    /// <summary>Units in a hold for one destination town.</summary>
    public static int Destination(this CargoHold hold, int town)
    {
        var total = 0;
        for (var i = 0; i < hold.Count; i++)
        {
            if (hold[i].Destination == town)
            {
                total += hold[i].Amount;
            }
        }

        return total;
    }
}
