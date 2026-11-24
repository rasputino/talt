using System.Globalization;
using System.Text.RegularExpressions;
using Talt.Client.Localization;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;

namespace Talt.Client.Tests.Localization;

public partial class LocalizationCoverageTests
{
    private static readonly Lazy<GeneratedGame> SmallGame = new(() => GameSetup.Create(new NewGameOptions(ChunksPerSide: 4, VehicleCount: 50, Seed: 3)));

    /// <summary>Every screen and window this feature has, built for one language.</summary>
    private static List<Widget> AllScreens(UiContext ui)
    {
        var host = new FakeHost(ui);
        var session = new GameSession(SmallGame.Value, new SessionOptions(), fastForwardUnlocked: true);
        var game = new InGameScreen(host, session, new Talt.Client.Rendering.OrbitCamera(), _ => false);
        session.BuildInstances();
        game.OpenPauseMenu();
        var industryId = session.Simulation.Industries.Count > 0 ? session.Simulation.Industries.IdAt(0) : -1;
        var industryWindow = industryId >= 0
            ? ObjectInfoWindow.Create(new Talt.Client.Picking.PickResult(Talt.Client.Picking.PickKind.Industry, 3, 4, 10, Core.Voxels.VoxelMaterial.Air, -1, industryId, default, default, 1), session)
            : null;
        var screens = new List<Widget>
        {
            new MainMenuScreen(host).Root,
            new NewGameScreen(host, new DefaultNewGameOptionsSource()).Root,
            game.Root,
            ui.Overlay!,
            SettingsWindow.Create(host),
            KeyBindingsWindow.Create(host),
            SampleWindows.Create(1),
            ObjectInfoWindow.Create(new Talt.Client.Picking.PickResult(Talt.Client.Picking.PickKind.Tile, 3, 4, 10, Core.Voxels.VoxelMaterial.Grass, -1, -1, default, default, 1), session),
            ObjectInfoWindow.Create(new Talt.Client.Picking.PickResult(Talt.Client.Picking.PickKind.Road, 3, 4, 10, Core.Voxels.VoxelMaterial.Road, -1, -1, default, default, 1), session),
            ObjectInfoWindow.Create(new Talt.Client.Picking.PickResult(Talt.Client.Picking.PickKind.Vehicle, 3, 4, 0, Core.Voxels.VoxelMaterial.Air, 1, -1, default, default, 1), session),
        };
        if (industryWindow is not null)
        {
            screens.Add(industryWindow);
        }

        // A road window on a seeded town street (feature 007).
        var roads = session.Construction.Roads;
        for (var tile = 0; tile < roads.Width * roads.Depth; tile++)
        {
            if (roads.HasRoad(tile))
            {
                screens.Add(ObjectInfoWindow.Create(new Talt.Client.Picking.PickResult(Talt.Client.Picking.PickKind.Road, tile % roads.Width, tile / roads.Width, 10, Core.Voxels.VoxelMaterial.Grass, -1, -1, default, default, 1), session));
                break;
            }
        }

        return screens;
    }

