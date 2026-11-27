using Talt.Core.Construction;
using Talt.Core.Stations;
using Talt.Core.Tests.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Demand;

/// <summary>
/// Destination demand test helpers on a generated map: bus stops in three towns, a depot, two lines A–B and B–C sharing
/// the stop in B (vehicles assigned so the lines have capacity), routes applied.
/// </summary>
internal sealed class DemandKit
{
    private DemandKit(StationKit kit) => Kit = kit;

    public StationKit Kit { get; }

    public StationRegistry Stations => Kit.Stations;

    public RoadVehicleSystem Vehicles => Kit.Simulation.RoadVehicles!;

    public int Passengers => Kit.Cargo("passengers");

    public int A { get; private set; }

    public int B { get; private set; }

    public int C { get; private set; }

    public int TownA { get; private set; }

    public int TownB { get; private set; }

    public int TownC { get; private set; }

    public int Depot { get; private set; }

    public int LineAB { get; private set; }

    public int LineBC { get; private set; }

    /// <summary>Three towns with a stop each, lines A–B and B–C, routes applied (one day after the lines).</summary>
    public static DemandKit ThreeTowns(int seed = 10, bool directLine = false)
    {
        var kit = new DemandKit(StationKit.Generated(seed));
        var towns = kit.Kit.Simulation.Towns!;
        Assert.True(towns.Count >= 3, "the map needs three towns");
        kit.TownA = 0;
        kit.TownB = 1;
        kit.TownC = 2;
        kit.A = kit.Kit.ServeTown(kit.TownA);
        kit.B = kit.Kit.ServeTown(kit.TownB);
        kit.C = kit.Kit.ServeTown(kit.TownC);
        kit.Depot = kit.BuildDepotNear(kit.TownA);
        kit.LineAB = kit.Line([kit.A, kit.B], 2);
        kit.LineBC = kit.Line([kit.B, kit.C], 2);
        if (directLine)
        {
            kit.Line([kit.A, kit.C], 2);
        }

        kit.Kit.Days(kit.Kit.Simulation.Router!.Rules.ApplyDelayDays + 1);
        Assert.True(kit.Kit.Simulation.Router!.AppliedCount > 0, "routes should be applied");
        return kit;
    }

    public int BuildDepotNear(int town)
    {
        var (cx, cz) = Kit.Simulation.Towns!.CentreOf(town);
        var plan = new ConstructionPlan();
        for (var r = 1; r < 16; r++)
        {
            for (var z = cz - r; z <= cz + r; z++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    DepotPlanner.PlanDepot(Kit.State, Company.PlayerId, Vehicles.Rules, x, z, plan);
                    if (plan.IsValid && Kit.Run(new BuildDepotCommand(Company.PlayerId, x, z)).Succeeded)
                    {
                        Vehicles.Depots.TryGetAt(Kit.State.Map.Index(x, z), out var depot);
                        return depot;
                    }
                }
            }
        }

        throw new InvalidOperationException("No depot fits near the town.");
    }

    /// <summary>A line through the stations with buses that stay stopped in the depot (capacity without driving).</summary>
    public int Line(int[] stations, int buses)
    {
        var ids = new int[buses];
        for (var i = 0; i < buses; i++)
        {
            Assert.True(Kit.Run(new BuyVehicleCommand(Company.PlayerId, Depot, "bus-townsman")).Succeeded, Kit.Last.ReasonCode);
            ids[i] = Vehicles.LastCreatedId;
        }

        Assert.True(Kit.Run(new CreateLineCommand(Company.PlayerId, stations, ids[0])).Succeeded, Kit.Last.ReasonCode);
        var line = Vehicles.Lines.IdAt(Vehicles.Lines.Count - 1);
        for (var i = 1; i < buses; i++)
        {
            Assert.True(Kit.Run(new AssignLineCommand(Company.PlayerId, ids[i], line)).Succeeded, Kit.Last.ReasonCode);
        }

        foreach (var id in ids)
        {
            Kit.Run(new StartVehicleCommand(Company.PlayerId, id, Start: false)); // stay in the depot: tests move cargo by hand
        }

        return line;
    }

    /// <summary>Waiting passengers at a station's group for a town, by next group.</summary>
    public int Waiting(int station, int town, int nextStation = -2)
    {
        var list = new List<DestinationAmount>();
        Stations.DestinationWaiting(station, Passengers, list);
        var nextKey = nextStation == -2 ? int.MinValue : Stations.GroupKeyOfStation(nextStation);
        var total = 0;
        foreach (var entry in list)
        {
            if (entry.Town == town && (nextKey == int.MinValue || entry.NextGroupKey == nextKey))
            {
                total += entry.Amount;
            }
        }

        return total;
    }

    /// <summary>Marks passengers served at a stop (as a first loading would) and lets days pass so they appear.</summary>
    public void Supply(int station, int days)
    {
        Stations.MarkServed(station, Passengers);
        Kit.Days(days);
    }
}
