using Talt.Core.Determinism;

namespace Talt.Core.Towns;

/// <summary>
/// Every town building of a game in structure-of-arrays form (data-model.md TownBuilding, research R1):
/// stable ids that are never reused, removal by swapping the last building into the free slot, and a
/// tile → building map for the rare queries (demolition, picking, replacement). Only
/// <see cref="TownRegistry"/> changes it, on the simulation thread.
/// </summary>
public sealed class TownBuildings : IStateHashable
{
    private const int InitialCapacity = 256;

    private readonly Dictionary<int, int> _buildingAtTile = [];
    private readonly Dictionary<int, int> _slotById = [];
    private readonly List<int> _changedTiles = [];
    private readonly int _mapWidth;

    private int[] _id = new int[InitialCapacity];
    private int[] _type = new int[InitialCapacity];
    private int[] _x = new int[InitialCapacity];
    private int[] _z = new int[InitialCapacity];
    private int[] _town = new int[InitialCapacity];
    private int[] _year = new int[InitialCapacity];
    private int[] _baseHeight = new int[InitialCapacity];
    private int _nextId = 1;

    internal TownBuildings(TownRules rules, int mapWidth)
    {
        Rules = rules;
        _mapWidth = mapWidth;
    }

    public TownRules Rules { get; }

    /// <summary>
    /// When true, changed footprint tiles are queued for <see cref="TryTakeChangedTiles"/> (the client's renderer
    /// turns it on; headless runs and benchmarks leave it off so nothing accumulates). Not simulation state.
    /// </summary>
    public bool TrackChanges { get; set; }

    /// <summary>
    /// Simulation-side list of changed footprint tiles for station coverage (feature 010, research R5), set by the
    /// station registry; independent of the client's <see cref="TrackChanges"/> list. Not simulation state.
    /// </summary>
    internal List<int>? StationWatch { get; set; }

    /// <summary>Number of buildings; valid slots are <c>0..Count-1</c>.</summary>
    public int Count { get; private set; }

    public int IdAt(int slot) => _id[slot];

    public int TypeAt(int slot) => _type[slot];

    public BuildingType TypeDefinitionAt(int slot) => Rules.Buildings[_type[slot]];

    /// <summary>Origin (minimum corner) tile column.</summary>
    public int XAt(int slot) => _x[slot];

    /// <summary>Origin (minimum corner) tile row.</summary>
    public int ZAt(int slot) => _z[slot];

    public int TownAt(int slot) => _town[slot];

    public int YearAt(int slot) => _year[slot];

    /// <summary>Height of the building's foundation (the highest ground of its footprint).</summary>
    public int BaseHeightAt(int slot) => _baseHeight[slot];

    /// <summary>Slot of a building id, or −1.</summary>
    public int SlotOf(int buildingId) => _slotById.TryGetValue(buildingId, out var slot) ? slot : -1;

    /// <summary>The building covering a tile index.</summary>
    public bool TryGetAt(int tile, out int buildingId) => _buildingAtTile.TryGetValue(tile, out buildingId);

    /// <summary>
    /// Moves the footprint tiles of buildings added or removed since the last call into <paramref name="into"/>
    /// (for the client's renderer). Not simulation state.
    /// </summary>
    public bool TryTakeChangedTiles(List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (_changedTiles.Count == 0)
        {
            return false;
        }

        into.AddRange(_changedTiles);
        _changedTiles.Clear();
        return true;
    }

    /// <summary>Forgets queued changes (after founding, which the client draws in full).</summary>
    public void DiscardChangedTiles() => _changedTiles.Clear();

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(Count);
        hasher.Append(_nextId);
        hasher.Append<int>(_id.AsSpan(0, Count));
        hasher.Append<int>(_type.AsSpan(0, Count));
        hasher.Append<int>(_x.AsSpan(0, Count));
        hasher.Append<int>(_z.AsSpan(0, Count));
        hasher.Append<int>(_town.AsSpan(0, Count));
        hasher.Append<int>(_year.AsSpan(0, Count));
        hasher.Append<int>(_baseHeight.AsSpan(0, Count));
    }

    /// <summary>Adds a building; the caller checked the plot. Returns its slot.</summary>
    internal int Add(int type, int x, int z, int town, int year, int baseHeight)
    {
        EnsureCapacity(Count + 1);
        var slot = Count++;
        var id = _nextId++;
        _id[slot] = id;
        _type[slot] = type;
        _x[slot] = x;
        _z[slot] = z;
        _town[slot] = town;
        _year[slot] = year;
        _baseHeight[slot] = baseHeight;
        _slotById.Add(id, slot);
        var definition = Rules.Buildings[type];
        for (var dz = 0; dz < definition.Depth; dz++)
        {
            for (var dx = 0; dx < definition.Width; dx++)
            {
                var tile = x + dx + ((z + dz) * _mapWidth);
                _buildingAtTile[tile] = id;
                StationWatch?.Add(tile);
                if (TrackChanges)
                {
                    _changedTiles.Add(tile);
                }
            }
        }

        return slot;
    }

    /// <summary>Removes the building in a slot (swap with last).</summary>
    internal void RemoveAt(int slot)
    {
        var definition = Rules.Buildings[_type[slot]];
        for (var dz = 0; dz < definition.Depth; dz++)
        {
            for (var dx = 0; dx < definition.Width; dx++)
            {
                var tile = _x[slot] + dx + ((_z[slot] + dz) * _mapWidth);
                _buildingAtTile.Remove(tile);
                StationWatch?.Add(tile);
                if (TrackChanges)
                {
                    _changedTiles.Add(tile);
                }
            }
        }

        _slotById.Remove(_id[slot]);
        var last = Count - 1;
        if (slot != last)
        {
            _id[slot] = _id[last];
            _type[slot] = _type[last];
            _x[slot] = _x[last];
            _z[slot] = _z[last];
            _town[slot] = _town[last];
            _year[slot] = _year[last];
            _baseHeight[slot] = _baseHeight[last];
            _slotById[_id[slot]] = slot;
        }

        Count = last;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _id.Length)
        {
            return;
        }

        var size = Math.Max(needed, _id.Length * 2);
        Array.Resize(ref _id, size);
        Array.Resize(ref _type, size);
        Array.Resize(ref _x, size);
        Array.Resize(ref _z, size);
        Array.Resize(ref _town, size);
        Array.Resize(ref _year, size);
        Array.Resize(ref _baseHeight, size);
    }
}
