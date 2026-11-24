using System.Text.Json;
using Talt.Core.Construction;

namespace Talt.Core.Roads;

/// <summary>Builds a road along the proposed path between two tiles (contracts/commands.md <c>buildRoad</c>).</summary>
public sealed record BuildRoadCommand(int CompanyId, string RoadType, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildRoad";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("roadType", RoadType);
        RoadCommandJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanRoad(state, CompanyId, state.RoadRules.FindType(RoadType), (X0, Z0), (X1, Z1), plan);

    internal static BuildRoadCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("roadType").GetString() ?? "",
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>Builds a straight bridge between two end tiles (contracts/commands.md <c>buildBridge</c>).</summary>
public sealed record BuildBridgeCommand(int CompanyId, string RoadType, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildBridge";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("roadType", RoadType);
        RoadCommandJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanBridge(state, CompanyId, state.RoadRules.FindType(RoadType), (X0, Z0), (X1, Z1), plan);

    internal static BuildBridgeCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("roadType").GetString() ?? "",
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>
/// Builds a straight tunnel from a portal tile (contracts/commands.md <c>buildTunnel</c>);
/// <paramref name="Direction"/> is a straight <see cref="Dir8"/>, or −1 for the single higher neighbour.
/// </summary>
public sealed record BuildTunnelCommand(int CompanyId, string RoadType, int X, int Z, int Direction) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildTunnel";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("roadType", RoadType);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
        writer.WriteNumber("direction", Direction);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanTunnel(state, CompanyId, state.RoadRules.FindType(RoadType), (X, Z), Direction, plan);

    internal static BuildTunnelCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("roadType").GetString() ?? "",
        e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32(), e.GetProperty("direction").GetInt32());
}

/// <summary>Removes road pieces along a straight or 45° line (contracts/commands.md <c>removeRoad</c>).</summary>
public sealed record RemoveRoadCommand(int CompanyId, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "removeRoad";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        RoadCommandJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanRemoval(state, CompanyId, (X0, Z0), (X1, Z1), plan);

    internal static RemoveRoadCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(),
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>Cycles a straight piece two-way → one-way → other way → two-way (contracts/commands.md <c>setOneWay</c>).</summary>
public sealed record SetOneWayCommand(int CompanyId, int X, int Z) : ConstructionCommand(CompanyId)
{
    public const string Type = "setOneWay";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanOneWay(state, CompanyId, (X, Z), plan);

    internal static SetOneWayCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32());
}

/// <summary>Builds two motorway carriageways along a straight or 45° line (contracts/commands.md <c>buildMotorway</c>).</summary>
public sealed record BuildMotorwayCommand(int CompanyId, int X0, int Z0, int X1, int Z1) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildMotorway";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        RoadCommandJson.WriteLine(writer, X0, Z0, X1, Z1);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanMotorway(state, CompanyId, (X0, Z0), (X1, Z1), plan);

    internal static BuildMotorwayCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(),
        e.GetProperty("x0").GetInt32(), e.GetProperty("z0").GetInt32(), e.GetProperty("x1").GetInt32(), e.GetProperty("z1").GetInt32());
}

/// <summary>Builds a motorway exit or entry ramp (contracts/commands.md <c>buildMotorwayConnector</c>).</summary>
public sealed record BuildMotorwayConnectorCommand(int CompanyId, int X, int Z, bool Exit) : ConstructionCommand(CompanyId)
{
    public const string Type = "buildMotorwayConnector";

    public override string TypeName => Type;

    public override void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("x", X);
        writer.WriteNumber("z", Z);
        writer.WriteBoolean("exit", Exit);
    }

    protected override void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan) =>
        planner.Roads.PlanConnector(state, CompanyId, (X, Z), Exit, plan);

    internal static BuildMotorwayConnectorCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("x").GetInt32(), e.GetProperty("z").GetInt32(), e.GetProperty("exit").GetBoolean());
}

internal static class RoadCommandJson
{
    public static void WriteLine(Utf8JsonWriter writer, int x0, int z0, int x1, int z1)
    {
        writer.WriteNumber("x0", x0);
        writer.WriteNumber("z0", z0);
        writer.WriteNumber("x1", x1);
        writer.WriteNumber("z1", z1);
    }
}
