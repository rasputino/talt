using System.Text.Json;

namespace Talt.Core.Construction;

/// <summary>
/// Raises, lowers or levels a rectangle of land (data-model.md ConstructionAction.TerraformCommand,
/// contracts/commands.md <c>terraform</c>). The rectangle is inclusive, in any corner order, and clipped
/// to the map; <paramref name="StartX"/>/<paramref name="StartZ"/> name the tile whose height a level
/// takes (ignored by raise and lower).
/// </summary>
public sealed record TerraformCommand(int CompanyId, TerraformMode Mode, int X0, int Z0, int X1, int Z1, int StartX = 0, int StartZ = 0)
    : ConstructionCommand(CompanyId)
{
    public const string Type = "terraform";

    public override string TypeName => Type;

    public TileRect Area => new(X0, Z0, X1, Z1);

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("mode", Mode switch
        {
            TerraformMode.Raise => "raise",
            TerraformMode.Lower => "lower",
            _ => "level",
        });
        writer.WriteNumber("x0", X0);
        writer.WriteNumber("z0", Z0);
        writer.WriteNumber("x1", X1);
        writer.WriteNumber("z1", Z1);
        writer.WriteNumber("startX", StartX);
        writer.WriteNumber("startZ", StartZ);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.PlanTerraform(state, CompanyId, Mode, Area, StartX, StartZ, plan);

    internal static TerraformCommand Read(JsonElement element) => new(
        element.GetProperty("companyId").GetInt32(),
        element.GetProperty("mode").GetString() switch
        {
            "raise" => TerraformMode.Raise,
            "lower" => TerraformMode.Lower,
            "level" => TerraformMode.Level,
            var other => throw new JsonException($"Unknown terraform mode '{other}'."),
        },
        element.GetProperty("x0").GetInt32(),
        element.GetProperty("z0").GetInt32(),
        element.GetProperty("x1").GetInt32(),
        element.GetProperty("z1").GetInt32(),
        element.TryGetProperty("startX", out var sx) ? sx.GetInt32() : 0,
        element.TryGetProperty("startZ", out var sz) ? sz.GetInt32() : 0);
}
