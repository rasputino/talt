using Talt.Core.Determinism;

namespace Talt.Core.Vehicles;

/// <summary>What vehicles do at a stop of a line (FR-012).</summary>
public enum StopRule : byte
{
    /// <summary>Unload, then load what is waiting and leave (default).</summary>
    LoadIfAvailable = 0,

    /// <summary>Unload, then wait until full.</summary>
    FullLoad = 1,

    /// <summary>Unload and load nothing.</summary>
    UnloadOnly = 2,

    /// <summary>Neither unload nor load.</summary>
    NoLoading = 3,
}

/// <summary>One stop of a line: a station of the company and its rule.</summary>
public readonly record struct LineStop(int StationId, StopRule Rule);

/// <summary>
/// The lines of a game (feature 012, data-model.md Line and stops): an ordered list of 1–64 stops shared by every
/// vehicle assigned to the line, a number for the automatic name, an optional custom name and a colour. Ids are
/// stable and never reused; lines are few, so each is a small object (not a hot path).
/// </summary>
public sealed class LineRegistry : IStateHashable
{
    /// <summary>Most stops of one line.</summary>
    public const int MaxStops = 64;

    /// <summary>Longest custom name.</summary>
    public const int MaxNameLength = 32;

    /// <summary>Number of line colours the client knows.</summary>
    public const int ColorCount = 12;

    private readonly List<Line> _lines = [];
    private readonly Dictionary<int, Line> _byId = [];
    private int _nextId = 1;

    public int Count => _lines.Count;

    /// <summary>Incremented on every change (windows refresh).</summary>
    public int Version { get; private set; }

    public bool EverUsed => _nextId > 1;

    /// <summary>The id the next line will get.</summary>
    public int NextId => _nextId;

    /// <summary>Line ids in creation order.</summary>
    public int IdAt(int index) => _lines[index].Id;

    public bool Exists(int lineId) => _byId.ContainsKey(lineId);

    public int OwnerOf(int lineId) => _byId[lineId].Owner;

    public int NumberOf(int lineId) => _byId[lineId].Number;

    public int ColorOf(int lineId) => _byId[lineId].Color;

    /// <summary>The custom name, or null for the automatic one ("Line N").</summary>
    public string? NameOf(int lineId) => _byId[lineId].Name;

    public int StopCount(int lineId) => _byId.TryGetValue(lineId, out var line) ? line.Stops.Count : 0;

    public LineStop StopAt(int lineId, int index) => _byId[lineId].Stops[index];

    /// <summary>Creates a line with the stops in order (consecutive duplicates merged); returns its id.</summary>
    public int Create(int ownerId, ReadOnlySpan<int> stations)
    {
        var number = 1;
        foreach (var other in _lines)
        {
            if (other.Owner == ownerId)
            {
                number = Math.Max(number, other.Number + 1);
            }
        }

        var line = new Line(_nextId++, ownerId, number, (number - 1) % ColorCount);
        foreach (var station in stations)
        {
            if (line.Stops.Count == 0 || line.Stops[^1].StationId != station)
            {
                line.Stops.Add(new LineStop(station, StopRule.LoadIfAvailable));
            }
        }

        _lines.Add(line);
        _byId.Add(line.Id, line);
        Version++;
        return line.Id;
    }

    public void Delete(int lineId)
    {
        if (_byId.Remove(lineId, out var line))
        {
            _lines.Remove(line);
            Version++;
        }
    }

    public void Rename(int lineId, string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        _byId[lineId].Name = trimmed.Length == 0 ? null : trimmed;
        Version++;
    }

    public void InsertStop(int lineId, int index, int stationId)
    {
        _byId[lineId].Stops.Insert(index, new LineStop(stationId, StopRule.LoadIfAvailable));
        MergeDuplicates(_byId[lineId]);
        Version++;
    }

    public void RemoveStop(int lineId, int index)
    {
        _byId[lineId].Stops.RemoveAt(index);
        MergeDuplicates(_byId[lineId]);
        Version++;
    }

    public void MoveStop(int lineId, int from, int to)
    {
        var stops = _byId[lineId].Stops;
        var stop = stops[from];
        stops.RemoveAt(from);
        stops.Insert(to, stop);
        MergeDuplicates(_byId[lineId]);
        Version++;
    }

    public void SetRule(int lineId, int index, StopRule rule)
    {
        var stops = _byId[lineId].Stops;
        stops[index] = stops[index] with { Rule = rule };
        Version++;
    }

    /// <summary>Drops every stop at a removed station (vehicles skip it already); lines keep at least nothing.</summary>
    public void RemoveStation(int stationId)
    {
        foreach (var line in _lines)
        {
            if (line.Stops.RemoveAll(s => s.StationId == stationId) > 0)
            {
                MergeDuplicates(line);
                Version++;
            }
        }
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_nextId);
        hasher.Append(_lines.Count);
        foreach (var line in _lines)
        {
            hasher.Append(line.Id);
            hasher.Append(line.Owner);
            hasher.Append(line.Number);
            hasher.Append(line.Color);
            hasher.Append(line.Name ?? string.Empty);
            hasher.Append(line.Stops.Count);
            foreach (var stop in line.Stops)
            {
                hasher.Append(stop.StationId);
                hasher.Append((int)stop.Rule);
            }
        }
    }

    private static void MergeDuplicates(Line line)
    {
        var stops = line.Stops;
        for (var i = stops.Count - 1; i > 0; i--)
        {
            if (stops[i].StationId == stops[i - 1].StationId)
            {
                stops.RemoveAt(i);
            }
        }

        if (stops.Count > 1 && stops[0].StationId == stops[^1].StationId)
        {
            stops.RemoveAt(stops.Count - 1);
        }
    }

    private sealed class Line(int id, int owner, int number, int color)
    {
        public int Id { get; } = id;

        public int Owner { get; } = owner;

        public int Number { get; } = number;

        public int Color { get; } = color;

        public string? Name { get; set; }

        public List<LineStop> Stops { get; } = [];
    }
}
