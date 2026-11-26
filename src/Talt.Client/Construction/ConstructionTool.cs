using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Client.Construction;

/// <summary>What a drag of a tool covers (FR-011).</summary>
internal enum DragShape
{
    /// <summary>Only the tile under the cursor; dragging does not extend it.</summary>
    Tile,

    /// <summary>A straight line along the dominant axis from the press tile (bridges, tunnels).</summary>
    Line,

    /// <summary>A straight or 45° line from the press tile (motorways, road removal; feature 007).</summary>
    Line8,

    /// <summary>The press tile and the cursor tile, freely (the road tool proposes the path between them).</summary>
    Path,

    /// <summary>The rectangle between the press tile and the cursor.</summary>
    Area,
}

/// <summary>Plans a tool's action for the preview without creating a command (no allocation per frame).</summary>
internal delegate void ToolPlan(ConstructionPlanner planner, ConstructionState state, int company, (int X, int Z) start, (int X, int Z) end, ConstructionPlan plan);

/// <summary>
/// A construction tool of the toolbar (data-model.md, client side): its id (icon, texts, key action),
/// drag shape, how its preview is planned and the command it submits. <see cref="Plan"/> must call the
/// same planner method as the command's own planning (a test compares both). Later features add tools
/// for their own commands (FR-017).
/// </summary>
/// <param name="Id">Used for the icon and <c>tool.&lt;id&gt;.tooltip</c>.</param>
/// <param name="ActionId">Key binding action that selects the tool.</param>
/// <param name="Create">Builds the command from the company, the press tile and the current tile.</param>
internal sealed record ConstructionTool(
    string Id,
    string ActionId,
    DragShape Shape,
    ToolPlan Plan,
    Func<int, (int X, int Z), (int X, int Z), ConstructionCommand> Create)
{
    /// <summary>The tool takes things away: its preview marks them in the removal colour, not the build colour.</summary>
    public bool Removes { get; init; }

    /// <summary>
    /// Builds the command when it depends on the game state (feature 010: the station form follows the tile);
    /// used instead of <see cref="Create"/> when set.
    /// </summary>
    public Func<ConstructionState, int, (int X, int Z), (int X, int Z), ConstructionCommand>? CreateFor { get; init; }

    /// <summary>A station tool (feature 010): drawn in the stations column, previews its catchment.</summary>
    public bool IsStation { get; init; }

    /// <summary>A depot tool (feature 012): drawn in the stations column, no catchment.</summary>
    public bool IsDepot { get; init; }

    /// <summary>A rail tool (feature 015): drawn in the rail column.</summary>
    public bool IsRail { get; init; }

    /// <summary>The command for a drag, from <see cref="CreateFor"/> or <see cref="Create"/>.</summary>
    public ConstructionCommand Command(ConstructionState state, int company, (int X, int Z) start, (int X, int Z) end) =>
        CreateFor is not null ? CreateFor(state, company, start, end) : Create(company, start, end);

    public static ConstructionTool Raise { get; } = Terraform("raise", "toolRaise", TerraformMode.Raise);

    public static ConstructionTool Lower { get; } = Terraform("lower", "toolLower", TerraformMode.Lower);

    public static ConstructionTool Level { get; } = Terraform("level", "toolLevel", TerraformMode.Level);

    public static ConstructionTool Clear { get; } = new("clear", "toolClear", DragShape.Area,
        static (planner, state, company, a, b, plan) => planner.PlanClear(state, company, new TileRect(a.X, a.Z, b.X, b.Z), plan),
        static (company, a, b) => new ClearAreaCommand(company, a.X, a.Z, b.X, b.Z));

    /// <summary>Road tool (feature 007): the proposed path between the press and release tiles.</summary>
    public static ConstructionTool Road { get; } = new("road", "toolRoad", DragShape.Path,
        static (planner, state, company, a, b, plan) => planner.Roads.PlanRoad(state, company, RoadToolOptions.TypeFor(state), a, b, plan),
        static (company, a, b) => new BuildRoadCommand(company, RoadToolOptions.TypeId, a.X, a.Z, b.X, b.Z));

    public static ConstructionTool Bridge { get; } = new("bridge", "toolBridge", DragShape.Line,
        static (planner, state, company, a, b, plan) => planner.Roads.PlanBridge(state, company, RoadToolOptions.TypeFor(state), a, b, plan),
        static (company, a, b) => new BuildBridgeCommand(company, RoadToolOptions.TypeId, a.X, a.Z, b.X, b.Z));

    public static ConstructionTool Tunnel { get; } = new("tunnel", "toolTunnel", DragShape.Line,
        static (planner, state, company, a, b, plan) => planner.Roads.PlanTunnel(state, company, RoadToolOptions.TypeFor(state), a, TunnelDirection(a, b), plan),
        static (company, a, b) => new BuildTunnelCommand(company, RoadToolOptions.TypeId, a.X, a.Z, TunnelDirection(a, b)));

    public static ConstructionTool RemoveRoad { get; } = new("removeRoad", "toolRemoveRoad", DragShape.Line8,
        static (planner, state, company, a, b, plan) => planner.Roads.PlanRemoval(state, company, a, b, plan),
        static (company, a, b) => new RemoveRoadCommand(company, a.X, a.Z, b.X, b.Z))
    {
        Removes = true,
    };

    public static ConstructionTool OneWay { get; } = new("oneWay", "toolOneWay", DragShape.Tile,
        static (planner, state, company, a, _, plan) => planner.Roads.PlanOneWay(state, company, a, plan),
        static (company, a, _) => new SetOneWayCommand(company, a.X, a.Z));

    public static ConstructionTool Motorway { get; } = new("motorway", "toolMotorway", DragShape.Line8,
        static (planner, state, company, a, b, plan) => planner.Roads.PlanMotorway(state, company, a, b, plan),
        static (company, a, b) => new BuildMotorwayCommand(company, a.X, a.Z, b.X, b.Z));

    public static ConstructionTool MotorwayExit { get; } = new("motorwayExit", "toolMotorwayExit", DragShape.Tile,
        static (planner, state, company, a, _, plan) => planner.Roads.PlanConnector(state, company, a, exit: true, plan),
        static (company, a, _) => new BuildMotorwayConnectorCommand(company, a.X, a.Z, true));

    public static ConstructionTool MotorwayEntry { get; } = new("motorwayEntry", "toolMotorwayEntry", DragShape.Tile,
        static (planner, state, company, a, _, plan) => planner.Roads.PlanConnector(state, company, a, exit: false, plan),
        static (company, a, _) => new BuildMotorwayConnectorCommand(company, a.X, a.Z, false));

    /// <summary>Bus stop (feature 010): drive-through on a straight road, a bay beside one elsewhere.</summary>
    public static ConstructionTool BusStop { get; } = Station("busStop", "toolBusStop", StationPieceKind.Bus);

    /// <summary>Truck stop (feature 010), in the same two forms.</summary>
    public static ConstructionTool TruckStop { get; } = Station("truckStop", "toolTruckStop", StationPieceKind.Truck);

    /// <summary>Warehouse of the chosen size (feature 010); the cursor tile is the footprint's minimum corner.</summary>
    public static ConstructionTool Warehouse { get; } = new("warehouse", "toolWarehouse", DragShape.Tile,
        static (_, state, company, a, _, plan) => StationPlanner.PlanWarehouse(state, company, StationToolOptions.SizeIndex(RulesOf(state)), a.X, a.Z, plan),
        static (company, a, _) => new BuildWarehouseCommand(company, "small", a.X, a.Z))
    {
        CreateFor = static (state, company, a, _) => new BuildWarehouseCommand(company, StationToolOptions.SizeId(RulesOf(state)), a.X, a.Z),
        IsStation = true,
    };

    public static ConstructionTool RemoveStation { get; } = new("removeStation", "toolRemoveStation", DragShape.Tile,
        static (_, state, company, a, _, plan) => StationPlanner.PlanRemoval(state, company, a.X, a.Z, plan),
        static (company, a, _) => new RemoveStationCommand(company, a.X, a.Z))
    {
        Removes = true,
        IsStation = true,
    };

    /// <summary>Road depot (feature 012): a free tile beside a road, facing it.</summary>
    public static ConstructionTool Depot { get; } = new("depot", "toolDepot", DragShape.Tile,
        static (_, state, company, a, _, plan) => DepotPlanner.PlanDepot(state, company, VehicleRules.Default, a.X, a.Z, plan),
        static (company, a, _) => new BuildDepotCommand(company, a.X, a.Z))
    {
        IsDepot = true,
    };

    public static ConstructionTool RemoveDepot { get; } = new("removeDepot", "toolRemoveDepot", DragShape.Tile,
        static (_, state, company, a, _, plan) => DepotPlanner.PlanRemoval(state, company, VehicleRules.Default, a.X, a.Z, plan),
        static (company, a, _) => new RemoveDepotCommand(company, a.X, a.Z))
    {
        Removes = true,
        IsDepot = true,
    };

    // ----- Rail (feature 015) -----

    public static ConstructionTool Rail { get; } = new("rail", "toolRail", DragShape.Path,
        static (planner, state, company, a, b, plan) => planner.Rails.PlanTrack(state, company, RailToolOptions.TypeFor(state), a, b, plan),
        static (company, a, b) => new BuildRailCommand(company, RailToolOptions.TypeId, a.X, a.Z, b.X, b.Z))
    {
        IsRail = true,
    };

    public static ConstructionTool RailBridge { get; } = new("railBridge", "toolRailBridge", DragShape.Line,
        static (planner, state, company, a, b, plan) => planner.Rails.PlanBridge(state, company, RailToolOptions.TypeFor(state), a, b, plan),
        static (company, a, b) => new BuildRailBridgeCommand(company, RailToolOptions.TypeId, a.X, a.Z, b.X, b.Z))
    {
        IsRail = true,
    };

    public static ConstructionTool RailTunnel { get; } = new("railTunnel", "toolRailTunnel", DragShape.Line,
        static (planner, state, company, a, b, plan) => planner.Rails.PlanTunnel(state, company, RailToolOptions.TypeFor(state), a, TunnelDirection(a, b), plan),
        static (company, a, b) => new BuildRailTunnelCommand(company, RailToolOptions.TypeId, a.X, a.Z, TunnelDirection(a, b)))
    {
        IsRail = true,
    };

    public static ConstructionTool RemoveRail { get; } = new("removeRail", "toolRemoveRail", DragShape.Line8,
        static (planner, state, company, a, b, plan) => planner.Rails.PlanRemoval(state, company, a, b, plan),
        static (company, a, b) => new RemoveRailCommand(company, a.X, a.Z, b.X, b.Z))
    {
        Removes = true,
        IsRail = true,
    };

    public static ConstructionTool Electrify { get; } = new("electrify", "toolElectrify", DragShape.Line8,
        static (planner, state, company, a, b, plan) => planner.Rails.PlanConversion(state, company, Math.Max(0, state.RailRules.ElectrifiedType()), a, b, plan),
        static (company, a, b) => new ConvertRailCommand(company, "electrified", a.X, a.Z, b.X, b.Z))
    {
        CreateFor = static (state, company, a, b) =>
            new ConvertRailCommand(company, state.RailRules.Types[Math.Max(0, state.RailRules.ElectrifiedType())].Id, a.X, a.Z, b.X, b.Z),
        IsRail = true,
    };

    /// <summary>Signal tool: a click sets the chosen kind facing one way, then the other, then both, then removes it.</summary>
    public static ConstructionTool Signal { get; } = new("signal", "toolSignal", DragShape.Tile,
        static (planner, state, company, a, _, plan) =>
        {
            var (kind, faces) = RailToolOptions.NextSignal(state, a);
            planner.Rails.PlanSignal(state, company, a.X, a.Z, kind, faces, plan);
        },
        static (company, a, _) => new SetSignalCommand(company, a.X, a.Z, 1, 1))
    {
        CreateFor = static (state, company, a, _) =>
        {
            var (kind, faces) = RailToolOptions.NextSignal(state, a);
            return new SetSignalCommand(company, a.X, a.Z, (int)kind, faces);
        },
        IsRail = true,
    };

    /// <summary>Rail station of the chosen shape; the cursor tile is its minimum corner.</summary>
    public static ConstructionTool RailStation { get; } = new("railStation", "toolRailStation", DragShape.Tile,
        static (planner, state, company, a, _, plan) => planner.Rails.PlanStation(state, company,
            RulesOf(state).FindPiece(StationPieceKind.Rail, StationPieceForm.Platform), a.X, a.Z, RailToolOptions.Axis, RailToolOptions.Platforms, RailToolOptions.Length, plan),
        static (company, a, _) => new BuildRailStationCommand(company, a.X, a.Z, RailToolOptions.Axis, RailToolOptions.Platforms, RailToolOptions.Length))
    {
        IsRail = true,
    };

    public static ConstructionTool RailDepot { get; } = new("railDepot", "toolRailDepot", DragShape.Tile,
        static (planner, state, company, a, _, plan) => planner.Rails.PlanDepot(state, company, a.X, a.Z, RailToolOptions.DepotDirection(state, a), plan),
        static (company, a, _) => new BuildRailDepotCommand(company, a.X, a.Z, 2))
    {
        CreateFor = static (state, company, a, _) => new BuildRailDepotCommand(company, a.X, a.Z, RailToolOptions.DepotDirection(state, a)),
        IsRail = true,
    };

    public static ConstructionTool RemoveRailDepot { get; } = new("removeRailDepot", "toolRemoveRailDepot", DragShape.Tile,
        static (planner, state, company, a, _, plan) => planner.Rails.PlanDepotRemoval(state, company,
            state.Map.Contains(a.X, a.Z) ? state.Map.Index(a.X, a.Z) : -1, plan),
        static (company, a, _) => new RemoveRailDepotCommand(company, -1))
    {
        CreateFor = static (state, company, a, _) => new RemoveRailDepotCommand(company, RailToolOptions.DepotAt(state, a)),
        Removes = true,
        IsRail = true,
    };

    /// <summary>
    /// The construction tools in toolbar order: terrain (feature 006), roads (feature 007), stations (feature 010),
    /// depots (feature 012).
    /// </summary>
    public static IReadOnlyList<ConstructionTool> All { get; } =
        [Raise, Lower, Level, Clear, Road, Bridge, Tunnel, RemoveRoad, OneWay, Motorway, MotorwayExit, MotorwayEntry, BusStop, TruckStop, Warehouse, RemoveStation,
            Depot, RemoveDepot, Rail, RailBridge, RailTunnel, RemoveRail, Electrify, Signal, RailStation, RailDepot, RemoveRailDepot];

    private static StationRules RulesOf(ConstructionState state) => state.Stations?.Rules ?? StationRules.Default;

    private static ConstructionTool Station(string id, string action, StationPieceKind kind) => new(id, action, DragShape.Tile,
        (_, state, company, a, _, plan) => StationPlanner.PlanPiece(state, company, StationPlanner.ChoosePiece(state, kind, a.X, a.Z), a.X, a.Z, plan),
        (company, a, _) => new BuildStationCommand(company, kind == StationPieceKind.Bus ? "bus-bay" : "truck-bay", a.X, a.Z))
    {
        CreateFor = (state, company, a, _) => new BuildStationCommand(company, RulesOf(state).Pieces[StationPlanner.ChoosePiece(state, kind, a.X, a.Z)].Id, a.X, a.Z),
        IsStation = true,
    };

    /// <summary>A tunnel goes from the press tile towards the drag; a click picks the single higher neighbour (-1).</summary>
    private static int TunnelDirection((int X, int Z) a, (int X, int Z) b) =>
        a == b ? -1 : RoadPlanner.LineOf(a, b, straightOnly: true).Direction;

    private static ConstructionTool Terraform(string id, string action, TerraformMode mode) => new(id, action, DragShape.Area,
        (planner, state, company, a, b, plan) => planner.PlanTerraform(state, company, mode, new TileRect(a.X, a.Z, b.X, b.Z), a.X, a.Z, plan),
        (company, a, b) => new TerraformCommand(company, mode, a.X, a.Z, b.X, b.Z, a.X, a.Z));
}