    private static IEnumerable<TextRef> Texts(Widget widget)
    {
        if (widget.Tooltip is { } tooltip)
        {
            yield return tooltip;
        }

        switch (widget)
        {
            case Label label:
                yield return label.Text;
                break;
            case Button { Text: { } text }:
                yield return text;
                break;
            case Checkbox checkbox:
                yield return checkbox.Text;
                break;
            case UiWindow window:
                yield return window.Title;
                break;
            case Dropdown dropdown:
                foreach (var choice in dropdown.Choices)
                {
                    yield return choice.Text;
                }

                break;
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void EveryVisibleText_ResolvesToATranslation(string language)
    {
        // SC-003: all player text comes from the tables; a missing key would show as "[key]".
        var ui = UiTestKit.CreateContext(language);
        var table = Strings.LoadTable(language);
        var count = 0;
        foreach (var root in AllScreens(ui))
        {
            foreach (var widget in UiTestKit.Walk(root))
            {
                foreach (var text in Texts(widget))
                {
                    var resolved = text.Resolve(ui);
                    Assert.False(resolved.StartsWith('[') && resolved.EndsWith(']'), $"{language}: '{resolved}'");
                    Assert.False(string.IsNullOrWhiteSpace(resolved), $"{language}: empty text in {widget.GetType().Name}");
                    if (text.Key is { } key)
                    {
                        Assert.True(table.ContainsKey(key), $"{language}: missing key {key}");
                    }

                    count++;
                }
            }
        }

        Assert.True(count > 150, $"only {count} texts checked");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void FixedWidthLabels_FitTheirText(string language)
    {
        // Edge case: long Spanish texts must not overflow fixed-width labels in the default layout.
        var ui = UiTestKit.CreateContext(language);
        foreach (var root in AllScreens(ui))
        {
            foreach (var widget in UiTestKit.Walk(root))
            {
                if (widget is Label { Width: { } width, Wrap: false } label)
                {
                    var measured = TextLayout.Measure(ui.Font, label.Text.Resolve(ui), ui.FontSize(label.Size));
                    Assert.True(measured <= width, $"{language}: '{label.Text.Resolve(ui)}' is {measured:F0} px in a {width} px label");
                }
            }
        }
    }

    [Fact]
    public void EveryKeyUsedInCode_Exists()
    {
        // Literal keys passed to TextRef.Of / Get / Format / Plural in the client sources.
        var root = FindRepositoryRoot();
        var english = Strings.LoadTable("en");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Talt.Client"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in KeyPattern().Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (!english.ContainsKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void CurrenciesActionsAndMaterials_HaveTexts()
    {
        var english = Strings.LoadTable("en");
        foreach (var currency in MoneyFormatter.Load().Currencies)
        {
            Assert.Contains(currency.NameKey, english.Keys);
        }

        foreach (var action in Talt.Client.Input.KeyBindings.LoadDefinitions())
        {
            Assert.Contains(action.LabelKey, english.Keys);
            Assert.Contains($"bindings.category.{action.Category}", english.Keys);
        }

        foreach (var material in Enum.GetNames<Core.Voxels.VoxelMaterial>())
        {
            Assert.Contains($"material.{material}", english.Keys);
        }
    }

    [Fact]
    public void Plural_ChoosesOneOrOther()
    {
        Assert.Equal("1 vehicle", Strings.Load("en").Plural("count.vehicles", 1));
        Assert.Equal("2 vehicles", Strings.Load("en").Plural("count.vehicles", 2));
        Assert.Equal("1 vehículo", Strings.Load("es").Plural("count.vehicles", 1));
        Assert.Equal("0 vehículos", Strings.Load("es").Plural("count.vehicles", 0));
    }

    [Fact]
    public void LanguageSwitch_ChangesBoundTextsAtOnce()
    {
        var ui = UiTestKit.CreateContext("en");
        var menu = new MainMenuScreen(new FakeHost(ui));
        Assert.Equal("New game", menu.Buttons[0].Text!.Resolve(ui));
        ui.SetStrings(Strings.Load("es"));
        Assert.Equal("Nueva partida", menu.Buttons[0].Text!.Resolve(ui));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Talt.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("""(?:TextRef\.Of|\.Get|\.Format|\.Plural)\("([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9-]+)+)"[,)]""")]
    private static partial Regex KeyPattern();
}

public class MoneyFormatterTests
{
    [Fact]
    public void DefaultCurrency_FollowsTheLanguage()
    {
        var money = MoneyFormatter.Load();
        Assert.Equal("USD", money.Resolve(null, "en").Code);
        Assert.Equal("EUR", money.Resolve(null, "es").Code);
        Assert.Equal("JPY", money.Resolve("JPY", "es").Code);
        Assert.Equal("EUR", money.Resolve("XXX", "es").Code);
    }

    [Fact]
    public void Amounts_AreConvertedAndFormattedPerCulture()
    {
        var money = MoneyFormatter.Load();
        Assert.Equal("$500,000", money.Format(500_000, money.Find("USD")!, CultureInfo.GetCultureInfo("en")));
        var euros = money.Format(500_000, money.Find("EUR")!, CultureInfo.GetCultureInfo("es"));
        Assert.Equal("460.000 €", euros.Replace(' ', ' '));
        Assert.Equal("¥75,000,000", money.Format(500_000, money.Find("JPY")!, CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void Data_HasTheMajorCurrencies()
    {
        var codes = MoneyFormatter.Load().Currencies.Select(c => c.Code).ToHashSet();
        foreach (var code in new[] { "USD", "EUR", "GBP", "JPY", "CNY", "INR", "CAD", "AUD", "CHF", "MXN", "BRL", "KRW", "SEK", "NOK", "PLN", "RUB", "TRY", "ZAR", "ARS", "CLP", "COP" })
        {
            Assert.Contains(code, codes);
        }
    }

    [Fact]
    public void EveryCurrencySymbol_IsInTheFont()
    {
        foreach (var currency in MoneyFormatter.Load().Currencies)
        {
            Assert.All(currency.Symbol, c => Assert.True(UiTestKit.Font.Contains(c), $"{currency.Code}: '{c}'"));
        }
    }
}
