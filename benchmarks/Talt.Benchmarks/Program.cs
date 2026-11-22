using BenchmarkDotNet.Running;

// Run all benchmarks:        dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter *
// Quick (less precise) run:  add --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
