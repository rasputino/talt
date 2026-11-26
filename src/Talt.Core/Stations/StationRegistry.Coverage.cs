using Talt.Core.Content;

namespace Talt.Core.Stations;

/// <summary>
/// Catchment, acceptance and the producer table (research R5). All of this is derived from stations,
/// industries and town buildings, rebuilt only when one of them changed, and never hashed.
/// </summary>
public sealed partial class StationRegistry
{
    private const int GridCell = 16;

    private readonly Dictionary<int, List<int>> _grid = [];
    private readonly List<int> _buildingWatch = [];
    private readonly Dictionary<int, int> _industryRow = [];
    private int[][] _kindAccepts = [];
    private int[][] _kindProduces = [];

    // Per station slot.
    private List<int>[] _covIndustries = new List<int>[InitialCapacity];
    private List<int>[] _covBuildings = new List<int>[InitialCapacity];
    private int[] _accept = new int[InitialCapacity];
    private ulong[] _acceptMask = new ulong[InitialCapacity];
    private ulong[] _supplyMask = new ulong[InitialCapacity];
    private bool[] _dirty = new bool[InitialCapacity];
    private int[] _gx0 = new int[InitialCapacity];
    private int[] _gz0 = new int[InitialCapacity];
    private int[] _gx1 = new int[InitialCapacity];
    private int[] _gz1 = new int[InitialCapacity];

    // Per group.
    private ulong[] _groupAccept = [];

    // Producer table (CSR): one row per covered industry or town building.
    private byte[] _prodKind = new byte[64];
    private int[] _prodId = new int[64];
    private int[] _prodTown = new int[64];
    private int[] _prodPassengers = new int[64];
    private int[] _prodMail = new int[64];
    private int[] _prodStart = new int[64];
    private int[] _prodCount = new int[64];
    private int[] _coverSlots = new int[256];
    private long[] _pairs = new long[256];
    private int _prodRows;
    private int _industryVersion = -1;
    private bool _coverageChanged;

    private const byte ProducerIndustry = 1;
    private const byte ProducerBuilding = 2;

