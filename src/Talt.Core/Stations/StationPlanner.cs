using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Core.Stations;

/// <summary>
/// Plans station construction into a <see cref="ConstructionPlan"/> (research R2, R3, contracts/commands.md):
/// bays, drive-through stops, warehouses and removals, with the reasons and costs of feature 006. Used by the
/// station commands and by the client's tool previews, so both run exactly the same checks. Changes nothing.
/// </summary>
public static class StationPlanner
{
    /// <summary>Plans a road station piece of type <paramref name="pieceType"/> on (x, z).</summary>
    public static void PlanPiece(ConstructionState state, int companyId, int pieceType, int x, int z, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Reset();
        var stations = state.Stations;
        if (stations is null)
        {
            plan.Reason = ConstructionReasons.NoMap;
            return;
        }

        var map = state.Map;
        if (!map.Contains(x, z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        if ((uint)pieceType >= (uint)stations.Rules.Pieces.Count)
        {
            plan.Reason = ConstructionReasons.UnknownStationType;
            return;
        }

        plan.Area = new TileRect(x, z, x, z);
        plan.HasArea = true;
        var tile = map.Index(x, z);
        var type = stations.Rules.Pieces[pieceType];
        string? reason = stations.HasPiece(tile) ? ConstructionReasons.ObjectInTheWay : null;
        if (reason is null)
        {
            reason = type.Form == StationPieceForm.Platform ? ConstructionReasons.UnknownStationType // Rail stations: buildRailStation.
                : type.Form == StationPieceForm.Bay
                ? PlanBay(state, companyId, type, tile, x, z, plan)
                : PlanDriveThrough(state, companyId, type, tile, plan);
        }

        reason ??= TownRule(state, companyId, x, z);
        reason ??= JoinRule(state, stations, companyId, x, z);
        if (reason is not null)
        {
            plan.AddProblem(tile, reason);
        }
    }

    /// <summary>Plans a warehouse of size <paramref name="warehouseType"/> whose footprint starts at (x, z).</summary>
    public static void PlanWarehouse(ConstructionState state, int companyId, int warehouseType, int x, int z, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Reset();
        var stations = state.Stations;
        if (stations is null)
        {
            plan.Reason = ConstructionReasons.NoMap;
            return;
        }

        if ((uint)warehouseType >= (uint)stations.Rules.Warehouses.Count)
        {
            plan.Reason = ConstructionReasons.UnknownStationType;
            return;
        }

        var map = state.Map;
        var type = stations.Rules.Warehouses[warehouseType];
        var area = new TileRect(x, z, x + type.Width - 1, z + type.Depth - 1);
        if (!area.TryClip(map.Width, map.Depth, out var clipped))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        plan.Area = clipped;
        plan.HasArea = true;
        if (clipped != area)
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var height = map.HeightAt(x, z);
        var touchesRoad = false;
        long cost = type.Cost;
        for (var tz = area.Z0; tz <= area.Z1; tz++)
        {
            for (var tx = area.X0; tx <= area.X1; tx++)
            {
                var tile = map.Index(tx, tz);
                var reason = FreeLand(state, companyId, tile, tx, tz) ?? (map.HeightAt(tx, tz) != height ? ConstructionReasons.NotFlat : null);
                reason ??= TownRule(state, companyId, tx, tz);
                if (reason is not null)
                {
                    plan.AddProblem(tile, reason);
                    continue;
                }

                var scenery = map.SceneryData[tile];
                if (Scenery.Kind(scenery) != 0)
                {
                    cost += state.Rules.ClearTreeCost;
                }

                var h = (byte)map.HeightAt(tx, tz);
                plan.AddChange(new TileChange(tile, h, h, scenery, Scenery.None, TileObjectKind.None, TileObjectKind.Station,
                    TileOwner.None, TileOwner.Company(companyId)));
                for (var d = 0; d < 8 && !touchesRoad; d += 2)
                {
                    var nx = tx + Dir8.OffsetX(d);
                    var nz = tz + Dir8.OffsetZ(d);
                    touchesRoad = map.Contains(nx, nz) && !area.Contains(nx, nz) && state.Roads.HasRoad(map.Index(nx, nz));
                }
            }
        }

        if (plan.Reason is null && !touchesRoad)
        {
            plan.Reason = ConstructionReasons.NoRoad;
        }

        plan.Cost = cost;
    }

    /// <summary>Plans removing the company's station piece on (x, z) (the whole warehouse for a warehouse tile).</summary>
    public static void PlanRemoval(ConstructionState state, int companyId, int x, int z, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Reset();
        var stations = state.Stations;
        var map = state.Map;
        if (stations is null)
        {
            plan.Reason = ConstructionReasons.NoMap;
            return;
        }

        if (!map.Contains(x, z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        plan.Area = new TileRect(x, z, x, z);
        plan.HasArea = true;
        var tile = map.Index(x, z);
        if (!stations.TryGetPieceAt(tile, out var stationId, out var pieceType, out var direction))
        {
            plan.AddProblem(tile, ConstructionReasons.NoStation);
            return;
        }

        var slot = stations.SlotOf(stationId);
        if (stations.OwnerAt(slot) != companyId)
        {
            plan.AddProblem(tile, ConstructionReasons.OwnedByOther);
            return;
        }

        if (pieceType == StationRegistry.WarehousePiece)
        {
            foreach (var t in stations.TilesAt(slot))
            {
                var h = map.Heights[t];
                plan.AddChange(new TileChange(t, h, h, map.SceneryData[t], map.SceneryData[t], TileObjectKind.Station, TileObjectKind.None,
                    state.Objects.OwnerAt(t), TileOwner.None));
            }

            plan.Area = new TileRect(stations.BoundsAt(slot).X0, stations.BoundsAt(slot).Z0, stations.BoundsAt(slot).X1, stations.BoundsAt(slot).Z1);
            plan.Cost = stations.Rules.Warehouses[stations.WarehouseTypeAt(slot)].RemoveCost;
            return;
        }

        var type = stations.Rules.Pieces[pieceType];
        if (type.Form == StationPieceForm.Platform)
        {
            // Feature 015: the platform's track goes with it and the neighbours lose their links.
            state.Planner.Rails.PlanPlatformRemoval(state, companyId, tile, type.RemoveCost, plan);
            return;
        }

        plan.Cost = type.RemoveCost;
        var height = map.Heights[tile];
        if (type.Form == StationPieceForm.DriveThrough)
        {
            // The road stays: an unchanged tile records the action and lets the client redraw the tile.
            AddUnchanged(state, tile, plan);
            return;
        }

        var roads = state.Roads;
        var oldPiece = roads.PieceAt(tile);
        plan.AddChange(new TileChange(tile, height, height, map.SceneryData[tile], map.SceneryData[tile], TileObjectKind.Station, TileObjectKind.None,
            state.Objects.OwnerAt(tile), TileOwner.None, oldPiece, oldPiece.PassingOnly, RoadChanged: true));
        var neighbour = roads.Neighbour(tile, direction);
        if (neighbour >= 0 && roads.HasRoad(neighbour))
        {
            var back = Dir8.Bit(Dir8.Opposite(direction));
            var old = roads.PieceAt(neighbour);
            var updated = old with { Links = (byte)(old.Links & ~back), Exits = (byte)(old.Exits & ~back) };
            var nh = map.Heights[neighbour];
            plan.AddChange(new TileChange(neighbour, nh, nh, map.SceneryData[neighbour], map.SceneryData[neighbour],
                state.Objects.KindAt(neighbour), state.Objects.KindAt(neighbour), state.Objects.OwnerAt(neighbour), state.Objects.OwnerAt(neighbour),
                old, updated, RoadChanged: true));
        }
    }

    /// <summary>
    /// The piece type a tool uses on (x, z) (research R2): the drive-through form on a road tile, the bay
    /// everywhere else.
    /// </summary>
    public static int ChoosePiece(ConstructionState state, StationPieceKind kind, int x, int z)
    {
        ArgumentNullException.ThrowIfNull(state);
        var rules = state.Stations?.Rules ?? StationRules.Default;
        var onRoad = state.Map.Contains(x, z) && state.Roads.HasRoad(state.Map.Index(x, z));
        return rules.FindPiece(kind, onRoad ? StationPieceForm.DriveThrough : StationPieceForm.Bay);
    }

    private static string? PlanBay(ConstructionState state, int companyId, StationPieceType type, int tile, int x, int z, ConstructionPlan plan)
    {
        var map = state.Map;
        var reason = FreeLand(state, companyId, tile, x, z);
        if (reason is not null)
        {
            return reason;
        }

        var roads = state.Roads;
        var height = map.Heights[tile];
        var sloped = false;
        for (var d = 0; d < 8; d += 2)
        {
            var neighbour = roads.Neighbour(tile, d);
            if (neighbour < 0 || !roads.HasRoad(neighbour) || !CanTakeBayLink(state, companyId, neighbour))
            {
                continue;
            }

            if (map.Heights[neighbour] != height)
            {
                sloped = true;
                continue;
            }

            var road = roads.PieceAt(neighbour);
            var back = Dir8.Opposite(d);
            var bayPiece = new RoadPiece(Dir8.Bit(d), Dir8.Bit(d), road.Type, RoadFeatures.None, 0, 0);
            var scenery = map.SceneryData[tile];
            long cost = type.Cost + (Scenery.Kind(scenery) != 0 ? state.Rules.ClearTreeCost : 0);
            plan.AddChange(new TileChange(tile, height, height, scenery, Scenery.None, TileObjectKind.None, TileObjectKind.Station,
                TileOwner.None, TileOwner.Company(companyId), RoadPiece.Empty, bayPiece, RoadChanged: true));
            var linked = road with { Links = (byte)(road.Links | Dir8.Bit(back)), Exits = (byte)(road.Exits | Dir8.Bit(back)) };
            var owner = state.Objects.OwnerAt(neighbour);
            plan.AddChange(new TileChange(neighbour, height, height, map.SceneryData[neighbour], map.SceneryData[neighbour],
                TileObjectKind.Road, TileObjectKind.Road, owner, owner, road, linked, RoadChanged: true));
            plan.Cost = cost;
            return null;
        }

        return sloped ? ConstructionReasons.NotFlat : ConstructionReasons.NoRoad;
    }

    private static string? PlanDriveThrough(ConstructionState state, int companyId, StationPieceType type, int tile, ConstructionPlan plan)
    {
        var roads = state.Roads;
        if (!roads.HasRoad(tile) || state.Objects.KindAt(tile) != TileObjectKind.Road)
        {
            return ConstructionReasons.NotARoad;
        }

        var piece = roads.PieceAt(tile);
        var straight = piece.Links == (Dir8.Bit(Dir8.N) | Dir8.Bit(Dir8.S)) || piece.Links == (Dir8.Bit(Dir8.E) | Dir8.Bit(Dir8.W));
        if (!straight || (piece.Flags & (RoadFeatures.LongLinkEnd | RoadFeatures.Connector)) != 0 || state.RoadRules.IsMotorway(piece.TypeIndex))
        {
            return ConstructionReasons.NotStraightRoad;
        }

        var owner = state.Objects.OwnerAt(tile);
        if (owner.Kind == TileOwnerKind.Town)
        {
            if (state.Towns is { } towns && !towns.MayBuildStation(owner.Index, companyId))
            {
                return ConstructionReasons.TownRefuses;
            }
        }
        else if (!owner.IsCompany(companyId))
        {
            return ConstructionReasons.OwnedByOther;
        }

        AddUnchanged(state, tile, plan);
        plan.Cost = type.Cost;
        return null;
    }

    /// <summary>A road tile a bay may link into: the company's or a town's, not a motorway, bridge/tunnel end, connector or station.</summary>
    private static bool CanTakeBayLink(ConstructionState state, int companyId, int tile)
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

    /// <summary>Why a tile cannot take a bay or warehouse tile: water, rough land, objects, roads, industries; or null.</summary>
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

    /// <summary>Building inside the zone of a town whose rating is below the station threshold is refused (FR-004).</summary>
    private static string? TownRule(ConstructionState state, int companyId, int x, int z)
    {
        if (state.Towns is not { } towns)
        {
            return null;
        }

        var town = towns.TownZoneAt(x, z);
        return town >= 0 && !towns.MayBuildStation(town, companyId) ? ConstructionReasons.TownRefuses : null;
    }

    private static string? JoinRule(ConstructionState state, StationRegistry stations, int companyId, int x, int z)
    {
        var join = stations.JoinTarget(companyId, x, z);
        if (join == -2)
        {
            return ConstructionReasons.AdjacentTwoStations;
        }

        if (join < 0)
        {
            return null;
        }

        var (x0, z0, x1, z1) = stations.BoundsAt(stations.SlotOf(join));
        var spread = Math.Max(Math.Max(x1, x) - Math.Min(x0, x), Math.Max(z1, z) - Math.Min(z0, z)) + 1;
        _ = state;
        return spread > stations.Rules.MaxSpread ? ConstructionReasons.StationTooSpread : null;
    }

    private static void AddUnchanged(ConstructionState state, int tile, ConstructionPlan plan)
    {
        var map = state.Map;
        var h = map.Heights[tile];
        var scenery = map.SceneryData[tile];
        var kind = state.Objects.KindAt(tile);
        var owner = state.Objects.OwnerAt(tile);
        plan.AddChange(new TileChange(tile, h, h, scenery, scenery, kind, kind, owner, owner));
    }
}
