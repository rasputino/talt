using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Finance;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>Period selectors and charts shared by the company, town and station windows.</summary>
internal static class HistoryCharts
{
    public static void AddTown(StackPanel content, TownRegistry towns, int town, Func<long> version)
    {
        var controls = Header(content);
        var metric = 0;
        controls.Add(new Dropdown(Choices("passengersGenerated", "passengersTransported", "mailGenerated", "mailTransported", "rating"),
            () => metric, i => metric = i));

        long? Value(int back)
        {
            if (!towns.History.TryGet(back, town, out var month))
            {
                return null;
            }

            return metric switch
            {
                0 => month.PassengersGenerated,
                1 => month.PassengersTransported,
                2 => month.MailGenerated,
                3 => month.MailTransported,
                _ => month.Rating,
            };
        }

        AddChart(content, MonthlyHistory<TownMonth>.MonthsKept, Value, towns.History.Date, () => version() ^ ((long)metric << 40));
    }

    public static void AddStation(StackPanel content, StationRegistry stations, int station, Func<long> version)
    {
        var controls = Header(content);
        var cargo = 0;
        var metric = 0;
        controls.Add(CargoSelector(stations.Content, () => cargo, i => cargo = i));
        controls.Add(new Dropdown(Choices("waiting", "pickedUp", "delivered", "rating"), () => metric, i => metric = i));

        long? Value(int back)
        {
            if (!stations.TryGetMonth(station, back, cargo, out var month))
            {
                return null;
            }

            return metric switch { 0 => month.Waiting, 1 => month.PickedUp, 2 => month.Delivered, _ => month.Rating >= 0 ? month.Rating : null };
        }

        AddChart(content, MonthlyHistory<StationCargoMonth>.MonthsKept, Value, back => stations.HistoryDate(station, back),
            () => version() ^ ((long)cargo << 48) ^ ((long)metric << 40));
    }

    public static void AddCompany(StackPanel content, GameSimulation simulation, Company company, FinanceWindow.MoneyText money, Func<long> version)
    {
        var ledger = company.Ledger!;
        var controls = Header(content);
        var annual = false;
        var page = 0;
        var metric = 0;
        var cargo = 0;
        controls.Add(new Dropdown(Choices("monthly", "annual"), () => annual ? 1 : 0, i => { annual = i == 1; page = 0; }));
        controls.Add(new Dropdown(Choices("incomeExpenses", "expenses", "delivered", "value"), () => metric, i => metric = i));
        var cargoSelector = controls.Add(CargoSelector(simulation.Content, () => cargo, i => cargo = i));
        cargoSelector.VisibleWhen = () => metric == 2;

        const int slots = Ledger.MonthsKept;
        var pages = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6, VisibleWhen = () => annual && ledger.YearCount > slots });
        pages.Add(new Button(TextRef.Of("history.older"), () => page = Math.Min((ledger.YearCount - 1) / slots, page + 1)));
        pages.Add(new Button(TextRef.Of("history.newer"), () => page = Math.Max(0, page - 1)));
        var valueVersion = long.MinValue;
        long currentValue = 0;
        long? Value(int back)
        {
            if (annual)
            {
                back += page * slots;
            }

            if (back >= (annual ? ledger.YearCount : ledger.MonthCount))
            {
                return null;
            }

            if (metric == 3 && back == 0)
            {
                var now = version();
                if (valueVersion != now)
                {
                    valueVersion = now;
                    currentValue = Money.Add(Money.Add(company.Balance, -company.Loan), simulation.Finance!.PropertyValue(company.Id));
                }

                return currentValue;
            }

            return metric switch
            {
                0 => annual ? ledger.YearIncome(back) : ledger.MonthIncome(back),
                1 => annual ? ledger.YearExpenses(back) : ledger.MonthExpenses(back),
                2 => annual ? ledger.YearDelivered(back, cargo) : ledger.MonthDelivered(back, cargo),
                _ => annual ? ledger.YearValue(back) : ledger.MonthValue(back),
            };
        }

        long Expenses(int back) => metric != 0 ? 0 : annual
            ? ledger.YearExpenses(back + (page * slots))
            : FinanceWindow.ChartValues(ledger, slots - 1 - back).Negative;

        AddChart(content, slots, Value, back => annual ? (ledger.YearNumber(back + (page * slots)), 0) : ledger.MonthDate(back),
            () => version() ^ (ledger.Version << 7) ^ ((long)cargo << 48) ^ ((long)metric << 40) ^ ((long)page << 24) ^ (annual ? long.MinValue : 0),
            (v, back, s) => metric switch
            {
                0 => s.Format("history.moneyPair", ("income", money(v, s)), ("expenses", money(Expenses(back), s))),
                2 => v.ToString("N0", s.Culture),
                _ => money(v, s),
            }, Expenses);
    }

    private static StackPanel Header(StackPanel content)
    {
        content.Add(InfoRows.Section("history.heading"));
        return content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
    }

    private static DropdownChoice[] Choices(params string[] names) =>
        names.Select((key, index) => new DropdownChoice(TextRef.Of("history." + key), index)).ToArray();

    private static Dropdown CargoSelector(GameContent content, Func<int> selected, Action<int> select)
    {
        var choices = new DropdownChoice[content.CargoCount];
        for (var i = 0; i < choices.Length; i++)
        {
            choices[i] = new DropdownChoice(TextRef.Of(content.Cargo(i).NameKey), i);
        }

        return new Dropdown(choices, selected, select);
    }

    private static void AddChart(StackPanel content, int slots, Func<int, long?> value, Func<int, (int Year, int Month)> date,
        Func<long> version, Func<long, int, Strings, string>? format = null, Func<int, long>? negativeSeries = null)
    {
        var selected = 0;
        content.Add(new BarChart(slots, slot =>
        {
            var v = value(slots - 1 - slot).GetValueOrDefault();
            return (Math.Max(v, 0), Math.Min(v, negativeSeries?.Invoke(slots - 1 - slot) ?? 0));
        })
        {
            Width = slots == Ledger.MonthsKept ? 540 : 350,
            Height = 68,
            SelectSlot = slot => selected = slots - 1 - slot,
            Tooltip = TextRef.Of("history.inspect"),
        });
        content.Add(new Label(TextRef.Dynamic(() => version() ^ ((long)selected << 32), s =>
        {
            var (year, month) = date(selected);
            var v = value(selected);
            if (year == 0 || !v.HasValue)
            {
                return s.Get("history.unavailable");
            }

            var label = month == 0 ? year.ToString(s.Culture) : new DateOnly(year, month, 1).ToString("Y", s.Culture);
            return s.Format("history.point", ("date", label), ("value", format?.Invoke(v.Value, selected, s) ?? v.Value.ToString("N0", s.Culture)));
        })) { Color = TextColor.Muted });
    }
}
