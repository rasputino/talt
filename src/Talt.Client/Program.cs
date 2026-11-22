using System.Diagnostics;
using System.Globalization;
using Talt.Client;
using Talt.Client.Localization;
using Talt.Core.Commands;
using Talt.Core.Meshing;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

// Technical prototype: voxel terrain with a road grid and simulated vehicles, rendered with WebGPU.
//
// Usage: Talt.Client [chunks-per-side] [vehicles] [seed] [options]
//   --start-year <year>          first in-game year (default 1950)
//   --language en|es             interface language (default: system language)
//   --unlock-fast-forward        unlock the separate fast-forward control (saved in settings)
//   --lock-fast-forward          lock it again (saved in settings)
//   --record [file]              record the session (default session.talt-session.json)
//   --print-fingerprint-at <n>   print the state fingerprint after tick n
//   --exit-after-fingerprint     close the window right after that fingerprint is printed (smoke test)
var positional = new List<int>();
var startYear = 1950;
string? language = null;
bool? unlockFastForward = null;
string? recordPath = null;
long? printFingerprintAt = null;
var exitAfterFingerprint = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--start-year":
            startYear = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--language":
            language = args[++i];
            break;
        case "--unlock-fast-forward":
            unlockFastForward = true;
            break;
        case "--lock-fast-forward":
            unlockFastForward = false;
            break;
        case "--record":
            recordPath = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "session.talt-session.json";
            break;
        case "--print-fingerprint-at":
            printFingerprintAt = long.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--exit-after-fingerprint":
            exitAfterFingerprint = true;
            break;
        default:
            positional.Add(int.Parse(args[i], CultureInfo.InvariantCulture));
            break;
    }
}

var chunksPerSide = positional.Count > 0 ? positional[0] : 16;
var vehicleCount = positional.Count > 1 ? positional[1] : 20_000;
var seed = positional.Count > 2 ? positional[2] : 2026;

var strings = Strings.Load(language ?? Strings.DetectLanguage());
var playerSettings = PlayerSettings.Load(PlayerSettings.DefaultPath);
if (unlockFastForward is { } unlock)
{
    playerSettings.FastForwardUnlocked = unlock;
    playerSettings.Save(PlayerSettings.DefaultPath);
}

var settings = new ScenarioSettings(
    Seed: seed,
    StartYear: startYear,
    MapSizeTiles: chunksPerSide * Chunk.Size,
    VehicleCount: vehicleCount);
var stopwatch = Stopwatch.StartNew();

var simulation = GameSimulation.Create(settings);
Report($"Created simulation ({simulation.Network.NodeCount:N0} junctions, {simulation.Vehicles.LineCount:N0} lines, {simulation.Vehicles.VehicleCount:N0} vehicles)");

var world = TerrainGenerator.Generate(new TerrainSettings(Seed: seed, ChunksX: chunksPerSide, ChunksZ: chunksPerSide));
NetworkTerrainPainter.PaintRoads(world, simulation.Network);
var heightmap = Heightmap.FromWorld(world, settings.MapSizeTiles, settings.MapSizeTiles);
Report($"Generated {settings.MapSizeTiles}×{settings.MapSizeTiles} terrain ({world.ChunkCount} chunks)");

// The world is only read while meshing, so chunks can be meshed in parallel.
var meshes = world.Chunks
    .AsParallel()
    .Select(chunk => ChunkMesher.Build(world, chunk))
    .Where(mesh => !mesh.IsEmpty)
    .ToList();
Report($"Meshed {meshes.Sum(m => m.FaceCount):N0} faces");

SessionRecorder? recorder = null;
if (recordPath is not null)
{
    recorder = new SessionRecorder(new RecordedSession(settings));
    simulation.AttachRecorder(recorder);
}

using var window = new GameWindow(new PrototypeScene(
    meshes, heightmap, simulation, strings, playerSettings, recorder, recordPath, printFingerprintAt, exitAfterFingerprint));
window.Run();

void Report(string message)
{
    Console.WriteLine($"{message} in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    stopwatch.Restart();
}
