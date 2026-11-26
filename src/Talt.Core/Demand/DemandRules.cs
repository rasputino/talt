using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Towns;

namespace Talt.Core.Demand;

/// <summary>Where a destination cargo comes from (data-model.md DemandRules.cargo.source).</summary>
public enum DemandSource : byte
{
    /// <summary>Town buildings (passengers, mail): tables per town with an own-town share.</summary>
    Buildings = 0,

    /// <summary>Industries (goods): tables per producing industry, towns that accept the cargo.</summary>
    Industries = 1,
}

/// <summary>Demand rule of one destination cargo.</summary>
/// <param name="CargoIndex">Cargo index in the game content.</param>
/// <param name="Source">Who produces it.</param>
/// <param name="OwnTownPerMille">Share of a town's cargo that wants the same town (0 for industries).</param>
/// <param name="PopulationExponent">a in population^a / distance^k (0–2).</param>
/// <param name="DistanceExponent">k in population^a / distance^k (0–3).</param>
public readonly record struct DemandCargoRule(int CargoIndex, DemandSource Source, int OwnTownPerMille, int PopulationExponent, int DistanceExponent);

/// <summary>
/// Validated destination demand and routing balancing from <c>demand.json</c> (feature 013, contracts/data-files.md,
/// data-model.md DemandRules). Immutable; <see cref="Fingerprint"/> is hashed with the demand state.
/// </summary>
public sealed class DemandRules
{
    private const string FileName = "demand.json";
    private static readonly Lazy<DemandRules> LazyDefault = new(() => FromJson(ReadResource(), GameContent.Default));

    private readonly int[] _ruleOfCargo;

    private DemandRules(DemandCargoRule[] cargo, int cargoCount)
    {
        Cargo = cargo;
        _ruleOfCargo = new int[cargoCount];
        Array.Fill(_ruleOfCargo, -1);
        for (var i = 0; i < cargo.Length; i++)
        {
            _ruleOfCargo[cargo[i].CargoIndex] = i;
        }
    }

    /// <summary>The rules of the embedded file with the default content.</summary>
    public static DemandRules Default => LazyDefault.Value;

    /// <summary>One rule per destination cargo, in file order.</summary>
    public IReadOnlyList<DemandCargoRule> Cargo { get; }

    /// <summary>Other towns kept per source.</summary>
    public int MaxDestinations { get; private init; }

    /// <summary>Largest distance between a source and a destination town, in tiles.</summary>
    public int RangeTiles { get; private init; }

    /// <summary>Days destination cargo may wait at one station before it expires.</summary>
    public int ExpiryDays { get; private init; }

    public int UpdateIntervalDays { get; private init; }

    public int ApplyDelayDays { get; private init; }

    /// <summary>Assignment passes (1–2).</summary>
    public int Iterations { get; private init; }

    public int DwellTicks { get; private init; }

    public int CongestionPenaltyPerMille { get; private init; }

    public int ZeroCapacityPenaltyTicks { get; private init; }

    public int MaxRouteDays { get; private init; }

    public int PruneMarginTiles { get; private init; }

    /// <summary>Town pairs drawn by the client's overlay (presentation only, not hashed).</summary>
    public int OverlayPairs { get; private init; }

    public ulong Fingerprint { get; private set; }

    /// <summary>The rule index of a cargo, or −1 when the cargo has no destinations.</summary>
    public int RuleOf(int cargo) => (uint)cargo < (uint)_ruleOfCargo.Length ? _ruleOfCargo[cargo] : -1;

    /// <summary>True when the cargo has destinations.</summary>
    public bool IsDirected(int cargo) => RuleOf(cargo) >= 0;

