using System.Text.Json;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Towns;

namespace Talt.Core.Stations;

/// <summary>Which vehicles use a road station piece (clarification Q2: bus stops passengers, truck stops the rest).</summary>
public enum StationPieceKind : byte
{
    Bus = 0,
    Truck = 1,

    /// <summary>A rail platform tile (feature 015).</summary>
    Rail = 2,
}

/// <summary>How a road station piece sits on the road (clarification Q1, research R2).</summary>
public enum StationPieceForm : byte
{
    /// <summary>Its own tile beside a road, with one entrance from it.</summary>
    Bay = 0,

    /// <summary>On a straight road tile that vehicles pass through.</summary>
    DriveThrough = 1,

    /// <summary>A rail platform tile with straight track along the station's axis (feature 015, research R10).</summary>
    Platform = 2,
}

/// <summary>One road station piece type from <c>stations.json</c> (data-model.md StationRules.pieces).</summary>
public sealed record StationPieceType(
    string Id,
    string NameKey,
    StationPieceKind Kind,
    StationPieceForm Form,
    long Cost,
    long RemoveCost,
    int CatchmentRadius,
    int CategoryMask,
    int BaseColor,
    int RoofColor)
{
    /// <summary>True when the piece handles cargo of the category.</summary>
    public bool Handles(CargoCategory category) => (CategoryMask & (1 << (int)category)) != 0;
}

/// <summary>One warehouse size from <c>stations.json</c> (data-model.md StationRules.warehouses).</summary>
public sealed record WarehouseType(
    string Id,
    string NameKey,
    int Width,
    int Depth,
    long Cost,
    long RemoveCost,
    int InfluenceRadius,
    int BaseColor,
    int RoofColor);

/// <summary>Rating rules (research R7): <c>[threshold, points]</c> tables, first match wins.</summary>
public sealed record StationRatingRules(
    int Initial,
    int Base,
    int MaxStepPerDay,
    int OverflowPenalty,
    (int Threshold, int Points)[] PickupDays,
    (int Threshold, int Points)[] SpeedKmh,
    (int Threshold, int Points)[] Waiting,
    int WaitingBeyond)
{
    /// <summary>The rating a group and cargo moves toward, 0–1 000.</summary>
    public int Target(int daysSincePickup, int speedKmh, int waiting)
    {
        var points = Base;
        if (daysSincePickup >= 0)
        {
            for (var i = 0; i < PickupDays.Length; i++)
            {
                var (threshold, value) = PickupDays[i];
                if (daysSincePickup <= threshold)
                {
                    points += value;
                    break;
                }
            }

            for (var i = 0; i < SpeedKmh.Length; i++)
            {
                var (threshold, value) = SpeedKmh[i];
                if (speedKmh >= threshold)
                {
                    points += value;
                    break;
                }
            }
        }

        var waitingPoints = WaitingBeyond;
        for (var i = 0; i < Waiting.Length; i++)
        {
            var (threshold, value) = Waiting[i];
            if (waiting <= threshold)
            {
                waitingPoints = value;
                break;
            }
        }

        return Math.Clamp(points + waitingPoints, 0, 1000);
    }
}

/// <summary>
/// Validated station balancing from <c>stations.json</c> (feature 010, contracts/data-files.md,
/// data-model.md StationRules). Immutable; <see cref="Fingerprint"/> is hashed with station state so
/// different balancing gives different fingerprints.
/// </summary>
public sealed class StationRules
{
    private const string FileName = "stations.json";
    private static readonly Lazy<StationRules> LazyDefault = new(() => FromJson(ReadResource(), GameContent.Default));

