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
//   --camera <x,z>               initial camera target tile (default: map centre)
//   --graphics <preset>          graphics preset for this run only: low, medium or high (not saved)
//   --day-night                  turn the day/night cycle on for this run
//   --time-of-day <0-1>          start the day/night cycle at this phase (0.5 noon, 0.9 night)
//   --measure-effects [seconds]  measure the frame cost of each graphics effect, print a table and exit
//   --no-audio                   start with the silent backend (CI and no-device machines)
//   --road-grid <spacing>        debug: lay a full road grid every <spacing> tiles (dense-network FPS check)
//   --town-names <style>         town name style: mixed (default), english or spanish
//   --town-size <size>           starting town sizes: small, normal (default) or large
//   --town-service <0-100>       debug: percent of every town's passengers and mail counted as transported (towns grow)
//   --station-service <0-100>    debug: every station's cargo is served and that percent of waiting cargo is collected
//   --seed-stations <n>          debug: build up to n stations beside industries and town roads at the start
//   --seed-vehicles <n>          debug: bus bays, depots, lines and up to n buses on them (turns infinite money on; feature 012)
//   --check-overlaps             debug: check after every tick that no two road vehicles overlap (stops the game on failure)
//   --sandbox                    the company never goes bankrupt (feature 011)
//   --inflation                  prices and payments rise over the years (feature 011)
//   --infinite-money             the company has infinite money from the start (cannot be turned off; feature 011)
//   --station-service-pays       debug: cargo collected by --station-service is paid as a delivery (feature 011)
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
System.Numerics.Vector2? cameraTarget = null;
GraphicsPreset? graphicsPreset = null;
var dayNight = false;
double? timeOfDay = null;
double? measureEffects = null;
var play = false;
float? uiScale = null;
var uiTestWindows = 0;
var noAudio = false;
int? roadGrid = null;
var townSettings = Talt.Core.Towns.TownSettings.Default;
var townOptionGiven = false;
Talt.Core.Stations.StationSettings? stationSettings = null;
var seedStations = 0;
var seedVehicles = 0;
var checkOverlaps = false;
var financeSettings = Talt.Core.Finance.FinanceSettings.Default;
var mapOptions = new List<(string Name, string Value)>();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--road-grid":
            roadGrid = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--town-names":
            townSettings = townSettings with
            {
                NameStyle = Talt.Core.Towns.TownSettings.TryParseNameStyle(args[++i], out var style)
                    ? style
                    : throw new ArgumentException("--town-names must be mixed, english or spanish."),
            };
            townOptionGiven = true;
            break;
        case "--town-size":
            townSettings = townSettings with
            {
                Size = Talt.Core.Towns.TownSettings.TryParseSize(args[++i], out var size)
                    ? size
                    : throw new ArgumentException("--town-size must be small, normal or large."),
            };
            townOptionGiven = true;
            break;
        case "--town-service":
            townSettings = townSettings with { DebugServicePercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
            townSettings.Validate();
            townOptionGiven = true;
            break;
        case "--station-service":
            stationSettings = new Talt.Core.Stations.StationSettings(int.Parse(args[++i], CultureInfo.InvariantCulture));
            stationSettings.Validate();
            townOptionGiven = true;
            break;
        case "--seed-stations":
            seedStations = int.Parse(args[++i], CultureInfo.InvariantCulture);
            townOptionGiven = true;
            break;
        case "--seed-vehicles":
            seedVehicles = int.Parse(args[++i], CultureInfo.InvariantCulture);
            townOptionGiven = true;
            break;
        case "--check-overlaps":
            checkOverlaps = true;
            break;
        case "--sandbox":
            financeSettings = financeSettings with { Sandbox = true };
            townOptionGiven = true;
            break;
        case "--inflation":
            financeSettings = financeSettings with { Inflation = true };
            townOptionGiven = true;
            break;
        case "--infinite-money":
            financeSettings = financeSettings with { InfiniteMoney = true };
            townOptionGiven = true;
            break;
        case "--station-service-pays":
            financeSettings = financeSettings with { DebugServicePays = true };
            townOptionGiven = true;
            break;
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
        case "--camera":
        {
            var parts = args[++i].Split(',');
            cameraTarget = parts.Length == 2
                ? new System.Numerics.Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture))
                : throw new ArgumentException("--camera must be x,z in tiles.");
            break;
        }

        case "--graphics":
            graphicsPreset = args[++i] switch
            {
                "low" => GraphicsPreset.Low,
                "medium" => GraphicsPreset.Medium,
                "high" => GraphicsPreset.High,
                _ => throw new ArgumentException("--graphics must be low, medium or high."),
            };
            break;
        case "--day-night":
            dayNight = true;
            break;
        case "--time-of-day":
            timeOfDay = double.Parse(args[++i], CultureInfo.InvariantCulture);
            if (timeOfDay is < 0 or > 1)
            {
                throw new ArgumentException("--time-of-day must be from 0 to 1.");
            }

            break;
        case "--measure-effects":
            measureEffects = i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 4;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                i++;
            }

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
var startGame = measureEffects is not null || play || positional.Count > 0 || mapOptions.Count > 0 || townOptionGiven || recordPath is not null || printFingerprintAt is not null;
using var window = new GameWindow(new ClientOptions(
    playerSettings,
    LanguageOverride: language,
    UiScaleOverride: uiScale,
    StartGame: startGame ? new NewGameOptions(chunksPerSide, vehicleCount, seed, startYear, mapOptions.Count > 0 ? mapSettings : null, roadGrid,
        townSettings == Talt.Core.Towns.TownSettings.Default ? null : townSettings, stationSettings, seedStations,
        financeSettings == Talt.Core.Finance.FinanceSettings.Default ? null : financeSettings, seedVehicles) : null,
    Session: new SessionOptions(recordPath, printFingerprintAt, exitAfterFingerprint, checkOverlaps),
    ShowStatistics: showStatistics,
    InitialZoom: zoom,
    InitialCameraTarget: cameraTarget,
    GraphicsOverride: graphicsPreset,
    DayNightOverride: dayNight,
    TimeOfDayStart: timeOfDay,
    MeasureEffectsSeconds: measureEffects,
    UiTestWindows: uiTestWindows,
    NoAudio: noAudio));
window.Run();
return 0;
