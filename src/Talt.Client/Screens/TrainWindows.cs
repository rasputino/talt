using System.Globalization;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Rails;
using Talt.Core.Vehicles;

namespace Talt.Client.Screens;

/// <summary>What the train windows ask the game screen to do (feature 015).</summary>
internal interface ITrainUi : IVehicleUi
{
    void OpenTrain(int trainId);

    void OpenRailDepot(int depotId);
}

/// <summary>Texts shared by the train windows.</summary>
internal static class TrainTexts
{
    public static string Name(Strings s, TrainSystem trains, int trainId)
    {
        var train = trains.Find(trainId);
        return train is null ? s.Get("vehicle.gone")
            : s.Format("train.name", ("model", train.Parts.Count > 0 ? s.Get(trains.ModelOf(train.Parts[0]).NameKey) : string.Empty),
                ("number", trainId.ToString(CultureInfo.InvariantCulture)));
    }

    public static string Status(Strings s, GameSession session, int trainId)
    {
        var trains = session.Simulation.Trains!;
        var train = trains.Find(trainId);
        if (train is null)
        {
            return s.Get("vehicle.gone");
        }

        var target = train.TargetKind switch
        {
            RouteGoalKind.Station => VehicleTexts.StationName(s, session, train.TargetId),
            RouteGoalKind.Depot or RouteGoalKind.AnyDepot => s.Get("vehicle.target.depot"),
            _ => s.Get("vehicle.target.none"),
        };
        return s.Format($"train.status.{TrainSystem.StatusOf(train).ToString().ToLowerInvariant()}", ("target", target));
    }

    /// <summary>Consist, load, speed, age, reliability and money.</summary>
    public static string Details(Strings s, ITrainUi ui, int trainId)
    {
        var session = ui.Session;
        var trains = session.Simulation.Trains!;
        var train = trains.Find(trainId);
        if (train is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append(s.Format("train.consist", ("parts", train.Parts.Count.ToString(CultureInfo.InvariantCulture)),
            ("length", (train.Length * 10 / Talt.Core.Determinism.FixedMath.UnitsPerTile / 10.0).ToString("0.0", s.Culture)),
            ("mass", train.MassTonnes.ToString("N0", s.Culture)), ("power", train.PowerKw.ToString("N0", s.Culture))));
        builder.Append('\n');
        for (var c = 0; c < session.Simulation.Content.CargoCount; c++)
        {
            var capacity = trains.CapacityOf(train, c);
            if (capacity == 0)
            {
                continue;
            }

            var amount = 0;
            foreach (var part in train.Parts)
            {
                amount += part.Cargo == c ? part.Hold.Total : 0;
            }

            builder.Append(s.Format("vehicle.load", ("cargo", VehicleTexts.CargoName(s, session, c)),
                ("amount", amount.ToString("N0", s.Culture)), ("capacity", capacity.ToString("N0", s.Culture))));
            builder.Append('\n');
        }

        builder.Append(s.Format("vehicle.speed", ("speed", trains.SpeedKmh(train).ToString(CultureInfo.InvariantCulture)),
            ("max", trains.Rules.KmhFromSpeed(train.MaxSpeed).ToString(CultureInfo.InvariantCulture))));
        builder.Append('\n');
        builder.Append(s.Format("vehicle.reliability", ("percent", (train.Reliability / 10).ToString(CultureInfo.InvariantCulture))));
        builder.Append('\n');
        builder.Append(s.Format("vehicle.money", ("value", ui.Money(trains.ValueOf(train), s)),
            ("thisYear", ui.Money(train.ProfitThisYear, s)), ("lastYear", ui.Money(train.ProfitLastYear, s))));
        return builder.ToString();
    }
}

/// <summary>
/// The train window (FR-029): state, consist, load, speed, reliability, money; start or stop, send to the depot, clone,
/// sell (in a depot), line choice and service interval. Every action is a recorded command.
/// </summary>
internal static class TrainWindow
{
    public const string Kind = "train";

