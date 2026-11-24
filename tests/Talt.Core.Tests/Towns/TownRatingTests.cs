using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Simulation;
using Talt.Core.Towns;

namespace Talt.Core.Tests.Towns;

/// <summary>US4: ratings fall with demolition, town roads and trees, recover monthly and gate actions.</summary>
public class TownRatingTests
{
    private const int Player = Company.PlayerId;

    [Fact]
    public void DemolishingATownBuilding_RemovesItWhole_CostsAndLowersTheRating()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var towns = simulation.Towns!;
        var state = simulation.Construction!;
        var slot = LargestBuilding(towns);
        var town = towns.Buildings.TownAt(slot);
        var id = towns.Buildings.IdAt(slot);
        var definition = towns.Buildings.TypeDefinitionAt(slot);
        var x = towns.Buildings.XAt(slot);
        var z = towns.Buildings.ZAt(slot);
        var population = towns.PopulationOf(town);
        var balance = state.Player.Balance;
        var rules = towns.Rules.Ratings;

        // Aim at one tile only: the whole footprint goes.
        var command = new ClearAreaCommand(Player, x, z, x, z);
        var preview = new ConstructionPlan();
        Assert.True(command.Preview(state, new ConstructionPlanner(), preview).Succeeded);
        Assert.Equal(definition.TileCount, preview.ChangeCount);
        Assert.Equal(-rules.BuildingPenalty, preview.RatingChange);

        simulation.Submit(command);
        simulation.Tick();

