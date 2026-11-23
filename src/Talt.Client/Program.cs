using System.Globalization;
using Talt.Client;
using Talt.Core.Maps;

// Transit & Logistics Tycoon client. Without game arguments it opens on the main menu (feature 005);
// game arguments start a game straight away (development and the CI smoke test).
//
// Usage: Talt.Client [chunks-per-side] [vehicles] [seed] [options]
//   --start-year <year>          first in-game year (default 1950)
//   --language en|es             interface language for this run (default: saved setting or system language)
//   --play                       start a game with the default options without showing the menu
//   --ui-scale <0.75-2>          interface scale for this run
//   --ui-test-windows <n>        open n sample windows in game (measures the interface cost)
//   --unlock-fast-forward        unlock the separate fast-forward control (saved in settings)
//   --lock-fast-forward          lock it again (saved in settings)
//   --record [file]              record the session (default session.talt-session.json)
//   --print-fingerprint-at <n>   print the state fingerprint after tick n
//   --exit-after-fingerprint     close the window right after that fingerprint is printed (smoke test)
//   --stats                      show render statistics from the start (same as pressing F3)
//   --zoom <distance>            initial camera distance ("max" for the farthest zoom)
//   --no-audio                   start with the silent backend (CI and no-device machines)
// Map options (feature 004); any of them starts a game straight away:
//   --map <N|WxD>                map size in tiles, multiples of 32, 256 to 16384 (default from chunks-per-side)
//   --seed <number>              map seed (default 2026; the map settings text is printed to share the map)
//   --roughness <0-100>          terrain detail and relief (default 50)
//   --max-height <8-127>         highest terrain in voxels (default 28)
//   --water <0-90>               percent of the map under the sea (default 25)
//   --sea-level <2..>            height of the sea surface in voxels (default 7)
//   --map-text <text>            all map settings as one text
var positional = new List<int>();
var startYear = 1950;
string? language = null;
bool? unlockFastForward = null;
string? recordPath = null;
long? printFingerprintAt = null;
var exitAfterFingerprint = false;
var showStatistics = false;
float? zoom = null;
var play = false;
float? uiScale = null;
var uiTestWindows = 0;
var noAudio = false;
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
        case "--print-fingerprint-at":
            printFingerprintAt = long.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--exit-after-fingerprint":
            exitAfterFingerprint = true;
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
        case "--play":
            play = true;
            break;
        case "--ui-scale":
            uiScale = float.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--ui-test-windows":
            uiTestWindows = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--no-audio":
            noAudio = true;
            break;
        default:
            positional.Add(int.Parse(args[i], CultureInfo.InvariantCulture));
            break;
    }
}

var chunksPerSide = positional.Count > 0 ? positional[0] : 16;
var vehicleCount = positional.Count > 1 ? positional[1] : 20_000;
var seed = positional.Count > 2 ? positional[2] : 2026;

// Map settings: the positional chunks-per-side and seed are the defaults, map options override them.
var mapBuilder = new MapSettingsBuilder(new MapSettings(chunksPerSide * 32, chunksPerSide * 32, seed));
foreach (var (name, value) in mapOptions)
{
    if (!mapBuilder.TryApply(name, value, out var optionError))
    {
        Console.Error.WriteLine($"Invalid map settings: {optionError}");
        return 2;
    }
}

var mapSettings = mapBuilder.Current;
try
{
    mapSettings.Validate();
    if (mapBuilder.SizeGiven && Math.Min(mapSettings.Width, mapSettings.Depth) < MapSettings.PlayerMinimumSize)
    {
        throw new ArgumentException($"Map sides must be at least {MapSettings.PlayerMinimumSize} tiles.");
    }
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Invalid map settings: {exception.Message}");
    return 2;
}

// The simulation seed keeps following the (small) seed so recorded sessions and fingerprints do not change.
if (mapBuilder.SeedGiven)
{
    seed = (int)(mapSettings.Seed ^ (mapSettings.Seed >> 32));
}

var playerSettings = PlayerSettings.Load(PlayerSettings.DefaultPath);
if (unlockFastForward is { } unlock)
{
    playerSettings.FastForwardUnlocked = unlock;
    playerSettings.Save(PlayerSettings.DefaultPath);
}

if (language is not null && !Talt.Client.Localization.Strings.SupportedLanguages.Contains(language))
{
    throw new ArgumentException($"Unsupported language '{language}'.");
}

if (uiScale is { } scale && !PlayerSettings.IsValidScale(scale))
{
    throw new ArgumentException("--ui-scale must be one of 0.75, 1, 1.25, 1.5, 1.75 or 2.");
}

// Any game argument skips the menu (contracts/command-line.md).
var startGame = play || positional.Count > 0 || mapOptions.Count > 0 || recordPath is not null || printFingerprintAt is not null;
using var window = new GameWindow(new ClientOptions(
    playerSettings,
    LanguageOverride: language,
    UiScaleOverride: uiScale,
    StartGame: startGame ? new NewGameOptions(chunksPerSide, vehicleCount, seed, startYear, mapOptions.Count > 0 ? mapSettings : null) : null,
    Session: new SessionOptions(recordPath, printFingerprintAt, exitAfterFingerprint),
    ShowStatistics: showStatistics,
    InitialZoom: zoom,
    UiTestWindows: uiTestWindows,
    NoAudio: noAudio));
window.Run();
return 0;
