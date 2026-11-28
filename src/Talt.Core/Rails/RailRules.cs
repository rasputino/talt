using System.Text.Json;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Towns;

namespace Talt.Core.Rails;

/// <summary>A track type (contracts/data-files.md trackTypes).</summary>
public sealed record RailType(string Id, string NameKey, long BuildCost, long RemoveCost, int SpeedKmh, bool Electrified, int Color, long MaintenancePerTile, int FirstYear);

/// <summary>Locomotive or wagon.</summary>
public enum RollingStockKind : byte
{
    Locomotive = 0,
    Wagon = 1,
}

/// <summary>How a locomotive is powered (electric ones need electrified track, FR-022).</summary>
public enum RailPower : byte
{
    None = 0,
    Steam = 1,
    Diesel = 2,
    Electric = 3,
}

/// <summary>Signal kinds (clarification Q2), stored in the low two bits of a tile's signal byte.</summary>
public enum SignalKind : byte
{
    None = 0,
    Block = 1,
    Path = 2,
    OneWayPath = 3,
}

/// <summary>A locomotive or wagon model (data-model.md Rolling stock model). Speeds and lengths in simulation units.</summary>
public sealed record RollingStockModel(
    string Id,
    string NameKey,
    RollingStockKind Kind,
    RailPower Power,
    string Category,
    ulong CargoMask,
    int Capacity,
    int PowerKw,
    int TractiveEffortKn,
    int MassTonnes,
    int Length,
    int SpeedKmh,
    int Speed,
    long Price,
    long RunningCostPerYear,
    int LifeYears,
    int Reliability,
    int FirstYear,
    int LastYear,
    int LoadPerStep,
    int Color,
    string SoundId)
{
    public bool AvailableIn(int year) => year >= FirstYear && year <= LastYear;

    public bool Carries(int cargo) => (uint)cargo < 64 && (CargoMask & (1UL << cargo)) != 0;

    /// <summary>The first cargo it can carry, or −1 when it carries nothing.</summary>
    public int DefaultCargo => CargoMask == 0 ? -1 : System.Numerics.BitOperations.TrailingZeroCount(CargoMask);
}

/// <summary>
/// Rail balancing from <c>rail.json</c> (feature 015, contracts/data-files.md): track types, signals, bridges, tunnels,
/// depots, stations, physics, timing, routing and rolling stock. Validated at load; errors name the file and field
/// (constitution VII). Lengths are converted to simulation units, speeds to units per tick with the road vehicles' scale.
/// </summary>
public sealed class RailRules
{
    public const string FileName = "rail.json";

    /// <summary>Spans and tunnel lengths are stored in a byte.</summary>
    public const int HardMaxSpan = 250;

    /// <summary>Speed values are stored ×256 so slow accelerations are not lost (sub-units per tick).</summary>
    public const int SpeedScale = 256;

    private static readonly Lazy<RailRules> LazyDefault = new(() => FromJson(ReadResource(), GameContent.Default));

    private RailRules()
    {
    }

    public static RailRules Default => LazyDefault.Value;

    public IReadOnlyList<RailType> Types { get; private init; } = [];

    public long ConvertCostPerTile { get; private init; }

    public long SlopeCost { get; private init; }

    public long BlockSignalCost { get; private init; }

    public long PathSignalCost { get; private init; }

    public long OneWayPathSignalCost { get; private init; }

    public long SignalRemoveCost { get; private init; }

    public long BridgeCostPerTile { get; private init; }

    public int BridgeMaxSpan { get; private init; }

    public long TunnelCostPerTile { get; private init; }

    public int TunnelMaxLength { get; private init; }

    public long DepotCost { get; private init; }

    public long DepotRemoveCost { get; private init; }

    public long DepotMaintenancePerYear { get; private init; }

    public int MaxPlatforms { get; private init; }

    public int MaxPlatformLength { get; private init; }

    public int TileMillimetres { get; private init; }

    public int GravityMilli { get; private init; }

    public int RampGradePermille { get; private init; }

    public int RollingResistancePermille { get; private init; }

    public int AccelerationScale { get; private init; }

    /// <summary>Braking deceleration in sub-units per tick² (×<see cref="SpeedScale"/>).</summary>
    public int Braking { get; private init; }

    public int CurveSpeedPercent { get; private init; }

    /// <summary>Extra look-ahead beyond the braking distance, units.</summary>
    public int LookAhead { get; private init; }

    public int MinDwellTicks { get; private init; }

    public int LoadIntervalTicks { get; private init; }

    public int PullForwardTicks { get; private init; }

    public int SearchNodesPerTick { get; private init; }

