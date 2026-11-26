using System.Text.Json;
using Talt.Core.Construction;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Rails;

/// <summary>Builds track along the proposed path between two tiles (contracts/commands.md <c>buildRail</c>).</summary>
public sealed record BuildRailCommand(int CompanyId, string RailType, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildRail";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("railType", RailType);
        RailJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanTrack(state, CompanyId, state.RailRules.FindType(RailType), (X0, Z0), (X1, Z1), plan);

    internal static BuildRailCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("railType").GetString() ?? "",
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>Builds a straight rail bridge (contracts/commands.md <c>buildRailBridge</c>).</summary>
public sealed record BuildRailBridgeCommand(int CompanyId, string RailType, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildRailBridge";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("railType", RailType);
        RailJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanBridge(state, CompanyId, state.RailRules.FindType(RailType), (X0, Z0), (X1, Z1), plan);

    internal static BuildRailBridgeCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("railType").GetString() ?? "",
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>Builds a straight rail tunnel from a portal (contracts/commands.md <c>buildRailTunnel</c>); −1 picks the direction.</summary>
public sealed record BuildRailTunnelCommand(int CompanyId, string RailType, int X, int Z, int Direction) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildRailTunnel";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("railType", RailType);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
        writer.WriteNumber("direction", Direction);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanTunnel(state, CompanyId, state.RailRules.FindType(RailType), (X, Z), Direction, plan);

    internal static BuildRailTunnelCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("railType").GetString() ?? "",
        e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32(), e.GetProperty("direction").GetInt32());
}

/// <summary>Removes track along a straight or 45° line (contracts/commands.md <c>removeRail</c>).</summary>
public sealed record RemoveRailCommand(int CompanyId, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "removeRail";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        RailJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanRemoval(state, CompanyId, (X0, Z0), (X1, Z1), plan);

    internal static RemoveRailCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(),
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>Changes the type of the company's track on a line, e.g. electrifies it (contracts/commands.md <c>convertRail</c>).</summary>
public sealed record ConvertRailCommand(int CompanyId, string RailType, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "convertRail";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("railType", RailType);
        RailJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanConversion(state, CompanyId, state.RailRules.FindType(RailType), (X0, Z0), (X1, Z1), plan);

    internal static ConvertRailCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("railType").GetString() ?? "",
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>
/// Places, changes or removes a signal (contracts/commands.md <c>setSignal</c>): kind 0 removes; faces bit 0 protects travel
/// towards the tile's lower link direction, bit 1 the other way.
/// </summary>
public sealed record SetSignalCommand(int CompanyId, int X, int Z, int Kind, int Faces) : ConstructionCommand(CompanyId)
{
    public const string Type = "setSignal";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
        writer.WriteNumber("kind", Kind);
        writer.WriteNumber("faces", Faces);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanSignal(state, CompanyId, X, Z, (SignalKind)Math.Clamp(Kind, 0, 255), Faces, plan);

    internal static SetSignalCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32(),
        e.GetProperty("kind").GetInt32(), e.GetProperty("faces").GetInt32());
}

/// <summary>Builds a rail station of platforms × length tiles (contracts/commands.md <c>buildRailStation</c>).</summary>
public sealed record BuildRailStationCommand(int CompanyId, int X, int Z, int Axis, int Platforms, int Length) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildRailStation";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
        writer.WriteNumber("axis", Axis);
        writer.WriteNumber("platforms", Platforms);
        writer.WriteNumber("length", Length);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanStation(state, CompanyId, PieceType(state), X, Z, Axis, Platforms, Length, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        var (x1, z1) = Axis == 0 ? (X + Length - 1, Z + Platforms - 1) : (X + Platforms - 1, Z + Length - 1);
        state.Stations!.AddRailPieces(CompanyId, PieceType(state), X, Z, x1, z1, Axis);
        state.Undo.Clear(); // Station pieces are not tile data, so the action cannot be undone (like road stations).
    }

    private static int PieceType(ConstructionState state) =>
        state.Stations?.Rules.FindPiece(StationPieceKind.Rail, StationPieceForm.Platform) ?? -1;

    internal static BuildRailStationCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32(),
        e.GetProperty("axis").GetInt32(), e.GetProperty("platforms").GetInt32(), e.GetProperty("length").GetInt32());
}

/// <summary>Builds a rail depot with its entrance towards a straight direction (contracts/commands.md <c>buildRailDepot</c>).</summary>
public sealed record BuildRailDepotCommand(int CompanyId, int X, int Z, int Direction) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildRailDepot";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
        writer.WriteNumber("direction", Direction);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Rails.PlanDepot(state, CompanyId, X, Z, Direction, plan);

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        state.RailDepots.Add(CompanyId, state.Map.Index(X, Z), Direction);
        state.Undo.Clear();
    }

    internal static BuildRailDepotCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32(), e.GetProperty("direction").GetInt32());
}

/// <summary>Removes an empty rail depot (contracts/commands.md <c>removeRailDepot</c>).</summary>
public sealed record RemoveRailDepotCommand(int CompanyId, int DepotId) : ConstructionCommand(CompanyId)
{
    public const string Type = "removeRailDepot";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("depotId", DepotId);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan)
    {
        var index = state.RailDepots.IndexOf(DepotId);
        planner.Rails.PlanDepotRemoval(state, CompanyId, index < 0 ? -1 : state.RailDepots.TileAt(index), plan);
    }

    protected override void Committed(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        state.RailDepots.Remove(DepotId);
        state.Undo.Clear();
    }

    internal static RemoveRailDepotCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("depotId").GetInt32());
}

internal static class RailJson
{
    public static void WriteLine(Utf8JsonWriter writer, int x0, int z0, int x1, int z1)
    {
        writer.WriteNumber("x0", x0);
        writer.WriteNumber("z0", z0);
        writer.WriteNumber("x1", x1);
        writer.WriteNumber("z1", z1);
    }
}
