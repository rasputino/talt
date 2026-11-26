using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Tests.Stations;

namespace Talt.Core.Tests.Rails;

/// <summary>US3: a train between two stations of a town carries passengers wagon by wagon and earns money.</summary>
public sealed class TrainEarningTests
{
    [Fact]
    public void TrainOnATownLineCarriesPassengersAndEarnsMoney()
    {
        // Candidate flat rows at the edge of the towns (the generated map is fixed, so the choice is too); the first one
        // whose two stations both reach town buildings is used.
        var probe = StationKit.Generated(10);
        var candidates = new List<(int X, int Z)>();
        for (var town = 0; town < probe.Simulation.Towns!.Count; town++)
        {
            var (cx, cz) = probe.Simulation.Towns.CentreOf(town);
            for (var r = 1; r < 14; r++)
            {
                foreach (var z in (ReadOnlySpan<int>)[cz - r, cz + r])
                {
                    for (var x0 = cx - 17; x0 <= cx; x0++)
                    {
                        if (FlatRow(probe.State, x0 - 3, x0 + 18, z))
                        {
                            candidates.Add((x0, z));
                            x0 += 6;
                        }
                    }
                }
            }
        }

        foreach (var (x, row) in candidates.Take(30))
        {
            var kit = StationKit.Generated(10);
            var state = kit.State;
            Assert.True(kit.Run(new BuildRailStationCommand(Company.PlayerId, x, row, 0, 1, 4)).Succeeded, kit.Last.ReasonCode);
            Assert.True(kit.Run(new BuildRailStationCommand(Company.PlayerId, x + 14, row, 0, 1, 4)).Succeeded, kit.Last.ReasonCode);
            Assert.True(state.Stations!.TryGetStationAt(x, row, out var a));
            Assert.True(state.Stations.TryGetStationAt(x + 14, row, out var b));
            kit.Days(1);
            if (state.Stations.CoveredBuildings(a).Count == 0 || state.Stations.CoveredBuildings(b).Count == 0)
            {
                continue;
            }

            Assert.True(kit.Run(new BuildRailCommand(Company.PlayerId, "normal", x - 2, row, x + 18, row)).Succeeded, kit.Last.ReasonCode);
            Assert.True(kit.Run(new BuildRailDepotCommand(Company.PlayerId, x - 3, row, Dir8.E)).Succeeded, kit.Last.ReasonCode);
            var depot = state.RailDepots.IdAt(0);
            var trains = kit.Simulation.Trains!;
            Assert.True(kit.Run(new BuyRailVehicleCommand(Company.PlayerId, depot, "loco-meridian")).Succeeded, kit.Last.ReasonCode);
            var id = trains.LastCreatedId;
            Assert.True(kit.Run(new BuyRailVehicleCommand(Company.PlayerId, depot, "wagon-coach", "", id)).Succeeded, kit.Last.ReasonCode);
            Assert.True(kit.Run(new BuyRailVehicleCommand(Company.PlayerId, depot, "wagon-coach", "", id)).Succeeded, kit.Last.ReasonCode);
            Assert.True(kit.Run(new Talt.Core.Vehicles.CreateLineCommand(Company.PlayerId, [a, b])).Succeeded, kit.Last.ReasonCode);
            Assert.True(kit.Run(new AssignTrainLineCommand(Company.PlayerId, id, kit.Simulation.RoadVehicles!.Lines.IdAt(0))).Succeeded, kit.Last.ReasonCode);
            for (var day = 0; day < 150 && trains.Deliveries == 0; day++)
            {
                kit.Days(1);
                Assert.Null(trains.CheckInvariants());
            }

            Assert.True(trains.Deliveries > 0, $"no delivery; train {TrainSystem.StatusOf(trains.Find(id)!)}");
            Assert.True(trains.Find(id)!.ProfitThisYear != 0);
            return;
        }

        Assert.Fail($"none of {candidates.Count} candidate rows reaches town buildings from both stations");
    }

    /// <summary>Every tile of the row is free, dry, buildable land at one height.</summary>
    private static bool FlatRow(ConstructionState state, int x0, int x1, int z)
    {
        var map = state.Map;
        if (x0 < 0 || x1 >= map.Width || z < 0 || z >= map.Depth)
        {
            return false;
        }

        var h = map.HeightAt(x0, z);
        for (var x = x0; x <= x1; x++)
        {
            var tile = map.Index(x, z);
            if (map.HeightAt(x, z) != h || !map.IsBuildable(x, z) || map.IsWater(tile) || state.Objects.KindAt(tile) != TileObjectKind.None
                || state.Roads.HasRoad(tile) || state.IsIndustry(x, z))
            {
                return false;
            }
        }

        return true;
    }
}
