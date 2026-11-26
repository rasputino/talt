using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Simulation;
using Talt.Core.Towns;

namespace Talt.Core.Stations;

/// <summary>Road station (bus/truck pieces) or warehouse.</summary>
public enum StationKind : byte
{
    Road = 0,
    Warehouse = 1,

    /// <summary>A rail station of platform tiles (feature 015).</summary>
    Rail = 2,
}

/// <summary>What an automatic station name is made of (research R11).</summary>
public enum StationNameKind : byte
{
    BusStop = 0,
    TruckStop = 1,
    Warehouse = 2,

    /// <summary>A rail station: town and place suffix (feature 015).</summary>
    RailStation = 3,
}

/// <summary>Place suffix of a warehouse name, from its position relative to the town centre.</summary>
public enum StationPlace : byte
{
    Central = 0,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

/// <summary>One station tile: its station, piece type (<see cref="StationRegistry.WarehousePiece"/> for warehouse tiles) and direction.</summary>
internal readonly record struct StationPiece(int StationId, byte Type, byte Direction);

/// <summary>
/// Every station of a game and the cargo rules around them (feature 010, research R1): road station pieces
/// joined into stations, warehouses grouping stations into virtual stations, catchment and acceptance,
/// waiting cargo, ratings and statistics, and the loading and unloading operations vehicles use (012).
/// </summary>
/// <remarks>
/// State is structure-of-arrays: stations by slot (ids stable, never reused, removal swaps the last station
/// in), groups by slot (rebuilt when construction changes them, research R4), per group and cargo tables
/// indexed <c>group * cargoCount + cargo</c>, and a pool of waiting cargo packets. State changes only through
/// construction commands, the daily and monthly updates and the vehicle operations, all integer-only and
/// without randomness; the rating phase may run in parallel per group with identical results. A registry
/// where no station was ever built appends nothing to the fingerprint (FR-030).
/// </remarks>
public sealed partial class StationRegistry : IStateHashable, IProductionSink
{
    /// <summary>Piece type value of warehouse footprint tiles.</summary>
    public const byte WarehousePiece = 255;

    /// <summary>Longest custom station name.</summary>
    public const int MaxNameLength = 32;

    private const int InitialCapacity = 16;

    private readonly ConstructionState _state;
    private readonly MapData _map;
    private readonly GameContent _content;
    private readonly IndustryFleet _industries;
    private readonly TownRegistry? _towns;
    private readonly StationSettings _settings;
    private readonly int _cargoCount;
    private readonly CargoCategory[] _cargoCategory;
    private readonly int _passengerCargo;
    private readonly int _mailCargo;
    private readonly int _goodsCargo;
    private readonly int _foodCargo;

    private readonly Dictionary<int, StationPiece> _pieces = [];
    private readonly Dictionary<int, int> _slotById = [];
    private readonly Dictionary<long, int> _nameCounters = [];

    // Centres of removed stations (feature 011, research R3), packed as z × 65 536 + x.
    private readonly Dictionary<int, int> _removedCentre = [];

    // Stations by slot.
    private int[] _id = new int[InitialCapacity];
    private int[] _owner = new int[InitialCapacity];
    private byte[] _kind = new byte[InitialCapacity];
    private byte[] _warehouseType = new byte[InitialCapacity];
    private int[] _x0 = new int[InitialCapacity];
    private int[] _z0 = new int[InitialCapacity];
    private int[] _x1 = new int[InitialCapacity];
    private int[] _z1 = new int[InitialCapacity];
    private int[] _town = new int[InitialCapacity];
    private byte[] _nameKind = new byte[InitialCapacity];
    private byte[] _namePlace = new byte[InitialCapacity];
    private int[] _nameNumber = new int[InitialCapacity];
    private string?[] _customName = new string?[InitialCapacity];
    private int[] _categories = new int[InitialCapacity];
    private int[] _radius = new int[InitialCapacity];
    private int[] _group = new int[InitialCapacity];
    private List<int>[] _tiles = new List<int>[InitialCapacity];
    private int _count;
    private int _nextId = 1;

