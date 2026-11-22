namespace Talt.Core.Network;

/// <summary>
/// A* shortest path search over a <see cref="TransportNetwork"/>.
/// </summary>
/// <remarks>
/// Reuses its internal buffers between searches, so a search allocates nothing beyond growing the
/// caller's output list. Not thread safe: use one instance per thread.
/// A "visit stamp" marks which entries belong to the current search, so buffers never need clearing.
/// </remarks>
public sealed class PathFinder
{
    private readonly TransportNetwork _network;
    private readonly float[] _cost;
    private readonly int[] _cameFromEdge;
    private readonly int[] _stamp;
    private readonly PriorityQueue<int, float> _open = new();
    private int _currentStamp;

    public PathFinder(TransportNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);

        _network = network;
        _cost = new float[network.NodeCount];
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

        Visit(start, 0f, -1);
        _open.Enqueue(start, Heuristic(start, goalPosition));

        while (_open.TryDequeue(out var node, out var priority))
        {
            // Lazy deletion: skip queue entries made obsolete by a cheaper path found later.
            if (priority > _cost[node] + Heuristic(node, goalPosition) + 1e-3f)
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
                    _open.Enqueue(next, cost + Heuristic(next, goalPosition));
                }
            }
        }

        return false;
    }

    private void Visit(int node, float cost, int fromEdge)
    {
        _stamp[node] = _currentStamp;
        _cost[node] = cost;
        _cameFromEdge[node] = fromEdge;
    }

    /// <summary>Straight-line distance. Never overestimates, because edges are straight segments.</summary>
    private float Heuristic(int node, System.Numerics.Vector2 goalPosition) =>
        System.Numerics.Vector2.Distance(_network.NodePosition(node), goalPosition);

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
