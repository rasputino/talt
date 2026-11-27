using Talt.Core.Demand;
using Talt.Core.Determinism;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>
/// Trains as a source of the cargo network (feature 013, research R3): every line with trains, once per cargo their
/// wagons carry, with the stops in order, the units the wagons carry per round and the time the slowest train needs per
/// tile. Destination cargo then rides trains through the same stations and routes as road vehicles.
/// </summary>
public sealed partial class TrainSystem : ILinkSource
{
    private readonly Dictionary<long, (int Units, int MinKmh)> _lineCargo = [];
    private int _linkChanges;

    /// <inheritdoc />
    public int LinkVersion => unchecked(Lines.Version + Version + _linkChanges);

    /// <inheritdoc />
    public void CollectLinks(LinkCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        _lineCargo.Clear();
        foreach (var train in _trains)
        {
            if (train.Line < 0)
            {
                continue;
            }

            var kmh = Math.Max(1, Rules.KmhFromSpeed(train.MaxSpeed));
            foreach (var part in train.Parts)
            {
                if (part.Cargo < 0)
                {
                    continue;
                }

                var key = ((long)train.Line << 8) | (uint)part.Cargo;
                _lineCargo.TryGetValue(key, out var sum);
                _lineCargo[key] = (sum.Units + Rules.Models[part.Model].Capacity, sum.Units == 0 ? kmh : Math.Min(sum.MinKmh, kmh));
            }
        }

        if (_lineCargo.Count == 0)
        {
            return;
        }

        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines.IdAt(i);
            var stops = Lines.StopCount(line);
            if (stops < 2)
            {
                continue;
            }

            for (var cargo = 0; cargo < _content.CargoCount; cargo++)
            {
                if (!_lineCargo.TryGetValue(((long)line << 8) | (uint)cargo, out var sum) || sum.Units == 0)
                {
                    continue;
                }

                var ticksPerTileMilli = (int)Math.Min(int.MaxValue, 1000L * FixedMath.TicksPerSecond * Rules.KmhPerTileSecond / sum.MinKmh);
                collector.BeginLine(Lines.OwnerOf(line), cargo, ticksPerTileMilli, sum.Units);
                for (var s = 0; s < stops; s++)
                {
                    var stop = Lines.StopAt(line, s);
                    collector.AddStop(
                        stop.StationId,
                        loads: stop.Rule is StopRule.LoadIfAvailable or StopRule.FullLoad,
                        unloads: stop.Rule != StopRule.NoLoading);
                }

                collector.EndLine();
            }
        }
    }

    /// <summary>The station of the stop after the train's current one on its line, or −1.</summary>
    private int NextStopStation(Train train)
    {
        var line = train.Line;
        if (line < 0 || !Lines.Exists(line))
        {
            return -1;
        }

        var count = Lines.StopCount(line);
        return count < 2 ? -1 : Lines.StopAt(line, (train.StopIndex + 1) % count).StationId;
    }

    /// <summary>Lines, wagons' cargo or line assignments changed: routes are rebuilt.</summary>
    internal void OnLinksChanged() => _linkChanges++;
}
