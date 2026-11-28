using System.Globalization;
using Talt.Client.Audio;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Maps;

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

    /// <summary>Audio feedback for interface, command and construction cues (feature 019).</summary>
    AudioSystem Audio { get; }

    void ShowMainMenu();

    void ShowNewGame();

    void StartGame(NewGameOptions options);

    void OpenSettings();

    void OpenKeyBindings();

    /// <summary>Saves the settings and applies them at once (language, scale, display...).</summary>
    void ApplySettings();

    /// <summary>Opens the save window (feature 014; only while a game runs).</summary>
    void OpenSaveWindow();

    /// <summary>Opens the load window (feature 014).</summary>
    void OpenLoadWindow();

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

/// <summary>The first screen: New game, Load game, Settings, Quit (FR-001, FR-003).</summary>
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
        Buttons.Add(column.Add(MenuButton(new Button(TextRef.Of("menu.loadGame"), _host.OpenLoadWindow) { Tooltip = TextRef.Of("menu.loadGame.tooltip") })));
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
/// Progress of a game being generated, written by the background task and read by the screen. Stage
/// -1 means the roads and traffic are being prepared (before map generation reports anything). Map
/// generation reports from parallel workers, possibly out of order, so updates are locked and only
/// move forward.
/// </summary>
internal sealed class GenerationProgress : IProgress<MapGenerationProgress>
{
    private readonly Lock _lock = new();
    private int _stage = -1;
    private float _fraction;

    public int Stage
    {
        get
        {
            lock (_lock)
            {
                return _stage;
            }
        }
    }

    public float Fraction
    {
        get
        {
            lock (_lock)
            {
                return _fraction;
            }
        }
    }

    public void Report(MapGenerationProgress value)
    {
        var fraction = Math.Clamp(value.Fraction, 0f, 1f);
        lock (_lock)
        {
            if ((int)value.Stage > _stage || ((int)value.Stage == _stage && fraction > _fraction))
            {
                _stage = (int)value.Stage;
                _fraction = Math.Max(_fraction, fraction);
            }
        }
    }
}

/// <summary>
/// Map options built from <see cref="INewGameOptionsSource"/> descriptors (FR-002), the shareable map
/// code, Start and Back. While the game is generated the buttons are disabled and the current stage and
/// a progress bar are shown (feature 004's <see cref="MapGenerationProgress"/>).
/// </summary>
internal sealed class NewGameScreen : IScreen
{
    private readonly IScreenHost _host;
    private readonly INewGameOptionsSource _source;
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
    private Label _status = null!;
    private ProgressBar _bar = null!;
    private StackPanel _buttons = null!;
    private GenerationProgress? _progress;

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

    /// <summary>Changes one option (as the editors do) and fixes values that depend on it.</summary>
    public void SetValue(string id, int value)
    {
        _values[id] = value;
        _source.Normalize(_values);
    }

    /// <summary>Shows the generating state with its progress, or the error that stopped it.</summary>
    public void SetGenerating(bool generating, TextRef? error = null, GenerationProgress? progress = null)
    {
        Generating = generating;
        _progress = generating ? progress : null;
        _buttons.Enabled = !generating;
        _status.Visible = generating || error is not null;
        _bar.Visible = generating && progress is not null;
        _status.Color = error is null ? TextColor.Accent : TextColor.Normal;
        _status.Text = error ?? TextRef.Dynamic(
            () => _progress is { } p ? p.Stage * 1000L + (int)(p.Fraction * 100) : -1,
            s => _progress is not { Stage: >= 0 } p
                ? s.Get("newGame.generating")
                : s.Format("newGame.generatingStage",
                    ("stage", s.Get($"stage.map.{(MapGenerationStage)p.Stage}")),
                    ("percent", (int)(p.Fraction * 100))));
    }

    /// <summary>Starts the game with the current values (the Start button).</summary>
    public void Start()
    {
        try
        {
            _host.StartGame(_source.Create(_values));
        }
        catch (ArgumentException exception)
        {
            SetGenerating(false, TextRef.Of("newGame.failed", ("reason", exception.Message)));
        }
    }

