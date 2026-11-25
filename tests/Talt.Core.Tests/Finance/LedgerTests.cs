using Talt.Core.Determinism;
using Talt.Core.Finance;

namespace Talt.Core.Tests.Finance;

public sealed class LedgerTests
{
    [Fact]
    public void Bookings_AddToTheMonthAndTheYear()
    {
        var ledger = new Ledger(cargoCount: 3, year: 1950, month: 1);
        ledger.Add(Ledger.Slot(LedgerCategory.Construction), -500);
        ledger.Add(Ledger.IncomeSlot(2), 1200);
        ledger.Add(Ledger.IncomeSlot(2), 300);

        Assert.Equal(9, ledger.SlotCount);
        Assert.Equal(-500, ledger.Month(0, Ledger.Slot(LedgerCategory.Construction)));
        Assert.Equal(1500, ledger.Year(0, Ledger.IncomeSlot(2)));
        Assert.Equal(1500, ledger.MonthIncome(0));
        Assert.Equal(-500, ledger.MonthExpenses(0));
        Assert.Equal(1000, ledger.YearTotal(0));
        Assert.True(ledger.HasEntries);
    }

    [Fact]
    public void MonthRotation_KeepsTheLast24Months_AndYearsKeepEverything()
    {
        var ledger = new Ledger(0, 1950, 1);
        var year = 1950;
        var month = 1;
        for (var i = 1; i <= 30; i++)
        {
            ledger.Add(0, -i);
            month++;
            if (month == 13)
            {
                month = 1;
                year++;
                ledger.StartMonth(year, month);
                ledger.StartYear(year);
            }
            else
            {
                ledger.StartMonth(year, month);
            }
        }

        Assert.Equal(Ledger.MonthsKept, ledger.MonthCount);
        Assert.Equal((1952, 7), ledger.MonthDate(0));
        Assert.Equal(0, ledger.Month(0, 0)); // The new month is empty.
        Assert.Equal(-30, ledger.Month(1, 0));
        Assert.Equal(-8, ledger.Month(24 - 1, 0));
        Assert.Equal(0, ledger.Month(24, 0)); // Older than the ring.
        Assert.Equal(3, ledger.YearCount);
        Assert.Equal(1950, ledger.YearNumber(2));
        Assert.Equal(-(1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10 + 11 + 12), ledger.Year(2, 0));
        Assert.Equal(-(25 + 26 + 27 + 28 + 29 + 30), ledger.Year(0, 0));
    }

    [Fact]
    public void BookingsThatCancel_LeaveNoEntries()
    {
        var ledger = new Ledger(0, 1950, 1);
        Assert.False(ledger.HasEntries);
        ledger.Add(0, -100);
        ledger.Add(0, 100);
        Assert.False(ledger.HasEntries);
    }

    [Fact]
    public void Money_Saturates_InsteadOfWrapping()
    {
        Assert.Equal(Money.Limit, Money.Add(Money.Limit, 1));
        Assert.Equal(-Money.Limit, Money.Add(-Money.Limit, -5));
        Assert.Equal(Money.Limit, Money.MulDiv(long.MaxValue, long.MaxValue, 1));
        Assert.Equal(333, Money.MulDiv(1000, 1, 3));
    }

    [Fact]
    public void Hash_DependsOnTheTotals()
    {
        static ulong Hash(Ledger l)
        {
            var h = new StateHasher();
            h.Append(l);
            return h.GetFingerprint();
        }

        var a = new Ledger(1, 1950, 1);
        var b = new Ledger(1, 1950, 1);
        Assert.Equal(Hash(a), Hash(b));
        b.Add(Ledger.IncomeSlot(0), 1);
        Assert.NotEqual(Hash(a), Hash(b));
    }
}
