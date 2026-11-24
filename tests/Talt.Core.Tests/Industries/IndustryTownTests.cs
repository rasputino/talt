using Talt.Core.Content;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Simulation;
using Talt.Core.Tests.Towns;
using Talt.Core.Towns;

namespace Talt.Core.Tests.Industries;

/// <summary>Every industry belongs to a nearby town that cannot grow into it.</summary>
public class IndustryTownTests
{
    private static readonly Lazy<GameSimulation> Simulation = new(() => GameSimulation.Create(TownTestKit.Scenario(seed: 8, size: 512)));

    private static double Distance(int ax, int az, int bx, int bz) => Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(az - bz, 2));

    [Fact]
    public void MinimumDistance_LeavesRoomForTheClearancePopulation_AndTheLargestFootprint()
    {
        var growth = TownRules.Default.Growth;
        var content = GameContent.Default;
        var largestHalfDiagonal = 0;
        for (var i = 0; i < content.IndustryKindCount; i++)
        {
            var kind = content.IndustryKind(i);
            largestHalfDiagonal = Math.Max(largestHalfDiagonal, (int)Math.Ceiling(Distance(0, 0, kind.FootprintWidth, kind.FootprintDepth) / 2));
        }

        var needed = growth.RadiusAt(growth.IndustryClearancePopulation) + largestHalfDiagonal;
        Assert.True(
            MapGenerationTuning.Default.MinTownIndustryDistance >= needed,
            $"MinTownIndustryDistance {MapGenerationTuning.Default.MinTownIndustryDistance} < {needed} needed for {growth.IndustryClearancePopulation} inhabitants.");
    }

    [Fact]
    public void EveryIndustry_BelongsToItsNearestTown_WithinTheAllowedDistance()
    {
        var simulation = Simulation.Value;
        var industries = simulation.Industries;
        var towns = simulation.Towns!;
        var tuning = MapGenerationTuning.Default;
        Assert.True(industries.Count > 0);
        for (var slot = 0; slot < industries.Count; slot++)
        {
            var town = industries.TownIdAt(slot);
            Assert.InRange(town, 0, towns.Count - 1);
            var (cx, cz) = towns.CentreOf(town);
            var own = Distance(cx, cz, industries.XAt(slot), industries.ZAt(slot));
            Assert.InRange(own, tuning.MinTownIndustryDistance, tuning.MaxTownIndustryDistance);
            for (var other = 0; other < towns.Count; other++)
            {
                var (ox, oz) = towns.CentreOf(other);
                Assert.True(Distance(ox, oz, industries.XAt(slot), industries.ZAt(slot)) >= own, $"Industry {industries.IdAt(slot)} is nearer town {other} than its own town {town}.");
            }

            Assert.True(industries.TryGetIndustry(industries.IdAt(slot), out var info));
            Assert.Equal(town, info.TownId);
        }
    }

    [Fact]
    public void TownsAtTheClearancePopulation_DoNotReachAnyIndustry()
    {
        var simulation = Simulation.Value;
        var industries = simulation.Industries;
        var towns = simulation.Towns!;
        var radius = towns.Rules.Growth.RadiusAt(towns.Rules.Growth.IndustryClearancePopulation);
        for (var slot = 0; slot < industries.Count; slot++)
        {
            Assert.True(industries.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ));
            for (var town = 0; town < towns.Count; town++)
            {
                var (cx, cz) = towns.CentreOf(town);
                var nearestX = Math.Clamp(cx, minX, maxX - 1);
                var nearestZ = Math.Clamp(cz, minZ, maxZ - 1);
                Assert.True(Distance(cx, cz, nearestX, nearestZ) > radius, $"Town {town} at {radius} tiles would reach industry {industries.IdAt(slot)}.");
            }
        }
    }

    [Fact]
    public void NearestTowns_PicksTheClosestSite_AndTheLowerIndexOnTies()
    {
        Site[] towns = [new(0, 0, SiteKind.Town, 4, 0), new(100, 0, SiteKind.Town, 4, 0)];
        Site[] industries = [new(30, 0, SiteKind.IndustryPlain, 2, 0), new(50, 0, SiteKind.IndustryPlain, 2, 0), new(90, 5, SiteKind.IndustryPlain, 2, 0)];

        Assert.Equal([0, 0, 1], IndustryPlacement.NearestTowns(industries, towns));
        Assert.Equal([-1, -1, -1], IndustryPlacement.NearestTowns(industries, []));
    }
}
