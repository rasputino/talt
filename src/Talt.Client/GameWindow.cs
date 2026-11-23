using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Talt.Client.Localization;
using Talt.Client.Rendering;
using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Simulation;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client;

/// <summary>Everything the prototype window needs.</summary>
internal sealed record PrototypeScene(
    VoxelWorld World,
    Heightmap Heightmap,
    GameSimulation Simulation,
    Strings Strings,
    PlayerSettings Settings,
    SessionRecorder? Recorder = null,
    string? RecordPath = null,
    long? PrintFingerprintAtTick = null,
    bool ExitAfterFingerprint = false,
    bool ShowStatistics = false,
    float? InitialZoom = null);

/// <summary>
/// Owns the window, the renderer, the camera controls and the fixed-step simulation loop.
/// </summary>
/// <remarks>
/// Controls: left drag rotates, right drag pans, wheel zooms, WASD/arrows pan, Q/E rotate,
/// Space pauses, 1/2/3 select ×1/×3/×8, F toggles fast forward (only when unlocked in the
/// settings), N adds a vehicle, Delete removes the last vehicle you added, F3 shows render
/// statistics, PageUp/PageDown raise/lower the tile under the camera target (Shift: a 32 x 32 area),
/// Esc quits.
/// </remarks>
internal sealed class GameWindow : IDisposable
{
    private const float KeyboardPanSpeed = 1.2f;    // Fraction of the camera distance per second.
    private const float KeyboardRotateSpeed = 1.5f; // Radians per second.
    private const float MouseRotateSpeed = 0.005f;  // Radians per pixel.
    private const float CameraClearance = 4f;       // Minimum height of the camera above the ground.
    private const float LaneOffset = 0.22f;         // Vehicles drive on the right half of the road.
    private const double MessageSeconds = 4;
    private const double LongFrameMilliseconds = 33.4; // Two refreshes at 60 Hz.
    private const int EditAreaSize = 32;            // Shift + PageUp/PageDown edits this many tiles per side.

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
    private readonly FrameTimeTracker _frameTimes = new();
    private readonly List<(int X, int Y, int Z)> _editedVoxels = [];

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
    private bool _showStatistics;
    private long _lastFrameTimestamp;
    private long _renderAllocatedBytes;
    private int _renderAllocationFrames;
    private double _allocatedBytesPerFrame;
    private double _updateMs;
    private double _simulationMs;
    private int _ticksThisFrame;
    private int _gen0;
    private int _gen1;
    private int _gen2;
    private TimeSpan _gcPause;

