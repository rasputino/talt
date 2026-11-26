using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Simulation;

namespace Talt.Core.Construction;

/// <summary>
/// Shared steps of every construction action (contracts/commands.md, FR-001, FR-017): plan the action,
/// check limits and money, then apply the plan as one unit, pay and remember it for undo. Subclasses
/// only describe what the action covers (<see cref="Plan"/>) and how it is written to JSON. The client
/// previews an action with <see cref="Preview"/>, which runs exactly the checks of <see cref="Validate"/>.
/// </summary>
/// <param name="CompanyId">Company that builds and pays.</param>
public abstract record ConstructionCommand(int CompanyId) : ISimulationCommand
{
    public abstract string TypeName { get; }

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction;
        return state is null
            ? Rejected(ConstructionReasons.NoMap)
            : Preview(state, state.Planner, state.Plan);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var state = simulation.Construction!;
        // Apply runs right after a successful Validate in the same tick, so state.Plan is this command's plan.
        state.Commit(state.Plan, state.FindCompany(CompanyId)!, simulation.CurrentTick);
        Committed(simulation);
    }

    /// <summary>
    /// Runs after the plan was committed (feature 010: stations record their pieces, which are not tile data).
    /// Must be deterministic and change only simulation state.
    /// </summary>
    protected virtual void Committed(GameSimulation simulation)
    {
    }

    /// <summary>
    /// Plans the action into <paramref name="plan"/> and checks it like <see cref="Validate"/> does,
    /// changing nothing. The plan holds the tiles, problems and cost for drawing the preview.
    /// </summary>
    public CommandResult Preview(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(plan);
        var company = state.FindCompany(CompanyId);
        if (company is null)
        {
            plan.Reset();
            plan.Reason = ConstructionReasons.CompanyNotFound;
            return Rejected(ConstructionReasons.CompanyNotFound);
        }

        Plan(state, planner, plan);
        plan.RatingChange = state.Towns?.PlanRatingChange(plan, CompanyId) ?? 0;
        return Check(state, company, plan);
    }

    /// <summary>
    /// The checks after planning (limits were checked by the planner; here the price level, the plan's
    /// reason and the money). Public so a tool preview that planned with the planner directly, without creating a
    /// command, runs exactly the same checks.
    /// </summary>
    public static CommandResult Check(ConstructionState state, Company company, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CostInflated)
        {
            // Feature 011 (research R7): the planned cost at today's price level, applied once per plan.
            plan.Cost = state.Inflate(plan.Cost);
            plan.CostInflated = true;
        }

        if (plan.Reason is not null)
        {
            return Rejected(plan.Reason);
        }

        if (state.Occupancy is { } occupancy && occupancy.Blocks(plan))
        {
            // Feature 012 (research R12): never take a road, stop or depot from under a vehicle.
            plan.Reason = ConstructionReasons.VehicleInTheWay;
            return Rejected(ConstructionReasons.VehicleInTheWay);
        }

        if (plan.Cost > company.Balance && !company.HasInfiniteMoney)
        {
            plan.Reason = ConstructionReasons.NotEnoughMoney;
            return Rejected(ConstructionReasons.NotEnoughMoney);
        }

        return CommandResult.Success;
    }

    public abstract void WriteProperties(Utf8JsonWriter writer);

    /// <summary>Describes what the action covers by filling the plan (no state changes).</summary>
    protected abstract void Plan(ConstructionState state, ConstructionPlanner planner, ConstructionPlan plan);

    internal static CommandResult Rejected(string reason) => CommandResult.Rejected(reason, ConstructionReasons.Describe(reason));
}
