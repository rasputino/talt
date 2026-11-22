using System.Collections.Concurrent;
using System.Diagnostics;
using Talt.Core.Meshing;
using Talt.Core.Voxels;

namespace Talt.Client.Rendering;

/// <summary>What a worker builds: a full-detail chunk mesh or a simplified region mesh.</summary>
/// <param name="RegionSlot">Index of the region in the renderer.</param>
/// <param name="Lod">0 for a chunk mesh, 1..MaxLod for a region mesh.</param>
/// <param name="ChunkIndex">Chunk inside the region (0..63) for LOD 0; unused otherwise.</param>
/// <param name="Version">Chunk version (LOD 0) or region version (LOD 1+) the job starts from.</param>
internal readonly record struct MeshJob(int RegionSlot, int Lod, int ChunkIndex, int Version);

/// <summary>A finished job: the mesh and how long it took to build.</summary>
internal sealed record MeshResult(MeshJob Job, PackedMesh Mesh, double BuildMilliseconds);

/// <summary>
/// Builds meshes on dedicated background threads (research R6). The render thread decides what to
/// build and in which order; this class only runs jobs and hands back results.
/// </summary>
/// <remarks>
/// Workers run below normal priority so the render thread and the simulation keep their cores.
/// At most <see cref="Capacity"/> jobs are queued or running at once, so the render thread can keep
/// re-prioritising as the camera moves.
/// </remarks>
internal sealed class ChunkMeshScheduler : IDisposable
{
    private readonly VoxelWorld _world;
    private readonly Func<int, RegionCoord> _regionOf;
    private readonly ConcurrentQueue<MeshJob> _jobs = new();
    private readonly ConcurrentQueue<MeshResult> _results = new();
    private readonly SemaphoreSlim _jobAvailable = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread[] _workers;
    private int _inFlight;

    /// <param name="world">The world to read.</param>
    /// <param name="regionOf">Maps a region slot to its coordinate.</param>
    /// <param name="workerCount">Worker threads; null uses processor count − 2 (at least 1).</param>
    public ChunkMeshScheduler(VoxelWorld world, Func<int, RegionCoord> regionOf, int? workerCount = null)
    {
        _world = world;
        _regionOf = regionOf;
        var count = workerCount ?? Math.Max(1, Environment.ProcessorCount - 2);
        Capacity = count * 2;
        _workers = new Thread[count];
        for (var i = 0; i < count; i++)
        {
            _workers[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"Mesh worker {i}",
            };
            _workers[i].Start();
        }
    }

    /// <summary>Most jobs queued or running at once.</summary>
    public int Capacity { get; }

    /// <summary>Jobs submitted whose results have not been taken yet.</summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>Whether another job can be submitted now.</summary>
    public bool HasRoom => InFlight < Capacity;

    public void Submit(MeshJob job)
    {
        Interlocked.Increment(ref _inFlight);
        _jobs.Enqueue(job);
        _jobAvailable.Release();
    }

    public bool TryTakeResult(out MeshResult result)
    {
        if (_results.TryDequeue(out result!))
        {
            Interlocked.Decrement(ref _inFlight);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var worker in _workers)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        _stop.Dispose();
        _jobAvailable.Dispose();
    }

    /// <summary>Builds the mesh a job asks for (also used directly by tests and benchmarks).</summary>
    public static PackedMesh Build(VoxelWorld world, RegionCoord region, MeshJob job)
    {
        if (job.Lod == 0)
        {
            var (ox, oy, oz) = (region.X * RegionCoord.ChunksPerSide, region.Y * RegionCoord.ChunksPerSide, region.Z * RegionCoord.ChunksPerSide);
            var i = job.ChunkIndex;
            var side = RegionCoord.ChunksPerSide;
            var chunk = new ChunkCoord(ox + i % side, oy + i / (side * side), oz + i / side % side);
            return GreedyMesher.BuildChunk(world, chunk);
        }

        return GreedyMesher.BuildRegionLod(world, region, job.Lod);
    }

    private void WorkerLoop()
    {
        var token = _stop.Token;
        try
        {
            while (true)
            {
                _jobAvailable.Wait(token);
                if (!_jobs.TryDequeue(out var job))
                {
                    continue;
                }

                var start = Stopwatch.GetTimestamp();
                PackedMesh mesh;
                try
                {
                    mesh = Build(_world, _regionOf(job.RegionSlot), job);
                }
                catch (Exception e) when (e is IndexOutOfRangeException or InvalidOperationException or ArgumentException)
                {
                    // A concurrent edit can only make the data stale; report and return an empty mesh
                    // so the job's version stays dirty and is rebuilt.
                    Console.Error.WriteLine($"Mesh job {job} failed: {e.Message}");
                    mesh = PackedMesh.Empty;
                    job = job with { Version = -1 };
                }

                _results.Enqueue(new MeshResult(job, mesh, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
