using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Simulation;
using Talt.Core.Towns;
using Xunit.Abstractions;

namespace Talt.Core.Tests.Towns;

/// <summary>US2 / SC-003: towns grow only with service, faster with more, and always by the rules.</summary>
public class TownGrowthTests(ITestOutputHelper output)
{
    [Fact]
    public void WithoutService_TownsDoNotGrow()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var before = TownTestKit.TotalPopulation(simulation.Towns!);
        var buildings = simulation.Towns!.Buildings.Count;
        var roads = simulation.Construction!.Roads.RoadTileCount;

        TownTestKit.RunMonths(simulation, 18);

        Assert.Equal(before, TownTestKit.TotalPopulation(simulation.Towns));
        Assert.Equal(buildings, simulation.Towns.Buildings.Count);
        Assert.Equal(roads, simulation.Construction.Roads.RoadTileCount);
    }

    [Fact]
    public void WithService_TownsGrowFasterWithMoreService()
    {
        var at60 = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(DebugServicePercent: 60)));
        var at100 = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(DebugServicePercent: 100)));
        var start = TownTestKit.TotalPopulation(at60.Towns!);
        var roads = at60.Construction!.Roads.RoadTileCount;

        TownTestKit.RunMonths(at60, 24);
        TownTestKit.RunMonths(at100, 24);

        var p60 = TownTestKit.TotalPopulation(at60.Towns!);
        var p100 = TownTestKit.TotalPopulation(at100.Towns!);
        output.WriteLine($"start {start}, after 2 years at 60 % {p60}, at 100 % {p100}; roads {roads} → {at60.Construction.Roads.RoadTileCount}");
        // SC-003: at least 5 % a year at 60 %, and at least 1.5 times that growth at 100 %.
        Assert.True(p60 >= start * 110L / 100, $"60 %: {start} → {p60}");
        Assert.True((p100 - start) * 2 >= (p60 - start) * 3, $"100 % grew {p100 - start}, 60 % grew {p60 - start}");
        Assert.True(at60.Construction.Roads.RoadTileCount > roads, "served towns should extend their streets");
        Assert.Null(TownTestKit.CheckInvariants(at60));
        Assert.Null(TownTestKit.CheckInvariants(at100));
    }

    [Fact]
    public void Growth_IsTheSameInParallelAndSequentialRuns()
    {
        var settings = TownTestKit.Scenario(31, towns: new TownSettings(DebugServicePercent: 80));
        var parallel = GameSimulation.Create(settings);
        var sequential = GameSimulation.Create(settings);

        TownTestKit.RunMonths(parallel, 6, parallel: true);
        TownTestKit.RunMonths(sequential, 6, parallel: false);

        Assert.Equal(sequential.ComputeFingerprint(), parallel.ComputeFingerprint());
    }

    [Fact]
    public void Growth_NeverRemovesOrRetypesWhatIsNotTheTowns()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(DebugServicePercent: 100)));
        var state = simulation.Construction!;
        var map = state.Map;
        var tiles = map.Width * map.Depth;
        var kind = new TileObjectKind[tiles];
        var owner = new TileOwner[tiles];
        var roadType = new int[tiles];
        var trees = new bool[tiles];
        var heights = map.Heights.ToArray();
        for (var t = 0; t < tiles; t++)
        {
            kind[t] = state.Objects.KindAt(t);
            owner[t] = state.Objects.OwnerAt(t);
            roadType[t] = state.Roads.TypeAt(t);
            trees[t] = Scenery.Kind(map.SceneryData[t]) != 0;
        }

        TownTestKit.RunMonths(simulation, 12);

        for (var t = 0; t < tiles; t++)
        {
            Assert.Equal(heights[t], map.Heights[t]); // FR-013: no terraforming.
            if (kind[t] == TileObjectKind.Road)
            {
                // Roads stay roads of the same owner and type (towns only add connections).
                Assert.Equal(TileObjectKind.Road, state.Objects.KindAt(t));
                Assert.Equal(owner[t], state.Objects.OwnerAt(t));
                Assert.Equal(roadType[t], state.Roads.TypeAt(t));
            }

            if (trees[t] && Scenery.Kind(map.SceneryData[t]) == 0)
            {
                // A tree only goes where the town built.
                Assert.Equal(TileOwnerKind.Town, state.Objects.OwnerAt(t).Kind);
            }
        }
    }

    [Fact]
    public void RecordTransported_CountsTowardNextMonthsService()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var towns = simulation.Towns!;
        TownTestKit.RunMonths(simulation, 1);
        Assert.True(towns.TryGetTown(0, out var info));
        Assert.True(info.PassengersGeneratedLastMonth > 0);
        Assert.Equal(0, info.PassengersTransportedLastMonth);

        towns.RecordTransported(0, mail: false, info.PassengersGeneratedLastMonth / 2);
        TownTestKit.RunMonths(simulation, 1);

        Assert.True(towns.TryGetTown(0, out info));
        Assert.Equal(towns.ServiceOf(0), info.ServicePerMille);
        Assert.True(info.ServicePerMille > 0);
        Assert.True(info.PassengersTransportedLastMonth > 0);
    }
}
