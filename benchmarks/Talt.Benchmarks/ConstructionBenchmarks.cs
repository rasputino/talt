using BenchmarkDotNet.Attributes;
using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Benchmarks;

/// <summary>
/// Construction previews (feature 006, research R14, SC-001/SC-007): planning a 25 × 20 drag (500 tiles)
/// on a hilly 1024² map, as the client does every time the cursor reaches a new tile. Budget: under
/// 1 ms and 0 B allocated per preview.
/// </summary>
[MemoryDiagnoser]
public class ConstructionBenchmarks
{
    private readonly ConstructionPlanner _planner = new();
    private readonly ConstructionPlan _plan = new();
    private ConstructionState _state = null!;
    private TileRect _area;

    [GlobalSetup]
    public void Setup()
    {
        var map = MapGenerator.Generate(new MapSettings(1024, 1024, Seed: 11, Roughness: 80, MaxHeight: 64, WaterAmount: 15, SeaLevel: 6));
        _state = new ConstructionState(ConstructionRules.Default with { StartingBalance = long.MaxValue / 4 }, map);
        _area = FindDryArea(map);
        Level(); // Warm the planner's buffers.
        Raise();
        Clear();
    }

    [Benchmark]
    public long Level()
    {
        _planner.PlanTerraform(_state, Company.PlayerId, TerraformMode.Level, _area, _area.X0, _area.Z0, _plan);
        return _plan.Cost;
    }

    [Benchmark]
    public long Raise()
    {
        _planner.PlanTerraform(_state, Company.PlayerId, TerraformMode.Raise, _area, 0, 0, _plan);
        return _plan.Cost;
    }

    [Benchmark]
    public long Clear()
    {
        _planner.PlanClear(_state, Company.PlayerId, _area, _plan);
        return _plan.Cost;
    }

    /// <summary>The first 25 × 20 rectangle of dry, hilly land from the map's centre (deterministic).</summary>
    private static TileRect FindDryArea(MapData map)
    {
        for (var z = map.Depth / 2; z < map.Depth - 40; z += 7)
        {
            for (var x = map.Width / 2; x < map.Width - 40; x += 7)
            {
                var dry = true;
                var min = int.MaxValue;
                var max = 0;
                for (var dz = 0; dz < 20 && dry; dz++)
                {
                    for (var dx = 0; dx < 25 && dry; dx++)
                    {
                        dry = !map.IsWater(map.Index(x + dx, z + dz));
                        min = Math.Min(min, map.HeightAt(x + dx, z + dz));
                        max = Math.Max(max, map.HeightAt(x + dx, z + dz));
                    }
                }

                if (dry && max - min >= 4)
                {
                    return new TileRect(x, z, x + 24, z + 19);
                }
            }
        }

        throw new InvalidOperationException("No dry hilly area on the benchmark map.");
    }
}
