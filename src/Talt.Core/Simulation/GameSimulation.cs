using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Finance;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Roads;
using Talt.Core.Scenarios;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Core.Simulation;

/// <summary>
/// Owns the complete simulated game state and advances it one fixed tick at a time
/// (data-model.md, GameSimulation; contracts/simulation-api.md).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Tick"/> is the only method that changes state after creation. Each tick: (1) apply the
/// queued player commands in submission order, (2) advance the systems (vehicles), (3) increment
/// <see cref="CurrentTick"/>, (4) fire day, month and year events when a boundary was crossed.
/// </para>
/// <para>
/// The simulation never reads the wall clock and all randomness comes from seeded
/// <see cref="DeterministicRandom"/> streams, so the same settings and the same commands produce the
/// same <see cref="ComputeFingerprint"/> at every tick, on every platform.
/// </para>
/// </remarks>
public sealed class GameSimulation : IStateHashable
{
    private readonly CommandInbox _commands = new();
    private SessionRecorder? _recorder;
    private bool _parallelTick = true;

    private GameSimulation(
        ScenarioSettings settings,
        TransportNetwork network,
        VehicleFleet vehicles,
        GameContent content,
        MapData? map,
        ConstructionState? construction)
    {
        Settings = settings;
        Content = content;
        Network = network;
        Vehicles = vehicles;
        Construction = construction;
        Clock = new GameClock(settings.StartYear);
        Random = new DeterministicRandom(unchecked((ulong)settings.Seed)).Fork(streamId: 0x51_4D);
        Industries = map is null
            ? IndustryFleet.CreateEmpty(content, settings.MapSizeTiles, settings.MapSizeTiles)
            : IndustryFleet.Create(content, map, settings.StartYear, Random.Fork(0x1D_05));
        if (construction is not null)
        {
            // Feature 007: town streets on the town sites (after industries, which they avoid), then the
            // routing graph; later industry openings skip tiles with objects or bridges.
            construction.Year = Date.Year;
            construction.AttachIndustries(Industries);
            RoadSeeder.SeedTowns(construction);
            // Feature 008: a town on every town site, grown to its starting size on those streets.
            Towns = TownRegistry.Found(construction, settings.EffectiveTowns, Random.Fork(0x70_57_4E), Date.Year);
            construction.AttachTowns(Towns);
            construction.RebuildNetwork();
            Industries.TileBlocked = tile => construction.Objects.KindAt(tile) != TileObjectKind.None
                || (construction.Roads.FlagsAt(tile) & RoadFeatures.Passing) != 0;
            // Feature 010: stations collect cargo from industries and towns and hand it to vehicles.
            Stations = StationRegistry.Create(construction, content, Industries, Towns, settings.EffectiveStations, Date);
            construction.AttachStations(Stations);
            // Feature 011: company accounts, paid for the stations' deliveries.
            var financeRules = ReferenceEquals(content, GameContent.Default) && ReferenceEquals(construction.RoadRules, RoadRules.Default)
                && ReferenceEquals(Stations.Rules, StationRules.Default)
                ? FinanceRules.Default
                : FinanceRules.FromJson(FinanceRules.ReadResource(), content, construction.RoadRules, Stations.Rules);
            Finance = new FinanceSystem(construction, Stations, content, financeRules, settings.EffectiveFinance, Date);
        }

        // Stations refresh their coverage before industries produce (industries hand them their share), then
        // collect town passengers and mail and update ratings (feature 010, research R14).
        Events.OnDayEnded(simulation =>
        {
            var date = simulation.Date;
            simulation.Stations?.BeginDay(date);
            simulation.Industries.AdvanceDay(date, simulation.Stations);
            simulation.Stations?.AdvanceDay(date, simulation._parallelTick);
        });
        Events.OnMonthEnded(simulation => Industries.AdvanceMonth(simulation.Date));
        if (Towns is not null)
        {
            Events.OnMonthEnded(simulation => simulation.Towns!.AdvanceMonth(simulation.Date, simulation._parallelTick));
        }

        if (Stations is not null)
        {
            Events.OnMonthEnded(simulation => simulation.Stations!.AdvanceMonth(simulation.Date));
        }

        if (Finance is not null)
        {
            // After the stations, so a day's deliveries and debug pickups are paid before its costs (research R9).
            Events.OnDayEnded(simulation => simulation.Finance!.AdvanceDay(simulation.Date));
            Events.OnMonthEnded(simulation => simulation.Finance!.AdvanceMonth(simulation.Date));
            Events.OnYearEnded(simulation => simulation.Finance!.AdvanceYear(simulation.Date));
        }
    }

    /// <summary>Number of completed ticks since the game started (data-model.md GameSimulation.CurrentTick).</summary>
    public long CurrentTick { get; private set; }

    public GameDate Date => Clock.DateAt(CurrentTick);

    public GameClock Clock { get; }

    public ScenarioSettings Settings { get; }

    /// <summary>Validated cargo and industry catalogue used by this game.</summary>
    public GameContent Content { get; }

    /// <summary>The towns of a game with a map (feature 008); null for map-less scenarios.</summary>
    public TownRegistry? Towns { get; }

    /// <summary>The stations of a game with a map (feature 010); null for map-less scenarios.</summary>
    public StationRegistry? Stations { get; }

    /// <summary>The company accounts of a game with a map (feature 011); null for map-less scenarios.</summary>
    public FinanceSystem? Finance { get; }

