using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Roads;
using Talt.Core.Simulation;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>
/// Debug and benchmark placements (<c>--seed-trains</c>, contracts/cli.md): clockwise racetrack loops on a lattice, each
/// with a station on both straights, one-way path signals every few tiles, a depot and up to <see cref="PerLoop"/> trains
/// on one line between its stations. Returned as recorded commands (infinite money first) whose ids are predicted from the
/// registries' next ids, so recordings replay them. Deterministic: lattice cells in row order.
/// </summary>
public static class TrainSeeder
{
    /// <summary>Trains on one loop.</summary>
    public const int PerLoop = 8;

    private const int Straight = 30; // Length of the top and bottom straights between the corner diagonals.
    private const int Side = 8; // Length of the left and right sides between the corner diagonals.
    private const int CellWidth = Straight + 16;
    private const int CellDepth = Side + 12;

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
        for (var oz = 2; oz + CellDepth < map.Depth && remaining > 0; oz += CellDepth)
        {
            for (var ox = 2; ox + CellWidth < map.Width && remaining > 0; ox += CellWidth)
            {
                var loop = Loop(companyId, ox, oz);
                if (!AllValid(state, planner, plan, loop))
                {
                    continue;
                }

                commands.AddRange(loop);
                var top = nextStation++;
                var bottom = nextStation++;
                var depot = nextDepot++;
                var count = Math.Min(PerLoop, remaining);
                var ids = new int[count];
                for (var t = 0; t < count; t++)
                {
                    ids[t] = nextTrain++;
                    commands.Add(new BuyRailVehicleCommand(companyId, depot, "loco-meridian"));
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

    /// <summary>The commands of one loop with its minimum corner at (ox, oz): track, stations, depot and signals.</summary>
    private static List<ConstructionCommand> Loop(int company, int ox, int oz)
    {
        var left = ox;
        var right = ox + Straight + 6;
        var top = oz;
        var bottom = oz + Side + 6;
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

    /// <summary>Previews the loop's track, stations and depot on the current state (independent cells, so previews agree).</summary>
    private static bool AllValid(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan, List<ConstructionCommand> loop)
    {
        foreach (var command in loop)
        {
            if (command is SetSignalCommand)
            {
                continue; // Needs the track first; always valid on the loop's plain tiles.
            }

            if (!command.Preview(state, planner, plan).Succeeded && plan.Reason != ConstructionReasons.NotEnoughMoney)
            {
                return false;
            }
        }

        return true;
    }
}
