using System.Text.Json;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Roads;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Core.Finance;

/// <summary>Payment data of one cargo (data-model.md FinanceRules.payments).</summary>
/// <param name="Rate">Money per 1 000 units per tile of distance.</param>
/// <param name="FreeDays">Days in transit paid at full value.</param>
public readonly record struct CargoPayment(long Rate, int FreeDays);

/// <summary>
/// Finance balancing from <c>finance.json</c> (feature 011, data-model.md FinanceRules): loan, payments,
/// maintenance, property value, bankruptcy, inflation and the debug distance. Parsed strictly and checked
/// against the cargo, road and station data of the game; errors name the file and the field (constitution VII).
/// Arrays are indexed by cargo index, road type index, station piece index and warehouse index.
/// </summary>
public sealed class FinanceRules
{
    public const string FileName = "finance.json";

    /// <summary>Largest money value accepted in the file.</summary>
    public const int MaxMoney = 1_000_000_000;

    private static readonly Lazy<FinanceRules> LazyDefault = new(() =>
        FromJson(ReadResource(), GameContent.Default, RoadRules.Default, StationRules.Default));

    private FinanceRules()
    {
    }

    /// <summary>The rules shipped with the game, checked against the default content, roads and stations.</summary>
    public static FinanceRules Default => LazyDefault.Value;

    public long StartLoan { get; private init; }

    public long MaxLoan { get; private init; }

    public long LoanStep { get; private init; }

    /// <summary>Yearly interest in per mille.</summary>
    public int InterestPerMille { get; private init; }

    /// <summary>By cargo index.</summary>
    public IReadOnlyList<CargoPayment> Payments { get; private init; } = [];

    public int MinDistance { get; private init; }

    /// <summary>Yearly maintenance per owned road tile, by road type index.</summary>
    public IReadOnlyList<long> RoadMaintenance { get; private init; } = [];

    /// <summary>Yearly maintenance per station piece, by piece type index.</summary>
    public IReadOnlyList<long> PieceMaintenance { get; private init; } = [];

    /// <summary>Yearly maintenance per warehouse, by warehouse type index.</summary>
    public IReadOnlyList<long> WarehouseMaintenance { get; private init; } = [];

    public int PropertyValuePercent { get; private init; }

    public int WarningMonths { get; private init; }

    public int GameOverMonths { get; private init; }

    /// <summary>Yearly cost rise with inflation, in basis points.</summary>
    public int CostInflationBasisPoints { get; private init; }

    /// <summary>Yearly payment rise with inflation, in basis points.</summary>
    public int PaymentInflationBasisPoints { get; private init; }

    public int InflationYears { get; private init; }

    public int DebugServiceDistance { get; private init; }

    /// <summary>Hash of every value (part of the finance fingerprint).</summary>
    public ulong Fingerprint { get; private set; }

