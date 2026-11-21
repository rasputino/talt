using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Talt.Client.Rendering;
using Talt.Core.Meshing;

namespace Talt.Client;

/// <summary>
/// Owns the window, the renderer and the camera controls.
/// Controls: left drag rotates, right drag pans, wheel zooms, WASD/arrows pan, Q/E rotate, Esc quits.
/// </summary>
internal sealed class GameWindow : IDisposable
{
    private const float KeyboardPanSpeed = 1.2f;    // Fraction of the camera distance per second.
    private const float KeyboardRotateSpeed = 1.5f; // Radians per second.
    private const float MouseRotateSpeed = 0.005f;  // Radians per pixel.

    private readonly IWindow _window;
    private readonly OrbitCamera _camera = new();
    private readonly IReadOnlyList<ChunkMesh> _meshes;
    private readonly string _worldDescription;
    private readonly Stopwatch _fpsTimer = new();

    private WebGpuRenderer? _renderer;
    private IInputContext? _input;
    private Vector2? _lastMouse;
    private int _frames;

    public GameWindow(IReadOnlyList<ChunkMesh> meshes, Vector3 worldCenter, string worldDescription)
    {
        _meshes = meshes;
        _worldDescription = worldDescription;
        _camera.Target = worldCenter;

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
        _renderer.UploadMeshes(_meshes);
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
            keyboard.KeyDown += (_, key, _) =>
            {
                if (key == Key.Escape)
                {
                    _window.Close();
                }
            };
        }

        _fpsTimer.Start();
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
    }

    private void OnRender(double deltaSeconds)
    {
        _renderer?.Draw(_camera);

        _frames++;
        if (_fpsTimer.Elapsed.TotalSeconds >= 1)
        {
            var fps = _frames / _fpsTimer.Elapsed.TotalSeconds;
            _window.Title = $"Transit & Logistics Tycoon — {fps:F0} FPS — {_worldDescription} — {_renderer?.TriangleCount:N0} triangles";
            _frames = 0;
            _fpsTimer.Restart();
        }
    }

    private static float Axis(IKeyboard keyboard, Key primary, Key secondary) =>
        keyboard.IsKeyPressed(primary) || keyboard.IsKeyPressed(secondary) ? 1f : 0f;
}
