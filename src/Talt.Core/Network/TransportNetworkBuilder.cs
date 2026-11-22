namespace Talt.Core.Network;

/// <summary>
/// Mutable helper to assemble a <see cref="TransportNetwork"/>.
/// </summary>
public sealed class TransportNetworkBuilder
{
    private readonly List<GridPoint> _nodes = [];
    private readonly List<int> _edgeFrom = [];
    private readonly List<int> _edgeTo = [];

    public int NodeCount => _nodes.Count;

    /// <summary>Adds a node at a ground position (in units) and returns its index.</summary>
    public int AddNode(GridPoint position)
    {
        _nodes.Add(position);
        return _nodes.Count - 1;
    }

    /// <summary>Adds a node at the corner of a tile and returns its index.</summary>
    public int AddNodeAtTile(int x, int z) => AddNode(GridPoint.FromTiles(x, z));

    /// <summary>Adds a one-way edge and returns its index.</summary>
    public int AddEdge(int from, int to)
    {
        if ((uint)from >= (uint)_nodes.Count || (uint)to >= (uint)_nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(from), "Both nodes must exist before adding an edge.");
        }

        if (from == to)
        {
            throw new ArgumentException("An edge cannot start and end at the same node.", nameof(to));
        }

        _edgeFrom.Add(from);
        _edgeTo.Add(to);
        return _edgeFrom.Count - 1;
    }

    /// <summary>Adds a two-way road: one edge in each direction.</summary>
    public void AddRoad(int a, int b)
    {
        AddEdge(a, b);
        AddEdge(b, a);
    }

    public TransportNetwork Build() => new([.. _nodes], [.. _edgeFrom], [.. _edgeTo]);
}
