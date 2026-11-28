using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Rails;
using Talt.Core.Simulation;
using Talt.Core.Vehicles;

namespace Talt.Client.Screens;

/// <summary>The company's line and vehicle directories, accessible together from the lines toolbar.</summary>
internal static class LinesWindow
{
    public const string Kind = "lines";

    public static UiWindow Create(IVehicleUi ui, Action<bool, int> locate)
    {
        var tabs = new TabControl();
        tabs.AddTab(TextRef.Of("lines.title"), new LineDirectory(ui).Build());
        tabs.AddTab(TextRef.Of("directory.fleet"), new FleetDirectory(ui, locate).Build());
        return new UiWindow(Kind, TextRef.Of("directory.title")) { Key = Kind, SingleInstance = true, Content = tabs };
    }

    internal static Widget AssignedVehicles(IVehicleUi ui, int lineId)
    {
        void Inspect(bool rail, int id)
        {
            if (rail)
            {
                (ui as ITrainUi)?.OpenTrain(id);
            }
            else
            {
                ui.OpenVehicle(id);
            }
        }

        return new FleetDirectory(ui, Inspect, lineId).Build();
    }

    private abstract class Directory(IVehicleUi ui)
    {
        private (long Period, int Roads, int Trains, int Lines) _snapshot;
        private bool _dirty = true;
        protected IVehicleUi Ui { get; } = ui;
        protected GameSession Session => Ui.Session;
        protected RoadVehicleSystem Roads => Session.RoadVehicles!;
        protected TrainSystem Trains => Session.Simulation.Trains!;
        protected LineRegistry Lines => Roads.Lines;
        protected int Owner => Session.Construction.Player.Id;
        protected int Mode { get; private set; }
        protected long Revision { get; private set; }

        protected abstract int Count { get; }
        protected abstract void Collect();
        protected abstract long KeyAt(int index);
        protected abstract TableCell Cell(Strings strings, long key, int column);
        protected abstract void Select(long key);

        protected void Filter(StackPanel controls, string[] keys, Action<int> changed)
        {
            var choices = new List<DropdownChoice>(keys.Length);
            for (var i = 0; i < keys.Length; i++)
            {
                choices.Add(new DropdownChoice(TextRef.Of(keys[i]), i));
            }

            var selected = 0;
            controls.Add(new Dropdown(choices, () => selected, i =>
            {
                selected = i;
                changed(i);
                _dirty = true;
            }) { MinWidth = 94 });
        }

        protected void ModeFilter(StackPanel controls) =>
            Filter(controls, ["directory.allModes", "directory.road", "directory.rail"], i => Mode = i);

        protected void Invalidate() => _dirty = true;

        protected void AddTable(StackPanel content, string[] headers, float[] widths)
        {
            var header = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 0 });
            for (var i = 0; i < headers.Length; i++)
            {
                header.Add(new Label(TextRef.Of(headers[i]), TextSize.Small) { Width = widths[i], Color = TextColor.Muted });
            }

