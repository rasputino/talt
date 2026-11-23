using Talt.Core.Determinism;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

/// <summary>
/// Fixed maps whose fingerprints must be identical on every platform (Windows, Linux x64/arm64,
/// macOS arm64: they run in CI). When the generator changes on purpose, bump
/// <see cref="MapSettings.GeneratorVersion"/>, regenerate these with <c>Talt.Headless map --map-text ...</c>
/// and say so in the commit message.
/// </summary>
public class MapGoldenTests
{
    [Theory]
    [InlineData("T1-256x256-s1-r50-h28-w25-l7", "AA298F1670226CF7")]
    [InlineData("T1-512x256-s2026-r70-h96-w35-l9", "423765DB137BCEE8")]
    [InlineData("T1-256x512-s-42-r20-h127-w10-l12", "4E49AE227B595307")]
    public void Fingerprint_MatchesTheRecordedValue(string text, string expected)
    {
        Assert.True(MapSettings.TryParse(text, out var settings, out var error), error);
        var map = MapGenerator.Generate(settings!);
        Assert.Equal(expected, StateHasher.Format(map.Fingerprint()));
    }
}
