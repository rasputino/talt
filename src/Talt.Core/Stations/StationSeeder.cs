using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Stations;

/// <summary>
/// Debug placements (<c>--seed-stations</c>, benchmarks; contracts/cli.md): finds up to n valid bays,
/// truck bays beside industries first and then bus bays beside town roads, and returns them as
/// <see cref="BuildStationCommand"/>s, so they are recorded and replayed like player actions. Deterministic:
/// industries in slot order, then tiles in index order; chosen tiles are kept at least
/// <see cref="Spacing"/> tiles apart so the commands never join or block each other.
/// </summary>
public static class StationSeeder
{
    /// <summary>Smallest distance between two seeded stations (Chebyshev, tiles).</summary>
    public const int Spacing = 3;

    public static IReadOnlyList<BuildStationCommand> Plan(GameSimulation simulation, int count, int companyId = Company.PlayerId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var commands = new List<BuildStationCommand>();
        var state = simulation.Construction;
        if (state?.Stations is not { } stations || count <= 0)
        {
            return commands;
        }

        var map = state.Map;
        var plan = new ConstructionPlan();
        var taken = new HashSet<int>();
        var truckBay = stations.Rules.FindPiece(StationPieceKind.Truck, StationPieceForm.Bay);
        var busBay = stations.Rules.FindPiece(StationPieceKind.Bus, StationPieceForm.Bay);

        bool TryAdd(int type, int x, int z)
        {
            for (var dz = -Spacing; dz <= Spacing; dz++)
            {
                for (var dx = -Spacing; dx <= Spacing; dx++)
                {
                    if (taken.Contains(((x + dx) & 0xFFFF) | ((z + dz) << 16)))
                    {
                        return false;
                    }
                }
            }

            StationPlanner.PlanPiece(state, companyId, type, x, z, plan);
            if (!plan.IsValid)
            {
                return false;
            }

            taken.Add((x & 0xFFFF) | (z << 16));
            commands.Add(new BuildStationCommand(companyId, stations.Rules.Pieces[type].Id, x, z));
            return true;
        }

        var industries = simulation.Industries;
        for (var slot = 0; slot < industries.Count && commands.Count < count; slot++)
        {
            if (!industries.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                continue;
            }

            var placed = false;
            for (var ring = 1; ring <= 3 && !placed; ring++)
            {
                for (var z = minZ - ring; z < maxZ + ring && !placed; z++)
                {
                    for (var x = minX - ring; x < maxX + ring && !placed; x++)
                    {
                        var onRing = x == minX - ring || x == maxX + ring - 1 || z == minZ - ring || z == maxZ + ring - 1;
                        placed = onRing && map.Contains(x, z) && TryAdd(truckBay, x, z);
                    }
                }
            }
        }

        var roads = state.Roads;
        for (var z = 0; z < map.Depth && commands.Count < count; z++)
        {
            for (var x = 0; x < map.Width && commands.Count < count; x++)
            {
                var tile = map.Index(x, z);
                if (roads.HasRoad(tile) || state.Objects.KindAt(tile) != TileObjectKind.None)
                {
                    continue;
                }

                if ((x > 0 && roads.HasRoad(tile - 1)) || (z > 0 && roads.HasRoad(tile - map.Width))
                    || (x < map.Width - 1 && roads.HasRoad(tile + 1)) || (z < map.Depth - 1 && roads.HasRoad(tile + map.Width)))
                {
                    TryAdd(busBay, x, z);
                }
            }
        }

        return commands;
    }
}
