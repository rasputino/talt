using Talt.Core.Saves;

namespace Talt.Core.Demand;

public sealed partial class DestinationTables
{
    /// <summary>Destination tables of every rule, the refresh cursor and the version (feature 014, <c>DMND</c>).</summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Width);
        writer.WriteInt(_townCursor);
        writer.WriteInt(Version);
        writer.WriteInt(_tables.Length);
        foreach (var table in _tables)
        {
            table.WriteState(writer);
        }
    }

    internal void ReadState(SaveReader reader)
    {
        if (reader.ReadInt() != Width)
        {
            throw reader.Fail("destination table width differs (demand rules changed)");
        }

        _townCursor = reader.ReadInt();
        Version = reader.ReadInt();
        if (reader.ReadInt() != _tables.Length)
        {
            throw reader.Fail("destination rule count differs (demand rules changed)");
        }

        foreach (var table in _tables)
        {
            table.ReadState(reader);
        }
    }

    private sealed partial class RuleTables
    {
        public void WriteState(SaveWriter writer)
        {
            writer.WriteDictionary(RowOfIndustry);
            writer.WriteInt(Rows);
            writer.WriteInt(Cursor);
            writer.WriteArray(Source);
            writer.WriteArray(X);
            writer.WriteArray(Z);
            writer.WriteArray(Count);
            writer.WriteArray(Dest);
            writer.WriteArray(Share);
            writer.WriteArray(Carry);
            writer.WriteArray(MovedThis);
            writer.WriteArray(MovedLast);
            writer.WriteArray(UnservedThis);
            writer.WriteArray(UnservedLast);
        }

        public void ReadState(SaveReader reader)
        {
            reader.ReadDictionaryInto(RowOfIndustry);
            Rows = reader.ReadInt();
            Cursor = reader.ReadInt();
            Source = reader.ReadRequiredArray<int>();
            X = reader.ReadRequiredArray<int>();
            Z = reader.ReadRequiredArray<int>();
            Count = reader.ReadRequiredArray<int>();
            Dest = reader.ReadRequiredArray<int>();
            Share = reader.ReadRequiredArray<int>();
            Carry = reader.ReadRequiredArray<long>();
            MovedThis = reader.ReadRequiredArray<int>();
            MovedLast = reader.ReadRequiredArray<int>();
            UnservedThis = reader.ReadRequiredArray<int>();
            UnservedLast = reader.ReadRequiredArray<int>();
            if (Rows < 0 || Rows > Count.Length || Source.Length != Count.Length || X.Length != Count.Length || Z.Length != Count.Length)
            {
                throw reader.Fail("destination table rows are inconsistent");
            }
        }
    }
}

public sealed partial class RouteTable
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Company);
        writer.WriteInt(Cargo);
        writer.WriteInt(Count);
        writer.WriteInt(NodeCount);
        writer.WriteArray(_group);
        writer.WriteArray(_town);
        writer.WriteArray(_hop1);
        writer.WriteArray(_hop2);
        writer.WriteArray(_share2);
        writer.WriteArray(_carry);
        writer.WriteArray(_nodes);
    }

    internal static RouteTable ReadState(SaveReader reader)
    {
        var table = new RouteTable
        {
            Company = reader.ReadInt(),
            Cargo = reader.Content.Map(ContentTable.Cargo, reader.ReadInt()),
            Count = reader.ReadInt(),
            NodeCount = reader.ReadInt(),
            _group = reader.ReadRequiredArray<int>(),
            _town = reader.ReadRequiredArray<int>(),
            _hop1 = reader.ReadRequiredArray<int>(),
            _hop2 = reader.ReadRequiredArray<int>(),
            _share2 = reader.ReadRequiredArray<int>(),
            _carry = reader.ReadRequiredArray<int>(),
            _nodes = reader.ReadRequiredArray<int>(),
        };
        if (table.Count < 0 || table.Count > table._group.Length || table.NodeCount < 0 || table.NodeCount > table._nodes.Length)
        {
            throw reader.Fail("route table is inconsistent");
        }

        return table;
    }
}

