using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Simulation;

namespace Talt.Core.Industries;

/// <summary>
/// All industries of a game and the rules that change them (research R3–R6). State is stored in
/// structure-of-arrays form: scalars in parallel arrays and cargo amounts in flat tables indexed
/// <c>slot * cargoCount + cargoIndex</c>. Removal swaps the last industry into the free slot, like
/// <see cref="Simulation.VehicleFleet"/>; ids are stable and never reused.
/// </summary>
/// <remarks>
/// Only <see cref="AdvanceDay"/>, <see cref="AdvanceMonth"/>, <see cref="Deliver"/> and
/// <see cref="Take"/> change state. They run on the simulation thread, are integer-only and draw from
/// the fleet's own forked <see cref="DeterministicRandom"/>, so parallel and sequential simulations
/// produce the same industries. An empty fleet contributes nothing to the state fingerprint.
/// </remarks>
public sealed class IndustryFleet : IStateHashable
{
    private const int InitialCapacity = 64;

    private readonly GameContent _content;
    private readonly int _cargoCount;
    private readonly Site[] _sites;
    private readonly bool[] _siteOccupied;
    private readonly int[] _freeSites;
    private readonly Dictionary<int, int> _tileOwner = [];
    private readonly Dictionary<int, int> _slotById = [];
    private readonly int _mapWidth;
    private readonly int _mapDepth;
    private readonly DeterministicRandom _random;

    // Precomputed per-kind tables so the hot loops resolve cargo indices only once per game.
    private readonly int[][] _productionCargo;
    private readonly int[][] _productionPerMonth;
    private readonly int[][] _recipeInputCargo;
    private readonly int[][] _recipeInputAmount;
    private readonly int[][] _recipeOutputCargo;
    private readonly int[][] _recipeOutputAmount;
    private readonly int[][] _stockCapacity;
    private readonly int[][] _recipeByCargo;
    private readonly int[][] _acceptsCargo;

    private int[] _kind = new int[InitialCapacity];
    private int[] _id = new int[InitialCapacity];
    private int[] _x = new int[InitialCapacity];
    private int[] _z = new int[InitialCapacity];
    private int[] _ratePerMille = new int[InitialCapacity];
    private int[] _monthsNeglected = new int[InitialCapacity];
    private int[] _siteIndex = new int[InitialCapacity];
    private int[] _deliveredThisMonth = new int[InitialCapacity];

    private int[] _available = new int[InitialCapacity * 1];
    private int[] _pendingInput = new int[InitialCapacity * 1];
    private int[] _acceptedThisMonth = new int[InitialCapacity * 1];
    private int[] _acceptedLastMonth = new int[InitialCapacity * 1];
    private int[] _shippedThisMonth = new int[InitialCapacity * 1];
    private int[] _shippedLastMonth = new int[InitialCapacity * 1];

    private int _count;
    private int _freeCount;
    private int _nextId = 1;
    private int _version;

    private IndustryFleet(GameContent content, Site[] sites, int mapWidth, int mapDepth, DeterministicRandom random)
    {
        _content = content;
        _cargoCount = content.CargoCount;
        _sites = sites;
        _mapWidth = mapWidth;
        _mapDepth = mapDepth;
        _random = random;
        _siteOccupied = new bool[sites.Length];
        _freeSites = new int[sites.Length];

        _productionCargo = new int[content.IndustryKindCount][];
        _productionPerMonth = new int[content.IndustryKindCount][];
        _recipeInputCargo = new int[content.IndustryKindCount][];
        _recipeInputAmount = new int[content.IndustryKindCount][];
        _recipeOutputCargo = new int[content.IndustryKindCount][];
        _recipeOutputAmount = new int[content.IndustryKindCount][];
        _stockCapacity = new int[content.IndustryKindCount][];
        _recipeByCargo = new int[content.IndustryKindCount][];
        _acceptsCargo = new int[content.IndustryKindCount][];
        BuildKindTables();

        _available = new int[InitialCapacity * _cargoCount];
        _pendingInput = new int[InitialCapacity * _cargoCount];
        _acceptedThisMonth = new int[InitialCapacity * _cargoCount];
        _acceptedLastMonth = new int[InitialCapacity * _cargoCount];
        _shippedThisMonth = new int[InitialCapacity * _cargoCount];
        _shippedLastMonth = new int[InitialCapacity * _cargoCount];
    }

