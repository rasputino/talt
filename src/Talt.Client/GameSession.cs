using System.Diagnostics;
using System.Numerics;
using Talt.Client.Audio;
using Talt.Client.Construction;
using Talt.Client.Rendering;
using Talt.Client.Ui;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Simulation;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client;

/// <summary>Development options of a session from the command line.</summary>
internal sealed record SessionOptions(
    string? RecordPath = null,
    long? PrintFingerprintAtTick = null,
    bool ExitAfterFingerprint = false);

/// <summary>
/// One running game: the simulation and its fixed-step runner, the world, and the vehicle snapshots
/// interpolated for drawing (feature 001 research R4). Player actions reach the simulation as commands
/// (constitution III). Moved out of <see cref="GameWindow"/> so the window can show menus without a game.
/// </summary>
internal sealed class GameSession
{
    private const float LaneOffset = 0.22f; // Vehicles drive on the right half of the road.
    private const int BatchSize = 4096;

    private static readonly Vector4[] VehicleColors =
    [
        new(0.80f, 0.10f, 0.08f, 1f),
        new(0.95f, 0.60f, 0.05f, 1f),
        new(0.10f, 0.35f, 0.85f, 1f),
        new(0.95f, 0.95f, 0.95f, 1f),
        new(0.15f, 0.60f, 0.25f, 1f),
        new(0.55f, 0.15f, 0.65f, 1f),
    ];

    private readonly StopwatchTimeSource _time = new();
    private readonly Random _uiRandom = new(); // Never the simulation's generator (I1): choices travel inside commands.
    private readonly Stack<int> _playerAddedVehicles = new();
    private readonly TerrainSync _terrainSync;
    private readonly Action<int> _buildInstancesBatch;
    private readonly SessionRecorder? _recorder;

    private VehiclePose[] _previousPoses = [];
    private int[] _previousIds = [];
    private int _previousCount;
    private VehiclePose[] _currentPoses = [];
    private int[] _currentIds = [];
    private int _currentCount;
    private long _currentCapturedAtTick = -1;
    private VehicleInstance[] _instances = [];
    private int[] _instanceIds = [];
    private int _drawCount;
    private float _drawFactor;
    private IndustryInstance[] _industryInstances = [];
    private int _industryCount;
    private long _industryVersion = -1;

    public GameSession(GeneratedGame game, SessionOptions options, bool fastForwardUnlocked)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(options);
        Game = game;
        Options = options;
        Simulation = game.Simulation;
        Runner.SetFastForwardUnlocked(fastForwardUnlocked);
        Simulation.CommandProcessed += OnCommandProcessed;
        Construction = Simulation.Construction ?? throw new ArgumentException("A client game needs a map.", nameof(game));
        _terrainSync = new TerrainSync(Construction, game.World, game.Heightmap);
        _buildInstancesBatch = BuildInstancesBatch;
        if (options.RecordPath is not null)
        {
            _recorder = new SessionRecorder(new RecordedSession(game.Scenario));
            Simulation.AttachRecorder(_recorder);
        }

