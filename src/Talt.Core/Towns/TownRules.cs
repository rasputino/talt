using System.Text.Json;
using Talt.Core.Determinism;

namespace Talt.Core.Towns;

/// <summary>Growth balancing from <c>towns.json</c> (data-model.md TownRules, research R5–R7).</summary>
public sealed record TownGrowthRules(
    int RadiusBase,
    int RadiusPerSqrtPopulation,
    int CentreZonePercent,
    int InnerZonePercent,
    int AttemptsPerAction,
    int FailuresBeforeStreet,
    int GrowthPerMilleOfBuildings,
    int ReplaceChancePercent,
    int ReplaceMinPopulation,
    int GridSpacing,
    int GridPatternPercent,
    int StreetSegmentMin,
    int StreetSegmentMax,
    int FoundingAttemptBudget);

/// <summary>One band of the starting population distribution.</summary>
public readonly record struct TownSizeBand(int Weight, int MinPopulation, int MaxPopulation);

/// <summary>Rating balancing from <c>towns.json</c> (research R9). Ratings run from 0 to 1 000.</summary>
public sealed record TownRatingRules(
    int Initial,
    int Hostile,
    int Poor,
    int Neutral,
    int Good,
    int DemolishMinimum,
    int StationMinimum,
    int BuildingPenalty,
    int RoadPenalty,
    int TreePenalty,
    int MaxPenaltyPerAction,
    int ZoneMargin,
    int RecoveryPerMonth,
    int ServiceTargetSpan)
{
    public const int Min = 0;
    public const int Max = 1000;

    /// <summary>The named band of a rating value.</summary>
    public TownRatingBand BandOf(int rating) =>
        rating < Hostile ? TownRatingBand.Hostile
        : rating < Poor ? TownRatingBand.Poor
        : rating < Neutral ? TownRatingBand.Neutral
        : rating < Good ? TownRatingBand.Good
        : TownRatingBand.Excellent;
}

/// <summary>Named rating bands (our own names; data-model.md Town rating).</summary>
public enum TownRatingBand
{
    Hostile,
    Poor,
    Neutral,
    Good,
    Excellent,
}

/// <summary>
/// All town balancing (constitution VII): building types from <c>town-buildings.json</c> and growth, sizes and
/// ratings from <c>towns.json</c>, validated at load. Immutable and shared between games.
/// </summary>
public sealed class TownRules
{
    public const string RulesFileName = "towns.json";
    public const string BuildingsFileName = "town-buildings.json";

