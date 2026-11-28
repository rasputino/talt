using System.Numerics;
using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Stations;

namespace Talt.Client.Screens;

/// <summary>Routes display legs on the physical network without disturbing vehicle routing budgets or caches.</summary>
internal sealed class LineOverlayRouter(ConstructionState state, StationRegistry stations)
{
    private readonly PriorityQueue<int, (long Estimate, long NegativeCost, int Tile)> _front = new();
    private readonly Dictionary<int, (long Cost, int Parent)> _visited = [];
    private readonly HashSet<int> _targets = [];
    private readonly RailRoute _railPath = new();
    private RailRouter? _railRouter;

    /// <summary>Returns the ordered tiles between compatible station pieces, including both endpoints.</summary>
    internal bool FindRoadLeg(int origin, int destination, StationPieceKind kind, List<int> path)
    {
        path.Clear();
        _targets.Clear();
        _front.Clear();
        _visited.Clear();
        var first = stations.SlotOf(origin);
        var last = stations.SlotOf(destination);
        if (first < 0 || last < 0)
        {
            return false;
        }

        var bounds = stations.BoundsAt(last);
        long DistanceToGoal(int tile)
        {
            var x = tile % state.Map.Width;
            var z = tile / state.Map.Width;
            var dx = Math.Max(0, Math.Max(bounds.X0 - x, x - bounds.X1));
            var dz = Math.Max(0, Math.Max(bounds.Z0 - z, z - bounds.Z1));
            return (long)Math.Max(dx, dz) * Dir8.StraightLength + (long)Math.Min(dx, dz) * (Dir8.DiagonalLength - Dir8.StraightLength);
        }

        var tiles = stations.TilesAt(last);
        for (var i = 0; i < tiles.Count; i++)
        {
            if (HasKind(tiles[i], kind))
            {
                _targets.Add(tiles[i]);
            }
        }

        if (_targets.Count == 0)
        {
            return false;
        }

        tiles = stations.TilesAt(first);
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (HasKind(tile, kind))
            {
                _front.Enqueue(tile, (DistanceToGoal(tile), 0, tile));
                _visited[tile] = (0, -1);
            }
        }

        var roads = state.Roads;
        while (_front.TryDequeue(out var tile, out var priority))
        {
            var travelled = -priority.NegativeCost;
            if (_visited[tile].Cost != travelled)
            {
                continue;
            }

            if (_targets.Contains(tile))
            {
                while (tile >= 0)
                {
                    path.Add(tile);
                    tile = _visited[tile].Parent;
                }

                path.Reverse();
                return true;
            }

            var exits = roads.ExitsAt(tile) & roads.LinksAt(tile);
            while (exits != 0)
            {
                var dir = BitOperations.TrailingZeroCount(exits);
                exits &= exits - 1;
                var to = roads.FollowLink(tile, dir);
                var cost = travelled + roads.LinkLength(tile, dir);
                if (!_visited.TryGetValue(to, out var old) || cost < old.Cost)
                {
                    _visited[to] = (cost, tile);
                    _front.Enqueue(to, (cost + DistanceToGoal(to), -cost, to));
                }
            }
        }

        return false;
    }

    internal bool FindRailLeg(int origin, int destination, int owner, bool electric, List<int> path)
    {
        path.Clear();
        var slot = stations.SlotOf(origin);
        if (slot < 0 || stations.SlotOf(destination) < 0)
        {
            return false;
        }

        _railRouter ??= new RailRouter(state);
        var tiles = stations.TilesAt(slot);
        var best = long.MaxValue;
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (!HasKind(tile, StationPieceKind.Rail))
            {
                continue;
            }

            var exits = (int)state.Rails.LinksAt(tile);
            while (exits != 0)
            {
                var dir = BitOperations.TrailingZeroCount(exits);
                exits &= exits - 1;
                // Depart from the platform end that faces outwards, avoiding redundant searches inside the platform.
                if (_railRouter.IsPlatformOf(state.Rails.FollowLink(tile, dir), origin)
                    || !_railRouter.FindRoute(tile, dir, RouteGoalKind.Station, destination, owner, electric,
                        null, -1, 0, int.MaxValue, _railPath, out var cost, out _) || cost >= best)
                {
                    continue;
                }

                best = cost;
                path.Clear();
                for (var step = 0; step < _railPath.Count; step++)
                {
                    path.Add(_railPath.FromAt(step));
                }

                var end = _railPath.Count - 1;
                path.Add(state.Rails.FollowLink(_railPath.FromAt(end), _railPath.DirAt(end)));
            }
        }

        return path.Count != 0;
    }

    private bool HasKind(int tile, StationPieceKind kind) =>
        stations.TryGetPieceAt(tile, out _, out var type, out _) && type != StationRegistry.WarehousePiece && stations.Rules.Pieces[type].Kind == kind;
}
