using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Roads;

namespace Talt.Core.Vehicles;

/// <summary>
/// The road depots of a game (feature 012, data-model.md Depot): stable ids that are never reused, owner, tile and the
/// direction from the depot tile towards its road. Structure of arrays by slot with swap-with-last removal; a tile →
/// id dictionary answers "is there a depot here". Depot tiles are <see cref="TileObjectKind.Depot"/> objects with a
/// one-link road piece, so the routing graph sees them as dead ends (research R5).
/// </summary>
public sealed partial class DepotRegistry : IStateHashable
{
    private readonly Dictionary<int, int> _slotById = [];
    private readonly Dictionary<int, int> _idByTile = [];
    private int[] _id = new int[8];
    private int[] _owner = new int[8];
    private int[] _tile = new int[8];
    private byte[] _direction = new byte[8];
    private int _nextId = 1;

    /// <summary>Number of depots.</summary>
    public int Count { get; private set; }

    /// <summary>Incremented on every change (client instances).</summary>
    public int Version { get; private set; }

    /// <summary>True once a depot was ever built (turns hashing on).</summary>
    public bool EverUsed => _nextId > 1;

    /// <summary>The id the next depot will get.</summary>
    public int NextId => _nextId;

    public int IdAt(int slot) => _id[slot];

    public int OwnerAt(int slot) => _owner[slot];

    public int TileAt(int slot) => _tile[slot];

    /// <summary>Direction (<see cref="Dir8"/>) from the depot tile towards its road.</summary>
    public int DirectionAt(int slot) => _direction[slot];

    public int SlotOf(int depotId) => _slotById.TryGetValue(depotId, out var slot) ? slot : -1;

    public bool HasDepot(int tile) => _idByTile.ContainsKey(tile);

    /// <summary>The depot on a tile.</summary>
    public bool TryGetAt(int tile, out int depotId) => _idByTile.TryGetValue(tile, out depotId);

    /// <summary>
    /// True when a road change on <paramref name="tile"/> would break a depot: the depot tile's own piece, or the road
    /// tile in front of a depot losing its link into it.
    /// </summary>
    public bool RoadChangeBlocked(ConstructionState state, int tile, in RoadPiece newPiece)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Count == 0)
        {
            return false;
        }

        if (_idByTile.ContainsKey(tile))
        {
            return true;
        }

        var roads = state.Roads;
        for (var d = 0; d < 8; d += 2)
        {
            var neighbour = roads.Neighbour(tile, d);
            if (neighbour >= 0 && _idByTile.TryGetValue(neighbour, out var id)
                && _direction[_slotById[id]] == Dir8.Opposite(d) && (newPiece.Links & Dir8.Bit(d)) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds a depot (after its construction plan was committed); returns its id.</summary>
    public int Add(int ownerId, int tile, int direction)
    {
        if (Count == _id.Length)
        {
            var size = Count * 2;
            Array.Resize(ref _id, size);
            Array.Resize(ref _owner, size);
            Array.Resize(ref _tile, size);
            Array.Resize(ref _direction, size);
        }

        var slot = Count++;
        var id = _nextId++;
        _id[slot] = id;
        _owner[slot] = ownerId;
        _tile[slot] = tile;
        _direction[slot] = (byte)direction;
        _slotById[id] = slot;
        _idByTile[tile] = id;
        Version++;
        return id;
    }

    /// <summary>Removes a depot (its tile was cleared by the removal plan).</summary>
    public void Remove(int depotId)
    {
        if (!_slotById.Remove(depotId, out var slot))
        {
            return;
        }

        _idByTile.Remove(_tile[slot]);
        var last = Count - 1;
        if (slot != last)
        {
            _id[slot] = _id[last];
            _owner[slot] = _owner[last];
            _tile[slot] = _tile[last];
            _direction[slot] = _direction[last];
            _slotById[_id[slot]] = slot;
        }

        Count--;
        Version++;
    }

    /// <summary>Depots of a company (for property maintenance).</summary>
    public int CountOwnedBy(int companyId)
    {
        var count = 0;
        for (var slot = 0; slot < Count; slot++)
        {
            if (_owner[slot] == companyId)
            {
                count++;
            }
        }

        return count;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_nextId);
        hasher.Append(Count);
        hasher.Append<int>(_id.AsSpan(0, Count));
        hasher.Append<int>(_owner.AsSpan(0, Count));
        hasher.Append<int>(_tile.AsSpan(0, Count));
        hasher.Append<byte>(_direction.AsSpan(0, Count));
    }
}