public sealed partial class RoutingProblem
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Company);
        writer.WriteInt(Cargo);
        writer.WriteInt(NodeCount);
        writer.WriteInt(EdgeCount);
        writer.WriteInt(ServingCount);
        writer.WriteInt(FlowCount);
        writer.WriteInt(TableEntryCount);
        writer.WriteList(_rawEdges);
        writer.WriteArray(_pairs);
        writer.WriteArray(_key);
        writer.WriteArray(_x);
        writer.WriteArray(_z);
        writer.WriteArray(_edgeStart);
        writer.WriteArray(_edgeTo);
        writer.WriteArray(_edgeTicks);
        writer.WriteArray(_edgeCapacity);
        writer.WriteArray(_servingTown);
        writer.WriteArray(_servingNode);
        writer.WriteArray(_flowNode);
        writer.WriteArray(_flowRow);
        writer.WriteArray(_flowUnits);
        writer.WriteDictionary(Rows);
        writer.WriteArray(_tableDest);
        writer.WriteArray(_tableShare);
    }

    internal static RoutingProblem ReadState(SaveReader reader)
    {
        var problem = new RoutingProblem
        {
            Company = reader.ReadInt(),
            Cargo = reader.Content.Map(ContentTable.Cargo, reader.ReadInt()),
            NodeCount = reader.ReadInt(),
            EdgeCount = reader.ReadInt(),
            ServingCount = reader.ReadInt(),
            FlowCount = reader.ReadInt(),
            TableEntryCount = reader.ReadInt(),
        };
        reader.ReadListInto(problem._rawEdges);
        problem._pairs = reader.ReadRequiredArray<long>();
        problem._key = reader.ReadRequiredArray<int>();
        problem._x = reader.ReadRequiredArray<int>();
        problem._z = reader.ReadRequiredArray<int>();
        problem._edgeStart = reader.ReadRequiredArray<int>();
        problem._edgeTo = reader.ReadRequiredArray<int>();
        problem._edgeTicks = reader.ReadRequiredArray<int>();
        problem._edgeCapacity = reader.ReadRequiredArray<int>();
        problem._servingTown = reader.ReadRequiredArray<int>();
        problem._servingNode = reader.ReadRequiredArray<int>();
        problem._flowNode = reader.ReadRequiredArray<int>();
        problem._flowRow = reader.ReadRequiredArray<int>();
        problem._flowUnits = reader.ReadRequiredArray<int>();
        reader.ReadDictionaryInto(problem.Rows);
        problem._tableDest = reader.ReadRequiredArray<int>();
        problem._tableShare = reader.ReadRequiredArray<int>();
        return problem;
    }
}

public sealed partial class LinkCollector
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteBool(_open);
        writer.WriteInt(LineCount);
        writer.WriteInt(StopCount);
        writer.WriteArray(_lineCompany);
        writer.WriteArray(_lineCargo);
        writer.WriteArray(_lineTicksPerTileMilli);
        writer.WriteArray(_lineUnitsPerRound);
        writer.WriteArray(_lineStopStart);
        writer.WriteArray(_lineStopCount);
        writer.WriteArray(_stopStation);
        writer.WriteArray(_stopFlags);
    }

    internal void ReadState(SaveReader reader)
    {
        _open = reader.ReadBool();
        LineCount = reader.ReadInt();
        StopCount = reader.ReadInt();
        _lineCompany = reader.ReadRequiredArray<int>();
        _lineCargo = reader.ReadRequiredArray<int>();
        reader.Content.MapInPlace(ContentTable.Cargo, _lineCargo.AsSpan(0, LineCount));
        _lineTicksPerTileMilli = reader.ReadRequiredArray<int>();
        _lineUnitsPerRound = reader.ReadRequiredArray<int>();
        _lineStopStart = reader.ReadRequiredArray<int>();
        _lineStopCount = reader.ReadRequiredArray<int>();
        _stopStation = reader.ReadRequiredArray<int>();
        _stopFlags = reader.ReadRequiredArray<byte>();
        if (LineCount < 0 || LineCount > _lineCompany.Length || StopCount < 0 || StopCount > _stopStation.Length)
        {
            throw reader.Fail("link collector is inconsistent");
        }
    }
}
