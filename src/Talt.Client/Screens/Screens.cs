using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;

namespace Talt.Client.Screens;

/// <summary>What screens and windows need from the client (implemented by <see cref="GameWindow"/>).</summary>
internal interface IScreenHost
{
    UiContext Ui { get; }

    PlayerSettings Settings { get; }

    KeyBindings Bindings { get; }

    MoneyFormatter Money { get; }

    /// <summary>The player's display currency.</summary>
    Currency Currency { get; }

    /// <summary>Whether a game is in progress.</summary>
    bool InGame { get; }

    /// <summary>The F3 render statistics in the window title.</summary>
    bool ShowStatistics { get; set; }

    void ShowMainMenu();

    void ShowNewGame();

    void StartGame(NewGameOptions options);

    void OpenSettings();

    void OpenKeyBindings();

    /// <summary>Saves the settings and applies them at once (language, scale, display...).</summary>
    void ApplySettings();

    void Quit();
}

/// <summary>A full-screen state of the client (data-model.md Screens).</summary>
internal interface IScreen
{
    /// <summary>Widget tree shown in the interface's screen layer.</summary>
    Widget Root { get; }

    /// <summary>Whether the simulation and world are shown.</summary>
    bool ShowsWorld { get; }

    /// <summary>Called every frame.</summary>
    void Update(double deltaSeconds);

    /// <summary>A bound action was pressed; returns whether the screen used it.</summary>
    bool HandleAction(string actionId, bool shift);
}

/// <summary>The first screen: New game, Load game (disabled), Settings, Quit (FR-001, FR-003).</summary>
internal sealed class MainMenuScreen : IScreen
{
    private readonly IScreenHost _host;

    public MainMenuScreen(IScreenHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Root = Build();
    }

    public Widget Root { get; }

    public bool ShowsWorld => false;

    /// <summary>The menu buttons in order (for tests).</summary>
    internal List<Button> Buttons { get; } = [];

    public void Update(double deltaSeconds)
    {
    }

    public bool HandleAction(string actionId, bool shift)
    {
        if (actionId == "settings")
        {
            _host.OpenSettings();
            return true;
        }

        return false;
    }

    private Panel Build()
    {
        var root = new Panel { Style = PanelStyle.Background, Padding = new Thickness(0) };
        // The column takes the width of its widest text (title, subtitle); buttons keep a fixed width.
        var column = root.Add(new StackPanel(Orientation.Vertical)
        {
            Spacing = 12,
            HorizontalAlignment = Alignment.Center,
            VerticalAlignment = Alignment.Center,
        });
        column.Add(new Label(TextRef.Of("app.title"), TextSize.Heading) { HorizontalAlignment = Alignment.Center, Color = TextColor.Accent });
        column.Add(new Label(TextRef.Of("menu.subtitle")) { HorizontalAlignment = Alignment.Center, Color = TextColor.Muted, Margin = new Thickness(0, 0, 0, 24) });
        Buttons.Add(column.Add(MenuButton(new Button(TextRef.Of("menu.newGame"), _host.ShowNewGame))));
        Buttons.Add(column.Add(MenuButton(new Button(TextRef.Of("menu.loadGame")) { Enabled = false, Tooltip = TextRef.Of("menu.loadGame.tooltip") })));
        Buttons.Add(column.Add(MenuButton(new Button(TextRef.Of("menu.settings"), _host.OpenSettings) { ShortcutAction = "settings", Tooltip = TextRef.Of("menu.settings.tooltip") })));
        Buttons.Add(column.Add(MenuButton(new Button(TextRef.Of("menu.quit"), _host.Quit))));
        root.Add(new Label(TextRef.Of("menu.footer"), TextSize.Small)
        {
            Color = TextColor.Muted,
            HorizontalAlignment = Alignment.Center,
            VerticalAlignment = Alignment.End,
            Margin = new Thickness(0, 0, 0, 12),
        });
        return root;
    }

    private static Button MenuButton(Button button)
    {
        button.Style = ButtonStyle.Menu;
        button.TextSize = TextSize.Title;
        button.Width = 360;
        button.HorizontalAlignment = Alignment.Center;
        return button;
    }
}

