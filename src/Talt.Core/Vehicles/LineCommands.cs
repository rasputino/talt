using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>Line checks shared by the line commands.</summary>
internal static class LineCommand
{
    public static string? OwnLine(GameSimulation simulation, int companyId, int lineId, out RoadVehicleSystem system)
    {
        system = simulation.RoadVehicles!;
        if (simulation.RoadVehicles is null)
        {
            return ConstructionReasons.NoMap;
        }

        if (!system.Lines.Exists(lineId))
        {
            return ConstructionReasons.NoLine;
        }

        return system.Lines.OwnerOf(lineId) != companyId ? ConstructionReasons.OwnedByOther : null;
    }

    public static string? OwnStation(GameSimulation simulation, int companyId, int stationId)
    {
        var stations = simulation.Stations;
        var slot = stations?.SlotOf(stationId) ?? -1;
        if (slot < 0)
        {
            return ConstructionReasons.NoStation;
        }

        return stations!.OwnerAt(slot) != companyId ? ConstructionReasons.OwnedByOther : null;
    }
}

/// <summary>
/// Creates a line with stations in order (contracts/commands.md <c>createLine</c>, FR-011); with a vehicle id, the
/// vehicle is assigned to it (and starts when it waits in a depot).
/// </summary>
public sealed record CreateLineCommand(int CompanyId, IReadOnlyList<int> Stations, int VehicleId = -1) : ISimulationCommand
{
    public const string Type = "createLine";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = simulation.RoadVehicles is null ? ConstructionReasons.NoMap
            : Stations is null || Stations.Count == 0 ? ConstructionReasons.LastStop
            : Stations.Count > LineRegistry.MaxStops ? ConstructionReasons.TooManyStops
            : null;
        for (var i = 0; reason is null && i < Stations!.Count; i++)
        {
            reason = LineCommand.OwnStation(simulation, CompanyId, Stations[i]);
        }

        if (reason is null && VehicleId >= 0)
        {
            reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out _, out _);
        }

        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        var stations = new int[Stations.Count];
        for (var i = 0; i < stations.Length; i++)
        {
            stations[i] = Stations[i];
        }

        var line = system.Lines.Create(CompanyId, stations);
        if (VehicleId >= 0)
        {
            system.AssignLine(system.SlotOf(VehicleId), line);
        }
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteStartArray("stations");
        foreach (var station in Stations)
        {
            writer.WriteNumberValue(station);
        }

        writer.WriteEndArray();
        writer.WriteNumber("vehicleId", VehicleId);
    }

    internal static CreateLineCommand Read(JsonElement e)
    {
        var stations = new List<int>();
        foreach (var s in e.GetProperty("stations").EnumerateArray())
        {
            stations.Add(s.GetInt32());
        }

        return new CreateLineCommand(e.GetProperty("companyId").GetInt32(), stations,
            e.TryGetProperty("vehicleId", out var v) ? v.GetInt32() : -1);
    }

    public bool Equals(CreateLineCommand? other) =>
        other is not null && CompanyId == other.CompanyId && VehicleId == other.VehicleId && Stations.SequenceEqual(other.Stations);

    public override int GetHashCode() => HashCode.Combine(CompanyId, VehicleId, Stations.Count);
}

/// <summary>Deletes a line; its vehicles lose it (contracts/commands.md <c>deleteLine</c>).</summary>
public sealed record DeleteLineCommand(int CompanyId, int LineId) : ISimulationCommand
{
    public const string Type = "deleteLine";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = LineCommand.OwnLine(simulation, CompanyId, LineId, out _);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        system.Lines.Delete(LineId);
        system.OnLineChanged(LineId, deleted: true);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("lineId", LineId);
    }

    internal static DeleteLineCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("lineId").GetInt32());
}

/// <summary>Renames a line; an empty name restores the automatic one (contracts/commands.md <c>renameLine</c>).</summary>
public sealed record RenameLineCommand(int CompanyId, int LineId, string Name) : ISimulationCommand
{
    public const string Type = "renameLine";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = LineCommand.OwnLine(simulation, CompanyId, LineId, out _)
            ?? ((Name ?? string.Empty).Trim().Length > LineRegistry.MaxNameLength ? ConstructionReasons.NameTooLong : null);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.RoadVehicles!.Lines.Rename(LineId, Name);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("lineId", LineId);
        writer.WriteString("name", Name);
    }

    internal static RenameLineCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("lineId").GetInt32(), e.GetProperty("name").GetString() ?? "");
}

/// <summary>What <see cref="EditLineStopsCommand"/> does.</summary>
public enum LineStopEdit : byte
{
    /// <summary>Insert <c>StationId</c> at <c>Index</c> (the stop count appends).</summary>
    Add = 0,

