using Talt.Core.Determinism;

namespace Talt.Core.Network;

/// <summary>One step of a route: an edge of <see cref="RoadNetwork"/> in tile terms (data-model.md Route).</summary>
public readonly record struct RouteStep(int FromTile, int FirstDirection, int ToTile, int Length, int SpeedKmh);

/// <summary>
/// Shortest routes over a <see cref="RoadNetwork"/> (research R6): A* on the junction-contracted graph with a
/// straight-line heuristic, plus a bounded cache of recent routes that is emptied whenever the network
/// changes.
/// </summary>
/// <remarks>
/// The open set is a binary heap ordered by (priority, higher cost, node tile) and a node is queued again only with a
/// strictly lower cost, so the route chosen among equally long ones depends only on the tile data, not on
/// node ids: a network updated incrementally and one built from scratch return the same route. Buffers are
/// reused (a visit stamp avoids clearing them); a search allocates nothing once they have grown to the
/// network's size. Not thread safe: one instance per thread.
/// </remarks>
public sealed class RoadRouter
{
    /// <summary>Most routes kept by the cache.</summary>
    public const int CacheCapacity = 4096;

    private readonly RoadNetwork _network;
    private readonly bool _useCache;
    private readonly Dictionary<long, RouteStep[]> _cache = [];
    private readonly Queue<long> _cacheOrder = new();
    private readonly List<RouteStep> _scratch = [];
    private int _cacheVersion = -1;

    private long[] _cost = [];
    private int[] _cameFrom = [];
    private int[] _cameFromEdge = [];
    private int[] _stamp = [];
    private long[] _heuristic = [];
    private int _currentStamp;

    private long[] _heapPriority = new long[1024];
    private long[] _heapCost = new long[1024];
    private int[] _heapKey = new int[1024];
    private int[] _heapNode = new int[1024];
    private int _heapCount;

