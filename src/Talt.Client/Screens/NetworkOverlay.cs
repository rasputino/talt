using System.Diagnostics;
using System.Numerics;
using Talt.Client.Rendering;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Client.Screens;

/// <summary>Independent map layers drawn beneath the HUD. Geometry is cached by topology or reporting month.</summary>
internal sealed class NetworkOverlay : Widget
{
    private static readonly UiColor[] LineColors =
    [
        UiColor.Parse("#00B8D4"), UiColor.Parse("#FF856F"), UiColor.Parse("#B77CFA"), UiColor.Parse("#F9C74F"),
        UiColor.Parse("#67E5A1"), UiColor.Parse("#E783BC"), UiColor.Parse("#56A9FE"), UiColor.Parse("#EF9B52"),
        UiColor.Parse("#91C65F"), UiColor.Parse("#AEB2EF"), UiColor.Parse("#EF6868"), UiColor.Parse("#7BDCD8"),
    ];

    internal static readonly UiColor[] HeatColors =
    [
        UiColor.Parse("#FFE58A"), UiColor.Parse("#FFAA4A"), UiColor.Parse("#EF673C"), UiColor.Parse("#BD233D"),
    ];

    private readonly GameSimulation _simulation;
    private readonly ConstructionState _state;
    private readonly StationRegistry _stations;
    private readonly LineOverlayRouter _router;
    private readonly List<int> _tiles = [];
    private readonly Dictionary<int, int> _modes = [];
    private readonly Dictionary<(int A, int B), int> _routeColors = [];
    private readonly HashSet<int> _covered = [];
    private readonly Layer _routes = new();
    private readonly Layer _areas = new();
    private readonly Layer _traffic = new();
    private (int Road, int Rail, int Stations, int Vehicles, int Trains) _routeVersion = (-1, -1, -1, -1, -1);
    private int _areaVersion = -1;
    private int _heatMonth = -1;
    private Matrix4x4 _projection;
    private Vector2 _viewport;

    internal NetworkOverlay(GameSimulation simulation)
    {
        _simulation = simulation;
        _state = simulation.Construction!;
        _stations = simulation.Stations!;
        _router = new LineOverlayRouter(_state, _stations);
    }

    internal bool Routes { get; set; }
    internal bool Areas { get; set; }
    internal bool Traffic { get; set; }
    internal bool HasGaps { get; private set; }
    internal bool Routing { get; private set; }
    internal bool HasLines { get; private set; }
    internal bool RoutesInView { get; private set; }
    internal bool TrafficInView { get; private set; }
    internal Vector3? RouteCentre { get; private set; }
    internal Vector3? TrafficCentre { get; private set; }
    private int _nextLine;
    private int _nextStop;

    internal void Update(Matrix4x4 projection, Vector2 viewport)
    {
        _projection = projection;
        _viewport = viewport;
        if (Routes && _simulation.RoadVehicles is { } vehicles && _simulation.Trains is { } trains)
        {
            var version = (_state.Network.Version, _state.Rails.Version, _stations.Version, vehicles.LinkVersion, trains.LinkVersion);
            if (_routeVersion != version)
            {
                _routeVersion = version;
                BeginRoutes(vehicles, trains);
            }

            if (Routing)
            {
                ContinueRoutes(vehicles.Lines);
            }
        }

        if (Areas && _areaVersion != _stations.Version)
        {
            _areaVersion = _stations.Version;
            _areas.Clear();
            _covered.Clear();
            for (var i = 0; i < _stations.Count; i++)
            {
                if (_stations.OwnerAt(i) != _state.Player.Id)
                {
                    continue;
                }

                _tiles.Clear();
                _stations.CatchmentTiles(_stations.IdAt(i), _tiles);
                foreach (var tile in _tiles)
                {
                    if (_covered.Add(tile))
                    {
                        var p = Position(tile);
                        _areas.Add(tile, new(p, p, UiColor.FromBytes(57, 192, 207, 82), MarkKind.Area), _state.Map.Width);
                    }
                }
            }
        }

        if (Traffic && _simulation.Traffic is { } history && _heatMonth != history.Year * 12 + history.Month)
        {
            _heatMonth = history.Year * 12 + history.Month;
            _traffic.Clear();
            TrafficCentre = null;
            var peak = 0;
            foreach (var entry in history.Completed)
            {
                var segment = entry.Key;
                AddLink(_traffic, segment.First, segment.Last, HeatColors[Band(entry.Value)]);
                if (entry.Value > peak)
                {
                    peak = entry.Value;
                    TrafficCentre = (Position(segment.First) + Position(segment.Last)) * 0.5f;
                }
            }
        }
    }

