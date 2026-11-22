using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Talt.Client.Rendering;
using Talt.Core.Meshing;
using Talt.Core.Simulation;
using Talt.Core.Terrain;

namespace Talt.Client;

/// <summary>Everything the prototype window needs to show.</summary>
internal sealed record PrototypeScene(
    IReadOnlyList<ChunkMesh> Meshes,
    Heightmap Heightmap,
    VehicleFleet Fleet,
    string Description);

/// <summary>
/// Owns the window, the renderer, the camera controls and the simulation loop.
/// Controls: left drag rotates, right drag pans, wheel zooms, WASD/arrows pan, Q/E rotate,
/// Space pauses, +/- change simulation speed, Esc quits.
/// </summary>
internal sealed class GameWindow : IDisposable
{
    private const float KeyboardPanSpeed = 1.2f;    // Fraction of the camera distance per second.
    private const float KeyboardRotateSpeed = 1.5f; // Radians per second.
    private const float MouseRotateSpeed = 0.005f;  // Radians per pixel.
    private const float CameraClearance = 4f;       // Minimum height of the camera above the ground.
    private const float LaneOffset = 0.22f;         // Vehicles drive on the right half of the road.

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
    private readonly VehiclePose[] _poses;
    private readonly VehicleInstance[] _instances;
    private readonly Stopwatch _fpsTimer = new();
    private readonly Stopwatch _tickTimer = new();

    private WebGpuRenderer? _renderer;
    private IInputContext? _input;
    private Vector2? _lastMouse;
    private int _frames;
    private double _tickMilliseconds;
    private int _ticks;
    private float _simulationSpeed = 1f;
    private bool _paused;

    public GameWindow(PrototypeScene scene)
    {
        _scene = scene;
        _poses = new VehiclePose[scene.Fleet.VehicleCount];
        _instances = new VehicleInstance[scene.Fleet.VehicleCount];

        var center = new Vector2(scene.Heightmap.Width / 2f, scene.Heightmap.Depth / 2f);
        _camera.Target = new Vector3(center.X, scene.Heightmap.Get(center.X, center.Y), center.Y);

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

        _fpsTimer.Start();
    }

    private void OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                _window.Close();
                break;
            case Key.Space:
                _paused = !_paused;
                break;
            case Key.KeypadAdd or Key.Equal:
                _simulationSpeed = MathF.Min(_simulationSpeed * 2f, 16f);
                break;
            case Key.KeypadSubtract or Key.Minus:
                _simulationSpeed = MathF.Max(_simulationSpeed / 2f, 0.25f);
                break;
        }
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

        if (!_paused)
        {
            // Clamp long frames (window dragged, debugger) so vehicles do not teleport.
            var step = MathF.Min(dt, 0.1f) * _simulationSpeed;
            _tickTimer.Restart();
            _scene.Fleet.Tick(step);
            _tickMilliseconds += _tickTimer.Elapsed.TotalMilliseconds;
            _ticks++;
        }

        UploadVehicles();
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

        var map = _scene.Heightmap;
        _scene.Fleet.CopyPoses(_poses);
        for (var v = 0; v < _poses.Length; v++)
        {
            var (position, direction) = _poses[v];
            var right = new Vector2(-direction.Y, direction.X);
            var p = position + right * LaneOffset;
            var alongZ = MathF.Abs(direction.Y) > MathF.Abs(direction.X) ? 1f : 0f;
            _instances[v] = new VehicleInstance(
                new Vector4(p.X, map.Get(position.X, position.Y), p.Y, alongZ),
                VehicleColors[v % VehicleColors.Length]);
        }

        _renderer.UpdateVehicles(_instances);
    }

    private void OnRender(double deltaSeconds)
    {
        _renderer?.Draw(_camera);

        _frames++;
        if (_fpsTimer.Elapsed.TotalSeconds >= 1)
        {
            var fps = _frames / _fpsTimer.Elapsed.TotalSeconds;
            var tick = _ticks > 0 ? _tickMilliseconds / _ticks : 0;
            var state = _paused ? "paused" : $"x{_simulationSpeed:0.##}";
            _window.Title = $"Transit & Logistics Tycoon — {fps:F0} FPS — {_scene.Description} — " +
                $"{_scene.Fleet.VehicleCount:N0} vehicles, tick {tick:F2} ms ({state})";
            _frames = 0;
            _ticks = 0;
            _tickMilliseconds = 0;
            _fpsTimer.Restart();
        }
    }

    private static float Axis(IKeyboard keyboard, Key primary, Key secondary) =>
        keyboard.IsKeyPressed(primary) || keyboard.IsKeyPressed(secondary) ? 1f : 0f;
}
