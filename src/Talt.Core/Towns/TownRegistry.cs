using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Simulation;

namespace Talt.Core.Towns;

/// <summary>Street layout style of a town (research R6).</summary>
public enum StreetPattern : byte
{
    Grid,
    Organic,
}

/// <summary>
/// All towns of a game with a map (feature 008, data-model.md Town, research R1): founding on the map's town
/// sites, monthly statistics, ratings and growth, and the rating rules construction asks about. Town state is
/// structure-of-arrays with the town id equal to its slot (towns are never removed); buildings are in
/// <see cref="Buildings"/>.
/// </summary>
/// <remarks>
/// State changes only at creation, in <see cref="AdvanceMonth"/>, <see cref="RecordTransported"/> and when a
/// construction plan is committed. Every town draws from its own stream forked by town id, the parallel phase
/// of the month update touches only per-town data, and growth runs sequentially in town-id order, so parallel
/// and sequential simulations are identical (research R8).
/// </remarks>
public sealed partial class TownRegistry : IStateHashable
{
    /// <summary>Most growth actions one town takes in a month (bounds the month's cost).</summary>
    public const int MaxActionsPerMonth = 64;

    private const int ParallelThreshold = 128;

    private readonly ConstructionState _state;
    private readonly MapData _map;
    private readonly DeterministicRandom _random;
    private readonly TownSettings _settings;
    private readonly Action<int> _monthPhase;

    private readonly int[] _centreX;
    private readonly int[] _centreZ;
    private readonly int[] _siteIndex;
    private readonly byte[] _pattern;
    private readonly string[] _names;
    private readonly DeterministicRandom[] _townRandom;
    private readonly int[] _population;
    private readonly int[] _buildingCount;
    private readonly int[] _passengerProduction;
    private readonly int[] _mailProduction;
    private readonly int[] _goodsAcceptance;
    private readonly int[] _foodAcceptance;
    private readonly int[] _growthPoints;
    private readonly int[] _passengersGeneratedLastMonth;
    private readonly int[] _mailGeneratedLastMonth;
    private readonly int[] _passengersTransportedThisMonth;
    private readonly int[] _mailTransportedThisMonth;
    private readonly int[] _passengersTransportedLastMonth;
    private readonly int[] _mailTransportedLastMonth;
    private readonly int[] _serviceLastMonth;
    private int[] _ratings;
    private int _companyCapacity;
    private int _year;

    private TownRegistry(ConstructionState state, TownSettings settings, DeterministicRandom random, TownRules rules, TownNames names, int townCount)
    {
        _state = state;
        _map = state.Map;
        _settings = settings;
        _random = random;
        Rules = rules;
        Names = names;
        Buildings = new TownBuildings(rules, _map.Width);
        Count = townCount;
        _centreX = new int[townCount];
        _centreZ = new int[townCount];
        _siteIndex = new int[townCount];
        _pattern = new byte[townCount];
        _names = new string[townCount];
        _townRandom = new DeterministicRandom[townCount];
        _population = new int[townCount];
        _buildingCount = new int[townCount];
        _passengerProduction = new int[townCount];
        _mailProduction = new int[townCount];
        _goodsAcceptance = new int[townCount];
        _foodAcceptance = new int[townCount];
        _growthPoints = new int[townCount];
        _passengersGeneratedLastMonth = new int[townCount];
        _mailGeneratedLastMonth = new int[townCount];
        _passengersTransportedThisMonth = new int[townCount];
        _mailTransportedThisMonth = new int[townCount];
        _passengersTransportedLastMonth = new int[townCount];
        _mailTransportedLastMonth = new int[townCount];
        _serviceLastMonth = new int[townCount];
        _companyCapacity = 4;
        _ratings = new int[townCount * _companyCapacity];
        Array.Fill(_ratings, rules.Ratings.Initial);
        _monthPhase = MonthPhase;
        _candidates = new int[rules.Buildings.Count];
    }

    public TownRules Rules { get; }

    public TownNames Names { get; }

    public TownSettings Settings => _settings;

    /// <summary>Every town building.</summary>
    public TownBuildings Buildings { get; }

    /// <summary>Number of towns; ids are <c>0..Count-1</c>.</summary>
    public int Count { get; }

