using System.Text.Json;

namespace Talt.Core.Construction;

/// <summary>
/// Clears a rectangle (data-model.md ConstructionAction.ClearAreaCommand, contracts/commands.md
/// <c>clearArea</c>): removes trees and demolishes the company's own objects, paying the removal costs.
/// Objects of towns or other companies are left standing (shown as problems in the preview).
/// </summary>
public sealed record ClearAreaCommand(int CompanyId, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "clearArea";

    public override string TypeName => Type;

    public TileRect Area => new(X0, Z0, X1, Z1);

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x0", X0);
        writer.WriteNumber("z0", Z0);
        writer.WriteNumber("x1", X1);
        writer.WriteNumber("z1", Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.PlanClear(state, CompanyId, Area, plan);

    internal static ClearAreaCommand Read(JsonElement element) => new(
        element.GetProperty("companyId").GetInt32(),
        element.GetProperty("x0").GetInt32(),
        element.GetProperty("z0").GetInt32(),
        element.GetProperty("x1").GetInt32(),
        element.GetProperty("z1").GetInt32());
}
