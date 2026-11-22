using Talt.Client.Localization;
using Talt.Core.Simulation;

namespace Talt.Client.Tests.Localization;

public class StringsTests
{
    [Fact]
    public void EveryKey_ExistsInEveryLanguage()
    {
        var english = Strings.LoadTable("en");
        foreach (var language in Strings.SupportedLanguages)
        {
            var table = Strings.LoadTable(language);
            Assert.Equal(english.Keys.Order(), table.Keys.Order());
            Assert.All(table.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        }
    }

    [Theory]
    [InlineData("en", "7 Mar 1950")]
    [InlineData("es", "7 mar 1950")]
    public void FormatDate_UsesTheLanguage(string language, string expected)
    {
        var strings = Strings.Load(language);
        Assert.Equal(expected, strings.FormatDate(new GameClock(1950).DateAt((31 + 28 + 6) * 40L)));
    }

    [Fact]
    public void FormatSpeed_CoversEverySpeed()
    {
        var strings = Strings.Load("es");
        foreach (var speed in Enum.GetValues<GameSpeed>())
        {
            Assert.DoesNotContain("[", strings.FormatSpeed(speed), StringComparison.Ordinal);
        }

        Assert.Equal("Avance rápido", strings.FormatSpeed(GameSpeed.FastForward));
    }

    [Fact]
    public void Format_ReplacesPlaceholdersWithCultureNumbers()
    {
        Assert.Equal("20.000 vehículos", Strings.Load("es").Format("status.vehicles", ("count", 20_000)).Replace(' ', '.'));
        Assert.Equal("20,000 vehicles", Strings.Load("en").Format("status.vehicles", ("count", 20_000)));
    }

    [Fact]
    public void UnknownKey_IsShownInBrackets()
    {
        Assert.Equal("[nope]", Strings.Load("en").Get("nope"));
    }

    [Fact]
    public void UnsupportedLanguage_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Strings.Load("fr"));
    }

    [Fact]
    public void EveryCommandTypeAndRejectionReason_HasAText()
    {
        var strings = Strings.Load("en");
        foreach (var key in new[]
        {
            "command.addVehicle", "command.removeVehicle",
            "command.rejected.line-not-found", "command.rejected.invalid-speed",
            "command.rejected.invalid-start-edge", "command.rejected.vehicle-not-found",
        })
        {
            Assert.True(strings.Contains(key), key);
        }
    }
}
