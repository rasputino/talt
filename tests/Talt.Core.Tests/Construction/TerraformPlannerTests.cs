using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Tests.Maps;

namespace Talt.Core.Tests.Construction;

/// <summary>Terraform rules of research R5: area changes, cascade, costs, blockers and limits (US2).</summary>
public class TerraformPlannerTests
{
    private readonly ConstructionPlanner _planner = new();
    private readonly ConstructionPlan _plan = new();

    [Fact]
    public void RaiseOneTile_OnFlatLand_RaisesOnlyThatTile()
    {
        var state = new TestMap().State();

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 5, 5));

        Assert.True(_plan.IsValid);
        var change = Assert.Single(_plan.Changes.ToArray());
        Assert.Equal(Index(state, 5, 5), change.Index);
        Assert.Equal(10, change.OldHeight);
        Assert.Equal(11, change.NewHeight);
        Assert.Equal(TestMap.Rules.TerraformStepCost, _plan.Cost);
    }

    [Fact]
    public void RaiseTwice_CascadesToTheNeighbours()
    {
        var state = new TestMap().Height(5, 5, 11).State();

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 5, 5));

        // 5,5 goes to 12; its four neighbours must reach 11.
        Assert.Equal(5, _plan.ChangeCount);
        Assert.Equal(12, HeightAfter(state, 5, 5));
        Assert.Equal(11, HeightAfter(state, 4, 5));
        Assert.Equal(11, HeightAfter(state, 5, 6));
        Assert.Equal(5 * TestMap.Rules.TerraformStepCost, _plan.Cost);
    }

    [Fact]
    public void LowerArea_CascadesOutward_AndCostsEveryStep()
    {
        var state = new TestMap().State();

        Plan(state, TerraformMode.Lower, new TileRect(10, 10, 12, 12));

        Assert.True(_plan.IsValid);
        Assert.Equal(9, _plan.ChangeCount); // Neighbours at 10 are within one step of 9.
        Assert.Equal(9 * TestMap.Rules.TerraformStepCost, _plan.Cost);
    }

    [Fact]
    public void LevelArea_TakesTheStartHeight_AndKeepsSlopesAround()
    {
        var map = new TestMap(64, 10);
        for (var z = 0; z < 64; z++)
        {
            for (var x = 0; x < 64; x++)
            {
                map.Height(x, z, 10 + (x / 2)); // A gentle slope rising to the east.
            }
        }

        var state = map.State();
        Plan(state, TerraformMode.Level, new TileRect(20, 20, 30, 25), startX: 20, startZ: 20);

        Assert.True(_plan.IsValid, _plan.Reason);
        for (var z = 20; z <= 25; z++)
        {
            for (var x = 20; x <= 30; x++)
            {
                Assert.Equal(20, HeightAfter(state, x, z));
            }
        }

        AssertSlopeRule(state);
        long steps = 0;
        foreach (var change in _plan.Changes)
        {
            steps += Math.Abs(change.NewHeight - change.OldHeight);
        }

        Assert.Equal(steps * TestMap.Rules.TerraformStepCost, _plan.Cost);
    }

    [Theory]
    [InlineData(TerraformMode.Raise)]
    [InlineData(TerraformMode.Lower)]
    [InlineData(TerraformMode.Level)]
    public void CornerOrder_DoesNotChangeThePlan(TerraformMode mode)
    {
        var state = HillyState();
        var reference = Snapshot(state, mode, new TileRect(10, 12, 20, 18), 10, 12);

        Assert.Equal(reference, Snapshot(state, mode, new TileRect(20, 18, 10, 12), 10, 12));
        Assert.Equal(reference, Snapshot(state, mode, new TileRect(10, 18, 20, 12), 10, 12));
        Assert.Equal(reference, Snapshot(state, mode, new TileRect(20, 12, 10, 18), 10, 12));
    }

    [Fact]
    public void WaterInTheArea_IsRefused()
    {
        var state = new TestMap().Water(6, 6, 7, 9).State();

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 7, 7));

        Assert.False(_plan.IsValid);
        Assert.Equal(ConstructionReasons.Water, _plan.Reason);
        Assert.Contains(_plan.Problems.ToArray(), p => p.Index == Index(state, 6, 6));
    }

    [Fact]
    public void LoweringLandBelowNeighbouringWater_WouldFlood()
    {
        var state = new TestMap().Water(6, 5, 8, 10).State(); // Water surface 10 next to land at 10.

        Plan(state, TerraformMode.Lower, new TileRect(5, 5, 5, 5));

        Assert.Equal(ConstructionReasons.WouldFlood, _plan.Reason);
    }

    [Fact]
    public void RaisingNextToWater_IsAllowed_AndNeverChangesWater()
    {
        var state = new TestMap().Water(6, 5, 8, 10).Height(5, 5, 11).State();

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 5, 5));

        Assert.True(_plan.IsValid, _plan.Reason);
        Assert.DoesNotContain(_plan.Changes.ToArray(), c => c.Index == Index(state, 6, 5));
    }

    [Fact]
    public void HeightLimits_AreRefused()
    {
        var high = new TestMap(64, TestMap.Rules.MaxHeight).State();
        Plan(high, TerraformMode.Raise, new TileRect(3, 3, 3, 3));
        Assert.Equal(ConstructionReasons.HeightLimit, _plan.Reason);

        var low = new TestMap(64, 1).State();
        Plan(low, TerraformMode.Lower, new TileRect(3, 3, 3, 3));
        Assert.Equal(ConstructionReasons.HeightLimit, _plan.Reason);
    }

    [Fact]
    public void TilesUnderObjects_AreRefusedWithTheOwnerReason()
    {
        var state = new TestMap().State();
        state.Objects.Set(Index(state, 5, 5), TileObjectKind.PublicRoad, TileOwner.Town(0));
        state.Objects.Set(Index(state, 9, 9), TileObjectKind.Test, TileOwner.Company(Company.PlayerId));

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 5, 5));
        Assert.Equal(ConstructionReasons.OwnedByOther, _plan.Reason);

        Plan(state, TerraformMode.Raise, new TileRect(9, 9, 9, 9));
        Assert.Equal(ConstructionReasons.ObjectInTheWay, _plan.Reason);
    }

    [Fact]
    public void CascadeIntoAnObject_IsRefused()
    {
        var state = new TestMap().Height(5, 5, 11).State();
        state.Objects.Set(Index(state, 6, 5), TileObjectKind.PublicRoad, TileOwner.Town(0));

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 5, 5));

        Assert.Equal(ConstructionReasons.OwnedByOther, _plan.Reason);
        Assert.Contains(_plan.Problems.ToArray(), p => p.Index == Index(state, 6, 5));
    }

    [Fact]
    public void TreesOnChangedTiles_AreRemovedAndCharged()
    {
        var state = new TestMap().Tree(5, 5).Tree(20, 20).State();

        Plan(state, TerraformMode.Raise, new TileRect(5, 5, 5, 5));

        var change = Assert.Single(_plan.Changes.ToArray());
        Assert.NotEqual(Scenery.None, change.OldScenery);
        Assert.Equal(Scenery.None, change.NewScenery);
        Assert.Equal(TestMap.Rules.TerraformStepCost + TestMap.Rules.ClearTreeCost, _plan.Cost);
    }

    [Fact]
    public void TooManyTiles_IsTooLarge()
    {
        var rules = TestMap.Rules with { MaxTilesPerAction = 50 };
        var state = new TestMap().State(rules);

        Plan(state, TerraformMode.Raise, new TileRect(0, 0, 9, 9));

        Assert.Equal(ConstructionReasons.TooLarge, _plan.Reason);
    }

    [Fact]
    public void AreaOutsideTheMap_IsClipped()
    {
        var state = new TestMap().State();

        Plan(state, TerraformMode.Lower, new TileRect(-5, -5, 1, 1));

        Assert.True(_plan.IsValid);
        Assert.Equal(new TileRect(0, 0, 1, 1), _plan.Area);
        Assert.Equal(4, _plan.ChangeCount);
    }

    [Fact]
    public void LevelAlreadyFlat_IsNothingToDo()
    {
        var state = new TestMap().State();

        Plan(state, TerraformMode.Level, new TileRect(3, 3, 8, 8), 3, 3);

        Assert.Equal(ConstructionReasons.NothingToDo, _plan.Reason);
        Assert.Equal(0, _plan.Cost);
    }

    [Fact]
    public void RandomActionsOnAGeneratedMap_KeepTheSlopeRule()
    {
        // SC-004: apply many random terraform actions and check every changed pair after each one.
        var map = MapGenerator.Generate(new MapSettings(128, 128, Seed: 11, Roughness: 80, MaxHeight: 64, WaterAmount: 15, SeaLevel: 6));
        var state = new ConstructionState(TestMap.Rules with { StartingBalance = long.MaxValue / 4 }, map);
        var random = new Random(5);
        var applied = 0;
        for (var n = 0; n < 200; n++)
        {
            var x = random.Next(4, 120);
            var z = random.Next(4, 120);
            var area = new TileRect(x, z, x + random.Next(0, 8), z + random.Next(0, 8));
            var mode = (TerraformMode)random.Next(3);
            var before = (byte[])map.Heights.ToArray().Clone();
            Plan(state, mode, area, x, z);
            if (!_plan.IsValid)
            {
                continue;
            }

            state.Commit(_plan, state.Player, n);
            applied++;
            AssertChangedPairsWithinOneStep(map, before);
        }

        Assert.True(applied > 50, $"Only {applied} actions were valid.");
    }

    [Fact]
    public void Planning_AllocatesNothingOnceWarm()
    {
        var state = HillyState();
        Plan(state, TerraformMode.Level, new TileRect(10, 10, 34, 29), 10, 10);
        Plan(state, TerraformMode.Raise, new TileRect(10, 10, 34, 29));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Plan(state, TerraformMode.Level, new TileRect(10, 10, 34, 29), 10, 10);
            Plan(state, TerraformMode.Raise, new TileRect(10, 10, 34, 29));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    internal static ConstructionState HillyState()
    {
        var map = new TestMap(64, 10);
        for (var z = 0; z < 64; z++)
        {
            for (var x = 0; x < 64; x++)
            {
                map.Height(x, z, 10 + ((x + (z / 2)) % 6 < 3 ? (x + (z / 2)) % 6 : 6 - ((x + (z / 2)) % 6)));
            }
        }

        return map.State();
    }

    private static int Index(ConstructionState state, int x, int z) => state.Map.Index(x, z);

    private static void AssertChangedPairsWithinOneStep(MapData map, byte[] before)
    {
        var h = map.Heights;
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var i = map.Index(x, z);
                if (map.IsWater(i))
                {
                    continue;
                }

                if (x + 1 < map.Width && !map.IsWater(i + 1) && (h[i] != before[i] || h[i + 1] != before[i + 1]))
                {
                    Assert.True(Math.Abs(h[i] - h[i + 1]) <= 1, $"Steep pair at {x},{z} east.");
                }

                if (z + 1 < map.Depth && !map.IsWater(i + map.Width) && (h[i] != before[i] || h[i + map.Width] != before[i + map.Width]))
                {
                    Assert.True(Math.Abs(h[i] - h[i + map.Width]) <= 1, $"Steep pair at {x},{z} south.");
                }
            }
        }
    }

    private void AssertSlopeRule(ConstructionState state)
    {
        var before = state.Map.Heights.ToArray();
        state.Commit(_plan, state.Player, 0);
        AssertChangedPairsWithinOneStep(state.Map, before);
    }

    private int HeightAfter(ConstructionState state, int x, int z)
    {
        var index = Index(state, x, z);
        foreach (var change in _plan.Changes)
        {
            if (change.Index == index)
            {
                return change.NewHeight;
            }
        }

        return state.Map.HeightAt(x, z);
    }

    private string Snapshot(ConstructionState state, TerraformMode mode, TileRect area, int startX, int startZ)
    {
        Plan(state, mode, area, startX, startZ);
        var changes = _plan.Changes.ToArray().OrderBy(c => c.Index).Select(c => $"{c.Index}:{c.NewHeight}");
        return $"{_plan.Reason}|{_plan.Cost}|{string.Join(',', changes)}";
    }

    private void Plan(ConstructionState state, TerraformMode mode, TileRect area, int startX = 0, int startZ = 0) =>
        _planner.PlanTerraform(state, Company.PlayerId, mode, area, startX, startZ, _plan);
}
