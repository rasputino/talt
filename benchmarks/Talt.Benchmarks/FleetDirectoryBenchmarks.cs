using System.Numerics;
using BenchmarkDotNet.Attributes;
using Talt.Client;
using Talt.Client.Localization;
using Talt.Client.Screens;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;

namespace Talt.Benchmarks;

/// <summary>CPU cost to open and scroll the fleet directory with 50,000 actual service vehicles.</summary>
[MemoryDiagnoser]
public class FleetDirectoryBenchmarks
{
    private UiContext _ui = null!;
    private IVehicleUi _host = null!;
    private VirtualTable _table = null!;
    private readonly UiDrawList _draw = new();
    private int _direction = -1;

    [GlobalSetup]
    public void Setup()
    {
        var atlas = new UiAtlas(1024, 1024);
        _ui = new UiContext(Theme.Load(), FontAtlas.Create(atlas), Icons.Load(atlas), atlas, Strings.Load("en"))
        {
            ViewportSize = new Vector2(1920, 1080),
        };
        var session = new GameSession(GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 2, SeedVehicles: 4)),
            new SessionOptions(), false);
        session.Simulation.ProcessPendingCommands();
        var roads = session.RoadVehicles!;
        var depot = roads.Depots.IdAt(0);
        while (roads.Count < 50_000)
        {
            roads.Add(session.Construction.Player.Id, roads.ModelIndexAt(0), roads.CargoAt(0), depot, 0, session.Simulation.Date.DayNumber);
        }

        _host = new FleetHost(session);
        Open();
    }

    [Benchmark]
    public void Open()
    {
        var window = LinesWindow.Create(_host, static (_, _) => { });
        var tabs = (TabControl)window.Content!;
        tabs.Select(1);
        _ui.Screen = window;
        _ui.Layout();
        _ui.Draw(_draw);
        _table = FindTable(tabs)!;
    }

    [Benchmark]
    public int ScrollFrame()
    {
        if (_table.FirstRow >= 49_990 || _table.FirstRow == 0)
        {
            _direction = _table.FirstRow == 0 ? -1 : 1;
        }

        _table.OnScroll(_ui, _direction);
        _ui.Layout();
        _ui.Draw(_draw);
        return _draw.QuadCount;
    }

    private static VirtualTable? FindTable(Widget root)
    {
        if (root is VirtualTable table)
        {
            return table;
        }

        for (var i = 0; i < root.ChildCount; i++)
        {
            if (FindTable(root.GetChild(i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private sealed class FleetHost(GameSession session) : IVehicleUi
    {
        public GameSession Session => session;
        public string Money(long amount, Strings s) => amount.ToString("N0", s.Culture);
        public void OpenVehicle(int vehicleId) { }
        public void OpenDepot(int depotId) { }
        public void OpenLine(int lineId) { }
        public void StartLineMode(int lineId, int vehicleId) { }
    }
}
