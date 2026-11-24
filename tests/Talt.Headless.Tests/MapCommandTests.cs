namespace Talt.Headless.Tests;

/// <summary>Argument rules of the <c>map</c> command (contracts/cli.md).</summary>
public class MapCommandTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Assert.True(MapCommand.TryParse(["map"], out var options, out _));
        Assert.Null(options!.Threads);
        Assert.False(options.BuildWorld);
    }

    [Fact]
    public void AllOptions_AreRead()
    {
        Assert.True(MapCommand.TryParse(
            ["map", "--map", "1024x512", "--seed", "-3", "--roughness", "70", "--max-height", "96", "--water", "35", "--sea-level", "9", "--threads", "2", "--build-world"],
            out var options,
            out var error), error);

        Assert.Equal("T2-1024x512-s-3-r70-h96-w35-l9", options!.Settings.ToText());
        Assert.Equal(2, options.Threads);
        Assert.True(options.BuildWorld);
    }

    [Fact]
    public void MapText_IsAccepted_AndLaterOptionsOverrideIt()
    {
        Assert.True(MapCommand.TryParse(["map", "--map-text", "T2-512x256-s9-r10-h40-w30-l8", "--water", "50"], out var options, out _));
        Assert.Equal("T2-512x256-s9-r10-h40-w30-l8".Replace("w30", "w50", StringComparison.Ordinal), options!.Settings.ToText());
    }

    [Theory]
    [InlineData("--map", "100")]
    [InlineData("--water", "99")]
    [InlineData("--threads", "0")]
    [InlineData("--bogus", "1")]
    public void Invalid_IsRejectedWithAMessage(string option, string value)
    {
        Assert.False(MapCommand.TryParse(["map", option, value], out var options, out var error));
        Assert.Null(options);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void MissingValue_IsRejected()
    {
        Assert.False(MapCommand.TryParse(["map", "--seed"], out _, out var error));
        Assert.Contains("--seed", error, StringComparison.Ordinal);
    }
}
