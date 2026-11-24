using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Simulation;
using Talt.Core.Towns;
using Xunit.Abstractions;

namespace Talt.Core.Tests.Towns;

/// <summary>US1 / SC-001: every town site holds a named town grown to its starting size, built by the rules.</summary>
public class TownFoundingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(-5)]
    public void EveryTownSite_HoldsANamedTownWithValidBuildings(int seed)
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(seed));
        var towns = simulation.Towns!;
        var sites = simulation.Construction!.Map.Sites.Count(s => s.Kind == SiteKind.Town);

        Assert.Equal(sites, towns.Count);
        Assert.True(towns.Count > 0);
        var names = new HashSet<string>();
        for (var town = 0; town < towns.Count; town++)
        {
            Assert.False(string.IsNullOrWhiteSpace(towns.NameOf(town)));
            Assert.True(names.Add(towns.NameOf(town)), $"duplicate name {towns.NameOf(town)}");
            output.WriteLine($"{towns.NameOf(town)} ({towns.PatternOf(town)}): {towns.PopulationOf(town)} people, {towns.BuildingCountOf(town)} buildings, radius {towns.RadiusOf(town)}");
        }

        Assert.Null(TownTestKit.CheckInvariants(simulation));
        Assert.True(towns.Buildings.Count >= towns.Count, "every town should have at least one building");
    }

    [Fact]
    public void SameSettings_FoundTheSameTowns()
    {
        var a = GameSimulation.Create(TownTestKit.Scenario(12));
        var b = GameSimulation.Create(TownTestKit.Scenario(12));

        Assert.Equal(a.ComputeFingerprint(), b.ComputeFingerprint());
        for (var town = 0; town < a.Towns!.Count; town++)
        {
            Assert.Equal(a.Towns.NameOf(town), b.Towns!.NameOf(town));
            Assert.Equal(a.Towns.PopulationOf(town), b.Towns.PopulationOf(town));
        }
    }

    [Fact]
    public void SizeOption_ScalesTheStartingPopulation()
    {
        var small = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(Size: TownSize.Small)));
        var normal = GameSimulation.Create(TownTestKit.Scenario(8));
        var large = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(Size: TownSize.Large)));

        var s = TownTestKit.TotalPopulation(small.Towns!);
        var n = TownTestKit.TotalPopulation(normal.Towns!);
        var l = TownTestKit.TotalPopulation(large.Towns!);
        output.WriteLine($"small {s}, normal {n}, large {l}");
        Assert.True(s < n && n < l, $"small {s}, normal {n}, large {l}");
    }

    [Fact]
    public void Towns_OwnTheirStreetsAndBuildings()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var state = simulation.Construction!;
        var towns = simulation.Towns!;
        for (var slot = 0; slot < towns.Buildings.Count; slot++)
        {
            var tile = state.Map.Index(towns.Buildings.XAt(slot), towns.Buildings.ZAt(slot));
            Assert.Equal(TileOwner.Town(towns.Buildings.TownAt(slot)), state.Objects.OwnerAt(tile));
        }

        Assert.True(state.Roads.RoadTileCount > 0);
    }

    [Fact]
    public void MapLessScenario_HasNoTowns()
    {
        var simulation = GameSimulation.Create(new Talt.Core.Scenarios.ScenarioSettings(VehicleCount: 10));
        Assert.Null(simulation.Towns);
    }
}
