using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Simulation;

namespace Talt.Core.Construction;

/// <summary>
/// Undoes the company's last construction action (data-model.md ConstructionAction.UndoConstructionCommand,
/// research R10): allowed within the undo window and only if nothing changed the touched tiles since;
/// restores them exactly and refunds the cost. One level only.
/// </summary>
public sealed record UndoConstructionCommand(int CompanyId) : ISimulationCommand
{
    public const string Type = "undoConstruction";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction;
        if (state is null)
        {
            return ConstructionCommand.Rejected(ConstructionReasons.NoMap);
        }

        var reason = state.CheckUndo(CompanyId, simulation.CurrentTick);
        return reason is null ? CommandResult.Success : ConstructionCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.Construction!.ApplyUndo();
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
    }

    internal static UndoConstructionCommand Read(JsonElement element) => new(element.GetProperty("companyId").GetInt32());
}
