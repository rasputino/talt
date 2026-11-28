using System.Globalization;
using System.Numerics;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Client.Screens;

/// <summary>
/// A vertical panel whose children are built again whenever <paramref name="version"/> changes (lists of vehicles,
/// stops, lines). It only rebuilds on a change, so steady frames allocate nothing.
/// </summary>
internal sealed class RebuildingPanel(Func<long> version, Action<StackPanel> build) : StackPanel(Orientation.Vertical)
{
    private long _built = long.MinValue;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var now = version();
        if (now != _built)
        {
            _built = now;
            ClearChildren();
            build(this);
        }

        return base.MeasureCore(ui, available);
    }
}

/// <summary>What the vehicle windows ask the game screen to do (feature 012).</summary>
internal interface IVehicleUi
{
    GameSession Session { get; }

    string Money(long amount, Strings s);

    void OpenVehicle(int vehicleId);

    void OpenDepot(int depotId);

    void OpenLine(int lineId);

    /// <summary>Starts the line-building mode: clicked stations are appended to <paramref name="lineId"/>, or form a new line for <paramref name="vehicleId"/>.</summary>
    void StartLineMode(int lineId, int vehicleId);
}

/// <summary>Texts shared by the vehicle windows.</summary>
internal static class VehicleTexts
{
    public static string ModelName(Strings s, VehicleModel model) => s.Get(model.NameKey);

    /// <summary>Index of the choice whose value is <paramref name="value"/>, or −1; a loop, as a predicate lambda would allocate every frame.</summary>
    public static int ChoiceIndex(List<DropdownChoice> choices, int value)
    {
        ArgumentNullException.ThrowIfNull(choices);
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i].Value is int choice && choice == value)
            {
                return i;
            }
        }

        return -1;
    }

    public static string VehicleName(Strings s, RoadVehicleSystem vehicles, int vehicleId)
    {
        var slot = vehicles.SlotOf(vehicleId);
        return slot < 0
            ? s.Get("vehicle.gone")
            : s.Format("vehicle.name", ("model", ModelName(s, vehicles.ModelAt(slot))), ("number", vehicleId.ToString(CultureInfo.InvariantCulture)));
    }

    public static string LineName(Strings s, LineRegistry lines, int lineId) =>
        !lines.Exists(lineId) ? s.Get("line.none")
        : lines.NameOf(lineId) ?? s.Format("line.name", ("number", lines.NumberOf(lineId).ToString(CultureInfo.InvariantCulture)));

    public static string StationName(Strings s, GameSession session, int stationId) =>
        session.Simulation.Stations is { } stations ? StationWindow.Name(s, stations, session.Simulation.Towns, stationId) : string.Empty;

    public static string CargoName(Strings s, GameSession session, int cargo) =>
        (uint)cargo < (uint)session.Simulation.Content.CargoCount ? s.Get(session.Simulation.Content.Cargo(cargo).NameKey) : string.Empty;

    /// <summary>"Driving to X", "Loading at X", "In depot"...</summary>
    public static string Status(Strings s, GameSession session, int vehicleId)
    {
        var vehicles = session.RoadVehicles!;
        var slot = vehicles.SlotOf(vehicleId);
        if (slot < 0)
        {
            return s.Get("vehicle.gone");
        }

        var status = vehicles.StatusAt(slot);
        var target = vehicles.TargetKindAt(slot) switch
        {
            VehicleTargetKind.Stop => StationName(s, session, vehicles.TargetIdAt(slot)),
            VehicleTargetKind.Depot => s.Get("vehicle.target.depot"),
            _ => s.Get("vehicle.target.none"),
        };
        return s.Format($"vehicle.status.{status.ToString().ToLowerInvariant()}", ("target", target));
    }
}

/// <summary>
/// The vehicle window (FR-035): model, line, state and target, load, speed, age, reliability, value, profits, and the
/// buttons to start or stop, send to the depot, sell (in a depot), clone, choose or create a line and change the
/// service interval. Every action is a recorded command.
/// </summary>
internal static class VehicleWindow
{
    public const string Kind = "vehicle";

