using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

public class GameClockTests
{
    [Fact]
    public void DefaultScenario_StartsOnFirstOfJanuary1950()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        Assert.Equal(new GameDate(1950, 1, 1, new DateOnly(1950, 1, 1).DayNumber), simulation.Date);
    }

    [Fact]
    public void CustomStartYear_IsUsed()
    {
        Assert.Equal(1975, new GameClock(1975).DateAt(0).Year);
    }

    [Fact]
    public void FortyTicksMakeOneDay()
    {
        var clock = new GameClock(1950);
        Assert.Equal(1, clock.DateAt(39).Day);
        Assert.Equal(2, clock.DateAt(40).Day);
        Assert.False(GameClock.IsDayBoundary(39));
        Assert.True(GameClock.IsDayBoundary(40));
        Assert.False(GameClock.IsDayBoundary(0));
    }

    [Theory]
    [InlineData(2024, 29)] // Leap year.
    [InlineData(1950, 1)]  // Not a leap year: 1 March.
    [InlineData(2000, 29)] // Divisible by 400: leap.
    [InlineData(1900, 1)]  // Divisible by 100 only: not leap.
    public void LeapYears_FollowTheGregorianCalendar(int year, int dayAfter28February)
    {
        var clock = new GameClock(year);
        var ticksTo28February = (31 + 27) * (long)GameClock.TicksPerDay;
        var next = clock.DateAt(ticksTo28February + GameClock.TicksPerDay);

        Assert.Equal(28, clock.DateAt(ticksTo28February).Day);
        Assert.Equal(dayAfter28February, next.Day);
    }

    [Fact]
    public void LastDayOfYear_RollsOverToNextYear()
    {
        var clock = new GameClock(1950);
        var date = clock.DateAt(365L * GameClock.TicksPerDay);
        Assert.Equal((1951, 1, 1), (date.Year, date.Month, date.Day));
    }

    [Fact]
    public void ThousandYears_DoNotOverflow()
    {
        var clock = new GameClock(1950);
        var ticks = 366L * 1000 * GameClock.TicksPerDay;
        Assert.True(clock.DateAt(ticks).Year >= 2950);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9001)]
    public void StartYearOutsideRange_IsRejected(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameClock(year));
    }

    [Fact]
    public void DateToString_IsIso()
    {
        Assert.Equal("1950-03-07", new GameClock(1950).DateAt((31 + 28 + 6) * 40L).ToString());
    }
}
