namespace Talt.Core.Demand;

/// <summary>
/// Exact integer apportionment (research R10): splits whole units over shares so every call hands out exactly the
/// amount given, no part is ever negative, and over many calls each part receives its share to within one unit.
/// Each part keeps a carry (its credit) between calls; no randomness is involved.
/// </summary>
public static class Apportion
{
    /// <summary>Scale of the shares: parts per million.</summary>
    public const int ShareScale = 1_000_000;

    /// <summary>
    /// Splits <paramref name="amount"/> over <paramref name="shares"/> (parts per million, summing to at most
    /// <see cref="ShareScale"/> — a smaller sum leaves the rest to the parts in proportion). Writes the units of each
    /// part to <paramref name="units"/> and updates <paramref name="carry"/> (one per part, kept by the caller).
    /// </summary>
    public static void Split(ReadOnlySpan<int> shares, Span<long> carry, int amount, Span<int> units)
    {
        var n = shares.Length;
        if (carry.Length < n || units.Length < n)
        {
            throw new ArgumentException("carry and units must be at least as long as shares.");
        }

        long total = 0;
        for (var i = 0; i < n; i++)
        {
            total += shares[i];
        }

        if (n == 0 || amount <= 0 || total <= 0)
        {
            units[..n].Clear();
            return;
        }

        var given = 0;
        for (var i = 0; i < n; i++)
        {
            carry[i] += (long)amount * shares[i];
            var whole = carry[i] >= 0 ? carry[i] / total : 0;
            units[i] = (int)whole;
            carry[i] -= whole * total;
            given += (int)whole;
        }

        // The few units left go to the parts with the largest credit (lowest index first on ties).
        for (var left = amount - given; left > 0; left--)
        {
            var best = -1;
            for (var i = 0; i < n; i++)
            {
                if (shares[i] > 0 && (best < 0 || carry[i] > carry[best]))
                {
                    best = i;
                }
            }

            units[best]++;
            carry[best] -= total;
        }
    }

    /// <summary>
    /// Splits units between two choices: <paramref name="share2PerMille"/> of them go to the second, with a carried
    /// remainder (0–999) so the split is exact over time. Returns the units for the second choice.
    /// </summary>
    public static int SplitTwo(int amount, int share2PerMille, ref int carry)
    {
        if (amount <= 0 || share2PerMille <= 0)
        {
            return 0;
        }

        if (share2PerMille >= 1000)
        {
            return amount;
        }

        var total = carry + ((long)amount * share2PerMille);
        carry = (int)(total % 1000);
        return (int)(total / 1000);
    }
}