    public static UiWindow Create(ITrainUi ui, int trainId)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var trains = session.Simulation.Trains!;
        var lines = trains.Lines;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Dynamic(() => trains.Version, s => TrainTexts.Name(s, trains, trainId))) { Key = $"train:{trainId}" };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 4 };
        long Tick() => session.Simulation.CurrentTick;
        Train? Find() => trains.Find(trainId);

        content.Add(new Label(TextRef.Dynamic(Tick, s => TrainTexts.Status(s, session, trainId))) { Wrap = true, Width = 360 });
        content.Add(new Label(TextRef.Dynamic(Tick, s => TrainTexts.Details(s, ui, trainId))) { Wrap = true, Width = 360 });
        content.Add(new RebuildingPanel(() => lines.Version ^ ((long)trains.Version << 32), panel =>
        {
            if (Find() is null)
            {
                return;
            }

            var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
            row.Add(new Label(TextRef.Of("vehicle.line")) { Margin = new Thickness(0, 4, 0, 0) });
            var choices = new List<DropdownChoice> { new(TextRef.Of("line.none"), -1) };
            for (var i = 0; i < lines.Count; i++)
            {
                var lineId = lines.IdAt(i);
                if (lines.OwnerOf(lineId) == company)
                {
                    choices.Add(new DropdownChoice(TextRef.Dynamic(() => lines.Version, s => VehicleTexts.LineName(s, lines, lineId)), lineId));
                }
            }

            row.Add(new Dropdown(choices, () => Math.Max(0, choices.FindIndex(c => (int)c.Value == (Find()?.Line ?? -1))),
                index => session.Submit(new AssignTrainLineCommand(company, trainId, (int)choices[index].Value))));
            row.Add(new Button(TextRef.Of("vehicle.newLine"), () => ui.StartLineMode(-1, -1)) { Tooltip = TextRef.Of("train.newLine.tooltip") });
            row.Add(new Button(TextRef.Of("vehicle.openLine"), () =>
            {
                if (Find() is { Line: >= 0 } train)
                {
                    ui.OpenLine(train.Line);
                }
            })
            { VisibleWhen = () => Find() is { Line: >= 0 } });
        }));

        var buttons = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        buttons.Add(new Button(TextRef.Dynamic(Tick, s => s.Get(Find() is { } t && (t.Flags & TrainBits.Started) != 0 ? "vehicle.stop" : "vehicle.start")), () =>
        {
            if (Find() is { } train)
            {
                session.Submit(new StartTrainCommand(company, trainId, (train.Flags & TrainBits.Started) == 0));
            }
        }));
        buttons.Add(new Button(TextRef.Of("vehicle.sendToDepot"), () => session.Submit(new SendTrainToDepotCommand(company, trainId)))
        {
            IsSelected = () => Find() is { } t && (t.Orders & VehicleOrders.SendToDepot) != 0,
            Tooltip = TextRef.Of("vehicle.sendToDepot.tooltip"),
        });
        buttons.Add(new Button(TextRef.Of("vehicle.clone"), () => session.Submit(new CloneTrainCommand(company, trainId))) { Tooltip = TextRef.Of("vehicle.clone.tooltip") });
        buttons.Add(new Button(TextRef.Of("train.openDepot"), () =>
        {
            if (Find() is { } t)
            {
                ui.OpenRailDepot(t.Depot >= 0 ? t.Depot : t.HomeDepot);
            }
        })
        { VisibleWhen = () => Find() is { Mode: TrainMode.InDepot } });

        var service = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        service.Add(new Label(TextRef.Dynamic(Tick, s => Find() is not { } t ? string.Empty
            : t.ServiceInterval == 0 ? s.Get("vehicle.service.never")
            : s.Format("vehicle.service", ("days", t.ServiceInterval.ToString(CultureInfo.InvariantCulture))))) { MinWidth = 200, Margin = new Thickness(0, 4, 0, 0) });
        service.Add(new Button(TextRef.Of("vehicle.service.less"), () => ChangeService(session, company, trainId, -30)));
        service.Add(new Button(TextRef.Of("vehicle.service.more"), () => ChangeService(session, company, trainId, 30)));
        window.Content = content;
        return window;
    }

    private static void ChangeService(GameSession session, int company, int trainId, int delta)
    {
        if (session.Simulation.Trains!.Find(trainId) is { } train)
        {
            session.Submit(new SetTrainServiceIntervalCommand(company, trainId, Math.Clamp(train.ServiceInterval + delta, 0, 990)));
        }
    }
}

