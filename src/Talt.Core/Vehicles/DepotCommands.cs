using System.Text.Json;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>
/// Plans depot construction and removal into a <see cref="ConstructionPlan"/> (FR-001, FR-002): a depot is built like a
/// station bay — a free, flat, dry tile beside a road of the company or a town at the same height, with a one-link road
/// piece into it and the reverse link on the road. Shared by the commands and the client's tool preview.
/// </summary>
public static class DepotPlanner
{
    /// <summary>Plans a depot on (x, z), facing the first suitable road of N, E, S, W.</summary>
    public static void PlanDepot(ConstructionState state, int companyId, VehicleRules rules, int x, int z, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Reset();
        var map = state.Map;
        if (!map.Contains(x, z) || state.Depots is null)
        {
            plan.Reason = state.Depots is null ? ConstructionReasons.NoMap : ConstructionReasons.NothingToDo;
            return;
        }

        plan.Area = new TileRect(x, z, x, z);
        plan.HasArea = true;
        var tile = map.Index(x, z);
        var reason = FreeLand(state, companyId, tile, x, z) ?? TownRule(state, companyId, x, z);
        if (reason is not null)
        {
            plan.AddProblem(tile, reason);
            return;
        }

        var roads = state.Roads;
        var height = map.Heights[tile];
        var sloped = false;
        for (var d = 0; d < 8; d += 2)
        {
            var neighbour = roads.Neighbour(tile, d);
            if (neighbour < 0 || !roads.HasRoad(neighbour) || !CanTakeLink(state, companyId, neighbour))
            {
                continue;
            }

            if (map.Heights[neighbour] != height)
            {
                sloped = true;
                continue;
            }

            var road = roads.PieceAt(neighbour);
            var piece = new RoadPiece(Dir8.Bit(d), Dir8.Bit(d), road.Type, RoadFeatures.None, 0, 0);
            var scenery = map.SceneryData[tile];
            plan.AddChange(new TileChange(tile, height, height, scenery, Scenery.None, TileObjectKind.None, TileObjectKind.Depot,
                TileOwner.None, TileOwner.Company(companyId), RoadPiece.Empty, piece, RoadChanged: true));
            var back = Dir8.Bit(Dir8.Opposite(d));
            var linked = road with { Links = (byte)(road.Links | back), Exits = (byte)(road.Exits | back) };
            var owner = state.Objects.OwnerAt(neighbour);
            plan.AddChange(new TileChange(neighbour, height, height, map.SceneryData[neighbour], map.SceneryData[neighbour],
                TileObjectKind.Road, TileObjectKind.Road, owner, owner, road, linked, RoadChanged: true));
            plan.Cost = rules.DepotCost + (Scenery.Kind(scenery) != 0 ? state.Rules.ClearTreeCost : 0);
            return;
        }

        plan.AddProblem(tile, sloped ? ConstructionReasons.NotFlat : ConstructionReasons.NoRoad);
    }

    /// <summary>The direction from a planned depot tile towards its road (after <see cref="PlanDepot"/> succeeded).</summary>
    public static int DirectionOf(ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ChangeCount == 0 ? -1 : System.Numerics.BitOperations.TrailingZeroCount(plan.Changes[0].NewRoad.Links);
    }

    /// <summary>Plans removing the company's depot on (x, z).</summary>
    public static void PlanRemoval(ConstructionState state, int companyId, VehicleRules rules, int x, int z, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Reset();
        var map = state.Map;
        if (!map.Contains(x, z) || state.Depots is null)
        {
            plan.Reason = state.Depots is null ? ConstructionReasons.NoMap : ConstructionReasons.NothingToDo;
            return;
        }

        plan.Area = new TileRect(x, z, x, z);
        plan.HasArea = true;
        var tile = map.Index(x, z);
        var depots = state.Depots;
        if (!depots.TryGetAt(tile, out var depotId))
        {
            plan.AddProblem(tile, ConstructionReasons.NoDepot);
            return;
        }

        var slot = depots.SlotOf(depotId);
        if (depots.OwnerAt(slot) != companyId)
        {
            plan.AddProblem(tile, ConstructionReasons.OwnedByOther);
            return;
        }

        var roads = state.Roads;
        var height = map.Heights[tile];
        var old = roads.PieceAt(tile);
        plan.AddChange(new TileChange(tile, height, height, map.SceneryData[tile], map.SceneryData[tile], TileObjectKind.Depot, TileObjectKind.None,
            state.Objects.OwnerAt(tile), TileOwner.None, old, RoadPiece.Empty, RoadChanged: true));
        var direction = depots.DirectionAt(slot);
        var neighbour = roads.Neighbour(tile, direction);
        if (neighbour >= 0 && roads.HasRoad(neighbour))
        {
            var back = Dir8.Bit(Dir8.Opposite(direction));
            var road = roads.PieceAt(neighbour);
            var updated = road with { Links = (byte)(road.Links & ~back), Exits = (byte)(road.Exits & ~back) };
            var nh = map.Heights[neighbour];
            plan.AddChange(new TileChange(neighbour, nh, nh, map.SceneryData[neighbour], map.SceneryData[neighbour],
                state.Objects.KindAt(neighbour), state.Objects.KindAt(neighbour), state.Objects.OwnerAt(neighbour), state.Objects.OwnerAt(neighbour),
                road, updated, RoadChanged: true));
        }

        plan.Cost = rules.DepotRemoveCost;
    }