    /// <summary>Content used by this fleet (cargo, kinds and economy rules).</summary>
    public GameContent Content => _content;

    /// <summary>Number of open industries. Valid slots are <c>0..Count-1</c>.</summary>
    public int Count => _count;

    /// <summary>Number of reserved industry sites of the map.</summary>
    public int SiteCount => _sites.Length;

    /// <summary>Number of sites still free for a new industry.</summary>
    public int FreeSiteCount => _freeCount;

    /// <summary>Incremented on every placement, opening and closing (clients cache by this value).</summary>
    public int Version => _version;

    public int MapWidth => _mapWidth;

    public int MapDepth => _mapDepth;

    /// <summary>Places the initial industries and returns the fleet (about 75 % of the sites).</summary>
    /// <param name="content">Validated content.</param>
    /// <param name="map">Generated map whose industry sites are used.</param>
    /// <param name="startYear">Era used for initial placement.</param>
    /// <param name="random">Industry random stream, normally forked from the simulation.</param>
    /// <param name="initialOccupancyPerMille">Override of the occupancy (0–1000); null uses the data rule.</param>
    public static IndustryFleet Create(
        GameContent content,
        MapData map,
        int startYear,
        DeterministicRandom random,
        int? initialOccupancyPerMille = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(random);
        var sites = map.IndustrySites.ToArray();
        var fleet = new IndustryFleet(content, sites, map.Width, map.Depth, random);
        fleet.PlaceInitial(startYear, initialOccupancyPerMille);
        return fleet;
    }

