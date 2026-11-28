using Talt.Headless;

namespace Talt.Headless.Tests;

/// <summary>Feature 014: <c>run --save</c> and <c>load</c> (contracts/save-api.md, headless tool).</summary>
public class SaveLoadCommandTests
{
    [Fact]
    public void Run_accepts_a_save_path_and_checkpoints()
    {
        Assert.True(HeadlessArguments.TryParse(["run", "--ticks", "100", "--checkpoint-every", "50", "--save", "game.talt"], out var options, out _));
        Assert.Equal("game.talt", options!.SavePath);
        Assert.Equal(50, options.CheckpointEvery);
    }

    [Fact]
    public void Load_takes_a_save_file_and_optional_ticks()
    {
        Assert.True(HeadlessArguments.TryParse(["load", "game.talt", "--ticks", "1000", "--checkpoint-every", "100", "--save", "again.talt", "--sequential"], out var options, out _));
        Assert.Equal(HeadlessCommand.Load, options!.Command);
        Assert.Equal("game.talt", options.SessionPath);
        Assert.Equal(1000, options.Ticks);
        Assert.Equal("again.talt", options.SavePath);
        Assert.True(options.Sequential);

        Assert.True(HeadlessArguments.TryParse(["load", "game.talt"], out var bare, out _));
        Assert.Null(bare!.Ticks);
    }

    [Fact]
    public void Load_without_a_file_is_refused()
    {
        Assert.False(HeadlessArguments.TryParse(["load", "--ticks", "10"], out _, out var error));
        Assert.Contains("save file", error, StringComparison.Ordinal);
    }
}