    private static readonly Lazy<TownRules> DefaultRules = new(
        () => Parse(TownJson.ReadResource(RulesFileName), TownJson.ReadResource(BuildingsFileName)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private TownRules(
        TownGrowthRules growth,
        TownSizeBand[] sizes,
        (int Small, int Normal, int Large) scale,
        TownRatingRules ratings,
        BuildingType[] buildings)
    {
        Growth = growth;
        Sizes = sizes;
        SizeScale = scale;
        Ratings = ratings;
        Buildings = buildings;
        Fingerprint = ComputeFingerprint();
    }

    /// <summary>The rules shipped with the game.</summary>
    public static TownRules Default => DefaultRules.Value;

    public TownGrowthRules Growth { get; }

    public IReadOnlyList<TownSizeBand> Sizes { get; }

    /// <summary>Percent applied to the starting population by the size option.</summary>
    public (int Small, int Normal, int Large) SizeScale { get; }

    public TownRatingRules Ratings { get; }

    /// <summary>Building types in file order (the simulation uses indices).</summary>
    public IReadOnlyList<BuildingType> Buildings { get; }

    /// <summary>Hash of every value, included in the town state fingerprint.</summary>
    public ulong Fingerprint { get; }

    /// <summary>Scale percent of a size option.</summary>
    public int ScalePercent(TownSize size) => size switch
    {
        TownSize.Small => SizeScale.Small,
        TownSize.Large => SizeScale.Large,
        _ => SizeScale.Normal,
    };

    /// <summary>Index of a building type id, or −1.</summary>
    public int FindBuilding(string id)
    {
        for (var i = 0; i < Buildings.Count; i++)
        {
            if (string.Equals(Buildings[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Parses and validates both files; errors name the file and the field.</summary>
    public static TownRules Parse(string rulesJson, string buildingsJson)
    {
        var buildings = ParseBuildings(buildingsJson);
        var json = new TownJson(RulesFileName);
        using var document = json.Parse(rulesJson);
        var root = document.RootElement;
        json.Known(root, "", "growth", "sizes", "sizeScalePercent", "ratings");

        var g = json.Object(root, "growth", "");
        json.Known(g, "growth.", "radiusBase", "radiusPerSqrtPopulation", "centreZonePercent", "innerZonePercent",
            "attemptsPerAction", "failuresBeforeStreet", "growthPerMilleOfBuildings", "replaceChancePercent",
            "replaceMinPopulation", "gridSpacing", "gridPatternPercent", "streetSegmentMin", "streetSegmentMax",
            "foundingAttemptBudget");
        var growth = new TownGrowthRules(
            json.Int(g, "radiusBase", "growth.", 1, 64),
            json.Int(g, "radiusPerSqrtPopulation", "growth.", 0, 1000),
            json.Int(g, "centreZonePercent", "growth.", 1, 100),
            json.Int(g, "innerZonePercent", "growth.", 1, 100),
            json.Int(g, "attemptsPerAction", "growth.", 1, 1000),
            json.Int(g, "failuresBeforeStreet", "growth.", 1, 1000),
            json.Int(g, "growthPerMilleOfBuildings", "growth.", 0, 1000),
            json.Int(g, "replaceChancePercent", "growth.", 0, 100),
            json.Int(g, "replaceMinPopulation", "growth.", 0, 1_000_000),
            json.Int(g, "gridSpacing", "growth.", 2, 32),
            json.Int(g, "gridPatternPercent", "growth.", 0, 100),
            json.Int(g, "streetSegmentMin", "growth.", 1, 64),
            json.Int(g, "streetSegmentMax", "growth.", 1, 64),
            json.Int(g, "foundingAttemptBudget", "growth.", 1, 1_000_000));
        if (growth.CentreZonePercent >= growth.InnerZonePercent)
        {
            throw json.Invalid("'growth.centreZonePercent' must be below 'growth.innerZonePercent'");
        }

        if (growth.StreetSegmentMin > growth.StreetSegmentMax)
        {
            throw json.Invalid("'growth.streetSegmentMin' must not exceed 'growth.streetSegmentMax'");
        }

        var sizesElement = json.ArrayOf(root, "sizes", "");
        var sizes = new List<TownSizeBand>();
        var index = 0;
        foreach (var entry in sizesElement.EnumerateArray())
        {
            var prefix = $"sizes[{index}].";
            json.Known(entry, prefix, "weight", "minPopulation", "maxPopulation");
            var band = new TownSizeBand(
                json.Int(entry, "weight", prefix, 1, 1000),
                json.Int(entry, "minPopulation", prefix, 0, 1_000_000),
                json.Int(entry, "maxPopulation", prefix, 0, 1_000_000));
            if (band.MinPopulation > band.MaxPopulation)
            {
                throw json.Invalid($"'{prefix}minPopulation' must not exceed '{prefix}maxPopulation'");
            }

            sizes.Add(band);
            index++;
        }

        var s = json.Object(root, "sizeScalePercent", "");
        json.Known(s, "sizeScalePercent.", "small", "normal", "large");
        var scale = (
            json.Int(s, "small", "sizeScalePercent.", 1, 1000),
            json.Int(s, "normal", "sizeScalePercent.", 1, 1000),
            json.Int(s, "large", "sizeScalePercent.", 1, 1000));

        var r = json.Object(root, "ratings", "");
        json.Known(r, "ratings.", "initial", "hostile", "poor", "neutral", "good", "demolishMinimum", "stationMinimum",
            "buildingPenalty", "roadPenalty", "treePenalty", "maxPenaltyPerAction", "zoneMargin", "recoveryPerMonth",
            "serviceTargetSpan");
        const int max = TownRatingRules.Max;
        var ratings = new TownRatingRules(
            json.Int(r, "initial", "ratings.", 0, max),
            json.Int(r, "hostile", "ratings.", 1, max),
            json.Int(r, "poor", "ratings.", 1, max),
            json.Int(r, "neutral", "ratings.", 1, max),
            json.Int(r, "good", "ratings.", 1, max),
            json.Int(r, "demolishMinimum", "ratings.", 0, max),
            json.Int(r, "stationMinimum", "ratings.", 0, max),
            json.Int(r, "buildingPenalty", "ratings.", 0, max),
            json.Int(r, "roadPenalty", "ratings.", 0, max),
            json.Int(r, "treePenalty", "ratings.", 0, max),
            json.Int(r, "maxPenaltyPerAction", "ratings.", 0, max),
            json.Int(r, "zoneMargin", "ratings.", 0, 256),
            json.Int(r, "recoveryPerMonth", "ratings.", 0, max),
            json.Int(r, "serviceTargetSpan", "ratings.", 0, max));
        if (!(ratings.Hostile < ratings.Poor && ratings.Poor < ratings.Neutral && ratings.Neutral < ratings.Good))
        {
            throw json.Invalid("'ratings' thresholds must increase: hostile < poor < neutral < good");
        }

        if (ratings.Initial + ratings.ServiceTargetSpan > max)
        {
            throw json.Invalid("'ratings.initial' + 'ratings.serviceTargetSpan' must not exceed 1000");
        }

        return new TownRules(growth, [.. sizes], scale, ratings, buildings);
    }

    private static BuildingType[] ParseBuildings(string buildingsJson)
    {
        var json = new TownJson(BuildingsFileName);
        using var document = json.Parse(buildingsJson);
        var root = document.RootElement;
        json.Known(root, "", "buildings");
        var list = json.ArrayOf(root, "buildings", "");
        if (list.GetArrayLength() > 250)
        {
            throw json.Invalid("'buildings' may have at most 250 entries");
        }

        var result = new List<BuildingType>();
        var index = 0;
        foreach (var entry in list.EnumerateArray())
        {
            var where = $"buildings[{index}]";
            json.Known(entry, where + ".", "id", "category", "width", "depth", "height", "residents", "passengersPerMonth",
                "mailPerMonth", "goodsAcceptance", "foodAcceptance", "era", "minTownPopulation", "zones", "weight", "level", "model");
            var id = json.String(entry, "id", where + ".");
            foreach (var c in id)
            {
                if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                {
                    throw json.Invalid($"'{where}.id' must use a-z, 0-9 and '-' (was '{id}')");
                }
            }

            if (result.Exists(b => b.Id == id))
            {
                throw json.Invalid($"building '{id}' is defined twice");
            }

            var prefix = $"building '{id}' field ";
            var p = $"buildings[{index}].";
            var category = json.String(entry, "category", p) switch
            {
                "house" => BuildingCategory.House,
                "apartment" => BuildingCategory.Apartment,
                "shop" => BuildingCategory.Shop,
                "office" => BuildingCategory.Office,
                "civic" => BuildingCategory.Civic,
                var other => throw json.Invalid($"{prefix}'category' must be house, apartment, shop, office or civic (was '{other}')"),
            };
            var era = json.ArrayOf(entry, "era", p);
            if (era.GetArrayLength() != 2)
            {
                throw json.Invalid($"{prefix}'era' must be [startYear, endYear]");
            }

            var eraStart = json.IntValue(era[0], $"{p}era[0]", 1, 9000);
            var eraEnd = json.IntValue(era[1], $"{p}era[1]", 1, 9000);
            if (eraStart > eraEnd)
            {
                throw json.Invalid($"{prefix}'era' must start before it ends");
            }

            var zones = TownZones.None;
            foreach (var zone in json.ArrayOf(entry, "zones", p).EnumerateArray())
            {
                zones |= zone.ValueKind == JsonValueKind.String ? zone.GetString() switch
                {
                    "centre" => TownZones.Centre,
                    "inner" => TownZones.Inner,
                    "outer" => TownZones.Outer,
                    _ => throw json.Invalid($"{prefix}'zones' entries must be centre, inner or outer"),
                } : throw json.Invalid($"{prefix}'zones' entries must be texts");
            }

            var model = json.Object(entry, "model", p);
            json.Known(model, p + "model.", "wall", "roof", "roofStyle", "windows");
            var roof = json.String(model, "roofStyle", p + "model.") switch
            {
                "flat" => RoofStyle.Flat,
                "pitched" => RoofStyle.Pitched,
                var other => throw json.Invalid($"{prefix}'model.roofStyle' must be flat or pitched (was '{other}')"),
            };
            var windows = json.String(model, "windows", p + "model.") switch
            {
                "none" => WindowStyle.None,
                "rows" => WindowStyle.Rows,
                "grid" => WindowStyle.Grid,
                var other => throw json.Invalid($"{prefix}'model.windows' must be none, rows or grid (was '{other}')"),
            };

            result.Add(new BuildingType(
                id,
                category,
                json.Int(entry, "width", p, 1, 2),
                json.Int(entry, "depth", p, 1, 2),
                json.Int(entry, "height", p, 1, 32),
                json.Int(entry, "residents", p, 0, 100_000),
                json.Int(entry, "passengersPerMonth", p, 0, 100_000),
                json.Int(entry, "mailPerMonth", p, 0, 100_000),
                json.Int(entry, "goodsAcceptance", p, 0, 8),
                json.Int(entry, "foodAcceptance", p, 0, 8),
                eraStart,
                eraEnd,
                json.Int(entry, "minTownPopulation", p, 0, 1_000_000),
                zones,
                json.Int(entry, "weight", p, 1, 1000),
                json.Int(entry, "level", p, 1, 9),
                new BuildingModel(json.Color(model, "wall", p + "model."), json.Color(model, "roof", p + "model."), roof, windows)));
            index++;
        }

        // A new town must always find something to build: every zone needs a type with residents and without a
        // population minimum.
        foreach (var zone in new[] { TownZones.Centre, TownZones.Inner, TownZones.Outer })
        {
            if (!result.Exists(b => (b.Zones & zone) != 0 && b.MinTownPopulation == 0 && b.Residents > 0))
            {
                throw json.Invalid($"'buildings' needs a type with residents and no population minimum for the {zone.ToString().ToLowerInvariant()} zone");
            }
        }

        return [.. result];
    }

    private ulong ComputeFingerprint()
    {
        var h = new StateHasher();
        var g = Growth;
        foreach (var v in new[]
        {
            g.RadiusBase, g.RadiusPerSqrtPopulation, g.CentreZonePercent, g.InnerZonePercent, g.AttemptsPerAction,
            g.FailuresBeforeStreet, g.GrowthPerMilleOfBuildings, g.ReplaceChancePercent, g.ReplaceMinPopulation,
            g.GridSpacing, g.GridPatternPercent, g.StreetSegmentMin, g.StreetSegmentMax, g.FoundingAttemptBudget,
        })
        {
            h.Append(v);
        }

        foreach (var band in Sizes)
        {
            h.Append(band.Weight);
            h.Append(band.MinPopulation);
            h.Append(band.MaxPopulation);
        }

        h.Append(SizeScale.Small);
        h.Append(SizeScale.Normal);
        h.Append(SizeScale.Large);
        var r = Ratings;
        foreach (var v in new[]
        {
            r.Initial, r.Hostile, r.Poor, r.Neutral, r.Good, r.DemolishMinimum, r.StationMinimum, r.BuildingPenalty,
            r.RoadPenalty, r.TreePenalty, r.MaxPenaltyPerAction, r.ZoneMargin, r.RecoveryPerMonth, r.ServiceTargetSpan,
        })
        {
            h.Append(v);
        }

        foreach (var b in Buildings)
        {
            h.Append(b.Id);
            foreach (var v in new[]
            {
                (int)b.Category, b.Width, b.Depth, b.Height, b.Residents, b.PassengersPerMonth, b.MailPerMonth,
                b.GoodsAcceptance, b.FoodAcceptance, b.EraStart, b.EraEnd, b.MinTownPopulation, (int)b.Zones, b.Weight, b.Level,
            })
            {
                h.Append(v);
            }
        }

        return h.GetFingerprint();
    }
}
