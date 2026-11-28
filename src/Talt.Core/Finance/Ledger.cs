using Talt.Core.Determinism;

namespace Talt.Core.Finance;

/// <summary>
/// What a change of cash was for (feature 011, data-model.md LedgerCategory, FR-014). Income of a cargo
/// uses the slot <see cref="Ledger.IncomeSlot"/>; the fixed categories come first.
/// </summary>
public enum LedgerCategory : byte
{
    Construction = 0,

    /// <summary>Filled by feature 012 (buying vehicles).</summary>
    VehiclePurchase = 1,
    RunningCosts = 2,
    PropertyMaintenance = 3,
    LoanInterest = 4,
    Other = 5,

    /// <summary>Income from deliveries; the cargo picks the slot.</summary>
    Income = 6,
}

/// <summary>
/// A company's totals by category (data-model.md Ledger, research R9): the last 24 months in a ring and one
/// entry per game year. Each period holds one signed total per slot (fixed categories, then one income slot
/// per cargo). Bookings add to the current month and the current year; month and year ends rotate them.
/// </summary>
public sealed partial class Ledger : IStateHashable
{
    /// <summary>Months kept.</summary>
    public const int MonthsKept = 24;

    /// <summary>Slots before the per-cargo income slots.</summary>
    public const int FixedSlots = (int)LedgerCategory.Income;

    private readonly long[][] _months;
    private readonly int[] _monthYear = new int[MonthsKept];
    private readonly int[] _monthNumber = new int[MonthsKept];
    private readonly List<long[]> _years = [];
    private readonly List<int> _yearNumbers = [];
    private int _current;

    public Ledger(int cargoCount, int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cargoCount);
        SlotCount = FixedSlots + cargoCount;
        _months = new long[MonthsKept][];
        for (var i = 0; i < MonthsKept; i++)
        {
            _months[i] = new long[SlotCount];
        }

        _monthYear[0] = year;
        _monthNumber[0] = month;
        MonthCount = 1;
        _years.Add(new long[SlotCount]);
        _yearNumbers.Add(year);
    }

    /// <summary>Fixed categories plus one income slot per cargo.</summary>
    public int SlotCount { get; }

    /// <summary>Months recorded (the current one included), at most <see cref="MonthsKept"/>.</summary>
    public int MonthCount { get; private set; }

    /// <summary>
    /// True when any period holds a non-zero total (turns finance hashing on, research R11). A construction
    /// undone in the same month leaves every total at zero, so the game hashes as if nothing happened.
    /// </summary>
    public bool HasEntries
    {
        get
        {
            foreach (var year in _years)
            {
                foreach (var value in year)
                {
                    if (value != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Years recorded (the current one included).</summary>
    public int YearCount => _years.Count;

    /// <summary>The slot of a cargo's income.</summary>
    public static int IncomeSlot(int cargo) => FixedSlots + cargo;

    /// <summary>The slot of a fixed category (not <see cref="LedgerCategory.Income"/>).</summary>
    public static int Slot(LedgerCategory category) => (int)category;

    /// <summary>The total of a slot <paramref name="back"/> months ago (0 = this month).</summary>
    public long Month(int back, int slot) => back < MonthCount ? _months[Ring(back)][slot] : 0;

    /// <summary>Year and month (1–12) of the month <paramref name="back"/> months ago.</summary>
    public (int Year, int Month) MonthDate(int back) => back < MonthCount ? (_monthYear[Ring(back)], _monthNumber[Ring(back)]) : (0, 0);

    /// <summary>The total of a slot <paramref name="back"/> years ago (0 = this year).</summary>
    public long Year(int back, int slot) => back < _years.Count ? _years[_years.Count - 1 - back][slot] : 0;

    /// <summary>The calendar year <paramref name="back"/> years ago.</summary>
    public int YearNumber(int back) => back < _years.Count ? _yearNumbers[_years.Count - 1 - back] : 0;

    /// <summary>Income (positive slots) of a month.</summary>
    public long MonthIncome(int back) => back < MonthCount ? Positive(_months[Ring(back)]) : 0;

    /// <summary>Expenses (negative slots, as a negative number) of a month.</summary>
    public long MonthExpenses(int back) => back < MonthCount ? Negative(_months[Ring(back)]) : 0;

    public long YearIncome(int back) => back < _years.Count ? Positive(_years[_years.Count - 1 - back]) : 0;

    public long YearExpenses(int back) => back < _years.Count ? Negative(_years[_years.Count - 1 - back]) : 0;

    /// <summary>Sum of every slot of a year: the change in cash for that year, loan movements apart.</summary>
    public long YearTotal(int back)
    {
        if (back >= _years.Count)
        {
            return 0;
        }

        long total = 0;
        foreach (var value in _years[_years.Count - 1 - back])
        {
            total = Money.Add(total, value);
        }

        return total;
    }

    internal void Add(int slot, long amount)
    {
        if (amount == 0)
        {
            return;
        }

        var month = _months[_current];
        month[slot] = Money.Add(month[slot], amount);
        var year = _years[^1];
        year[slot] = Money.Add(year[slot], amount);
    }

    /// <summary>Starts a new month (after the month-end charges were booked).</summary>
    internal void StartMonth(int year, int month)
    {
        _current = (_current + 1) % MonthsKept;
        Array.Clear(_months[_current]);
        _monthYear[_current] = year;
        _monthNumber[_current] = month;
        MonthCount = Math.Min(MonthsKept, MonthCount + 1);
    }

    /// <summary>Starts a new year (allocates one array per game year).</summary>
    internal void StartYear(int year)
    {
        _years.Add(new long[SlotCount]);
        _yearNumbers.Add(year);
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(MonthCount);
        for (var back = 0; back < MonthCount; back++)
        {
            var ring = Ring(back);
            hasher.Append(_monthYear[ring]);
            hasher.Append(_monthNumber[ring]);
            hasher.Append<long>(_months[ring]);
        }

        hasher.Append(_years.Count);
        for (var i = 0; i < _years.Count; i++)
        {
            hasher.Append(_yearNumbers[i]);
            hasher.Append<long>(_years[i]);
        }
    }

    private int Ring(int back) => ((_current - back) % MonthsKept + MonthsKept) % MonthsKept;

    private static long Positive(long[] slots)
    {
        long total = 0;
        foreach (var value in slots)
        {
            if (value > 0)
            {
                total = Money.Add(total, value);
            }
        }

        return total;
    }

    private static long Negative(long[] slots)
    {
        long total = 0;
        foreach (var value in slots)
        {
            if (value < 0)
            {
                total = Money.Add(total, value);
            }
        }

        return total;
    }
}

/// <summary>Saturating money arithmetic (research R1): results stay within ±<see cref="Limit"/> instead of wrapping.</summary>
public static class Money
{
    /// <summary>Largest amount of money kept (2⁶²).</summary>
    public const long Limit = 1L << 62;

    public static long Add(long a, long b) => Clamp((Int128)a + b);

    /// <summary>a × b / divisor, rounded toward zero, with an exact 128-bit intermediate.</summary>
    public static long MulDiv(long a, long b, long divisor) => Clamp((Int128)a * b / divisor);

    public static long Clamp(Int128 value) => value > Limit ? Limit : value < -Limit ? -Limit : (long)value;
}
