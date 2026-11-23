using Talt.Core.Maps;
using Talt.Core.Scenarios;

namespace Talt.Core.Tests.Scenarios;

public class ScenarioSettingsTests
{
    [Fact]
    public void MapSettingsAreValidatedWithTheScenario()
    {
        var settings = new ScenarioSettings(MapSizeTiles: 64, Map: new MapSettings(64, 64, 1, MaxHeight: 127));

        settings.Validate(); // Does not throw.
        Assert.NotNull(settings.Map);
    }

    [Fact]
    public void InvalidMapSettingsAreRejected()
    {
        var settings = new ScenarioSettings(Map: new MapSettings(70, 64, 1)); // Not a multiple of 32.

        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public void EqualityIncludesTheMap()
    {
        var first = new ScenarioSettings(Map: new MapSettings(64, 64, 1));
        var second = new ScenarioSettings(Map: new MapSettings(64, 64, 2));

        Assert.NotEqual(first, second);
        Assert.Equal(first, first with { });
    }
}
