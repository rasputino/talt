using System.Text.Json;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Core.Vehicles;

/// <summary>What drives a vehicle model; only the breakdown effect differs (clarification Q2).</summary>
public enum VehiclePower : byte
{
    Combustion = 0,
    Electric = 1,
}

/// <summary>
/// One road vehicle model from <c>vehicles.json</c> (data-model.md VehicleModel). Distances and speeds are already
/// converted to simulation units (1 tile = 65 536 units, 20 ticks per second).
/// </summary>
public sealed record VehicleModel(
    string Id,
    string NameKey,
    StationPieceKind Kind,
    string CargoCategory,
    ulong CargoMask,
    int Capacity,
    int SpeedKmh,
    int Speed,
    int Acceleration,
    int Length,
    long Price,
    long RunningCostPerYear,
    int LifeYears,
    int Reliability,
    int FirstYear,
    int LastYear,
    VehiclePower Power,
    int LoadPerStep,
    int Color)
{
    /// <summary>True when the model is offered in <paramref name="year"/>.</summary>
    public bool AvailableIn(int year) => year >= FirstYear && year <= LastYear;

    /// <summary>True when the model can carry the cargo (index of the game content).</summary>
    public bool Carries(int cargo) => (uint)cargo < 64 && (CargoMask & (1UL << cargo)) != 0;

    /// <summary>The first cargo the model can carry (its default choice).</summary>
    public int DefaultCargo => System.Numerics.BitOperations.TrailingZeroCount(CargoMask);
}

/// <summary>
/// Validated road vehicle balancing from <c>vehicles.json</c> (feature 012, contracts/data-files.md, research R10):
/// physics, stop timing, routing budget, reliability, value, depot costs and the starter models. Immutable;
/// <see cref="Fingerprint"/> is hashed with the vehicle state.
/// </summary>
public sealed class VehicleRules
{
    private const string FileName = "vehicles.json";
    private static readonly Lazy<VehicleRules> LazyDefault = new(() => FromJson(ReadResource(), GameContent.Default));

    private VehicleRules() => Models = [];

    /// <summary>The game's rules from the embedded data file.</summary>
    public static VehicleRules Default => LazyDefault.Value;

    public IReadOnlyList<VehicleModel> Models { get; private init; }

    /// <summary>km/h that make one tile per second.</summary>
    public int KmhPerTileSecond { get; private init; }

    /// <summary>Braking deceleration, units per tick².</summary>
    public int Braking { get; private init; }

    /// <summary>Smallest gap between vehicles in a lane, units.</summary>
    public int MinGap { get; private init; }

    /// <summary>Half the size of a junction box, units.</summary>
    public int JunctionRadius { get; private init; }

    /// <summary>How far past the centre of a dead end vehicles drive before turning, units.</summary>
    public int DeadEndExtent { get; private init; }

    /// <summary>Look-ahead beyond braking distance and two ticks of travel, units.</summary>
    public int LookAhead { get; private init; }

    public int CurveSpeedPercent { get; private init; }

    public int RampSpeedPercent { get; private init; }

    public int BayPlaces { get; private init; }

    public int MinDwellTicks { get; private init; }

    public int LoadIntervalTicks { get; private init; }

    /// <summary>Route search work allowed per tick, in expanded nodes.</summary>
    public int SearchNodesPerTick { get; private init; }

    public int StuckNoticeDays { get; private init; }

    public int ReliabilityDecayPerDay { get; private init; }

    public int OldAgeFactor { get; private init; }

    public int MinReliability { get; private init; }

    public int BreakdownChanceScale { get; private init; }

    public int BreakdownDays { get; private init; }

    public int BreakdownPulseTicks { get; private init; }

    public int RecoverPerMille { get; private init; }

    public int DefaultServiceIntervalDays { get; private init; }

    public int MinValuePercent { get; private init; }

    public long DepotCost { get; private init; }

    public long DepotRemoveCost { get; private init; }

    public long DepotMaintenancePerYear { get; private init; }

    /// <summary>Placeholder colours of depots (presentation only, not fingerprinted).</summary>
    public int DepotBaseColor { get; private init; }

    public int DepotRoofColor { get; private init; }

    private ulong? _fingerprint;

    /// <summary>Hash of every balancing value (part of the vehicle state hash).</summary>
    public ulong Fingerprint => _fingerprint ??= ComputeFingerprint();