    private static bool CanTakeLink(ConstructionState state, int companyId, int tile)
    {
        var piece = state.Roads.PieceAt(tile);
        if ((piece.Flags & (RoadFeatures.LongLinkEnd | RoadFeatures.Connector)) != 0 || state.RoadRules.IsMotorway(piece.TypeIndex)
            || state.Objects.KindAt(tile) != TileObjectKind.Road || (state.Stations?.HasPiece(tile) ?? false))
        {
            return false;
        }

        var owner = state.Objects.OwnerAt(tile);
        return owner.Kind == TileOwnerKind.Town || owner.IsCompany(companyId);
    }

    private static string? FreeLand(ConstructionState state, int companyId, int tile, int x, int z)
    {
        var map = state.Map;
        if (map.IsWater(tile))
        {
            return ConstructionReasons.Water;
        }

        if (state.IsIndustry(x, z))
        {
            return ConstructionReasons.Industry;
        }

        var kind = state.Objects.KindAt(tile);
        if (kind != TileObjectKind.None)
        {
            return state.Objects.OwnerAt(tile).IsCompany(companyId) ? ConstructionReasons.ObjectInTheWay : ConstructionReasons.OwnedByOther;
        }

        if (state.Roads.HasRoad(tile) || (state.Roads.FlagsAt(tile) & RoadFeatures.Passing) != 0)
        {
            return ConstructionReasons.ObjectInTheWay;
        }

        return map.IsBuildable(x, z) ? null : ConstructionReasons.NotFlat;
    }

    private static string? TownRule(ConstructionState state, int companyId, int x, int z)
    {
        if (state.Towns is not { } towns)
        {
            return null;
        }

        var town = towns.TownZoneAt(x, z);
        return town >= 0 && !towns.MayBuildStation(town, companyId) ? ConstructionReasons.TownRefuses : null;
    }
}

/// <summary>Builds a road depot (contracts/commands.md <c>buildDepot</c>, FR-001).</summary>
public sealed record BuildDepotCommand(int CompanyId, int X, int Z) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildDepot";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        DepotPlanner.PlanDepot(state, CompanyId, VehicleRules.Default, X, Z, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        var tile = state.Map.Index(X, Z);
        var direction = System.Numerics.BitOperations.TrailingZeroCount(state.Roads.LinksAt(tile));
        state.Depots!.Add(CompanyId, tile, direction);
        state.Undo.Clear(); // Depot state is not tile data, so the action cannot be undone (like stations).
    }

    internal static BuildDepotCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32());
}

/// <summary>Removes the company's depot on a tile when no vehicle is inside or on it (contracts/commands.md <c>removeDepot</c>, FR-002).</summary>
public sealed record RemoveDepotCommand(int CompanyId, int X, int Z) : ConstructionCommand(CompanyId)
{
    public const string Type = "removeDepot";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        DepotPlanner.PlanRemoval(state, CompanyId, VehicleRules.Default, X, Z, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        // The plan was refused while a vehicle was inside or on the tile (occupancy check), so the depot is empty.
        state.Depots!.TryGetAt(state.Map.Index(X, Z), out var depotId);
        state.Depots.Remove(depotId);
        state.Undo.Clear();
    }

    internal static RemoveDepotCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32());
}