    /// <summary>A fleet without industries, used by map-less scenarios (replays and tests).</summary>
    public static IndustryFleet CreateEmpty(GameContent content, int mapWidth, int mapDepth)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new IndustryFleet(content, [], mapWidth, mapDepth, new DeterministicRandom(0x1D05));
    }

    public int IdAt(int slot) => _id[slot];

    public int KindIndexAt(int slot) => _kind[slot];

    public string KindIdAt(int slot) => _content.IndustryKind(_kind[slot]).Id;

    public int XAt(int slot) => _x[slot];

    public int ZAt(int slot) => _z[slot];

    public int RatePerMilleAt(int slot) => _ratePerMille[slot];

    public int MonthsNeglectedAt(int slot) => _monthsNeglected[slot];

    public int AvailableAt(int slot, int cargoIndex) => _available[Row(slot, cargoIndex)];

    public int PendingInputAt(int slot, int cargoIndex) => _pendingInput[Row(slot, cargoIndex)];

    public int AcceptedLastMonthAt(int slot, int cargoIndex) => _acceptedLastMonth[Row(slot, cargoIndex)];

    public int ShippedLastMonthAt(int slot, int cargoIndex) => _shippedLastMonth[Row(slot, cargoIndex)];

    public int StockCapacityAt(int slot, int cargoIndex) => _stockCapacity[_kind[slot]][cargoIndex];

    /// <summary>Production of one cargo this month at the current condition, before storage caps.</summary>
    public int EffectiveProductionPerMonth(int slot, int cargoIndex)
    {
        var kind = _kind[slot];
        var cargos = _productionCargo[kind];
        for (var i = 0; i < cargos.Length; i++)
        {
            if (cargos[i] == cargoIndex)
            {
                return IndustryEconomy.MonthlyAmount(_productionPerMonth[kind][i], _ratePerMille[slot]);
            }
        }

        return 0;
    }

    /// <summary>Tile bounds of an industry footprint: minimum inclusive, maximum exclusive.</summary>
    public bool FootprintBounds(int slot, out int minX, out int minZ, out int maxX, out int maxZ)
    {
        if ((uint)slot >= (uint)_count)
        {
            minX = minZ = maxX = maxZ = 0;
            return false;
        }

        var kind = _content.IndustryKind(_kind[slot]);
        (minX, minZ, maxX, maxZ) = IndustryPlacement.Bounds(_x[slot], _z[slot], kind.FootprintWidth, kind.FootprintDepth);
        return true;
    }

    /// <summary>Slot of an industry id, or -1.</summary>
    public int SlotOf(int industryId) => _slotById.TryGetValue(industryId, out var slot) ? slot : -1;

    /// <summary>Read-only snapshot for the information window; false when the industry closed.</summary>
    public bool TryGetIndustry(int industryId, out IndustryInfo info)
    {
        if (!_slotById.TryGetValue(industryId, out var slot))
        {
            info = default;
            return false;
        }

        var kindIndex = _kind[slot];
        var kind = _content.IndustryKind(kindIndex);
        var cargo = new List<(string CargoId, int Available, int PendingInput, int Capacity)>();
        var lastMonth = new List<(string CargoId, int Accepted, int Shipped)>();
        for (var c = 0; c < _cargoCount; c++)
        {
            var row = Row(slot, c);
            var capacity = _stockCapacity[kindIndex][c];
            var available = _available[row];
            var pending = _pendingInput[row];
            var accepted = _acceptedLastMonth[row];
            var shipped = _shippedLastMonth[row];
            if (capacity == 0 && available == 0 && pending == 0 && accepted == 0 && shipped == 0
                && !Produces(kindIndex, c) && !Accepts(kindIndex, c))
            {
                continue;
            }

            cargo.Add((_content.Cargo(c).Id, available, pending, capacity));
            lastMonth.Add((_content.Cargo(c).Id, accepted, shipped));
        }

        info = new IndustryInfo(
            _id[slot],
            _x[slot],
            _z[slot],
            kind.Id,
            kind.NameKey,
            _ratePerMille[slot],
            _monthsNeglected[slot],
            kind.Production,
            kind.Accepts,
            kind.Recipes,
            cargo,
            lastMonth);
        return true;
    }

    /// <summary>
    /// Tiles where no industry may open (feature 007: roads and other objects, bridges above); set by the
    /// simulation when the game has construction state. Not state: a pure query of construction state.
    /// </summary>
    public Func<int, bool>? TileBlocked { get; set; }

    /// <summary>Industry id that owns a tile, for picking and construction (feature 006).</summary>
    public bool TryGetIndustryAt(int x, int z, out int industryId)
    {
        industryId = -1;
        if ((uint)x >= (uint)_mapWidth || (uint)z >= (uint)_mapDepth)
        {
            return false;
        }

        return _tileOwner.TryGetValue(x + (z * _mapWidth), out industryId);
    }

    /// <summary>True when the tile belongs to an open industry.</summary>
    public bool IsOccupied(int x, int z) => TryGetIndustryAt(x, z, out _);

    /// <summary>Slots of the open industries (UI and tools; not for per-tick loops).</summary>
    public IEnumerable<int> OpenSlots()
    {
        for (var slot = 0; slot < _count; slot++)
        {
            yield return slot;
        }
    }

    /// <summary>Adds one day of production. Called from the simulation's day event.</summary>
    public void AdvanceDay(GameDate date)
    {
        if (_count == 0)
        {
            return;
        }

        var ended = DateOnly.FromDayNumber(date.DayNumber - 1);
        var daysInMonth = DateTime.DaysInMonth(ended.Year, ended.Month);
        var dayOfMonth = ended.Day;
        for (var slot = 0; slot < _count; slot++)
        {
            var kindIndex = _kind[slot];
            var cargos = _productionCargo[kindIndex];
            if (cargos.Length == 0)
            {
                continue;
            }

            var amounts = _productionPerMonth[kindIndex];
            var rate = _ratePerMille[slot];
            for (var i = 0; i < cargos.Length; i++)
            {
                var monthly = IndustryEconomy.MonthlyAmount(amounts[i], rate);
                if (monthly <= 0)
                {
                    continue;
                }

                var daily = IndustryEconomy.DailyAmount(monthly, daysInMonth, dayOfMonth);
                var row = Row(slot, cargos[i]);
                var capacity = _stockCapacity[kindIndex][cargos[i]];
                var free = capacity - _available[row];
                if (free > 0 && daily > 0)
                {
                    _available[row] += Math.Min(daily, free);
                }
            }
        }
    }

    /// <summary>
    /// Rotates monthly statistics and applies the lifecycle rules: drift, growth from deliveries,
    /// decline when neglected, seeded closings and openings. Called from the month event.
    /// </summary>
    public void AdvanceMonth(GameDate date)
    {
        RotateMonthlyStatistics();
        var economy = _content.Economy;
        for (var slot = _count - 1; slot >= 0; slot--)
        {
            var delivered = _deliveredThisMonth[slot];
            if (delivered > 0)
            {
                _monthsNeglected[slot] = 0;
                _ratePerMille[slot] += economy.GrowthPerMille;
            }
            else
            {
                _monthsNeglected[slot]++;
                if (_monthsNeglected[slot] >= economy.DeclineAfterMonths)
                {
                    _ratePerMille[slot] -= economy.DeclinePerMille;
                }
            }

            var drift = economy.DriftPerMille;
            _ratePerMille[slot] += drift > 0 ? _random.NextInt(-drift, drift + 1) : 0;
            _ratePerMille[slot] = IndustryEconomy.ClampRate(_ratePerMille[slot], economy.MinRatePerMille, economy.MaxRatePerMille);
            _deliveredThisMonth[slot] = 0;

            if (_monthsNeglected[slot] >= economy.CloseAfterMonths
                && _ratePerMille[slot] <= economy.CloseAtRatePerMille
                && _random.NextInt(1000) < economy.CloseChancePerMille)
            {
                RemoveIndustry(slot);
            }
        }

        OpenNewIndustries(date.Year);
    }

    /// <summary>
    /// Offers cargo to an industry (feature 010). Accepted recipe cargo is processed in whole batches;
    /// accepted cargo without a recipe is consumed. Returns what happened.
    /// </summary>
    public IndustryDeliveryResult Deliver(int industryId, int cargoIndex, int amount)
    {
        if (amount <= 0 || (uint)cargoIndex >= (uint)_cargoCount || !_slotById.TryGetValue(industryId, out var slot))
        {
            return new IndustryDeliveryResult(DeliveryOutcome.Refused, 0, Math.Max(amount, 0), 0);
        }

        var kindIndex = _kind[slot];
        if (!Accepts(kindIndex, cargoIndex))
        {
            return new IndustryDeliveryResult(DeliveryOutcome.Refused, 0, amount, 0);
        }

        var row = Row(slot, cargoIndex);
        var recipe = _recipeByCargo[kindIndex][cargoIndex];
        if (recipe < 0)
        {
            _acceptedThisMonth[row] += amount;
            _deliveredThisMonth[slot] += amount;
            return new IndustryDeliveryResult(DeliveryOutcome.Accepted, amount, 0, amount);
        }

        var capacity = _stockCapacity[kindIndex][cargoIndex];
        var accepted = Math.Min(amount, Math.Max(0, capacity - _pendingInput[row]));
        if (accepted == 0)
        {
            return new IndustryDeliveryResult(DeliveryOutcome.Refused, 0, amount, 0);
        }

        _pendingInput[row] += accepted;
        _acceptedThisMonth[row] += accepted;
        _deliveredThisMonth[slot] += accepted;
        var (convertedCargo, convertedAmount) = ProcessRecipes(slot);
        var outcome = accepted < amount ? DeliveryOutcome.PartiallyAccepted : DeliveryOutcome.Accepted;
        return new IndustryDeliveryResult(
            outcome,
            accepted,
            amount - accepted,
            0,
            convertedCargo >= 0 ? _content.Cargo(convertedCargo).Id : null,
            convertedAmount);
    }

    /// <summary>Removes up to <paramref name="amount"/> available cargo (feature 010 loading).</summary>
    public int Take(int industryId, int cargoIndex, int amount)
    {
        if (amount <= 0 || (uint)cargoIndex >= (uint)_cargoCount || !_slotById.TryGetValue(industryId, out var slot))
        {
            return 0;
        }

        var row = Row(slot, cargoIndex);
        var taken = Math.Min(amount, _available[row]);
        if (taken > 0)
        {
            _available[row] -= taken;
            _shippedThisMonth[row] += taken;
        }

        return taken;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (_count == 0)
        {
            return; // Map-less scenarios keep their pre-009 fingerprints (research R7).
        }

        hasher.Append(_count);
        hasher.Append(_nextId);
        hasher.Append(_mapWidth);
        hasher.Append(_mapDepth);
        hasher.Append<int>(_kind.AsSpan(0, _count));
        hasher.Append<int>(_id.AsSpan(0, _count));
        hasher.Append<int>(_x.AsSpan(0, _count));
        hasher.Append<int>(_z.AsSpan(0, _count));
        hasher.Append<int>(_ratePerMille.AsSpan(0, _count));
        hasher.Append<int>(_monthsNeglected.AsSpan(0, _count));
        hasher.Append<int>(_siteIndex.AsSpan(0, _count));
        hasher.Append<int>(_deliveredThisMonth.AsSpan(0, _count));

        var used = _count * _cargoCount;
        hasher.Append<int>(_available.AsSpan(0, used));
        hasher.Append<int>(_pendingInput.AsSpan(0, used));
        hasher.Append<int>(_acceptedThisMonth.AsSpan(0, used));
        hasher.Append<int>(_acceptedLastMonth.AsSpan(0, used));
        hasher.Append<int>(_shippedThisMonth.AsSpan(0, used));
        hasher.Append<int>(_shippedLastMonth.AsSpan(0, used));
        hasher.Append(_random);
        hasher.Append(_content.Fingerprint());
        hasher.Append(_freeCount);
        hasher.Append<int>(_freeSites.AsSpan(0, _freeCount));
    }

    private void PlaceInitial(int startYear, int? initialOccupancyPerMille)
    {
        var perMille = Math.Clamp(
            initialOccupancyPerMille ?? (_content.Economy.InitialOccupancyPercent * 10),
            0,
            1000);
        for (var siteIndex = 0; siteIndex < _sites.Length; siteIndex++)
        {
            if (_random.NextInt(1000) >= perMille)
            {
                continue;
            }

            var kind = IndustryPlacement.ChooseKind(_content, _sites[siteIndex], startYear, _mapWidth, _mapDepth, _random);
            if (kind >= 0)
            {
                AddIndustry(kind, siteIndex);
            }
        }

        for (var siteIndex = 0; siteIndex < _sites.Length; siteIndex++)
        {
            if (!_siteOccupied[siteIndex])
            {
                _freeSites[_freeCount++] = siteIndex;
            }
        }
    }

    private void OpenNewIndustries(int year)
    {
        var economy = _content.Economy;
        var openings = 0;
        for (var free = _freeCount - 1; free >= 0 && openings < economy.MaxOpeningsPerMonth; free--)
        {
            if (_random.NextInt(1000) >= economy.OpeningChancePerMillePerSite)
            {
                continue;
            }

            var siteIndex = _freeSites[free];
            var kind = IndustryPlacement.ChooseKind(_content, _sites[siteIndex], year, _mapWidth, _mapDepth, _random);
            if (kind < 0 || AddIndustry(kind, siteIndex) < 0)
            {
                continue;
            }

            _freeSites[free] = _freeSites[--_freeCount];
            openings++;
        }
    }

    private int AddIndustry(int kindIndex, int siteIndex)
    {
        if (_siteOccupied[siteIndex])
        {
            return -1;
        }

        var site = _sites[siteIndex];
        var kind = _content.IndustryKind(kindIndex);
        if (!IndustryPlacement.Fits(kind.FootprintWidth, kind.FootprintDepth, site.X, site.Z, _mapWidth, _mapDepth))
        {
            return -1;
        }

        var (minX, minZ, maxX, maxZ) = IndustryPlacement.Bounds(site.X, site.Z, kind.FootprintWidth, kind.FootprintDepth);
        for (var z = minZ; z < maxZ; z++)
        {
            for (var x = minX; x < maxX; x++)
            {
                if (_tileOwner.ContainsKey(x + (z * _mapWidth)) || (TileBlocked?.Invoke(x + (z * _mapWidth)) ?? false))
                {
                    return -1;
                }
            }
        }

        EnsureCapacity(_count + 1);
        var slot = _count;
        var id = _nextId++;
        ClearRow(slot);
        _kind[slot] = kindIndex;
        _id[slot] = id;
        _x[slot] = site.X;
        _z[slot] = site.Z;
        _ratePerMille[slot] = 1000;
        _monthsNeglected[slot] = 0;
        _siteIndex[slot] = siteIndex;
        _deliveredThisMonth[slot] = 0;
        for (var z = minZ; z < maxZ; z++)
        {
            for (var x = minX; x < maxX; x++)
            {
                _tileOwner[x + (z * _mapWidth)] = id;
            }
        }

        _siteOccupied[siteIndex] = true;
        _slotById.Add(id, slot);
        _count++;
        _version++;
        return slot;
    }

    private void RemoveIndustry(int slot)
    {
        var id = _id[slot];
        var siteIndex = _siteIndex[slot];
        var kind = _content.IndustryKind(_kind[slot]);
        var (minX, minZ, maxX, maxZ) = IndustryPlacement.Bounds(_x[slot], _z[slot], kind.FootprintWidth, kind.FootprintDepth);
        for (var z = minZ; z < maxZ; z++)
        {
            for (var x = minX; x < maxX; x++)
            {
                _tileOwner.Remove(x + (z * _mapWidth));
            }
        }

        _slotById.Remove(id);
        _siteOccupied[siteIndex] = false;
        _freeSites[_freeCount++] = siteIndex;

        var last = _count - 1;
        if (slot != last)
        {
            _kind[slot] = _kind[last];
            _id[slot] = _id[last];
            _x[slot] = _x[last];
            _z[slot] = _z[last];
            _ratePerMille[slot] = _ratePerMille[last];
            _monthsNeglected[slot] = _monthsNeglected[last];
            _siteIndex[slot] = _siteIndex[last];
            _deliveredThisMonth[slot] = _deliveredThisMonth[last];
            CopyRow(slot, last);
            _slotById[_id[slot]] = slot;
        }

        _count = last;
        _version++;
    }

    private (int CargoIndex, int Amount) ProcessRecipes(int slot)
    {
        var kindIndex = _kind[slot];
        var inputs = _recipeInputCargo[kindIndex];
        var convertedCargo = -1;
        var convertedAmount = 0;
        for (var recipe = 0; recipe < inputs.Length; recipe++)
        {
            var input = inputs[recipe];
            var inputRow = Row(slot, input);
            var pending = _pendingInput[inputRow];
            var batch = _recipeInputAmount[kindIndex][recipe];
            if (pending < batch)
            {
                continue;
            }

            var output = _recipeOutputCargo[kindIndex][recipe];
            var outputRow = Row(slot, output);
            var free = _stockCapacity[kindIndex][output] - _available[outputRow];
            if (free <= 0)
            {
                continue;
            }

            var outputPerBatch = _recipeOutputAmount[kindIndex][recipe];
            var batches = Math.Min(pending / batch, free / outputPerBatch);
            if (batches <= 0)
            {
                continue;
            }

            _pendingInput[inputRow] -= batches * batch;
            _available[outputRow] += batches * outputPerBatch;
            convertedCargo = output;
            convertedAmount += batches * outputPerBatch;
        }

        return (convertedCargo, convertedAmount);
    }

    private void RotateMonthlyStatistics()
    {
        var used = _count * _cargoCount;
        Array.Copy(_acceptedThisMonth, _acceptedLastMonth, used);
        Array.Clear(_acceptedThisMonth, 0, used);
        Array.Copy(_shippedThisMonth, _shippedLastMonth, used);
        Array.Clear(_shippedThisMonth, 0, used);
    }

    private bool Accepts(int kindIndex, int cargoIndex)
    {
        var accepts = _acceptsCargo[kindIndex];
        for (var i = 0; i < accepts.Length; i++)
        {
            if (accepts[i] == cargoIndex)
            {
                return true;
            }
        }

        return false;
    }

    private bool Produces(int kindIndex, int cargoIndex)
    {
        var cargos = _productionCargo[kindIndex];
        for (var i = 0; i < cargos.Length; i++)
        {
            if (cargos[i] == cargoIndex)
            {
                return true;
            }
        }

        return false;
    }

    private int Row(int slot, int cargoIndex) => (slot * _cargoCount) + cargoIndex;

    private void ClearRow(int slot)
    {
        var start = slot * _cargoCount;
        Array.Clear(_available, start, _cargoCount);
        Array.Clear(_pendingInput, start, _cargoCount);
        Array.Clear(_acceptedThisMonth, start, _cargoCount);
        Array.Clear(_acceptedLastMonth, start, _cargoCount);
        Array.Clear(_shippedThisMonth, start, _cargoCount);
        Array.Clear(_shippedLastMonth, start, _cargoCount);
    }

    private void CopyRow(int destination, int source)
    {
        var destinationStart = destination * _cargoCount;
        var sourceStart = source * _cargoCount;
        Array.Copy(_available, sourceStart, _available, destinationStart, _cargoCount);
        Array.Copy(_pendingInput, sourceStart, _pendingInput, destinationStart, _cargoCount);
        Array.Copy(_acceptedThisMonth, sourceStart, _acceptedThisMonth, destinationStart, _cargoCount);
        Array.Copy(_acceptedLastMonth, sourceStart, _acceptedLastMonth, destinationStart, _cargoCount);
        Array.Copy(_shippedThisMonth, sourceStart, _shippedThisMonth, destinationStart, _cargoCount);
        Array.Copy(_shippedLastMonth, sourceStart, _shippedLastMonth, destinationStart, _cargoCount);
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _kind.Length)
        {
            return;
        }

        var size = Math.Max(needed, _kind.Length * 2);
        Array.Resize(ref _kind, size);
        Array.Resize(ref _id, size);
        Array.Resize(ref _x, size);
        Array.Resize(ref _z, size);
        Array.Resize(ref _ratePerMille, size);
        Array.Resize(ref _monthsNeglected, size);
        Array.Resize(ref _siteIndex, size);
        Array.Resize(ref _deliveredThisMonth, size);
        var cargoSize = size * _cargoCount;
        Array.Resize(ref _available, cargoSize);
        Array.Resize(ref _pendingInput, cargoSize);
        Array.Resize(ref _acceptedThisMonth, cargoSize);
        Array.Resize(ref _acceptedLastMonth, cargoSize);
        Array.Resize(ref _shippedThisMonth, cargoSize);
        Array.Resize(ref _shippedLastMonth, cargoSize);
    }

    private void BuildKindTables()
    {
        for (var kindIndex = 0; kindIndex < _content.IndustryKindCount; kindIndex++)
        {
            var kind = _content.IndustryKind(kindIndex);
            var productionCargo = new int[kind.Production.Count];
            var productionAmount = new int[kind.Production.Count];
            for (var i = 0; i < kind.Production.Count; i++)
            {
                productionCargo[i] = RequireCargo(kind.Production[i].CargoId);
                productionAmount[i] = kind.Production[i].PerMonth;
            }

            _productionCargo[kindIndex] = productionCargo;
            _productionPerMonth[kindIndex] = productionAmount;

            var recipeInput = new int[kind.Recipes.Count];
            var recipeInputAmount = new int[kind.Recipes.Count];
            var recipeOutput = new int[kind.Recipes.Count];
            var recipeOutputAmount = new int[kind.Recipes.Count];
            var recipeByCargo = new int[_cargoCount];
            Array.Fill(recipeByCargo, -1);
            for (var i = 0; i < kind.Recipes.Count; i++)
            {
                recipeInput[i] = RequireCargo(kind.Recipes[i].InputCargoId);
                recipeInputAmount[i] = kind.Recipes[i].InputAmount;
                recipeOutput[i] = RequireCargo(kind.Recipes[i].OutputCargoId);
                recipeOutputAmount[i] = kind.Recipes[i].OutputAmount;
                recipeByCargo[recipeInput[i]] = i;
            }

            _recipeInputCargo[kindIndex] = recipeInput;
            _recipeInputAmount[kindIndex] = recipeInputAmount;
            _recipeOutputCargo[kindIndex] = recipeOutput;
            _recipeOutputAmount[kindIndex] = recipeOutputAmount;
            _recipeByCargo[kindIndex] = recipeByCargo;

            var stock = new int[_cargoCount];
            for (var i = 0; i < kind.Stock.Count; i++)
            {
                stock[RequireCargo(kind.Stock[i].CargoId)] = kind.Stock[i].Capacity;
            }

            _stockCapacity[kindIndex] = stock;

            var accepts = new int[kind.Accepts.Count];
            for (var i = 0; i < kind.Accepts.Count; i++)
            {
                accepts[i] = RequireCargo(kind.Accepts[i]);
            }

            _acceptsCargo[kindIndex] = accepts;
        }
    }

    private int RequireCargo(string id)
    {
        var index = _content.CargoIndex(id);
        return index >= 0
            ? index
            : throw new InvalidOperationException($"Cargo '{id}' was validated but is missing from the content.");
    }
}
