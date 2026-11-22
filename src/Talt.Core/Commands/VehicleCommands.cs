using System.Text.Json;
using Talt.Core.Simulation;

namespace Talt.Core.Commands;

/// <summary>
/// Adds a vehicle to a line (data-model.md AddVehicleCommand.LineId, AddVehicleCommand.MaxSpeed,
/// AddVehicleCommand.StartEdgeIndex). The vehicle starts standing still and waits the standard dwell
/// time before departing.
/// </summary>
/// <param name="LineId">Existing line index.</param>
/// <param name="MaxSpeed">Cruise speed in units per tick, 1 to <see cref="VehicleFleet.MaxAllowedSpeed"/>.</param>
/// <param name="StartEdgeIndex">Line edge where the vehicle starts; 0 is the first stop.</param>
public sealed record AddVehicleCommand(int LineId, int MaxSpeed, int StartEdgeIndex = 0) : ISimulationCommand
{
    public const string Type = "addVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var fleet = simulation.Vehicles;

        if ((uint)LineId >= (uint)fleet.LineCount)
        {
            return CommandResult.Rejected("line-not-found", $"Line {LineId} does not exist.");
        }

        if (MaxSpeed <= 0 || MaxSpeed > VehicleFleet.MaxAllowedSpeed)
        {
            return CommandResult.Rejected("invalid-speed", $"Maximum speed {MaxSpeed} is outside 1..{VehicleFleet.MaxAllowedSpeed}.");
        }

        if ((uint)StartEdgeIndex >= (uint)fleet.LineEdgeCount(LineId))
        {
            return CommandResult.Rejected("invalid-start-edge", $"Start edge {StartEdgeIndex} is outside line {LineId}.");
        }

        return CommandResult.Success;
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.Vehicles.AddVehicle(LineId, MaxSpeed, VehicleFleet.DwellTicks, StartEdgeIndex);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("lineId", LineId);
        writer.WriteNumber("maxSpeed", MaxSpeed);
        writer.WriteNumber("startEdgeIndex", StartEdgeIndex);
    }

    internal static AddVehicleCommand Read(JsonElement element) => new(
        element.GetProperty("lineId").GetInt32(),
        element.GetProperty("maxSpeed").GetInt32(),
        element.TryGetProperty("startEdgeIndex", out var start) ? start.GetInt32() : 0);
}

/// <summary>
/// Removes an existing vehicle (data-model.md RemoveVehicleCommand.VehicleId).
/// </summary>
/// <param name="VehicleId">Stable id of a vehicle that has not been removed yet.</param>
public sealed record RemoveVehicleCommand(int VehicleId) : ISimulationCommand
{
    public const string Type = "removeVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.Vehicles.ContainsVehicle(VehicleId)
            ? CommandResult.Success
            : CommandResult.Rejected("vehicle-not-found", $"Vehicle {VehicleId} does not exist.");
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.Vehicles.RemoveVehicle(VehicleId);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("vehicleId", VehicleId);
    }

    internal static RemoveVehicleCommand Read(JsonElement element) => new(element.GetProperty("vehicleId").GetInt32());
}
