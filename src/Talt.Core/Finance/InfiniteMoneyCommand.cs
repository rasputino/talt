using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Finance;

/// <summary>
/// Turns infinite money on for a company for the rest of the game (feature 011, research R17,
/// contracts/commands.md <c>enableInfiniteMoney</c>). No command turns it off; the client asks for confirmation.
/// </summary>
public sealed record EnableInfiniteMoneyCommand(int CompanyId) : ISimulationCommand
{
    public const string Type = "enableInfiniteMoney";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = simulation.Finance is null ? ConstructionReasons.NoMap
            : simulation.Construction!.FindCompany(CompanyId) is not { } company ? ConstructionReasons.CompanyNotFound
            : company.HasInfiniteMoney ? ConstructionReasons.AlreadyInfinite
            : null;
        return reason is null ? CommandResult.Success : CommandResult.Rejected(reason, ConstructionReasons.Describe(reason));
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.Construction!.FindCompany(CompanyId)!.EnableInfiniteMoney(simulation.CurrentTick);
        simulation.Finance!.MarkActive();
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
    }

    internal static EnableInfiniteMoneyCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32());
}
