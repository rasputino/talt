using Talt.Core.Roads;

namespace Talt.Core.Tests.Roads;

public class RoadRulesTests
{
    [Fact]
    public void ShippedFile_HasTheFourTypesOfVersionOne()
    {
        var rules = RoadRules.Default;

        Assert.Equal(["dirt", "paved", "fast", "motorway"], rules.Types.Select(t => t.Id));
        Assert.Equal([1850, 1920, 1960, 1980], rules.Types.Select(t => t.IntroYear));
        Assert.Equal([50, 80, 100, 0], rules.Types.Select(t => t.SpeedKmh));
        Assert.True(rules.Types[3].Motorway);
        Assert.Equal(3, rules.MotorwayType());
    }

    [Theory]
    [InlineData(1900, 8)]
    [InlineData(1950, 16)]
    [InlineData(1984, 16)]
    [InlineData(1985, 32)]
    [InlineData(1800, 0)]
    public void BridgeSpan_GrowsWithTheEra(int year, int span) => Assert.Equal(span, RoadRules.Default.MaxBridgeSpan(year));

    [Fact]
    public void Availability_AndBestOrdinaryType_FollowTheYear()
    {
        var rules = RoadRules.Default;

        Assert.Equal(0, rules.BestOrdinaryType(1900));
        Assert.Equal(1, rules.BestOrdinaryType(1950));
        Assert.Equal(2, rules.BestOrdinaryType(2000));
        Assert.False(rules.IsAvailable(3, 1979));
        Assert.True(rules.IsAvailable(3, 1980));
        Assert.True(rules.IsBetter(2, 1));
        Assert.False(rules.IsBetter(0, 1));
    }

    [Theory]
    [InlineData("\"bogus\": 1,", "unknown field 'bogus'")]
    [InlineData("\"slopeCost\": -1,", "'slopeCost' must be 0 or more")]
    public void InvalidValues_NameTheField(string extra, string message)
    {
        var json = Valid().Replace("\"slopeCost\": 200,", extra.Contains("slopeCost") ? extra : "\"slopeCost\": 200," + extra);

        var error = Assert.Throws<InvalidDataException>(() => RoadRules.Parse(json));

        Assert.Contains(message, error.Message);
        Assert.StartsWith("roads.json:", error.Message);
    }

    [Fact]
    public void UnsortedEras_AndRepeatedIds_AreRejected()
    {
        var unsorted = Valid().Replace("{ \"fromYear\": 1950, \"maxSpan\": 16 }", "{ \"fromYear\": 1800, \"maxSpan\": 16 }");
        var repeated = Valid().Replace("\"id\": \"fast\"", "\"id\": \"paved\"");

        Assert.Contains("sorted by fromYear", Assert.Throws<InvalidDataException>(() => RoadRules.Parse(unsorted)).Message);
        Assert.Contains("repeats the id", Assert.Throws<InvalidDataException>(() => RoadRules.Parse(repeated)).Message);
    }

    [Fact]
    public void BadColour_IsRejected()
    {
        var json = Valid().Replace("\"surface\": \"8a6f4e\"", "\"surface\": \"brown\"");

        Assert.Contains("types[0].look.surface", Assert.Throws<InvalidDataException>(() => RoadRules.Parse(json)).Message);
    }

    private static string Valid()
    {
        using var stream = typeof(RoadRules).Assembly.GetManifestResourceStream("Talt.Core.Roads.Data.roads.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