    /// <summary>True when the station's own catchment accepts the cargo (8 eighths or more, and a piece handles it).</summary>
    public bool StationAccepts(int stationId, int cargo)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot >= 0 && (_acceptMask[slot] & (1UL << cargo)) != 0;
    }

    /// <summary>True when the station's group accepts the cargo (any member accepts it).</summary>
    public bool Accepts(int stationId, int cargo)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot >= 0 && (_groupAccept[_group[slot]] & (1UL << cargo)) != 0;
    }

    /// <summary>Acceptance of a cargo in the station's own catchment, in eighths.</summary>
    public int AcceptanceAt(int stationId, int cargo)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot < 0 ? 0 : _accept[(slot * _cargoCount) + cargo];
    }

    /// <summary>Bit set of the cargo the station would receive (produced in its catchment and handled).</summary>
    public ulong SuppliesMask(int stationId)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot < 0 ? 0 : _supplyMask[slot];
    }

    /// <summary>Bit set of the cargo the station's own catchment accepts.</summary>
    public ulong AcceptsMask(int stationId)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot < 0 ? 0 : _acceptMask[slot];
    }

    /// <summary>Industry ids with a tile in the station's catchment.</summary>
    public IReadOnlyList<int> CoveredIndustries(int stationId)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot < 0 ? [] : _covIndustries[slot];
    }

    /// <summary>Town building ids with a tile in the station's catchment.</summary>
    public IReadOnlyList<int> CoveredBuildings(int stationId)
    {
        EnsureCoverage();
        var slot = SlotOf(stationId);
        return slot < 0 ? [] : _covBuildings[slot];
    }

    /// <summary>
    /// Adds the map tiles of a station's catchment to <paramref name="into"/> (a warehouse's influence square for
    /// a warehouse). For the client's highlight; not for per-tick use.
    /// </summary>
    public void CatchmentTiles(int stationId, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var slot = SlotOf(stationId);
        if (slot < 0)
        {
            return;
        }

        var warehouse = _kind[slot] == (byte)StationKind.Warehouse;
        var r = warehouse ? Rules.Warehouses[_warehouseType[slot]].InfluenceRadius : _radius[slot];
        var x0 = Math.Max(0, _x0[slot] - r);
        var z0 = Math.Max(0, _z0[slot] - r);
        var x1 = Math.Min(_map.Width - 1, _x1[slot] + r);
        var z1 = Math.Min(_map.Depth - 1, _z1[slot] + r);
        for (var z = z0; z <= z1; z++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (warehouse || InCatchmentSlot(slot, x, z))
                {
                    into.Add(_map.Index(x, z));
                }
            }
        }
    }

    /// <summary>True when (x, z) is inside the station's catchment.</summary>
    public bool InCatchment(int stationId, int x, int z)
    {
        var slot = SlotOf(stationId);
        return slot >= 0 && InCatchmentSlot(slot, x, z);
    }

    /// <summary>
    /// Fills <paramref name="into"/> with what a placement would cover (FR-011): the catchment tiles of a new road
    /// piece joined with the station it would join, or a warehouse's influence square, and the cargo the result
    /// would receive and accept. Allocation-free once the preview's buffers have grown.
    /// </summary>
    public void PreviewCatchment(int companyId, bool warehouse, int typeIndex, int x, int z, CatchmentPreview into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Reset();
        if (!_map.Contains(x, z))
        {
            return;
        }

        EnsureCoverage();
        if (warehouse)
        {
            var type = Rules.Warehouses[typeIndex];
            var r = type.InfluenceRadius;
            into.SetArea(x - r, z - r, x + type.Width - 1 + r, z + type.Depth - 1 + r, _map.Width, _map.Depth);
            for (var tz = into.Z0; tz <= into.Z1; tz++)
            {
                for (var tx = into.X0; tx <= into.X1; tx++)
                {
                    into.AddTile(_map.Index(tx, tz));
                }
            }

            return;
        }

        PreviewArea(companyId, typeIndex, x, z, x, z, into);
    }

    /// <summary>
    /// The catchment preview of a rectangle of pieces (feature 015: rail platforms), like <see cref="PreviewCatchment"/>
    /// for one piece.
    /// </summary>
    public void PreviewArea(int companyId, int typeIndex, int x, int z, int xEnd, int zEnd, CatchmentPreview into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Reset();
        if (!_map.Contains(x, z) || (uint)typeIndex >= (uint)Rules.Pieces.Count)
        {
            return;
        }

        EnsureCoverage();
        var piece = Rules.Pieces[typeIndex];
        var join = -1;
        for (var jz = z; jz <= zEnd && join < 0; jz++)
        {
            for (var jx = x; jx <= xEnd && join < 0; jx++)
            {
                join = _map.Contains(jx, jz) ? JoinTarget(companyId, jx, jz) : -1;
            }
        }

        var joinSlot = join >= 0 ? SlotOf(join) : -1;
        var categories = piece.CategoryMask | (joinSlot >= 0 ? _categories[joinSlot] : 0);
        var radius = Math.Max(piece.CatchmentRadius, joinSlot >= 0 ? _radius[joinSlot] : 0);
        var x0 = x - piece.CatchmentRadius;
        var z0 = z - piece.CatchmentRadius;
        var x1 = xEnd + piece.CatchmentRadius;
        var z1 = zEnd + piece.CatchmentRadius;
        if (joinSlot >= 0)
        {
            x0 = Math.Min(x0, _x0[joinSlot] - radius);
            z0 = Math.Min(z0, _z0[joinSlot] - radius);
            x1 = Math.Max(x1, _x1[joinSlot] + radius);
            z1 = Math.Max(z1, _z1[joinSlot] + radius);
        }

        into.SetArea(x0, z0, x1, z1, _map.Width, _map.Depth);
        Span<int> eighths = stackalloc int[_cargoCount];
        eighths.Clear();
        ulong supplies = 0;
        var buildings = _towns?.Buildings;
        for (var tz = into.Z0; tz <= into.Z1; tz++)
        {
            for (var tx = into.X0; tx <= into.X1; tx++)
            {
                var inNew = tx >= x - piece.CatchmentRadius && tx <= xEnd + piece.CatchmentRadius
                    && tz >= z - piece.CatchmentRadius && tz <= zEnd + piece.CatchmentRadius;
                if (!inNew && (joinSlot < 0 || !InCatchmentSlot(joinSlot, tx, tz)))
                {
                    continue;
                }

                var tile = _map.Index(tx, tz);
                into.AddTile(tile);
                if (_industries.TryGetIndustryAt(tx, tz, out var industryId) && into.MarkIndustry(industryId))
                {
                    AddIndustryCargo(industryId, eighths, ref supplies);
                }

                if (buildings is not null && buildings.TryGetAt(tile, out var buildingId) && into.MarkBuilding(buildingId))
                {
                    AddBuildingCargo(buildings.SlotOf(buildingId), eighths, ref supplies);
                }
            }
        }

        ulong accepts = 0;
        for (var c = 0; c < _cargoCount; c++)
        {
            var handled = (categories & (1 << (int)_cargoCategory[c])) != 0;
            if (!handled)
            {
                supplies &= ~(1UL << c);
            }
            else if (eighths[c] >= 8)
            {
                accepts |= 1UL << c;
            }
        }

        into.Receives = supplies;
        into.Accepts = accepts;
        into.JoinStationId = join;
    }

    private void InitCoverage()
    {
        _kindAccepts = new int[_content.IndustryKindCount][];
        _kindProduces = new int[_content.IndustryKindCount][];
        for (var k = 0; k < _content.IndustryKindCount; k++)
        {
            var kind = _content.IndustryKind(k);
            _kindAccepts[k] = kind.Accepts.Select(_content.CargoIndex).Where(c => c >= 0).ToArray();
            _kindProduces[k] = kind.Production.Select(p => _content.CargoIndex(p.CargoId)).Where(c => c >= 0).ToArray();
        }

        _accept = new int[InitialCapacity * _cargoCount];
        if (_towns is not null)
        {
            _towns.Buildings.StationWatch = _buildingWatch;
        }
    }

    private void ResizeCoverage(int size)
    {
        Array.Resize(ref _covIndustries, size);
        Array.Resize(ref _covBuildings, size);
        Array.Resize(ref _accept, size * _cargoCount);
        Array.Resize(ref _acceptMask, size);
        Array.Resize(ref _supplyMask, size);
        Array.Resize(ref _dirty, size);
        Array.Resize(ref _gx0, size);
        Array.Resize(ref _gz0, size);
        Array.Resize(ref _gx1, size);
        Array.Resize(ref _gz1, size);
    }

    private void ResetCoverage(int slot)
    {
        _covIndustries[slot] = [];
        _covBuildings[slot] = [];
        Array.Clear(_accept, slot * _cargoCount, _cargoCount);
        _acceptMask[slot] = 0;
        _supplyMask[slot] = 0;
        _dirty[slot] = true;
        _gx0[slot] = 0;
        _gz0[slot] = 0;
        _gx1[slot] = -1;
        _gz1[slot] = -1;
    }

    private void MoveCoverage(int from, int to)
    {
        _covIndustries[to] = _covIndustries[from];
        _covBuildings[to] = _covBuildings[from];
        Array.Copy(_accept, from * _cargoCount, _accept, to * _cargoCount, _cargoCount);
        _acceptMask[to] = _acceptMask[from];
        _supplyMask[to] = _supplyMask[from];
        _dirty[to] = _dirty[from];
        _covIndustries[from] = null!;
        _covBuildings[from] = null!;
    }

    private bool InCatchmentSlot(int slot, int x, int z)
    {
        if (_kind[slot] == (byte)StationKind.Warehouse) // Road and rail stations (feature 015) have a catchment.
        {
            return false;
        }

        var r = _radius[slot];
        if (x < _x0[slot] - r || x > _x1[slot] + r || z < _z0[slot] - r || z > _z1[slot] + r)
        {
            return false;
        }

        var width = _map.Width;
        foreach (var tile in _tiles[slot])
        {
            var pr = Rules.Pieces[_pieces[tile].Type].CatchmentRadius;
            if (Math.Abs((tile % width) - x) <= pr && Math.Abs((tile / width) - z) <= pr)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts a road station in the grid cells its catchment touches (warehouses have none).</summary>
    private void RegisterGrid(int slot)
    {
        if (_kind[slot] == (byte)StationKind.Warehouse) // Road and rail stations (feature 015) have a catchment.
        {
            _gx1[slot] = -1;
            return;
        }

        var r = _radius[slot];
        _gx0[slot] = Math.Max(0, _x0[slot] - r) / GridCell;
        _gz0[slot] = Math.Max(0, _z0[slot] - r) / GridCell;
        _gx1[slot] = Math.Min(_map.Width - 1, _x1[slot] + r) / GridCell;
        _gz1[slot] = Math.Min(_map.Depth - 1, _z1[slot] + r) / GridCell;
        var id = _id[slot];
        for (var cz = _gz0[slot]; cz <= _gz1[slot]; cz++)
        {
            for (var cx = _gx0[slot]; cx <= _gx1[slot]; cx++)
            {
                var key = cx + (cz * 65536);
                if (!_grid.TryGetValue(key, out var list))
                {
                    _grid[key] = list = [];
                }

                list.Add(id);
            }
        }
    }

    private void UnregisterGrid(int slot)
    {
        var id = _id[slot];
        for (var cz = _gz0[slot]; cz <= _gz1[slot]; cz++)
        {
            for (var cx = _gx0[slot]; cx <= _gx1[slot]; cx++)
            {
                if (_grid.TryGetValue(cx + (cz * 65536), out var list))
                {
                    list.Remove(id);
                }
            }
        }

        _gx1[slot] = -1;
    }

    /// <summary>
    /// Brings the derived coverage up to date (research R5): industry openings and closings, town building
    /// changes and construction. Cheap when nothing changed.
    /// </summary>
    private void EnsureCoverage()
    {
        if (_count == 0)
        {
            _buildingWatch.Clear();
            return;
        }

        if (_industries.Version != _industryVersion)
        {
            _industryVersion = _industries.Version;
            RebuildIndustryCoverage();
            _coverageChanged = true;
        }

        if (_buildingWatch.Count > 0)
        {
            foreach (var tile in _buildingWatch)
            {
                MarkDirtyAround(tile % _map.Width, tile / _map.Width);
            }

            _buildingWatch.Clear();
        }

        for (var slot = 0; slot < _count; slot++)
        {
            if (_dirty[slot])
            {
                _dirty[slot] = false;
                RescanStation(slot);
                _coverageChanged = true;
            }
        }

        if (_coverageChanged)
        {
            _coverageChanged = false;
            for (var slot = 0; slot < _count; slot++)
            {
                RecomputeAcceptance(slot);
            }

            RecomputeGroupMasks();
            RebuildProducerTable();
        }
    }

    private void MarkDirtyAround(int x, int z)
    {
        if (_grid.TryGetValue((x / GridCell) + ((z / GridCell) * 65536), out var list))
        {
            foreach (var id in list)
            {
                var slot = SlotOf(id);
                if (slot >= 0 && InCatchmentSlot(slot, x, z))
                {
                    _dirty[slot] = true;
                }
            }
        }
    }

    /// <summary>Rescans a station's catchment for industries and town buildings.</summary>
    private void RescanStation(int slot)
    {
        var industries = _covIndustries[slot];
        var buildingsList = _covBuildings[slot];
        industries.Clear();
        buildingsList.Clear();
        if (_kind[slot] == (byte)StationKind.Warehouse) // Road and rail stations (feature 015) have a catchment.
        {
            return;
        }

        var r = _radius[slot];
        var x0 = Math.Max(0, _x0[slot] - r);
        var z0 = Math.Max(0, _z0[slot] - r);
        var x1 = Math.Min(_map.Width - 1, _x1[slot] + r);
        var z1 = Math.Min(_map.Depth - 1, _z1[slot] + r);
        var buildings = _towns?.Buildings;
        for (var z = z0; z <= z1; z++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (!InCatchmentSlot(slot, x, z))
                {
                    continue;
                }

                if (_industries.TryGetIndustryAt(x, z, out var industryId) && !industries.Contains(industryId))
                {
                    industries.Add(industryId);
                }

                if (buildings is not null && buildings.TryGetAt(_map.Index(x, z), out var buildingId) && !buildingsList.Contains(buildingId))
                {
                    buildingsList.Add(buildingId);
                }
            }
        }

        industries.Sort();
        buildingsList.Sort();
    }

    /// <summary>Re-derives every station's industry list from the industries' footprints and the station grid.</summary>
    private void RebuildIndustryCoverage()
    {
        for (var slot = 0; slot < _count; slot++)
        {
            _covIndustries[slot].Clear();
        }

        for (var i = 0; i < _industries.Count; i++)
        {
            if (!_industries.FootprintBounds(i, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                continue;
            }

            var id = _industries.IdAt(i);
            var cx0 = Math.Max(0, minX - 8) / GridCell;
            var cz0 = Math.Max(0, minZ - 8) / GridCell;
            var cx1 = Math.Min(_map.Width - 1, maxX + 8) / GridCell;
            var cz1 = Math.Min(_map.Depth - 1, maxZ + 8) / GridCell;
            for (var cz = cz0; cz <= cz1; cz++)
            {
                for (var cx = cx0; cx <= cx1; cx++)
                {
                    if (!_grid.TryGetValue(cx + (cz * 65536), out var list))
                    {
                        continue;
                    }

                    foreach (var stationId in list)
                    {
                        var slot = SlotOf(stationId);
                        if (slot < 0 || _covIndustries[slot].Contains(id) || !CoversRect(slot, minX, minZ, maxX - 1, maxZ - 1))
                        {
                            continue;
                        }

                        _covIndustries[slot].Add(id);
                    }
                }
            }
        }

        for (var slot = 0; slot < _count; slot++)
        {
            _covIndustries[slot].Sort();
        }
    }

    /// <summary>True when a piece's catchment square overlaps the inclusive rectangle.</summary>
    private bool CoversRect(int slot, int x0, int z0, int x1, int z1)
    {
        var width = _map.Width;
        foreach (var tile in _tiles[slot])
        {
            var r = Rules.Pieces[_pieces[tile].Type].CatchmentRadius;
            var px = tile % width;
            var pz = tile / width;
            if (px - r <= x1 && px + r >= x0 && pz - r <= z1 && pz + r >= z0)
            {
                return true;
            }
        }

        return false;
    }

    private void RecomputeAcceptance(int slot)
    {
        var row = _accept.AsSpan(slot * _cargoCount, _cargoCount);
        row.Clear();
        ulong supplies = 0;
        foreach (var id in _covIndustries[slot])
        {
            AddIndustryCargo(id, row, ref supplies);
        }

        var buildings = _towns?.Buildings;
        if (buildings is not null)
        {
            foreach (var id in _covBuildings[slot])
            {
                AddBuildingCargo(buildings.SlotOf(id), row, ref supplies);
            }
        }

        ulong accepts = 0;
        for (var c = 0; c < _cargoCount; c++)
        {
            if (!Handles(slot, c))
            {
                supplies &= ~(1UL << c);
            }
            else if (row[c] >= 8)
            {
                accepts |= 1UL << c;
            }
        }

        _acceptMask[slot] = accepts;
        _supplyMask[slot] = supplies;
    }

    private void AddIndustryCargo(int industryId, Span<int> eighths, ref ulong supplies)
    {
        var islot = _industries.SlotOf(industryId);
        if (islot < 0)
        {
            return;
        }

        var kind = _industries.KindIndexAt(islot);
        foreach (var c in _kindAccepts[kind])
        {
            eighths[c] += 8;
        }

        foreach (var c in _kindProduces[kind])
        {
            supplies |= 1UL << c;
        }
    }

    private void AddBuildingCargo(int buildingSlot, Span<int> eighths, ref ulong supplies)
    {
        if (buildingSlot < 0)
        {
            return;
        }

        var type = _towns!.Buildings.TypeDefinitionAt(buildingSlot);
        if (_goodsCargo >= 0)
        {
            eighths[_goodsCargo] += type.GoodsAcceptance;
        }

        if (_foodCargo >= 0)
        {
            eighths[_foodCargo] += type.FoodAcceptance;
        }

        if (_passengerCargo >= 0)
        {
            eighths[_passengerCargo] += Math.Min(8, type.PassengersPerMonth * 8 / Rules.PassengersFullAt);
            if (type.PassengersPerMonth > 0)
            {
                supplies |= 1UL << _passengerCargo;
            }
        }

        if (_mailCargo >= 0)
        {
            eighths[_mailCargo] += Math.Min(8, type.MailPerMonth * 8 / Rules.MailFullAt);
            if (type.MailPerMonth > 0)
            {
                supplies |= 1UL << _mailCargo;
            }
        }
    }

    private void RecomputeGroupMasks()
    {
        if (_groupAccept.Length < _groupCount)
        {
            _groupAccept = new ulong[Math.Max(_groupCount, _groupAccept.Length * 2)];
        }

        Array.Clear(_groupAccept, 0, _groupCount);
        for (var slot = 0; slot < _count; slot++)
        {
            _groupAccept[_group[slot]] |= _acceptMask[slot];
        }
    }

    /// <summary>Builds the producer table: every covered industry and building with the stations covering it.</summary>
    private void RebuildProducerTable()
    {
        var pairs = 0;
        for (var slot = 0; slot < _count; slot++)
        {
            pairs += _covIndustries[slot].Count + _covBuildings[slot].Count;
        }

        if (_pairs.Length < pairs)
        {
            _pairs = new long[Math.Max(pairs, _pairs.Length * 2)];
            _coverSlots = new int[_pairs.Length];
        }

        var n = 0;
        for (var slot = 0; slot < _count; slot++)
        {
            foreach (var id in _covIndustries[slot])
            {
                _pairs[n++] = ((long)ProducerIndustry << 58) | ((long)id << 18) | (long)slot;
            }

            foreach (var id in _covBuildings[slot])
            {
                _pairs[n++] = ((long)ProducerBuilding << 58) | ((long)id << 18) | (long)slot;
            }
        }

        Array.Sort(_pairs, 0, n);
        _industryRow.Clear();
        _prodRows = 0;
        var buildings = _towns?.Buildings;
        long previous = -1;
        for (var i = 0; i < n; i++)
        {
            var producer = _pairs[i] >> 18;
            var slot = (int)(_pairs[i] & ((1L << 18) - 1));
            if (producer != previous)
            {
                previous = producer;
                EnsureRowCapacity(_prodRows + 1);
                var row = _prodRows++;
                var kind = (byte)(producer >> 40);
                var id = (int)(producer & ((1L << 40) - 1));
                _prodKind[row] = kind;
                _prodId[row] = id;
                _prodStart[row] = i;
                _prodCount[row] = 0;
                _prodTown[row] = -1;
                _prodPassengers[row] = 0;
                _prodMail[row] = 0;
                if (kind == ProducerIndustry)
                {
                    _industryRow[id] = row;
                }
                else if (buildings is not null)
                {
                    var bslot = buildings.SlotOf(id);
                    if (bslot >= 0)
                    {
                        var type = buildings.TypeDefinitionAt(bslot);
                        _prodTown[row] = buildings.TownAt(bslot);
                        _prodPassengers[row] = type.PassengersPerMonth;
                        _prodMail[row] = type.MailPerMonth;
                    }
                }
            }

            _coverSlots[i] = slot;
            _prodCount[_prodRows - 1]++;
        }
    }

    private void EnsureRowCapacity(int needed)
    {
        if (needed <= _prodKind.Length)
        {
            return;
        }

        var size = Math.Max(needed, _prodKind.Length * 2);
        Array.Resize(ref _prodKind, size);
        Array.Resize(ref _prodId, size);
        Array.Resize(ref _prodTown, size);
        Array.Resize(ref _prodPassengers, size);
        Array.Resize(ref _prodMail, size);
        Array.Resize(ref _prodStart, size);
        Array.Resize(ref _prodCount, size);
    }
}

/// <summary>
/// What a station placement would cover, for the tool preview (FR-011): the catchment (or influence) tiles and
/// the cargo received and accepted. Reused between frames; allocation-free once grown.
/// </summary>
public sealed class CatchmentPreview
{
    private readonly List<int> _tiles = [];
    private readonly HashSet<int> _industries = [];
    private readonly HashSet<int> _buildings = [];

    /// <summary>Map tile indices inside the area.</summary>
    public IReadOnlyList<int> Tiles => _tiles;

    public int X0 { get; private set; }

    public int Z0 { get; private set; }

    public int X1 { get; private set; }

    public int Z1 { get; private set; }

    /// <summary>Cargo bits the placement would receive.</summary>
    public ulong Receives { get; internal set; }

    /// <summary>Cargo bits the placement would accept.</summary>
    public ulong Accepts { get; internal set; }

    /// <summary>Station the piece would join, −1 for a new station, −2 when refused for touching two.</summary>
    public int JoinStationId { get; internal set; } = -1;

    public void Reset()
    {
        _tiles.Clear();
        _industries.Clear();
        _buildings.Clear();
        Receives = 0;
        Accepts = 0;
        JoinStationId = -1;
        X0 = Z0 = 0;
        X1 = Z1 = -1;
    }

    internal void SetArea(int x0, int z0, int x1, int z1, int width, int depth)
    {
        X0 = Math.Max(0, x0);
        Z0 = Math.Max(0, z0);
        X1 = Math.Min(width - 1, x1);
        Z1 = Math.Min(depth - 1, z1);
    }

    internal void AddTile(int tile) => _tiles.Add(tile);

    internal bool MarkIndustry(int id) => _industries.Add(id);

    internal bool MarkBuilding(int id) => _buildings.Add(id);
}
