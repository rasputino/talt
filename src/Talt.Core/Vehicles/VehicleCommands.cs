using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>Shared checks of the vehicle and line commands (contracts/commands.md).</summary>
internal static class VehicleCommand
{
    public static CommandResult Rejected(string reason) => CommandResult.Rejected(reason, ConstructionReasons.Describe(reason));

    /// <summary>The vehicle's slot when it exists and belongs to the company; otherwise the refusal.</summary>
    public static string? OwnVehicle(GameSimulation simulation, int companyId, int vehicleId, out RoadVehicleSystem system, out int slot)
    {
        system = simulation.RoadVehicles!;
        slot = -1;
        if (simulation.RoadVehicles is null || simulation.Construction?.FindCompany(companyId) is null)
        {
            return ConstructionReasons.NoMap;
        }

        slot = system.SlotOf(vehicleId);
        if (slot < 0)
        {
            return ConstructionReasons.NoVehicle;
        }

        return system.OwnerAt(slot) != companyId ? ConstructionReasons.OwnedByOther : null;
    }

    /// <summary>Checks a purchase of <paramref name="model"/> carrying <paramref name="cargo"/> ("" = default) in a depot.</summary>
    public static string? CheckPurchase(GameSimulation simulation, int companyId, int depotId, string model, string cargo,
        out int modelIndex, out int cargoIndex, out long price)
    {
        modelIndex = -1;
        cargoIndex = -1;
        price = 0;
        var system = simulation.RoadVehicles;
        var company = simulation.Construction?.FindCompany(companyId);
        if (system is null || company is null)
        {
            return ConstructionReasons.NoMap;
        }

        var depot = system.Depots.SlotOf(depotId);
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
        if (!definition.Carries(cargoIndex))
        {
            return ConstructionReasons.CargoNotAllowed;
        }

        if (system.Count >= 1_000_000)
        {
            return ConstructionReasons.TooManyVehicles;
        }

        price = simulation.Finance?.Inflate(definition.Price) ?? definition.Price;
        return price > company.Balance && !company.HasInfiniteMoney ? ConstructionReasons.NotEnoughMoney : null;
    }

    /// <summary>Pays and adds the vehicle; returns its id.</summary>
    public static int Purchase(GameSimulation simulation, int companyId, int depotId, int modelIndex, int cargoIndex, long price)
    {
        var company = simulation.Construction!.FindCompany(companyId)!;
        company.Book(LedgerCategory.VehiclePurchase, -price);
        return simulation.RoadVehicles!.Add(companyId, modelIndex, cargoIndex, depotId, price, simulation.Date.DayNumber);
    }
}

/// <summary>Buys a road vehicle in a depot (contracts/commands.md <c>buyVehicle</c>, FR-007).</summary>
public sealed record BuyVehicleCommand(int CompanyId, int DepotId, string Model, string Cargo = "") : ISimulationCommand
{
    public const string Type = "buyVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.CheckPurchase(simulation, CompanyId, DepotId, Model, Cargo, out _, out _, out _);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        VehicleCommand.CheckPurchase(simulation, CompanyId, DepotId, Model, Cargo, out var model, out var cargo, out var price);
        VehicleCommand.Purchase(simulation, CompanyId, DepotId, model, cargo, price);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("depotId", DepotId);
        writer.WriteString("model", Model);
        writer.WriteString("cargo", Cargo);
    }

    internal static BuyVehicleCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("depotId").GetInt32(), e.GetProperty("model").GetString() ?? "",
        e.TryGetProperty("cargo", out var cargo) ? cargo.GetString() ?? "" : "");
}

/// <summary>Sells a vehicle stopped in a depot (contracts/commands.md <c>sellVehicle</c>, FR-008).</summary>
public sealed record SellVehicleCommand(int CompanyId, int VehicleId) : ISimulationCommand
{
    public const string Type = "sellVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out var system, out var slot);
        if (reason is null && system.DepotAt(slot) < 0)
        {
            reason = ConstructionReasons.NotInDepot;
        }

        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        var slot = system.SlotOf(VehicleId);
        simulation.Construction!.FindCompany(CompanyId)!.Book(LedgerCategory.VehiclePurchase, system.ValueAt(slot));
        system.Remove(VehicleId);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
    }

    internal static SellVehicleCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32());
}

