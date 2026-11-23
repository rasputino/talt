namespace Talt.Core.Industries;

/// <summary>
/// Integer arithmetic of the industry economy: how a monthly rate is split over days and how rates are
/// clamped (research R6). Keeping it here makes the rules easy to test and documents that no
/// fractional cargo exists.
/// </summary>
internal static class IndustryEconomy
{
    /// <summary>Units an industry produces in a month at a given condition, before storage caps.</summary>
    public static int MonthlyAmount(int perMonth, int ratePerMille) =>
        (int)(((long)perMonth * ratePerMille) / 1000);

    /// <summary>
    /// Part of a monthly amount that belongs to one day. The last day of the month receives the
    /// division remainder, so the days of a month always add up to the monthly amount exactly.
    /// </summary>
    public static int DailyAmount(int monthly, int daysInMonth, int dayOfMonth)
    {
        var daily = monthly / daysInMonth;
        return dayOfMonth == daysInMonth ? monthly - (daily * (daysInMonth - 1)) : daily;
    }

    /// <summary>Keeps a production rate inside the economy bounds.</summary>
    public static int ClampRate(int value, int min, int max) => Math.Clamp(value, min, max);
}
