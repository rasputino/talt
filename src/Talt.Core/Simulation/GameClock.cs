using Talt.Core.Determinism;

namespace Talt.Core.Simulation;

/// <summary>
/// An in-game calendar date (Gregorian, including leap years).
/// </summary>
public readonly record struct GameDate(int Year, int Month, int Day, int DayNumber)
{
    internal static GameDate FromDateOnly(DateOnly date) => new(date.Year, date.Month, date.Day, date.DayNumber);

    /// <summary>ISO 8601 date (yyyy-MM-dd), culture independent; for logs and tools, not the UI.</summary>
    public override string ToString() =>
        $"{Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}-" +
        $"{Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}-" +
        $"{Day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Converts the tick counter into calendar dates (data-model.md, GameClock; research R2).
/// </summary>
public sealed class GameClock
{
    /// <summary>Simulation ticks per in-game day.</summary>
    public const int TicksPerDay = 40;

    /// <summary>Ticks per real second at normal speed, so one day lasts 2 seconds.</summary>
    public const int TicksPerSecondAtNormalSpeed = FixedMath.TicksPerSecond;

    public GameClock(int startYear)
    {
        if (startYear is < Scenarios.ScenarioSettings.MinStartYear or > Scenarios.ScenarioSettings.MaxStartYear)
        {
            throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "Start year is outside the supported range.");
        }

        StartYear = startYear;
        StartDayNumber = new DateOnly(startYear, 1, 1).DayNumber;
    }

    public int StartYear { get; }

    /// <summary>Day number (days since 1 January of year 1) of the first in-game day.</summary>
    public int StartDayNumber { get; }

    /// <summary>Date in effect after <paramref name="tick"/> completed ticks.</summary>
    public GameDate DateAt(long tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        return GameDate.FromDateOnly(DateOnly.FromDayNumber(checked(StartDayNumber + (int)(tick / TicksPerDay))));
    }

    /// <summary>True when the tick that just completed (now <paramref name="tick"/>) ended a day.</summary>
    public static bool IsDayBoundary(long tick) => tick > 0 && tick % TicksPerDay == 0;
}