    /// <summary>Bumped by every building or street change (clients cache by it). Not hashed.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Founds a town on every town site of the state's map (research R3), after the street grids of feature 007
    /// were seeded. Call <see cref="ConstructionState.RebuildNetwork"/> afterwards.
    /// </summary>
    public static TownRegistry Found(
        ConstructionState state,
        TownSettings? settings,
        DeterministicRandom random,
        int year,
        TownRules? rules = null,
        TownNames? names = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        settings ??= TownSettings.Default;
        settings.Validate();
        rules ??= TownRules.Default;
        names ??= TownNames.Default;
        var sites = new List<Site>();
        foreach (var site in state.Map.Sites)
        {
            if (site.Kind == SiteKind.Town && sites.Count <= TileOwner.MaxIndex)
            {
                sites.Add(site);
            }
        }

        var registry = new TownRegistry(state, settings, random, rules, names, sites.Count);
        registry.FoundAll(sites, year);
        return registry;
    }

    /// <summary>The registry of the map's town sites before any town is founded; a save fills it in (feature 014).</summary>
    internal static TownRegistry CreateForRestore(ConstructionState state, TownSettings? settings, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(state);
        settings ??= TownSettings.Default;
        var count = 0;
        foreach (var site in state.Map.Sites)
        {
            if (site.Kind == SiteKind.Town && count <= TileOwner.MaxIndex)
            {
                count++;
            }
        }

        return new TownRegistry(state, settings, random, TownRules.Default, TownNames.Default, count);
    }

    public string NameOf(int town) => _names[town];

    public (int X, int Z) CentreOf(int town) => (_centreX[town], _centreZ[town]);

    public StreetPattern PatternOf(int town) => (StreetPattern)_pattern[town];

    public int PopulationOf(int town) => _population[town];

    public int BuildingCountOf(int town) => _buildingCount[town];

    /// <summary>Current growth radius in tiles (research R5).</summary>
    public int RadiusOf(int town) => Rules.Growth.RadiusAt(_population[town]);

    /// <summary>Share of last month's passengers and mail that were transported, per mille.</summary>
    public int ServiceOf(int town) => _serviceLastMonth[town];

    /// <summary>Rating of a company in a town (0–1 000); companies never seen have the initial rating.</summary>
    public int RatingOf(int town, int companyId) =>
        (uint)companyId < (uint)_companyCapacity && (uint)town < (uint)Count
            ? _ratings[(town * _companyCapacity) + companyId]
            : Rules.Ratings.Initial;

    public TownRatingBand BandOf(int rating) => Rules.Ratings.BandOf(rating);

    /// <summary>True when the town lets the company demolish its buildings and remove its roads (FR-019).</summary>
    public bool MayDemolish(int town, int companyId) => RatingOf(town, companyId) >= Rules.Ratings.DemolishMinimum;

    /// <summary>True when the town lets the company build a station in it (FR-020, enforced by feature 010).</summary>
    public bool MayBuildStation(int town, int companyId) => RatingOf(town, companyId) >= Rules.Ratings.StationMinimum;

    /// <summary>True when the town accepts goods (<paramref name="food"/> false) or food from its buildings (eighths ≥ 8).</summary>
    public bool Accepts(int town, bool food) => (food ? _foodAcceptance[town] : _goodsAcceptance[town]) >= 8;

    /// <summary>Owner town of a building tile, or −1.</summary>
    public int TownAt(int x, int z)
    {
        if (!_map.Contains(x, z) || !Buildings.TryGetAt(_map.Index(x, z), out var id))
        {
            return -1;
        }

        return Buildings.TownAt(Buildings.SlotOf(id));
    }

    /// <summary>The building on a tile.</summary>
    public bool TryGetBuildingAt(int x, int z, out int buildingId)
    {
        buildingId = -1;
        return _map.Contains(x, z) && Buildings.TryGetAt(_map.Index(x, z), out buildingId);
    }

