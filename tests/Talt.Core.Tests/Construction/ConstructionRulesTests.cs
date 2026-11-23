using Talt.Core.Construction;

namespace Talt.Core.Tests.Construction;

/// <summary>The construction data file (research R13, contracts/data-files.md).</summary>
public class ConstructionRulesTests
{
    private const string Valid = """
        {
          // comment
          "startingBalance": 500000, "terraformStepCost": 250, "clearTreeCost": 40,
          "demolishCosts": { "publicRoad": 0, "test": 100 },
          "undoWindowTicks": 200, "maxHeight": 120, "maxTilesPerAction": 16384, "maxActionCost": 1000000000000
        }
        """;

    [Fact]
    public void DefaultFile_Loads()
    {
        var rules = ConstructionRules.Default;

        Assert.Equal(500_000, rules.StartingBalance);
        Assert.True(rules.TerraformStepCost > 0);
        Assert.True(rules.MaxHeight <= ConstructionRules.HardMaxHeight);
        Assert.Equal(100, rules.DemolishCost(TileObjectKind.Test));
    }

    [Fact]
    public void ValidText_Parses()
    {
        var rules = ConstructionRules.Parse(Valid);

        Assert.Equal(200, rules.UndoWindowTicks);
        Assert.Equal(0, rules.DemolishCost(TileObjectKind.PublicRoad));
    }

    [Theory]
    [InlineData("\"startingBalance\": 500000", "\"startingBalance\": -1", "startingBalance")]
    [InlineData("\"terraformStepCost\": 250", "\"terraformStepCost\": -5", "terraformStepCost")]
    [InlineData("\"clearTreeCost\": 40", "\"clearTreeCost\": 1.5", "clearTreeCost")]
    [InlineData("\"undoWindowTicks\": 200", "\"undoWindowTicks\": -1", "undoWindowTicks")]
    [InlineData("\"maxHeight\": 120", "\"maxHeight\": 128", "maxHeight")]
    [InlineData("\"maxTilesPerAction\": 16384", "\"maxTilesPerAction\": 0", "maxTilesPerAction")]
    [InlineData("\"maxActionCost\": 1000000000000", "\"maxActionCost\": 0", "maxActionCost")]
    [InlineData("\"test\": 100", "\"castle\": 100", "castle")]
    [InlineData("\"test\": 100", "\"test\": -100", "demolishCosts.test")]
    [InlineData("\"clearTreeCost\": 40,", "\"clearTreeCost\": 40, \"bonus\": 1,", "bonus")]
    [InlineData("\"clearTreeCost\": 40,", "", "clearTreeCost")]
    public void InvalidValues_NameTheField(string original, string replacement, string field)
    {
        var error = Assert.Throws<InvalidDataException>(() => ConstructionRules.Parse(Valid.Replace(original, replacement, StringComparison.Ordinal)));

        Assert.Contains(field, error.Message, StringComparison.Ordinal);
        Assert.Contains(ConstructionRules.FileName, error.Message, StringComparison.Ordinal);
    }
}
