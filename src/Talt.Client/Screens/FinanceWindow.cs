using System.Globalization;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Simulation;

namespace Talt.Client.Screens;

/// <summary>
/// The finance window (feature 011, US4, FR-025): income and expenses by category for the current year and the two
/// before it, with totals; cash, loan, limits, interest and company value; borrow and repay buttons; a bar chart of
/// the last 24 months. Figures update whenever money, the loan or the day changes.
/// </summary>
internal static class FinanceWindow
{
    public const string Kind = "finance";

    /// <summary>Columns of the table: two previous years and the current one.</summary>
    public const int Years = 3;

    /// <summary>Formats money in the player's currency.</summary>
    public delegate string MoneyText(long amount, Strings strings);

    /// <param name="requestInfiniteMoney">Asks the player to confirm infinite money (the screen's confirmation overlay).</param>
    public static UiWindow Create(GameSession session, MoneyText money, Action requestInfiniteMoney)
    {
        ArgumentNullException.ThrowIfNull(requestInfiniteMoney);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(money);
        var simulation = session.Simulation;
        var finance = simulation.Finance ?? throw new ArgumentException("The game has no finances.", nameof(session));
        var company = session.Construction.Player;
        var ledger = company.Ledger!;
        long Version() => company.Balance ^ (company.Loan << 21) ^ ((long)simulation.Date.DayNumber << 42) ^ (company.HasInfiniteMoney ? 1L << 62 : 0);

        var window = new UiWindow(Kind, TextRef.Of("finance.title")) { SingleInstance = true };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 6 };