    /// <summary>The town whose zone (radius plus margin) contains the tile, nearest centre first, or −1.</summary>
    public int TownZoneAt(int x, int z)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        var margin = Rules.Ratings.ZoneMargin;
        for (var town = 0; town < Count; town++)
        {
            var distance = Math.Max(Math.Abs(x - _centreX[town]), Math.Abs(z - _centreZ[town]));
            if (distance <= RadiusOf(town) + margin && distance < bestDistance)
            {
                best = town;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Snapshot for the town window (contracts/towns-api.md).</summary>
    public bool TryGetTown(int town, out TownInfo info)
    {
        if ((uint)town >= (uint)Count)
        {
            info = default;
            return false;
        }

        info = new TownInfo(
            town,
            _names[town],
            _centreX[town],
            _centreZ[town],
            PatternOf(town),
            _population[town],
            _buildingCount[town],
            _passengersGeneratedLastMonth[town],
            _passengersTransportedLastMonth[town],
            _mailGeneratedLastMonth[town],
            _mailTransportedLastMonth[town],
            Accepts(town, food: false),
            Accepts(town, food: true),
            _serviceLastMonth[town],
            RadiusOf(town));
        return true;
    }

    /// <summary>
    /// Counts passengers (<paramref name="mail"/> false) or mail picked up in a town this month (feature 010's
    /// entry point; the debug service uses it too).
    /// </summary>
    public void RecordTransported(int town, bool mail, int amount)
    {
        if ((uint)town >= (uint)Count || amount <= 0)
        {
            return;
        }

        if (mail)
        {
            _mailTransportedThisMonth[town] = SaturatingAdd(_mailTransportedThisMonth[town], amount);
        }
        else
        {
            _passengersTransportedThisMonth[town] = SaturatingAdd(_passengersTransportedThisMonth[town], amount);
        }
    }

    /// <summary>
    /// The month update (research R7, R8): statistics, service, ratings and growth points per town (parallel
    /// when allowed), then the growth actions sequentially in town-id order.
    /// </summary>
    public void AdvanceMonth(GameDate date, bool parallel)
    {
        _year = date.Year;
        if (parallel && Count >= ParallelThreshold)
        {
            Parallel.For(0, Count, _monthPhase);
        }
        else
        {
            for (var town = 0; town < Count; town++)
            {
                MonthPhase(town);
            }
        }

        _state.Year = date.Year;
        for (var town = 0; town < Count; town++)
        {
            GrowMonth(town);
        }

        if (_networkPending)
        {
            _networkPending = false;
            _state.Network.RebuildChanged();
        }
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(Count);
        hasher.Append(Rules.Fingerprint);
        hasher.Append((int)_settings.NameStyle);
        hasher.Append((int)_settings.Size);
        hasher.Append(_settings.DebugServicePercent);
        hasher.Append<int>(_centreX);
        hasher.Append<int>(_centreZ);
        hasher.Append<int>(_siteIndex);
        hasher.Append<byte>(_pattern);
        hasher.Append<int>(_population);
        hasher.Append<int>(_buildingCount);
        hasher.Append<int>(_passengerProduction);
        hasher.Append<int>(_mailProduction);
        hasher.Append<int>(_goodsAcceptance);
        hasher.Append<int>(_foodAcceptance);
        hasher.Append<int>(_growthPoints);
        hasher.Append<int>(_passengersGeneratedLastMonth);
        hasher.Append<int>(_mailGeneratedLastMonth);
        hasher.Append<int>(_passengersTransportedThisMonth);
        hasher.Append<int>(_mailTransportedThisMonth);
        hasher.Append<int>(_passengersTransportedLastMonth);
        hasher.Append<int>(_mailTransportedLastMonth);
        hasher.Append<int>(_serviceLastMonth);
        hasher.Append(_companyCapacity);
        hasher.Append<int>(_ratings);
        foreach (var name in _names)
        {
            hasher.Append(name);
        }

        hasher.Append(_random);
        foreach (var stream in _townRandom)
        {
            hasher.Append(stream);
        }

        hasher.Append(Buildings);
    }

    /// <summary>Phase 1 of the month for one town: touches only that town's slots (safe in parallel).</summary>
    private void MonthPhase(int town)
    {
        var passengers = _passengerProduction[town];
        var mail = _mailProduction[town];
        var debug = _settings.DebugServicePercent;
        if (debug > 0)
        {
            RecordTransported(town, mail: false, (int)((long)passengers * debug / 100));
            RecordTransported(town, mail: true, (int)((long)mail * debug / 100));
        }

        _passengersGeneratedLastMonth[town] = passengers;
        _mailGeneratedLastMonth[town] = mail;
        _passengersTransportedLastMonth[town] = _passengersTransportedThisMonth[town];
        _mailTransportedLastMonth[town] = _mailTransportedThisMonth[town];
        _passengersTransportedThisMonth[town] = 0;
        _mailTransportedThisMonth[town] = 0;

        var generated = (long)passengers + mail;
        var transported = (long)_passengersTransportedLastMonth[town] + _mailTransportedLastMonth[town];
        var service = generated == 0 ? 0 : (int)Math.Min(1000, transported * 1000 / generated);
        _serviceLastMonth[town] = service;

        // Ratings move toward their target: neutral without service. Until stations exist (feature 010) all
        // transport is the player's, so only the player's target rises with service.
        var rules = Rules.Ratings;
        for (var company = 0; company < _companyCapacity; company++)
        {
            var target = rules.Initial + (company == Company.PlayerId ? service * rules.ServiceTargetSpan / 1000 : 0);
            ref var rating = ref _ratings[(town * _companyCapacity) + company];
            rating = rating < target
                ? Math.Min(target, rating + rules.RecoveryPerMonth)
                : Math.Max(target, rating - rules.RecoveryPerMonth);
        }

        if (service > 0)
        {
            var actions = 1 + (int)((long)_buildingCount[town] * Rules.Growth.GrowthPerMilleOfBuildings / 1000);
            _growthPoints[town] = (int)Math.Min(MaxActionsPerMonth * 1000L, _growthPoints[town] + ((long)actions * service));
        }
    }

    /// <summary>Changes a company's rating in a town, within the rating range.</summary>
    private void AddRating(int town, int companyId, int change)
    {
        EnsureCompany(companyId);
        ref var rating = ref _ratings[(town * _companyCapacity) + companyId];
        rating = Math.Clamp(rating + change, TownRatingRules.Min, TownRatingRules.Max);
    }

    private void EnsureCompany(int companyId)
    {
        if (companyId < _companyCapacity)
        {
            return;
        }

        var capacity = Math.Max(companyId + 1, _companyCapacity * 2);
        var ratings = new int[Count * capacity];
        Array.Fill(ratings, Rules.Ratings.Initial);
        for (var town = 0; town < Count; town++)
        {
            Array.Copy(_ratings, town * _companyCapacity, ratings, town * capacity, _companyCapacity);
        }

        _ratings = ratings;
        _companyCapacity = capacity;
    }

    private static int SaturatingAdd(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);

    /// <summary>Founds every town in site order (town id = position among the map's town sites).</summary>
    private void FoundAll(List<Site> sites, int year)
    {
        _year = year;
        var growth = Rules.Growth;
        var used = new HashSet<string>(StringComparer.Ordinal);
        var nameRandom = _random.Fork(0x4E_41_4D_45);
        for (var town = 0; town < sites.Count; town++)
        {
            var site = sites[town];
            _centreX[town] = site.X;
            _centreZ[town] = site.Z;
            _siteIndex[town] = town;
            _townRandom[town] = _random.Fork((ulong)(town + 1));
            _names[town] = Names.Generate(_settings.NameStyle, nameRandom, used);
            var random = _townRandom[town];
            _pattern[town] = (byte)(random.NextInt(100) < growth.GridPatternPercent ? StreetPattern.Grid : StreetPattern.Organic);
        }

        _founding = true;
        for (var town = 0; town < sites.Count; town++)
        {
            var target = TargetPopulation(town, sites[town]);
            var budget = growth.FoundingAttemptBudget;
            var failures = 0;
            while (_population[town] < target && budget-- > 0 && failures < 64)
            {
                if (TryGrowOnce(town))
                {
                    failures = 0;
                    continue;
                }

                failures++;
                if (!TryExtendStreet(town))
                {
                    failures += 4;
                }
            }
        }

        _founding = false;
        Buildings.DiscardChangedTiles();
    }

    private int TargetPopulation(int town, Site site)
    {
        var random = _townRandom[town];
        var total = 0;
        foreach (var band in Rules.Sizes)
        {
            total += band.Weight;
        }

        var pick = random.NextInt(total);
        var chosen = Rules.Sizes[^1];
        foreach (var band in Rules.Sizes)
        {
            if (pick < band.Weight)
            {
                chosen = band;
                break;
            }

            pick -= band.Weight;
        }

        var population = chosen.MinPopulation + random.NextInt(chosen.MaxPopulation - chosen.MinPopulation + 1);
        // Better sites (flatter, more room) grow slightly bigger towns: up to +12.5 % at score 255.
        population += population * site.Score / 2040;
        return (int)((long)population * Rules.ScalePercent(_settings.Size) / 100);
    }
}

/// <summary>Read-only snapshot of a town for the information window (contracts/towns-api.md).</summary>
public readonly record struct TownInfo(
    int Id,
    string Name,
    int CentreX,
    int CentreZ,
    StreetPattern Pattern,
    int Population,
    int Buildings,
    int PassengersGeneratedLastMonth,
    int PassengersTransportedLastMonth,
    int MailGeneratedLastMonth,
    int MailTransportedLastMonth,
    bool AcceptsGoods,
    bool AcceptsFood,
    int ServicePerMille,
    int Radius);