    /// <summary>Remove the stop at <c>Index</c>.</summary>
    Remove = 1,

    /// <summary>Move the stop at <c>Index</c> to <c>To</c>.</summary>
    Move = 2,

    /// <summary>Set the rule of the stop at <c>Index</c>.</summary>
    Rule = 3,
}

/// <summary>Edits the stops of a line (contracts/commands.md <c>editLineStops</c>, FR-011, FR-012).</summary>
public sealed record EditLineStopsCommand(int CompanyId, int LineId, LineStopEdit Op, int Index, int StationId = -1, int To = 0, StopRule Rule = StopRule.LoadIfAvailable)
    : ISimulationCommand
{
    public const string Type = "editLineStops";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = LineCommand.OwnLine(simulation, CompanyId, LineId, out var system);
        if (reason is null)
        {
            var count = system.Lines.StopCount(LineId);
            reason = Op switch
            {
                LineStopEdit.Add => Index < 0 || Index > count ? ConstructionReasons.InvalidIndex
                    : count >= LineRegistry.MaxStops ? ConstructionReasons.TooManyStops
                    : LineCommand.OwnStation(simulation, CompanyId, StationId),
                LineStopEdit.Remove => Index < 0 || Index >= count ? ConstructionReasons.InvalidIndex
                    : count == 1 ? ConstructionReasons.LastStop : null,
                LineStopEdit.Move => Index < 0 || Index >= count || To < 0 || To >= count ? ConstructionReasons.InvalidIndex : null,
                LineStopEdit.Rule => Index < 0 || Index >= count ? ConstructionReasons.InvalidIndex
                    : (byte)Rule > (byte)StopRule.NoLoading ? ConstructionReasons.InvalidValue : null,
                _ => ConstructionReasons.InvalidValue,
            };
        }

        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        var lines = system.Lines;
        switch (Op)
        {
            case LineStopEdit.Add:
                lines.InsertStop(LineId, Index, StationId);
                break;
            case LineStopEdit.Remove:
                lines.RemoveStop(LineId, Index);
                break;
            case LineStopEdit.Move:
                lines.MoveStop(LineId, Index, To);
                break;
            case LineStopEdit.Rule:
                lines.SetRule(LineId, Index, Rule);
                return; // The stops did not change.
        }

        system.OnLineChanged(LineId, deleted: false);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("lineId", LineId);
        writer.WriteNumber("op", (int)Op);
        writer.WriteNumber("index", Index);
        writer.WriteNumber("stationId", StationId);
        writer.WriteNumber("to", To);
        writer.WriteNumber("rule", (int)Rule);
    }

    internal static EditLineStopsCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("lineId").GetInt32(), (LineStopEdit)e.GetProperty("op").GetInt32(),
        e.GetProperty("index").GetInt32(), e.GetProperty("stationId").GetInt32(), e.GetProperty("to").GetInt32(),
        (StopRule)e.GetProperty("rule").GetInt32());
}

/// <summary>Assigns a vehicle to a line, or unassigns it with −1 (contracts/commands.md <c>assignLine</c>).</summary>
public sealed record AssignLineCommand(int CompanyId, int VehicleId, int LineId) : ISimulationCommand
{
    public const string Type = "assignLine";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out var system, out var slot);
        if (reason is null && LineId >= 0)
        {
            reason = LineCommand.OwnLine(simulation, CompanyId, LineId, out _);
        }

        if (reason is null && LineId >= 0)
        {
            // The line must have a stop of the vehicle's kind (bus or truck).
            var kind = system.ModelAt(slot).Kind;
            var stations = simulation.Stations!;
            var usable = false;
            for (var i = 0; !usable && i < system.Lines.StopCount(LineId); i++)
            {
                var stationSlot = stations.SlotOf(system.Lines.StopAt(LineId, i).StationId);
                if (stationSlot < 0)
                {
                    continue;
                }

                foreach (var tile in stations.TilesAt(stationSlot))
                {
                    if (stations.TryGetPieceAt(tile, out _, out var type, out _) && type != Stations.StationRegistry.WarehousePiece
                        && stations.Rules.Pieces[type].Kind == kind)
                    {
                        usable = true;
                        break;
                    }
                }
            }

            reason = usable ? null : ConstructionReasons.WrongKind;
        }

        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        system.AssignLine(system.SlotOf(VehicleId), LineId);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
        writer.WriteNumber("lineId", LineId);
    }

    internal static AssignLineCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32(), e.GetProperty("lineId").GetInt32());
}
