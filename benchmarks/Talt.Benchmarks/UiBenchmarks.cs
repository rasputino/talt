using System.Numerics;
using BenchmarkDotNet.Attributes;
using Talt.Client;
using Talt.Client.Audio;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Rendering;
using Talt.Client.Screens;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;

namespace Talt.Benchmarks;

/// <summary>
/// CPU cost of one interface frame (layout + draw list) for the in-game HUD with 0 and 20 sample
/// windows (feature 005 SC-001: 20 windows must add less than 1 ms per frame).
/// </summary>
[MemoryDiagnoser]
public class UiFrameBenchmarks
{
    private UiContext _ui = null!;
    private UiDrawList _list = null!;

    [Params(0, 20)]
    public int Windows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var atlas = new UiAtlas(1024, 1024);
        _ui = new UiContext(Theme.Load(), FontAtlas.Create(atlas), Icons.Load(atlas), atlas, Strings.Load("es"))
        {
            ViewportSize = new Vector2(1920, 1080),
        };
        var host = new BenchmarkHost(_ui);
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 4, VehicleCount: 100, Seed: 1));
        var screen = new InGameScreen(host, new GameSession(game, new SessionOptions(), false), new OrbitCamera(), _ => { }, _ => false);
        _ui.Screen = screen.Root;
        for (var i = 1; i <= Windows; i++)
        {
            _ui.Windows.Open(SampleWindows.Create(i));
        }

        _list = new UiDrawList();
        Frame(); // Warm up: texts resolved, arrays sized.
    }

    [Benchmark]
    public int Frame()
    {
        _ui.Update(1 / 60.0);
        _ui.Layout();
        _ui.Draw(_list);
        return _list.QuadCount;
    }

    private sealed class BenchmarkHost(UiContext ui) : IScreenHost
    {
        public UiContext Ui { get; } = ui;
        public PlayerSettings Settings { get; } = new();
        public KeyBindings Bindings { get; } = new(KeyBindings.LoadDefinitions());
        public MoneyFormatter Money { get; } = MoneyFormatter.Load();
        public AudioSystem Audio { get; } = AudioSystem.CreateSilent();
        public Currency Currency => Money.DefaultFor("es");
        public bool InGame => true;
        public bool ShowStatistics { get; set; }
        public void ShowMainMenu() { }
        public void ShowNewGame() { }
        public void StartGame(NewGameOptions options) { }
        public void OpenSettings() { }
        public void OpenKeyBindings() { }
        public void ApplySettings() { }
        public void Quit() { }
    }
}

/// <summary>
/// Picking cost per frame (feature 005 research R5): the terrain walk across a 4096 map at the
/// farthest zoom, and the test against 50 000 vehicles.
/// </summary>
public class PickingBenchmarks
{
    private PickContext _context = null!;
    private Ray _longRay;
    private Ray _downRay;
    private Ray _cameraRay;
    private readonly MapPicker _picker = new();

    [GlobalSetup]
    public void Setup()
    {
        // A flat 4096 x 4096 heightmap is enough: the walk visits the same columns whatever the heights.
        var world = new Talt.Core.Voxels.VoxelWorld();
        const int size = 4096;
        for (var z = 0; z < size; z += 32)
        {
            for (var x = 0; x < size; x += 32)
            {
                world.SetVoxel(x, 0, z, Talt.Core.Voxels.VoxelMaterial.Grass);
            }
        }

        var random = new Random(1);
        var vehicles = new VehicleInstance[50_000];
        for (var i = 0; i < vehicles.Length; i++)
        {
            vehicles[i] = new VehicleInstance(new Vector4(random.Next(size), 1, random.Next(size), random.Next(2)), Vector4.One);
        }

        _context = new PickContext
        {
            Heightmap = Talt.Core.Terrain.Heightmap.FromWorld(world, size, size),
            World = world,
            Vehicles = vehicles,
            VehicleIds = Enumerable.Range(0, vehicles.Length).ToArray(),
            VehicleCount = vehicles.Length,
        };

        // Grazing ray across the whole map (worst case for the walk) and a ray straight down.
        _longRay = new Ray(new Vector3(0.5f, 30, 0.5f), Vector3.Normalize(new Vector3(1, -0.0035f, 1)));
        _downRay = new Ray(new Vector3(2048.5f, 500, 2048.5f), -Vector3.UnitY);

        // The default in-game view: camera at distance 350, pitch 0.85, ray through the screen centre.
        var camera = new OrbitCamera { Target = new Vector3(2048, 1, 2048) };
        camera.FitMap(size);
        _cameraRay = Ray.FromScreen(camera.ViewProjection(16f / 9f), new Vector2(800, 450), new Vector2(1600, 900));
    }

    [Benchmark]
    public float TerrainAcrossTheMap() => TerrainPickLayer.Cast(_longRay, _context.Heightmap, _context.World).Distance;

    [Benchmark]
    public float FullPick50000Vehicles() => _picker.Pick(_downRay, _context).Distance;

    [Benchmark]
    public float FullPickFromTheDefaultCamera() => _picker.Pick(_cameraRay, _context).Distance;
}
