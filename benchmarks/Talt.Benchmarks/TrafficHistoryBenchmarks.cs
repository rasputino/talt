using BenchmarkDotNet.Attributes;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>Collection and rotation across a stable footprint of 50,000 active directed movements.</summary>
[MemoryDiagnoser]
public class TrafficHistoryBenchmarks
{
    private readonly TrafficHistory _traffic = new();
    private static readonly GameDate NextMonth = new(2001, 4, 1, 0);

    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < 50_000; i++)
        {
            if ((i & 7) != 0)
            {
                _traffic.RegisterRoadLane(i, i, i + 1);
            }
        }
    }

    [Benchmark]
    public void RecordAndCloseMonth()
    {
        for (var i = 0; i < 50_000; i++)
        {
            if ((i & 7) == 0)
            {
                _traffic.Record(i, i + 1, rail: true);
            }
            else
            {
                _traffic.RecordRoadLane(i);
            }
        }

        _traffic.CloseMonth(NextMonth);
    }

    [Benchmark]
    public int RecordCloseAndReadHeatmap()
    {
        RecordAndCloseMonth();
        return _traffic.Completed.Count;
    }
}