    public RoadRouter(RoadNetwork network, bool useCache = true)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _useCache = useCache;
    }

    /// <summary>Nodes expanded by the last search (0 when served from the cache).</summary>
    public int LastExpandedNodes { get; private set; }

    /// <summary>True when the last route came from the cache.</summary>
    public bool LastFromCache { get; private set; }

    /// <summary>
    /// Finds the shortest route between two node tiles (junctions, dead ends, structure ends...).
    /// </summary>
    /// <param name="steps">Cleared, then filled with the route's edges in travel order.</param>
    /// <param name="length">Total length in units.</param>
    /// <returns>False when a tile is not a node or no route exists.</returns>
    public bool TryFindRoute(int fromTile, int toTile, List<RouteStep> steps, out long length)
    {
        ArgumentNullException.ThrowIfNull(steps);
        steps.Clear();
        length = 0;
        LastExpandedNodes = 0;
        LastFromCache = false;
        var network = _network;
        if (!network.TryGetNode(fromTile, out var start) || !network.TryGetNode(toTile, out var goal))
        {
            return false;
        }

        if (start == goal)
        {
            return true;
        }

        var key = ((long)fromTile << 32) | (uint)toTile;
        if (_useCache)
        {
            if (_cacheVersion != network.Version)
            {
                _cache.Clear();
                _cacheOrder.Clear();
                _cacheVersion = network.Version;
            }

            if (_cache.TryGetValue(key, out var cached))
            {
                LastFromCache = true;
                foreach (var step in cached)
                {
                    steps.Add(step);
                    length += step.Length;
                }

                return cached.Length > 0;
            }
        }

        var found = Search(start, goal, steps, out length);
        if (_useCache)
        {
            if (_cache.Count >= CacheCapacity)
            {
                _cache.Remove(_cacheOrder.Dequeue());
            }

            _cache[key] = found ? [.. steps] : [];
            _cacheOrder.Enqueue(key);
        }

        return found;
    }

    private bool Search(int start, int goal, List<RouteStep> steps, out long length)
    {
        length = 0;
        var network = _network;
        EnsureCapacity(network.NodeCapacity);
        NextStamp();
        _heapCount = 0;
        var width = network.Layer.Width;
        var goalTile = network.NodeTile(goal);
        var goalX = goalTile % width;
        var goalZ = goalTile / width;

        Reach(start, 0, -1, -1, goalX, goalZ, width);
        Push(_heuristic[start], 0, network.NodeTile(start), start);
        while (Pop(out var node, out var priority))
        {
            if (priority > _cost[node] + _heuristic[node])
            {
                continue; // Obsolete entry: a cheaper path to the node was found later.
            }

            if (node == goal)
            {
                length = _cost[goal];
                Reconstruct(goal, steps);
                return true;
            }

            LastExpandedNodes++;
            var targets = network.OutTargets(node);
            var lengths = network.OutLengths(node);
            for (var e = 0; e < targets.Length; e++)
            {
                var next = targets[e];
                var cost = _cost[node] + lengths[e];
                if (_stamp[next] != _currentStamp)
                {
                    Reach(next, cost, node, e, goalX, goalZ, width);
                    Push(cost + _heuristic[next], cost, network.NodeTile(next), next);
                }
                else if (cost < _cost[next])
                {
                    _cost[next] = cost;
                    _cameFrom[next] = node;
                    _cameFromEdge[next] = e;
                    Push(cost + _heuristic[next], cost, network.NodeTile(next), next);
                }
            }
        }

        return false;
    }

    private void Reach(int node, long cost, int from, int edge, int goalX, int goalZ, int width)
    {
        _stamp[node] = _currentStamp;
        _cost[node] = cost;
        _cameFrom[node] = from;
        _cameFromEdge[node] = edge;
        var tile = _network.NodeTile(node);
        _heuristic[node] = Octile(Math.Abs(goalX - (tile % width)), Math.Abs(goalZ - (tile / width)));
    }

    /// <summary>
    /// Octile distance in units: every road step is straight (65 536) or diagonal (92 682), and long links are
    /// straight, so no route is shorter. Tighter than the straight-line distance, so fewer nodes are expanded.
    /// </summary>
    private static long Octile(int dx, int dz)
    {
        var low = Math.Min(dx, dz);
        var high = Math.Max(dx, dz);
        return ((long)high * Roads.Dir8.StraightLength) + ((long)low * (Roads.Dir8.DiagonalLength - Roads.Dir8.StraightLength));
    }

    private void Reconstruct(int goal, List<RouteStep> steps)
    {
        _scratch.Clear();
        var network = _network;
        for (var node = goal; _cameFrom[node] >= 0; node = _cameFrom[node])
        {
            var from = _cameFrom[node];
            var e = _cameFromEdge[node];
            _scratch.Add(new RouteStep(
                network.NodeTile(from),
                network.OutFirstDirections(from)[e],
                network.NodeTile(node),
                network.OutLengths(from)[e],
                network.OutSpeeds(from)[e]));
        }

        for (var i = _scratch.Count - 1; i >= 0; i--)
        {
            steps.Add(_scratch[i]);
        }
    }

    private void EnsureCapacity(int nodes)
    {
        if (_cost.Length >= nodes)
        {
            return;
        }

        var size = Math.Max(nodes, Math.Max(256, _cost.Length * 2));
        Array.Resize(ref _cost, size);
        Array.Resize(ref _cameFrom, size);
        Array.Resize(ref _cameFromEdge, size);
        Array.Resize(ref _stamp, size);
        Array.Resize(ref _heuristic, size);
    }

    private void NextStamp()
    {
        _currentStamp++;
        if (_currentStamp == int.MaxValue)
        {
            Array.Clear(_stamp);
            _currentStamp = 1;
        }
    }

    private void Push(long priority, long cost, int key, int node)
    {
        if (_heapCount == _heapNode.Length)
        {
            var size = _heapCount * 2;
            Array.Resize(ref _heapPriority, size);
            Array.Resize(ref _heapCost, size);
            Array.Resize(ref _heapKey, size);
            Array.Resize(ref _heapNode, size);
        }

        var index = _heapCount++;
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            if (!Less(priority, cost, key, _heapPriority[parent], _heapCost[parent], _heapKey[parent]))
            {
                break;
            }

            _heapPriority[index] = _heapPriority[parent];
            _heapCost[index] = _heapCost[parent];
            _heapKey[index] = _heapKey[parent];
            _heapNode[index] = _heapNode[parent];
            index = parent;
        }

        _heapPriority[index] = priority;
        _heapCost[index] = cost;
        _heapKey[index] = key;
        _heapNode[index] = node;
    }

    private bool Pop(out int node, out long priority)
    {
        if (_heapCount == 0)
        {
            node = 0;
            priority = 0;
            return false;
        }

        node = _heapNode[0];
        priority = _heapPriority[0];
        var count = --_heapCount;
        if (count > 0)
        {
            var lastPriority = _heapPriority[count];
            var lastCost = _heapCost[count];
            var lastKey = _heapKey[count];
            var lastNode = _heapNode[count];
            var index = 0;
            while (true)
            {
                var child = (index << 1) + 1;
                if (child >= count)
                {
                    break;
                }

                if (child + 1 < count && Less(_heapPriority[child + 1], _heapCost[child + 1], _heapKey[child + 1], _heapPriority[child], _heapCost[child], _heapKey[child]))
                {
                    child++;
                }

                if (!Less(_heapPriority[child], _heapCost[child], _heapKey[child], lastPriority, lastCost, lastKey))
                {
                    break;
                }

                _heapPriority[index] = _heapPriority[child];
                _heapCost[index] = _heapCost[child];
                _heapKey[index] = _heapKey[child];
                _heapNode[index] = _heapNode[child];
                index = child;
            }

            _heapPriority[index] = lastPriority;
            _heapCost[index] = lastCost;
            _heapKey[index] = lastKey;
            _heapNode[index] = lastNode;
        }

        return true;
    }

    /// <summary>Order (priority, then higher cost first, then node tile): pure tile data, so routes do not depend on node ids.</summary>
    private static bool Less(long priorityA, long costA, int keyA, long priorityB, long costB, int keyB) =>
        priorityA < priorityB || (priorityA == priorityB && (costA > costB || (costA == costB && keyA < keyB)));
}