    public GameWindow(PrototypeScene scene)
    {
        _scene = scene;
        _simulation = scene.Simulation;
        _strings = scene.Strings;
        _runner.SetFastForwardUnlocked(scene.Settings.FastForwardUnlocked);
        _simulation.CommandProcessed += OnCommandProcessed;
        _buildInstancesBatch = BuildInstancesBatch;
        _showStatistics = scene.ShowStatistics;

        var map = scene.Heightmap;
        _camera.FitMap(Math.Max(map.Width, map.Depth));
        if (scene.InitialZoom is { } zoom)
        {
            _camera.Distance = zoom;
        }

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
        _renderer = new WebGpuRenderer(_window, _scene.World);
        Console.WriteLine($"GPU: {_renderer.AdapterDescription}");

        _input = _window.CreateInput();
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += (_, wheel) => _camera.Distance *= MathF.Pow(0.88f, wheel.Y);
        }

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (k, key, _) => OnKeyDown(k, key);
        }

        _statsTimer.Start();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key)
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
            case Key.F3:
                _showStatistics = !_showStatistics;
                break;
            case Key.PageUp:
            case Key.PageDown:
                EditTerrain(raise: key == Key.PageUp, area: keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
                break;
        }
    }

    /// <summary>
    /// Debug terrain edit at the camera target (FR-009). Terrain is not simulation state, so this does
    /// not go through the command queue and does not affect determinism.
    /// </summary>
    private void EditTerrain(bool raise, bool area)
    {
        if (_renderer is null)
        {
            return;
        }

        var timestamp = Stopwatch.GetTimestamp();
        var size = area ? EditAreaSize : 1;
        var x0 = (int)MathF.Floor(_camera.Target.X) - size / 2;
        var z0 = (int)MathF.Floor(_camera.Target.Z) - size / 2;
        _editedVoxels.Clear();
        for (var z = z0; z < z0 + size; z++)
        {
            for (var x = x0; x < x0 + size; x++)
            {
                _editedVoxels.AddRange(raise
                    ? TerrainEditor.Raise(_scene.World, _scene.Heightmap, x, z)
                    : TerrainEditor.Lower(_scene.World, _scene.Heightmap, x, z));
            }
        }

        if (_editedVoxels.Count > 0)
        {
            _renderer.NotifyVoxelsChanged(_editedVoxels, timestamp);
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

        var updateStart = Stopwatch.GetTimestamp();
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

        var simulationStart = Stopwatch.GetTimestamp();
        var stats = _runner.Advance(deltaSeconds, _simulation, _time, PromoteCurrentToPrevious, AfterEachTick);
        if (stats.TicksRun > 0)
        {
            CaptureCurrentPoses();
        }

        _ticksThisSecond += stats.TicksRun;
        _tickMilliseconds += stats.TickMilliseconds;
        _ticksThisFrame += stats.TicksRun;
        _simulationMs += Stopwatch.GetElapsedTime(simulationStart).TotalMilliseconds;
        _updateMs += Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds;
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
        var renderStart = Stopwatch.GetTimestamp();
        UploadVehicles(); // Once per drawn frame, so uploads never outpace submissions.
        var vehiclesMs = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;

        // Managed allocations of the draw path (constitution II: none expected in steady state).
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        _renderer?.Draw(_camera);
        _renderAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _renderAllocationFrames++;

        // Frame time = time between two presented frames.
        var now = Stopwatch.GetTimestamp();
        if (_lastFrameTimestamp != 0)
        {
            var frameMs = Stopwatch.GetElapsedTime(_lastFrameTimestamp, now).TotalMilliseconds;
            _frameTimes.Record(frameMs);
            if (_showStatistics && frameMs > LongFrameMilliseconds)
            {
                ReportLongFrame(frameMs, vehiclesMs);
            }
        }

        _lastFrameTimestamp = now;
        _updateMs = 0;
        _simulationMs = 0;
        _ticksThisFrame = 0;
        (_gen0, _gen1, _gen2, _gcPause) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());

        while (_renderer is not null && _renderer.TryTakeCompletedEdit(out var editTimestamp, out var chunks))
        {
            Console.WriteLine(_strings.Format("edit.visible",
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
            _tickMilliseconds = 0;
            _statsTimer.Restart();
        }
    }

    private void UpdateTitle(double seconds)
    {
        var s = _strings;
        _allocatedBytesPerFrame = _renderAllocationFrames > 0 ? _renderAllocatedBytes / (double)_renderAllocationFrames : 0;
        _renderAllocatedBytes = 0;
        _renderAllocationFrames = 0;
        var fps = _frames / seconds;
        var effective = _ticksThisSecond / seconds / GameClock.TicksPerSecondAtNormalSpeed;
        if (_showStatistics && _renderer is not null)
        {
            var statistics = StatisticsText(_renderer.Statistics, effective);
            _window.Title = statistics;
            Console.WriteLine(statistics);
            return;
        }

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

    /// <summary>Prints where a frame longer than the budget spent its time (shown with the statistics).</summary>
    private void ReportLongFrame(double frameMs, double vehiclesMs)
    {
        var d = _renderer!.LastDrawTimings;
        var pause = (GC.GetTotalPauseDuration() - _gcPause).TotalMilliseconds;
        Console.WriteLine(
            $"Long frame {frameMs:F1} ms: update {_updateMs:F1} (simulation {_simulationMs:F1}, {_ticksThisFrame} ticks), " +
            $"vehicles {vehiclesMs:F1}, acquire {d.Acquire:F1}, prepare {d.Prepare:F1}, encode {d.Encode:F1}, " +
            $"wait GPU {d.WaitGpu:F1}, submit {d.Submit:F1}; GC {GC.CollectionCount(0) - _gen0}/{GC.CollectionCount(1) - _gen1}/{GC.CollectionCount(2) - _gen2} " +
            $"(pause {pause:F1} ms)");
    }

    private string StatisticsText(RenderStatistics r, double effectiveSpeed)
    {
        var s = _strings;
        return string.Join(" — ",
            s.FormatSpeed(_runner.Speed),
            s.Format("status.effectiveSpeed", ("speed", Math.Round(effectiveSpeed, 1))),
            s.Format("stats.frame", ("average", Math.Round(_frameTimes.AverageMs, 1)), ("worst", Math.Round(_frameTimes.WorstMs, 1))),
            s.Format("status.fps", ("fps", Math.Round(_frameTimes.FramesPerSecond))),
            s.Format("stats.draws", ("count", r.DrawCalls)),
            s.Format("stats.triangles", ("count", r.Triangles)),
            s.Format("stats.regions", ("lod0", r.RegionsLod0), ("lod1", r.RegionsLod1), ("lod2", r.RegionsLod2), ("lod3", r.RegionsLod3)),
            s.Format("stats.memory", ("mb", r.WorldGpuBytes / (1024 * 1024)), ("released", r.Lod0Released)),
            s.Format("stats.jobs", ("count", r.PendingJobs + r.JobsInFlight)),
            s.Format("stats.allocated", ("bytes", (long)_allocatedBytesPerFrame)));
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
