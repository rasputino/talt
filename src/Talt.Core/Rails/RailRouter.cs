using Talt.Core.Construction;
using Talt.Core.Roads;
using Talt.Core.Stations;

namespace Talt.Core.Rails;

/// <summary>What a train route leads to (research R8, R9).</summary>
public enum RouteGoalKind : byte
{
    None = 0,

    /// <summary>The far end of any platform of a station, entered along the platform.</summary>
    Station = 1,

    /// <summary>A depot tile, entered through its entrance.</summary>
    Depot = 2,

    /// <summary>Any company depot (the nearest by cost).</summary>
    AnyDepot = 3,
}

/// <summary>A route: steps (from tile, direction) from the start tile to the goal; buffers reused per train.</summary>
public sealed partial class RailRoute
{
    private int[] _from = new int[64];
    private byte[] _dir = new byte[64];

    public int Count { get; private set; }

    public int FromAt(int index) => _from[index];

    public int DirAt(int index) => _dir[index];

    internal void Clear() => Count = 0;

    internal void Add(int from, int dir)
    {
        if (Count == _from.Length)
        {
            Array.Resize(ref _from, Count * 2);
            Array.Resize(ref _dir, Count * 2);
        }

        _from[Count] = from;
        _dir[Count] = (byte)dir;
        Count++;
    }

    internal void Reverse() => (_from.AsSpan(0, Count)).Reverse();

    internal void ReverseDirs() => _dir.AsSpan(0, Count).Reverse();

    internal void CopyFrom(RailRoute other)
    {
        Count = 0;
        for (var i = 0; i < other.Count; i++)
        {
            Add(other._from[i], other._dir[i]);
        }
    }
}

/// <summary>
/// Train route search (feature 015, research R8, R9): A* over (tile, heading) states on the rail layer, a train heading
/// <c>h</c> leaving a tile only by links within 45° of <c>h</c>. Costs favour fast, short track; tiles owned by other trains
/// cost extra so a train choosing at a path signal takes a free platform or loop. Electric trains use electrified track
/// only; one-way path signals are never passed against their face. One instance per thread; buffers reused.
/// </summary>
public sealed class RailRouter
{
    private const int TurnPenalty = 20;
    private readonly ConstructionState _state;
    private readonly RailLayer _rails;
    private readonly TileSet _search = new(4096);
    private readonly SearchHeap _heap = new();
    private long[] _g = new long[4096 * 8];
    private int[] _from = new int[4096 * 8];
    private int[] _stampOf = new int[4096 * 8];
    private int _stamp;
    private readonly int[] _typeCost;
    private readonly int _minTypeCost;

    public RailRouter(ConstructionState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _rails = state.Rails;
        var types = state.RailRules.Types;
        _typeCost = new int[types.Count];
        _minTypeCost = int.MaxValue;
        for (var t = 0; t < types.Count; t++)
        {
            // Cost per 256 units of track: inversely proportional to the speed limit.
            _typeCost[t] = Math.Max(1, 64_000 / types[t].SpeedKmh);
            _minTypeCost = Math.Min(_minTypeCost, _typeCost[t]);
        }
    }

    /// <summary>States expanded by the last search.</summary>
    public int LastExpanded { get; private set; }

