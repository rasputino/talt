using Talt.Core.Construction;

namespace Talt.Client.Construction;

/// <summary>What a drag of a tool covers (FR-011).</summary>
internal enum DragShape
{
    /// <summary>Only the tile under the cursor; dragging does not extend it.</summary>
    Tile,

    /// <summary>A straight line along the dominant axis from the press tile (path tools, roads in 007).</summary>
    Line,

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
    public static ConstructionTool Raise { get; } = Terraform("raise", "toolRaise", TerraformMode.Raise);

    public static ConstructionTool Lower { get; } = Terraform("lower", "toolLower", TerraformMode.Lower);

    public static ConstructionTool Level { get; } = Terraform("level", "toolLevel", TerraformMode.Level);

    public static ConstructionTool Clear { get; } = new("clear", "toolClear", DragShape.Area,
        static (planner, state, company, a, b, plan) => planner.PlanClear(state, company, new TileRect(a.X, a.Z, b.X, b.Z), plan),
        static (company, a, b) => new ClearAreaCommand(company, a.X, a.Z, b.X, b.Z));

    /// <summary>The tools of feature 006, in toolbar order.</summary>
    public static IReadOnlyList<ConstructionTool> All { get; } = [Raise, Lower, Level, Clear];

    private static ConstructionTool Terraform(string id, string action, TerraformMode mode) => new(id, action, DragShape.Area,
        (planner, state, company, a, b, plan) => planner.PlanTerraform(state, company, mode, new TileRect(a.X, a.Z, b.X, b.Z), a.X, a.Z, plan),
        (company, a, b) => new TerraformCommand(company, mode, a.X, a.Z, b.X, b.Z, a.X, a.Z));
}
