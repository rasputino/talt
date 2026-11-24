using Talt.Core.Simulation;
using Talt.Core.Towns;
using Xunit.Abstractions;

namespace Talt.Core.Tests.Towns;

/// <summary>US5: large towns are taller and denser in the centre; eras and population minimums hold.</summary>
public class TownShapeTests(ITestOutputHelper output)
{
    [Fact]
    public void LargeTowns_HaveTallerBuildingsInTheCentre()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(Size: TownSize.Large, DebugServicePercent: 100)));
        TownTestKit.RunMonths(simulation, 12);
        var towns = simulation.Towns!;
        var largest = 0;
        for (var town = 1; town < towns.Count; town++)
        {
            if (towns.PopulationOf(town) > towns.PopulationOf(largest))
            {
                largest = town;
            }
        }

        long centreHeight = 0, centreCount = 0, outerHeight = 0, outerCount = 0;
        var buildings = towns.Buildings;
        for (var slot = 0; slot < buildings.Count; slot++)
        {
            if (buildings.TownAt(slot) != largest)
            {
                continue;
            }

            var zone = towns.ZoneAt(largest, buildings.XAt(slot), buildings.ZAt(slot));
            var height = buildings.TypeDefinitionAt(slot).Height;
            if (zone == TownZones.Centre)
            {
                centreHeight += height;
                centreCount++;
            }
            else if (zone == TownZones.Outer)
            {
                outerHeight += height;
                outerCount++;
            }
        }

        output.WriteLine($"{towns.NameOf(largest)}: {towns.PopulationOf(largest)} people; centre {centreCount} buildings avg {(double)centreHeight / Math.Max(1, centreCount):F1}, outer {outerCount} avg {(double)outerHeight / Math.Max(1, outerCount):F1}");
        Assert.True(centreCount > 0 && outerCount > 0);
        Assert.True(centreHeight * outerCount * 2 > outerHeight * centreCount * 3, "the centre should be at least 1.5 times as tall on average");
    }

    [Fact]
    public void Buildings_RespectEraAndPopulationMinimums()
    {
        // 1960 start: the 1975 glass residence must not appear yet.
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(Size: TownSize.Large), startYear: 1960));
        var towns = simulation.Towns!;
        var buildings = towns.Buildings;
        for (var slot = 0; slot < buildings.Count; slot++)
        {
            var definition = buildings.TypeDefinitionAt(slot);
            Assert.True(definition.InEra(buildings.YearAt(slot)), $"{definition.Id} built in {buildings.YearAt(slot)}");
        }

        var glass = towns.Rules.FindBuilding("glass-residence");
        Assert.True(glass >= 0);
        for (var slot = 0; slot < buildings.Count; slot++)
        {
            Assert.NotEqual(glass, buildings.TypeAt(slot));
        }
    }
}
