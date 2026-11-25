using System.Numerics;
using Silk.NET.Input;
using Talt.Client.Construction;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Rendering;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Simulation;
using Button = Talt.Client.Ui.Widgets.Button;

namespace Talt.Client.Screens;

/// <summary>
/// The game: top bar (money, date, speed controls), toolbar, notifications, pause menu, map picking
/// with hover and selection highlights, object information windows and camera control (US1, US2).
/// </summary>
internal sealed class InGameScreen : IScreen, IWorldInput
{
    private const float KeyboardPanSpeed = 1.2f;    // Fraction of the camera distance per second.
    private const float KeyboardRotateSpeed = 1.5f; // Radians per second.
    private const float KeyboardZoomSpeed = 1.8f;   // Zoom factor per second.
    private const float MouseRotateSpeed = 0.005f;  // Radians per pixel.
    private const float CameraClearance = 4f;       // Minimum height of the camera above the ground.
    private const double MessageSeconds = 4;
    private const int MaxMessages = 4;

    private readonly IScreenHost _host;
    private readonly GameSession _session;
    private readonly OrbitCamera _camera;
    private readonly Func<Key, bool> _isKeyDown;
    private readonly MapPicker _picker = new();
    private readonly PickContext _pickContext;
    private readonly TownLabels? _labels;
    private readonly HighlightBox[] _highlights = new HighlightBox[2];
    private readonly StackPanel _messages = new(Orientation.Vertical) { Spacing = 4 };
    private readonly List<double> _messageExpiry = [];
    private readonly Panel _pauseMenu;
    private readonly ToolController _tools = new();
    private readonly PreviewBoxes _previewBoxes = new();
    private readonly TreeInstances _trees = new();
    private readonly DecorScatter _decor = new();
    private readonly Panel _cursorLabel;
    private readonly Talt.Core.Stations.CatchmentPreview _catchment = new();
    private readonly List<int> _highlightTiles = [];
    private int _uploadedPreviewVersion = -1;
    private int _boxesVersion;
    private (int Station, int Version, int Highlight) _highlightKey = (-1, -1, -1);
    private int _highlightCount;
    private PickResult _selected = PickResult.None;
    private PickResult _hoveredAtPress = PickResult.None;
    private bool _rotating;
    private bool _panning;
    private bool _pausedByMenu;

    public InGameScreen(IScreenHost host, GameSession session, OrbitCamera camera, Func<Key, bool> isKeyDown)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _isKeyDown = isKeyDown ?? throw new ArgumentNullException(nameof(isKeyDown));
        _pickContext = new PickContext
        {
            Heightmap = session.Heightmap,
            World = session.World,
            Industries = session.Simulation.Industries,
            Roads = session.Construction.Roads,
            Towns = session.Simulation.Towns,
            Stations = session.Simulation.Stations,
        };
        _session.Message += ShowMessage;
        if (session.Simulation.Towns is { Count: > 0 } towns)
        {
            _labels = new TownLabels(towns, session.Heightmap);
        }

