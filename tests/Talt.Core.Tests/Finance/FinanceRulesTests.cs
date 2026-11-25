using Talt.Core.Content;
using Talt.Core.Finance;
using Talt.Core.Roads;
using Talt.Core.Stations;

namespace Talt.Core.Tests.Finance;

public sealed class FinanceRulesTests
{
    private static FinanceRules Parse(string json) => FinanceRules.FromJson(json, GameContent.Default, RoadRules.Default, StationRules.Default);

    private static string Shipped => FinanceRules.ReadResource();

    [Fact]
    public void ShippedFile_IsValid_AndHasTheClarifiedValues()
    {
        var rules = FinanceRules.Default;
        Assert.Equal(0, rules.StartLoan);
        Assert.Equal(500_000, rules.MaxLoan);
        Assert.Equal(10_000, rules.LoanStep);
        Assert.Equal(50, rules.InterestPerMille);
        Assert.Equal(GameContent.Default.CargoCount, rules.Payments.Count);
        Assert.Equal(RoadRules.Default.Types.Count, rules.RoadMaintenance.Count);
        Assert.Equal(3, rules.WarningMonths);
        Assert.Equal(6, rules.GameOverMonths);
        Assert.Equal(200, rules.CostInflationBasisPoints);
        Assert.Equal(150, rules.PaymentInflationBasisPoints);
        Assert.Equal(100, rules.InflationYears);
        Assert.Equal(40, rules.DebugServiceDistance);
        Assert.Equal(rules.Fingerprint, Parse(Shipped).Fingerprint);
    }

    [Theory]
    [InlineData("\"step\": 10000", "\"step\": 0", "'loan.step'")]
    [InlineData("\"max\": 500000", "\"max\": 505000", "multiples of 'loan.step'")]
    [InlineData("\"interestPerMille\": 50", "\"interestPerMille\": 2000", "'loan.interestPerMille'")]
    [InlineData("\"coal\": { \"rate\": 2400", "\"coalx\": { \"rate\": 2400", "unknown cargo 'coalx'")]
    [InlineData("\"mail\": { \"rate\": 4600, \"freeDays\": 3 },", "", "'payments.mail' is missing")]
    [InlineData("\"dirt\": 30,", "", "'maintenance.roads.dirt' is missing")]
    [InlineData("\"small\": 1800", "\"tiny\": 1800", "unknown id 'tiny'")]
    [InlineData("\"warningMonths\": 3", "\"warningMonths\": 9", "'bankruptcy.warningMonths'")]
    [InlineData("\"years\": 100", "\"years\": 100, \"extra\": 1", "unknown field 'inflation.extra'")]
    [InlineData("\"propertyValuePercent\": 50", "\"propertyValuePercent\": 101", "'propertyValuePercent'")]
    public void InvalidValues_NameTheField(string from, string to, string expected)
    {
        Assert.Contains(from, Shipped, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => Parse(Shipped.Replace(from, to, StringComparison.Ordinal)));
        Assert.StartsWith("finance.json:", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingAValue_ChangesTheFingerprint() =>
        Assert.NotEqual(FinanceRules.Default.Fingerprint, Parse(Shipped.Replace("\"interestPerMille\": 50", "\"interestPerMille\": 51", StringComparison.Ordinal)).Fingerprint);
}