    private Panel Build()
    {
        var root = new Panel { Style = PanelStyle.Background, Padding = new Thickness(0) };
        var panel = root.Add(new Panel { Style = PanelStyle.Panel, HorizontalAlignment = Alignment.Center, VerticalAlignment = Alignment.Center, Width = 600, Padding = new Thickness(20) });
        var column = panel.Add(new StackPanel(Orientation.Vertical) { Spacing = 8 });
        column.Add(new Label(TextRef.Of("newGame.title"), TextSize.Heading) { HorizontalAlignment = Alignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var option in _source.Options)
        {
            var row = column.Add(new StackPanel(Orientation.Horizontal) { Spacing = 10 });
            row.Add(new Label(TextRef.Of(option.LabelKey)) { Width = 190, Tooltip = option.TooltipKey is null ? null : TextRef.Of(option.TooltipKey) });
            AddEditor(row, option);
        }

        // The map settings as one text players can share (feature 004 MapSettings.ToText).
        column.Add(new Label(TextRef.Dynamic(ValuesVersion, s => MapCode(s)), TextSize.Small)
        {
            Color = TextColor.Muted,
            Tooltip = TextRef.Of("newGame.mapCode.tooltip"),
            Margin = new Thickness(0, 6, 0, 0),
        });

        _status = column.Add(new Label(TextRef.Of("newGame.generating")) { Visible = false, Wrap = true, Margin = new Thickness(0, 8, 0, 0) });
        _bar = column.Add(new ProgressBar(() => _progress?.Fraction ?? 0f) { Visible = false });
        _buttons = column.Add(new StackPanel(Orientation.Horizontal) { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) });
        _buttons.Add(new Button(TextRef.Of("newGame.back"), _host.ShowMainMenu) { ShortcutAction = "back", Tooltip = TextRef.Of("newGame.back.tooltip") });
        _buttons.Add(new Spacer { Fill = true });
        _buttons.Add(new Button(TextRef.Of("newGame.start"), Start) { Style = ButtonStyle.Menu, TextSize = TextSize.Title, Tooltip = TextRef.Of("newGame.start.tooltip") });
        return root;
    }

    private long ValuesVersion()
    {
        var hash = 17L;
        foreach (var (_, value) in _values)
        {
            hash = hash * 31 + value;
        }

        return hash;
    }

    private string MapCode(Talt.Client.Localization.Strings strings)
    {
        try
        {
            return strings.Format("newGame.mapCode", ("code", _source.Create(_values).MapSettings.ToText()));
        }
        catch (ArgumentException)
        {
            return strings.Get("newGame.mapCode.invalid");
        }
    }

    private void AddEditor(StackPanel row, NewGameOptionDescriptor option)
    {
        switch (option.Kind)
        {
            case NewGameOptionKind.Seed:
                row.Add(new TextInput(option.Default.ToString(CultureInfo.InvariantCulture), 9, char.IsAsciiDigit)
                {
                    OnChanged = text => SetValue(option.Id, int.TryParse(text, CultureInfo.InvariantCulture, out var v) ? v : option.Default),
                    Tooltip = option.TooltipKey is null ? null : TextRef.Of(option.TooltipKey),
                    Fill = true,
                });
                break;
            case NewGameOptionKind.Slider:
                row.Add(new Slider(option.Minimum, option.Maximum, option.Step, () => _values[option.Id], v => SetValue(option.Id, (int)MathF.Round(v)))
                {
                    Fill = true,
                    Tooltip = option.TooltipKey is null ? null : TextRef.Of(option.TooltipKey),
                });
                row.Add(new Label(TextRef.Dynamic(() => _values[option.Id], _ => _values[option.Id].ToString(CultureInfo.InvariantCulture)))
                {
                    Width = 40,
                    TextAlignment = Alignment.End,
                });
                break;
            default:
                // Counts get thousands separators; sizes and years are shown as plain digits.
                var choices = option.Choices
                    .Select(c => new DropdownChoice(TextRef.Of(c.LabelKey, ("value", option.GroupDigits ? c.Value : c.Value.ToString(CultureInfo.InvariantCulture))), c.Value))
                    .ToList();
                var values = option.Choices.Select(c => c.Value).ToArray();
                row.Add(new Dropdown(choices, () => Array.IndexOf(values, _values[option.Id]), index => SetValue(option.Id, values[index])) { Fill = true });
                break;
        }
    }
}