    /// <summary>All industries of the world; empty when the game was created without a map.</summary>
    public IndustryFleet Industries { get; }

    public TransportNetwork Network { get; }

    public VehicleFleet Vehicles { get; }

    public SimulationEvents Events { get; } = new();

    /// <summary>
    /// The map's tile data, objects, companies and undo record (feature 006); null when the scenario has
    /// no map, in which case construction commands are rejected.
    /// </summary>
    public ConstructionState? Construction { get; }

    /// <summary>Root random generator of the simulation; systems should <see cref="DeterministicRandom.Fork"/> it.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Commands waiting for the next tick.</summary>
    public int PendingCommandCount => _commands.Count;

    /// <summary>Raised during <see cref="Tick"/> for every processed command, in processing order.</summary>
    public event Action<CommandProcessed>? CommandProcessed;

    /// <summary>Creates a game from its settings.</summary>
    /// <param name="settings">The scenario; with a map, the simulation owns the map's tile data.</param>
    /// <param name="map">
    /// The map when the caller already generated it (the client, to show progress). When the scenario has
    /// map settings, the map must have been generated from exactly <see cref="ScenarioSettings.Map"/> and the
    /// game can be built on (feature 006); when it has none, a map only provides industry sites (feature 009,
    /// tests). When null and the scenario has a map, it is generated here (replays, headless runs).
    /// </param>
    /// <param name="content">Cargo and industry catalogue; null for the game's data files.</param>
    /// <param name="rules">Construction balancing; null for the game's data file.</param>
    public static GameSimulation Create(
        ScenarioSettings settings,
        MapData? map = null,
        GameContent? content = null,
        ConstructionRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (map is not null && settings.Map is not null && map.Settings != settings.Map)
        {
            throw new ArgumentException("The map was not generated from the scenario's map settings.", nameof(map));
        }

        content ??= GameContent.Default;
        var (network, fleet) = ScenarioBuilder.Build(settings);
        ConstructionState? construction = null;
        if (settings.Map is not null)
        {
            map ??= MapGenerator.Generate(settings.Map);
            construction = new ConstructionState(rules ?? ConstructionRules.Default, map);
        }

        return new GameSimulation(settings, network, fleet, content, map, construction);
    }

    /// <summary>Queues a command; it is processed at the start of the next tick.</summary>
    /// <returns>Submission id.</returns>
    public long Submit(ISimulationCommand command, int issuerId = 0) => _commands.Enqueue(command, issuerId);

    /// <summary>Records every processed command from now on.</summary>
    public void AttachRecorder(SessionRecorder recorder) => _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

    /// <summary>
    /// Processes the queued commands now, without advancing time or running any system (research R3):
    /// the client calls it while the game is paused so construction applies immediately (FR-018).
    /// Commands are recorded with the current tick, exactly as if processed at the start of the next
    /// <see cref="Tick"/>, so replays are unchanged.
    /// </summary>
    public void ProcessPendingCommands() => ProcessCommands();

    /// <summary>Advances the simulation by exactly one tick.</summary>
    /// <param name="parallel">Use all CPU cores where possible. Results are identical either way.</param>
    public void Tick(bool parallel = true)
    {
        _parallelTick = parallel;
        ProcessCommands();
        Vehicles.Tick(parallel);
        CurrentTick++;

        if (GameClock.IsDayBoundary(CurrentTick))
        {
            var date = Date;
            if (Construction is not null)
            {
                Construction.Year = date.Year;
            }

            var monthEnded = date.Day == 1;
            var yearEnded = monthEnded && date.Month == 1;
            Events.Dispatch(this, dayEnded: true, monthEnded, yearEnded);
        }
    }

    /// <summary>64-bit fingerprint of the full simulation state. Pure: changes nothing.</summary>
    public ulong ComputeFingerprint()
    {
        var hasher = new StateHasher();
        hasher.Append(this);
        return hasher.GetFingerprint();
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(CurrentTick);
        hasher.Append(Clock.StartDayNumber);
        hasher.Append(Random);
        hasher.Append(Network);
        hasher.Append(Vehicles);
        hasher.Append(Industries);
        if (Construction is not null)
        {
            // Only games with a map have construction state, so map-less goldens are unchanged (research R2).
            hasher.Append(Construction);
        }

        if (Towns is not null)
        {
            hasher.Append(Towns);
        }

        if (Stations is not null)
        {
            hasher.Append(Stations); // Appends nothing until a station is built (FR-030).
        }

        if (Finance is not null)
        {
            hasher.Append(Finance); // Appends nothing until the first booking (feature 011, research R11).
        }
    }

    private void ProcessCommands()
    {
        if (Construction is not null && _commands.Count > 0)
        {
            Construction.Year = Date.Year;
        }

        while (_commands.TryDequeue(out var pending))
        {
            // Feature 011 (FR-020): after game over every player command is refused.
            var result = Finance is not null && Finance.IsGameOver(Company.PlayerId)
                ? CommandResult.Rejected(ConstructionReasons.GameOver, ConstructionReasons.Describe(ConstructionReasons.GameOver))
                : pending.Command.Validate(this);
            if (result.Succeeded)
            {
                pending.Command.Apply(this);
            }

            var processed = new CommandProcessed(pending.SubmissionId, pending.IssuerId, CurrentTick, pending.Command, result);
            _recorder?.OnCommandProcessed(processed);
            CommandProcessed?.Invoke(processed);
        }
    }
}