/// <summary>
/// The rail depot window (FR-011, FR-029, clarification Q3): the trains inside with their parts (remove a part, sell, start,
/// open), and the locomotives and wagons offered this year with their figures; a wagon is added to the selected train, a
/// locomotive starts a new train (or joins the selected one).
/// </summary>
internal static class RailDepotWindow
{
    public const string Kind = "railDepot";

    public static UiWindow Create(ITrainUi ui, int depotId)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var trains = session.Simulation.Trains!;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Of("railDepot.title")) { Key = $"railDepot:{depotId}" };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 6 };
        var selected = -1;

        content.Add(new Label(TextRef.Of("depot.inside"), TextSize.Title));
        content.Add(new RebuildingPanel(() => trains.Version ^ ((long)selected << 40) ^ (session.Simulation.CurrentTick / 20 << 20), panel =>
        {
            var any = false;
            for (var slot = 0; slot < trains.Count; slot++)
            {
                var train = trains.TrainAt(slot);
                if (train.Depot != depotId || train.Owner != company)
                {
                    continue;
                }

                any = true;
                var id = train.Id;
                if (selected < 0 || trains.Find(selected) is not { Mode: TrainMode.InDepot })
                {
                    selected = id;
                }

                var row = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
                row.Add(new Button(TextRef.Dynamic(() => trains.Version, s => TrainTexts.Name(s, trains, id)), () => selected = id)
                {
                    MinWidth = 180,
                    IsSelected = () => selected == id,
                });
                row.Add(new Button(TextRef.Of("vehicle.open"), () => ui.OpenTrain(id)));
                row.Add(new Button(TextRef.Of("vehicle.start"), () => session.Submit(new StartTrainCommand(company, id, true))));
                row.Add(new Button(TextRef.Of("vehicle.sell"), () => session.Submit(new SellRailVehicleCommand(company, id))));
                var parts = panel.Add(new StackPanel(Orientation.Horizontal) { Spacing = 2, Margin = new Thickness(16, 0, 0, 0) });
                for (var p = 0; p < train.Parts.Count; p++)
                {
                    var index = p;
                    var model = trains.ModelOf(train.Parts[p]);
                    parts.Add(new Button(TextRef.Dynamic(() => 0, s => s.Get(model.NameKey)), () => session.Submit(new SellRailVehicleCommand(company, id, index)))
                    {
                        Tooltip = TextRef.Of("railDepot.removePart.tooltip"),
                    });
                }
            }

            if (!any)
            {
                selected = -1;
                panel.Add(new Label(TextRef.Of("depot.empty")) { Color = TextColor.Muted });
            }
        }));

        content.Add(new Label(TextRef.Of("depot.buy"), TextSize.Title) { Margin = new Thickness(0, 6, 0, 0) });
        content.Add(new RebuildingPanel(() => session.Simulation.Date.Year, panel =>
        {
            var rules = trains.Rules;
            for (var m = 0; m < rules.Models.Count; m++)
            {
                var model = rules.Models[m];
                if (!model.AvailableIn(session.Simulation.Date.Year))
                {
                    continue;
                }

                var row = panel.Add(new StackPanel(Orientation.Vertical) { Spacing = 2 });
                row.Add(new Label(TextRef.Dynamic(() => 0, s => s.Format(model.Kind == RollingStockKind.Locomotive ? "railDepot.locomotive" : "railDepot.wagon",
                    ("name", s.Get(model.NameKey)),
                    ("price", ui.Money(session.Simulation.Finance?.Inflate(model.Price) ?? model.Price, s)),
                    ("capacity", model.Capacity.ToString(CultureInfo.InvariantCulture)),
                    ("speed", model.SpeedKmh.ToString(CultureInfo.InvariantCulture)),
                    ("power", model.PowerKw.ToString("N0", s.Culture)),
                    ("mass", model.MassTonnes.ToString(CultureInfo.InvariantCulture)),
                    ("running", ui.Money(model.RunningCostPerYear, s)),
                    ("reliability", (model.Reliability / 10).ToString(CultureInfo.InvariantCulture))))) { Wrap = true, Width = 380 });
                var cargos = new List<DropdownChoice>();
                for (var c = 0; c < session.Simulation.Content.CargoCount; c++)
                {
                    if (model.Carries(c))
                    {
                        var cargo = c;
                        cargos.Add(new DropdownChoice(TextRef.Dynamic(() => 0, s => VehicleTexts.CargoName(s, session, cargo)), session.Simulation.Content.Cargo(c).Id));
                    }
                }

                var chosen = 0;
                var buy = row.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
                if (cargos.Count > 1)
                {
                    buy.Add(new Dropdown(cargos, () => chosen, index => chosen = index));
                }

                string Cargo() => cargos.Count == 0 ? string.Empty : (string)cargos[Math.Min(chosen, cargos.Count - 1)].Value;
                if (model.Kind == RollingStockKind.Locomotive)
                {
                    buy.Add(new Button(TextRef.Of("railDepot.newTrain"), () => session.Submit(new BuyRailVehicleCommand(company, depotId, model.Id, Cargo()))));
                }

                buy.Add(new Button(TextRef.Of("railDepot.addToTrain"), () =>
                {
                    if (selected >= 0)
                    {
                        session.Submit(new BuyRailVehicleCommand(company, depotId, model.Id, Cargo(), selected));
                    }
                })
                { VisibleWhen = () => selected >= 0 });
                if (model.Kind == RollingStockKind.Wagon)
                {
                    buy.Add(new Button(TextRef.Of("railDepot.addFive"), () =>
                    {
                        for (var i = 0; i < 5 && selected >= 0; i++)
                        {
                            session.Submit(new BuyRailVehicleCommand(company, depotId, model.Id, Cargo(), selected));
                        }
                    })
                    { VisibleWhen = () => selected >= 0 });
                }
            }
        }));
        window.Content = content;
        return window;
    }
}

