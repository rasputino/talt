using Arch.Core;
using BenchmarkDotNet.Attributes;
using Talt.Core.Determinism;
using Talt.Core.Network;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Structure of arrays (VehicleFleet) vs the Arch ECS for the same vehicle tick (research R5).
/// Both run single-threaded so the comparison measures storage and iteration, not scheduling.
/// The Arch version mirrors VehicleFleet's rules exactly (integer units, same constants).
/// </summary>
[MemoryDiagnoser]
public class ArchComparisonBenchmarks
{
    private VehicleFleet _fleet = null!;
    private World _world = null!;
    private QueryDescription _query;
    private ArchTick _archTick;

    [Params(100_000, 1_000_000)]
    public int Vehicles { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var network = GridNetworkGenerator.Generate(257, 257, spacingTiles: 16, seed: 1);
        var lines = BuildLines(network, lineCount: 2_000, seed: 1);
        var random = new DeterministicRandom(1);

        _fleet = new VehicleFleet(network);
        foreach (var (outbound, inbound) in lines)
        {
            _fleet.AddLine(outbound, inbound);
        }

        var lineData = new LineData(network, lines);
        _world = World.Create();
        for (var v = 0; v < Vehicles; v++)
        {
            var line = v % lines.Count;
            var speed = random.NextInt(SyntheticTraffic.MinCruiseSpeed, SyntheticTraffic.MaxCruiseSpeed + 1);
            _fleet.AddVehicle(line, speed);
            _world.Create(new Route { Line = line }, new Motion { MaxSpeed = speed }, new Dwell());
        }

        _query = new QueryDescription().WithAll<Route, Motion, Dwell>();
        _archTick = new ArchTick(lineData);

        for (var i = 0; i < 2_000; i++)
        {
            _fleet.Tick(parallel: false);
            _world.InlineQuery<ArchTick, Route, Motion, Dwell>(in _query, ref _archTick);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => World.Destroy(_world);

    [Benchmark(Baseline = true)]
    public void StructureOfArrays() => _fleet.Tick(parallel: false);

    [Benchmark]
    public void ArchEcs() => _world.InlineQuery<ArchTick, Route, Motion, Dwell>(in _query, ref _archTick);

    private static List<(int[] Outbound, int[] Inbound)> BuildLines(TransportNetwork network, int lineCount, int seed)
    {
        var pathFinder = new PathFinder(network);
        var random = new DeterministicRandom((ulong)seed);
        var outbound = new List<int>();
        var inbound = new List<int>();
        var lines = new List<(int[], int[])>();
        while (lines.Count < lineCount)
        {
            var a = random.NextInt(network.NodeCount);
            var b = random.NextInt(network.NodeCount);
            if (a != b && pathFinder.TryFindPath(a, b, outbound) && pathFinder.TryFindPath(b, a, inbound))
            {
                lines.Add((outbound.ToArray(), inbound.ToArray()));
            }
        }

        return lines;
    }

    private struct Route
    {
        public int Line;
        public int RouteIndex;
        public int Distance;
    }

    private struct Motion
    {
        public int Speed;
        public int MaxSpeed;
    }

    private struct Dwell
    {
        public int Ticks;
    }

    /// <summary>Flat line arrays, same layout as VehicleFleet's.</summary>
    private sealed class LineData
    {
        public LineData(TransportNetwork network, List<(int[] Outbound, int[] Inbound)> lines)
        {
            Network = network;
            var edges = new List<int>();
            var toStop = new List<long>();
            Start = new int[lines.Count];
            Length = new int[lines.Count];
            Return = new int[lines.Count];
            for (var l = 0; l < lines.Count; l++)
            {
                var loop = lines[l].Outbound.Concat(lines[l].Inbound).ToArray();
                Start[l] = edges.Count;
                Length[l] = loop.Length;
                Return[l] = lines[l].Outbound.Length;
                var distances = new long[loop.Length];
                for (var i = loop.Length - 1; i >= 0; i--)
                {
                    var next = i + 1 == loop.Length ? 0 : i + 1;
                    distances[i] = next == 0 || next == Return[l] ? 0 : network.EdgeLength(loop[next]) + distances[next];
                }

                edges.AddRange(loop);
                toStop.AddRange(distances);
            }

            Edges = [.. edges];
            DistanceToStop = [.. toStop];
        }

        public TransportNetwork Network { get; }

        public int[] Edges { get; }

        public long[] DistanceToStop { get; }

        public int[] Start { get; }

        public int[] Length { get; }

        public int[] Return { get; }
    }

    private struct ArchTick(LineData lines) : IForEach<Route, Motion, Dwell>
    {
        private readonly LineData _lines = lines;

        public readonly void Update(ref Route route, ref Motion motion, ref Dwell dwell)
        {
            if (dwell.Ticks > 0)
            {
                dwell.Ticks--;
                return;
            }

            var network = _lines.Network;
            var start = _lines.Start[route.Line];
            var index = route.RouteIndex;
            var edgeLength = network.EdgeLength(_lines.Edges[start + index]);
            var remaining = (long)edgeLength - route.Distance + _lines.DistanceToStop[start + index];
            var speed = FixedMath.BrakingDistance(motion.Speed, VehicleFleet.Deceleration) >= remaining
                ? Math.Max(motion.Speed - VehicleFleet.Deceleration, VehicleFleet.CreepSpeed)
                : Math.Min(motion.Speed + VehicleFleet.Acceleration, motion.MaxSpeed);

            if (speed >= remaining)
            {
                route.RouteIndex = index < _lines.Return[route.Line] ? _lines.Return[route.Line] : 0;
                route.Distance = 0;
                motion.Speed = 0;
                dwell.Ticks = VehicleFleet.DwellTicks;
                return;
            }

            var distance = route.Distance + speed;
            while (distance >= edgeLength)
            {
                distance -= edgeLength;
                index = index + 1 == _lines.Length[route.Line] ? 0 : index + 1;
                edgeLength = network.EdgeLength(_lines.Edges[start + index]);
            }

            route.RouteIndex = index;
            route.Distance = distance;
            motion.Speed = speed;
        }
    }
}
