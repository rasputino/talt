using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Industries;

public class IndustryPlacementTests
{
    [Fact]
    public void EverySuitableSiteCanHoldOneIndustryWithoutOverlap()
    {
        var sites = IndustryTestData.Sites(100, SiteKind.IndustryPlain, spacing: 8);
        var map = IndustryTestData.Map(512, 512, sites);
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(7), initialOccupancyPerMille: 1000);

        Assert.Equal(sites.Length, fleet.Count);
        var seenTiles = new HashSet<(int X, int Z)>();
        for (var slot = 0; slot < fleet.Count; slot++)
        {
            Assert.True(fleet.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ));
            Assert.True(minX >= 0 && minZ >= 0 && maxX <= map.Width && maxZ <= map.Depth);
            var kind = GameContent.Default.IndustryKind(fleet.KindIndexAt(slot));
            Assert.Contains(SiteKind.IndustryPlain, kind.SiteKinds);
            for (var z = minZ; z < maxZ; z++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    Assert.True(seenTiles.Add((x, z)), $"tile {x},{z} is used by two industries.");
                    Assert.True(fleet.TryGetIndustryAt(x, z, out var owner));
                    Assert.Equal(fleet.IdAt(slot), owner);
                }
            }
        }
    }

    [Fact]
    public void SameSeedPlacesTheSameIndustries()
    {
        var sites = IndustryTestData.Sites(80, SiteKind.IndustryPlain);
        var map = IndustryTestData.Map(512, 512, sites);
        var first = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(42));
        var second = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(42));

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void DefaultOccupancyIsAboutThreeQuarters()
    {
        var sites = IndustryTestData.Sites(400, SiteKind.IndustryPlain);
        var map = IndustryTestData.Map(512, 512, sites);
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(11));

        Assert.InRange(fleet.Count, 260, 340);
        Assert.Equal(sites.Length - fleet.Count, fleet.FreeSiteCount);
    }

    [Fact]
    public void EraFiltersKinds()
    {
        var content = GameContent.Default;
        var site = new Site(32, 32, SiteKind.IndustryPlain, 2, 128);
        var random = new DeterministicRandom(5);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 500; i++)
        {
            var kind = IndustryPlacement.ChooseKind(content, site, 1850, 512, 512, random);
            Assert.True(kind >= 0);
            var definition = content.IndustryKind(kind);
            Assert.True(definition.EraStart <= 1850 && definition.EraEnd >= 1850);
            Assert.DoesNotContain(definition.Id, new[] { "oil-well", "refinery", "factory", "food-plant", "power-station" });
            seen.Add(definition.Id);
        }

        Assert.NotEmpty(seen);
    }

    [Fact]
    public void SiteKindRestrictsTheChoice()
    {
        var content = GameContent.Default;
        var coast = new Site(32, 32, SiteKind.IndustryCoast, 2, 128);
        var forest = new Site(64, 64, SiteKind.IndustryForest, 2, 128);
        var random = new DeterministicRandom(9);

        for (var i = 0; i < 200; i++)
        {
            var coastKind = content.IndustryKind(IndustryPlacement.ChooseKind(content, coast, 1950, 512, 512, random));
            var forestKind = content.IndustryKind(IndustryPlacement.ChooseKind(content, forest, 1950, 512, 512, random));
            Assert.Contains(SiteKind.IndustryCoast, coastKind.SiteKinds);
            Assert.Contains(SiteKind.IndustryForest, forestKind.SiteKinds);
        }
    }

    [Fact]
    public void NonSquareMapKeepsFootprintsInside()
    {
        var sites = IndustryTestData.Sites(40, SiteKind.IndustryPlain, width: 256, depth: 512);
        var map = IndustryTestData.Map(256, 512, sites);
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(3), initialOccupancyPerMille: 1000);

        for (var slot = 0; slot < fleet.Count; slot++)
        {
            Assert.True(fleet.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ));
            Assert.InRange(minX, 0, 255);
            Assert.InRange(maxX, 1, 256);
            Assert.InRange(minZ, 0, 511);
            Assert.InRange(maxZ, 1, 512);
        }
    }

    [Fact]
    public void TownSitesAreNotUsedForIndustries()
    {
        var sites = new[]
        {
            new Site(32, 32, SiteKind.Town, 4, 128),
            new Site(64, 64, SiteKind.IndustryPlain, 2, 128),
        };
        var map = IndustryTestData.Map(512, 512, sites);
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(1), initialOccupancyPerMille: 1000);

        Assert.Equal(1, fleet.SiteCount);
        Assert.Equal(1, fleet.Count);
    }

    private static string Fingerprint(IndustryFleet fleet)
    {
        var hasher = new StateHasher();
        hasher.Append(fleet);
        return StateHasher.Format(hasher.GetFingerprint());
    }
}
