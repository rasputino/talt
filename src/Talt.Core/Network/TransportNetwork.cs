using System.Numerics;

namespace Talt.Core.Network;

/// <summary>
/// Immutable directed graph of a transport network. Nodes are junctions or stations; edges are
/// one-way segments between them. A two-way road is two edges.
/// </summary>
/// <remarks>
/// Adjacency is stored in compressed sparse row (CSR) form: the outgoing edges of node <c>n</c> are
/// <c>_outEdges[_firstOutEdge[n] .. _firstOutEdge[n + 1]]</c>. Everything lives in flat arrays, so
/// traversals are cache friendly and allocation free. Build instances with
/// <see cref="TransportNetworkBuilder"/>.
/// </remarks>
public sealed class TransportNetwork
{
    private readonly Vector2[] _nodePositions;
    private readonly int[] _edgeFrom;
    private readonly int[] _edgeTo;
    private readonly float[] _edgeLength;
    private readonly int[] _firstOutEdge;
    private readonly int[] _outEdges;

    internal TransportNetwork(Vector2[] nodePositions, int[] edgeFrom, int[] edgeTo)
    {
        _nodePositions = nodePositions;
        _edgeFrom = edgeFrom;
        _edgeTo = edgeTo;
        _edgeLength = new float[edgeFrom.Length];
        _firstOutEdge = new int[nodePositions.Length + 1];
        _outEdges = new int[edgeFrom.Length];

        for (var e = 0; e < edgeFrom.Length; e++)
        {
            _edgeLength[e] = Vector2.Distance(nodePositions[edgeFrom[e]], nodePositions[edgeTo[e]]);
            _firstOutEdge[edgeFrom[e] + 1]++;
        }

        for (var n = 0; n < nodePositions.Length; n++)
        {
            _firstOutEdge[n + 1] += _firstOutEdge[n];
        }

        var fill = (int[])_firstOutEdge.Clone();
        for (var e = 0; e < edgeFrom.Length; e++)
        {
            _outEdges[fill[edgeFrom[e]]++] = e;
        }
    }

    public int NodeCount => _nodePositions.Length;

    public int EdgeCount => _edgeFrom.Length;

    /// <summary>Position of a node on the ground plane, in tiles (X, Z).</summary>
    public Vector2 NodePosition(int node) => _nodePositions[node];

    public int EdgeFrom(int edge) => _edgeFrom[edge];

    public int EdgeTo(int edge) => _edgeTo[edge];

    /// <summary>Length of an edge, in tiles.</summary>
    public float EdgeLength(int edge) => _edgeLength[edge];

    public ReadOnlySpan<int> OutEdges(int node) =>
        _outEdges.AsSpan(_firstOutEdge[node], _firstOutEdge[node + 1] - _firstOutEdge[node]);
}