    public static UiWindow Create(IVehicleUi ui, int vehicleId)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var vehicles = session.RoadVehicles!;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Dynamic(() => vehicles.Version, s => VehicleTexts.VehicleName(s, vehicles, vehicleId))) { Key = $"vehicle:{vehicleId}" };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 4 };
        long Tick() => session.Simulation.CurrentTick;
        int Slot() => vehicles.SlotOf(vehicleId);

        content.Add(new Label(TextRef.Dynamic(Tick, s => VehicleTexts.Status(s, session, vehicleId))) { Wrap = true, Width = 340 });

        // Graphical figures: the load bar split by destination, gauges for speed, reliability and age, the money.
        content.Add(new RebuildingPanel(() => vehicles.Version, panel =>
        {
            var slot = Slot();
            if (slot < 0)
            {
                return;
            }

            var cargo = vehicles.CargoAt(slot);
            var hold = new HoldSegments(Tick, () => Slot() >= 0 ? 1 : 0, _ => Slot() >= 0 ? vehicles.HoldAt(Slot()) : null, InfoRows.CargoColor(session.Simulation.Content, cargo));
            VehicleGauges.AddLoad(panel, session.Simulation.Content, session.Simulation.Towns, cargo, hold, () => Slot() >= 0 ? vehicles.ModelAt(Slot()).Capacity : 0, Tick);
            VehicleGauges.AddSpeed(panel, () => Slot() >= 0 ? vehicles.SpeedKmhAt(Slot()) : 0, () => Slot() >= 0 ? vehicles.ModelAt(Slot()).SpeedKmh : 0, Tick);
            VehicleGauges.AddReliability(panel, () => Slot() >= 0 ? vehicles.ReliabilityAt(Slot()) : 0, Tick);
            VehicleGauges.AddAge(panel, () => Slot() >= 0 ? vehicles.AgeDaysAt(Slot()) : 0, () => Slot() >= 0 ? vehicles.ModelAt(Slot()).LifeYears : 1, Tick);
            VehicleGauges.AddMoney(panel, ui, () => Slot() >= 0 ? vehicles.ValueAt(Slot()) : 0, () => Slot() >= 0 ? vehicles.ProfitThisYearAt(Slot()) : 0,
                () => Slot() >= 0 ? vehicles.ProfitLastYearAt(Slot()) : 0, Tick);
        }));

        // Line: a choice of the company's lines (rebuilt when lines change) and "New line".
        content.Add(new RebuildingPanel(() => vehicles.Lines.Version ^ ((long)vehicles.Version << 32), panel =>
        {
            var slot = Slot();
            if (slot < 0)
            {
                return;
            }

            var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
            row.Add(new Label(TextRef.Of("vehicle.line")) { Margin = new Thickness(0, 4, 0, 0) });
            var choices = new List<DropdownChoice> { new(TextRef.Of("line.none"), -1) };
            for (var i = 0; i < vehicles.Lines.Count; i++)
            {
                var lineId = vehicles.Lines.IdAt(i);
                if (vehicles.Lines.OwnerOf(lineId) == company)
                {
                    choices.Add(new DropdownChoice(TextRef.Dynamic(() => vehicles.Lines.Version, s => VehicleTexts.LineName(s, vehicles.Lines, lineId)), lineId));
                }
            }

            row.Add(new Dropdown(choices, () =>
            {
                var current = Slot() < 0 ? -1 : vehicles.LineAt(Slot());
                return Math.Max(0, VehicleTexts.ChoiceIndex(choices, current));
            }, index => session.Submit(new AssignLineCommand(company, vehicleId, (int)choices[index].Value))));
            row.Add(new Button(TextRef.Of("vehicle.newLine"), () => ui.StartLineMode(-1, vehicleId)) { Tooltip = TextRef.Of("vehicle.newLine.tooltip") });
            row.Add(new Button(TextRef.Of("vehicle.openLine"), () =>
            {
                if (Slot() >= 0 && vehicles.LineAt(Slot()) >= 0)
                {
                    ui.OpenLine(vehicles.LineAt(Slot()));
                }
            })
            { VisibleWhen = () => Slot() >= 0 && vehicles.LineAt(Slot()) >= 0 });
        }));

        var buttons = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        buttons.Add(new Button(TextRef.Dynamic(Tick, s => s.Get(Slot() >= 0 && vehicles.StateAt(Slot()).Has(RoadVehicleBits.Stopped) ? "vehicle.start" : "vehicle.stop")), () =>
        {
            if (Slot() >= 0)
            {
                session.Submit(new StartVehicleCommand(company, vehicleId, vehicles.StateAt(Slot()).Has(RoadVehicleBits.Stopped)));
            }
        }));
        buttons.Add(new Button(TextRef.Of("vehicle.sendToDepot"), () => session.Submit(new SendToDepotCommand(company, vehicleId)))
        {
            IsSelected = () => Slot() >= 0 && (vehicles.OrdersAt(Slot()) & VehicleOrders.SendToDepot) != 0,
            Tooltip = TextRef.Of("vehicle.sendToDepot.tooltip"),
        });
        buttons.Add(new Button(TextRef.Of("vehicle.clone"), () => session.Submit(new CloneVehicleCommand(company, vehicleId))) { Tooltip = TextRef.Of("vehicle.clone.tooltip") });
        buttons.Add(new Button(TextRef.Of("vehicle.sell"), () => session.Submit(new SellVehicleCommand(company, vehicleId)))
        {
            VisibleWhen = () => Slot() >= 0 && vehicles.DepotAt(Slot()) >= 0,
            Tooltip = TextRef.Of("vehicle.sell.tooltip"),
        });

        // Service interval in steps of 30 days (0 = never).
        var service = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        service.Add(new Label(TextRef.Dynamic(Tick, s => Slot() < 0 ? string.Empty
            : vehicles.ServiceIntervalAt(Slot()) == 0 ? s.Get("vehicle.service.never")
            : s.Format("vehicle.service", ("days", vehicles.ServiceIntervalAt(Slot()).ToString(CultureInfo.InvariantCulture))))) { MinWidth = 200, Margin = new Thickness(0, 4, 0, 0) });
        service.Add(new Button(TextRef.Of("vehicle.service.less"), () => ChangeService(session, company, vehicleId, -30)));
        service.Add(new Button(TextRef.Of("vehicle.service.more"), () => ChangeService(session, company, vehicleId, 30)));
        window.Content = content;
        return window;
    }

    private static void ChangeService(GameSession session, int company, int vehicleId, int delta)
    {
        var vehicles = session.RoadVehicles!;
        var slot = vehicles.SlotOf(vehicleId);
        if (slot >= 0)
        {
            session.Submit(new SetServiceIntervalCommand(company, vehicleId, Math.Clamp(vehicles.ServiceIntervalAt(slot) + delta, 0, 990)));
        }
    }
}

