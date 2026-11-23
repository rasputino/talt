using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Industries;

public class IndustryLifecycleTests
{
    [Fact]
    public void NeglectedIndustryDeclinesAndCloses()
    {
        var economy = """
            {
              "driftPerMille": 0,
              "growthPerMille": 0,
              "declineAfterMonths": 0,
              "declinePerMille": 100,
              "closeAfterMonths": 1,
              "closeChancePerMille": 1000,
              "closeAtRatePerMille": 3000,
              "openingChancePerMillePerSite": 0,
              "maxOpeningsPerMonth": 0
            }
            """;
        var (_, fleet) = OneIndustry(economy, siteCount: 1);
        Assert.Equal(1, fleet.Count);

        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));

        Assert.Equal(0, fleet.Count);
        Assert.Equal(1, fleet.FreeSiteCount);
    }

    [Fact]
    public void DeliveriesGrowTheProductionRate()
    {
        var economy = """
            {
              "driftPerMille": 0,
              "growthPerMille": 50,
              "declineAfterMonths": 100,
              "declinePerMille": 0,
              "closeAfterMonths": 200,
              "closeChancePerMille": 0,
              "openingChancePerMillePerSite": 0,
              "maxOpeningsPerMonth": 0
            }
            """;
        var (content, fleet) = OneIndustry(economy, siteCount: 1, acceptsOnly: true);
        var id = fleet.IdAt(0);
        var cargo = content.CargoIndex("test-cargo");
        Assert.Equal(1000, fleet.RatePerMilleAt(0));

        fleet.Deliver(id, cargo, 5);
        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));

        Assert.Equal(1050, fleet.RatePerMilleAt(0));
        Assert.Equal(0, fleet.MonthsNeglectedAt(0));
    }

    [Fact]
    public void NeglectIncrementsTheCounter()
    {
        var economy = """
            {
              "driftPerMille": 0,
              "growthPerMille": 0,
              "declineAfterMonths": 24,
              "declinePerMille": 20,
              "closeAfterMonths": 60,
              "closeChancePerMille": 0,
              "openingChancePerMillePerSite": 0,
              "maxOpeningsPerMonth": 0
            }
            """;
        var (_, fleet) = OneIndustry(economy, siteCount: 1);

        for (var month = 2; month <= 4; month++)
        {
            fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, month));
        }

        Assert.Equal(3, fleet.MonthsNeglectedAt(0));
        Assert.Equal(1000, fleet.RatePerMilleAt(0));
    }

    [Fact]
    public void NewIndustriesOpenOnFreeSites()
    {
        var economy = """
            {
              "initialOccupancyPercent": 0,
              "driftPerMille": 0,
              "openingChancePerMillePerSite": 1000,
              "maxOpeningsPerMonth": 2
            }
            """;
        var (_, fleet) = OneIndustry(economy, siteCount: 3, occupancyPerMille: 0);
        Assert.Equal(0, fleet.Count);

        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));

        Assert.Equal(2, fleet.Count);
        Assert.Equal(1, fleet.FreeSiteCount);
    }

    [Fact]
    public void IndustryOutsideItsEraDoesNotOpen()
    {
        var economy = """
            {
              "initialOccupancyPercent": 0,
              "driftPerMille": 0,
              "openingChancePerMillePerSite": 1000,
              "maxOpeningsPerMonth": 2
            }
            """;
        var industry = IndustryTestData.MinimalIndustry
            .Replace("\"era\": [1800, 9999]", "\"era\": [1800, 1900]", StringComparison.Ordinal);
        var content = IndustryTestData.Content(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry, economy));
        var fleet = IndustryTestData.Fleet(content, siteCount: 1, occupancyPerMille: 0);

        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));

        Assert.Equal(0, fleet.Count);
        Assert.Equal(1, fleet.FreeSiteCount);
    }

    [Fact]
    public void ClosedSitesAreAvailableAgain()
    {
        var economy = """
            {
              "driftPerMille": 0,
              "declineAfterMonths": 0,
              "declinePerMille": 100,
              "closeAfterMonths": 1,
              "closeChancePerMille": 1000,
              "closeAtRatePerMille": 3000,
              "openingChancePerMillePerSite": 0,
              "maxOpeningsPerMonth": 0
            }
            """;
        var (_, fleet) = OneIndustry(economy, siteCount: 2);
        var closed = fleet.IdAt(0);
        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));
        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 3));

        Assert.False(fleet.TryGetIndustry(closed, out _));
        Assert.Equal(2, fleet.FreeSiteCount);
    }

    [Fact]
    public void SameSeedProducesTheSameHistory()
    {
        var economy = """
            {
              "initialOccupancyPercent": 100,
              "driftPerMille": 15,
              "growthPerMille": 25,
              "declineAfterMonths": 1,
              "declinePerMille": 20,
              "closeAfterMonths": 2,
              "closeChancePerMille": 100,
              "openingChancePerMillePerSite": 100,
              "maxOpeningsPerMonth": 2
            }
            """;
        var content = OneIndustryContent(economy);
        var sites = IndustryTestData.Sites(6, SiteKind.IndustryPlain, spacing: 8);
        var map = IndustryTestData.Map(256, 256, sites);
        var first = IndustryFleet.Create(content, map, 1950, new DeterministicRandom(33));
        var second = IndustryFleet.Create(content, map, 1950, new DeterministicRandom(33));

        for (var month = 1; month <= 12; month++)
        {
            first.AdvanceMonth(IndustryTestData.FirstDay(1950, month));
            second.AdvanceMonth(IndustryTestData.FirstDay(1950, month));
        }

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    private static (GameContent Content, IndustryFleet Fleet) OneIndustry(
        string economy,
        int siteCount,
        bool acceptsOnly = false,
        int occupancyPerMille = 1000)
    {
        var content = OneIndustryContent(economy, acceptsOnly);
        return (content, IndustryTestData.Fleet(content, siteCount: siteCount, occupancyPerMille: occupancyPerMille));
    }

    private static GameContent OneIndustryContent(string economy, bool acceptsOnly = false)
    {
        var extra = acceptsOnly
            ? "\"weight\": 1, \"accepts\": [\"test-cargo\"],"
            : "\"weight\": 1, \"production\": [{ \"cargo\": \"test-cargo\", \"perMonth\": 100 }], \"stock\": [{ \"cargo\": \"test-cargo\", \"capacity\": 10000 }],";
        var industry = IndustryTestData.MinimalIndustry.Replace("\"weight\": 1,", extra, StringComparison.Ordinal);
        return IndustryTestData.Content(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry, economy));
    }

    private static string Fingerprint(IndustryFleet fleet)
    {
        var hasher = new StateHasher();
        hasher.Append(fleet);
        return StateHasher.Format(hasher.GetFingerprint());
    }
}
