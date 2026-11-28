using BenchmarkDotNet.Running;

// Run all benchmarks:        dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter *
// Quick (less precise) run:  add --job short
// typeof(Program) would be ambiguous: the benchmarks also reference the client (feature 005).
if (args is ["--compare-save-ticks"])
{
    new Talt.Benchmarks.SaveLoadBenchmarks().CompareTicks();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Talt.Benchmarks.ChunkMeshingBenchmarks).Assembly).Run(args);