/// <summary>
/// The depot window (FR-003): the vehicles inside (open, start) and the models offered this year with their figures
/// and a buy button; trucks choose their cargo first.
/// </summary>
internal static class DepotWindow
{
    public const string Kind = "depot";

    public static UiWindow Create(IVehicleUi ui, int depotId)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var vehicles = session.RoadVehicles!;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Of("depot.title")) { Key = $"depot:{depotId}" };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 6 };
        var inside = new List<int>();

        content.Add(new Label(TextRef.Of("depot.inside"), TextSize.Title));
        content.Add(new RebuildingPanel(() => vehicles.Version ^ (session.Simulation.CurrentTick / 10 << 20), panel =>
        {
            vehicles.VehiclesInDepot(depotId, inside);
            if (inside.Count == 0)
            {
                panel.Add(new Label(TextRef.Of("depot.empty")) { Color = TextColor.Muted });
                return;
            }

            foreach (var id in inside)
            {
                var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
                row.Add(new Button(TextRef.Dynamic(() => vehicles.Version, s => VehicleTexts.VehicleName(s, vehicles, id)), () => ui.OpenVehicle(id)) { MinWidth = 200 });
                row.Add(new Button(TextRef.Of("vehicle.start"), () => session.Submit(new StartVehicleCommand(company, id, true))));
                row.Add(new Button(TextRef.Of("vehicle.sell"), () => session.Submit(new SellVehicleCommand(company, id))));
            }
        }));

        content.Add(new Label(TextRef.Of("depot.buy"), TextSize.Title) { Margin = new Thickness(0, 6, 0, 0) });
        DepotCatalog.Add(content, session, false, (panel, m) =>
        {
            var model = vehicles.Rules.Models[m];
            var row = panel.Add(new StackPanel(Orientation.Vertical) { Spacing = 2 });
            row.Add(new Label(TextRef.Dynamic(() => 0, s => s.Format("depot.model", ("name", VehicleTexts.ModelName(s, model)),
                ("price", ui.Money(session.Simulation.Finance?.Inflate(model.Price) ?? model.Price, s)),
                ("capacity", model.Capacity.ToString(CultureInfo.InvariantCulture)),
                ("speed", model.SpeedKmh.ToString(CultureInfo.InvariantCulture)),
                ("running", ui.Money(session.Simulation.Finance?.Inflate(model.RunningCostPerYear) ?? model.RunningCostPerYear, s)),
                ("life", model.LifeYears.ToString(CultureInfo.InvariantCulture)),
                ("reliability", (model.Reliability / 10).ToString(CultureInfo.InvariantCulture))))) { Wrap = true, Width = 360 });
            var cargos = new List<DropdownChoice>();
            for (var c = 0; c < session.Simulation.Content.CargoCount; c++)
            {
                if (model.Carries(c))
                {
                    var cargo = c;
                    cargos.Add(new DropdownChoice(TextRef.Dynamic(() => 0, s => VehicleTexts.CargoName(s, session, cargo)), session.Simulation.Content.Cargo(c).Id));
                }
            }

            row.Add(new Label(TextRef.Dynamic(() => 0, s => s.Format("catalog.cargo",
                ("cargo", cargos.Count == 0 ? s.Get("catalog.none") : string.Join(", ", Enumerable.Range(0, session.Simulation.Content.CargoCount)
                    .Where(model.Carries).Select(c => VehicleTexts.CargoName(s, session, c))))))) { Wrap = true, Width = 360 });
            var chosen = 0;
            var buy = row.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
            if (cargos.Count > 1)
            {
                buy.Add(new Dropdown(cargos, () => chosen, index => chosen = index));
            }

            buy.Add(new Button(TextRef.Of("depot.buyButton"), () =>
                session.Submit(new BuyVehicleCommand(company, depotId, model.Id, (string)cargos[Math.Min(chosen, cargos.Count - 1)].Value))));
        });
        window.Content = new ScrollArea(content) { MaxHeight = 420 };
        return window;
    }
}

