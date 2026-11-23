using Talt.Core.Determinism;

namespace Talt.Core.Network;

/// <summary>
/// A* shortest path search over a <see cref="TransportNetwork"/>, in integer units.
/// </summary>
/// <remarks>
/// Reuses its internal buffers between searches, so a search allocates nothing beyond growing the
/// caller's output list. Not thread safe: use one instance per thread.
/// A "visit stamp" marks which entries belong to the current search, so buffers never need clearing.
/// The heuristic of each node is computed once per search (when the node is first reached) and
/// cached, because its integer square root is the most expensive step of an expansion.
/// Ties in the priority queue are broken by node index, so results are deterministic. Within one
/// search every (priority, node) key is unique (a node is queued again only with a strictly lower
/// cost), so the order of expansions does not depend on how the queue is implemented.
/// </remarks>
public sealed class PathFinder
{
    private readonly TransportNetwork _network;
    private readonly long[] _cost;
    private readonly int[] _cameFromEdge;
    private readonly int[] _stamp;
    private readonly long[] _heuristic;
    private readonly MinHeap _open = new();
    private int _currentStamp;

    public PathFinder(TransportNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);

        _network = network;
        _cost = new long[network.NodeCount];
        _cameFromEdge = new int[network.NodeCount];
        _stamp = new int[network.NodeCount];
        _heuristic = new long[network.NodeCount];
    }

    /// <summary>Number of nodes expanded by the last search. Useful for benchmarks.</summary>
    public int LastExpandedNodes { get; private set; }

    /// <summary>
    /// Finds the shortest path from <paramref name="start"/> to <paramref name="goal"/>.
    /// </summary>
    /// <param name="start">Start node.</param>
    /// <param name="goal">Goal node.</param>
    /// <param name="edges">Cleared, then filled with the edges of the path in travel order.</param>
    /// <returns>False when the goal cannot be reached.</returns>
    public bool TryFindPath(int start, int goal, List<int> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        edges.Clear();
        LastExpandedNodes = 0;

        if (start == goal)
        {
            return true;
        }

        NextStamp();
        _open.Clear();
        var goalPosition = _network.NodePosition(goal);

        Reach(start, 0, -1, goalPosition);
        _open.Enqueue(_heuristic[start], start);

        while (_open.TryDequeue(out var node, out var priority))
        {
            // Lazy deletion: skip queue entries made obsolete by a cheaper path found later.
            if (priority > _cost[node] + _heuristic[node])
            {
                continue;
            }

            if (node == goal)
            {
                Reconstruct(goal, edges);
                return true;
            }

            LastExpandedNodes++;
            foreach (var edge in _network.OutEdges(node))
            {
                var next = _network.EdgeTo(edge);
                var cost = _cost[node] + _network.EdgeLength(edge);
                if (_stamp[next] != _currentStamp)
                {
                    Reach(next, cost, edge, goalPosition);
                    _open.Enqueue(cost + _heuristic[next], next);
                }
                else if (cost < _cost[next])
                {
                    _cost[next] = cost;
                    _cameFromEdge[next] = edge;
                    _open.Enqueue(cost + _heuristic[next], next);
                }
            }
        }

        return false;
    }

    /// <summary>First time <paramref name="node"/> is reached in this search.</summary>
    private void Reach(int node, long cost, int fromEdge, GridPoint goal)
    {
        _stamp[node] = _currentStamp;
        _cost[node] = cost;
        _cameFromEdge[node] = fromEdge;
        _heuristic[node] = Heuristic(node, goal);
    }

    /// <summary>
    /// Straight-line distance, rounded down. Exact for grid-aligned edges; for other edges the
    /// rounding of each edge length can make it overestimate by at most one unit per edge, which
    /// is negligible (1/65 536 of a tile) and keeps results deterministic.
    /// </summary>
    private long Heuristic(int node, GridPoint goal)
    {
        var position = _network.NodePosition(node);
        return FixedMath.Length((long)goal.X - position.X, (long)goal.Z - position.Z);
    }

    private void Reconstruct(int goal, List<int> edges)
    {
        for (var edge = _cameFromEdge[goal]; edge >= 0; edge = _cameFromEdge[_network.EdgeFrom(edge)])
        {
            edges.Add(edge);
        }

        edges.Reverse();
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

    /// <summary>
    /// Binary min-heap ordered by (priority, node). Faster than <see cref="PriorityQueue{TElement, TPriority}"/>
    /// with a tuple priority because comparisons are inlined; keeps its storage between searches.
    /// </summary>
    private sealed class MinHeap
    {
        private long[] _priorities = new long[1024];
        private int[] _nodes = new int[1024];
        private int _count;

        public void Clear() => _count = 0;

        public void Enqueue(long priority, int node)
        {
            if (_count == _nodes.Length)
            {
                Array.Resize(ref _priorities, _count * 2);
                Array.Resize(ref _nodes, _count * 2);
            }

            // Sift up.
            var index = _count++;
            while (index > 0)
            {
                var parent = (index - 1) >> 1;
                if (!Less(priority, node, _priorities[parent], _nodes[parent]))
                {
                    break;
                }

                _priorities[index] = _priorities[parent];
                _nodes[index] = _nodes[parent];
                index = parent;
            }

            _priorities[index] = priority;
            _nodes[index] = node;
        }

        public bool TryDequeue(out int node, out long priority)
        {
            if (_count == 0)
            {
                node = 0;
                priority = 0;
                return false;
            }

            node = _nodes[0];
            priority = _priorities[0];

            // Move the last entry to the root and sift it down.
            var count = --_count;
            if (count > 0)
            {
                var lastPriority = _priorities[count];
                var lastNode = _nodes[count];
                var index = 0;
                while (true)
                {
                    var child = (index << 1) + 1;
                    if (child >= count)
                    {
                        break;
                    }

                    if (child + 1 < count && Less(_priorities[child + 1], _nodes[child + 1], _priorities[child], _nodes[child]))
                    {
                        child++;
                    }

                    if (!Less(_priorities[child], _nodes[child], lastPriority, lastNode))
                    {
                        break;
                    }

                    _priorities[index] = _priorities[child];
                    _nodes[index] = _nodes[child];
                    index = child;
                }

                _priorities[index] = lastPriority;
                _nodes[index] = lastNode;
            }

            return true;
        }

        private static bool Less(long priorityA, int nodeA, long priorityB, int nodeB) =>
            priorityA < priorityB || (priorityA == priorityB && nodeA < nodeB);
    }
}
