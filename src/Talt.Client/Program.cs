using System.Diagnostics;
using System.Numerics;
using Talt.Client;
using Talt.Core.Meshing;
using Talt.Core.Network;
using Talt.Core.Simulation;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

// Technical prototype: voxel terrain with a road grid and simulated vehicles, rendered with WebGPU.
// Usage: Talt.Client [chunks-per-side] [vehicles] [seed]
var chunksPerSide = args.Length > 0 && int.TryParse(args[0], out var c) ? c : 16;
var vehicleCount = args.Length > 1 && int.TryParse(args[1], out var v) ? v : 20_000;
var seed = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 2026;

const int RoadSpacing = 16; // Tiles between junctions.

var settings = new TerrainSettings(Seed: seed, ChunksX: chunksPerSide, ChunksZ: chunksPerSide);
var size = chunksPerSide * Chunk.Size;
var stopwatch = Stopwatch.StartNew();

var world = TerrainGenerator.Generate(settings);
Report($"Generated {size}×{size} terrain ({world.ChunkCount} chunks)");

var junctions = size / RoadSpacing;
var network = GridNetworkGenerator.Generate(
    junctions, junctions, RoadSpacing, seed, removalChance: 0.15f,
    origin: new Vector2(RoadSpacing / 2f, RoadSpacing / 2f));
NetworkTerrainPainter.PaintRoads(world, network);
Report($"Built road network ({network.NodeCount:N0} junctions, {network.EdgeCount:N0} one-way edges)");

var lineCount = Math.Max(1, vehicleCount / 10);
var fleet = SyntheticTraffic.Create(network, lineCount, vehicleCount, seed);
Report($"Created {fleet.LineCount:N0} lines and {fleet.VehicleCount:N0} vehicles");

var heightmap = Heightmap.FromWorld(world, size, size);
// The world is only read while meshing, so chunks can be meshed in parallel.
var meshes = world.Chunks
    .AsParallel()
    .Select(chunk => ChunkMesher.Build(world, chunk))
    .Where(mesh => !mesh.IsEmpty)
    .ToList();
Report($"Meshed {meshes.Sum(m => m.FaceCount):N0} faces");

using var window = new GameWindow(new PrototypeScene(meshes, heightmap, fleet, $"{size}×{size} map"));
window.Run();

void Report(string message)
{
    Console.WriteLine($"{message} in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    stopwatch.Restart();
}
