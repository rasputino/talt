using Talt.Core.Saves;

namespace Talt.Core.Demand;

public sealed partial class CargoRouter
{
    /// <summary>The stage of a job being built over several ticks (0: none; tests of saves taken mid-build).</summary>
    internal int BuildStage => _buildStage;

    /// <summary>
    /// The router's schedule, version stamps, current route tables, a solved but not yet applied result, and the problems
    /// and collected lines of a job still being built (feature 014, research R1). The caller waits for a running job first.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        if (_jobRunning)
        {
            throw new InvalidOperationException("Wait for the route job before saving.");
        }

        writer.WriteLong(_nextStartTick);
        writer.WriteLong(_pendingApplyTick);
        writer.WriteInt(_startedVersion);
        writer.WriteBool(_everStarted);
        writer.WriteInt(_buildStage);
        writer.WriteInt(_applied);
        writer.WriteInt(Version);
        writer.WriteInt(LastDestinations);
        writer.WriteInt(_problemCount);
        WriteTables(writer, _current);
        WriteTables(writer, _spare);
        WriteTables(writer, _nextPool);
        writer.WriteInt(_problemPool.Count);
        foreach (var problem in _problemPool)
        {
            problem.WriteState(writer);
        }

        _collector.WriteState(writer);
        writer.WriteList(_stopGroups);
        writer.WriteArray(_stopKey);
        writer.WriteArray(_stopX);
        writer.WriteArray(_stopZ);
        writer.WriteList(_keys);
        _tables.WriteState(writer);
    }

    internal void ReadState(SaveReader reader)
    {
        _nextStartTick = reader.ReadLong();
        _pendingApplyTick = reader.ReadLong();
        _startedVersion = reader.ReadInt();
        _everStarted = reader.ReadBool();
        _buildStage = reader.ReadInt(0, 3);
        _applied = reader.ReadInt();
        Version = reader.ReadInt();
        LastDestinations = reader.ReadInt();
        _problemCount = reader.ReadInt();
        ReadTables(reader, _current);
        ReadTables(reader, _spare);
        ReadTables(reader, _nextPool);
        var problems = reader.ReadCount(16);
        _problemPool.Clear();
        for (var i = 0; i < problems; i++)
        {
            _problemPool.Add(RoutingProblem.ReadState(reader));
        }

        if (_problemCount < 0 || _problemCount > _problemPool.Count || (_pendingApplyTick >= 0 && _problemCount > _nextPool.Count))
        {
            throw reader.Fail("route job state is inconsistent");
        }

        _collector.ReadState(reader);
        reader.ReadListInto(_stopGroups);
        _stopKey = reader.ReadRequiredArray<int>();
        _stopX = reader.ReadRequiredArray<int>();
        _stopZ = reader.ReadRequiredArray<int>();
        reader.ReadListInto(_keys);
        for (var i = 0; i < _keys.Count; i++)
        {
            _keys[i] = (_keys[i] & ~255) | reader.Content.Map(ContentTable.Cargo, _keys[i] & 255);
        }
        _tables.ReadState(reader);
    }

    private static void WriteTables(SaveWriter writer, List<RouteTable> tables)
    {
        writer.WriteInt(tables.Count);
        foreach (var table in tables)
        {
            table.WriteState(writer);
        }
    }

    private static void ReadTables(SaveReader reader, List<RouteTable> tables)
    {
        var count = reader.ReadCount(16);
        tables.Clear();
        for (var i = 0; i < count; i++)
        {
            tables.Add(RouteTable.ReadState(reader));
        }
    }
}