    private StationRules(
        IReadOnlyList<StationPieceType> pieces,
        IReadOnlyList<WarehouseType> warehouses,
        int maxSpread,
        int waitingCap,
        int maxStationsPerProducer,
        int passengersFullAt,
        int mailFullAt,
        StationRatingRules rating,
        int lossBelowRating,
        int lossAboveWaiting,
        int lossPerMilleAtZero,
        int debugIntervalDays,
        int debugSpeedKmh)
    {
        Pieces = pieces;
        Warehouses = warehouses;
        MaxSpread = maxSpread;
        WaitingCap = waitingCap;
        MaxStationsPerProducer = maxStationsPerProducer;
        PassengersFullAt = passengersFullAt;
        MailFullAt = mailFullAt;
        Rating = rating;
        LossBelowRating = lossBelowRating;
        LossAboveWaiting = lossAboveWaiting;
        LossPerMilleAtZero = lossPerMilleAtZero;
        DebugIntervalDays = debugIntervalDays;
        DebugSpeedKmh = debugSpeedKmh;
        Fingerprint = ComputeFingerprint();
    }

    /// <summary>The game's station rules from the embedded data file.</summary>
    public static StationRules Default => LazyDefault.Value;

    public IReadOnlyList<StationPieceType> Pieces { get; }

    public IReadOnlyList<WarehouseType> Warehouses { get; }

    /// <summary>Largest side of a station's bounding box, in tiles.</summary>
    public int MaxSpread { get; }

    /// <summary>Most units of one cargo waiting per group.</summary>
    public int WaitingCap { get; }

    /// <summary>How many groups share one producer's output.</summary>
    public int MaxStationsPerProducer { get; }

    /// <summary>Monthly passengers of a building that give it 8 eighths of passenger acceptance.</summary>
    public int PassengersFullAt { get; }

    /// <summary>Monthly mail of a building that gives it 8 eighths of mail acceptance.</summary>
    public int MailFullAt { get; }

    public StationRatingRules Rating { get; }

    public int LossBelowRating { get; }

    public int LossAboveWaiting { get; }

    public int LossPerMilleAtZero { get; }

    public int DebugIntervalDays { get; }

    public int DebugSpeedKmh { get; }

    public ulong Fingerprint { get; }

