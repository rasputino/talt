using Talt.Headless;

namespace Talt.Headless.Tests;

/// <summary>contracts/headless-cli.md argument rules.</summary>
public class HeadlessArgumentsTests
{
    [Fact]
    public void Run_WithDefaults()
    {
        Assert.True(HeadlessArguments.TryParse(["run", "--ticks", "100"], out var options, out _));
        Assert.Equal(HeadlessCommand.Run, options!.Command);
        Assert.Equal(100, options.Ticks);
        Assert.Equal(2026, options.Settings.Seed);
        Assert.Equal(512, options.Settings.MapSizeTiles);
        Assert.False(options.Sequential);
    }

    [Fact]
    public void Run_WithAllOptions()
    {
        Assert.True(HeadlessArguments.TryParse(
            ["run", "--seed", "7", "--map", "4096", "--vehicles", "50000", "--lines", "6000",
             "--start-year", "1975", "--ticks", "10000", "--sequential", "--record", "s.json"],
            out var options, out _));

        Assert.Equal(7, options!.Settings.Seed);
        Assert.Equal(4096, options.Settings.MapSizeTiles);
        Assert.Equal(50_000, options.Settings.VehicleCount);
        Assert.Equal(6_000, options.Settings.EffectiveLineCount);
        Assert.Equal(1975, options.Settings.StartYear);
        Assert.True(options.Sequential);
        Assert.Equal("s.json", options.RecordPath);
    }

    [Fact]
    public void Replay_WithSessionAndCheckpoints()
    {
        Assert.True(HeadlessArguments.TryParse(["replay", "s.json", "--checkpoint-every", "1000"], out var options, out _));
        Assert.Equal(HeadlessCommand.Replay, options!.Command);
        Assert.Equal("s.json", options.SessionPath);
        Assert.Equal(1000, options.CheckpointEvery);
        Assert.Null(options.Ticks);
    }

    [Fact]
    public void Run_WithMapTextSetsMapSettings()
    {
        Assert.True(HeadlessArguments.TryParse(
            ["run", "--ticks", "10", "--map-text", "T1-512x256-s7-r40-h28-w35-l7"],
            out var options,
            out _));

        Assert.NotNull(options!.Settings.Map);
        Assert.Equal(512, options.Settings.Map!.Width);
        Assert.Equal(256, options.Settings.Map.Depth);
        Assert.Equal(7, options.Settings.Map.Seed);
    }

    [Fact]
    public void Run_WithoutMapTextHasNoMap()
    {
        Assert.True(HeadlessArguments.TryParse(["run", "--ticks", "10"], out var options, out _));

        Assert.Null(options!.Settings.Map);
    }

    public static TheoryData<string[]> InvalidArgumentSets => new()
    {
        Array.Empty<string>(),
        new[] { "fly" },
        new[] { "run" },                                  // Missing --ticks.
        new[] { "run", "--ticks", "abc" },                // Not a number.
        new[] { "run", "--ticks", "0" },                  // Not positive.
        new[] { "run", "--ticks", "10", "--map", "100" }, // Invalid map size.
        new[] { "run", "--ticks" },                       // Missing value.
        new[] { "run", "--ticks", "10", "--bogus" },
        new[] { "run", "--ticks", "10", "--map-text", "nope" },
        new[] { "replay" },                               // Missing session file.
    };

    [Theory]
    [MemberData(nameof(InvalidArgumentSets))]
    public void InvalidArguments_AreRejectedWithAMessage(string[] args)
    {
        Assert.False(HeadlessArguments.TryParse(args, out var options, out var error));
        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
