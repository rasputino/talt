using Talt.Core.Saves;
using Talt.Core.Determinism;
using Talt.Core.Simulation;

namespace Talt.Core.Stations;

/// <summary>
/// Each closed month retains the groups' cargo columns once and the station-to-group membership at that close.
/// Group slots are local to a snapshot. Station slot swaps move the twelve membership indices with the station.
/// </summary>
internal sealed class StationHistory(int cargoCount)
{
    private const int Months = MonthlyHistory<StationCargoMonth>.MonthsKept;
    private readonly Snapshot[] _snapshots = new Snapshot[Months];
    private int[] _founded = [];
    private int _closed;

    public void AddStation(int slot, int capacity)
    {
        if (slot >= _founded.Length)
        {
            Array.Resize(ref _founded, capacity);
            foreach (var month in _snapshots)
            {
                month?.EnsureCapacity(capacity, cargoCount);
            }
        }

        _founded[slot] = _closed;
        // Reserve all periods during construction, so the first monthly close also allocates nothing.
        if (_snapshots[0] is null)
        {
            for (var i = 0; i < Months; i++)
            {
                var snapshot = new Snapshot();
                snapshot.EnsureCapacity(capacity, cargoCount);
                _snapshots[i] = snapshot;
            }
        }
    }

    public void RemoveStation(int slot, int last)
    {
        if (slot == last)
        {
            return;
        }

        _founded[slot] = _founded[last];
        foreach (var month in _snapshots)
        {
            month.Group[slot] = month.Group[last];
        }
    }

    public void Close(GameDate boundary, ReadOnlySpan<int> groups, int groupCount, int[] waiting, int[] pickedUp,
        int[] delivered, int[] ratings, byte[] served)
    {
        if (groups.IsEmpty)
        {
            return;
        }

        var month = _snapshots[_closed++ % Months];
        month.Ordinal = (boundary.Year * 12) + boundary.Month - 2;
        groups.CopyTo(month.Group);
        var cells = groupCount * cargoCount;
        Array.Copy(waiting, month.Waiting, cells);
        Array.Copy(pickedUp, month.PickedUp, cells);
        Array.Copy(delivered, month.Delivered, cells);
        Array.Copy(ratings, month.Rating, cells);
        Array.Copy(served, month.Served, cells);
    }

    public int Count(int slot) => slot < 0 ? 0 : Math.Min(Months, _closed - _founded[slot]);

    public (int Year, int Month) Date(int slot, int back)
    {
        if ((uint)back >= (uint)Count(slot))
        {
            return default;
        }

        var month = At(back).Ordinal;
        return (month / 12, (month % 12) + 1);
    }

    public bool TryGet(int slot, int back, int cargo, out StationCargoMonth value)
    {
        if ((uint)back >= (uint)Count(slot) || (uint)cargo >= (uint)cargoCount)
        {
            value = default;
            return false;
        }

        var month = At(back);
        value = month.Read((month.Group[slot] * cargoCount) + cargo);
        return true;
    }

    // Fingerprint logical observations in stable order. Snapshot group indices and spare capacity are storage details.
    public void AppendStation(StateHasher hasher, int slot)
    {
        var count = Count(slot);
        if (count == 0)
        {
            return;
        }

        Span<StationCargoMonth> observations = stackalloc StationCargoMonth[cargoCount];
        hasher.Append(count);
        for (var back = 0; back < count; back++)
        {
            var month = At(back);
            hasher.Append(month.Ordinal);
            var start = month.Group[slot] * cargoCount;
            for (var cargo = 0; cargo < cargoCount; cargo++)
            {
                observations[cargo] = month.Read(start + cargo);
            }

            hasher.Append<StationCargoMonth>(observations);
        }
    }

    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_closed);
        writer.WriteArray(_founded);
        for (var i = 0; i < Math.Min(_closed, Months); i++)
        {
            var month = _snapshots[i];
            writer.WriteInt(month.Ordinal);
            writer.WriteArray(month.Group);
            writer.WriteArray(month.Waiting);
            writer.WriteArray(month.PickedUp);
            writer.WriteArray(month.Delivered);
            writer.WriteArray(month.Rating);
            writer.WriteArray(month.Served);
        }
    }

    internal void ReadState(SaveReader reader)
    {
        _closed = reader.ReadInt(0, int.MaxValue);
        _founded = reader.ReadRequiredArray<int>();
        if (_founded.Length > 0 && _snapshots[0] is null)
        {
            for (var i = 0; i < Months; i++)
            {
                _snapshots[i] = new Snapshot();
                _snapshots[i].EnsureCapacity(_founded.Length, cargoCount);
            }
        }

        for (var i = 0; i < Math.Min(_closed, Months); i++)
        {
            var month = _snapshots[i];
            month.Ordinal = reader.ReadInt();
            month.Group = reader.ReadRequiredArray<int>();
            month.Waiting = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
            month.PickedUp = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
            month.Delivered = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
            month.Rating = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
            month.Served = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<byte>());
        }
    }

    private Snapshot At(int back) => _snapshots[(_closed - 1 - back) % Months];

    internal sealed class Snapshot
    {
        public int Ordinal;
        public int[] Group = [];
        public int[] Waiting = [];
        public int[] PickedUp = [];
        public int[] Delivered = [];
        public int[] Rating = [];
        public byte[] Served = [];

        public void EnsureCapacity(int stations, int cargos)
        {
            var cells = checked(stations * cargos);
            Array.Resize(ref Group, stations);
            Array.Resize(ref Waiting, cells);
            Array.Resize(ref PickedUp, cells);
            Array.Resize(ref Delivered, cells);
            Array.Resize(ref Rating, cells);
            Array.Resize(ref Served, cells);
        }

        public StationCargoMonth Read(int cell) =>
            new(Waiting[cell], PickedUp[cell], Delivered[cell], Served[cell] == 0 ? -1 : Rating[cell]);
    }
}
