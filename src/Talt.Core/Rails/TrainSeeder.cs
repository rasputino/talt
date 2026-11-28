using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Simulation;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>
/// Debug and benchmark placements (<c>--seed-trains</c>, contracts/cli.md): clockwise racetrack loops, each with a station
/// on both straights, one-way path signals every few tiles, a depot and up to <see cref="PerLoop"/> trains on one line
/// between its stations. Loop corners are searched in row order, a cell apart (on level land they form a lattice). Returned as recorded commands (infinite money first) whose ids are predicted from the
/// registries' next ids, so recordings replay them. Deterministic: candidate corners in row order.
/// </summary>
public static class TrainSeeder
{
    /// <summary>Trains on one loop.</summary>
    public const int PerLoop = 8;

    private const int Straight = 30; // Length of the top and bottom straights between the corner diagonals.
    private const int Side = 8; // Length of the left and right sides between the corner diagonals.
    private const int CellWidth = Straight + 16;
    private const int CellDepth = Side + 12;
    private const int MaxRelief = 5; // Most height steps a footprint may span to be levelled.
    private const int Step = 2; // Search step: divides both cell sizes, so loops on level land keep the lattice.

    public static IReadOnlyList<ISimulationCommand> Plan(GameSimulation simulation, int trains, int companyId = Company.PlayerId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var commands = new List<ISimulationCommand>();
        var state = simulation.Construction;
        var system = simulation.Trains;
        if (state?.Stations is not { } stations || system is null || simulation.RoadVehicles is not { } road || trains <= 0)
        {
            return commands;
        }

        var year = simulation.Date.Year;
        var locomotive = system.Rules.Models.FirstOrDefault(m => m.Id == "loco-meridian" && m.AvailableIn(year))
            ?? system.Rules.Models.First(m => m.Kind == RollingStockKind.Locomotive && m.Power != RailPower.Electric && m.AvailableIn(year));

        if (!state.Player.HasInfiniteMoney)
        {
            commands.Add(new EnableInfiniteMoneyCommand(companyId));
        }

        var map = state.Map;
        var plan = new ConstructionPlan();
        var planner = new ConstructionPlanner();
        var nextStation = stations.NextStationId;
        var nextDepot = state.RailDepots.NextId;
        var nextTrain = system.NextId;
        var nextLine = road.Lines.NextId;
        var remaining = trains;
        var placed = new List<(int X, int Z)>();
        var claims = new List<TileRect>(); // Tiles each placed loop changes (its footprint and any levelling).
        for (var oz = 2; oz + CellDepth < map.Depth && remaining > 0; oz += Step)
        {
            for (var ox = 2; ox + CellWidth < map.Width && remaining > 0; ox += Step)
            {
                var claim = Footprint(ox, oz);
                if (Overlaps(placed, ox, oz) || Touches(claims, claim) || !Survey(state, claim, out var start))
                {
                    continue;
                }

                var loop = Loop(companyId, ox, oz);
                if (start >= 0)
                {
                    // Sloping land: level it first, and keep clear of the tiles the levelling reaches beyond the footprint.
                    var level = new TerraformCommand(companyId, TerraformMode.Level, claim.X0, claim.Z0, claim.X1, claim.Z1, start % map.Width, start / map.Width);
                    if (!Valid(level, state, planner, plan) || Touches(claims, claim = Union(claim, plan, map.Width)))
                    {
                        continue;
                    }

                    loop.Insert(0, level);
                }

                if (!AllValid(state, planner, plan, loop))
                {
                    continue;
                }

                placed.Add((ox, oz));
                claims.Add(claim);
                commands.AddRange(loop);
                var top = nextStation++;
                var bottom = nextStation++;
                var depot = nextDepot++;
                var count = Math.Min(PerLoop, remaining);
                var ids = new int[count];
                for (var t = 0; t < count; t++)
                {
                    ids[t] = nextTrain++;
                    commands.Add(new BuyRailVehicleCommand(companyId, depot, locomotive.Id));
                    commands.Add(new BuyRailVehicleCommand(companyId, depot, "wagon-coach", "", ids[t]));
                    commands.Add(new BuyRailVehicleCommand(companyId, depot, "wagon-coach", "", ids[t]));
                }

                commands.Add(new CreateLineCommand(companyId, [top, bottom]));
                var line = nextLine++;
                foreach (var id in ids)
                {
                    commands.Add(new AssignTrainLineCommand(companyId, id, line));
                }

                remaining -= count;
            }
        }

        return commands;
    }