    // Running totals (conservation checks and statistics).
    private long _totalSupplied;
    private long _totalLost;
    private long _totalDelivered;
    private long _totalDebugCollected;
    private int _today;

    private StationRegistry(ConstructionState state, GameContent content, IndustryFleet industries, TownRegistry? towns, StationRules rules, StationSettings settings, int today)
    {
        _state = state;
        _map = state.Map;
        _content = content;
        _industries = industries;
        _towns = towns;
        Rules = rules;
        _settings = settings;
        _today = today;
        _cargoCount = content.CargoCount;
        _cargoCategory = new CargoCategory[_cargoCount];
        for (var c = 0; c < _cargoCount; c++)
        {
            _cargoCategory[c] = content.Cargo(c).Category;
        }

        _passengerCargo = content.CargoIndex("passengers");
        _mailCargo = content.CargoIndex("mail");
        _goodsCargo = content.CargoIndex("goods");
        _foodCargo = content.CargoIndex("food");
        _ratingPhase = RatingPhase;
        InitGroups();
        InitCoverage();
    }

    /// <summary>Station balancing.</summary>
    public StationRules Rules { get; }

    public StationSettings Settings => _settings;

    public GameContent Content => _content;

    /// <summary>Number of stations (road stations and warehouses); valid slots are <c>0..Count-1</c>.</summary>
    public int Count => _count;

    /// <summary>The id the next station will get (debug seeders predict the ids of their commands).</summary>
    public int NextStationId => _nextId;

    /// <summary>Number of station tiles.</summary>
    public int PieceCount => _pieces.Count;

    /// <summary>Bumped on every construction or rename (clients cache by it). Not hashed.</summary>
    public int Version { get; private set; }

    /// <summary>The current day number (set at each day boundary).</summary>
    public int Today => _today;

    /// <summary>Receives accepted deliveries (feature 011). Not state.</summary>
    public IDeliveryListener? DeliveryListener { get; set; }

    /// <summary>Total units ever moved from producers to stations.</summary>
    public long TotalSupplied => _totalSupplied;

    /// <summary>Total units ever lost (waiting cap, neglect, removed stations).</summary>
    public long TotalLost => _totalLost;

    /// <summary>Total units ever delivered to consumers.</summary>
    public long TotalDelivered => _totalDelivered;

    /// <summary>Total units picked up by the debug service (<c>--station-service</c>).</summary>
    public long TotalDebugCollected => _totalDebugCollected;

    /// <summary>Creates the registry of a game with a map (no stations yet).</summary>
    public static StationRegistry Create(
        ConstructionState state,
        GameContent content,
        IndustryFleet industries,
        TownRegistry? towns,
        StationSettings? settings,
        GameDate date,
        StationRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(industries);
        settings ??= StationSettings.Default;
        settings.Validate();
        rules ??= ReferenceEquals(content, GameContent.Default) ? StationRules.Default : StationRules.FromJson(StationRules.ReadResource(), content);
        return new StationRegistry(state, content, industries, towns, rules, settings, date.DayNumber);
    }

    // ----- Queries -----

    public int IdAt(int slot) => _id[slot];

    public int OwnerAt(int slot) => _owner[slot];

    public StationKind KindAt(int slot) => (StationKind)_kind[slot];

    /// <summary>Warehouse size index of a warehouse station.</summary>
    public int WarehouseTypeAt(int slot) => _warehouseType[slot];

    /// <summary>Bounding box of the station's tiles (inclusive).</summary>
    public (int X0, int Z0, int X1, int Z1) BoundsAt(int slot) => (_x0[slot], _z0[slot], _x1[slot], _z1[slot]);

    /// <summary>Tiles of the station (map indices), in build order.</summary>
    public IReadOnlyList<int> TilesAt(int slot) => _tiles[slot];

    /// <summary>Slot of a station id, or −1.</summary>
    public int SlotOf(int stationId) => _slotById.TryGetValue(stationId, out var slot) ? slot : -1;