        var table = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 14 });
        table.Add(new Label(TextRef.Dynamic(Version, s => RowNames(s, simulation))) { Wrap = true, Width = 190 });
        for (var column = Years - 1; column >= 0; column--)
        {
            var back = column;
            table.Add(new Label(TextRef.Dynamic(Version, s => Column(s, simulation, back, money))) { Wrap = true, Width = 110, TextAlignment = Alignment.End });
        }

        content.Add(new Label(TextRef.Dynamic(Version, s => Summary(s, finance, money)))
        {
            Wrap = true,
            Width = 540,
        });
        content.Add(new Label(TextRef.Dynamic(Version, s => company.HasInfiniteMoney ? s.Get("finance.infinite.on") : string.Empty))
        {
            Wrap = true,
            Width = 540,
            Color = TextColor.Accent,
        });
        content.Add(new Label(TextRef.Dynamic(Version, s => Bankruptcy(s, finance)))
        {
            Wrap = true,
            Width = 540,
            Color = TextColor.Negative,
        });

        var buttons = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        foreach (var (mode, key) in new[]
        {
            (LoanMode.Borrow, "finance.borrow"), (LoanMode.Repay, "finance.repay"),
            (LoanMode.BorrowMax, "finance.borrowMax"), (LoanMode.RepayMax, "finance.repayMax"),
        })
        {
            var button = buttons.Add(new Button(TextRef.Of(key), () => session.Submit(new ChangeLoanCommand(company.Id, mode)))
            {
                Tooltip = TextRef.Of(key + ".tooltip"),
            });
            button.Visible = !company.HasInfiniteMoney;
            InfiniteMoneyWatch(window, button, company, visibleWhenInfinite: false);
        }

        // Feature 011 (research R17): one way only, after a confirmation.
        var infinite = buttons.Add(new Button(TextRef.Of("finance.infinite"), requestInfiniteMoney)
        {
            Tooltip = TextRef.Of("finance.infinite.tooltip"),
            Visible = !company.HasInfiniteMoney,
        });
        InfiniteMoneyWatch(window, infinite, company, visibleWhenInfinite: false);

        HistoryCharts.AddCompany(content, simulation, company, money, Version);
        window.Content = new ScrollArea(content) { MaxHeight = 620 };
        return window;
    }

    /// <summary>Hides the loan and infinite-money buttons once the mode is on (checked when the window is laid out).</summary>
    private static void InfiniteMoneyWatch(UiWindow window, Button button, Company company, bool visibleWhenInfinite) =>
        button.VisibleWhen = () => company.HasInfiniteMoney == visibleWhenInfinite;

    /// <summary>Income and expenses of a chart slot (0 = 24 months ago, the last slot = this month).</summary>
    public static (long Positive, long Negative) ChartValues(Ledger ledger, int slot)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var back = Ledger.MonthsKept - 1 - slot;
        return (ledger.MonthIncome(back), ledger.MonthExpenses(back));
    }

    /// <summary>
    /// The rows shown, in order: one per cargo with income in any shown year, the expense categories, then totals.
    /// Returns ledger slots, or −1 for the income total, −2 for the expense total and −3 for the year's result.
    /// </summary>
    public static List<int> Rows(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var ledger = simulation.Construction!.Player.Ledger!;
        var rows = new List<int>();
        for (var cargo = 0; cargo < simulation.Content.CargoCount; cargo++)
        {
            var slot = Ledger.IncomeSlot(cargo);
            for (var back = 0; back < Years; back++)
            {
                if (ledger.Year(back, slot) != 0)
                {
                    rows.Add(slot);
                    break;
                }
            }
        }

        rows.Add(-1);
        foreach (var category in new[]
        {
            LedgerCategory.Construction, LedgerCategory.VehiclePurchase, LedgerCategory.RunningCosts,
            LedgerCategory.PropertyMaintenance, LedgerCategory.LoanInterest, LedgerCategory.Other,
        })
        {
            rows.Add(Ledger.Slot(category));
        }

        rows.Add(-2);
        rows.Add(-3);
        return rows;
    }

    public static string RowNames(Strings s, GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(s);
        var builder = new StringBuilder();
        builder.Append(s.Get("finance.category"));
        foreach (var row in Rows(simulation))
        {
            builder.Append('\n');
            builder.Append(row switch
            {
                -1 => s.Get("finance.totalIncome"),
                -2 => s.Get("finance.totalExpenses"),
                -3 => s.Get("finance.result"),
                >= Ledger.FixedSlots => s.Format("finance.income", ("cargo", s.Get(simulation.Content.Cargo(row - Ledger.FixedSlots).NameKey))),
                _ => s.Get($"finance.category.{((LedgerCategory)row).ToString().ToLowerInvariant()}"),
            });
        }

        return builder.ToString();
    }

    /// <summary>One year's column: the year, then one figure per row ("—" for years before the game).</summary>
    public static string Column(Strings s, GameSimulation simulation, int back, MoneyText money)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(money);
        var ledger = simulation.Construction!.Player.Ledger!;
        var builder = new StringBuilder();
        var exists = back < ledger.YearCount;
        builder.Append(exists ? ledger.YearNumber(back).ToString(CultureInfo.InvariantCulture) : string.Empty);
        foreach (var row in Rows(simulation))
        {
            builder.Append('\n');
            if (!exists)
            {
                builder.Append('—');
                continue;
            }

            var value = row switch
            {
                -1 => ledger.YearIncome(back),
                -2 => ledger.YearExpenses(back),
                -3 => ledger.YearTotal(back),
                _ => ledger.Year(back, row),
            };
            builder.Append(money(value, s));
        }

        return builder.ToString();
    }

    public static string Summary(Strings s, FinanceSystem finance, MoneyText money)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(finance);
        ArgumentNullException.ThrowIfNull(money);
        var report = finance.Report(Company.PlayerId);
        return s.Format("finance.summary",
            ("cash", money(report.Cash, s)),
            ("loan", money(report.Loan, s)),
            ("max", money(report.MaxLoan, s)),
            ("step", money(report.LoanStep, s)),
            ("interest", (report.InterestPerMille / 10.0).ToString("0.#", s.Culture)),
            ("value", money(report.Value, s)));
    }

    /// <summary>The bankruptcy line: empty while the company is not bankrupt.</summary>
    public static string Bankruptcy(Strings s, FinanceSystem finance)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(finance);
        var report = finance.Report(Company.PlayerId);
        if (report.BankruptMonths == 0)
        {
            return string.Empty;
        }

        return finance.Settings.Sandbox
            ? s.Format("finance.bankrupt.sandbox", ("months", report.BankruptMonths.ToString(CultureInfo.InvariantCulture)))
            : s.Format("finance.bankrupt", ("months", report.BankruptMonths.ToString(CultureInfo.InvariantCulture)),
                ("limit", report.GameOverMonths.ToString(CultureInfo.InvariantCulture)));
    }
}
