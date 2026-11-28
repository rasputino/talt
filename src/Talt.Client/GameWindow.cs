using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Talt.Client.Audio;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Rendering;
using Talt.Client.Saves;
using Talt.Client.Screens;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Saves;
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
    Vector2? InitialCameraTarget = null,
    int UiTestWindows = 0,
    bool NoAudio = false,
    GraphicsPreset? GraphicsOverride = null,
    bool DayNightOverride = false,
    double? TimeOfDayStart = null,
    double? MeasureEffectsSeconds = null,
    string? LoadPath = null);

/// <summary>
/// Owns the window, the renderer, the interface and the current screen (main menu, new game, game).
/// Input goes through <see cref="InputRouter"/> (interface first, then key bindings, then the world);
/// the game itself lives in <see cref="GameSession"/>.
/// </summary>
internal sealed class GameWindow : IDisposable, IScreenHost, ISaveHost
{
    private const double LongFrameMilliseconds = 33.4; // Two refreshes at 60 Hz.
    private const int AtlasSize = 1024;

    private readonly IWindow _window;
    private readonly OrbitCamera _camera = new();
    private readonly ClientOptions _options;
    private readonly Stopwatch _statsTimer = new();
    private readonly Stopwatch _realTime = Stopwatch.StartNew();
    private readonly TimeOfDay _timeOfDay = new();
    private GraphicsSettings? _graphicsOverride;
    private ModelLibrary? _models;
    private ModelBatch? _modelBatch;
    private double _modelBatchMs;
    private EffectCostProbe? _probe;
    private double _prepareMsSum;
    private double _encodeMsSum;
    private double _waitMsSum;
    private double _batchMsSum;
    private int _timingFrames;
    private readonly FrameTimeTracker _frameTimes = new();
    private readonly UiDrawList _drawList = new();
    private readonly DefaultNewGameOptionsSource _newGameSource = new();
    private readonly Func<Key, bool> _isKeyDown;
    private readonly SaveController _saver = new();
    private readonly AutosaveScheduler _autosave = new();
    private Task<(SaveLoadResult Result, GeneratedGame? Game)>? _loading;
    private string? _loadingPath;
    private IScreen? _previousScreen;
    private Widget? _previousOverlay;
    private WindowManager? _previousWindows;
    private GenerationProgress? _loadingProgress;

    private WebGpuRenderer? _renderer;
    private IInputContext? _input;
    private UiContext? _ui;
    private InputRouter? _router;
    private KeyBindings? _bindings;
    private MoneyFormatter? _money;
    private AudioSystem? _audio;
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
        _graphicsOverride = options.GraphicsOverride is { } preset ? GraphicsSettings.For(preset) : null;
        if (options.TimeOfDayStart is { } phase)
        {
            _timeOfDay.Phase = phase;
        }
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

    public AudioSystem Audio => _audio ?? throw new InvalidOperationException("Audio is not loaded yet.");

    public Currency Currency => Money.Resolve(Settings.Currency, _language);

    public bool InGame => _session is not null;

    public bool ShowStatistics { get; set; }

    public void Run() => _window.Run();

    public void Dispose()
    {
        // Setup workers may still be priming the capture buffers when the window closes.
        try
        {
            _generation?.Wait();
            _loading?.Wait();
        }
        catch (AggregateException exception)
        {
            Console.Error.WriteLine(exception.GetBaseException().Message);
        }

        _saver.Dispose();
        _audio?.Dispose();
        _input?.Dispose();
        // Release GPU resources after the render loop has stopped, never during it.
        _renderer?.Dispose();
        _window.Dispose();
        _session?.SaveRecording();
    }

    public void ShowMainMenu() => ConfirmDiscard(() =>
    {
        LeaveGame();
        SetScreen(new MainMenuScreen(this));
    });

    public void ShowNewGame() => ConfirmDiscard(() =>
    {
        LeaveGame();
        SetScreen(new NewGameScreen(this, _newGameSource));
    });

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

        // Generation takes seconds on large maps: a background task keeps the window responsive, and the
        // screen shows the stage and a progress bar (feature 004 progress, task T069).
        var progress = new GenerationProgress();
        newGame.SetGenerating(true, progress: progress);
        _generation = Task.Run(() =>
        {
            var game = GameSetup.Create(options, Console.WriteLine, progress);
            _saver.Prepare(game.Simulation);
            return game;
        });
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
        _graphicsOverride = null; // A change in the settings screen replaces --graphics for this run.
        _audio?.ApplySettings(Settings);
        if (_screen is InGameScreen game)
        {
            game.ApplySettings(Settings);
        }

