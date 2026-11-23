namespace Talt.Core.Construction;

/// <summary>
/// A company and its money (data-model.md Company.Id, Company.Balance). Only construction spends it
/// in feature 006; income, loans and accounts arrive with the finances feature (011).
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

    /// <summary>Money in internal units (1 = one US dollar); shown converted to the display currency.</summary>
    public long Balance { get; private set; }

    internal void Spend(long amount) => Balance -= amount;

    internal void Refund(long amount) => Balance += amount;
}
