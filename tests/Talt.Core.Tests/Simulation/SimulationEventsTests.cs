using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

public class SimulationEventsTests
{
    [Fact]
    public void DailyMonthlyYearly_FireAtTheirBoundariesInOrder()
    {
        // Start on 1 Jan; run to the end of year 1 (365 days).
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny() with { StartYear = 1950 });
        var log = new List<string>();
        simulation.Events.OnDayEnded(s => log.Add($"d{s.Date}"));
        simulation.Events.OnMonthEnded(s => log.Add($"m{s.Date}"));
        simulation.Events.OnYearEnded(s => log.Add($"y{s.Date}"));

        for (var i = 0; i < 365 * GameClock.TicksPerDay; i++)
        {
            simulation.Tick();
        }

        Assert.Equal(365, log.Count(e => e[0] == 'd'));
        Assert.Equal(12, log.Count(e => e[0] == 'm'));
        Assert.Equal(1, log.Count(e => e[0] == 'y'));
        Assert.Equal("d1950-01-02", log[0]); // First day ends after 40 ticks.
        Assert.Equal(["d1951-01-01", "m1951-01-01", "y1951-01-01"], log.TakeLast(3));
        Assert.Contains("m1950-02-01", log);
    }

    [Fact]
    public void HandlersWithinAPeriod_RunInRegistrationOrder()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var order = new List<int>();
        simulation.Events.OnDayEnded(_ => order.Add(1));
        simulation.Events.OnDayEnded(_ => order.Add(2));
        simulation.Events.OnDayEnded(_ => order.Add(3));

        for (var i = 0; i < GameClock.TicksPerDay; i++)
        {
            simulation.Tick();
        }

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void NoEventsBeforeTheFirstDayEnds()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var fired = 0;
        simulation.Events.OnDayEnded(_ => fired++);

        for (var i = 0; i < GameClock.TicksPerDay - 1; i++)
        {
            simulation.Tick();
        }

        Assert.Equal(0, fired);
    }
}