    /// <summary>
    /// Finds the cheapest route from <paramref name="start"/> leaving it heading <paramref name="heading"/> to the goal.
    /// <paramref name="owners"/> (tile → train id) adds the occupied penalty for tiles of trains other than
    /// <paramref name="selfId"/>; null ignores trains. Returns false when no route exists within <paramref name="budget"/>
    /// expanded states (<paramref name="exhausted"/> tells which).
    /// </summary>
    public bool FindRoute(int start, int heading, RouteGoalKind goal, int goalId, int companyId, bool electric, int[]? owners, int selfId,
        int occupiedPenalty, int budget, RailRoute into, out long cost, out bool exhausted)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        cost = 0;
        exhausted = false;
        _search.Clear();
        _heap.Clear();
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_stampOf);
            _stamp = 1;
        }

        var stations = _state.Stations;
        var depots = _state.RailDepots;
        var goalTile = -1;
        if (goal == RouteGoalKind.Depot)
        {
            var index = depots.IndexOf(goalId);
            if (index < 0)
            {
                return false;
            }

            goalTile = depots.TileAt(index);
        }

        int goalX = -1, goalZ = -1;
        if (goal == RouteGoalKind.Station && stations is not null && stations.TryGetCentre(goalId, out var cx, out var cz))
        {
            (goalX, goalZ) = (cx, cz);
        }
        else if (goalTile >= 0)
        {
            (goalX, goalZ) = (goalTile % _rails.Width, goalTile / _rails.Width);
        }

        var width = _rails.Width;
        var startState = (Slot(start) * 8) + heading;
        Reach(startState, 0, -1);
        _heap.Push(Heuristic(start, goalX, goalZ), 0, startState);
        var expanded = 0;
        var found = -1;
        while (_heap.Count > 0)
        {
            var (g, state) = _heap.Pop();
            if (g > _g[state])
            {
                continue;
            }

            var tile = _search[state >> 3];
            var h = state & 7;
            if (state != startState && IsGoal(tile, h, goal, goalId, goalTile, companyId))
            {
                found = state;
                cost = g;
                break;
            }

            if (++expanded > budget)
            {
                exhausted = true;
                break;
            }

            var links = _rails.LinksAt(tile);
            var exits = RailGeometry.Exits(links, h);
            if (exits == 0)
            {
                continue;
            }

            var signal = _rails.SignalAt(tile);
            var oneWay = RailSignal.Kind(signal) == SignalKind.OneWayPath;
            var axis = oneWay ? RailSignal.Axis(links) : -1;
            for (var d = 0; d < 8; d++)
            {
                if ((exits & (1 << d)) == 0 || (oneWay && !RailSignal.Faces(signal, axis, d)))
                {
                    continue;
                }

                var next = _rails.FollowLink(tile, d);
                var type = _rails.TypeAt(next);
                if (type < 0 || (electric && !_state.RailRules.Types[type].Electrified))
                {
                    continue;
                }

                var kind = _state.Objects.KindAt(next);
                if (kind == TileObjectKind.RailDepot && next != goalTile
                    && !(goal == RouteGoalKind.AnyDepot && _state.Objects.OwnerAt(next).IsCompany(companyId)))
                {
                    continue; // Depots are dead ends trains only enter on purpose.
                }

                if (kind == TileObjectKind.RailDepot && depots.DirectionAtTile(next) != Dir8.Opposite(d))
                {
                    continue;
                }

                long step = ((long)_rails.LinkLength(tile, d) >> 8) * _typeCost[type];
                if (d != h)
                {
                    step += TurnPenalty;
                }

                if (owners is not null && owners[next] != 0 && owners[next] != selfId)
                {
                    step += occupiedPenalty;
                }

                var ng = g + step;
                var nextState = (Slot(next) * 8) + d;
                if (_stampOf[nextState] == _stamp && _g[nextState] <= ng)
                {
                    continue;
                }

                Reach(nextState, ng, state);
                _heap.Push(ng + Heuristic(next, goalX, goalZ), ng, nextState);
            }
        }

        LastExpanded = expanded;
        if (found < 0)
        {
            return false;
        }

        // Rebuild the steps from the goal back to the start.
        for (var s = found; _from[s] >= 0; s = _from[s])
        {
            into.Add(_search[_from[s] >> 3], s & 7);
        }

        into.Reverse();
        into.ReverseDirs();
        _ = width;
        return true;
    }

    /// <summary>True when the tile is a rail platform of the station.</summary>
    public bool IsPlatformOf(int tile, int stationId)
    {
        var stations = _state.Stations;
        return stations is not null && _state.Objects.KindAt(tile) == TileObjectKind.Station
            && stations.TryGetPieceAt(tile, out var id, out var type, out _) && id == stationId
            && type != StationRegistry.WarehousePiece && stations.Rules.Pieces[type].Kind == StationPieceKind.Rail;
    }

    private bool IsGoal(int tile, int heading, RouteGoalKind goal, int goalId, int goalTile, int companyId)
    {
        switch (goal)
        {
            case RouteGoalKind.Station:
                // The platform's far end in the travel direction (research R9): the search runs along the whole platform, so
                // tiles other trains hold on it count and a free platform wins.
                if (!IsPlatformOf(tile, goalId) || (_rails.LinksAt(tile) & Dir8.Bit(Dir8.Opposite(heading))) == 0)
                {
                    return false;
                }

                return (_rails.LinksAt(tile) & Dir8.Bit(heading)) == 0 || !IsPlatformOf(_rails.FollowLink(tile, heading), goalId);
            case RouteGoalKind.Depot:
                return tile == goalTile;
            case RouteGoalKind.AnyDepot:
                return _state.Objects.KindAt(tile) == TileObjectKind.RailDepot && _state.Objects.OwnerAt(tile).IsCompany(companyId);
            default:
                return false;
        }
    }

    private long Heuristic(int tile, int goalX, int goalZ)
    {
        if (goalX < 0)
        {
            return 0;
        }

        var dx = Math.Abs((tile % _rails.Width) - goalX);
        var dz = Math.Abs((tile / _rails.Width) - goalZ);
        var low = Math.Min(dx, dz);
        var high = Math.Max(dx, dz);
        // Octile distance in units / 256 × the cheapest track cost: never more than the real cost (admissible). A
        // station centre may lie up to half a platform beyond its nearest end, so the estimate is reduced by the most a
        // platform can reach.
        var units = (high * (long)Dir8.StraightLength) + (low * (long)(Dir8.DiagonalLength - Dir8.StraightLength));
        var slack = (long)(_state.RailRules.MaxPlatformLength + _state.RailRules.MaxPlatforms) * Dir8.StraightLength;
        return Math.Max(0, units - slack) / 256 * _minTypeCost;
    }

    private void Reach(int state, long g, int from)
    {
        _stampOf[state] = _stamp;
        _g[state] = g;
        _from[state] = from;
    }

    private int Slot(int tile)
    {
        var slot = _search.Add(tile, out var added);
        if (added && (slot + 1) * 8 > _g.Length)
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
            _stampOf.AsSpan(slot * 8, 8).Clear();
        }

        return slot;
    }
}