            content.Add(new VirtualTable(widths, Refresh, KeyAt, () => Revision, Cell, Select));
            content.Add(new Label(TextRef.Dynamic(() => Revision, s => Count == 0 ? s.Get("directory.empty") : string.Empty)) { Color = TextColor.Muted });
        }

        private int Refresh()
        {
            var version = (Session.Simulation.CurrentTick / GameClock.TicksPerSecondAtNormalSpeed, Roads.Version, Trains.Version, Lines.Version);
            if (_dirty || _snapshot != version)
            {
                _snapshot = version;
                _dirty = false;
                Collect();
                Revision++;
            }

            return Count;
        }
    }

    private sealed class FleetDirectory(IVehicleUi ui, Action<bool, int> locate, int? lineId = null) : Directory(ui)
    {
        private const long RailBit = 1L << 32;
        private readonly List<long> _ids = [];
        private readonly Dictionary<int, string> _stationNames = [];
        private (Strings? Language, int Stations) _nameVersion;
        private int _condition;
        protected override int Count => _ids.Count;

        public StackPanel Build()
        {
            var panel = new StackPanel(Orientation.Vertical) { Spacing = 4 };
            if (lineId.HasValue)
            {
                AddTable(panel, ["directory.number", "directory.vehicle", "directory.status", "directory.target", "directory.load"],
                    [45, 134, 105, 190, 68]);
                return panel;
            }

            var filters = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 4 });
            ModeFilter(filters);
            Filter(filters, ["directory.allVehicles", "directory.running", "directory.loading", "directory.broken",
                "directory.loss", "directory.old"], i => _condition = i);
            AddTable(panel, ["directory.number", "directory.vehicle", "directory.status", "directory.target", "directory.line", "directory.alerts"], [50, 145, 110, 185, 80, 155]);
            return panel;
        }

        protected override void Collect()
        {
            _ids.Clear();
            if (Mode != 2)
            {
                for (var i = 0; i < Roads.Count; i++)
                {
                    if (Roads.OwnerAt(i) == Owner && (!lineId.HasValue || Roads.LineAt(i) == lineId) && MatchesRoad(i))
                    {
                        _ids.Add(Roads.IdAt(i));
                    }
                }
            }

            if (Mode != 1)
            {
                for (var i = 0; i < Trains.Count; i++)
                {
                    var t = Trains.TrainAt(i);
                    if (t.Owner == Owner && (!lineId.HasValue || t.Line == lineId) && MatchesTrain(t))
                    {
                        _ids.Add(RailBit | (uint)t.Id);
                    }
                }
            }
        }

        private bool MatchesRoad(int i) => _condition switch
        {
            1 => Roads.StatusAt(i) == RoadVehicleStatus.Driving,
            2 => Roads.StatusAt(i) == RoadVehicleStatus.Loading,
            3 => Roads.StatusAt(i) == RoadVehicleStatus.BrokenDown,
            4 => Roads.ProfitThisYearAt(i) < 0,
            5 => RoadOld(i),
            _ => true,
        };

        private bool MatchesTrain(Train t) => _condition switch
        {
            1 => TrainSystem.StatusOf(t) == TrainStatus.Running,
            2 => TrainSystem.StatusOf(t) == TrainStatus.Loading,
            3 => TrainSystem.StatusOf(t) == TrainStatus.BrokenDown,
            4 => t.ProfitThisYear < 0,
            5 => TrainOld(t),
            _ => true,
        };

        private bool RoadOld(int slot) => Roads.AgeDaysAt(slot) >= Roads.ModelAt(slot).LifeYears * 365;
        private bool TrainOld(Train train) => train.Parts.Count > 0 && train.AgeDays >= Trains.ModelOf(train.Parts[0]).LifeYears * 365;

        protected override long KeyAt(int index) => _ids[index];
        protected override void Select(long key) => locate((key & RailBit) != 0, (int)key);

        protected override TableCell Cell(Strings s, long key, int column)
        {
            var id = (int)key;
            if ((key & RailBit) == 0 && Roads.SlotOf(id) is var slot && slot >= 0)
            {
                return column switch
                {
                    0 => id,
                    1 => s.Get(Roads.ModelAt(slot).NameKey),
                    2 => s.Get(RoadStatusKeys[(int)Roads.StatusAt(slot)]),
                    3 => Roads.TargetKindAt(slot) switch
                    {
                        VehicleTargetKind.Stop => StationName(s, Roads.TargetIdAt(slot)),
                        VehicleTargetKind.Depot => s.Get("vehicle.target.depot"),
                        _ => string.Empty,
                    },
                    4 when lineId.HasValue => TableCell.Gauge(InfoRows.Share(Roads.HoldAt(slot).Total, Roads.ModelAt(slot).Capacity), InfoRows.CargoColor(Session.Simulation.Content, Roads.CargoAt(slot))),
                    4 => LineCell(s, Roads.LineAt(slot)),
                    _ => Alerts(s, Roads.ProfitThisYearAt(slot) < 0, RoadOld(slot)),
                };
            }

            if ((key & RailBit) != 0 && Trains.Find(id) is { } t)
            {
                return column switch
                {
                    0 => id,
                    1 => t.Parts.Count > 0 ? s.Get(Trains.ModelOf(t.Parts[0]).NameKey) : string.Empty,
                    2 => s.Get(TrainStatusKeys[(int)TrainSystem.StatusOf(t)]),
                    3 => t.TargetKind switch
                    {
                        RouteGoalKind.Station => StationName(s, t.TargetId),
                        RouteGoalKind.Depot or RouteGoalKind.AnyDepot => s.Get("vehicle.target.depot"),
                        _ => string.Empty,
                    },
                    4 when lineId.HasValue => TableCell.Gauge(LineWindow.TrainLoad(Trains, t, Session.Simulation.Content.CargoCount), null),
                    4 => LineCell(s, t.Line),
                    _ => Alerts(s, t.ProfitThisYear < 0, TrainOld(t)),
                };
            }

            return string.Empty;
        }

        private static readonly string[] RoadStatusKeys = ["vehicle.status.indepot", "vehicle.status.stopped", "directory.running",
            "directory.loading", "directory.waiting", "directory.toDepot", "directory.broken", "directory.stuck", "directory.noRoute", "directory.noOrders"];
        private static readonly string[] TrainStatusKeys = ["train.status.indepot", "train.status.stopped", "directory.running",
            "directory.loading", "directory.signal", "directory.toDepot", "directory.broken", "directory.noRoute", "directory.noOrders", "directory.stuck"];

        private string StationName(Strings s, int station)
        {
            var version = (s, Session.Simulation.Stations!.Version);
            if (version != _nameVersion)
            {
                _stationNames.Clear();
                _nameVersion = version;
            }

            if (!_stationNames.TryGetValue(station, out var name))
            {
                name = VehicleTexts.StationName(s, Session, station);
                _stationNames.Add(station, name);
            }

            return name;
        }

        private TableCell LineCell(Strings s, int line)
        {
            if (!Lines.Exists(line))
            {
                return s.Get("line.none");
            }

            return Lines.NameOf(line) is { } name ? name : (TableCell)Lines.NumberOf(line);
        }

        private static string Alerts(Strings s, bool loss, bool old) => (loss, old) switch
        {
            (true, true) => s.Get("directory.lossAndOld"),
            (true, false) => s.Get("directory.loss"),
            (false, true) => s.Get("directory.old"),
            _ => string.Empty,
        };
    }

    private sealed class LineDirectory(IVehicleUi ui) : Directory(ui)
    {
        private readonly List<int> _ids = [];
        private readonly Dictionary<int, (int Road, int Rail)> _counts = [];
        private readonly string[] _headers = ["directory.line", "directory.vehicles", "directory.thisYear",
            "directory.lastYear", "directory.passengers", "directory.freight", "directory.load"];
        private int _profitFilter;
        private int _sort;
        private bool _ascending = true;
        private Comparison<int>? _compare;
        protected override int Count => _ids.Count;

        public StackPanel Build()
        {
            var panel = new StackPanel(Orientation.Vertical) { Spacing = 4 };
            var filters = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 4 });
            ModeFilter(filters);
            Filter(filters, ["directory.allResults", "directory.profit", "directory.loss"], i => _profitFilter = i);
            filters.Add(new Label(TextRef.Of("directory.sort")));
            Filter(filters, _headers, i => _sort = i);
            filters.Add(new Button(TextRef.Dynamic(() => _ascending ? 1 : 0,
                s => s.Get(_ascending ? "directory.ascending" : "directory.descending")), () =>
                {
                    _ascending = !_ascending;
                    Invalidate();
                }));
            AddTable(panel, _headers, [145, 60, 108, 108, 80, 70, 66]);
            panel.Add(new Button(TextRef.Of("lines.new"), () => Ui.StartLineMode(-1, -1))
            {
                HorizontalAlignment = Alignment.Start,
                Tooltip = TextRef.Of("lines.new.tooltip"),
            });
            return panel;
        }

        protected override void Collect()
        {
            _ids.Clear();
            _counts.Clear();
            for (var i = 0; i < Roads.Count; i++)
            {
                var line = Roads.LineAt(i);
                if (line >= 0)
                {
                    var counts = _counts.GetValueOrDefault(line);
                    _counts[line] = (counts.Road + 1, counts.Rail);
                }
            }

            for (var i = 0; i < Trains.Count; i++)
            {
                var line = Trains.TrainAt(i).Line;
                if (line >= 0)
                {
                    var counts = _counts.GetValueOrDefault(line);
                    _counts[line] = (counts.Road, counts.Rail + 1);
                }
            }

            for (var i = 0; i < Lines.Count; i++)
            {
                var id = Lines.IdAt(i);
                var counts = _counts.GetValueOrDefault(id);
                var profit = Lines.CurrentYear(id).Profit;
                if (Lines.OwnerOf(id) == Owner && (Mode != 1 || counts.Road > 0) && (Mode != 2 || counts.Rail > 0)
                    && (_profitFilter != 1 || profit > 0) && (_profitFilter != 2 || profit < 0))
                {
                    _ids.Add(id);
                }
            }

            _ids.Sort(_compare ??= Compare);
        }

        private int Compare(int a, int b)
        {
            var result = _sort == 0
                ? StringComparer.OrdinalIgnoreCase.Compare(Lines.NameOf(a), Lines.NameOf(b))
                : Metric(a, _sort).CompareTo(Metric(b, _sort));
            if (result == 0)
            {
                result = a.CompareTo(b);
            }

            return _ascending ? result : -Math.Sign(result);
        }

        private long Metric(int id, int column)
        {
            var stats = Lines.CurrentYear(id);
            var counts = _counts.GetValueOrDefault(id);
            return column switch
            {
                1 => counts.Road + counts.Rail,
                2 => stats.Profit,
                3 => Lines.HasPreviousYear(id) ? Lines.PreviousYear(id).Profit : long.MinValue,
                4 => stats.Passengers,
                5 => stats.Freight,
                _ => stats.LoadPercent ?? -1,
            };
        }

        protected override long KeyAt(int index) => _ids[index];

        protected override void Select(long key)
        {
            if (Lines.Exists((int)key))
            {
                Ui.OpenLine((int)key);
            }
        }

        protected override TableCell Cell(Strings s, long key, int column)
        {
            var id = (int)key;
            if (column == 0)
            {
                return Lines.NameOf(id) is { } name ? name : new TableCell(null, Lines.NumberOf(id), NumberTemplate: s.Get("line.name"));
            }

            var value = Metric(id, column);
            return column switch
            {
                3 when value == long.MinValue => s.Get("directory.noSample"),
                2 or 3 => Ui.MoneyCell(value, s),
                6 => value < 0 ? s.Get("directory.noSample") : new TableCell(null, value / 100m, "P0"),
                _ => new TableCell(null, value, "N0"),
            };
        }
    }
}
