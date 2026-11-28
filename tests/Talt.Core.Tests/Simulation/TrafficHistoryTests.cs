using Talt.Core.Determinism;
using Talt.Core.Simulation;
using Talt.Tests.Support;
using Talt.Core.Tests.Rails;
using Talt.Core.Tests.Vehicles;

namespace Talt.Core.Tests.Simulation;

public sealed class TrafficHistoryTests
{
    [Fact]
    public void PassageCountsJoinOppositeDirectionsAndKeepRoadSeparateFromRail()
    {
        var traffic = new TrafficHistory();
        traffic.Record(10, 17, rail: false);
        traffic.Record(17, 10, rail: false);
        traffic.Record(17, 10, rail: true);
        Assert.Empty(traffic.Completed);
        traffic.CloseMonth(new GameDate(2001, 1, 1, 0));
        Assert.Equal((2000, 12), (traffic.Year, traffic.Month));
        Assert.Equal(2, traffic.Completed[new(10, 17, false)]);
        Assert.Equal(1, traffic.Completed[new(10, 17, true)]);
        traffic.CloseMonth(new GameDate(2001, 2, 1, 0));
        Assert.Empty(traffic.Completed);
    }

    [Fact]
    public void RunningBusProducesPassagesOnTheRoadItUsed()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        kit.Line([west, east], kit.Buy(depot));
        kit.Days(31);
        var first = kit.State.Map.Index(20, 20);
        var closed = kit.Simulation.Traffic!.Completed;
        Assert.True(closed.ContainsKey(new(first, first + 1, false)));
        Assert.DoesNotContain(closed.Keys, s => s.Rail);
    }

    [Fact]
    public void MultiCarTrainCountsOnePassageOnItsSingleExitFromTheDepot()
    {
        var (kit, a, b, depot) = TrainMovementTests.Corridor();
        var id = kit.Train(depot, wagons: 3);
        kit.Line([a, b], id);
        kit.Ticks(31 * GameClock.TicksPerDay);
        var exited = kit.Tile(7, 20);
        Assert.Equal(1, kit.Simulation.Traffic!.Completed[new(exited, exited + 1, true)]);
    }

    [Fact]
    public void RebuiltLaneKeepsTheOriginalEndpointsAcrossMonthBoundariesAndFingerprintReads()
    {
        var traffic = new TrafficHistory();
        var reference = new TrafficHistory();
        void Check()
        {
            var actual = new StateHasher();
            var expected = new StateHasher();
            traffic.AppendTo(actual);
            reference.AppendTo(expected);
            Assert.Equal(expected.GetFingerprint(), actual.GetFingerprint());
        }

        traffic.RegisterRoadLane(0, 10, 11);
        traffic.RegisterRoadLane(1, 11, 10);
        traffic.RecordRoadLane(0);
        traffic.RecordRoadLane(1);
        reference.Record(10, 11, false);
        reference.Record(11, 10, false);
        Check();

        traffic.RegisterRoadLane(0, 10, 18);
        traffic.RecordRoadLane(0);
        reference.Record(10, 18, false);
        var february = new GameDate(2001, 2, 1, 0);
        traffic.CloseMonth(february);
        reference.CloseMonth(february);
        traffic.RegisterRoadLane(0, 10, 26);
        traffic.RecordRoadLane(0);
        reference.Record(10, 26, false);
        Assert.Equal(2, traffic.Completed[new(10, 11, false)]);
        Assert.Equal(1, traffic.Completed[new(10, 18, false)]);
        Check();

        var march = new GameDate(2001, 3, 1, 0);
        traffic.CloseMonth(march);
        reference.CloseMonth(march);
        Assert.Single(traffic.Completed);
        Check();
        var april = new GameDate(2001, 4, 1, 0);
        traffic.CloseMonth(april);
        reference.CloseMonth(april);
        Assert.Empty(traffic.Completed);
        Check();
    }

    [Fact]
    public void FingerprintIsIndependentOfVisitOrder()
    {
        var first = new TrafficHistory();
        var second = new TrafficHistory();
        first.Record(2, 5, false);
        first.Record(9, 12, true);
        second.Record(12, 9, true);
        second.Record(5, 2, false);
        var a = new StateHasher();
        var b = new StateHasher();
        first.AppendTo(a);
        second.AppendTo(b);
        Assert.Equal(a.GetFingerprint(), b.GetFingerprint());
    }

    [Fact]
    public void StableNetworkAndMonthCloseAllocateNothingAfterWarmup()
    {
        var traffic = new TrafficHistory();
        var date = new GameDate(2000, 2, 1, 0);
        AllocationAssert.NoSteadyStateAllocations(() =>
        {
            for (var tile = 0; tile < 2048; tile++)
            {
                traffic.Record(tile, tile + 1, false);
            }

            traffic.CloseMonth(date);
        }, 10, "traffic collection and rotation");
    }
}
