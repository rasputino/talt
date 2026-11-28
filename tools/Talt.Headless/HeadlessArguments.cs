using System.Globalization;
using Talt.Core.Maps;
using Talt.Core.Scenarios;

namespace Talt.Headless;

internal enum HeadlessCommand
{
    Run,
    Replay,

    /// <summary>Loads a save and continues it (feature 014).</summary>
    Load,
}

/// <summary>Parsed command line (contracts/headless-cli.md).</summary>
internal sealed record HeadlessOptions(
    HeadlessCommand Command,
    ScenarioSettings Settings,
    long? Ticks,
    bool Sequential,
    string? RecordPath,
    string? SessionPath,
    int CheckpointEvery,
    int SeedStations = 0,
    int SeedVehicles = 0,
    int? RoadGrid = null,
    bool CheckOverlaps = false,
    int SeedTrains = 0,
    string? SavePath = null);

/// <summary>Parses the headless runner's arguments without side effects (unit-testable).</summary>
internal static class HeadlessArguments
{
    public const string Usage = """
        Usage:
          Talt.Headless run [--seed <int>] [--map <tiles>] [--vehicles <int>] [--lines <int>]
                            [--start-year <int>] [--map-text <T2-...>] --ticks <int> [--sequential]
                            [--record <session.json>] [--town-names mixed|english|spanish]
                            [--town-size small|normal|large] [--town-service <0-100>]
                            [--station-service <0-100>] [--seed-stations <n>]
                            [--road-grid <spacing>] [--seed-vehicles <n>] [--seed-trains <n>] [--check-overlaps]
                            [--sandbox] [--inflation] [--infinite-money] [--station-service-pays]
          Talt.Headless replay <session.json> [--ticks <int>] [--checkpoint-every <int>] [--sequential]
          Talt.Headless load <save.talt> [--ticks <int>] [--checkpoint-every <int>] [--sequential] [--save <file.talt>]
        run also takes [--checkpoint-every <int>] and [--save <file.talt>] (feature 014).
        """;

    public static bool TryParse(string[] args, out HeadlessOptions? options, out string? error)
    {
        options = null;
        error = null;
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            error = "Missing command (run, replay or load).";
            return false;
        }

        var command = args[0] switch
        {
            "run" => HeadlessCommand.Run,
            "replay" => HeadlessCommand.Replay,
            "load" => HeadlessCommand.Load,
            _ => (HeadlessCommand?)null,
        };
        if (command is null)
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        var defaults = new ScenarioSettings();
        int seed = defaults.Seed, vehicles = defaults.VehicleCount, startYear = defaults.StartYear;
        int? map = null;
        int? lines = null;
        long? ticks = null;
        var sequential = false;
        var checkpointEvery = 0;
        string? record = null;
        string? session = null;
        string? save = null;
        string? mapText = null;
        var towns = Talt.Core.Towns.TownSettings.Default;
        var stationService = 0;
        var seedStations = 0;
        var seedVehicles = 0;
        var seedTrains = 0;
        int? roadGrid = null;
        var checkOverlaps = false;
        var finance = Talt.Core.Finance.FinanceSettings.Default;

