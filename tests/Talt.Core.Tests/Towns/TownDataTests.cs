using Talt.Core.Determinism;
using Talt.Core.Towns;

namespace Talt.Core.Tests.Towns;

/// <summary>FR-003/FR-005: the town data files load, and broken files report the file and field.</summary>
public class TownDataTests
{
    private static string RulesJson => ReadDefault(TownRules.RulesFileName);

    private static string BuildingsJson => ReadDefault(TownRules.BuildingsFileName);

    [Fact]
    public void DefaultFiles_Load()
    {
        var rules = TownRules.Default;
        Assert.True(rules.Buildings.Count >= 10);
        Assert.Contains(rules.Buildings, b => b.Category == BuildingCategory.House);
        Assert.Contains(rules.Buildings, b => b.Category == BuildingCategory.Apartment);
        Assert.Contains(rules.Buildings, b => b.Category == BuildingCategory.Shop);
        Assert.Contains(rules.Buildings, b => b.Category == BuildingCategory.Office);
        Assert.NotNull(TownNames.Default);
    }

    [Theory]
    [InlineData("\"radiusBase\": 5", "\"radiusBase\": 0", "growth.radiusBase")]
    [InlineData("\"poor\": 400", "\"poor\": 100", "thresholds")]
    [InlineData("\"growth\": {", "\"extra\": 1, \"growth\": {", "unknown field 'extra'")]
    [InlineData("\"weight\": 70,", "\"weight\": 0,", "sizes[0].weight")]
    public void BrokenRules_NameTheField(string find, string replace, string expected)
    {
        var json = RulesJson.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(RulesJson, json);
        var error = Assert.Throws<InvalidDataException>(() => TownRules.Parse(json, BuildingsJson));
        Assert.Contains("towns.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"width\": 2, \"depth\": 1, \"height\": 2,", "\"width\": 3, \"depth\": 1, \"height\": 2,", "width")]
    [InlineData("\"category\": \"house\", \"width\": 1", "\"category\": \"castle\", \"width\": 1", "category")]
    [InlineData("\"era\": [1700, 9000], \"minTownPopulation\": 0", "\"era\": [9000, 1700], \"minTownPopulation\": 0", "era")]
    [InlineData("\"wall\": \"#E2D4B4\"", "\"wall\": \"E2D4B4\"", "model.wall")]
    [InlineData("\"id\": \"brick-terrace\"", "\"id\": \"cottage\"", "defined twice")]
    public void BrokenBuildings_NameTheField(string find, string replace, string expected)
    {
        var json = BuildingsJson.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(BuildingsJson, json);
        var error = Assert.Throws<InvalidDataException>(() => TownRules.Parse(RulesJson, json));
        Assert.Contains("town-buildings.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_AreUniqueDeterministicAndFollowTheStyle()
    {
        var names = TownNames.Default;
        var a = Generate(names, TownNameStyle.Mixed, 3000, 7);
        var b = Generate(names, TownNameStyle.Mixed, 3000, 7);
        Assert.Equal(a, b);
        Assert.Equal(a.Count, a.Distinct().Count());
        Assert.All(a, n => Assert.All(n, c => Assert.True(char.IsLetter(c) || c is ' ' or '-' or '\'')));

        var english = Generate(names, TownNameStyle.English, 200, 1);
        var spanish = Generate(names, TownNameStyle.Spanish, 200, 1);
        Assert.All(english, n => Assert.Contains(names.English.Starts, s => n.Contains(s, StringComparison.Ordinal)));
        Assert.All(spanish, n => Assert.Contains(names.Spanish.Starts, s => n.Contains(s, StringComparison.Ordinal)));
    }

    [Fact]
    public void BrokenNames_NameTheField()
    {
        var json = ReadDefault(TownNames.FileName).Replace("\"Alder\"", "\"Ald3r\"", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => TownNames.Parse(json));
        Assert.Contains("english.starts[0]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_Validate()
    {
        Assert.Throws<ArgumentException>(() => new TownSettings(DebugServicePercent: 101).Validate());
        Assert.True(TownSettings.TryParseNameStyle("spanish", out var style));
        Assert.Equal(TownNameStyle.Spanish, style);
        Assert.False(TownSettings.TryParseSize("1", out _));
        Assert.False(TownSettings.TryParseSize("huge", out _));
    }

    private static List<string> Generate(TownNames names, TownNameStyle style, int count, ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        for (var i = 0; i < count; i++)
        {
            result.Add(names.Generate(style, random, used));
        }

        return result;
    }

    private static string ReadDefault(string fileName)
    {
        using var stream = typeof(TownRules).Assembly.GetManifestResourceStream("Talt.Core.Towns.Data." + fileName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
