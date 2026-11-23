namespace Talt.Core.Construction;

/// <summary>
/// Stable reason codes of refused construction actions (contracts/commands.md). The client maps each
/// to the localized text <c>construction.reason.&lt;code&gt;</c>; recorded sessions store them.
/// </summary>
public static class ConstructionReasons
{
    public const string NoMap = "no-map";
    public const string CompanyNotFound = "company-not-found";
    public const string NothingToDo = "nothing-to-do";
    public const string Water = "water";
    public const string WouldFlood = "would-flood";
    public const string HeightLimit = "height-limit";
    public const string TooSteep = "too-steep";
    public const string ObjectInTheWay = "object-in-the-way";
    public const string OwnedByOther = "owned-by-other";
    public const string TooLarge = "too-large";
    public const string NotEnoughMoney = "not-enough-money";
    public const string NothingToUndo = "nothing-to-undo";
    public const string UndoExpired = "undo-expired";
    public const string ChangedSince = "changed-since";

    /// <summary>An English description for logs and recorded sessions (players see localized texts).</summary>
    public static string Describe(string code) => code switch
    {
        NoMap => "The game has no map to build on.",
        CompanyNotFound => "The company does not exist.",
        NothingToDo => "Nothing would change.",
        Water => "A tile that must change is under water.",
        WouldFlood => "Land would end below the water next to it.",
        HeightLimit => "The land would go below the lowest or above the highest allowed height.",
        TooSteep => "The land around is too steep for this change.",
        ObjectInTheWay => "An object stands on a tile that must change; demolish it first.",
        OwnedByOther => "A tile belongs to someone else.",
        TooLarge => "The action is too large.",
        NotEnoughMoney => "Not enough money.",
        NothingToUndo => "There is nothing to undo.",
        UndoExpired => "The last action can no longer be undone.",
        ChangedSince => "Something changed the affected tiles since.",
        _ => "Refused.",
    };

    /// <summary>Every code, for localization coverage tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NoMap, CompanyNotFound, NothingToDo, Water, WouldFlood, HeightLimit, TooSteep, ObjectInTheWay,
        OwnedByOther, TooLarge, NotEnoughMoney, NothingToUndo, UndoExpired, ChangedSince,
    ];
}