    internal static int Band(int count) => count switch { <= 9 => 0, <= 49 => 1, <= 199 => 2, _ => 3 };

    private void BeginRoutes(RoadVehicleSystem vehicles, Talt.Core.Rails.TrainSystem trains)
    {
        _routes.Clear();
        _modes.Clear();
        _routeColors.Clear();
        HasGaps = false;
        HasLines = false;
        RouteCentre = null;
        _nextLine = 0;
        _nextStop = 0;
        Routing = true;
        for (var slot = 0; slot < vehicles.Count; slot++)
        {
            var line = vehicles.LineAt(slot);
            _modes[line] = _modes.GetValueOrDefault(line) | (vehicles.ModelAt(slot).Kind == StationPieceKind.Bus ? 1 : 2);
        }

        for (var slot = 0; slot < trains.Count; slot++)
        {
            var train = trains.TrainAt(slot);
            _modes[train.Line] = _modes.GetValueOrDefault(train.Line) | (train.Electric ? 8 : 4);
        }
    }

    private void ContinueRoutes(LineRegistry lines)
    {
        // Yield between legs so editing a large network does not run every search in one frame.
        var start = Stopwatch.GetTimestamp();
        do
        {
            if (_nextLine >= lines.Count)
            {
                FinishRoutes();
                Routing = false;
                return;
            }

            var line = lines.IdAt(_nextLine);
            var count = lines.StopCount(line);
            var owned = lines.OwnerOf(line) == _state.Player.Id;
            HasLines |= owned;
            if (count >= 2 && owned)
            {
                RouteLeg(line, lines.ColorOf(line), lines.StopAt(line, _nextStop).StationId,
                    lines.StopAt(line, (_nextStop + 1) % count).StationId);
                _nextStop++;
            }
            else
            {
                _nextStop = count;
            }

            if (_nextStop >= count)
            {
                _nextLine++;
                _nextStop = 0;
            }
        }
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2);
    }

    private void RouteLeg(int line, int colorIndex, int from, int to)
    {
        var color = 1 << colorIndex;
        var modes = _modes.GetValueOrDefault(line);
        var any = false;
        var missing = false;
        for (var mode = 1; mode <= 8; mode <<= 1)
        {
            if (((modes == 0 ? 7 : modes) & mode) == 0)
            {
                continue;
            }

            var found = mode <= 2 ? _router.FindRoadLeg(from, to, mode == 1 ? StationPieceKind.Bus : StationPieceKind.Truck, _tiles)
                : _router.FindRailLeg(from, to, _state.Player.Id, mode == 8, _tiles);
            if (RouteCentre is null && _tiles.Count >= 2)
            {
                var middle = _tiles.Count / 2;
                RouteCentre = (Position(_tiles[middle - 1]) + Position(_tiles[middle])) * 0.5f;
            }

            any |= found;
            missing |= !found;
            for (var tile = 1; tile < _tiles.Count; tile++)
            {
                var a = _tiles[tile - 1];
                var b = _tiles[tile];
                var link = (Math.Min(a, b), Math.Max(a, b));
                _routeColors[link] = _routeColors.GetValueOrDefault(link) | color;
            }
        }

        if (!any || (modes != 0 && missing))
        {
            MarkGap(from);
            MarkGap(to);
            HasGaps = true;
        }
    }

    private void FinishRoutes()
    {
        // Parallel stripes retain each line colour on shared streets and track.
        foreach (var (link, colors) in _routeColors)
        {
            var remaining = colors;
            var count = BitOperations.PopCount((uint)colors);
            var index = 0;
            while (remaining != 0)
            {
                var color = BitOperations.TrailingZeroCount(remaining);
                var offset = index++ - (count - 1) * 0.5f;
                AddLink(_routes, link.A, link.B, LineColors[color], offset);
                remaining &= remaining - 1;
            }
        }
    }

    private void MarkGap(int station)
    {
        if (_stations.TryGetCentre(station, out var x, out var z))
        {
            var tile = _state.Map.Index(x, z);
            var p = Position(tile);
            _routes.Add(tile, new(p, p, UiColor.FromBytes(239, 75, 64, 255), MarkKind.Gap), _state.Map.Width);
        }
    }

    private void AddLink(Layer layer, int from, int to, UiColor color, float offset = 0) =>
        layer.Add(from, new(Position(from), Position(to), color, MarkKind.Link, offset), _state.Map.Width);

    private Vector3 Position(int tile)
    {
        var map = _state.Map;
        var x = tile % map.Width;
        var z = tile / map.Width;
        return new(x + 0.5f, map.SurfaceHeightAt(x, z) + 0.35f, z + 0.5f);
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => Vector2.Zero;

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var frustum = new Frustum(_projection);
        if (Areas)
        {
            DrawLayer(_areas, frustum, list, ui.Scale);
        }

        // A broad heat-coloured ribbon remains visible on both sides of the narrow route stripe.
        TrafficInView = Traffic && DrawLayer(_traffic, frustum, list, ui.Scale * (Routes ? 11f : 7f));
        RoutesInView = Routes && DrawLayer(_routes, frustum, list, ui.Scale * 3.5f);
    }

    private bool DrawLayer(Layer layer, Frustum frustum, UiDrawList list, float width)
    {
        var visibleLink = false;
        foreach (var group in layer.Groups.Values)
        {
            if (!frustum.Intersects(group.Min, group.Max))
            {
                continue;
            }

            foreach (var mark in group.Marks)
            {
                if (mark.Kind == MarkKind.Area)
                {
                    var p = mark.A;
                    if (Project(p + new Vector3(-0.5f, 0, -0.5f), out var a)
                        && Project(p + new Vector3(0.5f, 0, -0.5f), out var b)
                        && Project(p + new Vector3(0.5f, 0, 0.5f), out var c)
                        && Project(p + new Vector3(-0.5f, 0, 0.5f), out var d))
                    {
                        list.AddPolygon(a, b, c, d, mark.Color);
                    }
                }
                else if (Project(mark.A, out var a))
                {
                    if (mark.Kind == MarkKind.Gap)
                    {
                        var offset = new Vector2(4 * width);
                        Stroke(list, a - offset, a + offset, width, mark.Color);
                        offset.Y = -offset.Y;
                        Stroke(list, a - offset, a + offset, width, mark.Color);
                    }
                    else if (Project(mark.B, out var b))
                    {
                        var before = list.QuadCount;
                        Stroke(list, a, b, width, mark.Color, mark.Offset);
                        visibleLink |= list.QuadCount > before;
                    }
                }
            }
        }
        return visibleLink;
    }

    private bool Project(Vector3 point, out Vector2 result)
    {
        var p = Vector4.Transform(new Vector4(point, 1), _projection);
        result = default;
        if (p.W <= 0.01f)
        {
            return false;
        }

        result = new((1 + p.X / p.W) * 0.5f * _viewport.X, (1 - p.Y / p.W) * 0.5f * _viewport.Y);
        return true;
    }

    private static void Stroke(UiDrawList list, Vector2 a, Vector2 b, float width, UiColor color, float offset = 0)
    {
        var dir = b - a;
        var length = dir.Length();
        if (length > 0.01f)
        {
            var normal = new Vector2(-dir.Y, dir.X) * (width * 0.5f / length);
            a += normal * (2 * offset);
            b += normal * (2 * offset);
            list.AddPolygon(a + normal, b + normal, b - normal, a - normal, color);
        }
    }

    private enum MarkKind { Link, Area, Gap }
    private readonly record struct Mark(Vector3 A, Vector3 B, UiColor Color, MarkKind Kind, float Offset = 0);

    private sealed class Group
    {
        public readonly List<Mark> Marks = [];
        public Vector3 Min = new(float.MaxValue);
        public Vector3 Max = new(float.MinValue);
    }

    private sealed class Layer
    {
        public readonly Dictionary<(int X, int Z), Group> Groups = [];
        private readonly Stack<Group> _unused = [];

        public void Clear()
        {
            foreach (var group in Groups.Values)
            {
                group.Marks.Clear();
                group.Min = new(float.MaxValue);
                group.Max = new(float.MinValue);
                _unused.Push(group);
            }

            Groups.Clear();
        }

        public void Add(int tile, Mark mark, int mapWidth)
        {
            var key = ((tile % mapWidth) >> 5, (tile / mapWidth) >> 5);
            if (!Groups.TryGetValue(key, out var group))
            {
                Groups[key] = group = _unused.TryPop(out var reusable) ? reusable : new();
            }

            group.Marks.Add(mark);
            group.Min = Vector3.Min(group.Min, Vector3.Min(mark.A, mark.B) - Vector3.One);
            group.Max = Vector3.Max(group.Max, Vector3.Max(mark.A, mark.B) + Vector3.One);
        }
    }
}
