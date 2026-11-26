using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Stations;

namespace Talt.Core.Rails;

/// <summary>Bridges, tunnels, removal, conversion, signals, stations and depots (research R3, R5, R10).</summary>
public sealed partial class RailPlanner
{
    // ----- Bridge -----

    /// <summary>Plans a straight rail bridge between two end tiles (<c>buildRailBridge</c>).</summary>
    public void PlanBridge(ConstructionState state, int companyId, int type, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type) || !_map.Contains(a.X, a.Z))
        {
            plan.Reason ??= ConstructionReasons.NothingToDo;
            return;
        }

        var (direction, span) = RoadPlanner.LineOf(a, b, straightOnly: true);
        var start = _map.Index(a.X, a.Z);
        var end = Step(start, direction, span);
        plan.Area = new TileRect(a.X, a.Z, end < 0 ? a.X : end % Width, end < 0 ? a.Z : end / Width);
        plan.HasArea = true;
        if (span < 2 || end < 0)
        {
            Refuse(start, ConstructionReasons.BridgeEnds);
            return;
        }

        if (!CheckEnds(start, end, direction) || Height(end) != Height(start))
        {
            Refuse(end, plan.Reason is null ? ConstructionReasons.BridgeEnds : plan.Reason);
            return;
        }

        if (span > _rules.BridgeMaxSpan)
        {
            Refuse(end, ConstructionReasons.BridgeTooLong);
            return;
        }

        var h = Height(start);
        for (var k = 1; k < span; k++)
        {
            var tile = Step(start, direction, k);
            if (IsIndustry(tile))
            {
                Refuse(tile, ConstructionReasons.Industry);
                return;
            }

            const RailFeatures railBridges = RailFeatures.OverBridgeX | RailFeatures.OverBridgeZ;
            const RoadFeatures roadBridges = RoadFeatures.OverBridgeX | RoadFeatures.OverBridgeZ;
            if (Surface(tile) > h || (Piece(tile).Flags & railBridges) != 0 || (_roads.FlagsAt(tile) & roadBridges) != 0)
            {
                Refuse(tile, ConstructionReasons.BridgeBlocked);
                return;
            }
        }

        NewTrack(Track(start), type);
        NewTrack(Track(end), type);
        LongLink(start, direction, span, RailFeatures.BridgeEnd);
        plan.Cost += span * _rules.BridgeCostPerTile;
        VerifyView();
        Emit();
    }

    /// <summary>Both ends of a bridge or tunnel may take a straight long link: free or plain own track, not a ramp.</summary>
    private bool CheckEnds(int start, int end, int direction)
    {
        foreach (var (tile, d) in (ReadOnlySpan<(int, int)>)[(start, direction), (end, Dir8.Opposite(direction))])
        {
            var kind = KindAt(tile);
            if (kind is not (TileObjectKind.None or TileObjectKind.Rail))
            {
                Refuse(tile, ConstructionReasons.ObjectInTheWay);
                return false;
            }

            var blocker = EnterBlocker(tile, Dir8.Opposite(d), false);
            if (blocker is not null)
            {
                Refuse(tile, blocker);
                return false;
            }

            var piece = Piece(tile);
            if (piece.LongDir != 0 || piece.Signal != 0 || !ShapeAllows(tile, (byte)(piece.Links | Dir8.Bit(d)), d))
            {
                Refuse(tile, ConstructionReasons.NotStraight);
                return false;
            }
        }

        return true;
    }

    // ----- Tunnel -----

    /// <summary>Plans a straight rail tunnel from a portal tile (<c>buildRailTunnel</c>); −1 picks the single higher straight neighbour.</summary>
    public void PlanTunnel(ConstructionState state, int companyId, int type, (int X, int Z) a, int direction, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type) || !_map.Contains(a.X, a.Z))
        {
            plan.Reason ??= ConstructionReasons.NothingToDo;
            return;
        }

        var portal = _map.Index(a.X, a.Z);
        plan.Area = new TileRect(a.X, a.Z, a.X, a.Z);
        plan.HasArea = true;
        var h = Height(portal);
        if (direction < 0)
        {
            for (var d = 0; d < 8 && direction < 0; d += 2)
            {
                var n = Neighbour(portal, d);
                if (n >= 0 && Height(n) > h)
                {
                    direction = d;
                }
            }
        }

        if (direction < 0 || !Dir8.IsStraight(direction))
        {
            Refuse(portal, ConstructionReasons.TunnelNoExit);
            return;
        }

        var first = Neighbour(portal, direction);
        if (first < 0 || Height(first) <= h)
        {
            Refuse(portal, ConstructionReasons.TunnelNoExit);
            return;
        }

        var exit = -1;
        var length = 0;
        for (var k = 1; ; k++)
        {
            var tile = Step(portal, direction, k);
            if (tile < 0)
            {
                Refuse(portal, ConstructionReasons.TunnelNoExit);
                return;
            }

            if (k > _rules.TunnelMaxLength)
            {
                Refuse(portal, ConstructionReasons.TunnelTooLong);
                return;
            }

            var height = Height(tile);
            if (height > h)
            {
                if ((Piece(tile).Flags & (RailFeatures.UnderTunnelX | RailFeatures.UnderTunnelZ)) != 0
                    || (_roads.FlagsAt(tile) & (RoadFeatures.UnderTunnelX | RoadFeatures.UnderTunnelZ)) != 0)
                {
                    Refuse(tile, ConstructionReasons.TunnelBlocked);
                    return;
                }

                continue;
            }

            if (height < h || IsWater(tile))
            {
                Refuse(tile, ConstructionReasons.TunnelNoExit);
                return;
            }

            exit = tile;
            length = k;
            break;
        }

        plan.Area = new TileRect(a.X, a.Z, exit % Width, exit / Width);
        if (!CheckEnds(portal, exit, direction))
        {
            return;
        }

        NewTrack(Track(portal), type);
        NewTrack(Track(exit), type);
        LongLink(portal, direction, length, RailFeatures.TunnelPortal);
        plan.Cost += (length - 1) * _rules.TunnelCostPerTile;
        VerifyView();
        Emit();
    }

    // ----- Removal and conversion -----

    /// <summary>
    /// Plans removing the company's track along a straight or 45° line (<c>removeRail</c>): whole pieces with their
    /// signals, bridges and tunnels with both ends; neighbours lose their links to removed tiles. Platforms are removed with
    /// the station tool and depots with the depot tool.
    /// </summary>
    public void PlanRemoval(ConstructionState state, int companyId, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!_map.Contains(a.X, a.Z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var (direction, steps) = RoadPlanner.LineOf(a, b, straightOnly: false);
        var start = _map.Index(a.X, a.Z);
        var last = start;
        for (var k = 0; k <= steps; k++)
        {
            var tile = Step(start, direction, k);
            if (tile < 0)
            {
                break;
            }

            last = tile;
            var piece = Piece(tile);
            if (!piece.HasRail)
            {
                continue;
            }

            var kind = KindAt(tile);
            if (kind == TileObjectKind.Station)
            {
                Refuse(tile, ConstructionReasons.RailStationTrack);
                continue;
            }

            if (kind == TileObjectKind.RailDepot)
            {
                Refuse(tile, ConstructionReasons.UseDepotTool);
                continue;
            }

            if (kind == TileObjectKind.Rail && !OwnerAt(tile).IsCompany(_companyId))
            {
                Refuse(tile, ConstructionReasons.OwnedByOther);
                continue;
            }

            RemovePiece(tile);
            if (piece.LongDir != 0)
            {
                var far = _rails.Follow(tile, piece.LongLinkDirection);
                var d = piece.LongLinkDirection;
                for (var i = 1; i < piece.Span; i++)
                {
                    var slot = Track(Step(tile, d, i));
                    _piece[slot] = _piece[slot] with { Flags = _piece[slot].Flags & ~(piece.IsBridgeEnd
                        ? RailFeatures.OverBridgeX | RailFeatures.OverBridgeZ : RailFeatures.UnderTunnelX | RailFeatures.UnderTunnelZ) };
                }

                if (Piece(far).HasRail)
                {
                    RemovePiece(far);
                }
            }
        }

        plan.Area = new TileRect(a.X, a.Z, last % Width, last / Width);
        plan.HasArea = true;
        Emit();
    }

    private void RemovePiece(int tile)
    {
        var slot = Track(tile);
        var piece = _piece[slot];
        if (!piece.HasRail)
        {
            return;
        }

        _plan.Cost += _rules.Types[piece.TypeIndex].RemoveCost + (piece.Signal != 0 ? _rules.SignalRemoveCost : 0);
        for (var d = 0; d < 8; d++)
        {
            if (!piece.HasLink(d))
            {
                continue;
            }

            var other = piece.LongLinkDirection == d ? Step(tile, d, piece.Span) : Neighbour(tile, d);
            if (other < 0)
            {
                continue;
            }

            var os = Track(other);
            var bit = Dir8.Bit(Dir8.Opposite(d));
            var op = _piece[os];
            if (op.LongLinkDirection == Dir8.Opposite(d))
            {
                continue; // The far end of a long link is removed too.
            }

            var links = (byte)(op.Links & ~bit);
            // A neighbour losing a link keeps its signal only while its links stay one straight pair.
            var signal = RailSignal.Axis(links) >= 0 ? op.Signal : (byte)0;
            _piece[os] = op with { Links = links, Signal = signal };
        }

        _piece[slot] = piece.PassingOnly;
        if (_kind[slot] == TileObjectKind.Rail)
        {
            _kind[slot] = TileObjectKind.None;
            _owner[slot] = TileOwner.None;
        }
    }

    /// <summary>Plans changing the company's track on a straight or 45° line to another type (<c>convertRail</c>, FR-006).</summary>
    public void PlanConversion(ConstructionState state, int companyId, int type, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type) || !_map.Contains(a.X, a.Z))
        {
            plan.Reason ??= ConstructionReasons.NothingToDo;
            return;
        }

        var (direction, steps) = RoadPlanner.LineOf(a, b, straightOnly: false);
        var start = _map.Index(a.X, a.Z);
        var last = start;
        for (var k = 0; k <= steps; k++)
        {
            var tile = Step(start, direction, k);
            if (tile < 0)
            {
                break;
            }

            last = tile;
            var piece = Piece(tile);
            if (!piece.HasRail || piece.TypeIndex == type)
            {
                continue;
            }

            if (KindAt(tile) == TileObjectKind.Rail && !OwnerAt(tile).IsCompany(_companyId))
            {
                Refuse(tile, ConstructionReasons.OwnedByOther);
                continue;
            }

            Convert(tile, type);
            if (piece.LongDir != 0)
            {
                Convert(_rails.Follow(tile, piece.LongLinkDirection), type);
            }
        }

        plan.Area = new TileRect(a.X, a.Z, last % Width, last / Width);
        plan.HasArea = true;
        Emit();
    }

    private void Convert(int tile, int type)
    {
        var slot = Track(tile);
        var piece = _piece[slot];
        if (piece.TypeIndex == type)
        {
            return;
        }

        _plan.Cost += Math.Max(0, _rules.Types[type].BuildCost - _rules.Types[piece.TypeIndex].BuildCost) + _rules.ConvertCostPerTile;
        _piece[slot] = piece with { Type = (byte)(type + 1) };
    }

    // ----- Signals -----

    /// <summary>
    /// Plans placing, changing or removing (kind <see cref="SignalKind.None"/>) a signal on the company's plain track
    /// (<c>setSignal</c>, FR-014): straight or diagonal tiles with exactly one opposite pair of links.
    /// </summary>
    public void PlanSignal(ConstructionState state, int companyId, int x, int z, SignalKind kind, int faces, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!_map.Contains(x, z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var tile = _map.Index(x, z);
        plan.Area = new TileRect(x, z, x, z);
        plan.HasArea = true;
        var piece = Piece(tile);
        if (!piece.HasRail)
        {
            Refuse(tile, ConstructionReasons.NoRail);
            return;
        }

        if (KindAt(tile) != TileObjectKind.Rail || !OwnerAt(tile).IsCompany(_companyId))
        {
            Refuse(tile, KindAt(tile) == TileObjectKind.Rail ? ConstructionReasons.OwnedByOther : ConstructionReasons.SignalPlace);
            return;
        }

        if (RailSignal.Axis(piece.Links) < 0 || piece.LongDir != 0)
        {
            Refuse(tile, ConstructionReasons.SignalPlace);
            return;
        }

        if ((uint)kind > (uint)SignalKind.OneWayPath || (kind != SignalKind.None && (faces & 3) == 0))
        {
            Refuse(tile, ConstructionReasons.InvalidValue);
            return;
        }

        var signal = RailSignal.Make(kind, faces);
        if (kind == SignalKind.OneWayPath && (faces & 3) == 3)
        {
            Refuse(tile, ConstructionReasons.InvalidValue); // A one-way signal faces one way.
            return;
        }

        var slot = Track(tile);
        _plan.Cost += signal == 0 ? (piece.Signal != 0 ? _rules.SignalRemoveCost : 0) : _rules.SignalCost(kind);
        _piece[slot] = piece with { Signal = signal };
        Emit();
    }

    // ----- Stations -----

    /// <summary>
    /// Plans a rail station of <paramref name="platforms"/> × <paramref name="length"/> tiles whose minimum corner is (x, z)
    /// (<c>buildRailStation</c>, FR-007): axis 0 runs the platforms along X, 1 along Z. Tiles must be flat at one height and
    /// free, or the company's plain track along the axis; track entering the ends joins the platforms.
    /// </summary>
    public void PlanStation(ConstructionState state, int companyId, int pieceType, int x, int z, int axis, int platforms, int length, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        var stations = state.Stations;
        if (stations is null)
        {
            plan.Reason = ConstructionReasons.NoMap;
            return;
        }

        if ((uint)pieceType >= (uint)stations.Rules.Pieces.Count || stations.Rules.Pieces[pieceType].Kind != StationPieceKind.Rail
            || axis is not (0 or 1) || platforms < 1 || platforms > _rules.MaxPlatforms || length < 1 || length > _rules.MaxPlatformLength)
        {
            plan.Reason = ConstructionReasons.InvalidValue;
            return;
        }

        var along = axis == 0 ? Dir8.E : Dir8.S;
        var back = Dir8.Opposite(along);
        var x1 = axis == 0 ? x + length - 1 : x + platforms - 1;
        var z1 = axis == 0 ? z + platforms - 1 : z + length - 1;
        var area = new TileRect(x, z, x1, z1);
        if (!area.TryClip(Width, _map.Depth, out var clipped) || clipped != area)
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        plan.Area = area;
        plan.HasArea = true;
        var type = stations.Rules.Pieces[pieceType];
        var height = Height(_map.Index(x, z));
        var join = -1;
        for (var tz = z; tz <= z1; tz++)
        {
            for (var tx = x; tx <= x1; tx++)
            {
                var tile = _map.Index(tx, tz);
                var reason = StationTileBlocker(tile, tx, tz, along, height);
                if (reason is null && state.Towns is { } towns)
                {
                    var town = towns.TownZoneAt(tx, tz);
                    reason = town >= 0 && !towns.MayBuildStation(town, companyId) ? ConstructionReasons.TownRefuses : null;
                }

                var target = stations.JoinTarget(companyId, tx, tz);
                if (reason is null && (target == -2 || (target >= 0 && join >= 0 && target != join)))
                {
                    reason = ConstructionReasons.AdjacentTwoStations;
                }

                if (reason is not null)
                {
                    plan.AddProblem(tile, reason);
                    continue;
                }

                if (target >= 0)
                {
                    join = target;
                }
            }
        }

        if (plan.Reason is not null)
        {
            return;
        }

        if (join >= 0)
        {
            var (bx0, bz0, bx1, bz1) = stations.BoundsAt(stations.SlotOf(join));
            var spread = Math.Max(Math.Max(bx1, x1) - Math.Min(bx0, x), Math.Max(bz1, z1) - Math.Min(bz0, z)) + 1;
            if (spread > stations.Rules.MaxSpread)
            {
                plan.Reason = ConstructionReasons.StationTooSpread;
                return;
            }
        }
        else if (Math.Max(x1 - x, z1 - z) + 1 > stations.Rules.MaxSpread)
        {
            plan.Reason = ConstructionReasons.StationTooSpread;
            return;
        }

        var trackType = 0;
        for (var tz = z; tz <= z1; tz++)
        {
            for (var tx = x; tx <= x1; tx++)
            {
                var tile = _map.Index(tx, tz);
                var slot = Track(tile);
                if (!_piece[slot].HasRail)
                {
                    NewTrack(slot, trackType);
                }

                _kind[slot] = TileObjectKind.Station;
                _owner[slot] = TileOwner.Company(companyId);
                plan.Cost += type.Cost;
                var first = axis == 0 ? tx == x : tz == z;
                var lastTile = axis == 0 ? tx == x1 : tz == z1;
                if (!lastTile)
                {
                    Link(tile, along, Neighbour(tile, along));
                }

                // The ends join track that points at them.
                foreach (var (end, d) in (ReadOnlySpan<(bool, int)>)[(first, back), (lastTile, along)])
                {
                    var outside = end ? Neighbour(tile, d) : -1;
                    if (outside >= 0 && KindAt(outside) == TileObjectKind.Rail && OwnerAt(outside).IsCompany(companyId)
                        && Piece(outside).HasRail && Height(outside) == height
                        && RailGeometry.IsValidShape((byte)(Piece(outside).Links | Dir8.Bit(Dir8.Opposite(d)))) && Piece(outside).Signal == 0)
                    {
                        Link(tile, d, outside);
                    }
                }
            }
        }

        VerifyView();
        Emit();
    }

    private string? StationTileBlocker(int tile, int x, int z, int along, int height)
    {
        if (IsWater(tile))
        {
            return ConstructionReasons.Water;
        }

        if (IsIndustry(tile))
        {
            return ConstructionReasons.Industry;
        }

        if (Height(tile) != height || !_map.IsBuildable(x, z))
        {
            return ConstructionReasons.NotFlat;
        }

        if ((_roads.FlagsAt(tile) & RoadFeatures.Passing) != 0 || (_rails.FlagsAt(tile) & RailFeatures.Passing) != 0)
        {
            return ConstructionReasons.ObjectInTheWay;
        }

        var kind = KindAt(tile);
        if (kind == TileObjectKind.None)
        {
            return _roads.HasRoad(tile) ? ConstructionReasons.ObjectInTheWay : null;
        }

        if (kind != TileObjectKind.Rail)
        {
            return OwnerAt(tile).IsCompany(_companyId) ? ConstructionReasons.ObjectInTheWay : ConstructionReasons.OwnedByOther;
        }

        if (!OwnerAt(tile).IsCompany(_companyId))
        {
            return ConstructionReasons.OwnedByOther;
        }

        var piece = Piece(tile);
        var pair = (byte)(Dir8.Bit(along) | Dir8.Bit(Dir8.Opposite(along)));
        return (piece.Links & ~pair) != 0 || piece.Signal != 0 || piece.LongDir != 0 ? ConstructionReasons.NotStraight : null;
    }

    // ----- Depots -----

    /// <summary>Plans a rail depot on (x, z) with its entrance towards <paramref name="direction"/> (<c>buildRailDepot</c>, FR-008).</summary>
    public void PlanDepot(ConstructionState state, int companyId, int x, int z, int direction, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!_map.Contains(x, z) || direction < 0 || direction > 7 || !Dir8.IsStraight(direction))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var tile = _map.Index(x, z);
        plan.Area = new TileRect(x, z, x, z);
        plan.HasArea = true;
        var height = Height(tile);
        var reason = KindAt(tile) != TileObjectKind.None || _roads.HasRoad(tile) || _rails.HasRail(tile) ? ConstructionReasons.ObjectInTheWay
            : IsWater(tile) ? ConstructionReasons.Water
            : IsIndustry(tile) ? ConstructionReasons.Industry
            : !_map.IsBuildable(x, z) ? ConstructionReasons.NotFlat
            : (_roads.FlagsAt(tile) & RoadFeatures.Passing) != 0 || (_rails.FlagsAt(tile) & RailFeatures.Passing) != 0 ? ConstructionReasons.ObjectInTheWay
            : null;
        if (reason is not null)
        {
            Refuse(tile, reason);
            return;
        }

        var slot = Track(tile);
        NewTrack(slot, 0);
        plan.Cost -= _rules.Types[0].BuildCost; // The depot's price includes its track.
        _kind[slot] = TileObjectKind.RailDepot;
        _owner[slot] = TileOwner.Company(companyId);
        plan.Cost += _rules.DepotCost;
        var front = Neighbour(tile, direction);
        if (front >= 0 && Piece(front).HasRail && Height(front) == height && Piece(front).Signal == 0 && Piece(front).LongDir == 0
            && (KindAt(front) == TileObjectKind.Rail && OwnerAt(front).IsCompany(companyId))
            && RailGeometry.IsValidShape((byte)(Piece(front).Links | Dir8.Bit(Dir8.Opposite(direction)))))
        {
            Link(tile, direction, front);
        }
        else
        {
            // Not joined yet: the depot keeps its entrance direction as its only link once track arrives (the track tool
            // joins it). Until then a lone link marks the direction.
            _piece[slot] = _piece[slot] with { Links = 0 };
        }

        VerifyView();
        Emit();
    }

    /// <summary>Plans removing a rail depot tile (<c>removeRailDepot</c>); its track goes and the front loses its link.</summary>
    public void PlanDepotRemoval(ConstructionState state, int companyId, int tile, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if ((uint)tile >= (uint)(Width * _map.Depth) || KindAt(tile) != TileObjectKind.RailDepot)
        {
            plan.Reason = ConstructionReasons.NoDepot;
            return;
        }

        plan.Area = new TileRect(tile % Width, tile / Width, tile % Width, tile / Width);
        plan.HasArea = true;
        if (!OwnerAt(tile).IsCompany(companyId))
        {
            Refuse(tile, ConstructionReasons.OwnedByOther);
            return;
        }

        var slot = Track(tile);
        RemovePiece(tile);
        plan.Cost = _rules.DepotRemoveCost;
        _kind[slot] = TileObjectKind.None;
        _owner[slot] = TileOwner.None;
        Emit();
    }

    /// <summary>Plans removing a rail platform tile: its track goes and the neighbours lose their links to it.</summary>
    public void PlanPlatformRemoval(ConstructionState state, int companyId, int tile, long removeCost, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        plan.Area = new TileRect(tile % Width, tile / Width, tile % Width, tile / Width);
        plan.HasArea = true;
        var slot = Track(tile);
        RemovePiece(tile);
        plan.Cost = removeCost;
        _kind[slot] = TileObjectKind.None;
        _owner[slot] = TileOwner.None;
        Emit();
    }
}
