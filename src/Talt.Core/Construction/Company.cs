using Talt.Core.Determinism;
using Talt.Core.Finance;

namespace Talt.Core.Construction;

/// <summary>
/// A company and its account (feature 006 balance; feature 011 data-model.md Company): cash, loan, the ledger
/// by category and period, company value and the bankruptcy state. Every change of cash goes through
/// <see cref="Book"/> (with a category) or the loan methods, so ledger totals always match the cash.
/// </summary>
public sealed class Company
{
    /// <summary>Id of the player's company.</summary>
    public const int PlayerId = 0;

    public Company(int id, long balance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, TileOwner.MaxIndex);
        Id = id;
        Balance = balance;
    }

    public int Id { get; }

    public Vehicles.ReplacementPolicy RoadReplacements { get; } = new();

    public Vehicles.ReplacementPolicy RailReplacements { get; } = new();


    /// <summary>Cash in internal units (1 = one US dollar); shown converted to the display currency. May be negative.</summary>
    public long Balance { get; private set; }

    /// <summary>Money borrowed (feature 011); 0 until the company borrows.</summary>
    public long Loan { get; private set; }

    /// <summary>Totals by category and period; null until the finance system attaches one (map-less tests).</summary>
    public Ledger? Ledger { get; private set; }

    /// <summary>Cash − loan + property value, recomputed at each month end.</summary>
    public long Value { get; internal set; }

    /// <summary>Consecutive month ends with negative cash and the maximum loan.</summary>
    public int BankruptMonths { get; internal set; }

    /// <summary>Set once the bankruptcy grace limit was reached outside sandbox games; final.</summary>
    public bool IsGameOver { get; internal set; }

    /// <summary>
    /// Infinite money (feature 011, research R17): money never limits the company and cash no longer changes, while
    /// the ledger keeps counting. Turned on once and never off.
    /// </summary>
    public bool HasInfiniteMoney { get; private set; }

    /// <summary>The tick infinite money was turned on, or −1 (later goals and scores may exclude such games).</summary>
    public long InfiniteMoneySinceTick { get; private set; } = -1;

    /// <summary>Accrual remainders (research R4): interest, maintenance and running costs.</summary>
    internal long InterestCarry { get; set; }

    internal long MaintenanceCarry { get; set; }

    internal long RunningCarry { get; set; }

    internal void AttachLedger(Ledger ledger) => Ledger ??= ledger ?? throw new ArgumentNullException(nameof(ledger));

    /// <summary>Changes cash by a signed amount booked under a category (income slots: <paramref name="cargo"/>).</summary>
    internal void Book(LedgerCategory category, long amount, int cargo = -1)
    {
        if (!HasInfiniteMoney)
        {
            Balance = Money.Add(Balance, amount);
        }

        Ledger?.Add(category == LedgerCategory.Income ? Ledger.IncomeSlot(cargo) : Ledger.Slot(category), amount);
    }

    /// <summary>A construction payment (feature 006).</summary>
    internal void Spend(long amount) => Book(LedgerCategory.Construction, -amount);

    /// <summary>A construction refund (undo).</summary>
    internal void Refund(long amount) => Book(LedgerCategory.Construction, amount);

    /// <summary>Turns infinite money on for good (no method turns it off).</summary>
    internal void EnableInfiniteMoney(long tick)
    {
        if (!HasInfiniteMoney)
        {
            HasInfiniteMoney = true;
            InfiniteMoneySinceTick = tick;
        }
    }

    internal void Borrow(long amount)
    {
        Loan += amount;
        Balance = Money.Add(Balance, amount);
    }

    internal void Repay(long amount)
    {
        Loan -= amount;
        Balance = Money.Add(Balance, -amount);
    }

    /// <summary>The finance state of the company (cash is hashed by the construction state).</summary>
    internal void AppendFinanceTo(StateHasher hasher)
    {
        hasher.Append(Id);
        hasher.Append(Loan);
        hasher.Append(Value);
        hasher.Append(BankruptMonths);
        hasher.Append(IsGameOver ? 1 : 0);
        hasher.Append(InfiniteMoneySinceTick);
        hasher.Append(InterestCarry);
        hasher.Append(MaintenanceCarry);
        hasher.Append(RunningCarry);
        if (Ledger is not null)
        {
            hasher.Append(Ledger);
        }
    }
}