        SaveSettings();
    }

    public void Quit() => ConfirmDiscard(_window.Close);

    // ---- Saving and loading (feature 014) ----

    /// <summary>The player's saves folder.</summary>
    public SaveStore Saves { get; } = new(SaveStore.DefaultFolder);

    /// <summary>The game version written into saves.</summary>
    private static string GameVersion { get; } = typeof(GameWindow).Assembly.GetName().Version?.ToString() ?? "0";

    public string SuggestedSaveName => _session is null ? string.Empty : Ui.Strings.FormatDate(_session.Simulation.Date);

    public void OpenSaveWindow()
    {
        if (_session is not null)
        {
            OpenCatalogWindow(() => SaveWindow.Create(this));
        }
    }

    public void OpenLoadWindow() => OpenCatalogWindow(() => LoadWindow.Create(this));

    private void OpenCatalogWindow(Func<UiWindow> create)
    {
        try
        {
            Ui.Windows.Open(create());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowMessage(TextRef.Of("load.failed.title"), TextRef.Of("load.failed.io", ("reason", error.Message)));
        }
    }

    public void ConfirmDiscard(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ReportSave(_saver.WaitForWrite());
        if (_session is { HasUnsavedProgress: true })
        {
            Ui.Windows.Open(ConfirmDialog.Create(Ui, TextRef.Of("unsaved.title"), TextRef.Of("unsaved.body"), TextRef.Of("unsaved.yes"), action));
        }
        else
        {
            action();
        }
    }

    public void ShowFileError(string messageKey) => ShowMessage(TextRef.Of("load.title"), TextRef.Of(messageKey));

    public void SaveAs(string name)
    {
        try
        {
            StartSave(Saves.Existing(name) ?? Saves.PathFor(name), name, SaveKind.Manual);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Notify(TextRef.Of("save.failed", ("reason", error.Message)));
        }
    }

    /// <summary>F5: saves to the single quick save slot without asking (FR-008a).</summary>
    private void QuickSave()
    {
        if (_session is not null)
        {
            StartSave(Saves.QuickSavePath, Ui.Strings.Get("load.quicksave"), SaveKind.QuickSave);
        }
    }

    /// <summary>F9: loads the quick save, asking first when there is unsaved progress (FR-008a).</summary>
    private void QuickLoad()
    {
        ReportSave(_saver.WaitForWrite());
        if (!File.Exists(Saves.QuickSavePath))
        {
            Notify(TextRef.Of("load.quick.none"));
            return;
        }

        ConfirmDiscard(() => LoadSave(Saves.QuickSavePath));
    }

    private void Autosave()
    {
        _autosave.Reset();
        try
        {
            StartSave(Saves.NextAutosavePath(Settings.AutosaveKeep), Ui.Strings.Get("load.autosave"), SaveKind.Autosave);
        }
        catch (IOException exception)
        {
            Notify(TextRef.Of("save.failed", ("reason", exception.Message)));
        }
    }

    private void StartSave(string path, string name, SaveKind kind)
    {
        if (_session is null)
        {
            return;
        }

        if (_saver.IsWriting)
        {
            // A manual or quick save waits for the running write (spec edge cases); an autosave would just try again.
            ReportSave(_saver.WaitForWrite());
        }

        if (_saver.TryStart(_session.Simulation, _camera, path, name, kind, GameVersion))
        {
            _autosave.Reset();
            _session.MarkSaved(_session.Simulation.CurrentTick);
            Console.WriteLine($"Saving '{name}' ({kind}): captured in {_saver.LastCaptureMilliseconds:F1} ms on the main thread.");
        }
    }

    private void ReportSave(SaveOutcome? outcome)
    {
        if (outcome is null)
        {
            return;
        }

        if (outcome.Result.Succeeded)
        {
            if (outcome.Kind == SaveKind.Autosave)
            {
                Saves.PruneAutosaves(Settings.AutosaveKeep);
            }

            Console.WriteLine($"Saved {outcome.Result.Path} ({outcome.Result.Bytes / 1048576.0:F1} MB).");
            Notify(TextRef.Of(outcome.Kind == SaveKind.QuickSave ? "save.quick.done" : outcome.Kind == SaveKind.Autosave ? "save.auto.done" : "save.done", ("name", outcome.Name)));
        }
        else
        {
            Console.Error.WriteLine($"Could not save {outcome.Result.Path}: {outcome.Result.Problem}");
            _session?.MarkSaved(-1); // The progress was not saved after all.
            Notify(TextRef.Of("save.failed", ("reason", outcome.Result.Problem ?? "?")));
        }
    }

    public void LoadSave(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (_loading is not null || _generation is not null)
        {
            return;
        }

        ReportSave(_saver.WaitForWrite());
        _loadingProgress = new GenerationProgress();
        _loadingPath = path;
        _previousScreen = _screen;
        _previousOverlay = Ui.Overlay;
        _previousWindows = Ui.Windows;
        Ui.Windows = new WindowManager();
        Ui.ResetInteraction();
        Ui.Overlay = null;
        _screen = new LoadingScreen(_loadingProgress);
        Ui.Screen = _screen.Root;
        var progress = _loadingProgress;
        _loading = Task.Run(() =>
        {
            var loaded = GameSetup.FromSave(path, progress);
            if (loaded.Game is { } game)
            {
                _saver.Prepare(game.Simulation);
            }

            return loaded;
        });
    }

    private void FinishLoading((SaveLoadResult Result, GeneratedGame? Game) loaded)
    {
        var (result, game) = loaded;
        _screen = _previousScreen;
        Ui.Overlay = _previousOverlay;
        Ui.Windows = _previousWindows!;
        _previousWindows = null;
        _previousScreen = null;
        _previousOverlay = null;
        if (game is null)
        {
            Console.Error.WriteLine($"Could not load {_loadingPath}: {result.Status}. {result.Problem}");
            SetScreen(_screen ?? new MainMenuScreen(this));

            var key = result.Status switch
            {
                SaveLoadStatus.TooNew => "load.failed.tooNew",
                SaveLoadStatus.MissingContent => "load.failed.missingContent",
                SaveLoadStatus.IoError => "load.failed.io",
                _ => "load.failed.damaged",
            };
            ShowMessage(TextRef.Of("load.failed.title"), TextRef.Of(key, ("missing", string.Join(", ", result.MissingContent)), ("reason", result.Problem ?? string.Empty)));
            return;
        }

        LeaveGame();
        EnterGame(game, result);
        if (result.Status == SaveLoadStatus.LoadedWithChangedData)
        {
            ShowMessage(TextRef.Of("load.warning.title"), TextRef.Of("load.warning.changedData"));
        }
    }

    private void ShowMessage(TextRef title, TextRef body)
    {
        var window = new UiWindow("message", title) { SingleInstance = true };
        window.Content = new Label(body) { Wrap = true, Width = 380 };
        Ui.Windows.Open(window);
    }

    private void Notify(TextRef message)
    {
        if (_session is not null)
        {
            _session.Notify(message);
        }
        else
        {
            ShowMessage(TextRef.Of("load.title"), message);
        }
    }

    /// <summary>Graphics settings in use: the saved ones, or the preset given with --graphics for this run.</summary>
    public GraphicsSettings ActiveGraphics => _probe is { Finished: false } probe ? probe.Current : _graphicsOverride ?? Settings.Graphics;

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
        _audio = AudioSystem.TryCreate(Settings, AudioSystem.DefaultAssetRoot, Console.Error.WriteLine, _options.NoAudio);
        _ui.Activated = _audio.PlayInterfaceClick;
        _renderer.AttachUi(atlas);
        _models = ModelLibrary.LoadShipped();
        _renderer.AttachModels(_models);
        _modelBatch = new ModelBatch(_models.VariantCount);
        Console.WriteLine($"Models: {_models.KeyCount} keys, {_models.VariantCount} variants, {_models.Triangles:N0} triangles, loaded and meshed in {_models.LoadMilliseconds:F0} ms");
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

        if (_options.LoadPath is { } load)
        {
            LoadSave(load); // --load: straight into a save (feature 014).
        }
        else if (_options.StartGame is { } start)
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

        _saver.WaitForWrite(); // The last save of this game is on disk before it goes away.
        _session.SaveRecording();
        _session.CommandFeedback -= OnCommandFeedback;
        _session.Detach();
        _session = null;
        _audio?.ClearGame();
        Ui.Windows.CloseEverything();
        _renderer?.ClearWorld();
    }

    private void EnterGame(GeneratedGame game, SaveLoadResult? loaded = null)
    {
        // A loaded game is not recorded (spec assumption: recordings start from a new game); --print-fingerprint-at still works.
        var sessionOptions = _options.Session ?? new SessionOptions();
        _session = new GameSession(game, loaded is null ? sessionOptions : sessionOptions with { RecordPath = null }, Settings.FastForwardUnlocked);
        _session.ExitRequested += () => _window.Close();
        _session.CommandFeedback += OnCommandFeedback;
        _audio?.SetGame(game.Map);
        _session.TerrainChanged += OnTerrainEdited;

        var map = game.Heightmap;
        _camera.FitMap(Math.Max(map.Width, map.Depth));
        _camera.Distance = _options.InitialZoom ?? 350f;
        var center = _options.InitialCameraTarget ?? new Vector2(map.Width / 2f, map.Depth / 2f);
        _camera.Target = new Vector3(center.X, map.Get(center.X, center.Y), center.Y);
        if (loaded is not null)
        {
            // Feature 014 (FR-005): the camera where it was and the game paused.
            SaveController.TryReadCamera(loaded.ExtraSections, _camera);
            if (!_session.Runner.IsPaused)
            {
                _session.Runner.TogglePause();
            }
        }

        _session.MarkSaved(_session.Simulation.CurrentTick);
        _autosave.Reset();

        _renderer?.SetWorld(game.World);
        _renderer?.SetMap(game.Map);
        _renderer?.SetRoads(_session.Construction.Roads, game.Map, _session.Construction.RoadRules);
        _renderer?.SetRails(_session.Construction.Rails, game.Map, _session.Construction.RailRules);
        if (_session.Simulation.Towns is { } towns)
        {
            _renderer?.SetTowns(towns, game.Map);
        }
        _session.UseModels(_models);
        if (_options.MeasureEffectsSeconds is { } seconds && _probe is null)
        {
            // Measured without vertical sync; the saved setting is not changed.
            _probe = new EffectCostProbe(ActiveGraphics, seconds);
            _window.VSync = false;
            _renderer?.SetVerticalSync(false);
            Console.WriteLine($"Measuring effect costs: {seconds:F0} s per configuration...");
        }

        var screen = new InGameScreen(this, _session, _camera, _isKeyDown);
        screen.UseModels(_models);
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

    private void OnCommandFeedback(bool succeeded, bool construction) =>
        _audio?.PlayCommandFeedback(succeeded, construction);

    private void OnAction(string action, bool shift)
    {
        if (_screen is LoadingScreen || _screen?.HandleAction(action, shift) == true)
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
            case "quickSave":
                QuickSave();
                break;
            case "quickLoad":
                QuickLoad();
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

        if (_loading is { IsCompleted: true } loading)
        {
            _loading = null;
            if (loading.IsCompletedSuccessfully)
            {
                FinishLoading(loading.Result);
            }
            else
            {
                var message = loading.Exception?.GetBaseException().Message ?? "?";
                FinishLoading((SaveLoadResult.Failed(SaveLoadStatus.Damaged, message), null));
            }
        }

        ReportSave(_saver.Poll());
        _ui.Update(deltaSeconds);
        _screen?.Update(deltaSeconds);
        if (_session is not null && _loading is null)
        {
            var stats = _session.Advance(deltaSeconds);
            _ticksThisSecond += stats.TicksRun;

            // Feature 014: autosave after the frame's ticks, at a tick boundary (FR-011, FR-013).
            var menuOpen = _screen is InGameScreen { IsPauseMenuOpen: true };
            if (_autosave.Update(deltaSeconds, _session.Runner.IsPaused || menuOpen, Settings.AutosaveMinutes, _saver.IsWriting))
            {
                Autosave();
            }
        }

        if (_audio is not null)
        {
            UpdateAudio(deltaSeconds);
        }
    }

    /// <summary>
    /// Fills the frame's model batch (feature 018): vehicles, town buildings, industries, trees and decoration,
    /// culled and sorted by <see cref="ModelBatch"/>, then uploads it.
    /// </summary>
    private void BuildModelBatch(InGameScreen game)
    {
        if (_renderer is null || _session is null || _modelBatch is null)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var parameters = GraphicsPresets.Default.Resolve(ActiveGraphics);
        game.UpdateDecor(parameters.SceneryDensity);
        var batch = _modelBatch;
        batch.Begin(_camera.ViewProjection(_renderer.AspectRatio), _camera.Position,
            parameters.ModelLevel1Distance, parameters.ModelLevel2Distance, parameters.ModelLevel3Distance, _camera.Distance * 2.5f * 1.5f);
        _renderer.PrepareTowns(_camera);
        if (_probe is not { Finished: false, ModelsEnabled: false })
        {
            _session.AppendVehicleModels(batch);
            _session.AppendTrainModels(batch);
            _session.AppendIndustryModels(batch);
            _renderer.AppendTownModels(batch);
            foreach (var tree in game.TreeModels)
            {
                batch.Add(tree.Model, tree.Position, tree.Yaw, Vector3.One, tree.Variation);
            }

            foreach (var decor in game.DecorModels)
            {
                batch.Add(decor.Model, decor.Position, decor.Yaw, Vector3.One, decor.Variation);
            }
        }

        batch.End();
        _renderer.UpdateModels(batch);
        _modelBatchMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void UpdateAudio(double deltaSeconds)
    {
        var forward = _camera.Target - _camera.Position;
        var forwardLength = forward.Length();
        forward = forwardLength > 0.0001f ? forward / forwardLength : Vector3.UnitZ;
        var right = Vector3.Cross(Vector3.UnitY, forward);
        var rightLength = right.Length();
        right = rightLength > 0.0001f ? right / rightLength : Vector3.UnitX;
        var screen = _screen as InGameScreen;
        var selected = screen is not null && screen.Selected.Kind == PickKind.Vehicle ? screen.Selected.VehicleId : -1;
        var hovered = screen is not null && screen.Hovered.Kind == PickKind.Vehicle ? screen.Hovered.VehicleId : -1;
        var vehicles = _session is null ? default : _session.Instances;
        var ids = _session is null ? default : _session.InstanceIds;
        var scene = new AudioScene
        {
            InGame = _session is not null,
            Paused = _session?.Runner.IsPaused ?? false,
            CameraPosition = _camera.Position,
            CameraTarget = _camera.Target,
            CameraForward = forward,
            CameraRight = right,
            CameraDistance = _camera.Distance,
            Vehicles = vehicles,
            VehicleIds = ids,
            RoadVehicles = _session?.RoadVehicles,
            SelectedVehicleId = selected,
            HoveredVehicleId = hovered,
            Ambients = _audio!.Ambients,
            DeltaSeconds = (float)deltaSeconds,
        };
        _audio.Update(scene);
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
            _renderer.UpdateVehicles(_session.HasVehicleModels ? default : _session.Instances);
            _renderer.UpdateIndustries(_session.IndustryInstances);
        }

        var uiStart = Stopwatch.GetTimestamp();
        UpdateViewport();
        if (_screen is InGameScreen game)
        {
            // Picking pauses while the camera is dragged, but keeps running during a tool drag (feature 006).
            game.UpdatePicking(_router.CursorPosition, _ui.ViewportSize, _router.IsCursorOverWorld && (!_router.IsDraggingWorld || game.IsToolDragging));
            _renderer.SetHighlights(game.Highlights, _camera.Position);
            if (game.TakePreviewChange())
            {
                _renderer.UpdatePreview(game.PreviewBoxes);
            }

            if (game.UpdateTrees())
            {
                _renderer.UpdateTrees(game.TreeBoxes);
            }

            BuildModelBatch(game);
        }

        _ui.Layout();
        _ui.Draw(_drawList);
        _uiCpuMs = Stopwatch.GetElapsedTime(uiStart).TotalMilliseconds;

        // Managed allocations of the draw path (constitution II: none expected in steady state).
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var theme = _ui.Theme.Background;
        var graphics = ActiveGraphics;
        _renderer.SetGraphics(GraphicsPresets.Default.Resolve(graphics));
        _timeOfDay.Advance(deltaSeconds, _session?.Runner.IsPaused ?? true);
        _renderer.SetLight(graphics.DayNight || _options.DayNightOverride ? _timeOfDay.Light : LightState.Day);
        _renderer.SetTime(_realTime.Elapsed.TotalSeconds);
        _renderer.Draw(_camera, _drawList, SrgbToLinear(theme));
        _renderAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _renderAllocationFrames++;
        _uiMsSum += _uiCpuMs + _renderer.LastUiMilliseconds;
        var timings = _renderer.LastDrawTimings;
        _prepareMsSum += timings.Prepare;
        _encodeMsSum += timings.Encode;
        _waitMsSum += timings.WaitGpu;
        _batchMsSum += _modelBatchMs;
        _timingFrames++;
        _uiMsFrames++;

        var now = Stopwatch.GetTimestamp();
        if (_lastFrameTimestamp != 0)
        {
            var frameMs = Stopwatch.GetElapsedTime(_lastFrameTimestamp, now).TotalMilliseconds;
            _frameTimes.Record(frameMs);
            if (_probe is not null && _session is not null && _probe.Record(deltaSeconds, frameMs))
            {
                var map = _session.Game.Map;
                Console.WriteLine(_probe.Report($"{map.Width} x {map.Depth} map, camera distance {_camera.Distance:F0}, {_renderer.AdapterDescription}"));
                _window.Close();
            }
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
        var audio = _audio?.Statistics ?? default;
        var statistics = string.Join(" — ",
            s.FormatSpeed(speed),
            s.Format("status.effectiveSpeed", ("speed", Math.Round(effective, 1))),
            s.Format("stats.frame", ("average", Math.Round(_frameTimes.AverageMs, 1)), ("worst", Math.Round(_frameTimes.WorstMs, 1))),
            s.Format("status.fps", ("fps", Math.Round(_frameTimes.FramesPerSecond))),
            s.Format("stats.ui", ("ms", Math.Round(_uiAverageMs, 2)), ("windows", Ui.Windows.Count)),
            s.Format("stats.audio", ("ms", Math.Round(audio.AverageUpdateMilliseconds, 3)), ("voices", audio.ActiveVoices)),
            s.Format("stats.draws", ("count", r.DrawCalls)),
            s.Format("stats.triangles", ("count", r.Triangles)),
            s.Format("stats.roads", ("draws", _renderer.RoadStatistics.Draws), ("triangles", _renderer.RoadStatistics.Triangles), ("mb", _renderer.RoadStatistics.Bytes / (1024 * 1024))),
            s.Format("stats.towns", ("draws", _renderer.TownStatistics.Draws), ("boxes", _renderer.TownStatistics.Instances), ("mb", _renderer.TownStatistics.Bytes / (1024 * 1024))),
            s.Format("stats.regions", ("lod0", r.RegionsLod0), ("lod1", r.RegionsLod1), ("lod2", r.RegionsLod2), ("lod3", r.RegionsLod3)),
            s.Format("stats.memory", ("mb", r.WorldGpuBytes / (1024 * 1024)), ("released", r.Lod0Released)),
            s.Format("stats.jobs", ("count", r.PendingJobs + r.JobsInFlight)),
            s.Format("stats.allocated", ("bytes", (long)_allocatedBytesPerFrame)),
            GraphicsStatistics(s));
        _prepareMsSum = _encodeMsSum = _waitMsSum = _batchMsSum = 0;
        _timingFrames = 0;
        _window.Title = statistics;
        Console.WriteLine(statistics);
    }

    /// <summary>Graphics part of the statistics (feature 018, FR-019).</summary>
    private string GraphicsStatistics(Strings s)
    {
        var frames = Math.Max(1, _timingFrames);
        var graphics = ActiveGraphics;
        var shadow = _renderer!.ShadowStatistics;
        var models = _renderer.ModelStatistics;
        var levels = _modelBatch?.PerLevel ?? default;
        return string.Join(" — ",
            s.Format("stats.graphics",
                ("preset", s.Get("settings.graphics.preset." + graphics.Preset.ToString().ToLowerInvariant())),
                ("cascades", shadow.Cascades), ("size", shadow.Size), ("shadowTriangles", shadow.Triangles), ("samples", _renderer.SampleCount)),
            s.Format("stats.models",
                ("draws", models.Draws), ("instances", models.Instances),
                ("levels", $"{levels.Level0}/{levels.Level1}/{levels.Level2}/{levels.Level3}"),
                ("triangles", models.Triangles), ("ms", Math.Round(_batchMsSum / frames, 2))),
            s.Format("stats.cpu",
                ("prepare", Math.Round(_prepareMsSum / frames, 2)), ("encode", Math.Round(_encodeMsSum / frames, 2)), ("wait", Math.Round(_waitMsSum / frames, 2))));
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
