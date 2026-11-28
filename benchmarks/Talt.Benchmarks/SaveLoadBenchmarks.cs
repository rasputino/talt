using BenchmarkDotNet.Attributes;
using Talt.Core.Maps;
using Talt.Core.Saves;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>Save, restore and resume feature 012's reference game with rail traffic added.</summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5, invocationCount: 1)]
public class SaveLoadBenchmarks
{
    private readonly SaveSnapshot _snapshot = new();
    private readonly SaveDetailsInput _details = new("Reference", SaveKind.Manual, DateTimeOffset.UnixEpoch, "benchmark");
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"talt-benchmark-{Guid.NewGuid():N}.talt");
    private GameSimulation _original = null!;
    private GameSimulation _loaded = null!;

    [GlobalSetup]
    public void Setup()
    {
        // The ordinary reference seed has 678 industries. Increase site density for the save budget.
        var tuning = MapGenerationTuning.Default with { IndustryAreaPerSite = 5_000 };
        _original = RoadVehicleBenchmarks.Create(50_000, warmupTicks: 400, trains: 100, tuning: tuning);
        if (_original.Trains!.Count != 100 || _original.RoadVehicles!.Count != 50_000 || _original.Industries.Count < 1000
            || _original.Towns!.Count < 500 || _original.Stations!.Count < 5000)
        {
            throw new InvalidOperationException("The seeded game falls below the save benchmark population targets.");
        }
        Capture();
        var result = Write();
        if (!result.Succeeded)
        {
            throw new IOException(result.Problem);
        }

        var timer = System.Diagnostics.Stopwatch.StartNew();
        double readMs;
        using (var stream = File.OpenRead(_path))
        using (var file = SaveFile.Read(stream))
        {
            readMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            _loaded = SaveGame.Restore(file, new SaveLoadOptions(), null).Simulation
                ?? throw new InvalidDataException("The reference save failed to restore.");
        }
        Console.WriteLine($"// Verified and decompressed in {readMs:F1} ms, restored in {timer.Elapsed.TotalMilliseconds:F1} ms. {_original.Towns!.Count} towns, {_original.Industries.Count} industries.");
        var brotliPath = _path + ".br";
        try
        {
            var brotli = SaveGame.Write(_snapshot, brotliPath, SaveCodec.Brotli);
            using var source = File.OpenRead(brotliPath);
            timer.Restart();
            using var compressed = SaveFile.Read(source);
            Console.WriteLine($"// Brotli q1: {brotli.Bytes} bytes, verified and decompressed in {timer.Elapsed.TotalMilliseconds:F1} ms.");
        }
        finally
        {
            File.Delete(brotliPath);
        }

        Console.WriteLine($"// {result.Bytes} bytes on disk, {_snapshot.Length} bytes of sections; {_original.RoadVehicles!.Count} buses, {_original.Trains!.Count} trains, {_original.Stations!.Count} stations.");
    }

    [Benchmark]
    public void Capture() => SaveGame.CaptureInto(_snapshot, _original, _details);

    [Benchmark]
    public SaveWriteResult Write() => SaveGame.Write(_snapshot, _path);

    [Benchmark]
    public SaveWriteResult WriteBrotli() => SaveGame.Write(_snapshot, _path, SaveCodec.Brotli);

    [Benchmark]
    public GameSimulation Load()
    {
        var result = SaveGame.Load(_path);
        return result.Simulation ?? throw new InvalidDataException(result.Problem);
    }

    [Benchmark(OperationsPerInvoke = 40)]
    public void OriginalTicks() => AdvanceDay(_original);

    [Benchmark(OperationsPerInvoke = 40)]
    public void LoadedTicks() => AdvanceDay(_loaded);

    private static void AdvanceDay(GameSimulation simulation)
    {
        for (var tick = 0; tick < GameClock.TicksPerDay; tick++)
        {
            simulation.Tick(parallel: true);
        }
    }

    // Alternate order so both simulations see the same machine load and calendar events.
    public void CompareTicks()
    {
        try
        {
            Setup();
            for (var day = 0; day < 8; day++)
            {
                AdvanceDay(_original);
                AdvanceDay(_loaded);
            }

            var original = new System.Diagnostics.Stopwatch();
            var loaded = new System.Diagnostics.Stopwatch();
            const int days = 40;
            for (var day = 0; day < days; day++)
            {
                if (day % 2 == 0)
                {
                    Measure(_original, original);
                    Measure(_loaded, loaded);
                }
                else
                {
                    Measure(_loaded, loaded);
                    Measure(_original, original);
                }
            }

            if (_original.ComputeFingerprint() != _loaded.ComputeFingerprint())
            {
                throw new InvalidOperationException("Reference trajectories differ after the paired run.");
            }

            Console.WriteLine($"Paired mean per tick: original {original.Elapsed.TotalMilliseconds / (days * GameClock.TicksPerDay):F3} ms, loaded {loaded.Elapsed.TotalMilliseconds / (days * GameClock.TicksPerDay):F3} ms.");
        }
        finally
        {
            Cleanup();
        }

        static void Measure(GameSimulation game, System.Diagnostics.Stopwatch timer)
        {
            timer.Start();
            AdvanceDay(game);
            timer.Stop();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _snapshot.Dispose();
        File.Delete(_path);
    }
}
