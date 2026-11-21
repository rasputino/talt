using System.Diagnostics;
using System.Numerics;
using Talt.Client;
using Talt.Core.Meshing;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

// Technical prototype: generate a voxel terrain, mesh it and render it with WebGPU.
// Usage: Talt.Client [chunks-per-side] [seed]
var chunksPerSide = args.Length > 0 && int.TryParse(args[0], out var c) ? c : 16;
var seed = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 2026;
var settings = new TerrainSettings(Seed: seed, ChunksX: chunksPerSide, ChunksZ: chunksPerSide);

var stopwatch = Stopwatch.StartNew();
var world = TerrainGenerator.Generate(settings);
var generateTime = stopwatch.Elapsed;

stopwatch.Restart();
// The world is only read while meshing, so chunks can be meshed in parallel.
var meshes = world.Chunks
    .AsParallel()
    .Select(chunk => ChunkMesher.Build(world, chunk))
    .Where(mesh => !mesh.IsEmpty)
    .ToList();
var meshTime = stopwatch.Elapsed;

var sizeInVoxels = chunksPerSide * Chunk.Size;
var description = $"{sizeInVoxels}×{sizeInVoxels} map";
Console.WriteLine($"Generated {description} ({world.ChunkCount} chunks) in {generateTime.TotalMilliseconds:F0} ms.");
Console.WriteLine($"Meshed {meshes.Sum(m => m.FaceCount):N0} faces in {meshTime.TotalMilliseconds:F0} ms.");

var center = new Vector3(sizeInVoxels / 2f, settings.WaterLevel, sizeInVoxels / 2f);
using var window = new GameWindow(meshes, center, description);
window.Run();