        _cursorLabel = BuildCursorLabel();
        Root = BuildHud();
        _pauseMenu = BuildPauseMenu();
    }

    public Widget Root { get; }

    public bool ShowsWorld => true;

    /// <summary>The object under the cursor this frame.</summary>
    public PickResult Hovered { get; private set; } = PickResult.None;

    public PickResult Selected => _selected;

    public bool IsPauseMenuOpen => _host.Ui.Overlay == _pauseMenu;

    /// <summary>Boxes to outline this frame (hover and selection).</summary>
    public ReadOnlySpan<HighlightBox> Highlights => _highlights.AsSpan(0, _highlightCount);

    public GameSession Session => _session;

    /// <summary>The on-screen notifications (for tests).</summary>
    internal StackPanel Messages => _messages;

    /// <summary>The construction tool and its preview (feature 006).</summary>
    internal ToolController Tools => _tools;

    /// <summary>True while a tool drag is in progress (picking must keep running then).</summary>
    public bool IsToolDragging => _tools.IsDragging;

    /// <summary>Ghost preview boxes of the current tool (feature 006).</summary>
    public ReadOnlySpan<BoxInstance> PreviewBoxes => _previewBoxes.Boxes;

    /// <summary>Trees near the camera.</summary>
    public ReadOnlySpan<BoxInstance> TreeBoxes => _trees.Boxes;

    /// <summary>Returns true once each time the preview boxes changed (upload them then).</summary>
    public bool TakePreviewChange()
    {
        if (_uploadedPreviewVersion == _boxesVersion)
        {
            return false;
        }

        _uploadedPreviewVersion = _boxesVersion;
        return true;
    }

    /// <summary>Trees drawn as models (feature 018).</summary>
    public ReadOnlySpan<ModelPlacement> TreeModels => _trees.Placements;

    /// <summary>Bushes and rocks around the camera (feature 018).</summary>
    public ReadOnlySpan<ModelPlacement> DecorModels => _decor.Placements;

    /// <summary>Uses voxel models for trees and decoration.</summary>
    public void UseModels(ModelLibrary? library)
    {
        _trees.UseModels(library);
        _decor.UseModels(library);
    }

    /// <summary>Rebuilds the bushes and rocks around the camera target when needed.</summary>
    public void UpdateDecor(float density) =>
        _decor.Update(_session.Construction.Map, _session.Construction.Objects, _camera.Target, _session.SceneryVersion, density);

    /// <summary>Rebuilds the trees around the camera target when needed; true when they changed.</summary>
    public bool UpdateTrees() => _trees.Update(_session.Construction.Map, _camera.Target, _session.SceneryVersion);

    /// <summary>Buttons of the top bar and toolbar (for tests and tooltips checks).</summary>
    internal List<Button> HudButtons { get; } = [];

    public void Update(double deltaSeconds)
    {
        var dt = (float)deltaSeconds;
        if (!_host.Ui.HasKeyboardFocus && !IsPauseMenuOpen)
        {
            var bindings = _host.Bindings;
            var right = Held(bindings, "panRight") - Held(bindings, "panLeft");
            var forward = Held(bindings, "panForward") - Held(bindings, "panBack");
            var rotate = Held(bindings, "rotateRight") - Held(bindings, "rotateLeft");
            var zoom = Held(bindings, "zoomOut") - Held(bindings, "zoomIn");
            var pan = _camera.Distance * KeyboardPanSpeed * dt;
            _camera.Pan(right * pan, forward * pan);
            _camera.Yaw += rotate * KeyboardRotateSpeed * dt;
            if (zoom != 0)
            {
                _camera.Distance *= MathF.Pow(KeyboardZoomSpeed, zoom * dt);
            }
        }

        FollowTerrain(dt);
        ExpireMessages();
    }

    /// <summary>Finds the object under the cursor and prepares the highlight boxes (call after the vehicles are built).</summary>
    public void UpdatePicking(Vector2 cursor, Vector2 viewport, bool cursorOverWorld)
    {
        _pickContext.Vehicles = _session.InstanceBuffer;
        _pickContext.VehicleIds = _session.InstanceIdBuffer;
        _pickContext.VehicleCount = _session.Instances.Length;
        Hovered = PickResult.None;
        if (cursorOverWorld && viewport.X >= 1 && viewport.Y >= 1 && !IsPauseMenuOpen)
        {
            var viewProjection = _camera.ViewProjection(viewport.X / viewport.Y);
            var ray = Ray.FromScreen(viewProjection, cursor, viewport);
            Hovered = _picker.Pick(ray, _pickContext);
            var labelTown = _labels?.TownUnder(cursor) ?? -1;
            if (labelTown >= 0)
            {
                Hovered = TownPick(labelTown); // A town's plate is in front of the world.
            }
        }

        if (_labels is not null && viewport.X >= 1 && viewport.Y >= 1)
        {
            _labels.Update(_camera.ViewProjection(viewport.X / viewport.Y), viewport, _camera.Position, _camera.Distance * 2.5f);
        }

        UpdateTool(cursor, cursorOverWorld && !IsPauseMenuOpen);
        FollowSelectedVehicle();
        FollowSelectedIndustry();
        FollowSelectedTown();
        _highlightCount = 0;
        var theme = _host.Ui.Theme;
        if (_selected.Kind != PickKind.None)
        {
            _highlights[_highlightCount++] = new HighlightBox(_selected.BoundsMin, _selected.BoundsMax, theme.HighlightSelection);
        }

        if (Hovered.Kind != PickKind.None && !Hovered.SameObject(_selected))
        {
            _highlights[_highlightCount++] = new HighlightBox(Hovered.BoundsMin, Hovered.BoundsMax, theme.HighlightHover);
        }
    }

    public bool HandleAction(string actionId, bool shift)
    {
        var runner = _session.Runner;
        switch (actionId)
        {
            case "back":
                if (!_tools.Cancel())
                {
                    Back();
                }

                return true;
            case "toolRaise":
            case "toolLower":
            case "toolLevel":
            case "toolClear":
            case "toolRoad":
            case "toolBridge":
            case "toolTunnel":
            case "toolRemoveRoad":
            case "toolOneWay":
            case "toolMotorway":
            case "toolMotorwayExit":
            case "toolMotorwayEntry":
            case "toolBusStop":
            case "toolTruckStop":
            case "toolWarehouse":
            case "toolRemoveStation":
                ToggleTool(actionId);
                return true;
            case "warehouseSize":
                StationToolOptions.Cycle(_session.Simulation.Stations?.Rules ?? Talt.Core.Stations.StationRules.Default);
                return true;
            case "roadType":
                RoadToolOptions.Cycle(_session.Construction);
                return true;
            case "undoConstruction":
                _session.Submit(new UndoConstructionCommand(_session.Construction.Player.Id));
                return true;
            case "pause":
                if (IsPauseMenuOpen)
                {
                    ClosePauseMenu();
                }
                else
                {
                    runner.TogglePause();
                }

                return true;
            case "speedNormal":
                SetSpeed(GameSpeed.Normal);
                return true;
            case "speedFast":
                SetSpeed(GameSpeed.Fast);
                return true;
            case "speedVeryFast":
                SetSpeed(GameSpeed.VeryFast);
                return true;
            case "fastForward":
                if (!runner.ToggleFastForward())
                {
                    ShowMessage(TextRef.Of("hud.fastForward.locked"));
                }

                return true;
            case "addVehicle":
                _session.SubmitAddVehicle();
                return true;
            case "removeVehicle":
                _session.SubmitRemoveVehicle();
                return true;
            case "closeAllWindows":
                _host.Ui.Windows.CloseAll();
                return true;
            case "settings":
                _host.OpenSettings();
                return true;
            case "statistics":
                _host.ShowStatistics = !_host.ShowStatistics;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Esc: closes the front window, then clears the selection, then opens or closes the pause menu (FR-006).</summary>
    public void Back()
    {
        var ui = _host.Ui;
        if (ui.Popup is not null)
        {
            ui.ClosePopup();
        }
        else if (ui.Windows.CloseFront())
        {
        }
        else if (_selected.Kind != PickKind.None)
        {
            _selected = PickResult.None;
        }
        else if (IsPauseMenuOpen)
        {
            ClosePauseMenu();
        }
        else
        {
            OpenPauseMenu();
        }
    }

    public void OpenPauseMenu()
    {
        if (IsPauseMenuOpen)
        {
            return;
        }

        _pausedByMenu = !_session.Runner.IsPaused;
        if (_pausedByMenu)
        {
            _session.Runner.TogglePause();
        }

        _host.Ui.Overlay = _pauseMenu;
    }

    public void ClosePauseMenu()
    {
        if (!IsPauseMenuOpen)
        {
            return;
        }

        _host.Ui.Overlay = null;
        if (_pausedByMenu && _session.Runner.IsPaused)
        {
            _session.Runner.TogglePause(); // Restores the speed from before the menu.
        }

        _pausedByMenu = false;
    }

    /// <summary>Shows a short on-screen message that disappears after a few seconds (FR-007).</summary>
    public void ShowMessage(TextRef text)
    {
        if (_messages.ChildCount >= MaxMessages)
        {
            _messages.Remove(_messages.GetChild(0));
            _messageExpiry.RemoveAt(0);
        }

        // On a panel so the text stays readable over bright terrain (hand test of feature 006).
        var panel = new Panel { Style = PanelStyle.Panel, HorizontalAlignment = Alignment.Center, Padding = new Thickness(10, 4) };
        panel.Add(new Label(text) { HorizontalAlignment = Alignment.Center });
        _messages.Add(panel);
        _messageExpiry.Add(_host.Ui.Time + MessageSeconds);
    }

    /// <summary>Opens (or focuses) the information window of an object (FR-010).</summary>
    public void Select(PickResult pick)
    {
        _selected = pick;
        if (pick.Kind == PickKind.None)
        {
            return;
        }

        var window = ObjectInfoWindow.Create(pick, _session);
        var opened = _host.Ui.Windows.Open(window);
        if (opened == window)
        {
            window.Closed += closed =>
            {
                if (_selected.Kind != PickKind.None && _selected.Key == closed.Key)
                {
                    _selected = PickResult.None;
                }
            };
        }
    }

    public void Leave()
    {
        _session.Message -= ShowMessage;
        if (IsPauseMenuOpen)
        {
            _host.Ui.Overlay = null;
        }
    }

    void IWorldInput.MouseDown(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left && _tools.Active is not null && !IsPauseMenuOpen)
        {
            // With a tool, a left drag builds instead of rotating (US4); over the sky nothing happens.
            if (_pickContext.Terrain.Kind != PickKind.None)
            {
                _tools.Press((_pickContext.Terrain.TileX, _pickContext.Terrain.TileZ));
            }

            return;
        }

        if (button == MouseButton.Right && _tools.IsDragging)
        {
            _tools.Cancel(); // The right button cancels a drag (FR-012).
            return;
        }

        _rotating |= button == MouseButton.Left;
        _panning |= button == MouseButton.Right;
        if (button == MouseButton.Left)
        {
            // Picking pauses while a button is held (the camera may be dragged), so the click selects
            // what was under the cursor when the button went down. Found in the hand test.
            _hoveredAtPress = Hovered;
        }
    }

    void IWorldInput.Drag(Vector2 position, Vector2 delta)
    {
        if (_rotating)
        {
            _camera.Yaw -= delta.X * MouseRotateSpeed;
            _camera.Pitch += delta.Y * MouseRotateSpeed;
        }
        else if (_panning)
        {
            // Scale with distance so the ground follows the cursor roughly.
            var scale = _camera.Distance * 0.0015f;
            _camera.Pan(-delta.X * scale, delta.Y * scale);
        }
    }

    void IWorldInput.MouseUp(MouseButton button, Vector2 position, bool isClick)
    {
        if (button == MouseButton.Left && _tools.IsDragging)
        {
            // Releasing confirms (clarified): a valid action is submitted, an invalid one says why.
            var command = _tools.Release(_session.Construction, out var reason);
            if (command is not null)
            {
                _session.Submit(command);
            }
            else if (reason is not null)
            {
                var code = reason;
                ShowMessage(TextRef.Dynamic(() => 0, s => s.Format("command.rejected", ("reason", s.Get($"construction.reason.{code}")))));
            }

            return;
        }

        if (button == MouseButton.Left)
        {
            _rotating = false;
            if (isClick && !IsPauseMenuOpen && _tools.Active is null)
            {
                Select(_hoveredAtPress); // A click without movement selects (FR-012a); clicking the sky clears.
            }
        }
        else if (button == MouseButton.Right)
        {
            _panning = false;
        }
    }

    void IWorldInput.Scroll(float delta)
    {
        if (!IsPauseMenuOpen)
        {
            _camera.Distance *= MathF.Pow(0.88f, delta);
        }
    }

    private float Held(KeyBindings bindings, string action) =>
        bindings.IsHeld(action, _isKeyDown, _isKeyDown(Key.ShiftLeft) || _isKeyDown(Key.ShiftRight)) ? 1f : 0f;

    private void SetSpeed(GameSpeed speed)
    {
        if (IsPauseMenuOpen)
        {
            ClosePauseMenu();
        }

        _session.Runner.SetStandardSpeed(speed);
    }

    private void ExpireMessages()
    {
        var now = _host.Ui.Time;
        for (var i = _messageExpiry.Count - 1; i >= 0; i--)
        {
            var left = _messageExpiry[i] - now;
            if (left <= 0)
            {
                _messages.Remove(_messages.GetChild(i));
                _messageExpiry.RemoveAt(i);
            }
            else
            {
                ((Label)((Panel)_messages.GetChild(i)).GetChild(0)).Opacity = (float)Math.Min(1, left / 0.75); // The text fades out at the end.
            }
        }
    }

    private void FollowSelectedVehicle()
    {
        if (_selected.Kind != PickKind.Vehicle)
        {
            return;
        }

        var ids = _session.InstanceIds;
        var vehicles = _session.Instances;
        for (var v = 0; v < ids.Length; v++)
        {
            if (ids[v] == _selected.VehicleId)
            {
                var (min, max) = VehiclePickLayer.Box(vehicles[v], 1f);
                _selected = _selected with { BoundsMin = min, BoundsMax = max };
                return;
            }
        }

        _selected = PickResult.None; // The vehicle was removed.
    }

    /// <summary>A pick of a whole town (its plate): the highlight covers the town's centre area.</summary>
    private PickResult TownPick(int town)
    {
        var towns = _session.Simulation.Towns!;
        var (cx, cz) = towns.CentreOf(town);
        var radius = Math.Max(2, towns.RadiusOf(town) / 3);
        var ground = _session.Heightmap.Get(cx, cz);
        return new PickResult(PickKind.Town, cx, cz, ground, Talt.Core.Voxels.VoxelMaterial.Air, -1, -1,
            new Vector3(cx - radius, ground, cz - radius), new Vector3(cx + radius + 1, ground + 0.5f, cz + radius + 1), 0f, town);
    }

    /// <summary>Drops the highlight of a demolished town building (the town window stays open).</summary>
    private void FollowSelectedTown()
    {
        if (_selected.Kind != PickKind.Town || _session.Simulation.Towns is not { } towns)
        {
            return;
        }

        var (cx, cz) = towns.CentreOf(_selected.TownId);
        var isPlate = _selected.TileX == cx && _selected.TileZ == cz && towns.TownAt(cx, cz) != _selected.TownId;
        if (!isPlate && towns.TownAt(_selected.TileX, _selected.TileZ) != _selected.TownId)
        {
            _selected = PickResult.None;
        }
    }

    private void FollowSelectedIndustry()
    {
        if (_selected.Kind != PickKind.Industry)
        {
            return;
        }

        var industries = _session.Simulation.Industries;
        var slot = industries.SlotOf(_selected.IndustryId);
        if (slot < 0)
        {
            _selected = PickResult.None; // The industry closed.
            return;
        }

        if (!industries.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ))
        {
            _selected = PickResult.None;
            return;
        }

        var kind = industries.Content.IndustryKind(industries.KindIndexAt(slot));
        var ground = _session.Heightmap.Get(industries.XAt(slot), industries.ZAt(slot));
        _selected = _selected with
        {
            BoundsMin = new Vector3(minX, ground, minZ),
            BoundsMax = new Vector3(maxX, ground + kind.ModelHeight, maxZ),
        };
    }

    /// <summary>
    /// Keeps the camera target on the ground and inside the map, and the camera above the terrain,
    /// so the view never ends up inside a hill.
    /// </summary>
    private void FollowTerrain(float dt)
    {
        var map = _session.Heightmap;
        var target = _camera.Target;
        target.X = Math.Clamp(target.X, 0f, map.Width);
        target.Z = Math.Clamp(target.Z, 0f, map.Depth);
        var ground = map.Get(target.X, target.Z);
        target.Y += (ground - target.Y) * MathF.Min(1f, dt * 6f);
        _camera.Target = target;

        for (var i = 0; i < 40; i++)
        {
            var position = _camera.Position;
            var previousPitch = _camera.Pitch;
            if (position.Y >= map.Get(position.X, position.Z) + CameraClearance)
            {
                break;
            }

            _camera.Pitch += 0.02f;
            if (_camera.Pitch == previousPitch)
            {
                break; // Already looking straight down.
            }
        }
    }

    private Panel BuildHud()
    {
        var theme = _host.Ui.Theme;
        var runner = _session.Runner;
        var simulation = _session.Simulation;
        var root = new Panel { Style = PanelStyle.None };
        if (_labels is not null)
        {
            root.Add(_labels); // Town plates under every other HUD element (feature 008).
        }

        // Top bar: menu, money, date, speed controls (FR-004).
        var topBar = root.Add(new Panel { Style = PanelStyle.Panel, VerticalAlignment = Alignment.Start, Height = theme.TopBarHeight, Padding = new Thickness(6, 3) });
        var bar = topBar.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        HudButtons.Add(bar.Add(new Button(null, OpenPauseMenu, "menu") { Style = ButtonStyle.Flat, Tooltip = TextRef.Of("hud.menu.tooltip"), ShortcutAction = "back" }));
        bar.Add(new IconView("coin") { Tooltip = TextRef.Of("hud.money.tooltip") });
        var company = _session.Construction.Player;
        bar.Add(new Label(TextRef.Dynamic(
            () => company.Balance ^ ((long)_host.Currency.Code.GetHashCode(StringComparison.Ordinal) << 40),
            s => _host.Money.Format(company.Balance, _host.Currency, s.Culture)), TextSize.Title)
        {
            MinWidth = 110,
            Tooltip = TextRef.Of("hud.money.tooltip"),
        });
        bar.Add(new IconView("calendar") { Margin = new Thickness(12, 0, 0, 0), Tooltip = TextRef.Of("hud.date.tooltip") });
        bar.Add(new Label(TextRef.Dynamic(() => simulation.Date.DayNumber, s => s.FormatDate(simulation.Date)), TextSize.Title)
        {
            MinWidth = 110,
            Tooltip = TextRef.Of("hud.date.tooltip"),
        });
        bar.Add(new Spacer { Fill = true });
        bar.Add(new Label(TextRef.Dynamic(() => (long)runner.Speed, s => s.FormatSpeed(runner.Speed))) { Color = TextColor.Muted, Margin = new Thickness(0, 0, 8, 0) });
        HudButtons.Add(bar.Add(new Button(null, () => HandleAction("pause", false), "pause")
        {
            Style = ButtonStyle.Flat,
            IsSelected = () => runner.IsPaused,
            Tooltip = TextRef.Of("hud.pause.tooltip"),
            ShortcutAction = "pause",
        }));
        HudButtons.Add(bar.Add(SpeedButton("speed1", GameSpeed.Normal, "speedNormal", "hud.speedNormal.tooltip")));
        HudButtons.Add(bar.Add(SpeedButton("speed2", GameSpeed.Fast, "speedFast", "hud.speedFast.tooltip")));
        HudButtons.Add(bar.Add(SpeedButton("speed3", GameSpeed.VeryFast, "speedVeryFast", "hud.speedVeryFast.tooltip")));
        var fastForward = bar.Add(new Button(null, () => HandleAction("fastForward", false), "fastForward")
        {
            Style = ButtonStyle.Flat,
            IsSelected = () => runner.Speed == GameSpeed.FastForward,
            Tooltip = TextRef.Of("hud.fastForward.tooltip"),
            ShortcutAction = "fastForward",
        });
        HudButtons.Add(fastForward);
        FastForwardButton = fastForward;

        // Toolbar along the left edge (FR-005).
        var toolbar = root.Add(new Panel
        {
            Style = PanelStyle.Panel,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
            Width = theme.ToolbarWidth,
            Margin = new Thickness(6, theme.TopBarHeight + 8, 0, 0),
            Padding = new Thickness(4),
        });
        var tools = toolbar.Add(new StackPanel(Orientation.Vertical) { Spacing = 4 });
        HudButtons.Add(tools.Add(ToolButton("settings", () => _host.OpenSettings(), "toolbar.settings.tooltip", "settings")));
        HudButtons.Add(tools.Add(ToolButton("closeAll", () => _host.Ui.Windows.CloseAll(), "toolbar.closeAll.tooltip", "closeAllWindows")));
        HudButtons.Add(tools.Add(ToolButton("statistics", () => _host.ShowStatistics = !_host.ShowStatistics, "toolbar.statistics.tooltip", "statistics")));
        if (_session.Simulation.Vehicles.LineCount > 0)
        {
            // Prototype traffic exists only in map-less scenarios; games with a map have none until feature 012.
            HudButtons.Add(tools.Add(ToolButton("addVehicle", _session.SubmitAddVehicle, "toolbar.addVehicle.tooltip", "addVehicle")));
            HudButtons.Add(tools.Add(ToolButton("removeVehicle", _session.SubmitRemoveVehicle, "toolbar.removeVehicle.tooltip", "removeVehicle")));
        }

        // Construction tools: terrain (feature 006) in this column, roads (feature 007) in a second one.
        var roadToolbar = root.Add(new Panel
        {
            Style = PanelStyle.Panel,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
            Width = theme.ToolbarWidth,
            Margin = new Thickness(12 + theme.ToolbarWidth, theme.TopBarHeight + 8, 0, 0),
            Padding = new Thickness(4),
        });
        var roadTools = roadToolbar.Add(new StackPanel(Orientation.Vertical) { Spacing = 4 });
        // Station tools (feature 010) in a third column.
        var stationToolbar = root.Add(new Panel
        {
            Style = PanelStyle.Panel,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
            Width = theme.ToolbarWidth,
            Margin = new Thickness(18 + (2 * theme.ToolbarWidth), theme.TopBarHeight + 8, 0, 0),
            Padding = new Thickness(4),
        });
        var stationTools = stationToolbar.Add(new StackPanel(Orientation.Vertical) { Spacing = 4 });
        foreach (var tool in ConstructionTool.All)
        {
            var button = ToolButton(tool.Id, () => ToggleTool(tool.ActionId), $"tool.{tool.Id}.tooltip", tool.ActionId);
            button.IsSelected = () => _tools.Active == tool;
            var column = tool.IsStation ? stationTools : tool.Shape == DragShape.Area || tool == ConstructionTool.Clear ? tools : roadTools;
            HudButtons.Add(column.Add(button));
        }

        var stationRules = _session.Simulation.Stations?.Rules ?? Talt.Core.Stations.StationRules.Default;
        HudButtons.Add(stationTools.Add(new Button(null, () => HandleAction("warehouseSize", false), "warehouseSize")
        {
            Style = ButtonStyle.Flat,
            Tooltip = TextRef.Dynamic(
                () => StationToolOptions.Version,
                s => s.Format("tool.warehouseSize.tooltip", ("size", s.Get(stationRules.Warehouses[StationToolOptions.SizeIndex(stationRules)].NameKey)))),
            ShortcutAction = "warehouseSize",
            Height = 36,
        }));

        HudButtons.Add(tools.Add(ToolButton("undo", () => HandleAction("undoConstruction", false), "tool.undo.tooltip", "undoConstruction")));
        var typeButton = new Button(null, () => HandleAction("roadType", false), "roadType")
        {
            Style = ButtonStyle.Flat,
            Tooltip = TextRef.Dynamic(
                () => RoadToolOptions.Version + (_session.Construction.Year * 1000L),
                s => s.Format("tool.roadType.tooltip", ("type", s.Get($"road.type.{_session.Construction.RoadRules.Types[RoadToolOptions.TypeFor(_session.Construction)].Id}")))),
            ShortcutAction = "roadType",
            Height = 36,
        };
        HudButtons.Add(roadTools.Add(typeButton));

        // Notifications at the bottom centre (FR-007).
        _messages.HorizontalAlignment = Alignment.Center;
        _messages.VerticalAlignment = Alignment.End;
        _messages.Margin = new Thickness(0, 0, 0, 24);
        root.Add(_messages);
        root.Add(_cursorLabel);
        return root;
    }

    /// <summary>The fast-forward button, shown only when fast forward is unlocked (US1-4).</summary>
    internal Button FastForwardButton { get; private set; } = null!;

    /// <summary>Applies settings that change the HUD (fast-forward unlock).</summary>
    public void ApplySettings(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _session.Runner.SetFastForwardUnlocked(settings.FastForwardUnlocked);
        FastForwardButton.Visible = settings.FastForwardUnlocked;
    }

    private Button SpeedButton(string icon, GameSpeed speed, string action, string tooltip) =>
        new(null, () => HandleAction(action, false), icon)
        {
            Style = ButtonStyle.Flat,
            IsSelected = () => _session.Runner.Speed == speed,
            Tooltip = TextRef.Of(tooltip),
            ShortcutAction = action,
        };

    private static Button ToolButton(string icon, Action onClick, string tooltip, string action) =>
        new(null, onClick, icon)
        {
            Style = ButtonStyle.Flat,
            Tooltip = TextRef.Of(tooltip),
            ShortcutAction = action,
            Height = 36,
        };

    private void ToggleTool(string actionId)
    {
        ConstructionTool? chosen = null;
        foreach (var tool in ConstructionTool.All)
        {
            if (tool.ActionId == actionId)
            {
                chosen = tool;
            }
        }

        _tools.Select(_tools.Active == chosen ? null : chosen);
        if (_tools.Active is not null)
        {
            _selected = PickResult.None;
        }
    }

    /// <summary>Feeds the tile under the cursor to the tool, plans the preview and places the cost label.</summary>
    private void UpdateTool(Vector2 cursor, bool overWorld)
    {
        var terrain = _pickContext.Terrain;
        _tools.Hover(overWorld && terrain.Kind != PickKind.None ? (terrain.TileX, terrain.TileZ) : null);
        var state = _session.Construction;
        var stations = _session.Simulation.Stations;
        var highlightKey = (_session.CatchmentStationId, stations?.Version ?? 0, _session.CatchmentVersion);
        var toolChanged = _tools.UpdatePreview(state);
        if (toolChanged || (!_tools.HasPreview && highlightKey != _highlightKey))
        {
            _highlightKey = highlightKey;
            _boxesVersion++;
            _catchment.Reset();
            if (_tools.HasPreview)
            {
                _previewBoxes.Build(_tools.Preview, _tools.PreviewResult.Succeeded, state.Map, _host.Ui.Theme, _tools.Active?.Removes == true);
                if (stations is not null && _tools.Active is { IsStation: true, Removes: false } tool && _tools.Preview.HasArea)
                {
                    // Feature 010 (FR-011): the catchment and the cargo the placement would receive and accept.
                    var area = _tools.Preview.Area;
                    var warehouse = tool == ConstructionTool.Warehouse;
                    var kind = tool == ConstructionTool.BusStop ? Talt.Core.Stations.StationPieceKind.Bus : Talt.Core.Stations.StationPieceKind.Truck;
                    var type = warehouse
                        ? StationToolOptions.SizeIndex(stations.Rules)
                        : Talt.Core.Stations.StationPlanner.ChoosePiece(state, kind, area.X0, area.Z0);
                    stations.PreviewCatchment(state.Player.Id, warehouse, type, area.X0, area.Z0, _catchment);
                    _previewBoxes.AppendArea(_catchment.Tiles, state.Map, _host.Ui.Theme);
                }
            }
            else
            {
                _previewBoxes.Clear();
                if (stations is not null && _session.CatchmentStationId >= 0)
                {
                    _highlightTiles.Clear();
                    stations.CatchmentTiles(_session.CatchmentStationId, _highlightTiles);
                    _previewBoxes.AppendArea(_highlightTiles, state.Map, _host.Ui.Theme);
                }
            }
        }

        _cursorLabel.Visible = _tools.HasPreview && overWorld;
        var scale = _host.Ui.Scale;
        _cursorLabel.Margin = new Thickness((cursor.X / scale) + 18, (cursor.Y / scale) + 18, 0, 0);
    }

    private Panel BuildCursorLabel()
    {
        var panel = new Panel
        {
            Style = PanelStyle.Panel,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
            Padding = new Thickness(6, 3),
            Visible = false,
        };
        panel.Add(new Label(TextRef.Dynamic(
            () => _tools.PreviewVersion ^ ((long)_host.Currency.Code.GetHashCode(StringComparison.Ordinal) << 32),
            ComposeCursorText)));
        return panel;
    }

    /// <summary>"Cost: $1,250" and, when refused, the reason (US1).</summary>
    private string ComposeCursorText(Strings s)
    {
        var plan = _tools.Preview;
        var cost = s.Format("construction.cost", ("cost", _host.Money.Format(plan.Cost, _host.Currency, s.Culture)));
        if (plan.RatingChange != 0)
        {
            // Feature 008: demolishing town property or trees lowers the towns' rating of the company.
            cost = s.Format("construction.costAndRating", ("cost", cost), ("change", plan.RatingChange.ToString(s.Culture)));
        }

        var reason = _tools.PreviewResult.ReasonCode;
        var text = _tools.PreviewResult.Succeeded || reason is null
            ? cost
            : s.Format("construction.costAndReason", ("cost", cost), ("reason", s.Get($"construction.reason.{reason}")));
        if (_tools.Active is { IsStation: true, Removes: false } && _tools.Active != ConstructionTool.Warehouse)
        {
            // Feature 010 (US1): what the station would receive and accept.
            var content = _session.Simulation.Content;
            // One line: the cursor label does not wrap, so '\n' would show as an unknown glyph (hand test 2026-11-25).
            text += " · " + s.Format("station.preview.receives", ("cargos", StationWindow.CargoList(s, content, _catchment.Receives)))
                + " · " + s.Format("station.preview.accepts", ("cargos", StationWindow.CargoList(s, content, _catchment.Accepts)));
        }

        return text;
    }

    private Panel BuildPauseMenu()
    {
        var overlay = new Panel { Style = PanelStyle.Overlay };
        var panel = overlay.Add(new Panel { Style = PanelStyle.Panel, HorizontalAlignment = Alignment.Center, VerticalAlignment = Alignment.Center, Width = 320, Padding = new Thickness(18) });
        var column = panel.Add(new StackPanel(Orientation.Vertical) { Spacing = 10 });
        column.Add(new Label(TextRef.Of("pause.title"), TextSize.Heading) { TextAlignment = Alignment.Center });
        column.Add(new Button(TextRef.Of("pause.resume"), ClosePauseMenu) { Style = ButtonStyle.Menu, TextSize = TextSize.Title, ShortcutAction = "back", Tooltip = TextRef.Of("pause.resume.tooltip") });
        column.Add(new Button(TextRef.Of("pause.settings"), _host.OpenSettings) { Style = ButtonStyle.Menu, TextSize = TextSize.Title });
        column.Add(new Button(TextRef.Of("pause.mainMenu"), _host.ShowMainMenu) { Style = ButtonStyle.Menu, TextSize = TextSize.Title, Tooltip = TextRef.Of("pause.mainMenu.tooltip") });
        column.Add(new Button(TextRef.Of("pause.quit"), _host.Quit) { Style = ButtonStyle.Menu, TextSize = TextSize.Title });
        return overlay;
    }
}
