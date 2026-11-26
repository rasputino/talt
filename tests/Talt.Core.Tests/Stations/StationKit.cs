using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Stations;

/// <summary>Station test helpers: a game on a hand-made or generated map, commands applied at once, days.</summary>
internal sealed class StationKit
{
    public StationKit(GameSimulation simulation)
    {
        Simulation = simulation;
        Simulation.CommandProcessed += processed => Last = processed.Result;
    }

    public GameSimulation Simulation { get; }

    public ConstructionState State => Simulation.Construction!;

    public StationRegistry Stations => Simulation.Stations!;

    public CommandResult Last { get; private set; }

    /// <summary>A flat hand-made map with no towns or industries (placement rules).</summary>
    public static StationKit Flat(TestMap? map = null, long balance = 100_000_000)
    {
        map ??= new TestMap();
        var data = map.Build();
        var settings = new ScenarioSettings(Seed: 3, StartYear: 1950, MapSizeTiles: 64, RoadSpacing: 16, RoadRemovalChancePerMille: 0,
            VehicleCount: 0, LineCount: 1, Map: data.Settings);
        return new StationKit(GameSimulation.Create(settings, data, rules: TestMap.Rules with { StartingBalance = balance }));
    }

    /// <summary>A generated map with towns and industries (cargo flow).</summary>
    public static StationKit Generated(int seed = 10, int size = 256, StationSettings? stations = null, bool extraMoney = true)
    {
        var settings = new ScenarioSettings(
            Seed: seed,
            StartYear: 1950,
            MapSizeTiles: size,
            VehicleCount: 0,
            Map: new MapSettings(size, size, Seed: seed, Roughness: 30, MaxHeight: 40, WaterAmount: 15, SeaLevel: 6),
            Stations: stations);
        var kit = new StationKit(GameSimulation.Create(settings));
        if (extraMoney)
        {
            kit.State.Player.Refund(100_000_000); // Outside commands: not for recorded-session tests.
        }

        return kit;
    }

    public int Tile(int x, int z) => State.Map.Index(x, z);

    public CommandResult Run(ISimulationCommand command)
    {
        Simulation.Submit(command);
        Simulation.ProcessPendingCommands();
        Assert.Null(State.Roads.Validate());
        return Last;
    }

    public CommandResult Road(int x0, int z0, int x1, int z1, string type = "paved") =>
        Run(new BuildRoadCommand(Company.PlayerId, type, x0, z0, x1, z1));

    public CommandResult Build(string pieceType, int x, int z) => Run(new BuildStationCommand(Company.PlayerId, pieceType, x, z));

    public CommandResult Warehouse(string size, int x, int z) => Run(new BuildWarehouseCommand(Company.PlayerId, size, x, z));

    public CommandResult Remove(int x, int z) => Run(new RemoveStationCommand(Company.PlayerId, x, z));

    public int StationAt(int x, int z) => Stations.TryGetStationAt(x, z, out var id) ? id : -1;

    public int Cargo(string id) => Simulation.Content.CargoIndex(id);

    /// <summary>Advances whole days (40 ticks each).</summary>
    public void Days(int days, bool parallel = true)
    {
        for (var i = 0; i < days * GameClock.TicksPerDay; i++)
        {
            Simulation.Tick(parallel);
        }
    }

    /// <summary>
    /// Finds the first open industry (slot order) that produces a cargo and has a tile where a
    /// truck bay could be built beside a new road, builds that road and the bay, and returns the station id.
    /// </summary>
    public (int StationId, int IndustryId, int Cargo) ServeIndustry(string? kind = null, string? cargoId = null)
    {
        var industries = Simulation.Industries;
        var plan = new ConstructionPlan();
        for (var slot = 0; slot < industries.Count; slot++)
        {
            var c = -1;
            if (kind is not null)
            {
                if (industries.KindIdAt(slot) != kind)
                {
                    continue;
                }

                c = Cargo(cargoId!);
            }

            for (var cargo = 0; cargo < Simulation.Content.CargoCount && c < 0; cargo++)
            {
                if (industries.EffectiveProductionPerMonth(slot, cargo) > 0)
                {
                    c = cargo;
                }
            }

            if (c < 0 || !industries.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                continue;
            }

            // A bay one tile beyond the footprint's east side, with a road one more tile east going north-south.
            for (var z = minZ; z < maxZ; z++)
            {
                var bx = maxX + 1;
                var rx = maxX + 2;
                if (!State.Map.Contains(rx, z + 1) || !State.Map.Contains(rx, z - 1))
                {
                    continue;
                }

                if (Road(rx, z - 1, rx, z + 1).Succeeded)
                {
                    StationPlanner.PlanPiece(State, Company.PlayerId, Stations.Rules.FindPiece("truck-bay"), bx, z, plan);
                    if (plan.IsValid && Build("truck-bay", bx, z).Succeeded)
                    {
                        return (StationAt(bx, z), industries.IdAt(slot), c);
                    }
                }
            }
        }

        throw new InvalidOperationException("No producing industry could be served on this map.");
    }

    /// <summary>A bus bay beside a town street, returning the station id (first fit in tile order around town 0).</summary>
    public int ServeTown(int town = 0, string piece = "bus-bay")
    {
        var (cx, cz) = Simulation.Towns!.CentreOf(town);
        var plan = new ConstructionPlan();
        var bus = Stations.Rules.FindPiece(piece);
        for (var r = 1; r < 12; r++)
        {
            for (var z = cz - r; z <= cz + r; z++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    if (!State.Map.Contains(x, z))
                    {
                        continue;
                    }

                    if (Stations.JoinTarget(Company.PlayerId, x, z) != -1)
                    {
                        continue; // A new station each time.
                    }

                    StationPlanner.PlanPiece(State, Company.PlayerId, bus, x, z, plan);
                    if (plan.IsValid && Build(piece, x, z).Succeeded)
                    {
                        return StationAt(x, z);
                    }
                }
            }
        }

        throw new InvalidOperationException("No bus bay fits near the town.");
    }
}
