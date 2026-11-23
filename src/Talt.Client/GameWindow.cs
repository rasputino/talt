using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Rendering;
using Talt.Client.Screens;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Simulation;

namespace Talt.Client;

/// <summary>Command-line options of the client (contracts/command-line.md).</summary>
internal sealed record ClientOptions(
    PlayerSettings Settings,
    string? LanguageOverride = null,
    float? UiScaleOverride = null,
    NewGameOptions? StartGame = null,
    SessionOptions? Session = null,
    bool ShowStatistics = false,
    float? InitialZoom = null,
    int UiTestWindows = 0);

/// <summary>
/// Owns the window, the renderer, the interface and the current screen (main menu, new game, game).
/// Input goes through <see cref="InputRouter"/> (interface first, then key bindings, then the world);
/// the game itself lives in <see cref="GameSession"/>.
/// </summary>
internal sealed class GameWindow : IDisposable, IScreenHost
{
    private const double LongFrameMilliseconds = 33.4; // Two refreshes at 60 Hz.
    private const int AtlasSize = 1024;

    private readonly IWindow _window;
    private readonly OrbitCamera _camera = new();
    private readonly ClientOptions _options;
    private readonly Stopwatch _statsTimer = new();
    private readonly FrameTimeTracker _frameTimes = new();
    private readonly UiDrawList _drawList = new();
    private readonly DefaultNewGameOptionsSource _newGameSource = new();
    private readonly Func<Key, bool> _isKeyDown;

    private WebGpuRenderer? _renderer;
    private IInputContext? _input;
    private UiContext? _ui;
    private InputRouter? _router;
    private KeyBindings? _bindings;
    private MoneyFormatter? _money;
    private IScreen? _screen;
    private GameSession? _session;
    private Task<GeneratedGame>? _generation;
    private string _language;
    private bool _uiTestWindowsOpened;
    private int _frames;
    private int _ticksThisSecond;
    private long _lastFrameTimestamp;
    private long _renderAllocatedBytes;
    private int _renderAllocationFrames;
    private double _allocatedBytesPerFrame;
    private double _uiCpuMs;
    private double _uiMsSum;
    private int _uiMsFrames;
    private double _uiAverageMs;

    public GameWindow(ClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Settings = options.Settings;
        _language = options.LanguageOverride ?? Settings.Language ?? Strings.DetectLanguage();
        ShowStatistics = options.ShowStatistics;
        _isKeyDown = IsKeyDown;

        var windowOptions = WindowOptions.Default with
        {
            Title = "Transit & Logistics Tycoon",
            Size = new Vector2D<int>(Settings.WindowWidth, Settings.WindowHeight),
            API = GraphicsAPI.None, // WebGPU manages the surface itself.
            VSync = Settings.VSync,
            WindowState = Settings.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
        };
        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += size => _renderer?.Resize(size);
        _window.Resize += OnWindowResize;
    }

    public UiContext Ui => _ui ?? throw new InvalidOperationException("The interface is not loaded yet.");

    public PlayerSettings Settings { get; }

    public KeyBindings Bindings => _bindings ?? throw new InvalidOperationException("Bindings are not loaded yet.");

    public MoneyFormatter Money => _money ?? throw new InvalidOperationException("Currencies are not loaded yet.");

    public Currency Currency => Money.Resolve(Settings.Currency, _language);

    public bool InGame => _session is not null;

    public bool ShowStatistics { get; set; }

    public void Run() => _window.Run();

    public void Dispose()
    {
        _input?.Dispose();
        // Release GPU resources after the render loop has stopped, never during it.
        _renderer?.Dispose();
        _window.Dispose();
        _session?.SaveRecording();
    }

    public void ShowMainMenu()
    {
        LeaveGame();
        SetScreen(new MainMenuScreen(this));
    }

    public void ShowNewGame() => SetScreen(new NewGameScreen(this, _newGameSource));

    public void StartGame(NewGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_generation is not null)
        {
            return;
        }

        if (_screen is not NewGameScreen newGame)
        {
            newGame = new NewGameScreen(this, _newGameSource);
            SetScreen(newGame);
        }

