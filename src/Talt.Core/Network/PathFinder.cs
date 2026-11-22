using Talt.Core.Determinism;

namespace Talt.Core.Network;

/// <summary>
/// A* shortest path search over a <see cref="TransportNetwork"/>, in integer units.
/// </summary>
/// <remarks>
/// Reuses its internal buffers between searches, so a search allocates nothing beyond growing the
/// caller's output list. Not thread safe: use one instance per thread.
/// A "visit stamp" marks which entries belong to the current search, so buffers never need clearing.
/// Ties in the priority queue are broken by node index, so results are deterministic.
/// </remarks>
public sealed class PathFinder
{
    private readonly TransportNetwork _network;
    private readonly long[] _cost;
    private readonly int[] _cameFromEdge;
    private readonly int[] _stamp;
    private readonly PriorityQueue<int, (long Priority, int Node)> _open = new();
    private int _currentStamp;

    public PathFinder(TransportNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);

        _network = network;
        _cost = new long[network.NodeCount];
        _cameFromEdge = new int[network.NodeCount];
        _stamp = new int[network.NodeCount];
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

        Visit(start, 0, -1);
        _open.Enqueue(start, (Heuristic(start, goalPosition), start));

        while (_open.TryDequeue(out var node, out var key))
        {
            // Lazy deletion: skip queue entries made obsolete by a cheaper path found later.
            if (key.Priority > _cost[node] + Heuristic(node, goalPosition))
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
                if (_stamp[next] != _currentStamp || cost < _cost[next])
                {
                    Visit(next, cost, edge);
                    _open.Enqueue(next, (cost + Heuristic(next, goalPosition), next));
                }
            }
        }

        return false;
    }

    private void Visit(int node, long cost, int fromEdge)
    {
        _stamp[node] = _currentStamp;
        _cost[node] = cost;
        _cameFromEdge[node] = fromEdge;
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
}
