using System.Globalization;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Finance;

namespace Talt.Client.Tests.Screens;

/// <summary>Feature 011 client parts: finance window texts and chart data, HUD, floating texts, game over, options.</summary>
public class FinanceClientTests
{
    private static (InGameScreen Screen, GameSession Session, FakeHost Host) Create(FinanceSettings? finance = null)
    {
        var ui = UiTestKit.CreateContext();
        var host = new FakeHost(ui);
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 4, VehicleCount: 0, Seed: 5, Finance: finance));
        var session = new GameSession(game, new SessionOptions(), fastForwardUnlocked: false);
        var screen = new InGameScreen(host, session, new Talt.Client.Rendering.OrbitCamera(), _ => false);
        ui.Screen = screen.Root;
        screen.ApplySettings(host.Settings);
        return (screen, session, host);
    }

    private static string Money(long amount, Talt.Client.Localization.Strings s) => amount.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void Window_ShowsTheLedgerByCategory_WithTotals()
    {
        var (_, session, host) = Create();
        var simulation = session.Simulation;
        var company = session.Construction.Player;
        company.Book(LedgerCategory.Construction, -1_000);
        company.Book(LedgerCategory.Income, 2_500, simulation.Content.CargoIndex("coal"));
        var s = host.Ui.Strings;

        var rows = FinanceWindow.Rows(simulation);
        Assert.Equal(Talt.Core.Finance.Ledger.IncomeSlot(simulation.Content.CargoIndex("coal")), rows[0]); // Only cargo with income.
        Assert.Equal(-1, rows[1]);
        Assert.Equal([-2, -3], rows[^2..]);

        var names = FinanceWindow.RowNames(s, simulation).Split('\n');
        var current = FinanceWindow.Column(s, simulation, 0, Money).Split('\n');
        var earlier = FinanceWindow.Column(s, simulation, 1, Money).Split('\n');
        Assert.Equal(names.Length, current.Length);
        Assert.Equal(simulation.Date.Year.ToString(CultureInfo.InvariantCulture), current[0]);
        Assert.Equal("2500", current[1]);
        Assert.Equal("2500", current[2]); // Total income.
        Assert.Equal("-1000", current[names.Length - 2]); // Total expenses.
        Assert.Equal("1500", current[^1]); // Result.
        Assert.All(earlier.Skip(1), cell => Assert.Equal("—", cell)); // No year before the game.
    }

    [Fact]
    public void Summary_AndBankruptcyLine_FollowTheReport()
    {
        var (_, session, host) = Create();
        var finance = session.Simulation.Finance!;
        var s = host.Ui.Strings;
        Assert.Contains("500000", FinanceWindow.Summary(s, finance, Money), StringComparison.Ordinal);
        Assert.Equal(string.Empty, FinanceWindow.Bankruptcy(s, finance));
        session.Construction.Player.BankruptMonths = 2;
        Assert.Contains("2", FinanceWindow.Bankruptcy(s, finance), StringComparison.Ordinal);
    }

    [Fact]
    public void Chart_ShowsTheLast24Months_OldestFirst()
    {
        var (_, session, _) = Create();
        var ledger = session.Construction.Player.Ledger!;
        session.Construction.Player.Book(LedgerCategory.RunningCosts, -40);
        session.Construction.Player.Book(LedgerCategory.Income, 90, 0);
        Assert.Equal((90, -40), FinanceWindow.ChartValues(ledger, Talt.Core.Finance.Ledger.MonthsKept - 1));
        Assert.Equal((0, 0), FinanceWindow.ChartValues(ledger, 0));
        var chart = new BarChart(Talt.Core.Finance.Ledger.MonthsKept, slot => FinanceWindow.ChartValues(ledger, slot));
        Assert.Equal(90, chart.Scale());
    }

    [Fact]
    public void HudCoin_AndKey_OpenTheFinanceWindow_AndLoanButtonsSubmitCommands()
    {
        var (screen, session, host) = Create();
        screen.HudButtons.First(b => b.ShortcutAction == "finance").Click();
        Assert.Equal(1, host.Ui.Windows.Count);
        screen.HandleAction("finance", false);
        Assert.Equal(1, host.Ui.Windows.Count); // Single instance.

        var window = host.Ui.Windows.Front!;
        Assert.Equal(FinanceWindow.Kind, window.Kind);
        var borrow = Find<Button>(window, b => b.Text is { } t && t.Resolve(host.Ui) == host.Ui.Strings.Get("finance.borrow"));
        borrow.Click();
        session.Simulation.ProcessPendingCommands();
        Assert.Equal(10_000, session.Construction.Player.Loan);
    }

    [Fact]
    public void Income_ShowsAFloatingText_AndGameOverShowsTheScreen()
    {
        var (screen, session, host) = Create();
        var finance = session.Simulation.Finance!;
        finance.Events.Clear();
        // An income event as a paid delivery makes it (the station's tile).
        finance.Events.Add(new MoneyEvent(Company.PlayerId, 1234, 10, 10, MoneyEventKind.Income));
        screen.Update(0.016);
        Assert.Equal(1, screen.MoneyTexts.Count);
        Assert.StartsWith("+", screen.MoneyTexts.TextAt(0), StringComparison.Ordinal);

        finance.Events.Add(new MoneyEvent(Company.PlayerId, -1, -1, -1, MoneyEventKind.GameOver));
        screen.Update(0.016);
        Assert.True(screen.IsGameOverShown);
        Assert.True(session.Runner.IsPaused);
        screen.OpenPauseMenu();
        Assert.False(screen.IsPauseMenuOpen); // The game-over screen stays.
    }

    [Fact]
    public void InfiniteMoney_NeedsConfirmation_ThenTheHudShowsIt()
    {
        var (screen, session, host) = Create();
        var company = session.Construction.Player;
        screen.RequestInfiniteMoney();
        Assert.True(screen.IsInfiniteMoneyConfirmationOpen);
        screen.Back(); // Esc cancels.
        Assert.False(screen.IsInfiniteMoneyConfirmationOpen);
        session.Simulation.ProcessPendingCommands();
        Assert.False(company.HasInfiniteMoney);

        screen.OpenFinance();
        var window = host.Ui.Windows.Front!;
        Find<Button>(window, b => b.Text is { } t && t.Resolve(host.Ui) == host.Ui.Strings.Get("finance.infinite")).Click();
        Assert.True(screen.IsInfiniteMoneyConfirmationOpen);
        screen.ConfirmInfiniteMoney();
        session.Simulation.ProcessPendingCommands();
        Assert.True(company.HasInfiniteMoney);
        Assert.False(screen.IsInfiniteMoneyConfirmationOpen);

        screen.RequestInfiniteMoney(); // Nothing to ask any more.
        Assert.False(screen.IsInfiniteMoneyConfirmationOpen);
        var money = Find<Label>(screen.Root, l => l.CurrentText(host.Ui) == host.Ui.Strings.Get("hud.money.infinite"));
        Assert.Equal(TextColor.Accent, money.ColorOf!());
    }

    [Fact]
    public void NewGameOptions_CarrySandboxAndInflation()
    {
        var source = new DefaultNewGameOptionsSource();
        var defaults = source.Create(new Dictionary<string, int>());
        Assert.Null(defaults.Finance);
        var chosen = source.Create(new Dictionary<string, int> { [DefaultNewGameOptionsSource.Sandbox] = 1, [DefaultNewGameOptionsSource.Inflation] = 1 });
        Assert.Equal(new FinanceSettings(Sandbox: true, Inflation: true), chosen.Finance);
        Assert.Equal(chosen.Finance, chosen.ToScenario().Finance);
        var infinite = source.Create(new Dictionary<string, int> { [DefaultNewGameOptionsSource.InfiniteMoney] = 1 });
        Assert.Equal(new FinanceSettings(InfiniteMoney: true), infinite.Finance);
    }

    private static T Find<T>(Widget root, Func<T, bool> match)
        where T : Widget
    {
        if (root is T typed && match(typed))
        {
            return typed;
        }

        for (var i = 0; i < root.ChildCount; i++)
        {
            if (TryFind(root.GetChild(i), match, out var found))
            {
                return found;
            }
        }

        throw new InvalidOperationException("Widget not found.");
    }

    private static bool TryFind<T>(Widget root, Func<T, bool> match, out T found)
        where T : Widget
    {
        if (root is T typed && match(typed))
        {
            found = typed;
            return true;
        }

        for (var i = 0; i < root.ChildCount; i++)
        {
            if (TryFind(root.GetChild(i), match, out found))
            {
                return true;
            }
        }

        found = null!;
        return false;
    }
}
