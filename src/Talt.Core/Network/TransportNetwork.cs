using System.Numerics;
using Talt.Core.Determinism;

namespace Talt.Core.Network;

/// <summary>
/// Immutable directed graph of a transport network. Nodes are junctions or stations; edges are
/// one-way segments between them. A two-way road is two edges.
/// </summary>
/// <remarks>
/// <para>
/// Adjacency is stored in compressed sparse row (CSR) form: the outgoing edges of node <c>n</c> are
/// <c>_outEdges[_firstOutEdge[n] .. _firstOutEdge[n + 1]]</c>. Everything lives in flat arrays, so
/// traversals are cache friendly and allocation free. Build instances with
/// <see cref="TransportNetworkBuilder"/>.
/// </para>
/// <para>
/// Positions and lengths are integer fixed-point units so the simulation stays bit-identical on every
/// platform. Edge lengths are computed once with an exact integer square root.
/// </para>
/// </remarks>
public sealed class TransportNetwork : IStateHashable
{
    private readonly int[] _nodeX;
    private readonly int[] _nodeZ;
    private readonly int[] _edgeFrom;
    private readonly int[] _edgeTo;
    private readonly int[] _edgeLength;
    private readonly int[] _firstOutEdge;
    private readonly int[] _outEdges;
    private readonly ulong _fingerprint;

    internal TransportNetwork(GridPoint[] nodes, int[] edgeFrom, int[] edgeTo)
    {
        _nodeX = [.. nodes.Select(n => n.X)];
        _nodeZ = [.. nodes.Select(n => n.Z)];
        _edgeFrom = edgeFrom;
        _edgeTo = edgeTo;
        _edgeLength = new int[edgeFrom.Length];
        _firstOutEdge = new int[nodes.Length + 1];
        _outEdges = new int[edgeFrom.Length];

        for (var e = 0; e < edgeFrom.Length; e++)
        {
            var dx = (long)_nodeX[edgeTo[e]] - _nodeX[edgeFrom[e]];
            var dz = (long)_nodeZ[edgeTo[e]] - _nodeZ[edgeFrom[e]];
            _edgeLength[e] = checked((int)FixedMath.Length(dx, dz));
            _firstOutEdge[edgeFrom[e] + 1]++;
        }

        for (var n = 0; n < nodes.Length; n++)
        {
            _firstOutEdge[n + 1] += _firstOutEdge[n];
        }

        var fill = (int[])_firstOutEdge.Clone();
        for (var e = 0; e < edgeFrom.Length; e++)
        {
            _outEdges[fill[edgeFrom[e]]++] = e;
        }

        // The network is immutable: hash it once and reuse the value in every fingerprint.
        var hasher = new StateHasher();
        hasher.Append<int>(_nodeX);
        hasher.Append<int>(_nodeZ);
        hasher.Append<int>(_edgeFrom);
        hasher.Append<int>(_edgeTo);
        _fingerprint = hasher.GetFingerprint();
    }

    public int NodeCount => _nodeX.Length;

    public int EdgeCount => _edgeFrom.Length;

    /// <summary>Position of a node, in units.</summary>
    public GridPoint NodePosition(int node) => new(_nodeX[node], _nodeZ[node]);

    /// <summary>Rendering only: position of a node, in tiles.</summary>
    public Vector2 NodePositionTiles(int node) => NodePosition(node).ToTiles();

    public int EdgeFrom(int edge) => _edgeFrom[edge];

    public int EdgeTo(int edge) => _edgeTo[edge];

    /// <summary>Length of an edge, in units.</summary>
    public int EdgeLength(int edge) => _edgeLength[edge];

    public ReadOnlySpan<int> OutEdges(int node) =>
        _outEdges.AsSpan(_firstOutEdge[node], _firstOutEdge[node + 1] - _firstOutEdge[node]);

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_fingerprint);
    }
}
