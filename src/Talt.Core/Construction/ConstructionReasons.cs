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

    // Road vehicles, depots and lines (feature 012, specs/012-road-vehicles/contracts/commands.md).
    public const string NoDepot = "no-depot";
    public const string UnknownModel = "unknown-model";
    public const string ModelNotAvailable = "model-not-available";
    public const string CargoNotAllowed = "cargo-not-allowed";
    public const string NoVehicle = "no-vehicle";
    public const string NotInDepot = "not-in-depot";
    public const string NoDepotReachable = "no-depot-reachable";
    public const string HoldNotEmpty = "hold-not-empty";
    public const string NoLine = "no-line";
    public const string InvalidIndex = "invalid-index";
    public const string TooManyStops = "too-many-stops";
    public const string LastStop = "last-stop";
    public const string WrongKind = "wrong-kind";
    public const string VehicleInTheWay = "vehicle-in-the-way";
    public const string InvalidValue = "invalid-value";
    public const string DepotRoad = "depot-road";
    public const string UseDepotTool = "use-depot-tool";
    public const string TooManyVehicles = "too-many-vehicles";

    // Railways (feature 015, specs/015-railways/contracts/commands.md).
    public const string TrainInTheWay = "train-in-the-way";
    public const string NoRail = "no-rail";
    public const string TurnTooSharp = "turn-too-sharp";
    public const string SignalPlace = "signal-place";
    public const string UnknownRailType = "unknown-rail-type";
    public const string NoTrain = "no-train";
    public const string TrainTooLong = "train-too-long";
    public const string NeedsLocomotive = "needs-locomotive";
    public const string RailStationTrack = "rail-station-track";
    public const string TrackLoop = "track-loop";

    // Finance (feature 011, specs/011-economy-finance/contracts/commands.md).
    public const string MaxLoan = "max-loan";
    public const string NoLoan = "no-loan";
    public const string NotEnoughCash = "not-enough-cash";
    public const string GameOver = "game-over";
    public const string InfiniteMoney = "infinite-money";
    public const string AlreadyInfinite = "already-infinite";

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

    // Towns (feature 008).
    public const string TownRefuses = "town-refuses";

    // Stations (feature 010, specs/010-stations-cargo/contracts/commands.md).
    public const string NoRoad = "no-road";
    public const string NotStraightRoad = "not-straight-road";
    public const string NotFlat = "not-flat";
    public const string AdjacentTwoStations = "adjacent-two-stations";
    public const string StationTooSpread = "station-too-spread";
    public const string NoStation = "no-station";
    public const string StationRoad = "station-road";
    public const string UseStationTool = "use-station-tool";
    public const string NameTooLong = "name-too-long";
    public const string UnknownStationType = "unknown-station-type";

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
        MaxLoan => "The loan is already at its maximum.",
        NoLoan => "There is no loan to repay.",
        NotEnoughCash => "There is not enough cash to repay.",
        GameOver => "The game is over.",
        InfiniteMoney => "The company has infinite money: loans are not needed.",
        AlreadyInfinite => "The company already has infinite money.",
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
        TownRefuses => "The town's rating of the company is too low for this.",
        NoRoad => "A station bay or warehouse must touch a road on one of its sides.",
        NotStraightRoad => "A drive-through stop needs a straight road tile without junctions, bridge or tunnel ends.",
        NotFlat => "The ground must be flat and level with the road.",
        AdjacentTwoStations => "The piece would touch two different stations.",
        StationTooSpread => "The station would become too spread out.",
        NoStation => "There is no station of the company here.",
        StationRoad => "A station uses this road.",
        UseStationTool => "Stations are removed with the station removal tool.",
        NameTooLong => "The name is too long.",
        UnknownStationType => "The station type does not exist.",
        NoDepot => "There is no depot of the company here.",
        UnknownModel => "The vehicle model does not exist.",
        ModelNotAvailable => "The vehicle model is not available this year.",
        CargoNotAllowed => "The vehicle cannot carry this cargo.",
        NoVehicle => "The vehicle does not exist.",
        NotInDepot => "The vehicle must be stopped in a depot.",
        NoDepotReachable => "No depot of the company can be reached.",
        HoldNotEmpty => "The vehicle must be empty.",
        NoLine => "The line does not exist.",
        InvalidIndex => "There is no such stop in the line.",
        TooManyStops => "The line has too many stops.",
        LastStop => "A line needs at least one stop.",
        WrongKind => "The line has no stop this vehicle can use.",
        VehicleInTheWay => "A vehicle is in the way.",
        InvalidValue => "The value is out of range.",
        DepotRoad => "A depot uses this road.",
        UseDepotTool => "Depots are removed with the depot removal tool.",
        TooManyVehicles => "The company has too many vehicles.",
        TrainInTheWay => "A train is on or has reserved this track.",
        NoRail => "There is no track here.",
        TurnTooSharp => "Track may turn at most 45 degrees per tile.",
        SignalPlace => "Signals go on plain straight or diagonal track.",
        UnknownRailType => "Unknown track type.",
        NoTrain => "The train does not exist.",
        TrainTooLong => "The train would be too long.",
        NeedsLocomotive => "A train needs a locomotive first.",
        RailStationTrack => "Station track is removed with the station.",
        TrackLoop => "The track would have to loop round to join there.",
        _ => "Refused.",
    };

    /// <summary>Every code, for localization coverage tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NoMap, CompanyNotFound, NothingToDo, Water, WouldFlood, HeightLimit, TooSteep, ObjectInTheWay,
        OwnedByOther, TooLarge, NotEnoughMoney, NothingToUndo, UndoExpired, ChangedSince,
        NoPath, RoadTypeUnavailable, UnknownRoadType, Downgrade, Industry, BridgeTooLong, BridgeEnds, BridgeBlocked,
        TunnelNoExit, TunnelTooLong, TunnelBlocked, NotStraight, MotorwayJunction, NotARoad, TownNeedsRoad,
        UseRoadRemoval, NotACarriageway, TownRefuses,
        NoRoad, NotStraightRoad, NotFlat, AdjacentTwoStations, StationTooSpread, NoStation, StationRoad, UseStationTool,
        NameTooLong, UnknownStationType,
        NoDepot, UnknownModel, ModelNotAvailable, CargoNotAllowed, NoVehicle, NotInDepot, NoDepotReachable, HoldNotEmpty,
        NoLine, InvalidIndex, TooManyStops, LastStop, WrongKind, VehicleInTheWay, InvalidValue, DepotRoad, UseDepotTool,
        TooManyVehicles,
        TrainInTheWay, NoRail, TurnTooSharp, SignalPlace, UnknownRailType, NoTrain, TrainTooLong, NeedsLocomotive, RailStationTrack, TrackLoop,
    ];
}