/// <summary>
/// The line window (FR-036): a strip of the stops with a mark per vehicle, the stops in order with their loading rule
/// (reorder, remove), "add stops" mode, the road vehicles and trains on the line with a load gauge, renaming and deleting.
/// </summary>
internal static class LineWindow
{
    public const string Kind = "line";

    public static UiWindow Create(IVehicleUi ui, int lineId)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var vehicles = session.RoadVehicles!;
        var lines = vehicles.Lines;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Dynamic(() => lines.Version, s => VehicleTexts.LineName(s, lines, lineId))) { Key = $"line:{lineId}" };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 6 };
        var onLine = new List<int>();
        var scratch = new List<int>();

        // The stops as a strip with a mark per vehicle (road vehicles and trains), refreshed every 10 ticks.
        content.Add(new StopStrip(() => lines.Exists(lineId) ? lines.StopCount(lineId) : 0,
            () => (session.Simulation.CurrentTick / 10) ^ ((long)lines.Version << 40),
            into => LineMarkers(session, lineId, scratch, into))
        {
            Tooltip = TextRef.Of("line.strip.tooltip"),
            VisibleWhen = () => lines.Exists(lineId) && lines.StopCount(lineId) > 0,
        });

        content.Add(new RebuildingPanel(() => lines.Version, panel =>
        {
            if (!lines.Exists(lineId))
            {
                panel.Add(new Label(TextRef.Of("line.gone")) { Color = TextColor.Muted });
                return;
            }

            var rules = new List<DropdownChoice>();
            foreach (var rule in Enum.GetValues<StopRule>())
            {
                rules.Add(new DropdownChoice(TextRef.Of($"line.rule.{rule.ToString().ToLowerInvariant()}"), rule));
            }

            for (var i = 0; i < lines.StopCount(lineId); i++)
            {
                var index = i;
                var stop = lines.StopAt(lineId, i);
                var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 4 });
                row.Add(new Label(TextRef.Dynamic(() => 0, s => $"{index + 1}. {VehicleTexts.StationName(s, session, stop.StationId)}")) { MinWidth = 190, Margin = new Thickness(0, 4, 0, 0) });
                row.Add(new Dropdown(rules, () => (int)lines.StopAt(lineId, Math.Min(index, lines.StopCount(lineId) - 1)).Rule,
                    choice => session.Submit(new EditLineStopsCommand(company, lineId, LineStopEdit.Rule, index, Rule: (StopRule)rules[choice].Value))) { MinWidth = 130 });
                row.Add(new Button(TextRef.Of("line.up"), () => session.Submit(new EditLineStopsCommand(company, lineId, LineStopEdit.Move, index, To: Math.Max(0, index - 1)))));
                row.Add(new Button(TextRef.Of("line.remove"), () => session.Submit(new EditLineStopsCommand(company, lineId, LineStopEdit.Remove, index))));
            }
        }));

        var buttons = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        buttons.Add(new Button(TextRef.Of("line.addStops"), () => ui.StartLineMode(lineId, -1)) { Tooltip = TextRef.Of("line.addStops.tooltip") });
        buttons.Add(new Button(TextRef.Of("line.delete"), () => session.Submit(new DeleteLineCommand(company, lineId))));

        var rename = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        var input = rename.Add(new TextInput(lines.Exists(lineId) ? lines.NameOf(lineId) ?? string.Empty : string.Empty, LineRegistry.MaxNameLength) { MinWidth = 200 });
        rename.Add(new Button(TextRef.Of("line.rename"), () => session.Submit(new RenameLineCommand(company, lineId, input.Text))));

        content.Add(new Label(TextRef.Of("line.vehicles"), TextSize.Title));
        var trains = session.Simulation.Trains;
        content.Add(new RebuildingPanel(() => vehicles.Version ^ ((long)lines.Version << 32) ^ ((long)(trains?.Version ?? 0) << 16) ^ (session.Simulation.CurrentTick / 40 << 48), panel =>
        {
            vehicles.VehiclesOnLine(lineId, onLine);
            var any = onLine.Count > 0;
            foreach (var id in onLine)
            {
                var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8 });
                row.Add(new Meter(() => vehicles.SlotOf(id) is var slot and >= 0 ? InfoRows.Share(vehicles.HoldAt(slot).Total, vehicles.ModelAt(slot).Capacity) : 0f, MeterColoring.Fixed)
                {
                    Width = 60,
                    FixedColor = vehicles.SlotOf(id) is var slot and >= 0 ? InfoRows.CargoColor(session.Simulation.Content, vehicles.CargoAt(slot)) : UiColor.White,
                    Tooltip = TextRef.Of("line.vehicle.load.tooltip"),
                });
                row.Add(new Button(TextRef.Dynamic(() => session.Simulation.CurrentTick / 20, s => VehicleTexts.VehicleName(s, vehicles, id) + " — " + VehicleTexts.Status(s, session, id)), () => ui.OpenVehicle(id))
                {
                    HorizontalAlignment = Alignment.Start,
                });
            }

            for (var i = 0; trains is not null && i < trains.Count; i++)
            {
                var train = trains.TrainAt(i);
                if (train.Line != lineId)
                {
                    continue;
                }

                any = true;
                var id = train.Id;
                var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8 });
                row.Add(new Meter(() => trains.Find(id) is { } t ? TrainLoad(trains, t, session.Simulation.Content.CargoCount) : 0f, MeterColoring.Accent)
                {
                    Width = 60,
                    Tooltip = TextRef.Of("line.vehicle.load.tooltip"),
                });
                row.Add(new Button(TextRef.Dynamic(() => session.Simulation.CurrentTick / 20, s => TrainTexts.Name(s, trains, id) + " — " + TrainTexts.Status(s, session, id)),
                    () => (ui as ITrainUi)?.OpenTrain(id))
                {
                    HorizontalAlignment = Alignment.Start,
                });
            }

            if (!any)
            {
                panel.Add(new Label(TextRef.Of("line.noVehicles")) { Color = TextColor.Muted });
            }
        }));
        window.Content = content;
        return window;
    }

    /// <summary>Where the line's vehicles and trains are drawn on its stop strip (see <see cref="StopStrip.Position"/>).</summary>
    public static void LineMarkers(GameSession session, int lineId, List<int> scratch, List<float> into)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scratch);
        ArgumentNullException.ThrowIfNull(into);
        var vehicles = session.RoadVehicles!;
        var lines = vehicles.Lines;
        var count = lines.Exists(lineId) ? lines.StopCount(lineId) : 0;
        if (count == 0)
        {
            return;
        }

        vehicles.VehiclesOnLine(lineId, scratch);
        foreach (var id in scratch)
        {
            var slot = vehicles.SlotOf(id);
            var status = slot < 0 ? RoadVehicleStatus.InDepot : vehicles.StatusAt(slot);
            if (status is not (RoadVehicleStatus.InDepot or RoadVehicleStatus.Stopped or RoadVehicleStatus.GoingToDepot))
            {
                into.Add(StopStrip.Position(vehicles.StopIndexAt(slot), count, status == RoadVehicleStatus.Loading));
            }
        }

        if (session.Simulation.Trains is not { } trains)
        {
            return;
        }

        for (var i = 0; i < trains.Count; i++)
        {
            var train = trains.TrainAt(i);
            var status = TrainSystem.StatusOf(train);
            if (train.Line == lineId && status is not (TrainStatus.InDepot or TrainStatus.Stopped or TrainStatus.GoingToDepot))
            {
                into.Add(StopStrip.Position(train.StopIndex, count, status == TrainStatus.Loading));
            }
        }
    }

    /// <summary>Cargo on board a train against its total capacity, 0–1.</summary>
    private static float TrainLoad(TrainSystem trains, Train train, int cargoCount)
    {
        long amount = 0, capacity = 0;
        foreach (var part in train.Parts)
        {
            amount += part.Hold.Total;
        }

        for (var c = 0; c < cargoCount; c++)
        {
            capacity += trains.CapacityOf(train, c);
        }

        return InfoRows.Share(amount, capacity);
    }
}