/// <summary>
/// Map options built from <see cref="INewGameOptionsSource"/> descriptors (FR-002), Start and Back.
/// While the game is generated the buttons are disabled and "Generating…" is shown.
/// </summary>
internal sealed class NewGameScreen : IScreen
{
    private readonly IScreenHost _host;
    private readonly INewGameOptionsSource _source;
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
    private Label _status = null!;
    private StackPanel _buttons = null!;

    public NewGameScreen(IScreenHost host, INewGameOptionsSource source)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        foreach (var option in source.Options)
        {
            _values[option.Id] = option.Default;
        }

        Root = Build();
    }

    public Widget Root { get; }

    public bool ShowsWorld => false;

    /// <summary>Current values (option id → value).</summary>
    public IReadOnlyDictionary<string, int> Values => _values;

    /// <summary>Whether a game is being generated.</summary>
    public bool Generating { get; private set; }

    public void Update(double deltaSeconds)
    {
    }

    public bool HandleAction(string actionId, bool shift)
    {
        if (actionId == "back" && !Generating)
        {
            _host.ShowMainMenu();
            return true;
        }

        return false;
    }

    /// <summary>Shows the generating state (or the error that stopped it).</summary>
    public void SetGenerating(bool generating, TextRef? error = null)
    {
        Generating = generating;
        _buttons.Enabled = !generating;
        _status.Visible = generating || error is not null;
        _status.Text = error ?? TextRef.Of("newGame.generating");
        _status.Color = error is null ? TextColor.Accent : TextColor.Normal;
    }

    /// <summary>Starts the game with the current values (the Start button).</summary>
    public void Start() => _host.StartGame(_source.Create(_values));

    private Panel Build()
    {
        var root = new Panel { Style = PanelStyle.Background, Padding = new Thickness(0) };
        var panel = root.Add(new Panel { Style = PanelStyle.Panel, HorizontalAlignment = Alignment.Center, VerticalAlignment = Alignment.Center, Width = 520, Padding = new Thickness(20) });
        var column = panel.Add(new StackPanel(Orientation.Vertical) { Spacing = 10 });
        column.Add(new Label(TextRef.Of("newGame.title"), TextSize.Heading) { TextAlignment = Alignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var option in _source.Options)
        {
            var row = column.Add(new StackPanel(Orientation.Horizontal) { Spacing = 10 });
            row.Add(new Label(TextRef.Of(option.LabelKey)) { Width = 180, Tooltip = option.TooltipKey is null ? null : TextRef.Of(option.TooltipKey) });
            row.Add(Editor(option)).Fill = true;
        }

        _status = column.Add(new Label(TextRef.Of("newGame.generating")) { Visible = false, TextAlignment = Alignment.Center, Wrap = true, Margin = new Thickness(0, 8, 0, 0) });
        _buttons = column.Add(new StackPanel(Orientation.Horizontal) { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) });
        _buttons.Add(new Button(TextRef.Of("newGame.back"), _host.ShowMainMenu) { ShortcutAction = "back", Tooltip = TextRef.Of("newGame.back.tooltip") });
        _buttons.Add(new Spacer { Fill = true });
        _buttons.Add(new Button(TextRef.Of("newGame.start"), Start) { Style = ButtonStyle.Menu, TextSize = TextSize.Title, Tooltip = TextRef.Of("newGame.start.tooltip") });
        return root;
    }

    private Widget Editor(NewGameOptionDescriptor option)
    {
        if (option.Kind == NewGameOptionKind.Seed)
        {
            return new TextInput(option.Default.ToString(System.Globalization.CultureInfo.InvariantCulture), 9, char.IsAsciiDigit)
            {
                OnChanged = text => _values[option.Id] = int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : option.Default,
                Tooltip = option.TooltipKey is null ? null : TextRef.Of(option.TooltipKey),
            };
        }

        // Counts get thousands separators; sizes and years are shown as plain digits.
        var choices = option.Choices
            .Select(c => new DropdownChoice(TextRef.Of(c.LabelKey, ("value", option.GroupDigits ? c.Value : c.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))), c.Value))
            .ToList();
        var values = option.Choices.Select(c => c.Value).ToArray();
        return new Dropdown(
            choices,
            () => Array.IndexOf(values, _values[option.Id]),
            index => _values[option.Id] = values[index]);
    }
}
