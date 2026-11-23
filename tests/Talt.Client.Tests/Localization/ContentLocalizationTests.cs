using Talt.Client.Localization;
using Talt.Core.Content;

namespace Talt.Client.Tests.Localization;

/// <summary>
/// Content data references localization keys; this test makes sure every key exists in both
/// languages, not just that the two files have the same keys (feature 009, FR-016).
/// </summary>
public class ContentLocalizationTests
{
    [Fact]
    public void EveryContentKeyExistsInEnglishAndSpanish()
    {
        var content = GameContent.Default;
        var languages = Strings.SupportedLanguages.Select(Strings.Load).ToList();

        foreach (var cargo in content.AllCargo)
        {
            AssertKey(languages, cargo.NameKey);
            AssertKey(languages, cargo.UnitKey);
            AssertKey(languages, CategoryKey(cargo.Category));
        }

        foreach (var kind in content.AllIndustryKinds)
        {
            AssertKey(languages, kind.NameKey);
        }

        foreach (var key in new[] { "vehicle.road", "vehicle.rail", "vehicle.ship", "vehicle.air" })
        {
            AssertKey(languages, key);
        }

        foreach (var key in new[]
                 {
                     "info.industry.title", "info.industry.gone", "info.industry.rate",
                     "info.industry.production", "info.industry.production.none",
                     "info.industry.accepts", "info.industry.accepts.none", "info.industry.recipe",
                     "info.industry.available", "info.industry.pending", "info.industry.lastMonth",
                     "info.cargo.properties",
                 })
        {
            AssertKey(languages, key);
        }
    }

    private static string CategoryKey(CargoCategory category) => category switch
    {
        CargoCategory.RawMaterial => "cargo.category.raw",
        CargoCategory.ProcessedGood => "cargo.category.processed",
        CargoCategory.Passenger => "cargo.category.passenger",
        CargoCategory.Mail => "cargo.category.mail",
        _ => "cargo.category.goods",
    };

    private static void AssertKey(List<Strings> languages, string key)
    {
        foreach (var strings in languages)
        {
            Assert.True(strings.Contains(key), $"Missing '{key}' in {strings.Language}.");
        }
    }
}
