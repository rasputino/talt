using System.Numerics;
using Talt.Client.Audio;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Screens;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;

namespace Talt.Client.Tests.Ui;

/// <summary>Shared interface objects for tests (the font atlas is generated once per test run).</summary>
internal static class UiTestKit
{
    private static readonly Lazy<(UiAtlas Atlas, FontAtlas Font, Icons Icons, Theme Theme)> Shared = new(() =>
    {
        var atlas = new UiAtlas(1024, 1024);
        var font = FontAtlas.Create(atlas);
        var icons = Icons.Load(atlas);
        return (atlas, font, icons, Theme.Load());
    });

    public static FontAtlas Font => Shared.Value.Font;

    public static UiAtlas Atlas => Shared.Value.Atlas;

    public static Icons Icons => Shared.Value.Icons;

    public static UiContext CreateContext(string language = "en", float scale = 1f, int width = 1600, int height = 900) =>
        new(Shared.Value.Theme, Shared.Value.Font, Shared.Value.Icons, Shared.Value.Atlas, Strings.Load(language))
        {
            Scale = scale,
            ViewportSize = new Vector2(width, height),
        };

    /// <summary>Lays out and draws one frame.</summary>
    public static void Frame(UiContext ui, UiDrawList? list = null)
    {
        ui.Layout();
        ui.Draw(list ?? new UiDrawList());
    }

    /// <summary>Every widget of a tree, depth first.</summary>
    public static IEnumerable<Widget> Walk(Widget root)
    {
        yield return root;
        for (var i = 0; i < root.ChildCount; i++)
        {
            foreach (var w in Walk(root.GetChild(i)))
            {
                yield return w;
            }
        }
    }
}

/// <summary>A screen host without a window, for building screens in tests.</summary>
internal sealed class FakeHost : IScreenHost
{
    public FakeHost(UiContext ui)
    {
        Ui = ui;
        Bindings = new KeyBindings(KeyBindings.LoadDefinitions());
        Money = MoneyFormatter.Load();
    }

    public UiContext Ui { get; }

    public PlayerSettings Settings { get; } = new();

    public KeyBindings Bindings { get; }

    public MoneyFormatter Money { get; }

    public AudioSystem Audio { get; } = AudioSystem.CreateSilent();

    public Currency Currency => Money.Resolve(Settings.Currency, Ui.Strings.Language);

    public bool InGame { get; set; }

    public bool ShowStatistics { get; set; }

    public List<string> Calls { get; } = [];

    public NewGameOptions? Started { get; private set; }

    public void ShowMainMenu() => Calls.Add("mainMenu");

    public void ShowNewGame() => Calls.Add("newGame");

    public void StartGame(NewGameOptions options)
    {
        Started = options;
        Calls.Add("start");
    }

    public void OpenSettings() => Calls.Add("settings");

    public void OpenKeyBindings() => Calls.Add("keyBindings");

    public void OpenSaveWindow() => Calls.Add("save");

    public void OpenLoadWindow() => Calls.Add("load");

    public void ApplySettings()
    {
        Calls.Add("apply");
        if (Settings.Language is { } language && language != Ui.Strings.Language)
        {
            Ui.SetStrings(Strings.Load(language));
        }

        if (Settings.UiScale is { } scale)
        {
            Ui.Scale = scale;
        }
    }

    public void Quit() => Calls.Add("quit");
}
