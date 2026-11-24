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

    // Roads (feature 007, contracts/commands.md).
    public const string NoPath = "no-path";
    public const string RoadTypeUnavailable = "road-type-unavailable";
    public const string UnknownRoadType = "unknown-road-type";
    public const string Downgrade = "downgrade";
    public const string Industry = "industry";
    public const string BridgeTooLong = "bridge-too-long";
    public const string BridgeEnds = "bridge-ends";
    public const string BridgeBlocked = "bridge-blocked";
    public const string TunnelNoExit = "tunnel-no-exit";
    public const string TunnelTooLong = "tunnel-too-long";
    public const string TunnelBlocked = "tunnel-blocked";
    public const string NotStraight = "not-straight";
    public const string MotorwayJunction = "motorway-junction";
    public const string NotARoad = "not-a-road";
    public const string TownNeedsRoad = "town-needs-road";
    public const string UseRoadRemoval = "use-road-removal";
    public const string NotACarriageway = "not-a-carriageway";

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
        NoPath => "No road path was found between the two points.",
        RoadTypeUnavailable => "This road type is not available in this year or for this tool.",
        UnknownRoadType => "The road type does not exist.",
        Downgrade => "A road cannot be replaced by a worse type.",
        Industry => "An industry stands in the way.",
        BridgeTooLong => "The bridge is longer than this era allows.",
        BridgeEnds => "A bridge needs two land ends at the same height, at least two tiles apart, in a straight line.",
        BridgeBlocked => "Something under the bridge is too high, or another bridge is in the way.",
        TunnelNoExit => "The tunnel finds no exit at the same height on the other side.",
        TunnelTooLong => "The tunnel is longer than allowed.",
        TunnelBlocked => "Another tunnel is in the way.",
        NotStraight => "Only a straight, flat piece can do this.",
        MotorwayJunction => "Motorways join other roads only at their ends or through connectors.",
        NotARoad => "There is no road here.",
        TownNeedsRoad => "The town needs this road to keep its streets connected.",
        UseRoadRemoval => "Roads are removed with the road removal tool.",
        NotACarriageway => "Connectors go on a straight, flat motorway carriageway.",
        _ => "Refused.",
    };

    /// <summary>Every code, for localization coverage tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NoMap, CompanyNotFound, NothingToDo, Water, WouldFlood, HeightLimit, TooSteep, ObjectInTheWay,
        OwnedByOther, TooLarge, NotEnoughMoney, NothingToUndo, UndoExpired, ChangedSince,
        NoPath, RoadTypeUnavailable, UnknownRoadType, Downgrade, Industry, BridgeTooLong, BridgeEnds, BridgeBlocked,
        TunnelNoExit, TunnelTooLong, TunnelBlocked, NotStraight, MotorwayJunction, NotARoad, TownNeedsRoad,
        UseRoadRemoval, NotACarriageway,
    ];
}
