using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Rails;

/// <summary>Electrification, short platforms, slopes and level crossings (US3, US7, clarifications Q4, Q5).</summary>
public sealed class TrainFeatureTests
{
    [Fact]
    public void ElectricLocomotivesNeedElectrifiedTrack()
    {
        // The electric model is sold from 1960.
        var late = new RailKit(startYear: 1970);
        late.Path((5, 20), (50, 20));
        var sa = late.Station(10, 20, 0, 1, 5);
        var sb = late.Station(40, 20, 0, 1, 5);
        var d = late.Depot(4, 20, Dir8.E);
        var electric = late.Train(d, locomotive: "loco-aurora", wagons: 1);
        late.Line([sa, sb], electric);
        late.Ticks(200);
        Assert.Equal(TrainMode.InDepot, late.Trains.Find(electric)!.Mode);
        Assert.True((late.Trains.Find(electric)!.Flags & TrainBits.NoRoute) != 0);
        late.Ok(new ConvertRailCommand(Company.PlayerId, "electrified", 4, 20, 50, 20));
        late.Ticks(1200);
        Assert.NotEqual(TrainMode.InDepot, late.Trains.Find(electric)!.Mode);
    }

    [Fact]
    public void LongTrainPullsForwardAlongAShortThroughPlatform()
    {
        var kit = new RailKit();
        kit.Path((5, 20), (60, 20));
        var a = kit.Station(10, 20, 0, 1, 3);
        var b = kit.Station(40, 20, 0, 1, 3);
        var depot = kit.Depot(4, 20, Dir8.E);
        // 1 locomotive + 6 coaches ≈ 5.8 tiles on 3-tile platforms.
        var id = kit.Train(depot, wagons: 6);
        kit.Line([a, b], id);
        var train = kit.Trains.Find(id)!;
        var stopsAtB = 0;
        var wasLoading = false;
        kit.Ticks(4000, each: () =>
        {
            var loading = train.Mode == TrainMode.Loading && train.TargetId == b;
            if (loading && !wasLoading)
            {
                stopsAtB++;
            }

            wasLoading = loading;
        });

        Assert.True(stopsAtB >= 2, $"stops at b: {stopsAtB}{System.Environment.NewLine}{kit.Describe()}");
    }

    [Fact]
    public void HeavyTrainsClimbSlower()
    {
        static int TopSpeed(int wagons, bool hill)
        {
            var map = new TestMap();
            if (hill)
            {
                for (var x = 20; x < 64; x++)
                {
                    for (var z = 0; z < 64; z++)
                    {
                        map.Height(x, z, 10 + Math.Min((x - 19) / 2, 4));
                    }
                }
            }

            var kit = new RailKit(map);
            kit.Path((5, 20), (60, 20));
            var a = kit.Station(6, 20, 0, 1, 3);
            var b = kit.Station(56, 20, 0, 1, 3);
            var depot = kit.Depot(4, 20, Dir8.E);
            var id = kit.Train(depot, locomotive: "loco-ironhorse", wagons: wagons, wagon: "wagon-hopper");
            kit.Line([a, b], id);
            var train = kit.Trains.Find(id)!;
            var best = 0;
            kit.Ticks(900, each: () =>
            {
                if (train.StepCount > 0)
                {
                    var x = train.StepFrom(train.HeadStep) % kit.Rails.Width;
                    if (x is >= 21 and <= 28)
                    {
                        best = Math.Max(best, train.Speed);
                    }
                }
            });

            return best;
        }

        Assert.True(TopSpeed(8, hill: true) < TopSpeed(8, hill: false));
    }

    [Fact]
    public void RoadVehiclesWaitAtALevelCrossingATrainOwns()
    {
        var kit = new RailKit();
        kit.Ok(new BuildRoadCommand(Company.PlayerId, "paved", 30, 4, 30, 40));
        kit.Path((5, 20), (60, 20));
        var crossing = kit.Tile(30, 20);
        Assert.True(kit.Rails.HasRail(crossing));
        var a = kit.Station(10, 20, 0, 1, 4);
        var b = kit.Station(50, 20, 0, 1, 4);
        var depot = kit.Depot(4, 20, Dir8.E);
        var id = kit.Train(depot);
        kit.Line([a, b], id);
        var closed = false;
        kit.Ticks(2000, each: () =>
        {
            if (kit.Trains.IsClosed(crossing))
            {
                closed = true;
            }
        });

        Assert.True(closed);
    }
}
