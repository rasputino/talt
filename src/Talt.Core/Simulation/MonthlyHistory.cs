using Talt.Core.Saves;
using Talt.Core.Determinism;

namespace Talt.Core.Simulation;

/// <summary>Twelve completed calendar months of fixed-width observations, allocated when the subject is created.</summary>
public sealed class MonthlyHistory<T>(int width) : IStateHashable where T : unmanaged
{
    public const int MonthsKept = 12;
    private readonly T[] _values = new T[checked(MonthsKept * width)];
    private readonly int[] _months = new int[MonthsKept];
    private int _latest = MonthsKept - 1;

    /// <summary>Number of observed months, including months whose measurements were zero.</summary>
    public int Count { get; private set; }

    /// <summary>Begins writing the completed month before the supplied new-month boundary.</summary>
    internal Span<T> Close(GameDate newMonth)
    {
        _latest = (_latest + 1) % MonthsKept;
        _months[_latest] = (newMonth.Year * 12) + newMonth.Month - 2;
        Count = Math.Min(MonthsKept, Count + 1);
        return _values.AsSpan(_latest * width, width);
    }

    /// <summary>Year and month of an observation, newest first. An absent observation has year zero.</summary>
    public (int Year, int Month) Date(int back)
    {
        if ((uint)back >= (uint)Count)
        {
            return default;
        }

        var month = _months[Slot(back)];
        return (month / 12, (month % 12) + 1);
    }

    /// <summary>Reads a measurement, distinguishing missing months from a measured zero.</summary>
    public bool TryGet(int back, int index, out T value)
    {
        if ((uint)back >= (uint)Count || (uint)index >= (uint)width)
        {
            value = default;
            return false;
        }

        value = _values[(Slot(back) * width) + index];
        return true;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(Count);
        for (var back = 0; back < Count; back++)
        {
            var slot = Slot(back);
            hasher.Append(_months[slot]);
            hasher.Append<T>(_values.AsSpan(slot * width, width));
        }
    }

    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Count);
        writer.WriteInt(_latest);
        writer.WriteArray(_months);
        writer.WriteArray(_values);
    }

    internal void ReadState(SaveReader reader)
    {
        Count = reader.ReadInt(0, MonthsKept);
        _latest = reader.ReadInt(0, MonthsKept - 1);
        reader.ReadInto<int>(_months);
        reader.ReadInto<T>(_values);
    }

    private int Slot(int back) => (_latest + MonthsKept - back) % MonthsKept;
}