    /// <summary>True when a loop at (ox, oz) would come closer than a cell to one already placed.</summary>
    private static bool Overlaps(List<(int X, int Z)> placed, int ox, int oz)
    {
        foreach (var (x, z) in placed)
        {
            if (Math.Abs(ox - x) < CellWidth && Math.Abs(oz - z) < CellDepth)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The commands of one loop with its minimum corner at (ox, oz): track, stations, depot and signals.</summary>
    private static List<ConstructionCommand> Loop(int company, int ox, int oz)
    {
        var (left, right, top, bottom) = Bounds(ox, oz);
        (int X, int Z)[] points =
        [
            (left + 3, top), (right - 3, top), (right, top + 3), (right, bottom - 3), (right - 3, bottom),
            (left + 3, bottom), (left, bottom - 3), (left, top + 3), (left + 3, top),
        ];
        var list = new List<ConstructionCommand>();
        for (var i = 0; i + 1 < points.Length; i++)
        {
            list.Add(new BuildRailCommand(company, "normal", points[i].X, points[i].Z, points[i + 1].X, points[i + 1].Z));
        }

        var stationX = left + 3 + (Straight / 2) - 2;
        list.Add(new BuildRailStationCommand(company, stationX, top, 0, 1, 5));
        list.Add(new BuildRailStationCommand(company, stationX, bottom, 0, 1, 5));
        list.Add(new BuildRailCommand(company, "normal", right - 3, bottom, right + 1, bottom));
        list.Add(new BuildRailDepotCommand(company, right + 2, bottom, Dir8.W));
        for (var x = left + 5; x < right - 3; x += 5)
        {
            if (x >= stationX - 1 && x <= stationX + 5)
            {
                continue;
            }

            list.Add(new SetSignalCommand(company, x, top, (int)SignalKind.OneWayPath, RailSignal.FaceLow));
            list.Add(new SetSignalCommand(company, x, bottom, (int)SignalKind.OneWayPath, RailSignal.FaceHigh));
        }

        list.Add(new SetSignalCommand(company, right, (top + bottom) / 2, (int)SignalKind.OneWayPath, RailSignal.FaceHigh));
        list.Add(new SetSignalCommand(company, left, (top + bottom) / 2, (int)SignalKind.OneWayPath, RailSignal.FaceLow));
        return list;
    }

    /// <summary>
    /// Checks a loop's footprint cheaply before any preview: no water, objects or industries, and at most
    /// <see cref="MaxRelief"/> between its lowest and highest tiles. <paramref name="start"/> is a tile at its most common
    /// height (ties to the lowest), the one a levelling gives it so that it moves the least land, or -1 when the footprint
    /// is already level.
    /// </summary>
    /// <remarks>
    /// Why sloping land is levelled: the commands are previewed one by one on the unbuilt state, but there a command that
    /// fits alone can be refused once the ones before it are built. A ramp takes only track along its axis, so a corner
    /// diagonal joining a straight that ends on a ramp is too steep, and track routed round a slope leaves the signals and
    /// the next piece off it. The loop was then left open and its trains never found a route. On level land every piece
    /// lies where the loop expects it, so the previews hold for the whole sequence.
    /// </remarks>
    private static bool Survey(ConstructionState state, TileRect footprint, out int start)
    {
        var map = state.Map;
        Span<int> counts = stackalloc int[256];
        int low = int.MaxValue, high = int.MinValue;
        for (var z = footprint.Z0; z <= footprint.Z1; z++)
        {
            for (var x = footprint.X0; x <= footprint.X1; x++)
            {
                var tile = map.Index(x, z);
                if (map.IsWater(tile) || state.Objects.KindAt(tile) != TileObjectKind.None || state.IsIndustry(x, z))
                {
                    start = -1;
                    return false;
                }

                var h = map.Heights[tile];
                counts[h]++;
                (low, high) = (Math.Min(low, h), Math.Max(high, h));
            }
        }

        var height = low;
        for (var h = low + 1; h <= high; h++)
        {
            height = counts[h] > counts[height] ? h : height;
        }

        start = -1;
        if (high - low > MaxRelief)
        {
            return false;
        }

        if (low == high)
        {
            return true;
        }

        for (var z = footprint.Z0; z <= footprint.Z1 && start < 0; z++)
        {
            for (var x = footprint.X0; x <= footprint.X1 && start < 0; x++)
            {
                start = map.HeightAt(x, z) == height ? map.Index(x, z) : -1;
            }
        }

        return true;
    }

    /// <summary>The loop's track, depot spur and depot with a one-tile border (always inside the map: see the search bounds).</summary>
    private static TileRect Footprint(int ox, int oz)
    {
        var (left, right, top, bottom) = Bounds(ox, oz);
        return new TileRect(left - 1, top - 1, right + 3, bottom + 1);
    }

    /// <summary>The claim grown to the tiles the last preview changes.</summary>
    private static TileRect Union(TileRect claim, ConstructionPlan plan, int width)
    {
        int x0 = claim.X0, z0 = claim.Z0, x1 = claim.X1, z1 = claim.Z1;
        foreach (var change in plan.Changes)
        {
            var x = change.Index % width;
            var z = change.Index / width;
            (x0, z0, x1, z1) = (Math.Min(x0, x), Math.Min(z0, z), Math.Max(x1, x), Math.Max(z1, z));
        }

        return new TileRect(x0, z0, x1, z1);
    }

    /// <summary>
    /// True when the claim comes within a tile of one already placed: each levelling was planned on the unbuilt land, so a
    /// tile between two claims keeps the height both plans saw.
    /// </summary>
    private static bool Touches(List<TileRect> claims, TileRect claim)
    {
        foreach (var other in claims)
        {
            if (claim.X0 <= other.X1 + 1 && other.X0 <= claim.X1 + 1 && claim.Z0 <= other.Z1 + 1 && other.Z0 <= claim.Z1 + 1)
            {
                return true;
            }
        }

        return false;
    }

    private static (int Left, int Right, int Top, int Bottom) Bounds(int ox, int oz) => (ox, ox + Straight + 6, oz, oz + Side + 6);

    /// <summary>Previews one command on the current state; money does not count (the seeder enables infinite money).</summary>
    private static bool Valid(ConstructionCommand command, ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        command.Preview(state, planner, plan).Succeeded || plan.Reason == ConstructionReasons.NotEnoughMoney;

    /// <summary>Previews the loop's track, stations and depot on the current state (independent cells, so previews agree).</summary>
    private static bool AllValid(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan, List<ConstructionCommand> loop)
    {
        foreach (var command in loop)
        {
            if (command is SetSignalCommand)
            {
                continue; // Needs the track first; always valid on the loop's plain tiles.
            }

            if (command is not TerraformCommand && !Valid(command, state, planner, plan))
            {
                return false;
            }
        }

        return true;
    }
}