    public int MaxSearchNodes { get; private init; }

    public int ReplanTicks { get; private init; }

    public int OccupiedPenalty { get; private init; }

    public int StuckNoticeDays { get; private init; }

    public int MaxParts { get; private init; }

    /// <summary>Longest train, units.</summary>
    public int MaxTrainLength { get; private init; }

    /// <summary>km/h that make one tile per second (shared with road vehicles).</summary>
    public int KmhPerTileSecond { get; private init; }

    public IReadOnlyList<RollingStockModel> Models { get; private init; } = [];

    public int FindType(string id)
    {
        for (var i = 0; i < Types.Count; i++)
        {
            if (string.Equals(Types[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public int FindModel(string id)
    {
        for (var i = 0; i < Models.Count; i++)
        {
            if (string.Equals(Models[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The first electrified type, or −1.</summary>
    public int ElectrifiedType()
    {
        for (var i = 0; i < Types.Count; i++)
        {
            if (Types[i].Electrified)
            {
                return i;
            }
        }

        return -1;
    }

    public long SignalCost(SignalKind kind) => kind switch
    {
        SignalKind.Block => BlockSignalCost,
        SignalKind.Path => PathSignalCost,
        SignalKind.OneWayPath => OneWayPathSignalCost,
        _ => 0,
    };

    /// <summary>Units per tick (not scaled) for a speed in km/h.</summary>
    public int SpeedFromKmh(int kmh) => (int)((long)kmh * FixedMath.UnitsPerTile / ((long)KmhPerTileSecond * FixedMath.TicksPerSecond));

    /// <summary>km/h for a speed in units per tick (not scaled).</summary>
    public int KmhFromSpeed(int speed) => (int)((long)speed * KmhPerTileSecond * FixedMath.TicksPerSecond / FixedMath.UnitsPerTile);

    public static string ReadResource()
    {
        using var stream = typeof(RailRules).Assembly.GetManifestResourceStream("Talt.Core.Rails.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses and validates the rail data; errors name the file and field.</summary>
    public static RailRules FromJson(string json, GameContent content, int kmhPerTileSecond = 40)
    {
        ArgumentNullException.ThrowIfNull(content);
        var j = new TownJson(FileName);
        using var document = j.Parse(json);
        var root = document.RootElement;
        j.Known(root, "", "trackTypes", "convertCostPerTile", "slopeCost", "signals", "bridge", "tunnel", "depot", "maintenancePerTile",
            "station", "physics", "stops", "routing", "stuck", "maxParts", "maxTrainLengthMilliTiles", "cargoCategories", "rollingStock");

        var maintenance = j.Object(root, "maintenancePerTile", "");
        var types = new List<RailType>();
        var n = 0;
        foreach (var e in j.ArrayOf(root, "trackTypes", "").EnumerateArray())
        {
            var p = $"trackTypes[{n++}].";
            j.Known(e, p, "id", "nameKey", "buildCost", "removeCost", "speedKmh", "electrified", "color", "firstYear");
            var id = j.String(e, "id", p);
            if (types.Exists(t => t.Id == id))
            {
                throw j.Invalid($"'{p}id' repeats '{id}'");
            }

            if (!e.TryGetProperty("electrified", out var el) || (el.ValueKind != JsonValueKind.True && el.ValueKind != JsonValueKind.False))
            {
                throw j.Invalid($"'{p}electrified' must be true or false");
            }

            types.Add(new RailType(id, j.String(e, "nameKey", p), j.Int(e, "buildCost", p, 0, 10_000_000), j.Int(e, "removeCost", p, 0, 10_000_000),
                j.Int(e, "speedKmh", p, 10, 1000), el.GetBoolean(), j.Color(e, "color", p), j.Int(maintenance, id, "maintenancePerTile.", 0, 10_000_000), j.Int(e, "firstYear", p, 1, 9000)));
        }

        if (types.Count > 250)
        {
            throw j.Invalid("'trackTypes' may have at most 250 entries");
        }

        if (types[0].Electrified)
        {
            throw j.Invalid("'trackTypes[0]' must be a non-electrified type");
        }

        var signals = j.Object(root, "signals", "");
        j.Known(signals, "signals.", "block", "path", "oneWayPath", "removeCost");
        var bridge = j.Object(root, "bridge", "");
        j.Known(bridge, "bridge.", "costPerTile", "maxSpan");
        var tunnel = j.Object(root, "tunnel", "");
        j.Known(tunnel, "tunnel.", "costPerTile", "maxLength");
        var depot = j.Object(root, "depot", "");
        j.Known(depot, "depot.", "cost", "removeCost", "maintenancePerYear");
        var station = j.Object(root, "station", "");
        j.Known(station, "station.", "maxPlatforms", "maxLength");
        var physics = j.Object(root, "physics", "");
        j.Known(physics, "physics.", "tileMillimetres", "gravityMilli", "rampGradePermille", "rollingResistancePermille", "accelerationScale",
            "brakingMilliTilesPerSecond2", "curveSpeedPercent", "lookAheadMilliTiles");
        var stops = j.Object(root, "stops", "");
        j.Known(stops, "stops.", "minDwellTicks", "loadIntervalTicks", "pullForwardTicks");
        var routing = j.Object(root, "routing", "");
        j.Known(routing, "routing.", "searchNodesPerTick", "maxSearchNodes", "replanTicks", "occupiedPenalty");
        var stuck = j.Object(root, "stuck", "");
        j.Known(stuck, "stuck.", "noticeDays");

        var categories = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var property in j.Object(root, "cargoCategories", "").EnumerateObject())
        {
            var path = "cargoCategories." + property.Name;
            if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
            {
                throw j.Invalid($"'{path}' must be a non-empty array");
            }

            var mask = 0UL;
            foreach (var cargo in property.Value.EnumerateArray())
            {
                var index = content.CargoIndex(cargo.GetString() ?? "");
                if (index < 0 || index >= 64)
                {
                    throw j.Invalid($"'{path}' names unknown cargo '{cargo.GetString()}'");
                }

                mask |= 1UL << index;
            }

            categories[property.Name] = mask;
        }

        var models = new List<RollingStockModel>();
        n = 0;
        foreach (var e in j.ArrayOf(root, "rollingStock", "").EnumerateArray())
        {
            var p = $"rollingStock[{n++}].";
            j.Known(e, p, "id", "nameKey", "kind", "power", "powerKw", "tractiveEffortKn", "massTonnes", "lengthMilliTiles", "speedKmh",
                "category", "capacity", "loadPerStep", "price", "runningCostPerYear", "lifeYears", "reliabilityPerMille", "firstYear", "lastYear", "color", "soundId");
            var id = j.String(e, "id", p);
            if (models.Exists(m => m.Id == id))
            {
                throw j.Invalid($"'{p}id' repeats '{id}'");
            }

            p = $"rollingStock[{id}].";
            var kind = j.String(e, "kind", p) switch
            {
                "locomotive" => RollingStockKind.Locomotive,
                "wagon" => RollingStockKind.Wagon,
                var other => throw j.Invalid($"'{p}kind' must be locomotive or wagon (was '{other}')"),
            };
            var power = RailPower.None;
            int powerKw = 0, effort = 0;
            if (kind == RollingStockKind.Locomotive)
            {
                power = j.String(e, "power", p) switch
                {
                    "steam" => RailPower.Steam,
                    "diesel" => RailPower.Diesel,
                    "electric" => RailPower.Electric,
                    var other => throw j.Invalid($"'{p}power' must be steam, diesel or electric (was '{other}')"),
                };
                powerKw = j.Int(e, "powerKw", p, 1, 100_000);
                effort = j.Int(e, "tractiveEffortKn", p, 1, 10_000);
            }
            else if (e.TryGetProperty("power", out _) || e.TryGetProperty("powerKw", out _))
            {
                throw j.Invalid($"'{p}': wagons have no power");
            }

            var category = "";
            ulong cargoMask = 0;
            int capacity = 0, loadPerStep = 0;
            if (kind == RollingStockKind.Wagon || e.TryGetProperty("category", out _))
            {
                category = j.String(e, "category", p);
                if (!categories.TryGetValue(category, out cargoMask))
                {
                    throw j.Invalid($"'{p}category' names unknown category '{category}'");
                }

                capacity = j.Int(e, "capacity", p, 1, 10_000);
                loadPerStep = j.Int(e, "loadPerStep", p, 1, 10_000);
            }

            var first = j.Int(e, "firstYear", p, 1, 9000);
            var last = j.Int(e, "lastYear", p, 1, 9000);
            if (first > last)
            {
                throw j.Invalid($"'{p}firstYear' is after lastYear");
            }

            var speedKmh = j.Int(e, "speedKmh", p, 10, 1000);
            models.Add(new RollingStockModel(id, j.String(e, "nameKey", p), kind, power, category, cargoMask, capacity, powerKw, effort,
                j.Int(e, "massTonnes", p, 1, 10_000), MilliTiles(j.Int(e, "lengthMilliTiles", p, 250, 3000)), speedKmh,
                (int)((long)speedKmh * FixedMath.UnitsPerTile / ((long)kmhPerTileSecond * FixedMath.TicksPerSecond)),
                j.Int(e, "price", p, 0, 100_000_000), j.Int(e, "runningCostPerYear", p, 0, 100_000_000), j.Int(e, "lifeYears", p, 1, 200),
                j.Int(e, "reliabilityPerMille", p, 1, 1000), first, last, loadPerStep, j.Color(e, "color", p), j.String(e, "soundId", p)));
        }

        if (!models.Exists(m => m.Kind == RollingStockKind.Locomotive && m.Power != RailPower.Electric && m.FirstYear <= 1850))
        {
            throw j.Invalid("'rollingStock' needs a non-electric locomotive available from 1850");
        }

        var tile = FixedMath.UnitsPerTile;
        return new RailRules
        {
            Types = types,
            ConvertCostPerTile = j.Int(root, "convertCostPerTile", "", 0, 10_000_000),
            SlopeCost = j.Int(root, "slopeCost", "", 0, 10_000_000),
            BlockSignalCost = j.Int(signals, "block", "signals.", 0, 10_000_000),
            PathSignalCost = j.Int(signals, "path", "signals.", 0, 10_000_000),
            OneWayPathSignalCost = j.Int(signals, "oneWayPath", "signals.", 0, 10_000_000),
            SignalRemoveCost = j.Int(signals, "removeCost", "signals.", 0, 10_000_000),
            BridgeCostPerTile = j.Int(bridge, "costPerTile", "bridge.", 0, 10_000_000),
            BridgeMaxSpan = j.Int(bridge, "maxSpan", "bridge.", 2, HardMaxSpan),
            TunnelCostPerTile = j.Int(tunnel, "costPerTile", "tunnel.", 0, 10_000_000),
            TunnelMaxLength = j.Int(tunnel, "maxLength", "tunnel.", 2, HardMaxSpan),
            DepotCost = j.Int(depot, "cost", "depot.", 0, 10_000_000),
            DepotRemoveCost = j.Int(depot, "removeCost", "depot.", 0, 10_000_000),
            DepotMaintenancePerYear = j.Int(depot, "maintenancePerYear", "depot.", 0, 10_000_000),
            MaxPlatforms = j.Int(station, "maxPlatforms", "station.", 1, 32),
            MaxPlatformLength = j.Int(station, "maxLength", "station.", 1, 64),
            TileMillimetres = j.Int(physics, "tileMillimetres", "physics.", 1000, 1_000_000),
            GravityMilli = j.Int(physics, "gravityMilli", "physics.", 0, 100_000),
            RampGradePermille = j.Int(physics, "rampGradePermille", "physics.", 0, 500),
            RollingResistancePermille = j.Int(physics, "rollingResistancePermille", "physics.", 0, 200),
            AccelerationScale = j.Int(physics, "accelerationScale", "physics.", 1, 1000),
            Braking = (int)((long)j.Int(physics, "brakingMilliTilesPerSecond2", "physics.", 50, 50_000) * tile * SpeedScale
                / (1000L * FixedMath.TicksPerSecond * FixedMath.TicksPerSecond)),
            CurveSpeedPercent = j.Int(physics, "curveSpeedPercent", "physics.", 10, 100),
            LookAhead = MilliTiles(j.Int(physics, "lookAheadMilliTiles", "physics.", 0, 100_000)),
            MinDwellTicks = j.Int(stops, "minDwellTicks", "stops.", 0, 100_000),
            LoadIntervalTicks = j.Int(stops, "loadIntervalTicks", "stops.", 1, 100_000),
            PullForwardTicks = j.Int(stops, "pullForwardTicks", "stops.", 0, 100_000),
            SearchNodesPerTick = j.Int(routing, "searchNodesPerTick", "routing.", 100, 100_000_000),
            MaxSearchNodes = j.Int(routing, "maxSearchNodes", "routing.", 100, 100_000_000),
            ReplanTicks = j.Int(routing, "replanTicks", "routing.", 1, 100_000),
            OccupiedPenalty = j.Int(routing, "occupiedPenalty", "routing.", 0, 100_000_000),
            StuckNoticeDays = j.Int(stuck, "noticeDays", "stuck.", 1, 1000),
            MaxParts = j.Int(root, "maxParts", "", 1, 64),
            MaxTrainLength = MilliTiles(j.Int(root, "maxTrainLengthMilliTiles", "", 1000, 200_000)),
            KmhPerTileSecond = kmhPerTileSecond,
            Models = models,
        };
    }

    private static int MilliTiles(int milli) => (int)((long)milli * FixedMath.UnitsPerTile / 1000);
}
