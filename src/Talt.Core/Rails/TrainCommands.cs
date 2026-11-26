using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Rails;

/// <summary>Shared checks of the train commands (contracts/commands.md).</summary>
internal static class TrainCommand
{
    public static CommandResult Result(string? reason) =>
        reason is null ? CommandResult.Success : CommandResult.Rejected(reason, ConstructionReasons.Describe(reason));

    /// <summary>The train when it exists and belongs to the company; otherwise the refusal.</summary>
    public static string? OwnTrain(GameSimulation simulation, int companyId, int trainId, out TrainSystem system, out Train train)
    {
        system = simulation.Trains!;
        train = null!;
        if (simulation.Trains is null || simulation.Construction?.FindCompany(companyId) is null)
        {
            return ConstructionReasons.NoMap;
        }

        var found = system.Find(trainId);
        if (found is null)
        {
            return ConstructionReasons.NoTrain;
        }

        train = found;
        return train.Owner != companyId ? ConstructionReasons.OwnedByOther : null;
    }

    /// <summary>Selling and recomposing need the train stopped in a depot.</summary>
    public static string? InDepot(Train train) =>
        train.Mode == TrainMode.InDepot && (train.Flags & TrainBits.Started) == 0 ? null : ConstructionReasons.NotInDepot;

    /// <summary>Checks buying <paramref name="model"/> into a new train (−1) or a train stopped in the depot.</summary>
    public static string? CheckPurchase(GameSimulation simulation, int companyId, int depotId, string model, string cargo, int trainId,
        out int modelIndex, out int cargoIndex, out long price)
    {
        modelIndex = -1;
        cargoIndex = -1;
        price = 0;
        var system = simulation.Trains;
        var company = simulation.Construction?.FindCompany(companyId);
        if (system is null || company is null)
        {
            return ConstructionReasons.NoMap;
        }

        var depot = system.Depots.IndexOf(depotId);
        if (depot < 0)
        {
            return ConstructionReasons.NoDepot;
        }

        if (system.Depots.OwnerAt(depot) != companyId)
        {
            return ConstructionReasons.OwnedByOther;
        }

        modelIndex = system.Rules.FindModel(model ?? string.Empty);
        if (modelIndex < 0)
        {
            return ConstructionReasons.UnknownModel;
        }

        var definition = system.Rules.Models[modelIndex];
        if (!definition.AvailableIn(simulation.Date.Year))
        {
            return ConstructionReasons.ModelNotAvailable;
        }

        cargoIndex = string.IsNullOrEmpty(cargo) ? definition.DefaultCargo : simulation.Content.CargoIndex(cargo);
        if (definition.CargoMask != 0 && !definition.Carries(cargoIndex))
        {
            return ConstructionReasons.CargoNotAllowed;
        }

        if (trainId < 0)
        {
            if (definition.Kind != RollingStockKind.Locomotive)
            {
                return ConstructionReasons.NeedsLocomotive;
            }
        }
        else
        {
            var reason = OwnTrain(simulation, companyId, trainId, out _, out var train) ?? InDepot(train);
            if (reason is not null)
            {
                return reason;
            }

            if (train.Depot != depotId)
            {
                return ConstructionReasons.NotInDepot;
            }

            if (train.Parts.Count >= system.Rules.MaxParts || train.Length + definition.Length > system.Rules.MaxTrainLength)
            {
                return ConstructionReasons.TrainTooLong;
            }
        }

        if (system.Count >= 100_000)
        {
            return ConstructionReasons.TooManyVehicles;
        }

        price = simulation.Finance?.Inflate(definition.Price) ?? definition.Price;
        return price > company.Balance && !company.HasInfiniteMoney ? ConstructionReasons.NotEnoughMoney : null;
    }

    public static void WriteIds(Utf8JsonWriter writer, int companyId, int trainId)
    {
        writer.WriteNumber("companyId", companyId);
        writer.WriteNumber("trainId", trainId);
    }
}

