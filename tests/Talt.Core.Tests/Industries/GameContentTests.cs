using Talt.Core.Content;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Industries;

public class GameContentTests
{
    [Fact]
    public void DefaultContent_LoadsTheExpectedCatalogues()
    {
        var content = GameContent.Default;

        Assert.Equal(13, content.CargoCount);
        Assert.Equal(11, content.IndustryKindCount);
        Assert.Equal(CargoCategory.Passenger, content.Cargo(content.CargoIndex("passengers")).Category);
        Assert.True(content.Cargo(content.CargoIndex("passengers")).DestinationRequired);
        Assert.False(content.Cargo(content.CargoIndex("coal")).DestinationRequired);

        var coalMine = content.IndustryKind(content.IndustryKindIndex("coal-mine"));
        Assert.Equal(180, coalMine.Production.Single(p => p.CargoId == "coal").PerMonth);
        Assert.Equal(900, coalMine.CapacityFor("coal"));
        Assert.Contains(SiteKind.IndustryHills, coalMine.SiteKinds);

        var steelMill = content.IndustryKind(content.IndustryKindIndex("steel-mill"));
        Assert.True(steelMill.AcceptsCargo("coal"));
        Assert.True(steelMill.AcceptsCargo("iron-ore"));
        Assert.Equal(new RecipeDefinition("iron-ore", 3, "steel", 2), steelMill.RecipeFor("iron-ore"));
        Assert.Null(steelMill.RecipeFor("coal"));

        var powerStation = content.IndustryKind(content.IndustryKindIndex("power-station"));
        Assert.Empty(powerStation.Production);
        Assert.Empty(powerStation.Recipes);
        Assert.True(powerStation.AcceptsCargo("coal"));
    }

    [Fact]
    public void DefaultContent_EveryReferencedKeyIsSet()
    {
        foreach (var cargo in GameContent.Default.AllCargo)
        {
            Assert.False(string.IsNullOrWhiteSpace(cargo.NameKey));
            Assert.False(string.IsNullOrWhiteSpace(cargo.UnitKey));
            Assert.NotEqual(0u, cargo.Color);
        }

        foreach (var kind in GameContent.Default.AllIndustryKinds)
        {
            Assert.False(string.IsNullOrWhiteSpace(kind.NameKey));
            Assert.NotEmpty(kind.Model);
            Assert.NotEmpty(kind.SiteKinds);
        }
    }

    [Fact]
    public void Fingerprint_ChangesWhenContentChanges()
    {
        var first = Content();
        var second = GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo.Replace("\"perDay\"", "\"perDay\"", StringComparison.Ordinal)),
            IndustryTestData.IndustryFile(IndustryTestData.MinimalIndustry));

        Assert.Equal(first.Fingerprint(), second.Fingerprint());
        var changed = GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo.Replace("\"unit.tonnes\"", "\"unit.crates\"", StringComparison.Ordinal)),
            IndustryTestData.IndustryFile(IndustryTestData.MinimalIndustry));
        Assert.NotEqual(first.Fingerprint(), changed.Fingerprint());
    }

    [Fact]
    public void DuplicateCargoId_IsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile($"{IndustryTestData.RawCargo},{IndustryTestData.RawCargo}"),
            IndustryTestData.IndustryFile(IndustryTestData.MinimalIndustry)));
        Assert.Contains("duplicate cargo id", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DestinationFlagMustMatchCategory()
    {
        var passenger = IndustryTestData.RawCargo
            .Replace("\"category\": \"raw\"", "\"category\": \"passenger\"", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(passenger),
            IndustryTestData.IndustryFile(IndustryTestData.MinimalIndustry)));
        Assert.Contains("destinationRequired", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownVehicleKind_IsRejected()
    {
        var cargo = IndustryTestData.RawCargo.Replace("\"road\"", "\"teleport\"", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(cargo),
            IndustryTestData.IndustryFile(IndustryTestData.MinimalIndustry)));
        Assert.Contains("vehicle kind", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateIndustryId_IsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile($"{IndustryTestData.MinimalIndustry},{IndustryTestData.MinimalIndustry}")));
        Assert.Contains("duplicate industry id", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownRecipeCargo_IsRejected()
    {
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"weight\": 1,", "\"weight\": 1, \"accepts\": [\"test-cargo\"], \"recipes\": [{ \"input\": \"test-cargo\", \"inputAmount\": 1, \"output\": \"missing\", \"outputAmount\": 1 }], \"stock\": [{ \"cargo\": \"test-cargo\", \"capacity\": 10 }],", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry)));
        Assert.Contains("unknown cargo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionWithoutStock_IsRejected()
    {
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"weight\": 1,", "\"weight\": 1, \"production\": [{ \"cargo\": \"test-cargo\", \"perMonth\": 10 }],", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry)));
        Assert.Contains("no positive stock capacity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FootprintOutsideTheReservedSquare_IsRejected()
    {
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"footprint\": [1, 1]", "\"footprint\": [6, 1]", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry)));
        Assert.Contains("footprint", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelBoxOutsideTheFootprint_IsRejected()
    {
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"width\": 1, \"depth\": 1, \"height\": 1", "\"width\": 2, \"depth\": 1, \"height\": 1", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry)));
        Assert.Contains("does not fit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownSiteKind_IsRejected()
    {
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"plain\"", "\"swamp\"", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry)));
        Assert.Contains("site kind", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EconomyWithInvalidRates_IsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(
                IndustryTestData.MinimalIndustry,
                """{"minRatePerMille": 2000, "maxRatePerMille": 1000}""")));
        Assert.Contains("minRatePerMille", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecipeInputMustBeAccepted()
    {
        var otherCargo = IndustryTestData.RawCargo.Replace("\"id\": \"test-cargo\"", "\"id\": \"other-cargo\"", StringComparison.Ordinal);
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"weight\": 1,", "\"weight\": 1, \"recipes\": [{ \"input\": \"test-cargo\", \"inputAmount\": 1, \"output\": \"other-cargo\", \"outputAmount\": 1 }], \"stock\": [{ \"cargo\": \"test-cargo\", \"capacity\": 10 }, { \"cargo\": \"other-cargo\", \"capacity\": 10 }],", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => GameContent.FromJson(
            IndustryTestData.CargoFile($"{IndustryTestData.RawCargo},{otherCargo}"),
            IndustryTestData.IndustryFile(industry)));
        Assert.Contains("does not accept", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GameContent Content() => GameContent.FromJson(
        IndustryTestData.CargoFile(IndustryTestData.RawCargo),
        IndustryTestData.IndustryFile(IndustryTestData.MinimalIndustry));
}