/// <summary>The list of the company's trains: one button per train opens its window.</summary>
internal static class TrainsWindow
{
    public const string Kind = "trains";

    public static UiWindow Create(ITrainUi ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var session = ui.Session;
        var trains = session.Simulation.Trains!;
        var company = session.Construction.Player.Id;
        var window = new UiWindow(Kind, TextRef.Of("trains.title")) { Key = "trains", SingleInstance = true };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 4 };
        content.Add(new RebuildingPanel(() => trains.Version, panel =>
        {
            var any = false;
            for (var slot = 0; slot < trains.Count && slot < 200; slot++)
            {
                var train = trains.TrainAt(slot);
                if (train.Owner != company)
                {
                    continue;
                }

                any = true;
                var id = train.Id;
                panel.Add(new Button(TextRef.Dynamic(() => session.Simulation.CurrentTick / 20, s => TrainTexts.Name(s, trains, id) + " — " + TrainTexts.Status(s, session, id)),
                    () => ui.OpenTrain(id))
                {
                    HorizontalAlignment = Alignment.Start,
                });
            }

            if (!any)
            {
                panel.Add(new Label(TextRef.Of("trains.none")) { Color = TextColor.Muted, Wrap = true, Width = 300 });
            }
        }));
        window.Content = content;
        return window;
    }
}
