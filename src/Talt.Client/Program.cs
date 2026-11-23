using System.Diagnostics;
using System.Globalization;
using Talt.Client;
using Talt.Client.Localization;
using Talt.Core.Commands;
using Talt.Core.Maps;
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
//   --stats                      show render statistics from the start (same as pressing F3)
//   --zoom <distance>            initial camera distance ("max" for the farthest zoom)
// New-game options (feature 004; the in-window new-game screen comes after the UI foundation):
//   --map <N|WxD>                map size in tiles, multiples of 32, 256 to 16384 (default from chunks-per-side)
//   --seed <number>              map seed (default: random, printed so the map can be shared)
//   --roughness <0-100>          terrain detail and relief (default 50)
//   --max-height <8-127>         highest terrain in voxels (default 28)
//   --water <0-90>               percent of the map under the sea (default 25)
//   --sea-level <2..>            height of the sea surface in voxels (default 7)
//   --map-text <text>            all map settings as one text (printed after generation)
var positional = new List<int>();
var startYear = 1950;
string? language = null;
bool? unlockFastForward = null;
string? recordPath = null;
long? printFingerprintAt = null;
var exitAfterFingerprint = false;
var showStatistics = false;
float? zoom = null;
var mapOptions = new List<(string Name, string Value)>();

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
        case "--zoom":
            i++;
            zoom = args[i] == "max" ? float.MaxValue : float.Parse(args[i], CultureInfo.InvariantCulture);
            break;
        case "--stats":
            showStatistics = true;
            break;
        case "--map":
        case "--seed":
        case "--roughness":
        case "--max-height":
        case "--water":
        case "--sea-level":
        case "--map-text":
            mapOptions.Add((args[i], args[++i]));
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
var strings = Strings.Load(language ?? Strings.DetectLanguage());

// Map settings: positional chunks-per-side gives the default size, options override.
var mapBuilder = new MapSettingsBuilder(new MapSettings(
    Width: chunksPerSide * Chunk.Size,
    Depth: chunksPerSide * Chunk.Size,
    Seed: positional.Count > 2 ? positional[2] : 0,
    Roughness: 50,
    MaxHeight: 28,
    WaterAmount: 25,
    SeaLevel: 7));
foreach (var (name, value) in mapOptions)
{
    if (!mapBuilder.TryApply(name, value, out var optionError))
    {
        return FailMapSettings(optionError!);
    }
}

var mapSettings = mapBuilder.Current;
if (mapBuilder.SizeGiven && Math.Min(mapSettings.Width, mapSettings.Depth) < MapSettings.PlayerMinimumSize)
{
    return FailMapSettings($"Map sides must be at least {MapSettings.PlayerMinimumSize} tiles.");
}

if (positional.Count <= 2 && !mapBuilder.SeedGiven)
{
    // No seed given: pick one at random (outside the deterministic core) and show it below.
    mapSettings = mapSettings with { Seed = Random.Shared.NextInt64(0, 1L << 40) };
}

try
{
    mapSettings.Validate();
}
catch (ArgumentException exception)
{
    return FailMapSettings(exception.Message);
}

// The simulation seed keeps its old default so recorded sessions and fingerprints do not change.
var seed = mapBuilder.SeedGiven || positional.Count > 2 ? (int)(mapSettings.Seed ^ (mapSettings.Seed >> 32)) : 2026;
var playerSettings = PlayerSettings.Load(PlayerSettings.DefaultPath);
if (unlockFastForward is { } unlock)
{
    playerSettings.FastForwardUnlocked = unlock;
    playerSettings.Save(PlayerSettings.DefaultPath);
}

var settings = new ScenarioSettings(
    Seed: seed,
    StartYear: startYear,
    MapSizeTiles: Math.Min(mapSettings.Width, mapSettings.Depth),
    VehicleCount: vehicleCount);
var stopwatch = Stopwatch.StartNew();

var simulation = GameSimulation.Create(settings);
Report($"Created simulation ({simulation.Network.NodeCount:N0} junctions, {simulation.Vehicles.LineCount:N0} lines, {simulation.Vehicles.VehicleCount:N0} vehicles)");

Console.WriteLine(strings.Format("map.settings", ("text", mapSettings.ToText())));
var lastPercent = -1;
var mapProgress = new SynchronousProgress<MapGenerationProgress>(p =>
{
    // World building is the last tenth of the bar; generation fills the first nine tenths.
    var fraction = p.Stage == MapGenerationStage.World ? 0.9f + (0.1f * p.Fraction) : 0.9f * p.Fraction;
    var percent = (int)(fraction * 100f);
    if (percent != lastPercent && (percent - lastPercent >= 5 || percent == 100))
    {
        lastPercent = percent;
        Console.WriteLine(strings.Format("map.progress", ("percent", percent.ToString("D3", CultureInfo.InvariantCulture)), ("stage", strings.Get($"stage.map.{p.Stage}"))));
    }
});
var map = MapGenerator.Generate(mapSettings, mapProgress);
var world = WorldBuilder.Build(map, mapProgress);
NetworkTerrainPainter.PaintRoads(world, simulation.Network);
var heightmap = Heightmap.FromMapData(map);
Console.WriteLine(strings.Format(
    "map.generated",
    ("width", map.Width),
    ("depth", map.Depth),
    ("rivers", map.Statistics.RiverTiles),
    ("towns", map.TownSites.Count()),
    ("industries", map.IndustrySites.Count())));
Report($"Generated {map.Width}×{map.Depth} terrain ({world.ChunkCount} chunks)");

// No meshing here: the window opens now and the renderer fills the map in, nearest first (FR-012).
SessionRecorder? recorder = null;
if (recordPath is not null)
{
    recorder = new SessionRecorder(new RecordedSession(settings));
    simulation.AttachRecorder(recorder);
}

using var window = new GameWindow(new PrototypeScene(
    world, heightmap, simulation, strings, playerSettings, recorder, recordPath, printFingerprintAt, exitAfterFingerprint, showStatistics, zoom));
window.Run();
return 0;

int FailMapSettings(string message)
{
    Console.Error.WriteLine(strings.Format("map.error", ("message", message)));
    return 2;
}

void Report(string message)
{
    Console.WriteLine($"{message} in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    stopwatch.Restart();
}

/// <summary>Progress sink that runs the callback on the reporting thread (the console printer needs no marshalling).</summary>
internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
