using BenchmarkDotNet.Attributes;
using Talt.Core.Rails;
using Talt.Core.Vehicles;

namespace Talt.Benchmarks;

/// <summary>Annual catalog scan shared by purchase windows and model introductions.</summary>
[MemoryDiagnoser]
public class CatalogBenchmarks
{
    private readonly IReadOnlyList<VehicleModel> _road = VehicleRules.Default.Models;
    private readonly IReadOnlyList<RollingStockModel> _rail = RailRules.Default.Models;

    [Benchmark]
    public int CountModelsOnSale()
    {
        var count = 0;
        for (var i = 0; i < _road.Count; i++)
        {
            count += _road[i].AvailableIn(2000) ? 1 : 0;
        }

        for (var i = 0; i < _rail.Count; i++)
        {
            count += _rail[i].AvailableIn(2000) ? 1 : 0;
        }

        return count;
    }
}
