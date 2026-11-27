using Talt.Core.Demand;
using Talt.Core.Determinism;

namespace Talt.Core.Vehicles;

/// <summary>
/// Road vehicles as a source of the cargo network (feature 013, research R3): every line with vehicles, once per cargo
/// they carry, with the stops in order, the units its vehicles carry per round and the time its slowest vehicle needs
/// per tile.
/// </summary>
public sealed partial class RoadVehicleSystem : ILinkSource
{
    // Per (line id × cargo): capacity and slowest speed of the vehicles on it, filled by CollectLinks (reused).
    private int[] _lineUnits = [];
    private int[] _lineMinKmh = [];
    private int _linkChanges;

    /// <inheritdoc />
    public int LinkVersion => unchecked(Lines.Version + Version + _linkChanges);

    /// <inheritdoc />
    public void CollectLinks(LinkCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        var cargoCount = _content.CargoCount;
        var cells = Lines.NextId * cargoCount;
        if (_lineUnits.Length < cells)
        {
            _lineUnits = new int[Math.Max(cells, _lineUnits.Length * 2)];
            _lineMinKmh = new int[_lineUnits.Length];
        }

        Array.Clear(_lineUnits, 0, cells);
        var any = false;
        for (var slot = 0; slot < Count; slot++)
        {
            var line = _line[slot];
            if (line < 0 || line >= Lines.NextId || (uint)_cargo[slot] >= (uint)cargoCount)
            {
                continue;
            }

            var cell = (line * cargoCount) + _cargo[slot];
            var model = Rules.Models[_model[slot]];
            _lineMinKmh[cell] = _lineUnits[cell] == 0 ? model.SpeedKmh : Math.Min(_lineMinKmh[cell], model.SpeedKmh);
            _lineUnits[cell] += model.Capacity;
            any = true;
        }

        if (!any)
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

            for (var cargo = 0; cargo < cargoCount; cargo++)
            {
                var cell = (line * cargoCount) + cargo;
                if (_lineUnits[cell] == 0)
                {
                    continue;
                }

                var sum = (Units: _lineUnits[cell], MinKmh: _lineMinKmh[cell]);

                // Ticks per tile at the slowest vehicle's top speed, in thousandths.
                var ticksPerTileMilli = (int)Math.Min(int.MaxValue, 1000L * FixedMath.TicksPerSecond * Rules.KmhPerTileSecond / Math.Max(1, sum.MinKmh));
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

    /// <summary>The station of the stop after the vehicle's current one on its line, or −1.</summary>
    private int NextStopStation(int slot)
    {
        var line = _line[slot];
        if (line < 0 || !Lines.Exists(line))
        {
            return -1;
        }

        var count = Lines.StopCount(line);
        if (count < 2)
        {
            return -1;
        }

        return Lines.StopAt(line, (_stopIndex[slot] + 1) % count).StationId;
    }
}
