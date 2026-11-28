using System.Runtime.InteropServices;
using Talt.Core.Determinism;

namespace Talt.Core.Simulation;

/// <summary>Physical link between two tile centres. Opposite directions share a count; modes do not.</summary>
public readonly record struct TrafficSegment(int First, int Last, bool Rail);

/// <summary>Actual crossings during the open month and the most recently completed calendar month.</summary>
public sealed class TrafficHistory : IStateHashable
{
    private Dictionary<TrafficSegment, int> _open = [];
    private Dictionary<TrafficSegment, int> _closed = [];
    private readonly Dictionary<TrafficSegment, int> _closedView = [];
    private readonly Dictionary<TrafficSegment, int> _openView = [];
    private RoadLane[] _roadLanes = [];
    private int _laneCount;
    private int _epoch = 1;
    private bool _closedViewDirty;

    public int Year { get; private set; }

    /// <summary>1–12, or zero until the first month end.</summary>
    public int Month { get; private set; }

    public IReadOnlyDictionary<TrafficSegment, int> Completed => ClosedView();

    /// <summary>Records one completed traversal, whether a bus, truck or complete train.</summary>
    internal void Record(int from, int to, bool rail)
        => Add(_open, from, to, rail, 1);

    // The road system assigns a compact, permanent index to each (tile, direction). Its far endpoint can
    // change when a bridge or tunnel is rebuilt, so preserve the old geometry along with its month's passages.
    internal void RegisterRoadLane(int index, int from, int to)
    {
        if (index >= _roadLanes.Length)
        {
            Array.Resize(ref _roadLanes, Math.Max(index + 1, Math.Max(256, _roadLanes.Length * 2)));
        }

        ref var lane = ref _roadLanes[index];
        if (lane.Epoch != 0 && lane.To == to)
        {
            return;
        }

        _laneCount = Math.Max(_laneCount, index + 1);
        lane.Advance(_epoch);
        if (lane.Count > 0)
        {
            Add(_open, lane.From, lane.To, rail: false, lane.Count);
            lane.Count = 0;
        }

        lane.From = from;
        lane.To = to;
    }

    internal void RecordRoadLane(int index)
    {
        ref var lane = ref _roadLanes[index];
        lane.Advance(_epoch);
        if (lane.Count < int.MaxValue)
        {
            lane.Count++;
        }
    }

    private static void Add(Dictionary<TrafficSegment, int> counts, int from, int to, bool rail, int amount)
    {
        var key = new TrafficSegment(Math.Min(from, to), Math.Max(from, to), rail);
        ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, key, out _);
        count = (int)Math.Min(int.MaxValue, (long)count + amount);
    }

    internal void CloseMonth(GameDate next)
    {
        Year = next.Month == 1 ? next.Year - 1 : next.Year;
        Month = next.Month == 1 ? 12 : next.Month - 1;
        (_open, _closed) = (_closed, _open);
        _open.Clear();
        _epoch++;
        _closedViewDirty = true;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        // An empty history's reporting date follows directly from the simulation clock.
        var closed = ClosedView();
        var open = _open;
        if (_laneCount > 0)
        {
            BuildView(_openView, closed: false);
            open = _openView;
        }

        if (open.Count == 0 && closed.Count == 0)
        {
            return;
        }

        hasher.Append(Year);
        hasher.Append(Month);
        AppendCounts(hasher, closed);
        AppendCounts(hasher, open);
    }

    private Dictionary<TrafficSegment, int> ClosedView()
    {
        if (_laneCount == 0)
        {
            return _closed;
        }

        if (_closedViewDirty)
        {
            BuildView(_closedView, closed: true);
            _closedViewDirty = false;
        }

        return _closedView;
    }

    private void BuildView(Dictionary<TrafficSegment, int> result, bool closed)
    {
        result.Clear();
        foreach (var item in closed ? _closed : _open)
        {
            result.Add(item.Key, item.Value);
        }

        var epoch = closed ? _epoch - 1 : _epoch;
        for (var i = 0; i < _laneCount; i++)
        {
            ref readonly var lane = ref _roadLanes[i];
            if (lane.Epoch == epoch && lane.Count > 0)
            {
                Add(result, lane.From, lane.To, rail: false, lane.Count);
            }
            else if (closed && lane.Epoch == _epoch && lane.PreviousCount > 0)
            {
                Add(result, lane.From, lane.PreviousTo, rail: false, lane.PreviousCount);
            }
        }
    }

    internal struct RoadLane
    {
        public int From;
        public int To;
        public int Count;
        public int Epoch;
        public int PreviousTo;
        public int PreviousCount;

        public void Advance(int epoch)
        {
            if (Epoch != epoch)
            {
                PreviousCount = Epoch == epoch - 1 ? Count : 0;
                PreviousTo = To;
                Count = 0;
                Epoch = epoch;
            }
        }
    }

    private static void AppendCounts(StateHasher hasher, Dictionary<TrafficSegment, int> counts)
    {
        hasher.Append(counts.Count);
        foreach (var entry in counts.OrderBy(e => e.Key.Rail).ThenBy(e => e.Key.First).ThenBy(e => e.Key.Last))
        {
            hasher.Append(entry.Key.Rail ? 1 : 0);
            hasher.Append(entry.Key.First);
            hasher.Append(entry.Key.Last);
            hasher.Append(entry.Value);
        }
    }
}
