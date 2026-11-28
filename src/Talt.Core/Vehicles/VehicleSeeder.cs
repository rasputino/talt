using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Vehicles;

/// <summary>
/// Debug and benchmark placements (<c>--seed-vehicles</c>, research R14, contracts/cli.md): bus bays beside roads on a
/// lattice of <paramref name="spacing"/> tiles, a depot near each, a line between every bay and its east and south
/// neighbours, and the vehicles spread round-robin over the lines. Returned as recorded commands (infinite money first,
/// then stations, depots, purchases, lines), whose ids are predicted from the registries' next ids, so recordings
/// replay them. Deterministic: lattice points in row order.
/// </summary>
public static class VehicleSeeder
{
    /// <summary>Most seeded vehicles on one line.</summary>
    public const int MaxPerLine = 8;

    public static IReadOnlyList<ISimulationCommand> Plan(GameSimulation simulation, int vehicles, int spacing = 40, int companyId = Company.PlayerId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var commands = new List<ISimulationCommand>();
        var state = simulation.Construction;
        var system = simulation.RoadVehicles;
        if (state?.Stations is not { } stations || system is null || vehicles <= 0)
        {
            return commands;
        }

        if (!state.Player.HasInfiniteMoney)
        {
            commands.Add(new EnableInfiniteMoneyCommand(companyId));
        }

        var bus = system.Rules.Models.First(m => m.CargoCategory == "passengers" && m.AvailableIn(simulation.Date.Year));
        var map = state.Map;
        var plan = new ConstructionPlan();
        var bay = stations.Rules.FindPiece("bus-bay");
        var columns = map.Width / spacing;
        var rows = map.Depth / spacing;
        var site = new int[columns * rows]; // Station id per lattice point, or −1.
        var siteComponent = new int[columns * rows]; // Road component of the bay's road, so lines only join connected bays.
        var components = new Dictionary<int, int>();
        Array.Fill(site, -1);
        var depotOf = new Dictionary<int, int>();
        var used = new HashSet<int>();
        var nextStation = stations.NextStationId;
        var nextDepot = system.Depots.NextId;
        var depotCommands = new List<ISimulationCommand>();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cx = (column * spacing) + (spacing / 2);
                var cz = (row * spacing) + (spacing / 2);
                if (!TryFind(cx, cz, 10, (x, z) =>
                    {
                        if (stations.JoinTarget(companyId, x, z) != -1 || Near(used, map.Width, x, z, 2))
                        {
                            return false;
                        }

                        StationPlanner.PlanPiece(state, companyId, bay, x, z, plan);
                        return plan.IsValid;
                    }, out var bx, out var bz))
                {
                    continue;
                }

                used.Add(map.Index(bx, bz));
                commands.Add(new BuildStationCommand(companyId, "bus-bay", bx, bz));
                siteComponent[(row * columns) + column] = RoadComponent(state, components, map.Index(bx, bz));
                var stationId = nextStation++;
                site[(row * columns) + column] = stationId;
                if (TryFind(bx, bz, 6, (x, z) =>
                    {
                        if (Near(used, map.Width, x, z, 1))
                        {
                            return false;
                        }

                        DepotPlanner.PlanDepot(state, companyId, system.Rules, x, z, plan);
                        return plan.IsValid;
                    }, out var dx, out var dz))
                {
                    used.Add(map.Index(dx, dz));
                    depotCommands.Add(new BuildDepotCommand(companyId, dx, dz));
                    depotOf[stationId] = nextDepot++;
                }
            }
        }

        commands.AddRange(depotCommands);

        // A line from every station with a depot to its nearest other seeded station (lattice distance, then row order).
        var lines = new List<(int A, int B, int Depot)>();
        var paired = new HashSet<long>();
        for (var i = 0; i < site.Length; i++)
        {
            var a = site[i];
            if (a < 0 || !depotOf.TryGetValue(a, out var depot))
            {
                continue;
            }

            var best = -1;
            var bestDistance = int.MaxValue;
            for (var j = 0; j < site.Length; j++)
            {
                if (j == i || site[j] < 0 || siteComponent[j] != siteComponent[i])
                {
                    continue;
                }

                var distance = Math.Abs((j % columns) - (i % columns)) + Math.Abs((j / columns) - (i / columns));
                if (distance < bestDistance && distance <= 3)
                {
                    bestDistance = distance;
                    best = j;
                }
            }

            if (best >= 0 && paired.Add(((long)Math.Min(i, best) << 32) | (uint)Math.Max(i, best)))
            {
                lines.Add((a, site[best], depot));
            }
        }

        if (lines.Count == 0)
        {
            return commands;
        }

        // At most this many vehicles per line, so seeded games do not start out gridlocked.
        vehicles = Math.Min(vehicles, lines.Count * MaxPerLine);
        var nextVehicle = system.NextId;
        var nextLine = system.Lines.NextId;
        var assignments = new List<(int Vehicle, int Line)>();
        for (var v = 0; v < vehicles; v++)
        {
            var line = v % lines.Count;
            commands.Add(new BuyVehicleCommand(companyId, lines[line].Depot, bus.Id));
            assignments.Add((nextVehicle++, nextLine + line));
        }

        foreach (var (a, b, _) in lines)
        {
            commands.Add(new CreateLineCommand(companyId, [a, b]));
        }

        foreach (var (vehicle, line) in assignments)
        {
            commands.Add(new AssignLineCommand(companyId, vehicle, line));
        }

        return commands;
    }

    /// <summary>
    /// A label for the road component next to a planned bay tile: the smallest road tile index reachable from its road
    /// neighbours over road links (flood fill over the road layer, memoised per component).
    /// </summary>
    private static int RoadComponent(ConstructionState state, Dictionary<int, int> labels, int bayTile)
    {
        var roads = state.Roads;
        for (var d = 0; d < 8; d += 2)
        {
            var start = roads.Neighbour(bayTile, d);
            if (start < 0 || !roads.HasRoad(start))
            {
                continue;
            }

            if (labels.TryGetValue(start, out var known))
            {
                return known;
            }

            var visited = new List<int> { start };
            var seen = new HashSet<int> { start };
            var label = start;
            for (var i = 0; i < visited.Count; i++)
            {
                var tile = visited[i];
                label = Math.Min(label, tile);
                var links = roads.LinksAt(tile);
                for (var dir = 0; dir < 8; dir++)
                {
                    if ((links & (1 << dir)) != 0 && roads.Follow(tile, dir) is var next and >= 0 && seen.Add(next))
                    {
                        visited.Add(next);
                    }
                }
            }

            foreach (var tile in visited)
            {
                labels[tile] = label;
            }

            return label;
        }

        return -1 - bayTile;
    }

    /// <summary>The first tile in growing square rings around (cx, cz) that passes <paramref name="fits"/>.</summary>
    private static bool TryFind(int cx, int cz, int radius, Func<int, int, bool> fits, out int fx, out int fz)
    {
        for (var r = 0; r <= radius; r++)
        {
            for (var z = cz - r; z <= cz + r; z++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    if ((Math.Abs(x - cx) == r || Math.Abs(z - cz) == r) && x >= 0 && z >= 0 && fits(x, z))
                    {
                        fx = x;
                        fz = z;
                        return true;
                    }
                }
            }
        }

        fx = fz = -1;
        return false;
    }

    private static bool Near(HashSet<int> used, int width, int x, int z, int distance)
    {
        for (var dz = -distance; dz <= distance; dz++)
        {
            for (var dx = -distance; dx <= distance; dx++)
            {
                if (used.Contains(((z + dz) * width) + x + dx))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
