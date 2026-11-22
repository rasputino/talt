using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Talt.Client.Localization;
using Talt.Client.Rendering;
using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Meshing;
using Talt.Core.Simulation;
using Talt.Core.Terrain;

namespace Talt.Client;

/// <summary>Everything the prototype window needs.</summary>
internal sealed record PrototypeScene(
    IReadOnlyList<ChunkMesh> Meshes,
    Heightmap Heightmap,
    GameSimulation Simulation,
    Strings Strings,
    PlayerSettings Settings,
    SessionRecorder? Recorder = null,
    string? RecordPath = null,
    long? PrintFingerprintAtTick = null,
    bool ExitAfterFingerprint = false);

/// <summary>
/// Owns the window, the renderer, the camera controls and the fixed-step simulation loop.
/// </summary>
/// <remarks>
/// Controls: left drag rotates, right drag pans, wheel zooms, WASD/arrows pan, Q/E rotate,
/// Space pauses, 1/2/3 select ×1/×3/×8, F toggles fast forward (only when unlocked in the
/// settings), N adds a vehicle, Delete removes the last vehicle you added, Esc quits.
/// </remarks>
internal sealed class GameWindow : IDisposable
{
    private const float KeyboardPanSpeed = 1.2f;    // Fraction of the camera distance per second.
    private const float KeyboardRotateSpeed = 1.5f; // Radians per second.
    private const float MouseRotateSpeed = 0.005f;  // Radians per pixel.
    private const float CameraClearance = 4f;       // Minimum height of the camera above the ground.
    private const float LaneOffset = 0.22f;         // Vehicles drive on the right half of the road.
    private const double MessageSeconds = 4;

    private static readonly Vector4[] VehicleColors =
    [
        new(0.80f, 0.10f, 0.08f, 1f),
        new(0.95f, 0.60f, 0.05f, 1f),
        new(0.10f, 0.35f, 0.85f, 1f),
        new(0.95f, 0.95f, 0.95f, 1f),
        new(0.15f, 0.60f, 0.25f, 1f),
        new(0.55f, 0.15f, 0.65f, 1f),
    ];

    private readonly IWindow _window;
    private readonly OrbitCamera _camera = new();
    private readonly PrototypeScene _scene;
    private readonly GameSimulation _simulation;
    private readonly Strings _strings;
    private readonly SimulationRunner _runner = new();
    private readonly StopwatchTimeSource _time = new();
    private readonly Stopwatch _statsTimer = new();
    private readonly Stopwatch _messageTimer = new();
    private readonly Random _uiRandom = new(); // Never the simulation's generator (I1): choices travel inside commands.
    private readonly Stack<int> _playerAddedVehicles = new();

    // Two pose snapshots for interpolation (research R4), indexed by slot, with the ids in each slot.
    private VehiclePose[] _previousPoses = [];
    private int[] _previousIds = [];
    private int _previousCount;
    private VehiclePose[] _currentPoses = [];
    private int[] _currentIds = [];
    private int _currentCount;
    private long _currentCapturedAtTick = -1;
    private VehicleInstance[] _instances = [];
    private int _drawCount;
    private float _drawFactor;
    private readonly Action<int> _buildInstancesBatch;

    private WebGpuRenderer? _renderer;
    private IInputContext? _input;
    private Vector2? _lastMouse;
    private int _frames;
    private int _ticksThisSecond;
    private double _tickMilliseconds;
    private string _message = string.Empty;

    public GameWindow(PrototypeScene scene)
    {
        _scene = scene;
        _simulation = scene.Simulation;
        _strings = scene.Strings;
        _runner.SetFastForwardUnlocked(scene.Settings.FastForwardUnlocked);
        _simulation.CommandProcessed += OnCommandProcessed;
        _buildInstancesBatch = BuildInstancesBatch;

        var map = scene.Heightmap;
        var center = new Vector2(map.Width / 2f, map.Depth / 2f);
        _camera.Target = new Vector3(center.X, map.Get(center.X, center.Y), center.Y);

        CaptureCurrentPoses();
        CopyCurrentToPrevious();

        var options = WindowOptions.Default with
        {
            Title = "Transit & Logistics Tycoon",
            Size = new Vector2D<int>(1600, 900),
            API = GraphicsAPI.None, // WebGPU manages the surface itself.
            VSync = true,
        };
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += size => _renderer?.Resize(size);
    }

    public void Run() => _window.Run();

    public void Dispose()
    {
        _input?.Dispose();
        // Release GPU resources after the render loop has stopped, never during it.
        _renderer?.Dispose();
        _window.Dispose();
        SaveRecording();
    }

