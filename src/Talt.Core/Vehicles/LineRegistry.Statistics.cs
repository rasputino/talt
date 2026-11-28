using Talt.Core.Determinism;
using Talt.Core.Finance;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>A line's operating result, boardings and distance-weighted occupancy for one calendar year.</summary>
public readonly record struct LineYearStatistics(long Profit, long Passengers, long Freight, long OccupiedDistance, long CapacityDistance)
{
    /// <summary>Rounded percentage of available unit-distance used, or null before any capacity was offered.</summary>
    public int? LoadPercent => CapacityDistance == 0 ? null : (int)((((Int128)OccupiedDistance * 100) + (CapacityDistance / 2)) / CapacityDistance);

    internal void AppendTo(StateHasher hasher)
    {
        hasher.Append(Profit);
        hasher.Append(Passengers);
        hasher.Append(Freight);
        hasher.Append(OccupiedDistance);
        hasher.Append(CapacityDistance);
    }
}

public sealed partial class LineRegistry
{
    internal Action? CollectOpenDistances { private get; init; }

    private bool _openDistancesDirty;

    /// <summary>Attributed totals since the start of the current year or the creation of the line.</summary>
    public LineYearStatistics CurrentYear(int lineId)
    {
        if (_openDistancesDirty)
        {
            foreach (var entry in _lines)
            {
                entry.OpenOccupied = 0;
                entry.OpenOffered = 0;
            }

            CollectOpenDistances?.Invoke();
            _openDistancesDirty = false;
        }

        var line = _byId[lineId];
        return line.Current with
        {
            OccupiedDistance = line.Current.OccupiedDistance + line.OpenOccupied,
            CapacityDistance = line.Current.CapacityDistance + line.OpenOffered,
        };
    }

    // Read-only views and fingerprints share one scan per movement revision, regardless of how many lines they inspect.
    internal void InvalidateOpenDistances() => _openDistancesDirty = true;

    internal void AddOpenDistance(int lineId, long distance, int load, int capacity)
    {
        if (_byId.TryGetValue(lineId, out var line))
        {
            line.OpenOccupied += distance * load;
            line.OpenOffered += distance * capacity;
        }
    }

    /// <summary>Attributed totals for the last closed year.</summary>
    public LineYearStatistics PreviousYear(int lineId) => _byId[lineId].Previous;

    /// <summary>Whether the line existed at the last year boundary.</summary>
    public bool HasPreviousYear(int lineId) => _byId[lineId].HasPreviousYear;

    /// <summary>Whole simulation days between first-stop departures of the same mode. Zero disables the hold.</summary>
    public int IntervalDays(int lineId) => _byId[lineId].IntervalDays;

    internal void RecordProfit(int lineId, long delta)
    {
        if (delta != 0 && _byId.TryGetValue(lineId, out var line))
        {
            line.Current = line.Current with { Profit = Money.Add(line.Current.Profit, delta) };
        }
    }

    internal void RecordBoarding(int lineId, int amount, bool passengers)
    {
        if (amount > 0 && _byId.TryGetValue(lineId, out var line))
        {
            var current = line.Current;
            line.Current = passengers
                ? current with { Passengers = current.Passengers + amount }
                : current with { Freight = current.Freight + amount };
        }
    }

    internal void RecordDistance(int lineId, long distance, int occupied, int capacity)
    {
        if (_byId.TryGetValue(lineId, out var line))
        {
            var current = line.Current;
            line.Current = current with
            {
                OccupiedDistance = current.OccupiedDistance + (occupied * distance),
                CapacityDistance = current.CapacityDistance + (capacity * distance),
            };
        }
    }

    internal void CloseYear()
    {
        foreach (var line in _lines)
        {
            line.Previous = line.Current;
            line.HasPreviousYear = true;
            line.Current = default;
        }
    }

    internal void SetIntervalDays(int lineId, int days)
    {
        var line = _byId[lineId];
        line.IntervalDays = days;
        if (days == 0)
        {
            line.LastRoadDeparture = -1;
            line.LastRailDeparture = -1;
        }
    }

    internal bool TryDispatch(int lineId, int station, long tick, bool rail)
    {
        if (!_byId.TryGetValue(lineId, out var line) || line.IntervalDays == 0 || line.Stops.Count == 0 || line.Stops[0].StationId != station)
        {
            return true;
        }

        ref var last = ref (rail ? ref line.LastRailDeparture : ref line.LastRoadDeparture);
        if (last >= 0 && tick - last < (long)line.IntervalDays * GameClock.TicksPerDay)
        {
            return false;
        }

        last = tick;
        return true;
    }
}
