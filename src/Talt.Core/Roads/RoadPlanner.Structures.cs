using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Roads;

/// <summary>Bridges, tunnels, removal, one-way, motorways and connectors (research R7–R9).</summary>
public sealed partial class RoadPlanner
{
    private readonly TileSet _visited = new(256);
    private int[] _queue = new int[256];

    /// <summary>
    /// The tiles of a straight line drag (contracts/commands.md): the 45° direction closest to b − a and the
    /// number of steps, so that the line ends at the projection of b.
    /// </summary>
    public static (int Direction, int Steps) LineOf((int X, int Z) a, (int X, int Z) b, bool straightOnly)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        int direction;
        if (straightOnly)
        {
            direction = dx == 0 && dz == 0 ? -1
                : Math.Abs(dx) >= Math.Abs(dz) ? (dx > 0 ? Dir8.E : Dir8.W)
                : (dz > 0 ? Dir8.S : Dir8.N);
        }
        else
        {
            direction = Dir8.Octant(dx, dz);
        }

        if (direction < 0)
        {
            return (Dir8.N, 0);
        }

        var steps = Dir8.OffsetX(direction) == 0 ? Math.Abs(dz)
            : Dir8.OffsetZ(direction) == 0 ? Math.Abs(dx)
            : Math.Max(Math.Abs(dx), Math.Abs(dz));
        return (direction, steps);
    }

    // ----- Bridge -----

    /// <summary>Plans a straight bridge between two end tiles (contracts/commands.md <c>buildBridge</c>, research R8).</summary>
    public void PlanBridge(ConstructionState state, int companyId, int type, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type, motorway: false) || !_map.Contains(a.X, a.Z))
        {
            plan.Reason ??= ConstructionReasons.NothingToDo;
            return;
        }

        var (direction, span) = LineOf(a, b, straightOnly: true);
        var start = _map.Index(a.X, a.Z);
        var end = Step(start, direction, span);
        if (span < 2 || end < 0)
        {
            Refuse(start, ConstructionReasons.BridgeEnds);
            return;
        }

        if (!CheckBridge(start, direction, span, _rules.MaxBridgeSpan(_state.Year)))
        {
            return;
        }

        LongLink(start, direction, span, RoadFeatures.BridgeEnd, true, true);
        MarkPathTile(start, type);
        MarkPathTile(end, type);
        plan.Cost += span * _rules.BridgeCostPerTile;
        VerifyView();
        Emit();
    }

    /// <summary>Checks a bridge from <paramref name="start"/>; records the problem and returns false when it cannot be built.</summary>
    private bool CheckBridge(int start, int direction, int span, int maxSpan)
    {
        var end = Step(start, direction, span);
        foreach (var tile in (ReadOnlySpan<int>)[start, end])
        {
            var blocker = EnterBlocker(tile, allowMotorwayEnd: false);
            if (blocker is not null)
            {
                Refuse(tile, blocker);
                return false;
            }
        }

        var h = Height(start);
        if (Height(end) != h)
        {
            Refuse(end, ConstructionReasons.BridgeEnds);
            return false;
        }

        if (span > maxSpan)
        {
            Refuse(end, ConstructionReasons.BridgeTooLong);
            return false;
        }

        var startPiece = Piece(start);
        var endPiece = Piece(end);
        if (startPiece.LongDir != 0 || endPiece.LongDir != 0
            || !ShapeAllows(start, (byte)(startPiece.Links | Dir8.Bit(direction)), direction)
            || !ShapeAllows(end, (byte)(endPiece.Links | Dir8.Bit(Dir8.Opposite(direction))), Dir8.Opposite(direction)))
        {
            Refuse(startPiece.LongDir != 0 ? start : end, ConstructionReasons.NotStraight);
            return false;
        }

        for (var k = 1; k < span; k++)
        {
            var tile = Step(start, direction, k);
            if (IsIndustry(tile))
            {
                Refuse(tile, ConstructionReasons.Industry);
                return false;
            }

            if (Surface(tile) > h || (Piece(tile).Flags & (RoadFeatures.OverBridgeX | RoadFeatures.OverBridgeZ)) != 0)
            {
                Refuse(tile, ConstructionReasons.BridgeBlocked);
                return false;
            }
        }

        return true;
    }

    // ----- Tunnel -----

    /// <summary>
    /// Plans a straight tunnel from portal <paramref name="a"/> (contracts/commands.md <c>buildTunnel</c>,
    /// research R8); <paramref name="direction"/> −1 picks the single higher straight neighbour.
    /// </summary>
    public void PlanTunnel(ConstructionState state, int companyId, int type, (int X, int Z) a, int direction, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type, motorway: false) || !_map.Contains(a.X, a.Z))
        {
            plan.Reason ??= ConstructionReasons.NothingToDo;
            return;
        }

        var portal = _map.Index(a.X, a.Z);
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

        var blocker = EnterBlocker(portal, allowMotorwayEnd: false);
        if (blocker is not null)
        {
            Refuse(portal, blocker);
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
                if ((Piece(tile).Flags & (RoadFeatures.UnderTunnelX | RoadFeatures.UnderTunnelZ)) != 0)
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

        blocker = EnterBlocker(exit, allowMotorwayEnd: false);
        var portalPiece = Piece(portal);
        var exitPiece = Piece(exit);
        if (blocker is not null)
        {
            Refuse(exit, blocker);
            return;
        }

        if (portalPiece.LongDir != 0 || exitPiece.LongDir != 0
            || !ShapeAllows(portal, (byte)(portalPiece.Links | Dir8.Bit(direction)), direction)
            || !ShapeAllows(exit, (byte)(exitPiece.Links | Dir8.Bit(Dir8.Opposite(direction))), Dir8.Opposite(direction)))
        {
            Refuse(portalPiece.LongDir != 0 ? portal : exit, ConstructionReasons.NotStraight);
            return;
        }

        LongLink(portal, direction, length, RoadFeatures.TunnelPortal, true, true);
        MarkPathTile(portal, type);
        MarkPathTile(exit, type);
        plan.Cost += (length - 1) * _rules.TunnelCostPerTile;
        VerifyView();
        Emit();
    }

    // ----- Removal -----

    /// <summary>
    /// Plans removing the road pieces along a straight or 45° line (contracts/commands.md <c>removeRoad</c>,
    /// research R9): the company's pieces are removed, whole bridges and tunnels with them; a town's pieces
    /// only while the town's streets stay connected; others' pieces are skipped.
    /// </summary>
    public void PlanRemoval(ConstructionState state, int companyId, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!_map.Contains(a.X, a.Z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var (direction, steps) = LineOf(a, b, straightOnly: false);
        var start = _map.Index(a.X, a.Z);
        var removed = 0;
        var skippedTown = false;
        var skippedOther = false;
        for (var i = 0; i <= steps; i++)
        {
            var tile = Step(start, direction, i);
            if (tile < 0)
            {
                break;
            }

            var piece = Piece(tile);
            if (!piece.HasRoad)
            {
                continue;
            }

            var owner = OwnerAt(tile);
            if (owner.IsCompany(companyId))
            {
                plan.Cost += _rules.RemoveCost;
                RemovePiece(tile);
                removed++;
            }
            else if (owner.Kind == TileOwnerKind.Town)
            {
                if (TownStaysConnected(tile, owner))
                {
                    plan.Cost += _rules.TownRoadRemoveCost;
                    RemovePiece(tile);
                    removed++;
                }
                else
                {
                    plan.AddProblem(tile, ConstructionReasons.TownNeedsRoad, blocking: false);
                    skippedTown = true;
                }
            }
            else
            {
                plan.AddProblem(tile, ConstructionReasons.OwnedByOther, blocking: false);
                skippedOther = true;
            }
        }

        if (removed == 0)
        {
            plan.Reason = skippedTown ? ConstructionReasons.TownNeedsRoad
                : skippedOther ? ConstructionReasons.OwnedByOther
                : ConstructionReasons.NotARoad;
            return;
        }

        Emit();
    }

    /// <summary>Removes a piece in the view: its links on the neighbours, and the whole bridge or tunnel.</summary>
    private void RemovePiece(int tile)
    {
        var slot = Track(tile);
        var piece = _piece[slot];
        if (!piece.HasRoad)
        {
            return;
        }

        var longDirection = piece.LongLinkDirection;
        for (var d = 0; d < 8; d++)
        {
            if (!piece.HasLink(d))
            {
                continue;
            }

            var other = Follow(tile, d);
            var os = Track(other);
            var op = _piece[os];
            var mask = (byte)~Dir8.Bit(Dir8.Opposite(d));
            op = op with { Links = (byte)(op.Links & mask), Exits = (byte)(op.Exits & mask) };
            if (op.IsConnector && op.LinkCount != 3)
            {
                op = op with { Flags = op.Flags & ~RoadFeatures.Connector };
            }

            _piece[os] = op;
        }

        _piece[slot] = piece.PassingOnly;
        _kind[slot] = TileObjectKind.None;
        _owner[slot] = TileOwner.None;

        if (longDirection >= 0)
        {
            var passing = piece.IsBridgeEnd
                ? (Dir8.OffsetX(longDirection) != 0 ? RoadFeatures.OverBridgeX : RoadFeatures.OverBridgeZ)
                : (Dir8.OffsetX(longDirection) != 0 ? RoadFeatures.UnderTunnelX : RoadFeatures.UnderTunnelZ);
            for (var k = 1; k < piece.Span; k++)
            {
                var s = Track(Step(tile, longDirection, k));
                _piece[s] = _piece[s] with { Flags = _piece[s].Flags & ~passing };
            }

            // The far end goes with it (its long link was just removed above).
            var far = Step(tile, longDirection, piece.Span);
            var fs = Track(far);
            _piece[fs] = _piece[fs] with { Flags = _piece[fs].Flags & ~RoadFeatures.LongLinkEnd, Span = 0, LongDir = 0 };
            RemovePiece(far);
        }
    }

    /// <summary>
    /// True when removing a town piece keeps that town's remaining pieces connected (research R9): all its
    /// linked neighbours of the same town still reach each other through the town's own pieces.
    /// </summary>
    private bool TownStaysConnected(int tile, TileOwner town)
    {
        var piece = Piece(tile);
        Span<int> targets = stackalloc int[8];
        var targetCount = 0;
        for (var d = 0; d < 8; d++)
        {
            if (piece.HasLink(d))
            {
                var n = Follow(tile, d);
                if (OwnerAt(n) == town && targets[..targetCount].IndexOf(n) < 0)
                {
                    targets[targetCount++] = n;
                }
            }
        }

        if (targetCount <= 1)
        {
            return true;
        }

        // Breadth-first search from the first neighbour over the town's pieces, without the removed tile.
        _visited.Clear();
        _visited.Add(tile, out _);
        _visited.Add(targets[0], out _);
        var head = 0;
        var count = 0;
        Enqueue(targets[0], ref count);
        var found = 1;
        while (head < count && found < targetCount)
        {
            var current = _queue[head++];
            var p = Piece(current);
            for (var d = 0; d < 8; d++)
            {
                if (!p.HasLink(d))
                {
                    continue;
                }

                var n = Follow(current, d);
                if (OwnerAt(n) != town)
                {
                    continue;
                }

                _visited.Add(n, out var added);
                if (!added)
                {
                    continue;
                }

                if (targets[..targetCount].IndexOf(n) >= 0)
                {
                    found++;
                }

                Enqueue(n, ref count);
            }
        }

        return found >= targetCount;
    }

    private void Enqueue(int tile, ref int count)
    {
        if (count == _queue.Length)
        {
            Array.Resize(ref _queue, count * 2);
        }

        _queue[count++] = tile;
    }

    // ----- One-way -----

    /// <summary>
    /// Plans switching a straight piece of the company's between two-way, one-way towards its lower link
    /// direction, one-way the other way, and back (contracts/commands.md <c>setOneWay</c>).
    /// </summary>
    public void PlanOneWay(ConstructionState state, int companyId, (int X, int Z) a, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!_map.Contains(a.X, a.Z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var tile = _map.Index(a.X, a.Z);
        var piece = Piece(tile);
        if (!piece.HasRoad)
        {
            Refuse(tile, ConstructionReasons.NotARoad);
            return;
        }

        if (!OwnerAt(tile).IsCompany(companyId))
        {
            Refuse(tile, ConstructionReasons.OwnedByOther);
            return;
        }

        var low = System.Numerics.BitOperations.TrailingZeroCount(piece.Links);
        var high = Dir8.Opposite(low);
        if (piece.LinkCount != 2 || !piece.HasLink(high) || piece.LongDir != 0 || piece.IsConnector || _rules.IsMotorway(piece.TypeIndex))
        {
            Refuse(tile, ConstructionReasons.NotStraight);
            return;
        }

        var lowBit = Dir8.Bit(low);
        var highBit = Dir8.Bit(high);
        var exits = piece.Exits == (lowBit | highBit) ? lowBit
            : piece.Exits == lowBit ? highBit
            : (byte)(lowBit | highBit);
        var slot = Track(tile);
        _piece[slot] = piece with { Exits = exits };
        plan.Cost += _rules.OneWayCost;
        Emit();
    }

    // ----- Motorway -----

    /// <summary>
    /// Plans a motorway along a straight or 45° line (contracts/commands.md <c>buildMotorway</c>, research R7):
    /// carriageway 1 on the line travelling from a to the end, carriageway 2 on its left travelling back,
    /// elevated where the ground cannot carry both (straight lines only), joined to existing carriageway ends.
    /// </summary>
    public void PlanMotorway(ConstructionState state, int companyId, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        var type = _rules.MotorwayType();
        if (type < 0 || !CheckType(type, motorway: true))
        {
            plan.Reason ??= ConstructionReasons.RoadTypeUnavailable;
            return;
        }

        if (!_map.Contains(a.X, a.Z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var (d, steps) = LineOf(a, b, straightOnly: false);
        if (steps < 1)
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var offset = Dir8.IsStraight(d) ? Dir8.Rotate(d, -2) : Dir8.Rotate(d, -3);
        var start1 = _map.Index(a.X, a.Z);
        var start2 = Neighbour(start1, offset);
        var end1 = Step(start1, d, steps);
        var end2 = start2 < 0 ? -1 : Step(start2, d, steps);
        if (start2 < 0 || end1 < 0 || end2 < 0)
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        // Every lane tile must be free ground (or bridged over).
        var back = Dir8.Opposite(d);
        var i = 0;
        while (i < steps)
        {
            var t1 = Step(start1, d, i);
            var t2 = Step(start2, d, i);
            if (i == 0 && !LaneTileFree(t1, t2))
            {
                return;
            }

            var n1 = Step(start1, d, i + 1);
            var n2 = Step(start2, d, i + 1);
            if (GroundStepOk(t1, d) && GroundStepOk(t2, d) && LaneTileFree(n1, n2, record: false))
            {
                Link(t1, d, n1, true, false);
                Link(t2, d, n2, false, true);
                i++;
                continue;
            }

            if (!Dir8.IsStraight(d))
            {
                LaneTileFree(n1, n2);
                if (_plan.Reason is null)
                {
                    Refuse(n1, ConstructionReasons.TooSteep);
                }

                return;
            }

            // Elevated section: both carriageways bridge to the next tiles at their own heights.
            var j = -1;
            for (var k = 2; k <= _rules.ElevatedMaxSpan && i + k <= steps; k++)
            {
                var e1 = Step(start1, d, i + k);
                var e2 = Step(start2, d, i + k);
                if (Height(e1) == Height(t1) && Height(e2) == Height(t2) && !IsWater(e1) && !IsWater(e2)
                    && LaneTileFree(e1, e2, record: false))
                {
                    j = k;
                    break;
                }
            }

            if (j < 0 || !CheckBridge(t1, d, j, _rules.ElevatedMaxSpan) || !CheckBridge(t2, d, j, _rules.ElevatedMaxSpan))
            {
                if (_plan.Reason is null)
                {
                    Refuse(n1, ConstructionReasons.BridgeBlocked);
                }

                return;
            }

            LongLink(t1, d, j, RoadFeatures.BridgeEnd, true, false);
            LongLink(t2, d, j, RoadFeatures.BridgeEnd, false, true);
            _plan.Cost += 2L * j * _rules.ElevatedCostPerTile;
            i += j;
        }

        for (var k = 0; k <= steps; k++)
        {
            MarkPathTile(Step(start1, d, k), type);
            MarkPathTile(Step(start2, d, k), type);
        }

        // Continue existing carriageways at both ends (sources feed sinks).
        JoinCarriageway(start1, back, incoming: true);
        JoinCarriageway(end1, d, incoming: false);
        JoinCarriageway(end2, d, incoming: true);
        JoinCarriageway(start2, back, incoming: false);

        VerifyView();
        Emit();
    }

    private bool GroundStepOk(int tile, int d) => TerrainAllows(tile, d, out _);

    /// <summary>Lane tiles must be empty land; records the reason unless <paramref name="record"/> is false.</summary>
    private bool LaneTileFree(int t1, int t2, bool record = true)
    {
        foreach (var tile in (ReadOnlySpan<int>)[t1, t2])
        {
            if (tile < 0)
            {
                return false;
            }

            string? reason = null;
            if (IsWater(tile))
            {
                reason = ConstructionReasons.Water;
            }
            else if (IsIndustry(tile))
            {
                reason = ConstructionReasons.Industry;
            }
            else if (KindAt(tile) == TileObjectKind.Road && _view.Find(tile) < 0)
            {
                reason = ConstructionReasons.MotorwayJunction;
            }
            else if (KindAt(tile) != TileObjectKind.None && _view.Find(tile) < 0)
            {
                reason = OwnerAt(tile).IsCompany(_companyId) ? ConstructionReasons.ObjectInTheWay : ConstructionReasons.OwnedByOther;
            }

            if (reason is not null)
            {
                if (record)
                {
                    Refuse(tile, reason);
                }

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Joins a new carriageway end to an existing carriageway end next to it: <paramref name="incoming"/>
    /// means traffic arrives into <paramref name="tile"/> (look for an existing end where its carriageway stops).
    /// </summary>
    private void JoinCarriageway(int tile, int towards, bool incoming)
    {
        foreach (var turn in (ReadOnlySpan<int>)[0, -1, 1])
        {
            var d = Dir8.Rotate(towards, turn);
            var other = Neighbour(tile, d);
            if (other < 0 || _view.Find(other) >= 0)
            {
                continue; // Only existing ends untouched by this action.
            }

            var piece = Piece(other);
            if (!_rules.IsMotorway(piece.TypeIndex) || !IsMotorwayEnd(piece) || !OwnerAt(other).IsCompany(_companyId))
            {
                continue;
            }

            var isSink = piece.Exits == 0;
            if (incoming != isSink || !TerrainAllows(tile, d, out _))
            {
                continue;
            }

            Link(tile, d, other, exitAB: !incoming, exitBA: incoming);
            return;
        }
    }

    // ----- Connector -----

    /// <summary>
    /// Plans a motorway exit (<paramref name="exit"/> true) or entry on the outer side of a straight, flat
    /// carriageway tile (contracts/commands.md <c>buildMotorwayConnector</c>, research R7).
    /// </summary>
    public void PlanConnector(ConstructionState state, int companyId, (int X, int Z) a, bool exit, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!_map.Contains(a.X, a.Z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var tile = _map.Index(a.X, a.Z);
        var piece = Piece(tile);
        if (!_rules.IsMotorway(piece.TypeIndex) || piece.LinkCount != 2 || piece.LongDir != 0 || piece.IsConnector
            || Dir8.Count(piece.Exits) != 1 || !OwnerAt(tile).IsCompany(companyId))
        {
            Refuse(tile, piece.HasRoad && !OwnerAt(tile).IsCompany(companyId) ? ConstructionReasons.OwnedByOther : ConstructionReasons.NotACarriageway);
            return;
        }

        var travel = System.Numerics.BitOperations.TrailingZeroCount(piece.Exits);
        var h = Height(tile);
        var ahead = Neighbour(tile, travel);
        var behind = Neighbour(tile, Dir8.Opposite(travel));
        if (!Dir8.IsStraight(travel) || !piece.HasLink(Dir8.Opposite(travel)) || Height(ahead) != h || Height(behind) != h)
        {
            Refuse(tile, ConstructionReasons.NotACarriageway);
            return;
        }

        var outer = Dir8.Rotate(travel, 2);
        var ramp = Neighbour(tile, outer);
        if (ramp < 0)
        {
            Refuse(tile, ConstructionReasons.NotACarriageway);
            return;
        }

        var blocker = EnterBlocker(ramp, allowMotorwayEnd: false);
        if (blocker is not null || Height(ramp) != h)
        {
            Refuse(ramp, blocker ?? ConstructionReasons.TooSteep);
            return;
        }

        var rampType = _rules.BestOrdinaryType(_state.Year);
        Link(tile, outer, ramp, exitAB: exit, exitBA: !exit);
        var slot = Track(tile);
        _piece[slot] = _piece[slot] with { Flags = _piece[slot].Flags | RoadFeatures.Connector };
        if (!Piece(ramp).HasRoad)
        {
            NewRoad(Track(ramp), rampType);
        }

        plan.Cost += _rules.ConnectorCost;
        VerifyView();
        Emit();
    }
}
