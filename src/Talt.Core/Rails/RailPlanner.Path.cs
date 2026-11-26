using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Core.Rails;

/// <summary>The track tool: path proposal over (tile, heading) states with turns of at most 45° (research R3).</summary>
public sealed partial class RailPlanner
{
    private readonly TileSet _search = new(4096);
    private readonly SearchHeap _heap = new();
    private long[] _g = new long[4096 * StatesPerTile];
    private int[] _from = new int[4096 * StatesPerTile];
    private int[] _stampOf = new int[4096 * StatesPerTile];
    private int _stamp;
    private int[] _pathTiles = new int[256];
    private int _pathCount;

    /// <summary>States expanded by the last path search (benchmarks).</summary>
    public int LastExpandedStates { get; private set; }

    /// <summary>Plans track of type <paramref name="type"/> from tile <paramref name="a"/> to tile <paramref name="b"/> (<c>buildRail</c>).</summary>
    public void PlanTrack(ConstructionState state, int companyId, int type, (int X, int Z) a, (int X, int Z) b, ConstructionPlan plan)
    {
        Begin(state, companyId, plan);
        if (!CheckType(type))
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
        plan.Area = new TileRect(a.X, a.Z, b.X, b.Z);
        plan.HasArea = true;
        if (start == goal)
        {
            var blocker = EnterBlocker(start, -1, true);
            if (blocker is not null || KindAt(start) != TileObjectKind.None)
            {
                Refuse(start, blocker ?? ConstructionReasons.NothingToDo);
                return;
            }

            NewTrack(Track(start), type);
            Emit();
            return;
        }

        if (!FindPath(start, goal))
        {
            Refuse(goal, ConstructionReasons.NoPath);
            return;
        }

        if (_pathCount > state.RoadRules.MaxPathTiles)
        {
            plan.Reason = ConstructionReasons.TooLarge;
            return;
        }

        if (TotalTurn() >= MaxTurnSteps)
        {
            // Joining track at an angle it cannot take would need a loop (hand test 2026-11-26): refuse and say why.
            Refuse(goal, ConstructionReasons.TrackLoop);
            return;
        }

        for (var i = 0; i < _pathCount; i++)
        {
            var tile = _pathTiles[i];
            var slot = Track(tile);
            if (!_piece[slot].HasRail)
            {
                NewTrack(slot, type);
            }

            if (i + 1 < _pathCount)
            {
                var next = _pathTiles[i + 1];
                var d = Dir8.FromOffset((next % Width) - (tile % Width), (next / Width) - (tile / Width));
                if (!Piece(tile).HasLink(d))
                {
                    Link(tile, d, next);
                    if (Dir8.IsStraight(d) && Height(next) != Height(tile))
                    {
                        plan.Cost += _rules.SlopeCost;
                    }
                }
            }
        }

        VerifyView();
        Emit();
    }