    /// <summary>
    /// The centre tile of a station's bounding box, also for stations removed since (feature 011, research R3):
    /// deliveries are paid by the distance from where the cargo was picked up.
    /// </summary>
    public bool TryGetCentre(int stationId, out int x, out int z)
    {
        var slot = SlotOf(stationId);
        if (slot >= 0)
        {
            x = (_x0[slot] + _x1[slot]) / 2;
            z = (_z0[slot] + _z1[slot]) / 2;
            return true;
        }

        if (_removedCentre.TryGetValue(stationId, out var packed))
        {
            x = packed & 0xFFFF;
            z = packed >> 16;
            return true;
        }

        x = z = -1;
        return false;
    }

    /// <summary>
    /// Counts a company's property for maintenance and company value (feature 011): road station pieces by piece
    /// type into <paramref name="pieces"/> and warehouses by size into <paramref name="warehouses"/> (added to
    /// what the spans hold). Walks the stations once; allocates nothing.
    /// </summary>
    public void CountProperty(int companyId, Span<int> pieces, Span<int> warehouses)
    {
        for (var slot = 0; slot < _count; slot++)
        {
            if (_owner[slot] != companyId)
            {
                continue;
            }

            if ((StationKind)_kind[slot] == StationKind.Warehouse)
            {
                warehouses[_warehouseType[slot]]++;
                continue;
            }

            foreach (var tile in _tiles[slot])
            {
                pieces[_pieces[tile].Type]++;
            }
        }
    }

    private static int PackCentre(int x, int z) => (z << 16) | x;

    /// <summary>The station with a tile on (x, z).</summary>
    public bool TryGetStationAt(int x, int z, out int stationId)
    {
        stationId = -1;
        if (!_map.Contains(x, z) || !_pieces.TryGetValue(_map.Index(x, z), out var piece))
        {
            return false;
        }

        stationId = piece.StationId;
        return true;
    }

    /// <summary>The piece on a tile: its type index (or <see cref="WarehousePiece"/>) and direction.</summary>
    public bool TryGetPieceAt(int tile, out int stationId, out int pieceType, out int direction)
    {
        if (_pieces.TryGetValue(tile, out var piece))
        {
            stationId = piece.StationId;
            pieceType = piece.Type;
            direction = piece.Direction;
            return true;
        }

        stationId = pieceType = direction = -1;
        return false;
    }

    /// <summary>The station's automatic name parts: town (−1 none), kind, place (warehouses) and number.</summary>
    public (int Town, StationNameKind Kind, StationPlace Place, int Number) NameAt(int slot) =>
        (_town[slot], (StationNameKind)_nameKind[slot], (StationPlace)_namePlace[slot], _nameNumber[slot]);

    /// <summary>The name the player gave the station, or null for its automatic name.</summary>
    public string? CustomNameAt(int slot) => _customName[slot];

    /// <summary>Bit set of the cargo categories the station's pieces handle (bit = <see cref="CargoCategory"/>).</summary>
    public int CategoriesAt(int slot) => _categories[slot];

    /// <summary>True when one of the station's pieces handles the cargo.</summary>
    public bool Handles(int slot, int cargo) => (_categories[slot] & (1 << (int)_cargoCategory[cargo])) != 0;

    // ----- Simulation -----

    /// <summary>
    /// Start of a day, before industries produce (they hand production to <see cref="TakeProduction"/>): sets
    /// the day, refreshes coverage after construction or town changes and applies the debug service's
    /// "served" marks.
    /// </summary>
    public void BeginDay(GameDate date)
    {
        _today = date.DayNumber;
        EnsureCoverage();
        if (_settings.DebugServicePercent > 0)
        {
            MarkDebugServed();
        }
    }