/// <summary>
/// Buys a locomotive or wagon in a rail depot (contracts/commands.md <c>buyRailVehicle</c>): a new train for
/// <paramref name="TrainId"/> −1 (locomotives only), otherwise added to a train stopped in that depot.
/// </summary>
public sealed record BuyRailVehicleCommand(int CompanyId, int DepotId, string Model, string Cargo = "", int TrainId = -1) : ISimulationCommand
{
    public const string Type = "buyRailVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return TrainCommand.Result(TrainCommand.CheckPurchase(simulation, CompanyId, DepotId, Model, Cargo, TrainId, out _, out _, out _));
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        TrainCommand.CheckPurchase(simulation, CompanyId, DepotId, Model, Cargo, TrainId, out var model, out var cargo, out var price);
        simulation.Trains!.Buy(CompanyId, DepotId, model, cargo, TrainId, price);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("depotId", DepotId);
        writer.WriteString("model", Model);
        writer.WriteString("cargo", Cargo);
        writer.WriteNumber("trainId", TrainId);
    }

    internal static BuyRailVehicleCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("depotId").GetInt32(), e.GetProperty("model").GetString() ?? "",
        e.TryGetProperty("cargo", out var c) ? c.GetString() ?? "" : "", e.TryGetProperty("trainId", out var t) ? t.GetInt32() : -1);
}

/// <summary>Sells a part (−1: the whole train) of a train stopped in its depot (contracts/commands.md <c>sellRailVehicle</c>).</summary>
public sealed record SellRailVehicleCommand(int CompanyId, int TrainId, int Part = -1) : ISimulationCommand
{
    public const string Type = "sellRailVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out _, out var train);
        reason ??= TrainCommand.InDepot(train);
        reason ??= Part >= train.Parts.Count || Part < -1 ? ConstructionReasons.InvalidIndex : null;
        return TrainCommand.Result(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.Trains!;
        system.Sell(system.Find(TrainId)!, Part);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
        writer.WriteNumber("part", Part);
    }

    internal static SellRailVehicleCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32(), e.TryGetProperty("part", out var p) ? p.GetInt32() : -1);
}

/// <summary>Starts or stops a train (contracts/commands.md <c>startTrain</c>).</summary>
public sealed record StartTrainCommand(int CompanyId, int TrainId, bool Start) : ISimulationCommand
{
    public const string Type = "startTrain";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out _, out var train);
        reason ??= Start && train.PowerKw <= 0 ? ConstructionReasons.NeedsLocomotive : null;
        return TrainCommand.Result(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.Trains!;
        system.SetStarted(system.Find(TrainId)!, Start);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
        writer.WriteBoolean("start", Start);
    }

    internal static StartTrainCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32(), e.GetProperty("start").GetBoolean());
}

/// <summary>Sends a train to the nearest depot, or cancels that (contracts/commands.md <c>sendTrainToDepot</c>).</summary>
public sealed record SendTrainToDepotCommand(int CompanyId, int TrainId) : ISimulationCommand
{
    public const string Type = "sendTrainToDepot";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return TrainCommand.Result(TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out _, out _));
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.Trains!;
        system.ToggleSendToDepot(system.Find(TrainId)!);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
    }

    internal static SendTrainToDepotCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32());
}

/// <summary>Assigns a train to a line, −1 unassigns (contracts/commands.md <c>assignTrainLine</c>, research R13).</summary>
public sealed record AssignTrainLineCommand(int CompanyId, int TrainId, int LineId) : ISimulationCommand
{
    public const string Type = "assignTrainLine";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out var system, out var train);
        if (reason is null && LineId >= 0)
        {
            reason = !system.Lines.Exists(LineId) ? ConstructionReasons.NoLine
                : system.Lines.OwnerOf(LineId) != CompanyId ? ConstructionReasons.OwnedByOther : null;
        }

        reason ??= LineId >= 0 && train.PowerKw <= 0 ? ConstructionReasons.NeedsLocomotive : null;
        return TrainCommand.Result(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.Trains!;
        system.AssignLine(system.Find(TrainId)!, LineId);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
        writer.WriteNumber("lineId", LineId);
    }

    internal static AssignTrainLineCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32(), e.GetProperty("lineId").GetInt32());
}

