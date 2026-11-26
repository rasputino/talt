using Talt.Core.Content;
using Talt.Core.Demand;

namespace Talt.Core.Tests.Demand;

public sealed class DemandRulesTests
{
    private static string Valid => DemandRules.ReadResource();

    [Fact]
    public void Default_HasPassengersMailAndGoods()
    {
        var rules = DemandRules.Default;
        var content = GameContent.Default;
        Assert.Equal(3, rules.Cargo.Count);
        Assert.True(rules.IsDirected(content.CargoIndex("passengers")));
        Assert.True(rules.IsDirected(content.CargoIndex("mail")));
        Assert.True(rules.IsDirected(content.CargoIndex("goods")));
        Assert.False(rules.IsDirected(content.CargoIndex("food")));
        Assert.False(rules.IsDirected(content.CargoIndex("coal")));
        Assert.Equal(60, rules.ExpiryDays);
        Assert.Equal(500, rules.Cargo[rules.RuleOf(content.CargoIndex("passengers"))].OwnTownPerMille);
        Assert.NotEqual(0UL, rules.Fingerprint);
    }

    [Theory]
    [InlineData("\"id\": \"passengers\"", "\"id\": \"coal\"", "destinationRequired")]
    [InlineData("\"id\": \"passengers\"", "\"id\": \"nothing\"", "unknown cargo")]
    [InlineData("\"source\": \"industries\", \"ownTownPerMille\": 0", "\"source\": \"industries\", \"ownTownPerMille\": 10", "must be 0")]
    [InlineData("\"populationExponent\": 2", "\"populationExponent\": 3", "populationExponent")]
    [InlineData("\"applyDelayDays\": 2", "\"applyDelayDays\": 4", "applyDelayDays")]
    [InlineData("\"maxDestinations\": 32", "\"maxDestinations\": 0", "maxDestinations")]
    [InlineData("\"expiryDays\": 60", "\"expiryDays\": 60, \"extra\": 1", "unknown field")]
    public void Invalid_FilesAreRefusedWithTheField(string find, string replace, string message)
    {
        var json = ReplaceFirst(Valid, find, replace);
        var error = Assert.Throws<InvalidDataException>(() => DemandRules.FromJson(json, GameContent.Default));
        Assert.Contains("demand.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDestinationCargo_IsRefused()
    {
        var json = ReplaceFirst(Valid, "{ \"id\": \"mail\", \"source\": \"buildings\", \"ownTownPerMille\": 500, \"populationExponent\": 2, \"distanceExponent\": 2 },", string.Empty);
        var error = Assert.Throws<InvalidDataException>(() => DemandRules.FromJson(json, GameContent.Default));
        Assert.Contains("mail", error.Message, StringComparison.Ordinal);
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{find}' not found in demand.json");
        return string.Concat(text.AsSpan(0, at), replace, text.AsSpan(at + find.Length));
    }
}