    public static string ReadResource()
    {
        using var stream = typeof(DemandRules).Assembly.GetManifestResourceStream("Talt.Core.Demand.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses and validates the file against the content's cargo types.</summary>
    public static DemandRules FromJson(string json, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var j = new TownJson(FileName);
        using var document = j.Parse(json);
        var root = document.RootElement;
        j.Known(root, "", "cargo", "maxDestinations", "rangeTiles", "expiryDays", "routing", "overlay");

        var rules = new List<DemandCargoRule>();
        var index = 0;
        foreach (var element in j.ArrayOf(root, "cargo", "").EnumerateArray())
        {
            var p = $"cargo[{index++}].";
            j.Known(element, p, "id", "source", "ownTownPerMille", "populationExponent", "distanceExponent");
            var id = j.String(element, "id", p);
            var cargo = content.CargoIndex(id);
            if (cargo < 0)
            {
                throw j.Invalid($"'{p}id' names an unknown cargo '{id}'");
            }

            var definition = content.Cargo(cargo);
            if (!definition.DestinationRequired)
            {
                throw j.Invalid($"'{p}id' names '{id}', which cargo.json does not mark destinationRequired");
            }

            if (rules.Exists(r => r.CargoIndex == cargo))
            {
                throw j.Invalid($"'{p}id' repeats '{id}'");
            }

            var source = j.String(element, "source", p) switch
            {
                "buildings" => DemandSource.Buildings,
                "industries" => DemandSource.Industries,
                var other => throw j.Invalid($"'{p}source' must be buildings or industries (was '{other}')"),
            };
            if (source == DemandSource.Buildings && definition.Category is not (CargoCategory.Passenger or CargoCategory.Mail))
            {
                throw j.Invalid($"'{p}source' may be buildings only for passenger or mail cargo");
            }

            var own = j.Int(element, "ownTownPerMille", p, 0, 1000);
            if (source == DemandSource.Industries && own != 0)
            {
                throw j.Invalid($"'{p}ownTownPerMille' must be 0 for industry cargo");
            }

            rules.Add(new DemandCargoRule(
                cargo,
                source,
                own,
                j.Int(element, "populationExponent", p, 0, 2),
                j.Int(element, "distanceExponent", p, 0, 3)));
        }

        for (var c = 0; c < content.CargoCount; c++)
        {
            if (content.Cargo(c).DestinationRequired && !rules.Exists(r => r.CargoIndex == c))
            {
                throw j.Invalid($"'cargo' has no entry for destination cargo '{content.Cargo(c).Id}'");
            }
        }

        var routing = j.Object(root, "routing", "");
        j.Known(routing, "routing.", "updateIntervalDays", "applyDelayDays", "iterations", "dwellTicks", "congestionPenaltyPerMille",
            "zeroCapacityPenaltyTicks", "maxRouteDays", "pruneMarginTiles");
        var overlay = j.Object(root, "overlay", "");
        j.Known(overlay, "overlay.", "pairs");
        var update = j.Int(routing, "updateIntervalDays", "routing.", 1, 365);
        var delay = j.Int(routing, "applyDelayDays", "routing.", 1, 364);
        if (delay >= update)
        {
            throw j.Invalid("'routing.applyDelayDays' must be smaller than 'routing.updateIntervalDays'");
        }

        var result = new DemandRules([.. rules], content.CargoCount)
        {
            MaxDestinations = j.Int(root, "maxDestinations", "", 1, 64),
            RangeTiles = j.Int(root, "rangeTiles", "", 1, 65_535),
            ExpiryDays = j.Int(root, "expiryDays", "", 1, 3650),
            UpdateIntervalDays = update,
            ApplyDelayDays = delay,
            Iterations = j.Int(routing, "iterations", "routing.", 1, 2),
            DwellTicks = j.Int(routing, "dwellTicks", "routing.", 0, 100_000),
            CongestionPenaltyPerMille = j.Int(routing, "congestionPenaltyPerMille", "routing.", 0, 100_000),
            ZeroCapacityPenaltyTicks = j.Int(routing, "zeroCapacityPenaltyTicks", "routing.", 0, 10_000_000),
            MaxRouteDays = j.Int(routing, "maxRouteDays", "routing.", 1, 3650),
            PruneMarginTiles = j.Int(routing, "pruneMarginTiles", "routing.", 0, 65_535),
            OverlayPairs = j.Int(overlay, "pairs", "overlay.", 0, 1000),
        };
        result.Fingerprint = result.ComputeFingerprint();
        return result;
    }

    private ulong ComputeFingerprint()
    {
        var h = new StateHasher();
        h.Append(Cargo.Count);
        foreach (var rule in Cargo)
        {
            h.Append(rule.CargoIndex);
            h.Append((int)rule.Source);
            h.Append(rule.OwnTownPerMille);
            h.Append(rule.PopulationExponent);
            h.Append(rule.DistanceExponent);
        }

        h.Append(MaxDestinations);
        h.Append(RangeTiles);
        h.Append(ExpiryDays);
        h.Append(UpdateIntervalDays);
        h.Append(ApplyDelayDays);
        h.Append(Iterations);
        h.Append(DwellTicks);
        h.Append(CongestionPenaltyPerMille);
        h.Append(ZeroCapacityPenaltyTicks);
        h.Append(MaxRouteDays);
        h.Append(PruneMarginTiles);
        return h.GetFingerprint();
    }
}