/// <summary>The list of the company's lines (key F4): one button per line opens its window.</summary>
internal static class LinesWindow
{
    public const string Kind = "lines";

    public static UiWindow Create(IVehicleUi ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var vehicles = session.RoadVehicles!;
        var lines = vehicles.Lines;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Of("lines.title")) { Key = "lines", SingleInstance = true };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 4 };
        var onLine = new List<int>();
        content.Add(new RebuildingPanel(() => lines.Version ^ ((long)vehicles.Version << 32), panel =>
        {
            var any = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var lineId = lines.IdAt(i);
                if (lines.OwnerOf(lineId) != company)
                {
                    continue;
                }

                any = true;
                vehicles.VehiclesOnLine(lineId, onLine);
                var count = onLine.Count;
                panel.Add(new Button(TextRef.Dynamic(() => lines.Version, s => s.Format("lines.entry", ("name", VehicleTexts.LineName(s, lines, lineId)),
                    ("stops", lines.StopCount(lineId).ToString(CultureInfo.InvariantCulture)), ("vehicles", count.ToString(CultureInfo.InvariantCulture)))), () => ui.OpenLine(lineId))
                {
                    HorizontalAlignment = Alignment.Start,
                });
            }

            if (!any)
            {
                panel.Add(new Label(TextRef.Of("lines.none")) { Color = TextColor.Muted, Wrap = true, Width = 300 });
            }
        }));
        content.Add(new Button(TextRef.Of("lines.new"), () => ui.StartLineMode(-1, -1)) { Tooltip = TextRef.Of("lines.new.tooltip") });
        window.Content = content;
        return window;
    }
}
