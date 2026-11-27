using Talt.Core.Determinism;

namespace Talt.Core.Stations;

/// <summary>
/// One part of a cargo hold (data-model.md CargoHold): an amount of one cargo with where it was picked up.
/// </summary>
/// <param name="Cargo">Cargo index in the game content.</param>
/// <param name="Amount">Whole units.</param>
/// <param name="Origin">Station id where it was first picked up.</param>
/// <param name="SourceTown">Town the passengers or mail came from, or −1.</param>
/// <param name="Destination">Destination station id (feature 013), or −1 for any destination.</param>
/// <param name="PickupDay">Day number when it was first loaded onto a vehicle.</param>
/// <param name="Credit">Transfer credit recorded by earlier legs (feature 013, money units; statistics only).</param>
public readonly record struct HeldCargo(int Cargo, int Amount, int Origin, int SourceTown, int Destination, int PickupDay, long Credit = 0);

/// <summary>
/// What a vehicle carries (feature 012) or what a test moves between stations: a small list of
/// <see cref="HeldCargo"/> parts, filled by <see cref="StationRegistry.Load"/> and emptied by
/// <see cref="StationRegistry.Unload"/>. Parts with the same cargo, origin, town, destination and pickup
/// day are merged. Grows only when it holds more distinct parts than ever before (no allocation in steady
/// state).
/// </summary>
public sealed class CargoHold : IStateHashable
{
    private HeldCargo[] _parts = new HeldCargo[4];

    /// <summary>Number of distinct parts held.</summary>
    public int Count { get; private set; }

    public HeldCargo this[int index] => (uint)index < (uint)Count ? _parts[index] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Units of all cargo held.</summary>
    public int Total
    {
        get
        {
            var total = 0;
            for (var i = 0; i < Count; i++)
            {
                total += _parts[i].Amount;
            }

            return total;
        }
    }

    /// <summary>Units of one cargo held.</summary>
    public int AmountOf(int cargo)
    {
        var total = 0;
        for (var i = 0; i < Count; i++)
        {
            if (_parts[i].Cargo == cargo)
            {
                total += _parts[i].Amount;
            }
        }

        return total;
    }

    /// <summary>Empties the hold (the held cargo is gone: callers account for it).</summary>
    public void Clear() => Count = 0;

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(Count);
        for (var i = 0; i < Count; i++)
        {
            var p = _parts[i];
            hasher.Append(p.Cargo);
            hasher.Append(p.Amount);
            hasher.Append(p.Origin);
            hasher.Append(p.SourceTown);
            hasher.Append(p.Destination);
            hasher.Append(p.PickupDay);
            hasher.Append(p.Credit);
        }
    }

    internal void Add(in HeldCargo cargo)
    {
        if (cargo.Amount <= 0)
        {
            return;
        }

        for (var i = 0; i < Count; i++)
        {
            var p = _parts[i];
            if (p.Cargo == cargo.Cargo && p.Origin == cargo.Origin && p.SourceTown == cargo.SourceTown
                && p.Destination == cargo.Destination && p.PickupDay == cargo.PickupDay)
            {
                _parts[i] = p with { Amount = p.Amount + cargo.Amount, Credit = p.Credit + cargo.Credit };
                return;
            }
        }

        if (Count == _parts.Length)
        {
            Array.Resize(ref _parts, _parts.Length * 2);
        }

        _parts[Count++] = cargo;
    }

    /// <summary>Sets a part's amount; zero removes it, keeping the order of the others. The credit shrinks in proportion.</summary>
    internal void SetAmount(int index, int amount)
    {
        var part = _parts[index];
        SetPart(index, amount, part.Amount > 0 ? part.Credit * Math.Max(0, amount) / part.Amount : 0);
    }

    /// <summary>Sets a part's amount and credit; zero amount removes it, keeping the order of the others.</summary>
    internal void SetPart(int index, int amount, long credit)
    {
        if (amount > 0)
        {
            _parts[index] = _parts[index] with { Amount = amount, Credit = credit };
            return;
        }

        Array.Copy(_parts, index + 1, _parts, index, Count - index - 1);
        Count--;
    }
}
