using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Stations;

/// <summary>
/// Builds a road station piece (contracts/commands.md <c>buildStation</c>): <paramref name="PieceType"/> is a
/// piece id of <c>stations.json</c> (<c>bus-bay</c>, <c>bus-stop</c>, <c>truck-bay</c>, <c>truck-stop</c>).
/// </summary>
public sealed record BuildStationCommand(int CompanyId, string PieceType, int X, int Z) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildStation";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("pieceType", PieceType);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        StationPlanner.PlanPiece(state, CompanyId, state.Stations?.Rules.FindPiece(PieceType) ?? -1, X, Z, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        state.Stations!.AddRoadPiece(CompanyId, state.Stations.Rules.FindPiece(PieceType), X, Z);
        state.Undo.Clear(); // Station state is not tile data, so the action cannot be undone (research R12).
    }

    internal static BuildStationCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("pieceType").GetString() ?? "",
        e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32());
}

/// <summary>Builds a warehouse (contracts/commands.md <c>buildWarehouse</c>); (X, Z) is the footprint's minimum corner.</summary>
public sealed record BuildWarehouseCommand(int CompanyId, string Size, int X, int Z) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildWarehouse";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("size", Size);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        StationPlanner.PlanWarehouse(state, CompanyId, state.Stations?.Rules.FindWarehouse(Size) ?? -1, X, Z, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        state.Stations!.AddWarehouse(CompanyId, state.Stations.Rules.FindWarehouse(Size), X, Z);
        state.Undo.Clear();
    }

    internal static BuildWarehouseCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("size").GetString() ?? "",
        e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32());
}

/// <summary>Removes the company's station piece on a tile, or the whole warehouse (contracts/commands.md <c>removeStation</c>).</summary>
public sealed record RemoveStationCommand(int CompanyId, int X, int Z) : ConstructionCommand(CompanyId)
{
    public const string Type = "removeStation";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        StationPlanner.PlanRemoval(state, CompanyId, X, Z, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        state.Stations!.RemovePieceAt(X, Z);
        state.Undo.Clear();
    }

    internal static RemoveStationCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32());
}

/// <summary>
/// Gives a station or warehouse a custom name (contracts/commands.md <c>renameStation</c>); an empty name
/// restores the automatic one.
/// </summary>
public sealed record RenameStationCommand(int CompanyId, int StationId, string Name) : ISimulationCommand
{
    public const string Type = "renameStation";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var stations = simulation.Construction?.Stations;
        if (stations is null)
        {
            return Rejected(ConstructionReasons.NoMap);
        }

        var slot = stations.SlotOf(StationId);
        if (slot < 0)
        {
            return Rejected(ConstructionReasons.NoStation);
        }

        if (stations.OwnerAt(slot) != CompanyId)
        {
            return Rejected(ConstructionReasons.OwnedByOther);
        }

        return (Name ?? string.Empty).Trim().Length > StationRegistry.MaxNameLength
            ? Rejected(ConstructionReasons.NameTooLong)
            : CommandResult.Success;
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        simulation.Construction!.Stations!.Rename(StationId, Name);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("stationId", StationId);
        writer.WriteString("name", Name);
    }

    internal static RenameStationCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("stationId").GetInt32(), e.GetProperty("name").GetString() ?? "");

    private static CommandResult Rejected(string reason) => CommandResult.Rejected(reason, ConstructionReasons.Describe(reason));
}
