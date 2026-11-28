using Talt.Core.Saves;

namespace Talt.Core.Finance;

public sealed partial class Ledger
{
    /// <summary>Writes the month ring and every year's totals with their calendar identity (feature 014).</summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(SlotCount);
        writer.WriteInt(_current);
        writer.WriteInt(MonthCount);
        writer.WriteArray(_monthYear);
        writer.WriteArray(_monthNumber);
        writer.WriteInt(_months.Length);
        foreach (var month in _months)
        {
            writer.WriteArray(month);
        }

        writer.WriteList(_yearNumbers);
        writer.WriteInt(_years.Count);
        foreach (var year in _years)
        {
            writer.WriteArray(year);
        }
    }

    private static void ReadPeriod(SaveReader reader, long[] period, int savedSlots)
    {
        reader.ReadInt(savedSlots, savedSlots);
        for (var i = 0; i < savedSlots; i++)
        {
            var slot = i < FixedSlots ? i : IncomeSlot(reader.Content.Map(ContentTable.Cargo, i - FixedSlots));
            period[slot] = reader.ReadLong();
        }
    }

    internal void ReadState(SaveReader reader)
    {
        var savedSlots = reader.ReadInt();
        if (savedSlots != FixedSlots + reader.Content.SavedCount(ContentTable.Cargo, SlotCount - FixedSlots))
        {
            throw reader.Fail("ledger slot count differs (cargo types changed)");
        }

        _current = reader.ReadInt(0, MonthsKept - 1);
        MonthCount = reader.ReadInt(0, MonthsKept);
        reader.ReadInto<int>(_monthYear);
        reader.ReadInto<int>(_monthNumber);
        if (reader.ReadInt() != _months.Length)
        {
            throw reader.Fail("ledger month ring length differs");
        }

        foreach (var month in _months)
        {
            ReadPeriod(reader, month, savedSlots);
        }

        reader.ReadListInto(_yearNumbers);
        var years = reader.ReadCount(4);
        if (years != _yearNumbers.Count)
        {
            throw reader.Fail("ledger year lists differ");
        }

        _years.Clear();
        for (var i = 0; i < years; i++)
        {
            var year = new long[SlotCount];
            ReadPeriod(reader, year, savedSlots);

            _years.Add(year);
        }
    }
}
