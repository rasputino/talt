using Talt.Core.Saves;

namespace Talt.Core.Finance;

public sealed partial class FinanceSystem
{
    /// <summary>
    /// The <c>FINA</c> section (feature 014): activity flag, price levels, totals, station piece counts and the running
    /// cost table. Company balances, loans and ledgers are in <c>CNST</c> with the companies. The money event feed is
    /// interface state (floating texts) and starts empty.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteBool(_active);
        writer.WriteLong(CostLevel);
        writer.WriteLong(PaymentLevel);
        writer.WriteInt(InflationMonths);
        writer.WriteLong(TotalPaid);
        writer.WriteLong(Deliveries);
        writer.WriteArray(_pieceCounts);
        writer.WriteArray(_warehouseCounts);
        RunningCosts.WriteState(writer);
    }

    internal void ReadState(SaveReader reader)
    {
        _active = reader.ReadBool();
        CostLevel = reader.ReadLong();
        PaymentLevel = reader.ReadLong();
        InflationMonths = reader.ReadInt();
        TotalPaid = reader.ReadLong();
        Deliveries = reader.ReadLong();
        reader.Content.Rows(ContentTable.StationPiece, reader.ReadRequiredArray<int>()).CopyTo(_pieceCounts, 0);
        reader.Content.Rows(ContentTable.Warehouse, reader.ReadRequiredArray<int>()).CopyTo(_warehouseCounts, 0);
        RunningCosts.ReadState(reader);
    }
}

public sealed partial class RunningCosts
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_count);
        writer.WriteInt(ActiveCount);
        writer.WriteArray(_company);
        writer.WriteArray(_yearlyCost);
        writer.WriteArray(_value);
        writer.WriteArray(_active);
        writer.WriteArray(_yearlyByCompany);
        writer.WriteArray(_valueByCompany);

        // The free handles are exactly the inactive ones below the count (they are rebuilt on load: enumerating a sorted set
        // allocates, and capture must not).
    }

    internal void ReadState(SaveReader reader)
    {
        var count = reader.ReadInt();
        ActiveCount = reader.ReadInt();
        _company = reader.ReadRequiredArray<int>();
        _yearlyCost = reader.ReadRequiredArray<long>();
        _value = reader.ReadRequiredArray<long>();
        _active = reader.ReadRequiredArray<bool>();
        _yearlyByCompany = reader.ReadRequiredArray<long>();
        _valueByCompany = reader.ReadRequiredArray<long>();
        if (count < 0 || count > _company.Length || _company.Length != _yearlyCost.Length || _company.Length != _value.Length || _company.Length != _active.Length)
        {
            throw reader.Fail("running cost arrays are inconsistent");
        }

        _count = count;
        _free.Clear();
        for (var handle = 0; handle < count; handle++)
        {
            if (!_active[handle])
            {
                _free.Add(handle);
            }
        }
    }
}