/// <summary>Changes the cargo of an empty part of a train stopped in a depot (contracts/commands.md <c>setTrainCargo</c>).</summary>
public sealed record SetTrainCargoCommand(int CompanyId, int TrainId, int Part, string Cargo) : ISimulationCommand
{
    public const string Type = "setTrainCargo";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out var system, out var train);
        reason ??= TrainCommand.InDepot(train);
        reason ??= (uint)Part >= (uint)train.Parts.Count ? ConstructionReasons.InvalidIndex : null;
        if (reason is null)
        {
            var part = train.Parts[Part];
            reason = part.Hold.Count > 0 ? ConstructionReasons.HoldNotEmpty
                : !system.Rules.Models[part.Model].Carries(simulation.Content.CargoIndex(Cargo ?? "")) ? ConstructionReasons.CargoNotAllowed : null;
        }

        return TrainCommand.Result(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        TrainSystem.SetCargo(simulation.Trains!.Find(TrainId)!, Part, simulation.Content.CargoIndex(Cargo));
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
        writer.WriteNumber("part", Part);
        writer.WriteString("cargo", Cargo);
    }

    internal static SetTrainCargoCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32(), e.GetProperty("part").GetInt32(), e.GetProperty("cargo").GetString() ?? "");
}

/// <summary>Sets a train's service interval in days, 0 = never (contracts/commands.md <c>setTrainServiceInterval</c>).</summary>
public sealed record SetTrainServiceIntervalCommand(int CompanyId, int TrainId, int Days) : ISimulationCommand
{
    public const string Type = "setTrainServiceInterval";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out _, out _);
        reason ??= Days is < 0 or > 3650 ? ConstructionReasons.InvalidValue : null;
        return TrainCommand.Result(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        TrainSystem.SetServiceInterval(simulation.Trains!.Find(TrainId)!, Days);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
        writer.WriteNumber("days", Days);
    }

    internal static SetTrainServiceIntervalCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32(), e.GetProperty("days").GetInt32());
}

/// <summary>Buys a copy of a train (same parts, cargo and line) in its depot (contracts/commands.md <c>cloneTrain</c>).</summary>
public sealed record CloneTrainCommand(int CompanyId, int TrainId) : ISimulationCommand
{
    public const string Type = "cloneTrain";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = TrainCommand.OwnTrain(simulation, CompanyId, TrainId, out var system, out var train);
        if (reason is null)
        {
            var depot = train.Depot >= 0 ? train.Depot : train.HomeDepot;
            reason = system.Depots.IndexOf(depot) < 0 ? ConstructionReasons.NoDepot : null;
            long total = 0;
            foreach (var part in train.Parts)
            {
                var model = system.Rules.Models[part.Model];
                reason ??= model.AvailableIn(simulation.Date.Year) ? null : ConstructionReasons.ModelNotAvailable;
                total += simulation.Finance?.Inflate(model.Price) ?? model.Price;
            }

            var company = simulation.Construction!.FindCompany(CompanyId)!;
            reason ??= total > company.Balance && !company.HasInfiniteMoney ? ConstructionReasons.NotEnoughMoney : null;
        }

        return TrainCommand.Result(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.Trains!;
        var train = system.Find(TrainId)!;
        var depot = train.Depot >= 0 ? train.Depot : train.HomeDepot;
        var id = -1;
        foreach (var part in train.Parts)
        {
            var model = system.Rules.Models[part.Model];
            id = system.Buy(CompanyId, depot, part.Model, part.Cargo, id, simulation.Finance?.Inflate(model.Price) ?? model.Price);
        }

        var copy = system.Find(id)!;
        copy.ServiceInterval = train.ServiceInterval;
        if (train.Line >= 0)
        {
            system.AssignLine(copy, train.Line);
        }
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TrainCommand.WriteIds(writer, CompanyId, TrainId);
    }

    internal static CloneTrainCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("trainId").GetInt32());
}
