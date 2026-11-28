using Talt.Core.Saves;

namespace Talt.Core.Construction;

public sealed partial class ConstructionState
{
    /// <summary>
    /// The <c>CNST</c> section (feature 014): year, cost level, action version, the companies with every finance field,
    /// owned-tile counters, the tile objects and the undo record. Road and rail layers have their own sections.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Year);
        writer.WriteLong(CostLevel);
        writer.WriteInt(Version);
        writer.WriteArray(_ownedRoads);
        writer.WriteArray(_ownedRails);
        writer.WriteInt(_companies.Count);
        foreach (var company in _companies)
        {
            company.WriteState(writer);
        }

        Objects.WriteState(writer);
        Undo.WriteState(writer);
    }

    internal void ReadState(SaveReader reader)
    {
        Year = reader.ReadInt();
        CostLevel = reader.ReadLong();
        Version = reader.ReadInt();
        _ownedRoads = reader.Content.Rows(ContentTable.RoadType, reader.ReadRequiredArray<int>());
        _ownedRails = reader.Content.Rows(ContentTable.RailType, reader.ReadRequiredArray<int>());
        var count = reader.ReadInt(1, 1);
        for (var i = 0; i < count; i++)
        {
            _companies[i].ReadState(reader);
        }

        Objects.ReadState(reader);
        Undo.ReadState(reader);
    }
}

public sealed partial class Company
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Id);
        writer.WriteLong(Balance);
        writer.WriteLong(Loan);
        writer.WriteLong(Value);
        writer.WriteInt(BankruptMonths);
        writer.WriteBool(IsGameOver);
        writer.WriteBool(HasInfiniteMoney);
        writer.WriteLong(InfiniteMoneySinceTick);
        writer.WriteLong(InterestCarry);
        writer.WriteLong(MaintenanceCarry);
        writer.WriteLong(RunningCarry);
        writer.WriteBool(Ledger is not null);
        Ledger?.WriteState(writer);
    }

    internal void ReadState(SaveReader reader)
    {
        if (reader.ReadInt() != Id)
        {
            throw reader.Fail("company id mismatch");
        }

        Balance = reader.ReadLong();
        Loan = reader.ReadLong();
        Value = reader.ReadLong();
        BankruptMonths = reader.ReadInt();
        IsGameOver = reader.ReadBool();
        HasInfiniteMoney = reader.ReadBool();
        InfiniteMoneySinceTick = reader.ReadLong();
        InterestCarry = reader.ReadLong();
        MaintenanceCarry = reader.ReadLong();
        RunningCarry = reader.ReadLong();
        var hasLedger = reader.ReadBool();
        if (hasLedger != Ledger is not null)
        {
            throw reader.Fail("company ledger presence differs from the game's");
        }

        Ledger?.ReadState(reader);
    }
}

public sealed partial class TileObjects
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteArray(_kinds);
        writer.WriteArray(_owners);
    }

    internal void ReadState(SaveReader reader)
    {
        reader.ReadInto<byte>(_kinds);
        reader.ReadInto<ushort>(_owners);
    }
}

public sealed partial class UndoRecord
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteBool(HasValue);
        writer.WriteInt(CompanyId);
        writer.WriteLong(AppliedAtTick);
        writer.WriteLong(Cost);
        writer.WriteInt(TileCount);
        writer.WriteArray(_tiles);
        writer.WriteArray(_before);
        writer.WriteArray(_after);
    }

    internal void ReadState(SaveReader reader)
    {
        HasValue = reader.ReadBool();
        CompanyId = reader.ReadInt();
        AppliedAtTick = reader.ReadLong();
        Cost = reader.ReadLong();
        var count = reader.ReadInt();
        _tiles = reader.ReadRequiredArray<int>();
        _before = reader.ReadRequiredArray<TileSnapshot>();
        _after = reader.ReadRequiredArray<TileSnapshot>();
        if (count < 0 || count > _tiles.Length || count > _before.Length || count > _after.Length)
        {
            throw reader.Fail("undo tile count outside its buffers");
        }

        TileCount = count;
        if (!reader.Content.IsIdentityFor(ContentTable.RoadType) || !reader.Content.IsIdentityFor(ContentTable.RailType))
        {
            for (var i = 0; i < count; i++)
            {
                _before[i] = Remap(_before[i]);
                _after[i] = Remap(_after[i]);
            }
        }

        TileSnapshot Remap(TileSnapshot tile) => tile with
        {
            Road = tile.Road with { Type = reader.Content.PieceType(ContentTable.RoadType, tile.Road.Type) },
            Rail = tile.Rail with { Type = reader.Content.PieceType(ContentTable.RailType, tile.Rail.Type) },
        };
    }
}
