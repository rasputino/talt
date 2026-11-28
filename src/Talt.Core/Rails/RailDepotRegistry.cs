using Talt.Core.Determinism;

namespace Talt.Core.Rails;

/// <summary>
/// The rail depots of a game (feature 015, data-model.md RailDepot): id, owner, tile and entrance direction. Few, so a
/// list in creation order with stable ids; hashed with the train system once anything existed.
/// </summary>
public sealed partial class RailDepotRegistry : IStateHashable
{
    private readonly List<Depot> _depots = [];
    private readonly Dictionary<int, int> _indexByTile = [];
    private int _nextId = 1;

    public int Count => _depots.Count;

    public bool EverUsed => _nextId > 1;

    /// <summary>The id the next depot will get.</summary>
    public int NextId => _nextId;

    /// <summary>Incremented on every change (windows refresh).</summary>
    public int Version { get; private set; }

    public int IdAt(int index) => _depots[index].Id;

    public int OwnerAt(int index) => _depots[index].Owner;

    public int TileAt(int index) => _depots[index].Tile;

    public int DirectionAt(int index) => _depots[index].Direction;

    /// <summary>Index of a depot id, or −1.</summary>
    public int IndexOf(int depotId)
    {
        for (var i = 0; i < _depots.Count; i++)
        {
            if (_depots[i].Id == depotId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of the depot on a tile, or −1.</summary>
    public int IndexAtTile(int tile) => _indexByTile.TryGetValue(tile, out var index) ? index : -1;

    /// <summary>The entrance direction of the depot on a tile, or −1.</summary>
    public int DirectionAtTile(int tile) => _indexByTile.TryGetValue(tile, out var index) ? _depots[index].Direction : -1;

    internal int Add(int owner, int tile, int direction)
    {
        var id = _nextId++;
        _depots.Add(new Depot(id, owner, tile, direction));
        _indexByTile[tile] = _depots.Count - 1;
        Version++;
        return id;
    }

    internal void Remove(int depotId)
    {
        var index = IndexOf(depotId);
        if (index < 0)
        {
            return;
        }

        _depots.RemoveAt(index);
        _indexByTile.Clear();
        for (var i = 0; i < _depots.Count; i++)
        {
            _indexByTile[_depots[i].Tile] = i;
        }

        Version++;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_nextId);
        hasher.Append(_depots.Count);
        foreach (var depot in _depots)
        {
            hasher.Append(depot.Id);
            hasher.Append(depot.Owner);
            hasher.Append(depot.Tile);
            hasher.Append(depot.Direction);
        }
    }

    private readonly record struct Depot(int Id, int Owner, int Tile, int Direction);
}