/// <summary>Buys another vehicle like this one, with its line and settings (contracts/commands.md <c>cloneVehicle</c>, FR-009).</summary>
public sealed record CloneVehicleCommand(int CompanyId, int VehicleId) : ISimulationCommand
{
    public const string Type = "cloneVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = Check(simulation, out _, out _, out _, out _, out _);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        Check(simulation, out var slot, out var depot, out var model, out var cargo, out var price);
        var system = simulation.RoadVehicles!;
        var id = VehicleCommand.Purchase(simulation, CompanyId, depot, model, cargo, price);
        var copy = system.SlotOf(id);
        system.SetServiceInterval(copy, system.ServiceIntervalAt(slot));
        if (system.LineAt(slot) >= 0)
        {
            system.AssignLine(copy, system.LineAt(slot));
        }
    }

    private string? Check(GameSimulation simulation, out int slot, out int depot, out int model, out int cargo, out long price)
    {
        depot = -1;
        model = cargo = -1;
        price = 0;
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out var system, out slot);
        if (reason is not null)
        {
            return reason;
        }

        depot = system.DepotAt(slot) >= 0 ? system.DepotAt(slot) : system.HomeDepotAt(slot);
        var cargoId = simulation.Content.Cargo(system.CargoAt(slot)).Id;
        return VehicleCommand.CheckPurchase(simulation, CompanyId, depot, system.ModelAt(slot).Id, cargoId, out model, out cargo, out price);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
    }

    internal static CloneVehicleCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32());
}

/// <summary>Starts or stops a vehicle (contracts/commands.md <c>startVehicle</c>).</summary>
public sealed record StartVehicleCommand(int CompanyId, int VehicleId, bool Start) : ISimulationCommand
{
    public const string Type = "startVehicle";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out _, out _);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        system.SetStopped(system.SlotOf(VehicleId), !Start);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
        writer.WriteBoolean("start", Start);
    }

    internal static StartVehicleCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32(), e.GetProperty("start").GetBoolean());
}

/// <summary>Sends a vehicle to the nearest depot, or cancels that order (contracts/commands.md <c>sendToDepot</c>, FR-010).</summary>
public sealed record SendToDepotCommand(int CompanyId, int VehicleId) : ISimulationCommand
{
    public const string Type = "sendToDepot";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out var system, out _);
        if (reason is null && system.Depots.CountOwnedBy(CompanyId) == 0)
        {
            reason = ConstructionReasons.NoDepotReachable;
        }

        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        system.ToggleSendToDepot(system.SlotOf(VehicleId)); // Unreachable now: the order stays and is tried again later.
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
    }

    internal static SendToDepotCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32());
}

/// <summary>Changes the cargo a truck carries, in a depot with an empty hold (contracts/commands.md <c>setVehicleCargo</c>, FR-006).</summary>
public sealed record SetVehicleCargoCommand(int CompanyId, int VehicleId, string Cargo) : ISimulationCommand
{
    public const string Type = "setVehicleCargo";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out var system, out var slot);
        if (reason is null)
        {
            reason = system.DepotAt(slot) < 0 ? ConstructionReasons.NotInDepot
                : system.HoldAt(slot).Total > 0 ? ConstructionReasons.HoldNotEmpty
                : !system.ModelAt(slot).Carries(simulation.Content.CargoIndex(Cargo ?? string.Empty)) ? ConstructionReasons.CargoNotAllowed
                : null;
        }

        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        system.SetCargo(system.SlotOf(VehicleId), simulation.Content.CargoIndex(Cargo));
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
        writer.WriteString("cargo", Cargo);
    }

    internal static SetVehicleCargoCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32(), e.GetProperty("cargo").GetString() ?? "");
}

/// <summary>Sets a vehicle's service interval in days, 0 = never (contracts/commands.md <c>setServiceInterval</c>, FR-026).</summary>
public sealed record SetServiceIntervalCommand(int CompanyId, int VehicleId, int Days) : ISimulationCommand
{
    public const string Type = "setServiceInterval";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var reason = VehicleCommand.OwnVehicle(simulation, CompanyId, VehicleId, out _, out _)
            ?? (Days is < 0 or > 1000 ? ConstructionReasons.InvalidValue : null);
        return reason is null ? CommandResult.Success : VehicleCommand.Rejected(reason);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var system = simulation.RoadVehicles!;
        system.SetServiceInterval(system.SlotOf(VehicleId), Days);
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteNumber("vehicleId", VehicleId);
        writer.WriteNumber("days", Days);
    }

    internal static SetServiceIntervalCommand Read(JsonElement e) => new(
        e.GetProperty("companyId").GetInt32(), e.GetProperty("vehicleId").GetInt32(), e.GetProperty("days").GetInt32());
}