        CaptureCurrentPoses();
        CopyCurrentToPrevious();
    }

    public GeneratedGame Game { get; }

    public SessionOptions Options { get; }

    public GameSimulation Simulation { get; }

    public SimulationRunner Runner { get; } = new();

    public VoxelWorld World => Game.World;

    public Heightmap Heightmap => Game.Heightmap;

    /// <summary>Tile data, companies and undo of the game (feature 006).</summary>
    public ConstructionState Construction { get; }

    /// <summary>Changes whenever trees or tile objects may have changed (tree instances are rebuilt).</summary>
    public int SceneryVersion => _terrainSync.SceneryVersion;

    /// <summary>Raised with the changed voxels after construction changed the terrain (for the renderer).</summary>
    public event Action<IReadOnlyList<(int X, int Y, int Z)>>? TerrainChanged;

    /// <summary>Vehicles as drawn this frame (for rendering and picking).</summary>
    public ReadOnlySpan<VehicleInstance> Instances => _instances.AsSpan(0, _drawCount);

    /// <summary>Backing array of <see cref="Instances"/> (picking reads it without copying).</summary>
    internal VehicleInstance[] InstanceBuffer => _instances;

    /// <summary>Backing array of <see cref="InstanceIds"/>.</summary>
    internal int[] InstanceIdBuffer => _instanceIds;

    /// <summary>Vehicle id of each entry of <see cref="Instances"/>.</summary>
    public ReadOnlySpan<int> InstanceIds => _instanceIds.AsSpan(0, _drawCount);

    /// <summary>Industry placeholder boxes as drawn this frame (feature 009).</summary>
    public ReadOnlySpan<IndustryInstance> IndustryInstances => _industryInstances.AsSpan(0, _industryCount);

    /// <summary>Station whose catchment the station window highlights (feature 010), or −1.</summary>
    public int CatchmentStationId { get; private set; } = -1;

    /// <summary>Incremented whenever <see cref="CatchmentStationId"/> changes.</summary>
    public int CatchmentVersion { get; private set; }

    /// <summary>Turns the catchment highlight of a station on, or off when it is already on (station window).</summary>
    public void ToggleCatchment(int stationId)
    {
        CatchmentStationId = CatchmentStationId == stationId ? -1 : stationId;
        CatchmentVersion++;
    }

    /// <summary>Turns off the highlight of a station (its window closed).</summary>
    public void ClearCatchment(int stationId)
    {
        if (CatchmentStationId == stationId)
        {
            CatchmentStationId = -1;
            CatchmentVersion++;
        }
    }

    /// <summary>Raised for every processed command with a localized message.</summary>
    public event Action<TextRef>? Message;

    /// <summary>
    /// Raised for every processed command: whether it succeeded and whether its type is a
    /// construction command (feature 019 audio feedback).
    /// </summary>
    public event Action<bool, bool>? CommandFeedback;

    /// <summary>Raised when the fingerprint requested on the command line was printed and the game should exit.</summary>
    public event Action? ExitRequested;

    /// <summary>Advances the simulation by the real time elapsed (fixed ticks).</summary>
    public RunnerFrameStats Advance(double deltaSeconds)
    {
        var stats = Runner.Advance(deltaSeconds, Simulation, _time, PromoteCurrentToPrevious, AfterEachTick);
        if (stats.TicksRun > 0)
        {
            CaptureCurrentPoses();
        }
        else if (Runner.IsPaused && Simulation.PendingCommandCount > 0)
        {
            Simulation.ProcessPendingCommands(); // Construction applies at once while paused (FR-018).
        }

        var voxels = _terrainSync.Sync();
        if (voxels.Count > 0)
        {
            TerrainChanged?.Invoke(voxels);
        }

        return stats;
    }

    public void SubmitAddVehicle()
    {
        var fleet = Simulation.Vehicles;
        if (fleet.LineCount == 0)
        {
            return; // Games with a map have no prototype lines (feature 007, FR-061).
        }

        var line = _uiRandom.Next(fleet.LineCount);
        var speed = _uiRandom.Next(SyntheticTraffic.MinCruiseSpeed, SyntheticTraffic.MaxCruiseSpeed + 1);
        Simulation.Submit(new AddVehicleCommand(line, speed));
    }

    public void SubmitRemoveVehicle()
    {
        if (_playerAddedVehicles.TryPop(out var id))
        {
            Simulation.Submit(new RemoveVehicleCommand(id));
        }
    }

    /// <summary>Queues a player command (construction tools).</summary>
    public void Submit(ISimulationCommand command) => Simulation.Submit(command);

    /// <summary>Interpolates the two snapshots and builds this frame's vehicle instances (parallel batches).</summary>
    public void BuildInstances()
    {
        _drawCount = _currentCount;
        _drawFactor = Runner.InterpolationFactor;
        var batches = (_drawCount + BatchSize - 1) / BatchSize;
        if (batches > 1)
        {
            Parallel.For(0, batches, _buildInstancesBatch);
        }
        else if (batches == 1)
        {
            BuildInstancesBatch(0);
        }

        BuildIndustryInstances();
    }

    /// <summary>Writes the recorded session, if recording.</summary>
    public void SaveRecording()
    {
        if (_recorder is null || Options.RecordPath is null)
        {
            return;
        }

        _recorder.AddCheckpoint(Simulation);
        File.WriteAllText(Options.RecordPath, _recorder.Session.ToJson());
        Console.WriteLine($"Session recorded to {Path.GetFullPath(Options.RecordPath)} ({Simulation.CurrentTick} ticks).");
    }

    public void Detach()
    {
        Simulation.CommandProcessed -= OnCommandProcessed;
        CommandFeedback = null;
    }

    private void OnCommandProcessed(CommandProcessed processed)
    {
        if (processed.Result.Succeeded && processed.Command is AddVehicleCommand)
        {
            _playerAddedVehicles.Push(Simulation.Vehicles.LastCreatedVehicleId);
        }

        var command = processed.Command.TypeName;
        var reason = processed.Result.ReasonCode;
        var succeeded = processed.Result.Succeeded;
        var isConstruction = processed.Command is ConstructionCommand || AudioCommandClassifier.IsConstruction(command);
        CommandFeedback?.Invoke(succeeded, isConstruction);
        if (succeeded && processed.Command is ConstructionCommand)
        {
            return; // The preview already showed what happens and the money changes in the top bar.
        }

        Message?.Invoke(TextRef.Dynamic(() => 0, s => succeeded
            ? s.Format("command.applied", ("command", s.Get($"command.{command}")))
            : s.Format("command.rejected", ("reason", RejectionText(s, reason)))));
    }

    private static string RejectionText(Localization.Strings s, string? reason) =>
        s.Contains($"command.rejected.{reason}") ? s.Get($"command.rejected.{reason}")
        : s.Contains($"construction.reason.{reason}") ? s.Get($"construction.reason.{reason}")
        : s.Get("command.rejected.unknown");

    /// <summary>
    /// Called right before the last tick of a frame: the current snapshot becomes "previous". When it
    /// is already up to date (the usual one-tick frame) the buffers are just swapped, no copying.
    /// </summary>
    private void PromoteCurrentToPrevious()
    {
        if (_currentCapturedAtTick != Simulation.CurrentTick)
        {
            CaptureCurrentPoses();
        }

        (_previousPoses, _currentPoses) = (_currentPoses, _previousPoses);
        (_previousIds, _currentIds) = (_currentIds, _previousIds);
        _previousCount = _currentCount;
    }

    private void AfterEachTick(GameSimulation simulation)
    {
        if (Options.PrintFingerprintAtTick == simulation.CurrentTick)
        {
            Console.WriteLine($"fingerprint at tick {simulation.CurrentTick}: {StateHasher.Format(simulation.ComputeFingerprint())}");
            if (Options.ExitAfterFingerprint)
            {
                ExitRequested?.Invoke();
            }
        }
    }

    private void CaptureCurrentPoses()
    {
        var fleet = Simulation.Vehicles;
        EnsureCapacity(fleet.VehicleCount);
        fleet.CopyPoses(_currentPoses, parallel: true);
        fleet.CopyIds(_currentIds);
        _currentCount = fleet.VehicleCount;
        _currentCapturedAtTick = Simulation.CurrentTick;
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
        Array.Resize(ref _instanceIds, size);
    }

    private void BuildInstancesBatch(int batch)
    {
        var map = Heightmap;
        var end = Math.Min((batch + 1) * BatchSize, _drawCount);
        for (var v = batch * BatchSize; v < end; v++)
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
            _instanceIds[v] = _currentIds[v];
        }
    }

    /// <summary>
    /// Rebuilds the industry boxes only when the fleet changed (opening, closing or a new game).
    /// One model box becomes one instance; the shader stretches the unit cube to its extent. Station
    /// placeholders (feature 010) are appended to the same instances, so they need no renderer of their own
    /// until feature 018 replaces them with final models.
    /// </summary>
    private void BuildIndustryInstances()
    {
        var industries = Simulation.Industries;
        var stations = Simulation.Stations;
        var version = (uint)industries.Version | ((long)(stations?.Version ?? 0) << 32);
        if (version == _industryVersion)
        {
            return;
        }

        _industryVersion = version;
        var total = stations is null ? 0 : StationPlaceholders.BoxCount(stations);
        for (var slot = 0; slot < industries.Count; slot++)
        {
            total += Simulation.Content.IndustryKind(industries.KindIndexAt(slot)).Model.Count;
        }

        if (_industryInstances.Length < total)
        {
            Array.Resize(ref _industryInstances, Math.Max(total, Math.Max(64, _industryInstances.Length * 2)));
        }

        var index = 0;
        for (var slot = 0; slot < industries.Count; slot++)
        {
            var kind = Simulation.Content.IndustryKind(industries.KindIndexAt(slot));
            var x = industries.XAt(slot);
            var z = industries.ZAt(slot);
            var ground = Game.Map.HeightAt(x, z);
            foreach (var box in kind.Model)
            {
                var centerX = x + box.X + ((box.Width % 2 == 0) ? 0f : 0.5f);
                var centerZ = z + box.Z + ((box.Depth % 2 == 0) ? 0f : 0.5f);
                _industryInstances[index++] = new IndustryInstance(
                    new Vector4(centerX, ground + box.Y, centerZ, 0f),
                    new Vector4(box.Width, box.Height, box.Depth, 0f),
                    ToColor(box.Color));
            }
        }

        if (stations is not null)
        {
            index = StationPlaceholders.Build(stations, Construction, Game.Map, _industryInstances, index);
        }

        _industryCount = index;
    }

    private static Vector4 ToColor(uint rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
