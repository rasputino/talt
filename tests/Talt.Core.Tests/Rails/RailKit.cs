using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Rails;

/// <summary>Rail test helpers: a game on a hand-made map, commands applied at once, ticks with the safety invariant.</summary>
internal sealed class RailKit
{
    public RailKit(TestMap? map = null, int startYear = 1950, long balance = 1_000_000_000)
    {
        map ??= new TestMap();
        var data = map.Build();
        var settings = new ScenarioSettings(Seed: 3, StartYear: startYear, MapSizeTiles: 64, RoadSpacing: 16,
            RoadRemovalChancePerMille: 0, VehicleCount: 0, LineCount: 1, Map: data.Settings);
        Simulation = GameSimulation.Create(settings, data, rules: TestMap.Rules with { StartingBalance = balance });
        Simulation.CommandProcessed += processed => Last = processed.Result;
    }

    public GameSimulation Simulation { get; }

    public ConstructionState State => Simulation.Construction!;

    public RailLayer Rails => State.Rails;

    public CommandResult Last { get; private set; }

    public int Tile(int x, int z) => State.Map.Index(x, z);

    public CommandResult Run(ISimulationCommand command)
    {
        Simulation.Submit(command);
        Simulation.ProcessPendingCommands();
        Assert.Null(Rails.Validate());
        return Last;
    }

    public CommandResult Track(int x0, int z0, int x1, int z1, string type = "normal") =>
        Run(new BuildRailCommand(Company.PlayerId, type, x0, z0, x1, z1));

    public CommandResult Signal(int x, int z, SignalKind kind, int faces) =>
        Run(new SetSignalCommand(Company.PlayerId, x, z, (int)kind, faces));

    public TrainSystem Trains => Simulation.Trains!;

    /// <summary>Builds straight or 45° segments through the points, failing with a map dump when one is refused.</summary>
    public void Path(params (int X, int Z)[] points)
    {
        for (var i = 0; i + 1 < points.Length; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            Assert.True(Track(a.X, a.Z, b.X, b.Z).Succeeded, $"{a}->{b}: {Last.ReasonCode}{System.Environment.NewLine}{Dump(Math.Min(a.X, b.X) - 3, Math.Min(a.Z, b.Z) - 3, Math.Max(a.X, b.X) + 3, Math.Max(a.Z, b.Z) + 3)}");
        }
    }

    /// <summary>Every train's state (failure messages).</summary>
    public string Describe()
    {
        var text = new System.Text.StringBuilder();
        var w = Rails.Width;
        for (var slot = 0; slot < Trains.Count; slot++)
        {
            var t = Trains.TrainAt(slot);
            var head = t.StepCount > 0 ? t.StepFrom(t.HeadStep) : -1;
            text.AppendLine(System.FormattableString.Invariant(
                $"train {t.Id}: {TrainSystem.StatusOf(t)} mode {t.Mode} flags {t.Flags} target {t.TargetKind}:{t.TargetId} head ({head % w},{head / w}) end ({t.EndTile % w},{t.EndTile / w}) steps {t.StepCount} headStep {t.HeadStep} route {t.Route.Count}/{t.RoutePos} speed {t.Speed} next {(t.RoutePos < t.Route.Count ? t.Route.FromAt(t.RoutePos) % w + "," + t.Route.FromAt(t.RoutePos) / w + ">" + t.Route.DirAt(t.RoutePos) : "-")} last {(t.Route.Count > 0 ? t.Route.FromAt(t.Route.Count - 1) % w + "," + t.Route.FromAt(t.Route.Count - 1) / w : "-")} owners {Owners(t)}"));
        }

        return text.ToString();
    }

    private string Owners(Train t)
    {
        var w = Rails.Width;
        var list = new List<string>();
        for (var i = 0; i < t.StepCount; i++)
        {
            list.Add(System.FormattableString.Invariant($"{t.StepFrom(i) % w},{t.StepFrom(i) / w}"));
        }

        return string.Join(" ", list);
    }

    /// <summary>ASCII picture of the track in a rectangle (tests' failure messages).</summary>
    public string Dump(int x0, int z0, int x1, int z1)
    {
        var text = new System.Text.StringBuilder();
        for (var z = Math.Max(0, z0); z <= Math.Min(Rails.Depth - 1, z1); z++)
        {
            text.Append(z.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            for (var x = Math.Max(0, x0); x <= Math.Min(Rails.Width - 1, x1); x++)
            {
                var tile = Tile(x, z);
                var links = Rails.LinksAt(tile);
                var c = links == 0 ? (Rails.HasRail(tile) ? 'o' : '.')
                    : Talt.Core.Roads.Dir8.Count(links) > 2 ? '+'
                    : links == 0b0100_0100 ? '-' : links == 0b0001_0001 ? '|' : links == 0b0010_0010 ? '/' : links == 0b1000_1000 ? (char)92 : '~';
                if (Rails.SignalAt(tile) != 0)
                {
                    c = 'S';
                }

                text.Append(c);
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    public void Ok(ISimulationCommand command) => Assert.True(Run(command).Succeeded, Last.ReasonCode);

    /// <summary>Builds a station of platforms × length with minimum corner (x, z); returns its id.</summary>
    public int Station(int x, int z, int axis, int platforms, int length)
    {
        Ok(new BuildRailStationCommand(Company.PlayerId, x, z, axis, platforms, length));
        Assert.True(State.Stations!.TryGetStationAt(x, z, out var id));
        return id;
    }

    public int Depot(int x, int z, int direction)
    {
        Ok(new BuildRailDepotCommand(Company.PlayerId, x, z, direction));
        return State.RailDepots.IdAt(State.RailDepots.IndexAtTile(Tile(x, z)));
    }

    /// <summary>Buys a locomotive and wagons in a depot; returns the train id.</summary>
    public int Train(int depot, string locomotive = "loco-meridian", int wagons = 3, string wagon = "wagon-coach")
    {
        Ok(new BuyRailVehicleCommand(Company.PlayerId, depot, locomotive));
        var id = Trains.LastCreatedId;
        for (var i = 0; i < wagons; i++)
        {
            Ok(new BuyRailVehicleCommand(Company.PlayerId, depot, wagon, "", id));
        }

        return id;
    }

    /// <summary>Creates a line through the stations and assigns the trains (which start).</summary>
    public int Line(int[] stations, params int[] trains)
    {
        Ok(new Talt.Core.Vehicles.CreateLineCommand(Company.PlayerId, stations));
        var lines = Simulation.RoadVehicles!.Lines;
        var line = lines.IdAt(lines.Count - 1);
        foreach (var train in trains)
        {
            Ok(new AssignTrainLineCommand(Company.PlayerId, train, line));
        }

        return line;
    }

    /// <summary>Runs ticks checking the safety invariant after every one.</summary>
    public void Ticks(int ticks, bool parallel = true, Action? each = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            Simulation.Tick(parallel);
            var problem = Trains.CheckInvariants();
            Assert.True(problem is null, $"tick {Simulation.CurrentTick}: {problem}");
            each?.Invoke();
        }
    }
}