    /// <summary>Index of a model id, or −1.</summary>
    public int FindModel(string id)
    {
        for (var i = 0; i < Models.Count; i++)
        {
            if (Models[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Converts km/h into units per tick with this file's scale.</summary>
    public int SpeedFromKmh(int kmh) => (int)((long)kmh * FixedMath.UnitsPerTile / ((long)KmhPerTileSecond * FixedMath.TicksPerSecond));

    /// <summary>Converts units per tick into km/h (rounded down).</summary>
    public int KmhFromSpeed(int speed) => (int)((long)speed * KmhPerTileSecond * FixedMath.TicksPerSecond / FixedMath.UnitsPerTile);

    /// <summary>The embedded <c>vehicles.json</c> text.</summary>
    public static string ReadResource()
    {
        using var stream = typeof(VehicleRules).Assembly.GetManifestResourceStream("Talt.Core.Vehicles.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses and validates the vehicle data (constitution VII); errors name the file, model and field.</summary>
    public static VehicleRules FromJson(string json, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var j = new TownJson(FileName);
        using var document = j.Parse(json);
        var root = document.RootElement;
        j.Known(root, "", "kmhPerTileSecond", "physics", "stops", "routing", "stuck", "reliability", "service", "value", "depot", "cargoCategories", "models");
        var kmhPerTile = j.Int(root, "kmhPerTileSecond", "", 1, 1000);

        var physics = j.Object(root, "physics", "");
        j.Known(physics, "physics.", "brakingMilliTilesPerSecond2", "minGapMilliTiles", "junctionRadiusMilliTiles", "deadEndExtentMilliTiles",
            "lookAheadMilliTiles", "curveSpeedPercent", "rampSpeedPercent");
        var braking = Acceleration(j.Int(physics, "brakingMilliTilesPerSecond2", "physics.", 100, 20_000));
        var minGap = MilliTiles(j.Int(physics, "minGapMilliTiles", "physics.", 10, 300));
        var junctionRadius = MilliTiles(j.Int(physics, "junctionRadiusMilliTiles", "physics.", 100, 400));
        var deadEnd = MilliTiles(j.Int(physics, "deadEndExtentMilliTiles", "physics.", 100, 490));
        var lookAhead = MilliTiles(j.Int(physics, "lookAheadMilliTiles", "physics.", 100, 8000));
        var curve = j.Int(physics, "curveSpeedPercent", "physics.", 10, 100);
        var ramp = j.Int(physics, "rampSpeedPercent", "physics.", 10, 100);

        var stops = j.Object(root, "stops", "");
        j.Known(stops, "stops.", "bayPlaces", "minDwellTicks", "loadIntervalTicks");
        var routing = j.Object(root, "routing", "");
        j.Known(routing, "routing.", "searchNodesPerTick");
        var stuck = j.Object(root, "stuck", "");
        j.Known(stuck, "stuck.", "noticeDays");
        var reliability = j.Object(root, "reliability", "");
        j.Known(reliability, "reliability.", "decayPerDay", "oldAgeFactor", "minimum", "breakdownChanceScale", "breakdownDays", "breakdownPulseTicks", "recoverPerMille");
        var service = j.Object(root, "service", "");
        j.Known(service, "service.", "defaultIntervalDays");
        var value = j.Object(root, "value", "");
        j.Known(value, "value.", "minValuePercent");
        var depot = j.Object(root, "depot", "");
        j.Known(depot, "depot.", "cost", "removeCost", "maintenancePerYear", "model");
        var depotModel = j.Object(depot, "model", "depot.");
        j.Known(depotModel, "depot.model.", "base", "roof");

        // Truck categories: every freight cargo and mail in exactly one category (research R10).
        var categories = j.Object(root, "cargoCategories", "");
        var categoryMasks = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var covered = 0UL;
        foreach (var property in categories.EnumerateObject())
        {
            var mask = 0UL;
            var path = "cargoCategories." + property.Name;
            if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
            {
                throw j.Invalid($"'{path}' must be a non-empty array");
            }

            foreach (var cargoId in property.Value.EnumerateArray())
            {
                var index = content.CargoIndex(cargoId.GetString() ?? "");
                if (index < 0 || index >= 64)
                {
                    throw j.Invalid($"'{path}' names unknown cargo '{cargoId.GetString()}'");
                }

                if ((covered & (1UL << index)) != 0)
                {
                    throw j.Invalid($"'{path}': cargo '{cargoId.GetString()}' is in more than one category");
                }

                covered |= 1UL << index;
                mask |= 1UL << index;
            }

            categoryMasks[property.Name] = mask;
        }

        for (var c = 0; c < content.CargoCount; c++)
        {
            if ((covered & (1UL << c)) == 0)
            {
                throw j.Invalid($"'cargoCategories' does not list cargo '{content.Cargo(c).Id}'");
            }
        }

        var maxLength = FixedMath.UnitsPerTile - (2 * junctionRadius) - minGap;
        var models = new List<VehicleModel>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var element in j.ArrayOf(root, "models", "").EnumerateArray())
        {
            var p = $"models[{n++}].";
            j.Known(element, p, "id", "nameKey", "kind", "cargoCategory", "capacity", "speedKmh", "accelerationMilli", "lengthMilliTiles",
                "price", "runningCostPerYear", "lifeYears", "reliabilityPerMille", "firstYear", "lastYear", "power", "loadPerStep", "color");
            var id = j.String(element, "id", p);
            if (!ids.Add(id))
            {
                throw j.Invalid($"'{p}id' repeats '{id}'");
            }

            p = $"models[{id}].";
            var kind = j.String(element, "kind", p) switch
            {
                "bus" => StationPieceKind.Bus,
                "truck" => StationPieceKind.Truck,
                var other => throw j.Invalid($"'{p}kind' must be bus or truck (was '{other}')"),
            };
            var category = j.String(element, "cargoCategory", p);
            if (!categoryMasks.TryGetValue(category, out var cargoMask))
            {
                throw j.Invalid($"'{p}cargoCategory' names unknown category '{category}'");
            }

            var passengers = content.CargoIndex("passengers");
            var carriesPassengers = passengers >= 0 && (cargoMask & (1UL << passengers)) != 0;
            if ((kind == StationPieceKind.Bus) != carriesPassengers)
            {
                throw j.Invalid($"'{p}cargoCategory': buses carry passengers and trucks never do");
            }

            var speedKmh = j.Int(element, "speedKmh", p, 10, 200);
            var length = MilliTiles(j.Int(element, "lengthMilliTiles", p, 150, 450));
            if (length > maxLength)
            {
                throw j.Invalid($"'{p}lengthMilliTiles' must leave room between two junctions (at most {maxLength * 1000 / FixedMath.UnitsPerTile})");
            }

            var firstYear = j.Int(element, "firstYear", p, 1, 9000);
            var lastYear = j.Int(element, "lastYear", p, 1, 9000);
            if (firstYear > lastYear)
            {
                throw j.Invalid($"'{p}firstYear' is after lastYear");
            }

            var power = j.String(element, "power", p) switch
            {
                "combustion" => VehiclePower.Combustion,
                "electric" => VehiclePower.Electric,
                var other => throw j.Invalid($"'{p}power' must be combustion or electric (was '{other}')"),
            };
            var speed = (int)((long)speedKmh * FixedMath.UnitsPerTile / ((long)kmhPerTile * FixedMath.TicksPerSecond));
            models.Add(new VehicleModel(
                id,
                j.String(element, "nameKey", p),
                kind,
                category,
                cargoMask,
                j.Int(element, "capacity", p, 1, 200),
                speedKmh,
                speed,
                Acceleration(j.Int(element, "accelerationMilli", p, 100, 5000)),
                length,
                j.Int(element, "price", p, 0, 100_000_000),
                j.Int(element, "runningCostPerYear", p, 0, 100_000_000),
                j.Int(element, "lifeYears", p, 1, 100),
                j.Int(element, "reliabilityPerMille", p, 1, 1000),
                firstYear,
                lastYear,
                power,
                j.Int(element, "loadPerStep", p, 1, 200),
                j.Color(element, "color", p)));
        }

        // Every start year from 1850 to 2100 offers a bus and a truck of every category.
        foreach (var category in categoryMasks.Keys)
        {
            for (var year = 1850; year <= 2100; year++)
            {
                if (!models.Exists(m => m.CargoCategory == category && m.AvailableIn(year)))
                {
                    throw j.Invalid($"'models' offer no '{category}' vehicle in {year}");
                }
            }
        }

        var rules = new VehicleRules
        {
            Models = models,
            KmhPerTileSecond = kmhPerTile,
            Braking = braking,
            MinGap = minGap,
            JunctionRadius = junctionRadius,
            DeadEndExtent = deadEnd,
            LookAhead = lookAhead,
            CurveSpeedPercent = curve,
            RampSpeedPercent = ramp,
            BayPlaces = j.Int(stops, "bayPlaces", "stops.", 1, 2),
            MinDwellTicks = j.Int(stops, "minDwellTicks", "stops.", 1, 10_000),
            LoadIntervalTicks = j.Int(stops, "loadIntervalTicks", "stops.", 1, 1000),
            SearchNodesPerTick = j.Int(routing, "searchNodesPerTick", "routing.", 100, 10_000_000),
            StuckNoticeDays = j.Int(stuck, "noticeDays", "stuck.", 1, 365),
            ReliabilityDecayPerDay = j.Int(reliability, "decayPerDay", "reliability.", 0, 100),
            OldAgeFactor = j.Int(reliability, "oldAgeFactor", "reliability.", 1, 100),
            MinReliability = j.Int(reliability, "minimum", "reliability.", 0, 1000),
            BreakdownChanceScale = j.Int(reliability, "breakdownChanceScale", "reliability.", 0, 1000),
            BreakdownDays = j.Int(reliability, "breakdownDays", "reliability.", 1, 365),
            BreakdownPulseTicks = j.Int(reliability, "breakdownPulseTicks", "reliability.", 1, 10_000),
            RecoverPerMille = j.Int(reliability, "recoverPerMille", "reliability.", 0, 1000),
            DefaultServiceIntervalDays = j.Int(service, "defaultIntervalDays", "service.", 0, 1000),
            MinValuePercent = j.Int(value, "minValuePercent", "value.", 0, 100),
            DepotCost = j.Int(depot, "cost", "depot.", 0, 100_000_000),
            DepotRemoveCost = j.Int(depot, "removeCost", "depot.", 0, 100_000_000),
            DepotMaintenancePerYear = j.Int(depot, "maintenancePerYear", "depot.", 0, 100_000_000),
            DepotBaseColor = j.Color(depotModel, "base", "depot.model."),
            DepotRoofColor = j.Color(depotModel, "roof", "depot.model."),
        };
        return rules;
    }

    private static int MilliTiles(int milli) => (int)((long)milli * FixedMath.UnitsPerTile / 1000);

    private static int Acceleration(int milliTilesPerSecond2) =>
        (int)((long)milliTilesPerSecond2 * FixedMath.UnitsPerTile / (1000L * FixedMath.TicksPerSecond * FixedMath.TicksPerSecond));

    private ulong ComputeFingerprint()
    {
        var h = new StateHasher();
        h.Append(KmhPerTileSecond);
        h.Append(Braking);
        h.Append(MinGap);
        h.Append(JunctionRadius);
        h.Append(DeadEndExtent);
        h.Append(LookAhead);
        h.Append(CurveSpeedPercent);
        h.Append(RampSpeedPercent);
        h.Append(BayPlaces);
        h.Append(MinDwellTicks);
        h.Append(LoadIntervalTicks);
        h.Append(SearchNodesPerTick);
        h.Append(StuckNoticeDays);
        h.Append(ReliabilityDecayPerDay);
        h.Append(OldAgeFactor);
        h.Append(MinReliability);
        h.Append(BreakdownChanceScale);
        h.Append(BreakdownDays);
        h.Append(BreakdownPulseTicks);
        h.Append(RecoverPerMille);
        h.Append(DefaultServiceIntervalDays);
        h.Append(MinValuePercent);
        h.Append(DepotCost);
        h.Append(DepotRemoveCost);
        h.Append(DepotMaintenancePerYear);
        foreach (var m in Models)
        {
            h.Append(m.Id);
            h.Append((int)m.Kind);
            h.Append(m.CargoMask);
            h.Append(m.Capacity);
            h.Append(m.Speed);
            h.Append(m.Acceleration);
            h.Append(m.Length);
            h.Append(m.Price);
            h.Append(m.RunningCostPerYear);
            h.Append(m.LifeYears);
            h.Append(m.Reliability);
            h.Append(m.FirstYear);
            h.Append(m.LastYear);
            h.Append((int)m.Power);
            h.Append(m.LoadPerStep);
        }

        return h.GetFingerprint();
    }
}