    private bool FindPath(int start, int goal)
    {
        _search.Clear();
        _heap.Clear();
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_stampOf);
            _stamp = 1;
        }

        var goalX = goal % Width;
        var goalZ = goal / Width;
        var margin = _state.RoadRules.SearchMargin;
        var minX = Math.Max(0, Math.Min(start % Width, goalX) - margin);
        var maxX = Math.Min(Width - 1, Math.Max(start % Width, goalX) + margin);
        var minZ = Math.Max(0, Math.Min(start / Width, goalZ) - margin);
        var maxZ = Math.Min(_map.Depth - 1, Math.Max(start / Width, goalZ) + margin);
        var startState = (SearchSlot(start) * StatesPerTile) + StartState;
        Reach(startState, 0, -1);
        _heap.Push(Heuristic(start, goalX, goalZ), 0, startState);
        var expanded = 0;
        var limit = _state.RoadRules.MaxSearchNodes;
        while (_heap.Count > 0)
        {
            var (g, state) = _heap.Pop();
            if (g > _g[state])
            {
                continue;
            }

            var slot = state / StatesPerTile;
            var heading = state % StatesPerTile;
            var tile = _search[slot];
            if (tile == goal)
            {
                LastExpandedStates = expanded;
                Reconstruct(state);
                return true;
            }

            if (++expanded > limit)
            {
                break;
            }

            var straightOnly = heading != StartState && tile != start && MustGoStraight(tile);
            for (var d = 0; d < 8; d++)
            {
                if (heading != StartState && (RailGeometry.Turn(heading, d) > 1 || (straightOnly && d != heading)))
                {
                    continue;
                }

                if (!TerrainAllows(tile, d, out var next))
                {
                    continue;
                }

                var nx = next % Width;
                var nz = next / Width;
                if (nx < minX || nx > maxX || nz < minZ || nz > maxZ)
                {
                    continue;
                }

                if (tile == start && KindAt(start) is TileObjectKind.Station or TileObjectKind.Road
                    && !(Piece(start).HasLink(d) || Piece(start).HasLink(Dir8.Opposite(d))))
                {
                    continue; // Leaving a platform or crossing only along its track.
                }

                if (tile == start && KindAt(start) == TileObjectKind.RailDepot && _state.RailDepots.DirectionAtTile(start) != d)
                {
                    continue; // Leaving a depot only through its entrance.
                }

                if (EnterBlocker(next, d, next == goal) is not null)
                {
                    continue;
                }

                // Joining existing track must not make a 90° kink at either tile.
                var here = Piece(tile);
                var there = Piece(next);
                if ((here.HasRail && !RailGeometry.IsValidShape((byte)(here.Links | Dir8.Bit(d))))
                    || (there.HasRail && !RailGeometry.IsValidShape((byte)(there.Links | Dir8.Bit(Dir8.Opposite(d))))))
                {
                    continue;
                }

                var existing = Piece(tile).HasLink(d);
                long cost = Dir8.IsStraight(d) ? (existing ? ReuseStraight : StraightStep) : (existing ? ReuseDiagonal : DiagonalStep);
                if (heading != StartState && d != heading)
                {
                    cost += Turn45;
                }

                if (Height(next) != Height(tile))
                {
                    cost += RampPenalty;
                }

                if (Scenery.Kind(_map.SceneryData[next]) != 0)
                {
                    cost += TreePenalty;
                }

                if (KindAt(next) == TileObjectKind.Road)
                {
                    cost += CrossingPenalty;
                }

                var nextState = (SearchSlot(next) * StatesPerTile) + d;
                var ng = g + cost;
                if (_stampOf[nextState] == _stamp && _g[nextState] <= ng)
                {
                    continue;
                }

                Reach(nextState, ng, state);
                _heap.Push(ng + Heuristic(next, goalX, goalZ), ng, nextState);
            }
        }

        LastExpandedStates = expanded;
        return false;
    }

    /// <summary>A drag may turn its heading by less than this many 45° steps (180°): a U-turn or more is a loop.</summary>
    private const int MaxTurnSteps = 4;

    /// <summary>
    /// The largest net change of heading along the proposed path, in 45° steps: S-curves cancel out, so only paths that
    /// turn back on themselves reach <see cref="MaxTurnSteps"/>.
    /// </summary>
    private int TotalTurn()
    {
        var net = 0;
        var most = 0;
        var previous = -1;
        for (var i = 0; i + 1 < _pathCount; i++)
        {
            var a = _pathTiles[i];
            var b = _pathTiles[i + 1];
            var d = Dir8.FromOffset((b % Width) - (a % Width), (b / Width) - (a / Width));
            if (previous >= 0)
            {
                var turn = ((d - previous + 12) & 7) - 4; // −4..3: signed 45° steps.
                net += turn;
                most = Math.Max(most, Math.Abs(net));
            }

            previous = d;
        }

        return most;
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
            }

            _pathTiles[_pathCount++] = _search[state / StatesPerTile];
        }

        Array.Reverse(_pathTiles, 0, _pathCount);
    }
}
