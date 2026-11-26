using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Simulation;
using Talt.Headless;

// Headless runner: runs and replays simulations without a window or GPU (contracts/headless-cli.md).
// Exit codes: 0 success, 1 determinism failure, 2 invalid arguments or file, 3 unexpected error.
try
{
    if (args.Length > 0 && args[0] == "map")
    {
        if (!MapCommand.TryParse(args, out var mapOptions, out var mapError))
        {
            Console.Error.WriteLine(mapError);
            Console.Error.WriteLine(MapCommand.Usage);
            return 2;
        }

        return MapCommand.Run(mapOptions!);
    }

    if (!HeadlessArguments.TryParse(args, out var options, out var error))
    {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine(HeadlessArguments.Usage);
        return 2;
    }

    return options!.Command == HeadlessCommand.Run ? Run(options) : Replay(options);
}
catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 3;
}

static int Run(HeadlessOptions options)
{
    var stopwatch = Stopwatch.StartNew();
    var simulation = GameSimulation.Create(options.Settings);
    var setup = stopwatch.Elapsed;

    SessionRecorder? recorder = null;
    if (options.RecordPath is not null)
    {
        recorder = new SessionRecorder(new RecordedSession(options.Settings));
        simulation.AttachRecorder(recorder);
    }

    if (options.RoadGrid is int spacing && simulation.Construction is { } construction)
    {
        // Debug: a full road grid (not a command: recordings of such runs do not replay).
        construction.SeedDebugGrid(spacing);
    }

    // Feature 010: debug stations as ordinary commands, processed at the first tick (recorded and replayed).
    foreach (var command in Talt.Core.Stations.StationSeeder.Plan(simulation, options.SeedStations))
    {
        simulation.Submit(command);
    }

    // Feature 012: debug vehicles (stations, depots, lines, purchases) as ordinary commands.
    var seeded = Talt.Core.Vehicles.VehicleSeeder.Plan(simulation, options.SeedVehicles);
    foreach (var command in seeded)
    {
        simulation.Submit(command);
    }

    // Feature 015: debug trains (loops, stations, signals, depots, purchases) as ordinary commands.
    var seededTrains = Talt.Core.Rails.TrainSeeder.Plan(simulation, options.SeedTrains);
    foreach (var command in seededTrains)
    {
        simulation.Submit(command);
    }

    if (seeded.Count > 0 || seededTrains.Count > 0)
    {
        setup = stopwatch.Elapsed;
    }

    stopwatch.Restart();
    var ticks = options.Ticks!.Value;
    var halfStart = 0L;
    var secondHalfMs = -1.0;
    for (long t = 0; t < ticks; t++)
    {
        if (t == ticks / 2 && (simulation.RoadVehicles is { Count: > 0 } || simulation.Trains is { Count: > 0 }))
        {
            halfStart = Stopwatch.GetTimestamp();
        }

        simulation.Tick(!options.Sequential);
        if (t == ticks - 1 && halfStart > 0)
        {
            secondHalfMs = (Stopwatch.GetTimestamp() - halfStart) * 1000.0 / Stopwatch.Frequency / (ticks - (ticks / 2));
        }

        if (options.CheckOverlaps && (simulation.RoadVehicles?.CheckInvariants() ?? simulation.Trains?.CheckInvariants()) is { } problem)
        {
            Console.Error.WriteLine($"Vehicle check failed at tick {simulation.CurrentTick}: {problem}");
            return 1;
        }

        if (recorder is not null && (simulation.CurrentTick % 1000 == 0 || simulation.CurrentTick == ticks))
        {
            recorder.AddCheckpoint(simulation);
        }
    }

    var simulate = stopwatch.Elapsed;
    if (recorder is not null)
    {
        File.WriteAllText(options.RecordPath!, recorder.Session.ToJson());
    }

    PrintSummary(simulation, setup, simulate, ticks);
    if (secondHalfMs >= 0)
    {
        // Feature 012: the steady-state tick once seeded vehicles have left their depots.
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"second-half-avg-tick-ms: {secondHalfMs:F3}"));
    }

    return 0;
}

static int Replay(HeadlessOptions options)
{
    var session = RecordedSession.FromJson(File.ReadAllText(options.SessionPath!));
    var stopwatch = Stopwatch.StartNew();
    var result = SessionReplayer.Replay(session, options.Ticks, options.CheckpointEvery, !options.Sequential);
    var elapsed = stopwatch.Elapsed;

    PrintSummary(result.Simulation, TimeSpan.Zero, elapsed, result.Simulation.CurrentTick);
    foreach (var checkpoint in result.Checkpoints)
    {
        Console.WriteLine($"checkpoint {checkpoint.Tick}: {StateHasher.Format(checkpoint.Actual)} {(checkpoint.Matches ? "OK" : "MISMATCH")}");
    }

    foreach (var (tick, fingerprint) in result.ExtraFingerprints)
    {
        Console.WriteLine($"fingerprint {tick}: {StateHasher.Format(fingerprint)}");
    }

    if (!result.Succeeded)
    {
        Console.Error.WriteLine($"Determinism failure at tick {result.FirstFailingTick}: {result.FailureReason}");
        return 1;
    }

    return 0;
}

