using Talt.Core.Content;

namespace Talt.Core.Industries;

/// <summary>Result of an amount of cargo offered to an industry (feature 010 uses this).</summary>
public enum DeliveryOutcome : byte
{
    /// <summary>Nothing was accepted.</summary>
    Refused = 0,

    /// <summary>All the offered cargo was accepted.</summary>
    Accepted,

    /// <summary>Part of the offered cargo was accepted; the rest does not fit.</summary>
    PartiallyAccepted,
}

/// <summary>What happened to one delivery (contracts/industry-api.md).</summary>
/// <param name="Outcome">Refused, fully accepted or partially accepted.</param>
/// <param name="Accepted">Units accepted and recorded this month.</param>
/// <param name="Refused">Units the industry did not take.</param>
/// <param name="Consumed">Units consumed without producing another cargo.</param>
/// <param name="ConvertedCargoId">Output cargo produced by a recipe, or null.</param>
/// <param name="ConvertedAmount">Units of that output produced; zero when none.</param>
public readonly record struct IndustryDeliveryResult(
    DeliveryOutcome Outcome,
    int Accepted,
    int Refused,
    int Consumed,
    string? ConvertedCargoId = null,
    int ConvertedAmount = 0);

/// <summary>
/// Read-only snapshot of one industry for the information window and tools. Built on demand from the
/// live arrays; it does not expose mutable state.
/// </summary>
/// <param name="Id">Stable industry id.</param>
/// <param name="X">Centre tile column.</param>
/// <param name="Z">Centre tile row.</param>
/// <param name="KindId">Industry kind id.</param>
/// <param name="NameKey">Localization key of the kind name.</param>
/// <param name="TownId">Town the industry belongs to and is named after, or -1 when the map has no town.</param>
/// <param name="RatePerMille">Current production condition (1000 = nominal).</param>
/// <param name="MonthsNeglected">Months since the last accepted delivery.</param>
/// <param name="Production">Monthly production entries of the kind.</param>
/// <param name="Accepts">Cargo the industry accepts.</param>
/// <param name="Recipes">Automatic conversions of the kind.</param>
/// <param name="Cargo">Stored cargo: available, pending recipe input and capacity.</param>
/// <param name="LastMonth">Accepted and shipped units per cargo last month.</param>
public readonly record struct IndustryInfo(
    int Id,
    int X,
    int Z,
    string KindId,
    string NameKey,
    int TownId,
    int RatePerMille,
    int MonthsNeglected,
    IReadOnlyList<ProductionEntry> Production,
    IReadOnlyList<string> Accepts,
    IReadOnlyList<RecipeDefinition> Recipes,
    IReadOnlyList<(string CargoId, int Available, int PendingInput, int Capacity)> Cargo,
    IReadOnlyList<(string CargoId, int Accepted, int Shipped)> LastMonth);
