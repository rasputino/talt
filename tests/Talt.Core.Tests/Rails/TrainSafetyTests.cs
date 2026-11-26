using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Rails;

/// <summary>
/// Scenario fixtures (SC-002): trains never share a tile (checked every tick by <see cref="RailKit.Ticks"/>) and keep
/// completing their lines (no deadlock) on standard layouts.
/// </summary>
public sealed class TrainSafetyTests
{
    /// <summary>
    /// A racetrack run clockwise: top straight z 10 and bottom straight z 34 between x 15 and 55, block signals every
    /// few tiles facing the travel direction and one-way path signals that keep trains clockwise. Stations on both
    /// straights, a depot off the top-left.
    /// </summary>
    internal static (RailKit Kit, int Top, int Bottom, int Depot) Racetrack(SignalKind signals = SignalKind.Block)
    {
        var kit = new RailKit(new TestMap(96));
        kit.Path((15, 10), (55, 10), (58, 13), (58, 31), (55, 34), (15, 34), (12, 31), (12, 13), (15, 10));
        var top = kit.Station(30, 10, 0, 1, 6);
        var bottom = kit.Station(30, 34, 0, 1, 6);
        Assert.True(kit.Track(15, 10, 8, 10).Succeeded, kit.Last.ReasonCode);
        var depot = kit.Depot(7, 10, Dir8.E);
        // Clockwise: east on the top, south on the right, west on the bottom, north on the left.
        foreach (var x in new[] { 20, 26, 38, 44, 50 })
        {
            Assert.True(kit.Signal(x, 10, signals, RailSignal.FaceLow).Succeeded, $"top {x}: {kit.Last.ReasonCode}");
            Assert.True(kit.Signal(x, 34, signals, RailSignal.FaceHigh).Succeeded, $"bottom {x}: {kit.Last.ReasonCode}");
        }

        foreach (var z in new[] { 18, 26 })
        {
            Assert.True(kit.Signal(58, z, SignalKind.OneWayPath, RailSignal.FaceHigh).Succeeded, $"right {z}: {kit.Last.ReasonCode}");
            Assert.True(kit.Signal(12, z, SignalKind.OneWayPath, RailSignal.FaceLow).Succeeded, $"left {z}: {kit.Last.ReasonCode}");
        }

        return (kit, top, bottom, depot);
    }

    [Fact]
    public void TenTrainsOnASignalledLoopNeverCollideAndKeepRunning()
    {
        var (kit, top, bottom, depot) = Racetrack();
        var trains = new int[10];
        for (var i = 0; i < trains.Length; i++)
        {
            trains[i] = kit.Train(depot, wagons: 2);
        }

        kit.Line([top, bottom], trains);
        var visits = new Dictionary<int, int>();
        var wasLoading = new HashSet<int>();
        kit.Ticks(20_000, each: () => CountVisits(kit, visits, wasLoading));
        foreach (var id in trains)
        {
            Assert.True(visits.GetValueOrDefault(id) >= 2, $"train {id} stopped {visits.GetValueOrDefault(id)} times{System.Environment.NewLine}{kit.Describe()}{kit.Dump(0, 0, 80, 40)}");
        }
    }

    [Fact]
    public void ParallelAndSequentialTicksAreIdentical()
    {
        var fingerprints = new List<ulong>[2];
        for (var run = 0; run < 2; run++)
        {
            var (kit, top, bottom, depot) = Racetrack(SignalKind.Path);
            var trains = new int[6];
            for (var i = 0; i < trains.Length; i++)
            {
                trains[i] = kit.Train(depot, wagons: 2);
            }

            kit.Line([top, bottom], trains);
            fingerprints[run] = [];
            for (var k = 0; k < 10; k++)
            {
                kit.Ticks(500, parallel: run == 0);
                fingerprints[run].Add(kit.Simulation.ComputeFingerprint());
            }
        }

        Assert.Equal(fingerprints[0], fingerprints[1]);
    }

