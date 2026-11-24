using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Roads;

/// <summary>The road tool: path proposal and building (research R4).</summary>
public sealed partial class RoadPlanner
{
    private readonly TileSet _search = new(4096);
    private long[] _g = new long[4096 * StatesPerTile];
    private int[] _from = new int[4096 * StatesPerTile];
    private int[] _stampOf = new int[4096 * StatesPerTile];
    private int _stamp;
    private long[] _heapF = new long[1024];
    private long[] _heapG = new long[1024];
    private int[] _heapS = new int[1024];
    private int _heapCount;
    private int[] _pathTiles = new int[256];
    private byte[] _pathArrival = new byte[256];
    private int _pathCount;

    /// <summary>States expanded by the last path search (benchmarks).</summary>
    public int LastExpandedStates { get; private set; }

    /// <summary>
    /// Plans a road of type <paramref name="type"/> from tile <paramref name="a"/> to tile <paramref name="b"/>
    /// (contracts/commands.md <c>buildRoad</c>): the proposed path, new pieces, joins, upgrades of the
    /// company's worse roads on the path, cleared trees and short bridges over water.
    /// </summary>
    public void PlanRoad(ConstructionState state, int companyId, int type, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type, motorway: false))
        {
            return;
        }

        if (!_map.Contains(a.X, a.Z) || !_map.Contains(b.X, b.Z))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return;
        }

        var start = _map.Index(a.X, a.Z);
        var goal = _map.Index(b.X, b.Z);
        var startBlocker = EnterBlocker(start, allowMotorwayEnd: start != goal);
        var goalBlocker = start == goal ? null : EnterBlocker(goal, allowMotorwayEnd: true);
        if (startBlocker is not null || goalBlocker is not null)
        {
            if (startBlocker is not null)
            {
                Refuse(start, startBlocker);
            }

            if (goalBlocker is not null)
            {
                Refuse(goal, goalBlocker);
            }

            return;
        }

        if (start == goal)
        {
            // A single piece: a new isolated piece, or an upgrade.
            MarkPathTile(start, type);
            Emit();
            return;
        }

        if (!FindPath(start, goal))
        {
            ExplainFailure(start, goal);
            return;
        }

        if (_pathCount > _rules.MaxPathTiles)
        {
            plan.Reason = ConstructionReasons.TooLarge;
            return;
        }

        Materialize(type);
        VerifyView();
        Emit();
    }

    /// <summary>
    /// When no path exists, names the first obstacle on the straight line between the ends (too steep,
    /// water, an industry, a motorway...), so the player knows what to fix; "no path" otherwise.
    /// </summary>
    private void ExplainFailure(int start, int goal)
    {
        var width = Width;
        var tile = start;
        var guard = width + _map.Depth;
        while (tile != goal && guard-- > 0)
        {
            var d = Dir8.Octant((goal % width) - (tile % width), (goal / width) - (tile / width));
            var next = Neighbour(tile, d);
            var blocker = EnterBlocker(next, allowMotorwayEnd: next == goal);
            if (blocker is not null)
            {
                Refuse(next, blocker);
                Refuse(goal, ConstructionReasons.NoPath);
                return;
            }

            if (!TerrainAllows(tile, d, out _))
            {
                Refuse(next, ConstructionReasons.TooSteep);
                Refuse(goal, ConstructionReasons.NoPath);
                return;
            }

            tile = next;
        }

        Refuse(start, ConstructionReasons.NoPath);
        Refuse(goal, ConstructionReasons.NoPath);
    }

    private bool CheckType(int type, bool motorway)
    {
        if ((uint)type >= (uint)_rules.Types.Count)
        {
            _plan.Reason = ConstructionReasons.UnknownRoadType;
            return false;
        }

        if (_rules.Types[type].Motorway != motorway || !_rules.IsAvailable(type, _state.Year))
        {
            _plan.Reason = ConstructionReasons.RoadTypeUnavailable;
            return false;
        }

        return true;
    }

    private void Materialize(int type)
    {
        for (var i = 1; i < _pathCount; i++)
        {
            var from = _pathTiles[i - 1];
            var to = _pathTiles[i];
            var arrival = _pathArrival[i];
            var direction = arrival & 7;
            var piece = Piece(from);
            if (arrival >= 8)
            {
                if (piece.LongLinkDirection != direction)
                {
                    var span = Distance(from, to);
                    LongLink(from, direction, span, RoadFeatures.BridgeEnd, true, true);
                    _plan.Cost += span * _rules.BridgeCostPerTile;
                }

                continue;
            }

            if (piece.HasLink(direction))
            {
                continue; // Following an existing road.
            }

            var exitForward = true;
            var exitBack = true;
            var toPiece = Piece(to);
            var fromPiece = piece;
            if (_rules.IsMotorway(toPiece.TypeIndex))
            {
                // Joining a carriageway's end: traffic flows the carriageway's way (research R7).
                exitForward = toPiece.Exits != 0;
                exitBack = !exitForward;
            }
            else if (_rules.IsMotorway(fromPiece.TypeIndex))
            {
                exitForward = fromPiece.Exits == 0;
                exitBack = !exitForward;
            }

            Link(from, direction, to, exitForward, exitBack);
        }

        for (var i = 0; i < _pathCount; i++)
        {
            MarkPathTile(_pathTiles[i], type);
        }

        // Ramps cost extra (new pieces only).
        for (var i = 0; i < _pathCount; i++)
        {
            var tile = _pathTiles[i];
            if (_roads.HasRoad(tile))
            {
                continue;
            }

            var piece = Piece(tile);
            for (var d = 0; d < 8; d += 2)
            {
                var n = Neighbour(tile, d);
                if (piece.HasLink(d) && piece.LongLinkDirection != d && n >= 0 && Height(n) == Height(tile) + 1)
                {
                    _plan.Cost += _rules.SlopeCost;
                    break;
                }
            }
        }
    }

    /// <summary>A path tile gets a new piece, or an upgrade when it is the company's and of a worse type.</summary>
    private void MarkPathTile(int tile, int type)
    {
        var slot = Track(tile);
        var piece = _piece[slot];
        if (!piece.HasRoad)
        {
            NewRoad(slot, type);
            return;
        }

        var current = piece.TypeIndex;
        if (_owner[slot].IsCompany(_companyId) && !_rules.IsMotorway(current) && _rules.IsBetter(type, current))
        {
            _piece[slot] = piece with { Type = (byte)(type + 1) };
            _plan.Cost += Math.Max(0, _rules.Types[type].BuildCost - _rules.Types[current].BuildCost);
        }
    }

    private int Distance(int a, int b) => Math.Max(Math.Abs((a % Width) - (b % Width)), Math.Abs((a / Width) - (b / Width)));

    // ----- Search -----

    /// <summary>
    /// Weighted A* over (tile, arrival) states (research R4). Arrival 0–7: by a short step in that
    /// direction; 8–15: over a bridge or tunnel in direction − 8; 16: the start.
    /// </summary>
    private bool FindPath(int start, int goal)
    {
        _search.Clear();
        _heapCount = 0;
        _pathCount = 0;
        LastExpandedStates = 0;
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_stampOf);
            _stamp = 1;
        }

        var margin = _rules.SearchMargin;
        var width = Width;
        var minX = Math.Max(0, Math.Min(start % width, goal % width) - margin);
        var maxX = Math.Min(width - 1, Math.Max(start % width, goal % width) + margin);
        var minZ = Math.Max(0, Math.Min(start / width, goal / width) - margin);
        var maxZ = Math.Min(_map.Depth - 1, Math.Max(start / width, goal / width) + margin);
        var goalX = goal % width;
        var goalZ = goal / width;
        var maxSpan = _rules.MaxBridgeSpan(_state.Year);

        var startState = (SearchSlot(start) * StatesPerTile) + StartState;
        Reach(startState, 0, -1);
        Push(Heuristic(start, goalX, goalZ), 0, startState);
        while (_heapCount > 0)
        {
            var (f, state) = Pop();
            var slot = state / StatesPerTile;
            var arrival = state % StatesPerTile;
            var g = _g[state];
            if (f > g + Heuristic(_search[slot], goalX, goalZ))
            {
                continue;
            }

            var tile = _search[slot];
            if (tile == goal)
            {
                Reconstruct(state);
                return HasNoRepeats();
            }

            if (++LastExpandedStates > _rules.MaxSearchNodes)
            {
                return false;
            }

            var here = Piece(tile);
            var arrivalDir = arrival & 7;
            var baseLinks = arrival < StartState ? (byte)(here.Links | Dir8.Bit(Dir8.Opposite(arrivalDir))) : here.Links;
            var longHere = arrival is >= 8 and < StartState ? Dir8.Opposite(arrivalDir) : here.LongLinkDirection;
            for (var d = 0; d < 8; d++)
            {
                if (arrival < StartState && (d == Dir8.Opposite(arrivalDir) || TurnSteps(arrivalDir, d) > 2))
                {
                    continue;
                }

                var turn = arrival < StartState ? TurnSteps(arrivalDir, d) : 0;
                var turnCost = turn == 0 ? 0 : turn == 1 ? Turn45 : Turn90;
                var links = (byte)(baseLinks | Dir8.Bit(d));

                if (here.LongLinkDirection == d)
                {
                    // Follow an existing bridge or tunnel.
                    var far = Step(tile, d, here.Span);
                    if (far >= 0 && InBox(far, minX, maxX, minZ, maxZ) && ShapeAllows(tile, links, d))
                    {
                        Relax(state, far, 8 + d, g + (ReuseStraight * here.Span) + turnCost, goalX, goalZ);
                    }

                    continue;
                }

                if (!ShapeAllows(tile, links, longHere))
                {
                    continue;
                }

                if (TerrainAllows(tile, d, out var next))
                {
                    if (!InBox(next, minX, maxX, minZ, maxZ) || EnterBlocker(next, allowMotorwayEnd: next == goal) is not null)
                    {
                        continue;
                    }

                    var nextPiece = Piece(next);
                    if (!ShapeAllows(next, (byte)(nextPiece.Links | Dir8.Bit(Dir8.Opposite(d))), nextPiece.LongLinkDirection))
                    {
                        continue;
                    }

                    var straight = Dir8.IsStraight(d);
                    long cost = here.HasLink(d)
                        ? (straight ? ReuseStraight : ReuseDiagonal)
                        : (straight ? StraightStep : DiagonalStep);
                    cost += turnCost;
                    if (Height(next) != Height(tile))
                    {
                        cost += RampPenalty;
                    }

                    if (!nextPiece.HasRoad && Scenery.Kind(_map.SceneryData[next]) != 0)
                    {
                        cost += TreePenalty;
                    }

                    Relax(state, next, d, g + cost, goalX, goalZ);
                }
                else if (Dir8.IsStraight(d) && next >= 0 && IsWater(next) && longHere < 0 && ShapeAllows(tile, links, d))
                {
                    TryBridgeJump(state, tile, d, maxSpan, g + turnCost, goalX, goalZ, minX, maxX, minZ, maxZ);
                }
            }
        }

        return false;
    }

    /// <summary>A short bridge over water found by the road tool (research R4): the first land tile at the same height.</summary>
    private void TryBridgeJump(int state, int tile, int d, int maxSpan, long g, int goalX, int goalZ, int minX, int maxX, int minZ, int maxZ)
    {
        var h = Height(tile);
        for (var k = 1; k <= maxSpan; k++)
        {
            var t = Step(tile, d, k);
            if (t < 0 || !InBox(t, minX, maxX, minZ, maxZ))
            {
                return;
            }

            if (k >= 2 && !IsWater(t) && Height(t) == h)
            {
                var piece = Piece(t);
                if (EnterBlocker(t, allowMotorwayEnd: false) is null
                    && ShapeAllows(t, (byte)(piece.Links | Dir8.Bit(Dir8.Opposite(d))), Dir8.Opposite(d))
                    && piece.LongDir == 0)
                {
                    Relax(state, t, 8 + d, g + (BridgePerTile * k), goalX, goalZ);
                }

                return;
            }

            if (Surface(t) > h || IsIndustry(t) || (Piece(t).Flags & (RoadFeatures.OverBridgeX | RoadFeatures.OverBridgeZ)) != 0)
            {
                return;
            }
        }
    }

    private bool InBox(int tile, int minX, int maxX, int minZ, int maxZ)
    {
        if (tile < 0)
        {
            return false;
        }

        var x = tile % Width;
        var z = tile / Width;
        return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
    }

    private void Relax(int fromState, int tile, int arrival, long g, int goalX, int goalZ)
    {
        var state = (SearchSlot(tile) * StatesPerTile) + arrival;
        if (_stampOf[state] == _stamp && _g[state] <= g)
        {
            return;
        }

        Reach(state, g, fromState);
        Push(g + Heuristic(tile, goalX, goalZ), g, state);
    }

    private void Reach(int state, long g, int from)
    {
        _stampOf[state] = _stamp;
        _g[state] = g;
        _from[state] = from;
    }

    private int SearchSlot(int tile)
    {
        var slot = _search.Add(tile, out var added);
        if (added && (slot + 1) * StatesPerTile > _g.Length)
        {
            var size = _g.Length * 2;
            Array.Resize(ref _g, size);
            Array.Resize(ref _from, size);
            var stamps = new int[size];
            _stampOf.AsSpan().CopyTo(stamps);
            _stampOf = stamps;
        }

        if (added)
        {
            // A fresh slot may hold states of an earlier search with the current stamp only if the stamp
            // wrapped; clear its states so they read as unvisited.
            _stampOf.AsSpan(slot * StatesPerTile, StatesPerTile).Clear();
        }

        return slot;
    }

    private long Heuristic(int tile, int goalX, int goalZ)
    {
        var dx = Math.Abs((tile % Width) - goalX);
        var dz = Math.Abs((tile / Width) - goalZ);
        var low = Math.Min(dx, dz);
        var high = Math.Max(dx, dz);
        // Weighted (× 3/2): the search heads for the goal and expands far fewer states; the proposed path may
        // be a little longer than the cheapest, which is fine for a proposal (research R4).
        return ((high * (long)StraightStep) + (low * (long)(DiagonalStep - StraightStep))) * 3 / 2;
    }

    private void Reconstruct(int goalState)
    {
        _pathCount = 0;
        for (var state = goalState; state >= 0; state = _from[state])
        {
            if (_pathCount == _pathTiles.Length)
            {
                Array.Resize(ref _pathTiles, _pathCount * 2);
                Array.Resize(ref _pathArrival, _pathCount * 2);
            }

            _pathTiles[_pathCount] = _search[state / StatesPerTile];
            _pathArrival[_pathCount] = (byte)(state % StatesPerTile);
            _pathCount++;
        }

        Array.Reverse(_pathTiles, 0, _pathCount);
        Array.Reverse(_pathArrival, 0, _pathCount);
    }

    /// <summary>A path through the same tile twice would make a knot; refuse it (rare with the turn limit).</summary>
    private bool HasNoRepeats()
    {
        for (var i = 0; i < _pathCount; i++)
        {
            for (var j = i + 1; j < _pathCount && j < i + 9; j++)
            {
                if (_pathTiles[i] == _pathTiles[j])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void Push(long f, long g, int state)
    {
        if (_heapCount == _heapF.Length)
        {
            Array.Resize(ref _heapF, _heapCount * 2);
            Array.Resize(ref _heapG, _heapCount * 2);
            Array.Resize(ref _heapS, _heapCount * 2);
        }

        var index = _heapCount++;
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            if (!Less(f, g, state, _heapF[parent], _heapG[parent], _heapS[parent]))
            {
                break;
            }

            _heapF[index] = _heapF[parent];
            _heapG[index] = _heapG[parent];
            _heapS[index] = _heapS[parent];
            index = parent;
        }

        _heapF[index] = f;
        _heapG[index] = g;
        _heapS[index] = state;
    }

    private (long F, int State) Pop()
    {
        var f = _heapF[0];
        var s = _heapS[0];
        var count = --_heapCount;
        if (count > 0)
        {
            var lastF = _heapF[count];
            var lastG = _heapG[count];
            var lastS = _heapS[count];
            var index = 0;
            while (true)
            {
                var child = (index << 1) + 1;
                if (child >= count)
                {
                    break;
                }

                if (child + 1 < count && Less(_heapF[child + 1], _heapG[child + 1], _heapS[child + 1], _heapF[child], _heapG[child], _heapS[child]))
                {
                    child++;
                }

                if (!Less(_heapF[child], _heapG[child], _heapS[child], lastF, lastG, lastS))
                {
                    break;
                }

                _heapF[index] = _heapF[child];
                _heapG[index] = _heapG[child];
                _heapS[index] = _heapS[child];
                index = child;
            }

            _heapF[index] = lastF;
            _heapG[index] = lastG;
            _heapS[index] = lastS;
        }

        return (f, s);
    }

    /// <summary>
    /// Ties go to the deeper state (higher cost so far), then by state number; a state's number depends on the
    /// order tiles were first reached, itself deterministic.
    /// </summary>
    private static bool Less(long fa, long ga, int sa, long fb, long gb, int sb) =>
        fa < fb || (fa == fb && (ga > gb || (ga == gb && sa < sb)));
}