static void PrintSummary(GameSimulation simulation, TimeSpan setup, TimeSpan simulate, long ticks)
{
    var c = CultureInfo.InvariantCulture;
    Console.WriteLine($"ticks: {simulation.CurrentTick}");
    Console.WriteLine($"date: {simulation.Date}");
    Console.WriteLine($"vehicles: {simulation.Vehicles.VehicleCount}");
    Console.WriteLine($"industries: {simulation.Industries.Count}");
    Console.WriteLine($"road-tiles: {simulation.Construction?.Roads.RoadTileCount ?? 0}");
    if (simulation.Towns is { } towns)
    {
        // Feature 008: town summary (contracts/cli.md).
        var population = 0L;
        for (var town = 0; town < towns.Count; town++)
        {
            population += towns.PopulationOf(town);
        }

        Console.WriteLine($"towns: {towns.Count}");
        Console.WriteLine($"town-population: {population}");
        Console.WriteLine($"town-buildings: {towns.Buildings.Count}");
        foreach (var town in Enumerable.Range(0, towns.Count).OrderByDescending(towns.PopulationOf).ThenBy(t => t).Take(5))
        {
            Console.WriteLine($"  {towns.NameOf(town)}: {towns.PopulationOf(town)}");
        }
    }
    if (simulation.Stations is { Count: > 0 } stations)
    {
        // Feature 010: station summary (contracts/cli.md).
        Console.WriteLine($"stations: {stations.Count}");
        Console.WriteLine($"station-groups: {stations.GroupCount}");
        Console.WriteLine($"waiting: {stations.TotalWaiting}");
        Console.WriteLine($"supplied: {stations.TotalSupplied}");
        Console.WriteLine($"collected: {stations.TotalDebugCollected}");
        Console.WriteLine($"lost: {stations.TotalLost}");
        var busiest = Enumerable.Range(0, stations.Count)
            .Select(slot => (Id: stations.IdAt(slot), Waiting: Enumerable.Range(0, simulation.Content.CargoCount).Sum(c => stations.WaitingAt(stations.IdAt(slot), c))))
            .OrderByDescending(s => s.Waiting).ThenBy(s => s.Id).Take(5);
        foreach (var (id, waiting) in busiest)
        {
            Console.WriteLine($"  station {id}: {waiting} waiting");
        }
    }

    if (simulation.RoadVehicles is { } roadVehicles && roadVehicles.EverUsed)
    {
        // Feature 012: vehicle summary (contracts/cli.md).
        Console.WriteLine($"road-vehicles: {roadVehicles.Count}");
        Console.WriteLine($"lines: {roadVehicles.Lines.Count}");
        Console.WriteLine($"depots: {roadVehicles.Depots.Count}");
        Console.WriteLine($"vehicle-deliveries: {roadVehicles.Deliveries}");
        Console.WriteLine($"vehicle-tiles: {roadVehicles.TotalDistance / 65_536}");
        Console.WriteLine($"route-searches: {roadVehicles.RouteSearches}");
        var states = Enumerable.Range(0, roadVehicles.Count).GroupBy(roadVehicles.StatusAt).OrderBy(g => g.Key);
        Console.WriteLine("vehicle-states: " + string.Join(", ", states.Select(g => $"{g.Key} {g.Count()}")));
    }

    if (simulation.Router is { EverStarted: true } router && simulation.Stations is { } directed)
    {
        // Feature 013: destination cargo (contracts/demand-api.md).
        Console.WriteLine($"route-sets: {router.AppliedCount} ({router.LastDestinations} destinations)");
        Console.WriteLine($"destination-cargo: generated {directed.DirectedGenerated}, delivered {directed.DirectedDelivered}, "
            + $"waiting {directed.DirectedWaiting}, transferred {directed.DirectedTransferred}, lost {directed.DirectedLost} "
            + $"(expired {directed.DirectedExpired}), unserved {directed.DirectedUnserved}");
    }

    if (simulation.Trains is { Count: > 0 } trains)
    {
        // Feature 015: trains, track and train deliveries.
        Console.WriteLine($"trains: {trains.Count}");
        Console.WriteLine($"rail-tiles: {simulation.Construction!.Rails.RailTileCount}");
        Console.WriteLine($"train-deliveries: {trains.Deliveries}");
    }

    if (simulation.Finance is { } finance)
    {
        // Feature 011: the player's finances (contracts/cli.md), internal units.
        var report = finance.Report(Talt.Core.Construction.Company.PlayerId);
        var ledger = simulation.Construction!.Player.Ledger!;
        long income = 0, expenses = 0;
        for (var back = 0; back < ledger.YearCount; back++)
        {
            income += ledger.YearIncome(back);
            expenses += ledger.YearExpenses(back);
        }

        Console.WriteLine($"cash: {report.Cash}");
        Console.WriteLine($"loan: {report.Loan}/{report.MaxLoan}");
        Console.WriteLine($"company-value: {report.Value}");
        Console.WriteLine($"income: {income}");
        Console.WriteLine($"expenses: {expenses}");
        Console.WriteLine($"deliveries-paid: {finance.Deliveries}");
        Console.WriteLine($"bankrupt-months: {report.BankruptMonths}{(report.IsGameOver ? " (game over)" : string.Empty)}");
        Console.WriteLine($"infinite-money: {(finance.UsedInfiniteMoney(Talt.Core.Construction.Company.PlayerId) ? "on" : "off")}");
    }

    Console.WriteLine($"fingerprint: {StateHasher.Format(simulation.ComputeFingerprint())}");
    Console.WriteLine($"setup-ms: {setup.TotalMilliseconds.ToString("F0", c)}");
    Console.WriteLine($"simulate-ms: {simulate.TotalMilliseconds.ToString("F0", c)}");
    Console.WriteLine($"avg-tick-ms: {(ticks > 0 ? simulate.TotalMilliseconds / ticks : 0).ToString("F3", c)}");
}