    /// <summary>
    /// The rest of the day (research R14): town buildings' passengers and mail to stations, ratings and losses
    /// (per group, in parallel when allowed), and the debug service's pickups.
    /// </summary>
    public void AdvanceDay(GameDate date, bool parallel)
    {
        if (_count == 0)
        {
            return;
        }

        SupplyFromBuildings(date);
        UpdateRatings(parallel);
        if (_settings.DebugServicePercent > 0 && date.DayNumber % Rules.DebugIntervalDays == 0)
        {
            DebugPickups();
        }
    }

    /// <summary>Rotates the monthly statistics of every group and cargo.</summary>
    public void AdvanceMonth(GameDate date)
    {
        _ = date;
        RotateStatistics();
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (_nextId == 1)
        {
            return; // No station was ever built: games keep their pre-010 fingerprints (FR-030).
        }

        hasher.Append(Rules.Fingerprint);
        hasher.Append(_settings.DebugServicePercent);
        hasher.Append(_nextId);
        hasher.Append(_count);
        for (var slot = 0; slot < _count; slot++)
        {
            hasher.Append(_id[slot]);
            hasher.Append(_owner[slot]);
            hasher.Append(_kind[slot]);
            hasher.Append(_warehouseType[slot]);
            hasher.Append(_x0[slot]);
            hasher.Append(_z0[slot]);
            hasher.Append(_x1[slot]);
            hasher.Append(_z1[slot]);
            hasher.Append(_town[slot]);
            hasher.Append(_nameKind[slot]);
            hasher.Append(_namePlace[slot]);
            hasher.Append(_nameNumber[slot]);
            hasher.Append(_customName[slot] ?? string.Empty);
            hasher.Append(_categories[slot]);
            hasher.Append(_radius[slot]);
            hasher.Append(_group[slot]);
            var tiles = _tiles[slot];
            hasher.Append(tiles.Count);
            foreach (var tile in tiles)
            {
                var piece = _pieces[tile];
                hasher.Append(tile);
                hasher.Append(piece.Type);
                hasher.Append(piece.Direction);
            }
        }

        foreach (var key in SortedKeys(_nameCounters))
        {
            hasher.Append(key);
            hasher.Append(_nameCounters[key]);
        }

        AppendGroups(hasher);
        foreach (var key in SortedKeys(_carry))
        {
            hasher.Append(key);
            hasher.Append(_carry[key]);
        }

        hasher.Append(_totalSupplied);
        hasher.Append(_totalLost);
        hasher.Append(_totalDelivered);
        hasher.Append(_totalDebugCollected);
        if (_removedCentre.Count > 0)
        {
            // Feature 011: only once a station was removed, so earlier station goldens stay valid.
            var ids = new int[_removedCentre.Count];
            _removedCentre.Keys.CopyTo(ids, 0);
            Array.Sort(ids);
            hasher.Append(ids.Length);
            foreach (var id in ids)
            {
                hasher.Append(id);
                hasher.Append(_removedCentre[id]);
            }
        }
    }

    private static long[] SortedKeys<TValue>(Dictionary<long, TValue> dictionary)
    {
        var keys = new long[dictionary.Count];
        dictionary.Keys.CopyTo(keys, 0);
        Array.Sort(keys);
        return keys;
    }

    private void EnsureStationCapacity(int needed)
    {
        if (needed <= _id.Length)
        {
            return;
        }

        var size = Math.Max(needed, _id.Length * 2);
        Array.Resize(ref _id, size);
        Array.Resize(ref _owner, size);
        Array.Resize(ref _kind, size);
        Array.Resize(ref _warehouseType, size);
        Array.Resize(ref _x0, size);
        Array.Resize(ref _z0, size);
        Array.Resize(ref _x1, size);
        Array.Resize(ref _z1, size);
        Array.Resize(ref _town, size);
        Array.Resize(ref _nameKind, size);
        Array.Resize(ref _namePlace, size);
        Array.Resize(ref _nameNumber, size);
        Array.Resize(ref _customName, size);
        Array.Resize(ref _categories, size);
        Array.Resize(ref _radius, size);
        Array.Resize(ref _group, size);
        Array.Resize(ref _tiles, size);
        ResizeCoverage(size);
    }
}
