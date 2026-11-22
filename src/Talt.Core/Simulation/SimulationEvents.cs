namespace Talt.Core.Simulation;

/// <summary>
/// Work that game systems run at the end of every in-game day, month and year (research R10).
/// </summary>
/// <remarks>
/// Handlers run on the simulation thread right after the tick that completes the period, in the
/// order daily → monthly → yearly, and within each period in registration order. Register handlers
/// when the simulation is created so the order is deterministic.
/// </remarks>
public sealed class SimulationEvents
{
    private readonly List<Action<GameSimulation>> _daily = [];
    private readonly List<Action<GameSimulation>> _monthly = [];
    private readonly List<Action<GameSimulation>> _yearly = [];

    public void OnDayEnded(Action<GameSimulation> handler) => _daily.Add(handler ?? throw new ArgumentNullException(nameof(handler)));

    public void OnMonthEnded(Action<GameSimulation> handler) => _monthly.Add(handler ?? throw new ArgumentNullException(nameof(handler)));

    public void OnYearEnded(Action<GameSimulation> handler) => _yearly.Add(handler ?? throw new ArgumentNullException(nameof(handler)));

    internal void Dispatch(GameSimulation simulation, bool dayEnded, bool monthEnded, bool yearEnded)
    {
        if (dayEnded)
        {
            Run(_daily, simulation);
        }

        if (monthEnded)
        {
            Run(_monthly, simulation);
        }

        if (yearEnded)
        {
            Run(_yearly, simulation);
        }
    }

    private static void Run(List<Action<GameSimulation>> handlers, GameSimulation simulation)
    {
        foreach (var handler in handlers)
        {
            handler(simulation);
        }
    }
}