    /// <summary>Index of a piece type id, or −1.</summary>
    public int FindPiece(string id)
    {
        for (var i = 0; i < Pieces.Count; i++)
        {
            if (Pieces[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of the piece of a kind and form, or −1.</summary>
    public int FindPiece(StationPieceKind kind, StationPieceForm form)
    {
        for (var i = 0; i < Pieces.Count; i++)
        {
            if (Pieces[i].Kind == kind && Pieces[i].Form == form)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of a warehouse size id, or −1.</summary>
    public int FindWarehouse(string id)
    {
        for (var i = 0; i < Warehouses.Count; i++)
        {
            if (Warehouses[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The embedded <c>stations.json</c> text.</summary>
    public static string ReadResource()
    {
        using var stream = typeof(StationRules).Assembly.GetManifestResourceStream("Talt.Core.Stations.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses and validates the station data (constitution VII); errors name the field.</summary>
    public static StationRules FromJson(string json, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.CargoCount > 64)
        {
            throw new InvalidDataException($"{FileName}: stations support at most 64 cargo types (the content has {content.CargoCount}).");
        }

        var j = new TownJson(FileName);
        using var document = j.Parse(json);
        var root = document.RootElement;
        j.Known(root, "", "pieces", "warehouses", "maxSpread", "waitingCap", "maxStationsPerProducer", "acceptance", "rating", "loss", "debug");

        var pieces = new List<StationPieceType>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var element in j.ArrayOf(root, "pieces", "").EnumerateArray())
        {
            var p = $"pieces[{index++}].";
            j.Known(element, p, "id", "nameKey", "kind", "form", "cost", "removeCost", "catchmentRadius", "categories", "model");
            var id = j.String(element, "id", p);
            if (!ids.Add(id))
            {
                throw j.Invalid($"'{p}id' repeats '{id}'");
            }

            var kind = j.String(element, "kind", p) switch
            {
                "bus" => StationPieceKind.Bus,
                "truck" => StationPieceKind.Truck,
                "rail" => StationPieceKind.Rail,
                var other => throw j.Invalid($"'{p}kind' must be bus, truck or rail (was '{other}')"),
            };
            var form = j.String(element, "form", p) switch
            {
                "bay" => StationPieceForm.Bay,
                "driveThrough" => StationPieceForm.DriveThrough,
                "platform" => StationPieceForm.Platform,
                var other => throw j.Invalid($"'{p}form' must be bay, driveThrough or platform (was '{other}')"),
            };
            if ((kind == StationPieceKind.Rail) != (form == StationPieceForm.Platform))
            {
                throw j.Invalid($"'{p}form' must be platform exactly for rail pieces");
            }
            var mask = 0;
            foreach (var category in j.ArrayOf(element, "categories", p).EnumerateArray())
            {
                mask |= 1 << (int)ParseCategory(j, category.GetString(), p + "categories");
            }

            var model = j.Object(element, "model", p);
            j.Known(model, p + "model.", "base", "roof");
            pieces.Add(new StationPieceType(
                id,
                j.String(element, "nameKey", p),
                kind,
                form,
                j.Int(element, "cost", p, 0, 100_000_000),
                j.Int(element, "removeCost", p, 0, 100_000_000),
                j.Int(element, "catchmentRadius", p, 1, 8),
                mask,
                j.Color(model, "base", p + "model."),
                j.Color(model, "roof", p + "model.")));
        }

        foreach (var kind in new[] { StationPieceKind.Bus, StationPieceKind.Truck })
        {
            foreach (var form in new[] { StationPieceForm.Bay, StationPieceForm.DriveThrough })
            {
                if (pieces.Count(piece => piece.Kind == kind && piece.Form == form) != 1)
                {
                    throw j.Invalid($"'pieces' must have exactly one {kind.ToString().ToLowerInvariant()} piece of form {form}");
                }
            }
        }

        if (pieces.Count(piece => piece.Kind == StationPieceKind.Rail) > 1)
        {
            throw j.Invalid("'pieces' may have at most one rail piece");
        }

        var warehouses = new List<WarehouseType>();
        index = 0;
        foreach (var element in j.ArrayOf(root, "warehouses", "").EnumerateArray())
        {
            var p = $"warehouses[{index++}].";
            j.Known(element, p, "id", "nameKey", "width", "depth", "cost", "removeCost", "influenceRadius", "model");
            var id = j.String(element, "id", p);
            if (!ids.Add(id))
            {
                throw j.Invalid($"'{p}id' repeats '{id}'");
            }

            var model = j.Object(element, "model", p);
            j.Known(model, p + "model.", "base", "roof");
            warehouses.Add(new WarehouseType(
                id,
                j.String(element, "nameKey", p),
                j.Int(element, "width", p, 1, 6),
                j.Int(element, "depth", p, 1, 6),
                j.Int(element, "cost", p, 0, 100_000_000),
                j.Int(element, "removeCost", p, 0, 100_000_000),
                j.Int(element, "influenceRadius", p, 1, 64),
                j.Color(model, "base", p + "model."),
                j.Color(model, "roof", p + "model.")));
        }

        var acceptance = j.Object(root, "acceptance", "");
        j.Known(acceptance, "acceptance.", "passengersFullAt", "mailFullAt");
        var rating = j.Object(root, "rating", "");
        j.Known(rating, "rating.", "initial", "base", "maxStepPerDay", "overflowPenalty", "pickupDays", "speedKmh", "waiting", "waitingBeyond");
        var loss = j.Object(root, "loss", "");
        j.Known(loss, "loss.", "belowRating", "aboveWaiting", "perMilleAtZero");
        var debug = j.Object(root, "debug", "");
        j.Known(debug, "debug.", "intervalDays", "speedKmh");

        var ratingRules = new StationRatingRules(
            j.Int(rating, "initial", "rating.", 0, 1000),
            j.Int(rating, "base", "rating.", 0, 1000),
            j.Int(rating, "maxStepPerDay", "rating.", 1, 1000),
            j.Int(rating, "overflowPenalty", "rating.", 0, 1000),
            Table(j, rating, "pickupDays", ascending: true).ToArray(),
            Table(j, rating, "speedKmh", ascending: false).ToArray(),
            Table(j, rating, "waiting", ascending: true).ToArray(),
            j.Int(rating, "waitingBeyond", "rating.", -1000, 1000));

        return new StationRules(
            pieces,
            warehouses,
            j.Int(root, "maxSpread", "", 1, 64),
            j.Int(root, "waitingCap", "", 1, 1_000_000),
            j.Int(root, "maxStationsPerProducer", "", 1, 8),
            j.Int(acceptance, "passengersFullAt", "acceptance.", 1, 100_000),
            j.Int(acceptance, "mailFullAt", "acceptance.", 1, 100_000),
            ratingRules,
            j.Int(loss, "belowRating", "loss.", 0, 1000),
            j.Int(loss, "aboveWaiting", "loss.", 0, 1_000_000),
            j.Int(loss, "perMilleAtZero", "loss.", 0, 1000),
            j.Int(debug, "intervalDays", "debug.", 1, 365),
            j.Int(debug, "speedKmh", "debug.", 1, 1000));
    }

    private static CargoCategory ParseCategory(TownJson j, string? text, string path) => text switch
    {
        "raw" => CargoCategory.RawMaterial,
        "processed" => CargoCategory.ProcessedGood,
        "passenger" => CargoCategory.Passenger,
        "mail" => CargoCategory.Mail,
        "goods" => CargoCategory.Goods,
        _ => throw j.Invalid($"'{path}' names an unknown cargo category '{text}'"),
    };

    private static List<(int Threshold, int Points)> Table(TownJson j, JsonElement parent, string name, bool ascending)
    {
        var rows = new List<(int, int)>();
        var index = 0;
        foreach (var row in j.ArrayOf(parent, name, "rating.").EnumerateArray())
        {
            var path = $"rating.{name}[{index++}]";
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 2)
            {
                throw j.Invalid($"'{path}' must be [threshold, points]");
            }

            var threshold = j.IntValue(row[0], path + "[0]", 0, 1_000_000);
            var points = j.IntValue(row[1], path + "[1]", -1000, 1000);
            if (rows.Count > 0 && (ascending ? threshold <= rows[^1].Item1 : threshold >= rows[^1].Item1))
            {
                throw j.Invalid($"'rating.{name}' thresholds must be {(ascending ? "increasing" : "decreasing")}");
            }

            rows.Add((threshold, points));
        }

        return rows;
    }

    private ulong ComputeFingerprint()
    {
        var h = new StateHasher();
        foreach (var piece in Pieces)
        {
            if (piece.Kind == StationPieceKind.Rail)
            {
                continue; // Feature 015 (research R15): hashed by the train system once rail exists.
            }

            h.Append(piece.Id);
            h.Append((int)piece.Kind);
            h.Append((int)piece.Form);
            h.Append(piece.Cost);
            h.Append(piece.RemoveCost);
            h.Append(piece.CatchmentRadius);
            h.Append(piece.CategoryMask);
        }

        foreach (var warehouse in Warehouses)
        {
            h.Append(warehouse.Id);
            h.Append(warehouse.Width);
            h.Append(warehouse.Depth);
            h.Append(warehouse.Cost);
            h.Append(warehouse.RemoveCost);
            h.Append(warehouse.InfluenceRadius);
        }

        h.Append(MaxSpread);
        h.Append(WaitingCap);
        h.Append(MaxStationsPerProducer);
        h.Append(PassengersFullAt);
        h.Append(MailFullAt);
        h.Append(Rating.Initial);
        h.Append(Rating.Base);
        h.Append(Rating.MaxStepPerDay);
        h.Append(Rating.OverflowPenalty);
        foreach (var table in new[] { Rating.PickupDays, Rating.SpeedKmh, Rating.Waiting })
        {
            h.Append(table.Length);
            foreach (var (threshold, points) in table)
            {
                h.Append(threshold);
                h.Append(points);
            }
        }

        h.Append(Rating.WaitingBeyond);
        h.Append(LossBelowRating);
        h.Append(LossAboveWaiting);
        h.Append(LossPerMilleAtZero);
        h.Append(DebugIntervalDays);
        h.Append(DebugSpeedKmh);
        return h.GetFingerprint();
    }
}
