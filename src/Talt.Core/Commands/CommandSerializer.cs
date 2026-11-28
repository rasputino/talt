using System.Text.Json;
using Talt.Core.Construction;
using Talt.Core.Roads;

namespace Talt.Core.Commands;

/// <summary>
/// Writes and reads commands as JSON objects with a <c>type</c> discriminator
/// (contracts/recorded-session.md). Uses explicit readers registered per type, no reflection.
/// </summary>
public static class CommandSerializer
{
    private static readonly Dictionary<string, Func<JsonElement, ISimulationCommand>> Readers = new(StringComparer.Ordinal)
    {
        [AddVehicleCommand.Type] = AddVehicleCommand.Read,
        [RemoveVehicleCommand.Type] = RemoveVehicleCommand.Read,
        [TerraformCommand.Type] = TerraformCommand.Read,
        [ClearAreaCommand.Type] = ClearAreaCommand.Read,
        [UndoConstructionCommand.Type] = UndoConstructionCommand.Read,
        [BuildRoadCommand.Type] = BuildRoadCommand.Read,
        [BuildBridgeCommand.Type] = BuildBridgeCommand.Read,
        [BuildTunnelCommand.Type] = BuildTunnelCommand.Read,
        [RemoveRoadCommand.Type] = RemoveRoadCommand.Read,
        [SetOneWayCommand.Type] = SetOneWayCommand.Read,
        [BuildMotorwayCommand.Type] = BuildMotorwayCommand.Read,
        [BuildMotorwayConnectorCommand.Type] = BuildMotorwayConnectorCommand.Read,
        [Stations.BuildStationCommand.Type] = Stations.BuildStationCommand.Read,
        [Stations.BuildWarehouseCommand.Type] = Stations.BuildWarehouseCommand.Read,
        [Stations.RemoveStationCommand.Type] = Stations.RemoveStationCommand.Read,
        [Stations.RenameStationCommand.Type] = Stations.RenameStationCommand.Read,
        [Finance.ChangeLoanCommand.Type] = Finance.ChangeLoanCommand.Read,
        [Finance.EnableInfiniteMoneyCommand.Type] = Finance.EnableInfiniteMoneyCommand.Read,
        [Vehicles.BuildDepotCommand.Type] = Vehicles.BuildDepotCommand.Read,
        [Vehicles.RemoveDepotCommand.Type] = Vehicles.RemoveDepotCommand.Read,
        [Vehicles.SetReplacementCommand.Type] = Vehicles.SetReplacementCommand.Read,
        [Vehicles.BuyVehicleCommand.Type] = Vehicles.BuyVehicleCommand.Read,
        [Vehicles.SellVehicleCommand.Type] = Vehicles.SellVehicleCommand.Read,
        [Vehicles.CloneVehicleCommand.Type] = Vehicles.CloneVehicleCommand.Read,
        [Vehicles.StartVehicleCommand.Type] = Vehicles.StartVehicleCommand.Read,
        [Vehicles.SendToDepotCommand.Type] = Vehicles.SendToDepotCommand.Read,
        [Vehicles.SetVehicleCargoCommand.Type] = Vehicles.SetVehicleCargoCommand.Read,
        [Vehicles.SetServiceIntervalCommand.Type] = Vehicles.SetServiceIntervalCommand.Read,
        [Vehicles.CreateLineCommand.Type] = Vehicles.CreateLineCommand.Read,
        [Vehicles.DeleteLineCommand.Type] = Vehicles.DeleteLineCommand.Read,
        [Vehicles.RenameLineCommand.Type] = Vehicles.RenameLineCommand.Read,
        [Vehicles.SetLineIntervalCommand.Type] = Vehicles.SetLineIntervalCommand.Read,
        [Vehicles.EditLineStopsCommand.Type] = Vehicles.EditLineStopsCommand.Read,
        [Vehicles.AssignLineCommand.Type] = Vehicles.AssignLineCommand.Read,
        [Rails.BuildRailCommand.Type] = Rails.BuildRailCommand.Read,
        [Rails.BuildRailBridgeCommand.Type] = Rails.BuildRailBridgeCommand.Read,
        [Rails.BuildRailTunnelCommand.Type] = Rails.BuildRailTunnelCommand.Read,
        [Rails.RemoveRailCommand.Type] = Rails.RemoveRailCommand.Read,
        [Rails.ConvertRailCommand.Type] = Rails.ConvertRailCommand.Read,
        [Rails.SetSignalCommand.Type] = Rails.SetSignalCommand.Read,
        [Rails.BuildRailStationCommand.Type] = Rails.BuildRailStationCommand.Read,
        [Rails.BuildRailDepotCommand.Type] = Rails.BuildRailDepotCommand.Read,
        [Rails.RemoveRailDepotCommand.Type] = Rails.RemoveRailDepotCommand.Read,
        [Rails.BuyRailVehicleCommand.Type] = Rails.BuyRailVehicleCommand.Read,
        [Rails.SellRailVehicleCommand.Type] = Rails.SellRailVehicleCommand.Read,
        [Rails.StartTrainCommand.Type] = Rails.StartTrainCommand.Read,
        [Rails.SendTrainToDepotCommand.Type] = Rails.SendTrainToDepotCommand.Read,
        [Rails.AssignTrainLineCommand.Type] = Rails.AssignTrainLineCommand.Read,
        [Rails.SetTrainCargoCommand.Type] = Rails.SetTrainCargoCommand.Read,
        [Rails.SetTrainServiceIntervalCommand.Type] = Rails.SetTrainServiceIntervalCommand.Read,
        [Rails.CloneTrainCommand.Type] = Rails.CloneTrainCommand.Read,
    };

    private static readonly Lock Gate = new();

    /// <summary>
    /// Registers an extra command type (used by tests for <c>test.*</c> commands, and later features
    /// for their own commands).
    /// </summary>
    public static void Register(string typeName, Func<JsonElement, ISimulationCommand> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(reader);
        lock (Gate)
        {
            Readers[typeName] = reader;
        }
    }

    public static void Write(Utf8JsonWriter writer, ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(command);
        writer.WriteStartObject();
        writer.WriteString("type", command.TypeName);
        command.WriteProperties(writer);
        writer.WriteEndObject();
    }

    public static ISimulationCommand Read(JsonElement element)
    {
        var type = element.GetProperty("type").GetString()
            ?? throw new JsonException("Command type is missing.");

        Func<JsonElement, ISimulationCommand>? reader;
        lock (Gate)
        {
            Readers.TryGetValue(type, out reader);
        }

        return reader is null
            ? throw new JsonException($"Unknown command type '{type}'.")
            : reader(element);
    }
}