        Assert.Equal(-1, towns.Buildings.SlotOf(id));
        Assert.Equal(population - definition.Residents, towns.PopulationOf(town));
        Assert.Equal(rules.Initial - rules.BuildingPenalty, towns.RatingOf(town, Player));
        Assert.Equal(balance - (state.Rules.DemolishCost(TileObjectKind.TownBuilding) * definition.TileCount), state.Player.Balance);
        Assert.Equal(TileObjectKind.None, state.Objects.KindAt(state.Map.Index(x, z)));
        Assert.False(state.Undo.HasValue); // Demolishing town buildings cannot be undone.
        Assert.Null(TownTestKit.CheckInvariants(simulation));
    }

    [Fact]
    public void BelowTheDemolitionMinimum_TheTownRefuses()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var towns = simulation.Towns!;
        var state = simulation.Construction!;
        var rules = towns.Rules.Ratings;
        var town = towns.Buildings.TownAt(0);

        // Demolish until the rating drops below the minimum.
        var guard = 0;
        while (towns.MayDemolish(town, Player) && guard++ < 50)
        {
            var slot = FirstBuildingOf(towns, town);
            simulation.Submit(new ClearAreaCommand(Player, towns.Buildings.XAt(slot), towns.Buildings.ZAt(slot), towns.Buildings.XAt(slot), towns.Buildings.ZAt(slot)));
            simulation.Tick();
        }

        Assert.True(towns.RatingOf(town, Player) < rules.DemolishMinimum);
        Assert.Equal(TownRatingBand.Poor, towns.BandOf(towns.RatingOf(town, Player)));
        var next = FirstBuildingOf(towns, town);
        var refused = new ClearAreaCommand(Player, towns.Buildings.XAt(next), towns.Buildings.ZAt(next), towns.Buildings.XAt(next), towns.Buildings.ZAt(next));
        var result = refused.Preview(state, new ConstructionPlanner(), new ConstructionPlan());
        Assert.False(result.Succeeded);
        Assert.Equal(ConstructionReasons.TownRefuses, result.ReasonCode);

        // Town roads are refused too.
        var (rx, rz) = FirstRoadOf(state, town);
        var removal = new RemoveRoadCommand(Player, rx, rz, rx, rz).Preview(state, new ConstructionPlanner(), new ConstructionPlan());
        Assert.False(removal.Succeeded);
        Assert.Equal(ConstructionReasons.TownRefuses, removal.ReasonCode);
        Assert.True(towns.MayBuildStation(town, Player) == towns.RatingOf(town, Player) >= rules.StationMinimum);
    }

    [Fact]
    public void Ratings_RecoverTowardNeutral_AndRiseWithService()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var towns = simulation.Towns!;
        var rules = towns.Rules.Ratings;
        var slot = FirstBuildingOf(towns, 0);
        simulation.Submit(new ClearAreaCommand(Player, towns.Buildings.XAt(slot), towns.Buildings.ZAt(slot), towns.Buildings.XAt(slot), towns.Buildings.ZAt(slot)));
        simulation.Tick();
        var low = towns.RatingOf(0, Player);
        Assert.True(low < rules.Initial);

        TownTestKit.RunMonths(simulation, 1);
        Assert.Equal(Math.Min(rules.Initial, low + rules.RecoveryPerMonth), towns.RatingOf(0, Player));
        TownTestKit.RunMonths(simulation, 12);
        Assert.Equal(rules.Initial, towns.RatingOf(0, Player));

        var served = GameSimulation.Create(TownTestKit.Scenario(8, towns: new TownSettings(DebugServicePercent: 100)));
        TownTestKit.RunMonths(served, 30);
        Assert.Equal(rules.Initial + rules.ServiceTargetSpan, served.Towns!.RatingOf(0, Player));
        Assert.Equal(TownRatingBand.Excellent, served.Towns.BandOf(served.Towns.RatingOf(0, Player)));
    }

    [Fact]
    public void ClearingTreesInATownZone_LowersTheRating_UpToTheCap()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var towns = simulation.Towns!;
        var state = simulation.Construction!;
        var map = state.Map;
        var rules = towns.Rules.Ratings;

        // The town with the most trees in its zone.
        var bestTown = -1;
        var bestTrees = 0;
        for (var town = 0; town < towns.Count; town++)
        {
            var (cx, cz) = towns.CentreOf(town);
            var r = towns.RadiusOf(town) + rules.ZoneMargin;
            var trees = 0;
            for (var z = cz - r; z <= cz + r; z++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    if (map.Contains(x, z) && Scenery.Kind(map.SceneryAt(x, z)) != 0 && towns.TownZoneAt(x, z) == town)
                    {
                        trees++;
                    }
                }
            }

            if (trees > bestTrees)
            {
                bestTrees = trees;
                bestTown = town;
            }
        }

        Assert.True(bestTrees > 0);
        var (tx, tz) = towns.CentreOf(bestTown);
        var reach = towns.RadiusOf(bestTown) + rules.ZoneMargin;
        // A clear that only contains trees (and skips other objects).
        simulation.Submit(new ClearAreaCommand(Player, tx - reach, tz - reach, tx + reach, tz + reach));
        var before = towns.RatingOf(bestTown, Player);
        simulation.Tick();
        var drop = before - towns.RatingOf(bestTown, Player);
        Assert.True(drop > 0);
        Assert.True(drop <= rules.MaxPenaltyPerAction);
    }

    [Fact]
    public void RemovingATownRoad_LowersTheRating()
    {
        var simulation = GameSimulation.Create(TownTestKit.Scenario(8));
        var towns = simulation.Towns!;
        var state = simulation.Construction!;
        for (var tile = 0; tile < state.Map.Width * state.Map.Depth; tile++)
        {
            var owner = state.Objects.OwnerAt(tile);
            if (state.Objects.KindAt(tile) != TileObjectKind.Road || owner.Kind != TileOwnerKind.Town)
            {
                continue;
            }

            var x = tile % state.Map.Width;
            var z = tile / state.Map.Width;
            var command = new RemoveRoadCommand(Player, x, z, x, z);
            if (!command.Preview(state, new ConstructionPlanner(), new ConstructionPlan()).Succeeded)
            {
                continue; // The town needs it to stay connected.
            }

            var town = owner.Index;
            simulation.Submit(command);
            simulation.Tick();
            Assert.Equal(towns.Rules.Ratings.Initial - towns.Rules.Ratings.RoadPenalty, towns.RatingOf(town, Player));
            return;
        }

        Assert.Fail("No removable town road found.");
    }

    private static int LargestBuilding(TownRegistry towns)
    {
        var best = 0;
        for (var slot = 1; slot < towns.Buildings.Count; slot++)
        {
            if (towns.Buildings.TypeDefinitionAt(slot).TileCount > towns.Buildings.TypeDefinitionAt(best).TileCount)
            {
                best = slot;
            }
        }

        return best;
    }

    private static int FirstBuildingOf(TownRegistry towns, int town)
    {
        for (var slot = 0; slot < towns.Buildings.Count; slot++)
        {
            if (towns.Buildings.TownAt(slot) == town)
            {
                return slot;
            }
        }

        throw new InvalidOperationException("The town has no buildings left.");
    }

    private static (int X, int Z) FirstRoadOf(ConstructionState state, int town)
    {
        for (var tile = 0; tile < state.Map.Width * state.Map.Depth; tile++)
        {
            if (state.Roads.HasRoad(tile) && state.Objects.OwnerAt(tile) == TileOwner.Town(town))
            {
                return (tile % state.Map.Width, tile / state.Map.Width);
            }
        }

        throw new InvalidOperationException("The town has no roads.");
    }
}