    private void OnLoad()
    {
        _renderer = new WebGpuRenderer(_window);
        _renderer.UploadMeshes(_scene.Meshes);
        Console.WriteLine($"GPU: {_renderer.AdapterDescription}");
        Console.WriteLine($"Uploaded {_renderer.TriangleCount:N0} triangles.");

        _input = _window.CreateInput();
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += (_, wheel) => _camera.Distance *= MathF.Pow(0.88f, wheel.Y);
        }

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => OnKeyDown(key);
        }

        _statsTimer.Start();
    }

    private void OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                _window.Close();
                break;
            case Key.Space:
                _runner.TogglePause();
                break;
            case Key.Number1:
                _runner.SetStandardSpeed(GameSpeed.Normal);
                break;
            case Key.Number2:
                _runner.SetStandardSpeed(GameSpeed.Fast);
                break;
            case Key.Number3:
                _runner.SetStandardSpeed(GameSpeed.VeryFast);
                break;
            case Key.F:
                _runner.ToggleFastForward(); // Ignored while locked (FR-007a).
                break;
            case Key.N:
                SubmitAddVehicle();
                break;
            case Key.Delete:
                SubmitRemoveVehicle();
                break;
        }
    }

    private void SubmitAddVehicle()
    {
        var fleet = _simulation.Vehicles;
        var line = _uiRandom.Next(fleet.LineCount);
        var speed = _uiRandom.Next(SyntheticTraffic.MinCruiseSpeed, SyntheticTraffic.MaxCruiseSpeed + 1);
        _simulation.Submit(new AddVehicleCommand(line, speed));
    }

    private void SubmitRemoveVehicle()
    {
        if (_playerAddedVehicles.TryPop(out var id))
        {
            _simulation.Submit(new RemoveVehicleCommand(id));
        }
    }

    private void OnCommandProcessed(CommandProcessed processed)
    {
        if (processed.Result.Succeeded && processed.Command is AddVehicleCommand)
        {
            _playerAddedVehicles.Push(_simulation.Vehicles.LastCreatedVehicleId);
        }

        _message = processed.Result.Succeeded
            ? _strings.Format("command.applied", ("command", _strings.Get($"command.{processed.Command.TypeName}")))
            : _strings.Format("command.rejected", ("reason", RejectionText(processed.Result.ReasonCode)));
        _messageTimer.Restart();
    }

    private string RejectionText(string? reasonCode)
    {
        var key = $"command.rejected.{reasonCode}";
        return _strings.Contains(key) ? _strings.Get(key) : _strings.Get("command.rejected.unknown");
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_lastMouse is { } last)
        {
            var delta = position - last;
            if (mouse.IsButtonPressed(MouseButton.Left))
            {
                _camera.Yaw -= delta.X * MouseRotateSpeed;
                _camera.Pitch += delta.Y * MouseRotateSpeed;
            }
            else if (mouse.IsButtonPressed(MouseButton.Right))
            {
                // Scale with distance so the ground follows the cursor roughly.
                var scale = _camera.Distance * 0.0015f;
                _camera.Pan(-delta.X * scale, delta.Y * scale);
            }
        }

        _lastMouse = position;
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_input is null)
        {
            return;
        }

        var dt = (float)deltaSeconds;
        foreach (var keyboard in _input.Keyboards)
        {
            var right = Axis(keyboard, Key.D, Key.Right) - Axis(keyboard, Key.A, Key.Left);
            var forward = Axis(keyboard, Key.W, Key.Up) - Axis(keyboard, Key.S, Key.Down);
            var rotate = Axis(keyboard, Key.E, Key.E) - Axis(keyboard, Key.Q, Key.Q);

            var pan = _camera.Distance * KeyboardPanSpeed * dt;
            _camera.Pan(right * pan, forward * pan);
            _camera.Yaw += rotate * KeyboardRotateSpeed * dt;
        }

        FollowTerrain(dt);

        var stats = _runner.Advance(deltaSeconds, _simulation, _time, PromoteCurrentToPrevious, AfterEachTick);
        if (stats.TicksRun > 0)
        {
            CaptureCurrentPoses();
        }

        _ticksThisSecond += stats.TicksRun;
        _tickMilliseconds += stats.TickMilliseconds;
        UploadVehicles();
    }

    /// <summary>
    /// Called right before the last tick of a frame: the current snapshot becomes "previous". When it
    /// is already up to date (the usual one-tick frame) the buffers are just swapped, no copying.
    /// </summary>
    private void PromoteCurrentToPrevious()
    {
        if (_currentCapturedAtTick != _simulation.CurrentTick)
        {
            CaptureCurrentPoses();
        }

        (_previousPoses, _currentPoses) = (_currentPoses, _previousPoses);
        (_previousIds, _currentIds) = (_currentIds, _previousIds);
        _previousCount = _currentCount;
    }

    private void AfterEachTick(GameSimulation simulation)
    {
        if (_scene.PrintFingerprintAtTick == simulation.CurrentTick)
        {
            Console.WriteLine($"fingerprint at tick {simulation.CurrentTick}: {StateHasher.Format(simulation.ComputeFingerprint())}");
            if (_scene.ExitAfterFingerprint)
            {
                _window.Close();
            }
        }
    }

    private void CaptureCurrentPoses()
    {
        var fleet = _simulation.Vehicles;
        EnsureCapacity(fleet.VehicleCount);
        fleet.CopyPoses(_currentPoses, parallel: true);
        fleet.CopyIds(_currentIds);
        _currentCount = fleet.VehicleCount;
        _currentCapturedAtTick = _simulation.CurrentTick;
    }

    private void CopyCurrentToPrevious()
    {
        EnsureCapacity(_currentCount);
        Array.Copy(_currentPoses, _previousPoses, _currentCount);
        Array.Copy(_currentIds, _previousIds, _currentCount);
        _previousCount = _currentCount;
    }

    private void EnsureCapacity(int count)
    {
        if (_currentPoses.Length >= count)
        {
            return;
        }

        var size = Math.Max(count, _currentPoses.Length * 2);
        Array.Resize(ref _previousPoses, size);
        Array.Resize(ref _previousIds, size);
        Array.Resize(ref _currentPoses, size);
        Array.Resize(ref _currentIds, size);
        Array.Resize(ref _instances, size);
    }

    /// <summary>
    /// Keeps the camera target on the ground and inside the map, and the camera above the terrain,
    /// so the view never ends up inside a hill.
    /// </summary>
    private void FollowTerrain(float dt)
    {
        var map = _scene.Heightmap;
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

    private void UploadVehicles()
    {
        if (_renderer is null)
        {
            return;
        }

        // Interpolate and build GPU instances in parallel batches (rendering only).
        _drawCount = _currentCount;
        _drawFactor = _runner.InterpolationFactor;
        const int batchSize = 4096;
        var batches = (_drawCount + batchSize - 1) / batchSize;
        if (batches > 1)
        {
            Parallel.For(0, batches, _buildInstancesBatch);
        }
        else if (batches == 1)
        {
            BuildInstancesBatch(0);
        }

        _renderer.UpdateVehicles(_instances.AsSpan(0, _drawCount));
    }

    private void BuildInstancesBatch(int batch)
    {
        const int batchSize = 4096;
        var map = _scene.Heightmap;
        var end = Math.Min((batch + 1) * batchSize, _drawCount);
        for (var v = batch * batchSize; v < end; v++)
        {
            // Only interpolate when the slot holds the same vehicle in both snapshots.
            var pose = v < _previousCount && _previousIds[v] == _currentIds[v]
                ? PoseInterpolator.Interpolate(_previousPoses[v], _currentPoses[v], _drawFactor)
                : _currentPoses[v];

            var (position, direction) = pose;
            var right = new Vector2(-direction.Y, direction.X);
            var p = position + right * LaneOffset;
            var alongZ = MathF.Abs(direction.Y) > MathF.Abs(direction.X) ? 1f : 0f;
            _instances[v] = new VehicleInstance(
                new Vector4(p.X, map.Get(position.X, position.Y), p.Y, alongZ),
                VehicleColors[_currentIds[v] % VehicleColors.Length]);
        }
    }

    private void OnRender(double deltaSeconds)
    {
        _renderer?.Draw(_camera);

        _frames++;
        var seconds = _statsTimer.Elapsed.TotalSeconds;
        if (seconds >= 1)
        {
            UpdateTitle(seconds);
            _frames = 0;
            _ticksThisSecond = 0;
            _tickMilliseconds = 0;
            _statsTimer.Restart();
        }
    }

    private void UpdateTitle(double seconds)
    {
        var s = _strings;
        var fps = _frames / seconds;
        var effective = _ticksThisSecond / seconds / GameClock.TicksPerSecondAtNormalSpeed;
        var tick = _ticksThisSecond > 0 ? _tickMilliseconds / _ticksThisSecond : 0;
        var parts = new List<string>
        {
            "Transit & Logistics Tycoon",
            s.FormatDate(_simulation.Date),
            s.FormatSpeed(_runner.Speed),
            s.Format("status.effectiveSpeed", ("speed", Math.Round(effective, 1))),
            s.Format("status.fps", ("fps", Math.Round(fps))),
            s.Format("status.vehicles", ("count", _simulation.Vehicles.VehicleCount)),
            s.Format("status.tick", ("ms", Math.Round(tick, 2))),
        };

        if (_messageTimer.IsRunning && _messageTimer.Elapsed.TotalSeconds < MessageSeconds)
        {
            parts.Add(_message);
        }

        _window.Title = string.Join(" — ", parts);
    }

    private void SaveRecording()
    {
        if (_scene.Recorder is null || _scene.RecordPath is null)
        {
            return;
        }

        _scene.Recorder.AddCheckpoint(_simulation);
        File.WriteAllText(_scene.RecordPath, _scene.Recorder.Session.ToJson());
        Console.WriteLine($"Session recorded to {Path.GetFullPath(_scene.RecordPath)} ({_simulation.CurrentTick} ticks).");
    }

    private static float Axis(IKeyboard keyboard, Key primary, Key secondary) =>
        keyboard.IsKeyPressed(primary) || keyboard.IsKeyPressed(secondary) ? 1f : 0f;
}
