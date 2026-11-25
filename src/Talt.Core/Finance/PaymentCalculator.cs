namespace Talt.Core.Finance;

/// <summary>The price of one delivery (data-model.md Payment).</summary>
/// <param name="Amount">Money paid.</param>
/// <param name="Distance">Tiles paid for (at least the rules' minimum).</param>
/// <param name="Days">Days in transit.</param>
/// <param name="TimeFactorPerMille">Share of the full value kept after the days in transit.</param>
public readonly record struct PaymentQuote(long Amount, int Distance, int Days, int TimeFactorPerMille);

/// <summary>
/// The delivery payment formula (feature 011, research R2), pure and exact:
/// <c>floor(rate × units × distance × timeFactor‰ × paymentLevel / (1000 × 1000 × 10⁶))</c>, where the time
/// factor is 1000 for the cargo's free days and then falls by its daily decay down to its minimum share.
/// </summary>
public static class PaymentCalculator
{
    /// <summary>Price level of 1.0 in parts per million.</summary>
    public const long LevelOne = 1_000_000;

    /// <summary>The time factor in per mille.</summary>
    public static int TimeFactor(int days, int freeDays, int decayPerDayPerMille, int minimumPercent)
    {
        var late = Math.Max(0, days - freeDays);
        var factor = 1000L - ((long)decayPerDayPerMille * late);
        return (int)Math.Max(minimumPercent * 10L, Math.Min(1000L, factor));
    }

    /// <summary>Prices a delivery.</summary>
    /// <param name="rate">Money per 1 000 units per tile.</param>
    /// <param name="units">Units delivered.</param>
    /// <param name="distance">Tiles between the stations (raised to <paramref name="minDistance"/>).</param>
    /// <param name="paymentLevel">Inflation level of payments in parts per million (<see cref="LevelOne"/> = none).</param>
    public static PaymentQuote Quote(
        long rate, int freeDays, int decayPerDayPerMille, int minimumPercent,
        int units, int distance, int minDistance, int days, long paymentLevel)
    {
        var paid = Math.Max(minDistance, distance);
        var days0 = Math.Max(0, days);
        var factor = TimeFactor(days0, freeDays, decayPerDayPerMille, minimumPercent);
        if (units <= 0 || rate <= 0)
        {
            return new PaymentQuote(0, paid, days0, factor);
        }

        var value = (Int128)rate * units * paid * factor * paymentLevel / ((Int128)1000 * 1000 * LevelOne);
        return new PaymentQuote(Money.Clamp(value), paid, days0, factor);
    }
}
