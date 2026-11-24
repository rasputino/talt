using Talt.Core.Construction;
using Talt.Core.Roads;

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

    /// <summary>The construction tools in toolbar order: terrain (feature 006), then roads (feature 007).</summary>
    public static IReadOnlyList<ConstructionTool> All { get; } =
        [Raise, Lower, Level, Clear, Road, Bridge, Tunnel, RemoveRoad, OneWay, Motorway, MotorwayExit, MotorwayEntry];

    /// <summary>A tunnel goes from the press tile towards the drag; a click picks the single higher neighbour (-1).</summary>
    private static int TunnelDirection((int X, int Z) a, (int X, int Z) b) =>
        a == b ? -1 : RoadPlanner.LineOf(a, b, straightOnly: true).Direction;

    private static ConstructionTool Terraform(string id, string action, TerraformMode mode) => new(id, action, DragShape.Area,
        (planner, state, company, a, b, plan) => planner.PlanTerraform(state, company, mode, new TileRect(a.X, a.Z, b.X, b.Z), a.X, a.Z, plan),
        (company, a, b) => new TerraformCommand(company, mode, a.X, a.Z, b.X, b.Z, a.X, a.Z));
}
