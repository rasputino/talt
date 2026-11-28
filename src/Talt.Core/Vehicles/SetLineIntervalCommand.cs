using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>Changes the minimum interval in game days between first-stop departures for each transport mode.</summary>
public sealed record SetLineIntervalCommand(int CompanyId, int LineId, int Days) : ISimulationCommand
{
    public const string Type = "setLineInterval";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = LineCommand.OwnLine(simulation, CompanyId, LineId, out _)
            ?? (Days < 0 ? ConstructionReasons.InvalidValue : null);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.RoadVehicles!.Lines.SetIntervalDays(LineId, Days);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("lineId", LineId);
        writer.WriteNumber("days", Days);
    }

    internal static SetLineIntervalCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("lineId").GetInt32(), e.GetProperty("days").GetInt32());
}
