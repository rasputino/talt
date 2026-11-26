using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Roads;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Tests.Construction;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Vehicles;

/// <summary>Road vehicle test helpers: a flat hand-made map, commands applied at once, ticks with the invariant check.</summary>
internal sealed class VehicleKit
{
    public VehicleKit(GameSimulation simulation)
    {
        Simulation = simulation;
        Simulation.CommandProcessed += processed => Last = processed.Result;
    }

    public GameSimulation Simulation { get; }

    public ConstructionState State => Simulation.Construction!;

    public RoadVehicleSystem Vehicles => Simulation.RoadVehicles!;

    public StationRegistry Stations => Simulation.Stations!;

    public CommandResult Last { get; private set; }

    /// <summary>A flat map with no towns or industries.</summary>
    public static VehicleKit Flat(int size = 64, long balance = 100_000_000)
    {
        var map = new TestMap(size);
        var data = map.Build();
        var settings = new ScenarioSettings(Seed: 3, StartYear: 1950, MapSizeTiles: size, RoadSpacing: 16, RoadRemovalChancePerMille: 0,
            VehicleCount: 0, LineCount: 1, Map: data.Settings);
        return new VehicleKit(GameSimulation.Create(settings, data, rules: TestMap.Rules with { StartingBalance = balance }));
    }

    public CommandResult Run(ISimulationCommand command)
    {
        Simulation.Submit(command);
        Simulation.ProcessPendingCommands();
        return Last;
    }

    public void Road(int x0, int z0, int x1, int z1) =>
        Assert.True(Run(new BuildRoadCommand(Company.PlayerId, "paved", x0, z0, x1, z1)).Succeeded, Last.ReasonCode);

    /// <summary>Builds a bay or drive-through stop and returns its station id.</summary>
    public int Stop(string piece, int x, int z)
    {
        Assert.True(Run(new BuildStationCommand(Company.PlayerId, piece, x, z)).Succeeded, Last.ReasonCode);
        Assert.True(Stations.TryGetStationAt(x, z, out var id));
        return id;
    }

    public int Depot(int x, int z)
    {
        Assert.True(Run(new BuildDepotCommand(Company.PlayerId, x, z)).Succeeded, Last.ReasonCode);
        Assert.True(Vehicles.Depots.TryGetAt(State.Map.Index(x, z), out var id));
        return id;
    }

    public int Buy(int depot, string model = "bus-townsman", string cargo = "")
    {
        Assert.True(Run(new BuyVehicleCommand(Company.PlayerId, depot, model, cargo)).Succeeded, Last.ReasonCode);
        return Vehicles.LastCreatedId;
    }

    /// <summary>Creates a line through the stations and assigns the vehicles (which start).</summary>
    public int Line(int[] stations, params int[] vehicles)
    {
        Assert.True(Run(new CreateLineCommand(Company.PlayerId, stations, vehicles.Length > 0 ? vehicles[0] : -1)).Succeeded, Last.ReasonCode);
        var line = Vehicles.Lines.IdAt(Vehicles.Lines.Count - 1);
        for (var i = 1; i < vehicles.Length; i++)
        {
            Assert.True(Run(new AssignLineCommand(Company.PlayerId, vehicles[i], line)).Succeeded, Last.ReasonCode);
        }

        return line;
    }

    public int Slot(int vehicleId) => Vehicles.SlotOf(vehicleId);

    /// <summary>Runs ticks, checking the invariants after every one.</summary>
    public void Ticks(int ticks, bool parallel = true)
    {
        for (var i = 0; i < ticks; i++)
        {
            Simulation.Tick(parallel);
            var problem = Vehicles.CheckInvariants();
            Assert.True(problem is null, $"tick {Simulation.CurrentTick}: {problem}");
        }
    }

    public void Days(int days, bool parallel = true) => Ticks(days * GameClock.TicksPerDay, parallel);

    /// <summary>
    /// A straight east-west road on z = 20 from x = 8 to x = 48 with a bus bay south of each end and a depot south of the
    /// middle; returns (west stop, east stop, depot).
    /// </summary>
    public (int West, int East, int Depot) Corridor()
    {
        Road(8, 20, 48, 20);
        var west = Stop("bus-bay", 8, 21);
        var east = Stop("bus-bay", 48, 21);
        var depot = Depot(28, 21);
        return (west, east, depot);
    }
}