        try
        {
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seed": seed = ParseInt(args, ref i); break;
                    case "--map": map = ParseInt(args, ref i); break;
                    case "--vehicles": vehicles = ParseInt(args, ref i); break;
                    case "--lines": lines = ParseInt(args, ref i); break;
                    case "--start-year": startYear = ParseInt(args, ref i); break;
                    case "--ticks": ticks = ParseLong(args, ref i); break;
                    case "--checkpoint-every": checkpointEvery = ParseInt(args, ref i); break;
                    case "--sequential": sequential = true; break;
                    case "--record": record = Next(args, ref i); break;
                    case "--save": save = Next(args, ref i); break;
                    case "--map-text": mapText = Next(args, ref i); break;
                    case "--town-names":
                        towns = towns with
                        {
                            NameStyle = Talt.Core.Towns.TownSettings.TryParseNameStyle(Next(args, ref i), out var style)
                                ? style
                                : throw new FormatException("--town-names must be mixed, english or spanish."),
                        };
                        break;
                    case "--town-size":
                        towns = towns with
                        {
                            Size = Talt.Core.Towns.TownSettings.TryParseSize(Next(args, ref i), out var size)
                                ? size
                                : throw new FormatException("--town-size must be small, normal or large."),
                        };
                        break;
                    case "--town-service": towns = towns with { DebugServicePercent = ParseInt(args, ref i) }; break;
                    case "--station-service": stationService = ParseInt(args, ref i); break;
                    case "--seed-stations": seedStations = ParseInt(args, ref i); break;
                    case "--seed-vehicles": seedVehicles = ParseInt(args, ref i); break;
                    case "--seed-trains": seedTrains = ParseInt(args, ref i); break;
                    case "--road-grid": roadGrid = ParseInt(args, ref i); break;
                    case "--check-overlaps": checkOverlaps = true; break;
                    case "--sandbox": finance = finance with { Sandbox = true }; break;
                    case "--inflation": finance = finance with { Inflation = true }; break;
                    case "--infinite-money": finance = finance with { InfiniteMoney = true }; break;
                    case "--station-service-pays": finance = finance with { DebugServicePays = true }; break;
                    default:
                        if (command is HeadlessCommand.Replay or HeadlessCommand.Load && session is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            session = args[i];
                            break;
                        }

                        error = $"Unknown argument '{args[i]}'.";
                        return false;
                }
            }
        }
        catch (FormatException exception)
        {
            error = exception.Message;
            return false;
        }

        if (ticks is <= 0 || checkpointEvery < 0)
        {
            error = "--ticks must be positive and --checkpoint-every cannot be negative.";
            return false;
        }

        var settings = new ScenarioSettings(Seed: seed, StartYear: startYear, MapSizeTiles: map ?? defaults.MapSizeTiles, VehicleCount: vehicles, LineCount: lines,
            Towns: towns == Talt.Core.Towns.TownSettings.Default ? null : towns,
            Stations: stationService == 0 ? null : new Talt.Core.Stations.StationSettings(stationService),
            Finance: finance == Talt.Core.Finance.FinanceSettings.Default ? null : finance);
        if (mapText is not null)
        {
            if (!MapSettings.TryParse(mapText, out var parsedMap, out var mapError))
            {
                error = $"Invalid map text: {mapError}";
                return false;
            }

            // The road network must fit inside the map (feature 006): without --map it covers the
            // map's shorter side, as in the client's new-game options.
            settings = settings with
            {
                Map = parsedMap,
                MapSizeTiles = map ?? Math.Min(parsedMap!.Width, parsedMap.Depth),
            };
        }

        if (command == HeadlessCommand.Run)
        {
            if (ticks is null)
            {
                error = "run requires --ticks.";
                return false;
            }

            try
            {
                settings.Validate();
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }
        else if (session is null)
        {
            error = command == HeadlessCommand.Load ? "load requires a save file." : "replay requires a session file.";
            return false;
        }

        if (seedStations < 0 || seedVehicles < 0 || seedTrains < 0)
        {
            error = seedStations < 0 ? "--seed-stations cannot be negative." : "--seed-vehicles cannot be negative.";
            return false;
        }

        if (seedVehicles > 0 && seedTrains > 0)
        {
            // Both seeders predict station and line ids from the same unbuilt state: the trains would get the buses' stops and line.
            error = "--seed-vehicles and --seed-trains cannot be combined.";
            return false;
        }

        if (roadGrid is < 2)
        {
            error = "--road-grid must be at least 2.";
            return false;
        }

        options = new HeadlessOptions(command.Value, settings, ticks, sequential, record, session, checkpointEvery, seedStations, seedVehicles, roadGrid, checkOverlaps, seedTrains, save);
        return true;
    }

    private static string Next(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new FormatException($"Missing value after {args[i - 1]}.");

    private static int ParseInt(string[] args, ref int i)
    {
        var text = Next(args, ref i);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid integer for {args[i - 1]}.");
    }

    private static long ParseLong(string[] args, ref int i)
    {
        var text = Next(args, ref i);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid integer for {args[i - 1]}.");
    }
}