    public static string ReadResource()
    {
        using var stream = typeof(FinanceRules).Assembly.GetManifestResourceStream("Talt.Core.Finance.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses and validates the finance data against the game's cargo, road and station data.</summary>
    public static FinanceRules FromJson(string json, GameContent content, RoadRules roads, StationRules stations)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(roads);
        ArgumentNullException.ThrowIfNull(stations);
        var j = new TownJson(FileName);
        using var document = j.Parse(json);
        var root = document.RootElement;
        j.Known(root, "", "loan", "payments", "minDistance", "maintenance", "propertyValuePercent", "bankruptcy", "inflation", "debug");

        var loan = j.Object(root, "loan", "");
        j.Known(loan, "loan.", "start", "max", "step", "interestPerMille");
        long start = j.Int(loan, "start", "loan.", 0, MaxMoney);
        long max = j.Int(loan, "max", "loan.", 0, MaxMoney);
        long step = j.Int(loan, "step", "loan.", 1, MaxMoney);
        if (start > max)
        {
            throw j.Invalid($"'loan.start' must not be above 'loan.max' (was {start} > {max})");
        }

        if (start % step != 0 || max % step != 0)
        {
            throw j.Invalid("'loan.start' and 'loan.max' must be multiples of 'loan.step'");
        }

        var interest = j.Int(loan, "interestPerMille", "loan.", 0, 1000);

        var paymentsElement = j.Object(root, "payments", "");
        var payments = new CargoPayment[content.CargoCount];
        var seen = new bool[content.CargoCount];
        foreach (var property in paymentsElement.EnumerateObject())
        {
            var index = content.CargoIndex(property.Name);
            if (index < 0)
            {
                throw j.Invalid($"'payments' names an unknown cargo '{property.Name}'");
            }

            var p = $"payments.{property.Name}.";
            j.Known(property.Value, p, "rate", "freeDays");
            payments[index] = new CargoPayment(j.Int(property.Value, "rate", p, 0, MaxMoney), j.Int(property.Value, "freeDays", p, 0, 3650));
            seen[index] = true;
        }

        for (var c = 0; c < seen.Length; c++)
        {
            if (!seen[c])
            {
                throw j.Invalid($"'payments.{content.Cargo(c).Id}' is missing");
            }
        }

        var maintenance = j.Object(root, "maintenance", "");
        j.Known(maintenance, "maintenance.", "roads", "stationPieces", "warehouses");
        var roadIds = roads.Types.Select(t => t.Id).ToArray();
        var pieceIds = stations.Pieces.Select(t => t.Id).ToArray();
        var warehouseIds = stations.Warehouses.Select(t => t.Id).ToArray();

        var bankruptcy = j.Object(root, "bankruptcy", "");
        j.Known(bankruptcy, "bankruptcy.", "warningMonths", "gameOverMonths");
        var warning = j.Int(bankruptcy, "warningMonths", "bankruptcy.", 1, 120);
        var gameOver = j.Int(bankruptcy, "gameOverMonths", "bankruptcy.", 1, 120);
        if (warning > gameOver)
        {
            throw j.Invalid($"'bankruptcy.warningMonths' must not be above 'bankruptcy.gameOverMonths' (was {warning} > {gameOver})");
        }

        var inflation = j.Object(root, "inflation", "");
        j.Known(inflation, "inflation.", "costBasisPoints", "paymentBasisPoints", "years");
        var debug = j.Object(root, "debug", "");
        j.Known(debug, "debug.", "serviceDistance");

        var rules = new FinanceRules
        {
            StartLoan = start,
            MaxLoan = max,
            LoanStep = step,
            InterestPerMille = interest,
            Payments = payments,
            MinDistance = j.Int(root, "minDistance", "", 1, 100_000),
            RoadMaintenance = Table(j, maintenance, "roads", roadIds),
            PieceMaintenance = Table(j, maintenance, "stationPieces", pieceIds),
            WarehouseMaintenance = Table(j, maintenance, "warehouses", warehouseIds),
            PropertyValuePercent = j.Int(root, "propertyValuePercent", "", 0, 100),
            WarningMonths = warning,
            GameOverMonths = gameOver,
            CostInflationBasisPoints = j.Int(inflation, "costBasisPoints", "inflation.", 0, 5000),
            PaymentInflationBasisPoints = j.Int(inflation, "paymentBasisPoints", "inflation.", 0, 5000),
            InflationYears = j.Int(inflation, "years", "inflation.", 0, 1000),
            DebugServiceDistance = j.Int(debug, "serviceDistance", "debug.", 1, 100_000),
        };
        rules.Fingerprint = rules.ComputeFingerprint();
        return rules;
    }

    private static long[] Table(TownJson j, JsonElement parent, string name, string[] ids)
    {
        var element = j.Object(parent, name, "maintenance.");
        var values = new long[ids.Length];
        var seen = new bool[ids.Length];
        foreach (var property in element.EnumerateObject())
        {
            var index = Array.IndexOf(ids, property.Name);
            if (index < 0)
            {
                throw j.Invalid($"'maintenance.{name}' names an unknown id '{property.Name}'");
            }

            values[index] = j.IntValue(property.Value, $"maintenance.{name}.{property.Name}", 0, MaxMoney);
            seen[index] = true;
        }

        for (var i = 0; i < ids.Length; i++)
        {
            if (!seen[i])
            {
                throw j.Invalid($"'maintenance.{name}.{ids[i]}' is missing");
            }
        }

        return values;
    }

    private ulong ComputeFingerprint()
    {
        var h = new StateHasher();
        h.Append(StartLoan);
        h.Append(MaxLoan);
        h.Append(LoanStep);
        h.Append(InterestPerMille);
        h.Append(Payments.Count);
        foreach (var payment in Payments)
        {
            h.Append(payment.Rate);
            h.Append(payment.FreeDays);
        }

        h.Append(MinDistance);
        foreach (var table in new[] { RoadMaintenance, PieceMaintenance, WarehouseMaintenance })
        {
            h.Append(table.Count);
            foreach (var value in table)
            {
                h.Append(value);
            }
        }

        h.Append(PropertyValuePercent);
        h.Append(WarningMonths);
        h.Append(GameOverMonths);
        h.Append(CostInflationBasisPoints);
        h.Append(PaymentInflationBasisPoints);
        h.Append(InflationYears);
        h.Append(DebugServiceDistance);
        return h.GetFingerprint();
    }
}