    /// <summary>
    /// A single line with a passing loop: termini at both ends (x 6–10 and x 70–74 on z 20), the loop on z 24 between x
    /// 30 and 50, path signals at the loop ends; two trains on one line meet at the loop.
    /// </summary>
    [Fact]
    public void SingleLineWithPassingLoopHasNoDeadlock()
    {
        var kit = new RailKit(new TestMap(96));
        kit.Path((4, 20), (75, 20));
        kit.Path((30, 20), (34, 24), (46, 24), (50, 20));
        var west = kit.Station(6, 20, 0, 1, 5);
        var east = kit.Station(70, 20, 0, 1, 5);
        var depot = kit.Depot(3, 20, Dir8.E);
        // Path signals both ways on the main track inside the loop and on the loop track, and at the single-track ends.
        Assert.True(kit.Signal(40, 20, SignalKind.Path, RailSignal.FaceLow | RailSignal.FaceHigh).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Signal(40, 24, SignalKind.Path, RailSignal.FaceLow | RailSignal.FaceHigh).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Signal(26, 20, SignalKind.Path, RailSignal.FaceLow).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Signal(54, 20, SignalKind.Path, RailSignal.FaceHigh).Succeeded, kit.Last.ReasonCode);
        var first = kit.Train(depot, wagons: 2);
        var second = kit.Train(depot, wagons: 2);
        kit.Line([west, east], first);
        kit.Ticks(600);
        kit.Line([east, west], second);
        var visits = new Dictionary<int, int>();
        var wasLoading = new HashSet<int>();
        kit.Ticks(24_000, each: () => CountVisits(kit, visits, wasLoading));
        Assert.True(visits.GetValueOrDefault(first) >= 4, $"first stopped {visits.GetValueOrDefault(first)} times");
        Assert.True(visits.GetValueOrDefault(second) >= 4, $"second stopped {visits.GetValueOrDefault(second)} times");
    }

    /// <summary>
    /// A three-platform terminus (z 19–21, x 10–15) with its throat at (18, 20): trains leave east on the outbound track
    /// (z 20), run round a loop at the far end past a through station and come back on the inbound track (z 26), whose last
    /// path signal before the terminus picks a free platform. Three trains of one line use different platforms.
    /// </summary>
    [Fact]
    public void TrainsChooseFreePlatformsAtATerminus()
    {
        var kit = new RailKit(new TestMap(96));
        var terminus = kit.Station(10, 19, 0, 3, 6);
        kit.Path((15, 20), (62, 20), (64, 22), (64, 24), (62, 26), (24, 26), (18, 20));
        kit.Path((15, 19), (17, 19), (18, 20));
        kit.Path((15, 21), (17, 21), (18, 20));
        var far = kit.Station(40, 20, 0, 1, 5);
        foreach (var x in new[] { 30, 50 })
        {
            Assert.True(kit.Signal(x, 20, SignalKind.OneWayPath, RailSignal.FaceLow).Succeeded, kit.Last.ReasonCode);
            Assert.True(kit.Signal(x, 26, SignalKind.OneWayPath, RailSignal.FaceHigh).Succeeded, kit.Last.ReasonCode);
        }

        kit.Path((62, 26), (68, 26));
        var depot = kit.Depot(69, 26, Dir8.W);
        var trains = new[] { kit.Train(depot, wagons: 1), kit.Train(depot, wagons: 1), kit.Train(depot, wagons: 1) };
        kit.Line([terminus, far], trains);
        var platforms = new HashSet<int>();
        var visits = new Dictionary<int, int>();
        var wasLoading = new HashSet<int>();
        kit.Ticks(20_000, each: () =>
        {
            CountVisits(kit, visits, wasLoading);
            foreach (var id in trains)
            {
                var train = kit.Trains.Find(id)!;
                if (train.Mode == TrainMode.Loading && train.TargetId == terminus)
                {
                    platforms.Add(train.EndTile / kit.Rails.Width);
                }
            }
        });

        foreach (var id in trains)
        {
            Assert.True(visits.GetValueOrDefault(id) >= 2, $"train {id} stopped {visits.GetValueOrDefault(id)} times{System.Environment.NewLine}{kit.Describe()}{kit.Dump(0, 0, 80, 40)}");
        }

        Assert.True(platforms.Count >= 2, $"platforms used: {string.Join(",", platforms)}");
    }

    /// <summary>Counts how many times each train began loading.</summary>
    private static void CountVisits(RailKit kit, Dictionary<int, int> visits, HashSet<int> wasLoading)
    {
        for (var slot = 0; slot < kit.Trains.Count; slot++)
        {
            var train = kit.Trains.TrainAt(slot);
            if (train.Mode == TrainMode.Loading)
            {
                if (wasLoading.Add(train.Id))
                {
                    visits[train.Id] = visits.GetValueOrDefault(train.Id) + 1;
                }
            }
            else
            {
                wasLoading.Remove(train.Id);
            }
        }
    }
}