        newGame.SetGenerating(true);
        // Generation takes seconds on large maps: a background task keeps the window responsive.
        _generation = Task.Run(() => GameSetup.Create(options, Console.WriteLine));
    }

    public void OpenSettings() => Ui.Windows.Open(SettingsWindow.Create(this));

    public void OpenKeyBindings() => Ui.Windows.Open(KeyBindingsWindow.Create(this));

    public void ApplySettings()
    {
        var ui = Ui;
        var language = _options.LanguageOverride ?? Settings.Language ?? Strings.DetectLanguage();
        if (language != _language || language != ui.Strings.Language)
        {
            _language = language;
            ui.SetStrings(Strings.Load(language));
        }

        var scale = _options.UiScaleOverride ?? Settings.UiScale ?? SuggestedScale();
        ui.Scale = scale;

        var state = Settings.Fullscreen ? WindowState.Fullscreen : WindowState.Normal;
        if (_window.WindowState != state && (Settings.Fullscreen || _window.WindowState == WindowState.Fullscreen))
        {
            _window.WindowState = state;
        }

        _window.VSync = Settings.VSync;
        _renderer?.SetVerticalSync(Settings.VSync);
        if (_screen is InGameScreen game)
        {
            game.ApplySettings(Settings);
        }

        SaveSettings();
    }

    public void Quit() => _window.Close();

    private void SaveSettings()
    {
        try
        {
            Settings.Save(PlayerSettings.DefaultPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not save settings: {exception.Message}");
        }
    }

    private void OnLoad()
    {
        _renderer = new WebGpuRenderer(_window, Settings.VSync);
        Console.WriteLine($"GPU: {_renderer.AdapterDescription}");

        var atlas = new UiAtlas(AtlasSize, AtlasSize);
        var font = FontAtlas.Create(atlas);
        Console.WriteLine($"Interface font: {font.GlyphCount} glyphs in {font.BuildMilliseconds:F0} ms.");
        var icons = Icons.Load(atlas);
        _money = MoneyFormatter.Load();
        _bindings = new KeyBindings(KeyBindings.LoadDefinitions());
        _bindings.ApplyOverrides(Settings.KeyBindings, problem => Settings.LoadProblems.Add(problem));
        _ui = new UiContext(Theme.Load(), font, icons, atlas, Strings.Load(_language))
        {
            Scale = _options.UiScaleOverride ?? Settings.UiScale ?? SuggestedScale(),
            ShortcutText = action => _bindings.Primary(action) is { } chord ? KeyNames.Display(_ui!.Strings, chord) : null,
        };
        _renderer.AttachUi(atlas);
        UpdateViewport();

        _router = new InputRouter(_ui, _bindings, new WorldInputProxy(this), OnAction);
        _input = _window.CreateInput();
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, position) => _router.MouseMove(ToFramebuffer(position));
            mouse.MouseDown += (m, button) => _router.MouseDown(button, ToFramebuffer(m.Position));
            mouse.MouseUp += (m, button) => _router.MouseUp(button, ToFramebuffer(m.Position));
            mouse.Scroll += (_, wheel) => _router.Scroll(wheel.Y);
        }

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (k, key, _) => _router.KeyDown(key, k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));
            keyboard.KeyChar += (_, character) => _router.Char(character);
        }

        if (_options.StartGame is { } start)
        {
            StartGame(start); // Game arguments on the command line skip the menu (smoke test).
        }
        else
        {
            SetScreen(new MainMenuScreen(this));
        }

        if (Settings.LoadProblems.Count > 0)
        {
            ShowSettingsProblems();
        }

        _statsTimer.Start();
    }

    private void ShowSettingsProblems()
    {
        var window = new UiWindow("settings-problems", TextRef.Of("settings.problems.title"));
        window.Content = new Label(TextRef.Of("settings.problems.text")) { Wrap = true, Width = 360 };
        Ui.Windows.Open(window);
        Settings.LoadProblems.Clear();
        SaveSettings(); // Rewrites the file without the invalid values.
    }

    private void SetScreen(IScreen screen)
    {
        var ui = Ui;
        ui.ResetInteraction();
        if (_screen is InGameScreen previous && screen is not InGameScreen)
        {
            previous.Leave();
        }

        _screen = screen;
        ui.Screen = screen.Root;
    }

    private void LeaveGame()
    {
        if (_session is null)
        {
            return;
        }

        if (_screen is InGameScreen game)
        {
            game.Leave();
        }

        _session.SaveRecording();
        _session.Detach();
        _session = null;
        Ui.Windows.CloseEverything();
        _renderer?.ClearWorld();
    }

    private void EnterGame(GeneratedGame game)
    {
        _session = new GameSession(game, _options.Session ?? new SessionOptions(), Settings.FastForwardUnlocked);
        _session.ExitRequested += () => _window.Close();

        var map = game.Heightmap;
        _camera.FitMap(Math.Max(map.Width, map.Depth));
        _camera.Distance = _options.InitialZoom ?? 350f;
        var center = new Vector2(map.Width / 2f, map.Depth / 2f);
        _camera.Target = new Vector3(center.X, map.Get(center.X, center.Y), center.Y);

        _renderer?.SetWorld(game.World);
        var screen = new InGameScreen(this, _session, _camera, OnTerrainEdited, _isKeyDown);
        SetScreen(screen);
        screen.ApplySettings(Settings);
        if (_options.UiTestWindows > 0 && !_uiTestWindowsOpened)
        {
            _uiTestWindowsOpened = true;
            for (var i = 1; i <= _options.UiTestWindows; i++)
            {
                Ui.Windows.Open(SampleWindows.Create(i));
            }
        }
    }

    private void OnTerrainEdited(IReadOnlyList<(int X, int Y, int Z)> voxels) =>
        _renderer?.NotifyVoxelsChanged(voxels, Stopwatch.GetTimestamp());

    private void OnAction(string action, bool shift)
    {
        if (_screen?.HandleAction(action, shift) == true)
        {
            return;
        }

        switch (action)
        {
            case "back":
                Ui.Windows.CloseFront();
                break;
            case "closeAllWindows":
                Ui.Windows.CloseAll();
                break;
            case "settings":
                OpenSettings();
                break;
            case "statistics":
                ShowStatistics = !ShowStatistics;
                break;
        }
    }

    private bool IsKeyDown(Key key)
    {
        if (_input is null)
        {
            return false;
        }

        foreach (var keyboard in _input.Keyboards)
        {
            if (keyboard.IsKeyPressed(key))
            {
                return true;
            }
        }

        return false;
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_ui is null)
        {
            return;
        }

        if (_generation is { IsCompleted: true } generation)
        {
            _generation = null;
            if (generation.IsCompletedSuccessfully)
            {
                EnterGame(generation.Result);
            }
            else
            {
                var message = generation.Exception?.GetBaseException().Message ?? "?";
                Console.Error.WriteLine($"Could not create the game: {message}");
                (_screen as NewGameScreen)?.SetGenerating(false, TextRef.Of("newGame.failed", ("reason", message)));
            }
        }

        _ui.Update(deltaSeconds);
        _screen?.Update(deltaSeconds);
        if (_session is not null)
        {
            var stats = _session.Advance(deltaSeconds);
            _ticksThisSecond += stats.TicksRun;
        }
    }

    private void OnRender(double deltaSeconds)
    {
        if (_renderer is null || _ui is null || _router is null)
        {
            return;
        }

        if (_session is not null)
        {
            _session.BuildInstances(); // Once per drawn frame, so uploads never outpace submissions.
            _renderer.UpdateVehicles(_session.Instances);
        }

        var uiStart = Stopwatch.GetTimestamp();
        UpdateViewport();
        if (_screen is InGameScreen game)
        {
            game.UpdatePicking(_router.CursorPosition, _ui.ViewportSize, _router.IsCursorOverWorld && !_router.IsDraggingWorld);
            _renderer.SetHighlights(game.Highlights, _camera.Position);
        }

        _ui.Layout();
        _ui.Draw(_drawList);
        _uiCpuMs = Stopwatch.GetElapsedTime(uiStart).TotalMilliseconds;

        // Managed allocations of the draw path (constitution II: none expected in steady state).
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var theme = _ui.Theme.Background;
        _renderer.Draw(_camera, _drawList, SrgbToLinear(theme));
        _renderAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _renderAllocationFrames++;
        _uiMsSum += _uiCpuMs + _renderer.LastUiMilliseconds;
        _uiMsFrames++;

        var now = Stopwatch.GetTimestamp();
        if (_lastFrameTimestamp != 0)
        {
            var frameMs = Stopwatch.GetElapsedTime(_lastFrameTimestamp, now).TotalMilliseconds;
            _frameTimes.Record(frameMs);
            if (ShowStatistics && frameMs > LongFrameMilliseconds)
            {
                var d = _renderer.LastDrawTimings;
                Console.WriteLine($"Long frame {frameMs:F1} ms: ui {_uiCpuMs:F2}, acquire {d.Acquire:F1}, prepare {d.Prepare:F1}, encode {d.Encode:F1}, wait GPU {d.WaitGpu:F1}, submit {d.Submit:F1}");
            }
        }

        _lastFrameTimestamp = now;
        while (_renderer.TryTakeCompletedEdit(out var editTimestamp, out var chunks))
        {
            Console.WriteLine(_ui.Strings.Format("edit.visible",
                ("ms", Math.Round(Stopwatch.GetElapsedTime(editTimestamp).TotalMilliseconds, 1)),
                ("chunks", chunks)));
        }

        _frames++;
        var seconds = _statsTimer.Elapsed.TotalSeconds;
        if (seconds >= 1)
        {
            UpdateTitle(seconds);
            _frames = 0;
            _ticksThisSecond = 0;
            _statsTimer.Restart();
        }
    }

    private void UpdateTitle(double seconds)
    {
        var s = _ui!.Strings;
        _allocatedBytesPerFrame = _renderAllocationFrames > 0 ? _renderAllocatedBytes / (double)_renderAllocationFrames : 0;
        _renderAllocatedBytes = 0;
        _renderAllocationFrames = 0;
        _uiAverageMs = _uiMsFrames > 0 ? _uiMsSum / _uiMsFrames : 0;
        _uiMsSum = 0;
        _uiMsFrames = 0;
        if (!ShowStatistics)
        {
            _window.Title = s.Get("app.title");
            return;
        }

        var effective = _ticksThisSecond / seconds / GameClock.TicksPerSecondAtNormalSpeed;
        var r = _renderer!.Statistics;
        var speed = _session?.Runner.Speed ?? GameSpeed.Paused;
        var statistics = string.Join(" — ",
            s.FormatSpeed(speed),
            s.Format("status.effectiveSpeed", ("speed", Math.Round(effective, 1))),
            s.Format("stats.frame", ("average", Math.Round(_frameTimes.AverageMs, 1)), ("worst", Math.Round(_frameTimes.WorstMs, 1))),
            s.Format("status.fps", ("fps", Math.Round(_frameTimes.FramesPerSecond))),
            s.Format("stats.ui", ("ms", Math.Round(_uiAverageMs, 2)), ("windows", Ui.Windows.Count)),
            s.Format("stats.draws", ("count", r.DrawCalls)),
            s.Format("stats.triangles", ("count", r.Triangles)),
            s.Format("stats.regions", ("lod0", r.RegionsLod0), ("lod1", r.RegionsLod1), ("lod2", r.RegionsLod2), ("lod3", r.RegionsLod3)),
            s.Format("stats.memory", ("mb", r.WorldGpuBytes / (1024 * 1024))),
            s.Format("stats.jobs", ("count", r.PendingJobs + r.JobsInFlight)),
            s.Format("stats.allocated", ("bytes", (long)_allocatedBytesPerFrame)));
        _window.Title = statistics;
        Console.WriteLine(statistics);
    }

    private void UpdateViewport()
    {
        var size = _window.FramebufferSize;
        Ui.ViewportSize = new Vector2(size.X, size.Y);
    }

    private void OnWindowResize(Vector2D<int> size)
    {
        if (_window.WindowState == WindowState.Normal && size.X >= PlayerSettings.MinimumWindowWidth && size.Y >= PlayerSettings.MinimumWindowHeight
            && (size.X != Settings.WindowWidth || size.Y != Settings.WindowHeight))
        {
            Settings.WindowWidth = size.X;
            Settings.WindowHeight = size.Y;
            _windowSizeDirty = true;
        }
    }

    private bool _windowSizeDirty;

    /// <summary>Mouse positions come in window coordinates; the interface works in framebuffer pixels (high-DPI macOS).</summary>
    private Vector2 ToFramebuffer(Vector2 position)
    {
        var window = _window.Size;
        var framebuffer = _window.FramebufferSize;
        if (_windowSizeDirty && _router is not null && !_router.IsDraggingWorld)
        {
            _windowSizeDirty = false;
            SaveSettings();
        }

        return window.X > 0 && window.Y > 0
            ? new Vector2(position.X * framebuffer.X / window.X, position.Y * framebuffer.Y / window.Y)
            : position;
    }

    /// <summary>
    /// First-start interface scale (FR-024): the operating system's scaling (framebuffer / window size),
    /// or a larger step on high-resolution monitors that report no scaling; rounded to 25 %.
    /// </summary>
    private float SuggestedScale()
    {
        var ratio = _window.Size.X > 0 ? _window.FramebufferSize.X / (float)_window.Size.X : 1f;
        if (ratio <= 1.01f && _window.Monitor?.VideoMode.Resolution is { } resolution)
        {
            ratio = resolution.Y >= 2000 ? 1.5f : resolution.Y >= 1400 ? 1.25f : 1f;
        }

        var stepped = MathF.Round(ratio * 4f) / 4f;
        return Math.Clamp(stepped, 1f, 2f);
    }

    private static Vector3 SrgbToLinear(UiColor color)
    {
        static float Channel(byte value)
        {
            var c = value / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        return new Vector3(Channel(color.R), Channel(color.G), Channel(color.B));
    }

    /// <summary>Sends world input to the in-game screen, if any (menus have no world).</summary>
    private sealed class WorldInputProxy(GameWindow owner) : IWorldInput
    {
        private IWorldInput? Target => owner._screen as IWorldInput;

        public void MouseDown(MouseButton button, Vector2 position) => Target?.MouseDown(button, position);

        public void Drag(Vector2 position, Vector2 delta) => Target?.Drag(position, delta);

        public void MouseUp(MouseButton button, Vector2 position, bool isClick) => Target?.MouseUp(button, position, isClick);

        public void Scroll(float delta) => Target?.Scroll(delta);
    }
}
