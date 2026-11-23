using System.Diagnostics;
using System.Globalization;
using Talt.Core.Determinism;
using Talt.Core.Maps;

namespace Talt.Headless;

/// <summary>
/// The <c>map</c> command: generates a map without a window and prints timing, counts and the
/// content fingerprint (contracts/cli.md), so determinism can be checked in CI.
/// </summary>
internal static class MapCommand
{
    public const string Usage = """
          Talt.Headless map [--map <N|WxD>] [--seed <long>] [--roughness <0-100>] [--max-height <8-127>]
                            [--water <0-90>] [--sea-level <2..>] [--map-text <text>] [--threads <n>] [--build-world]
        """;

    /// <summary>Parses the arguments after <c>map</c>; false with an error message when invalid.</summary>
    public static bool TryParse(string[] args, out MapCommandOptions? options, out string? error)
    {
        options = null;
        error = null;
        var builder = new MapSettingsBuilder();
        int? threads = null;
        var buildWorld = false;

        for (var i = 1; i < args.Length; i++)
        {
            if (MapSettingsBuilder.IsMapOption(args[i]))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value after {args[i]}.";
                    return false;
                }

                if (!builder.TryApply(args[i], args[++i], out error))
                {
                    return false;
                }
            }
            else if (args[i] == "--threads")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1)
                {
                    error = "--threads must be a positive whole number.";
                    return false;
                }

                threads = count;
            }
            else if (args[i] == "--build-world")
            {
                buildWorld = true;
            }
            else
            {
                error = $"Unknown argument '{args[i]}'.";
                return false;
            }
        }

        try
        {
            builder.Current.Validate();
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        options = new MapCommandOptions(builder.Current, threads, buildWorld);
        return true;
    }

    public static int Run(MapCommandOptions options)
    {
        var timings = new StageTimer();
        var stopwatch = Stopwatch.StartNew();
        var map = MapGenerator.Generate(options.Settings, timings, maxDegreeOfParallelism: options.Threads);
        var generated = stopwatch.Elapsed;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Map settings: {options.Settings.ToText()}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Generated {map.Width}x{map.Depth} in {generated.TotalMilliseconds:F0} ms"));
        foreach (var (stage, milliseconds) in timings.Durations())
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {stage,-8} {milliseconds,8:F0} ms"));
        }

        var statistics = map.Statistics;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Sea tiles {statistics.SeaTiles}, lake tiles {statistics.LakeTiles}, river tiles {statistics.RiverTiles} ({statistics.Springs} springs)"));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Sites: {map.TownSites.Count()} towns, {map.IndustrySites.Count()} industries"));
        Console.WriteLine($"Fingerprint: {StateHasher.Format(map.Fingerprint())}");

        if (options.BuildWorld)
        {
            stopwatch.Restart();
            var world = WorldBuilder.Build(map);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"World built in {stopwatch.Elapsed.TotalMilliseconds:F0} ms ({world.ChunkCount} chunks)"));
        }

        return 0;
    }

    /// <summary>Synchronous progress sink that records when each stage started.</summary>
    private sealed class StageTimer : IProgress<MapGenerationProgress>
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(MapGenerationStage Stage, double Start)> _starts = [];
        private MapGenerationStage? _last;

        public void Report(MapGenerationProgress value)
        {
            if (_last != value.Stage)
            {
                _starts.Add((value.Stage, _clock.Elapsed.TotalMilliseconds));
                _last = value.Stage;
            }
        }

        public IEnumerable<(MapGenerationStage Stage, double Milliseconds)> Durations()
        {
            var end = _clock.Elapsed.TotalMilliseconds;
            for (var i = 0; i < _starts.Count; i++)
            {
                var next = i + 1 < _starts.Count ? _starts[i + 1].Start : end;
                yield return (_starts[i].Stage, next - _starts[i].Start);
            }
        }
    }
}

/// <summary>Parsed <c>map</c> command line.</summary>
/// <param name="Settings">What to generate.</param>
/// <param name="Threads">Worker thread limit; null for all cores.</param>
/// <param name="BuildWorld">Also build (and time) the voxel world.</param>
internal sealed record MapCommandOptions(MapSettings Settings, int? Threads, bool BuildWorld);
