using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class MapSettingsTests
{
    [Fact]
    public void Defaults_AreValid() => new MapSettings().Validate();

    [Theory]
    [InlineData(63, 256, "Width")]
    [InlineData(256, 100, "Depth")]
    [InlineData(16_416, 256, "Width")]
    public void Size_MustBeAMultipleOf32InRange(int width, int depth, string name)
    {
        var settings = new MapSettings(width, depth);
        var exception = Assert.Throws<ArgumentException>(settings.Validate);
        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 28, 25, 7, "Roughness")]
    [InlineData(101, 28, 25, 7, "Roughness")]
    [InlineData(50, 7, 25, 7, "MaxHeight")]
    [InlineData(50, 128, 25, 7, "MaxHeight")]
    [InlineData(50, 28, 91, 7, "WaterAmount")]
    [InlineData(50, 28, -1, 7, "WaterAmount")]
    [InlineData(50, 28, 25, 1, "SeaLevel")]
    [InlineData(50, 28, 25, 27, "SeaLevel")]
    public void Settings_AreRangeChecked_AndErrorsNameTheSetting(int roughness, int maxHeight, int water, int sea, string name)
    {
        var settings = new MapSettings(256, 256, 1, roughness, maxHeight, water, sea);
        var exception = Assert.Throws<ArgumentException>(settings.Validate);
        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
        Assert.Contains("between", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1024, 512, 2026L)]
    [InlineData(256, 256, 0L)]
    [InlineData(4096, 4096, -7L)]
    [InlineData(320, 192, long.MaxValue)]
    [InlineData(320, 192, long.MinValue)]
    public void Text_RoundTrips(int width, int depth, long seed)
    {
        var settings = new MapSettings(width, depth, seed, 33, 90, 40, 11);
        Assert.True(MapSettings.TryParse(settings.ToText(), out var parsed, out var error), error);
        Assert.Equal(settings, parsed);
    }

    [Fact]
    public void Text_HasTheDocumentedShape() =>
        Assert.Equal("T2-1024x512-s2026-r50-h28-w25-l7", new MapSettings(1024, 512, 2026).ToText());

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("T2-1024x512-s2026-r50-h28-w25")]
    [InlineData("T2-1024-s2026-r50-h28-w25-l7")]
    [InlineData("T2-1024x512-sabc-r50-h28-w25-l7")]
    [InlineData("T2-1000x512-s1-r50-h28-w25-l7")]
    public void Text_Invalid_IsRejectedWithAMessage(string text)
    {
        Assert.False(MapSettings.TryParse(text, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Text_OfAnotherGeneratorVersion_IsRefusedClearly()
    {
        Assert.False(MapSettings.TryParse("T99-1024x512-s1-r50-h28-w25-l7", out _, out var error));
        Assert.Contains("version 99", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_AppliesOptionsInOrder_AndTextCanBeOverridden()
    {
        var builder = new MapSettingsBuilder();
        Assert.True(builder.TryApply("--map-text", "T2-512x256-s9-r10-h40-w30-l8", out _));
        Assert.True(builder.TryApply("--water", "50", out _));
        Assert.True(builder.TryApply("--map", "1024", out _));

        Assert.Equal(new MapSettings(1024, 1024, 9, 10, 40, 50, 8), builder.Current);
        Assert.True(builder.SeedGiven);
        Assert.True(builder.SizeGiven);
    }

    [Theory]
    [InlineData("--map", "big")]
    [InlineData("--seed", "x")]
    [InlineData("--water", "1.5")]
    [InlineData("--map-text", "nope")]
    public void Builder_RejectsUnreadableValues(string option, string value)
    {
        Assert.False(new MapSettingsBuilder().TryApply(option, value, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("1024", 1024, 1024)]
    [InlineData("1024x512", 1024, 512)]
    [InlineData("256X4096", 256, 4096)]
    public void Size_IsParsedAsSquareOrWidthByDepth(string text, int width, int depth)
    {
        Assert.True(MapSettingsBuilder.TryParseSize(text, out var w, out var d));
        Assert.Equal((width, depth), (w, d));
    }
}
