using System.Diagnostics;
using System.Numerics;
using Silk.NET.WebGPU;
using Talt.Core.Meshing;
using Talt.Core.Voxels;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>
/// Draws the voxel world by region (research R3–R6): keeps one GPU vertex buffer per region and
/// level of detail, culls regions against the view and the fog, picks each region's level of detail
/// from its screen-space error, schedules background meshing nearest-first and uploads finished
/// meshes within a per-frame budget.
/// </summary>
/// <remarks>
/// Full-detail (LOD 0) region buffers are the concatenation of their chunks' meshes. When chunks are
/// rebuilt, a new region buffer is assembled on the GPU: unchanged chunk ranges are copied from the old
/// buffer and only the new chunk meshes are uploaded, so no copy of the meshes stays in main memory.
/// </remarks>
internal sealed unsafe class WorldRenderer : IDisposable
{
    private const int LodCount = GreedyMesher.MaxLod + 1;
    private const int ChunksPerRegion = RegionCoord.ChunksPerSide * RegionCoord.ChunksPerSide * RegionCoord.ChunksPerSide;
    private const int BytesPerQuad = 4 * PackedVertex.SizeInBytes;
    private const long UploadBudgetBytes = 4L << 20;
    private const double UploadBudgetMilliseconds = 2;
    private const float InvisiblePriorityPenalty = 1_000_000f;

    private static readonly Comparison<Candidate> ByPriority = (a, b) => a.Priority.CompareTo(b.Priority);

    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly VoxelWorld _world;
    private readonly Region[] _regions;
    private readonly Region?[] _regionGrid;
    private readonly int _gridX;
    private readonly int _gridY;
    private readonly int _gridZ;
    private readonly ChunkMeshScheduler _scheduler;

    private readonly List<Candidate> _candidates = [];
    private readonly List<DrawItem> _draws = [];
    private readonly List<Region> _regionsWithResults = [];
    private readonly List<nint> _releaseAfterSubmit = [];
    private readonly List<PendingEdit> _edits = [];
    private readonly HashSet<ChunkCoord> _touched = [];

    private Buffer* _originBuffer;
    private Buffer* _indexBuffer;
    private int _indexQuadCapacity;
    private long _worldBytes;
    private long _lod0Released;
    private RenderStatistics _statistics;

    public WorldRenderer(WebGPU wgpu, Device* device, Queue* queue, VoxelWorld world)
    {
        _wgpu = wgpu;
        _device = device;
        _queue = queue;
        _world = world;

        // Region grid covering every existing chunk.
        int maxX = 0, maxY = 0, maxZ = 0;
        foreach (var chunk in world.Chunks)
        {
            var r = RegionCoord.FromChunk(chunk.Coord);
            if (r.X < 0 || r.Y < 0 || r.Z < 0)
            {
                throw new InvalidOperationException("The renderer supports worlds at non-negative coordinates only.");
            }

            (maxX, maxY, maxZ) = (Math.Max(maxX, r.X), Math.Max(maxY, r.Y), Math.Max(maxZ, r.Z));
        }

        (_gridX, _gridY, _gridZ) = (maxX + 1, maxY + 1, maxZ + 1);
        _regionGrid = new Region?[_gridX * _gridY * _gridZ];
        var regions = new List<Region>();
        foreach (var chunk in world.Chunks)
        {
            var coord = RegionCoord.FromChunk(chunk.Coord);
            ref var cell = ref _regionGrid[GridIndex(coord)];
            if (cell is null)
            {
                cell = new Region(coord, regions.Count);
                regions.Add(cell);
            }

            cell.AddChunk(chunk.Coord);
        }

        _regions = [.. regions];
        foreach (var region in _regions)
        {
            region.FinishSetup();
        }

        CreateOriginBuffer();
        EnsureIndexCapacity(16 * 1024);
        _scheduler = new ChunkMeshScheduler(world, slot => _regions[slot].Coord);
    }

    /// <summary>Counters of the last prepared frame.</summary>
    public RenderStatistics Statistics => _statistics;

    /// <summary>Regions in the world.</summary>
    public int RegionCount => _regions.Length;

    /// <summary>
    /// Takes finished meshes (within the upload budget), assembles region buffers, culls regions,
    /// chooses their level of detail and schedules new meshing jobs. Copies are recorded into
    /// <paramref name="encoder"/>, which must be submitted before <see cref="AfterSubmit"/>.
    /// </summary>
    public void Prepare(CommandEncoder* encoder, Matrix4x4 viewProjection, Vector3 cameraPosition, float fogDistance,
        float screenHeightPixels, float verticalFieldOfView)
    {
        TakeResults(encoder);

        var frustum = new Frustum(viewProjection);
        var fogFull = fogDistance * 1.5f; // Fog is complete here (see voxel.wgsl).
        _statistics = default;
        _draws.Clear();
        _candidates.Clear();
        var maxQuads = 0;
        var now = Stopwatch.GetTimestamp();
        var releaseDistance = Lod0Eviction.ReleaseDistance(
            LodSelector.FullDetailDistance(screenHeightPixels, verticalFieldOfView), fogFull);

        foreach (var region in _regions)
        {
            var distance = LodSelector.DistanceToBox(cameraPosition, region.BoxMin, region.BoxMax);
            var pixelsPerUnit = LodSelector.PixelsPerUnit(distance, screenHeightPixels, verticalFieldOfView);
            if (distance > fogFull)
            {
                region.Shown = false;
                _statistics.RegionsCulled++;
                ReleaseLod0IfIdle(region, distance, releaseDistance, pixelsPerUnit, now, visible: false);
                continue; // Entirely sky-coloured: nothing to draw, nothing to build yet.
            }

            var visible = frustum.Intersects(region.BoxMin, region.BoxMax);
            region.DesiredLod = LodSelector.Select(pixelsPerUnit, Math.Max(region.DesiredLod, 0));
            if (region.DesiredLod == 0)
            {
                region.LastWantedLod0 = now;
            }

            AddCandidates(region, distance + (visible ? 0f : InvisiblePriorityPenalty));
            ReleaseLod0IfIdle(region, distance, releaseDistance, pixelsPerUnit, now, visible);

            region.Shown = false;
            if (!visible)
            {
                _statistics.RegionsCulled++;
                continue;
            }

            var lod = region.DrawableLod();
            if (lod < 0)
            {
                continue; // Not built yet (filling in).
            }

            var level = region.Lods[lod];
            if (level.Quads == 0 || !frustum.Intersects(level.Min, level.Max))
            {
                _statistics.RegionsCulled++;
                continue;
            }

            region.Shown = true;
            region.DisplayedLod = lod;
            _draws.Add(new DrawItem((nint)level.Buffer, level.Quads, region.Slot));
            maxQuads = Math.Max(maxQuads, level.Quads);
            _statistics.DrawCalls++;
            _statistics.Triangles += level.Quads * 2L;
            _statistics.CountRegion(lod);
        }

        EnsureIndexCapacity(maxQuads);
        SubmitJobs();
        _statistics.WorldGpuBytes = _worldBytes;
        _statistics.Lod0Released = _lod0Released;
        _statistics.PendingJobs = _candidates.Count;
        _statistics.JobsInFlight = _scheduler.InFlight;
    }

    /// <summary>Records the draws prepared by <see cref="Prepare"/>. The voxel pipeline must be set.</summary>
    public void Encode(RenderPassEncoder* pass)
    {
        if (_draws.Count == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _originBuffer, 0, (ulong)(_regions.Length * sizeof(Vector4)));
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _indexBuffer, IndexFormat.Uint32, 0, (ulong)_indexQuadCapacity * 6 * sizeof(uint));
        foreach (var draw in _draws)
        {
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, (Buffer*)draw.Buffer, 0, (ulong)draw.Quads * BytesPerQuad);
            _wgpu.RenderPassEncoderDrawIndexed(pass, (uint)draw.Quads * 6, 1, 0, 0, (uint)draw.Slot);
        }
    }

    /// <summary>Releases buffers replaced this frame, once the frame's commands are submitted.</summary>
    public void AfterSubmit()
    {
        foreach (var buffer in _releaseAfterSubmit)
        {
            _wgpu.BufferRelease((Buffer*)buffer);
        }

        _releaseAfterSubmit.Clear();
    }

    /// <summary>
    /// Marks the meshes that may change because these voxels changed (FR-007). Returns the number of
    /// chunks to rebuild. Completion is reported by <see cref="TryTakeCompletedEdit"/>.
    /// </summary>
    public int NotifyVoxelsChanged(IReadOnlyList<(int X, int Y, int Z)> voxels, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(voxels);

        _touched.Clear();
        foreach (var (x, y, z) in voxels)
        {
            foreach (var chunk in ChunkCoord.TouchedBy(x, y, z))
            {
                _touched.Add(chunk);
            }
        }

        var edit = new PendingEdit(timestamp);
        foreach (var chunk in _touched)
        {
            var region = FindRegion(RegionCoord.FromChunk(chunk));
            var index = Region.ChunkIndex(chunk);
            if (region is null || !region.ChunkExists[index])
            {
                continue;
            }

            region.ChunkVersion[index]++;
            edit.Chunks.Add((region, index, region.ChunkVersion[index]));
        }

        foreach (var (region, _, _) in edit.Chunks)
        {
            if (!edit.Regions.Contains(region))
            {
                region.Version++;
                edit.Regions.Add(region);
            }
        }

        if (edit.Chunks.Count > 0)
        {
            _edits.Add(edit);
        }

        return edit.Chunks.Count;
    }

    /// <summary>
    /// Returns the oldest edit whose result is on screen in the frame just prepared: every touched
    /// region shows meshes built from the edited voxels.
    /// </summary>
    public bool TryTakeCompletedEdit(out long timestamp, out int chunks)
    {
        for (var i = 0; i < _edits.Count; i++)
        {
            var edit = _edits[i];
            if (IsVisible(edit))
            {
                _edits.RemoveAt(i);
                timestamp = edit.Timestamp;
                chunks = edit.Chunks.Count;
                return true;
            }
        }

        timestamp = 0;
        chunks = 0;
        return false;
    }

    public void Dispose()
    {
        _scheduler.Dispose();
        AfterSubmit();
        foreach (var region in _regions)
        {
            foreach (var level in region.Lods)
            {
                if (level.Buffer != null)
                {
                    _wgpu.BufferRelease(level.Buffer);
                    level.Buffer = null;
                }
            }
        }

        _wgpu.BufferRelease(_originBuffer);
        _wgpu.BufferRelease(_indexBuffer);
    }

    private static bool IsVisible(PendingEdit edit)
    {
        foreach (var region in edit.Regions)
        {
            if (!region.Shown)
            {
                continue; // Off screen: correct whenever it comes into view.
            }

            if (region.DisplayedLod > 0)
            {
                if (region.Lods[region.DisplayedLod].BuiltVersion < region.Version)
                {
                    return false;
                }

                continue;
            }

            foreach (var (chunkRegion, index, version) in edit.Chunks)
            {
                if (chunkRegion == region && region.ChunkBuilt[index] < version)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void AddCandidates(Region region, float priority)
    {
        var lod = region.DesiredLod;
        if (lod == 0)
        {
            for (var i = 0; i < ChunksPerRegion; i++)
            {
                if (region.ChunkExists[i] && !region.ChunkInFlight[i] && region.ChunkBuilt[i] != region.ChunkVersion[i])
                {
                    _candidates.Add(new Candidate(priority, new MeshJob(region.Slot, 0, i, region.ChunkVersion[i])));
                }
            }

            return;
        }

        var level = region.Lods[lod];
        if (!level.InFlight && level.BuiltVersion != region.Version)
        {
            // Simplified levels are cheap and fill the view quickly: build them before nearby detail.
            _candidates.Add(new Candidate(priority * 0.5f, new MeshJob(region.Slot, lod, 0, region.Version)));
        }
    }

    /// <summary>
    /// Releases the region's full-detail buffer when it has not been wanted for a while and the camera is
    /// far away (<see cref="Lod0Eviction"/>). A coarser level is built first, so the region never
    /// drops to nothing.
    /// </summary>
    private void ReleaseLod0IfIdle(Region region, float distance, float releaseDistance, float pixelsPerUnit,
        long now, bool visible)
    {
        var level = region.Lods[0];
        if (level.Buffer == null)
        {
            return; // Nothing resident (or an empty region, whose LOD 0 costs nothing).
        }

        var idle = Stopwatch.GetElapsedTime(region.LastWantedLod0, now).TotalSeconds;
        switch (Lod0Eviction.Decide(idle, distance, releaseDistance, region.HasChunkWork(), region.HasCoarserLevel()))
        {
            case Lod0EvictionAction.BuildCoarserLevel:
                var lod = LodSelector.Select(pixelsPerUnit, Math.Max(region.DesiredLod, 1));
                var coarser = region.Lods[Math.Max(lod, 1)];
                if (!coarser.InFlight && coarser.BuiltVersion != region.Version)
                {
                    var priority = distance + (visible ? 0f : InvisiblePriorityPenalty);
                    _candidates.Add(new Candidate(priority, new MeshJob(region.Slot, Math.Max(lod, 1), 0, region.Version)));
                }

                break;

            case Lod0EvictionAction.Evict:
                ReleaseLater(level);
                level.Quads = 0;
                level.BuiltVersion = 0;
                level.Available = false;
                Array.Clear(region.ChunkBuilt);
                Array.Clear(region.ChunkQuads);
                Array.Clear(region.ChunkOffset);
                _lod0Released++;
                break;
        }
    }

    private void SubmitJobs()
    {
        if (_candidates.Count == 0 || !_scheduler.HasRoom)
        {
            return;
        }

        _candidates.Sort(ByPriority);
        foreach (var candidate in _candidates)
        {
            if (!_scheduler.HasRoom)
            {
                break;
            }

            var job = candidate.Job;
            var region = _regions[job.RegionSlot];
            if (job.Lod == 0 ? region.ChunkInFlight[job.ChunkIndex] : region.Lods[job.Lod].InFlight)
            {
                continue; // Already submitted by another candidate this frame.
            }

            if (job.Lod == 0)
            {
                region.ChunkInFlight[job.ChunkIndex] = true;
            }
            else
            {
                region.Lods[job.Lod].InFlight = true;
            }

            _scheduler.Submit(job);
        }
    }

    private void TakeResults(CommandEncoder* encoder)
    {
        var start = Stopwatch.GetTimestamp();
        long bytes = 0;
        while (bytes < UploadBudgetBytes
            && Stopwatch.GetElapsedTime(start).TotalMilliseconds < UploadBudgetMilliseconds
            && _scheduler.TryTakeResult(out var result))
        {
            var job = result.Job;
            var region = _regions[job.RegionSlot];
            bytes += result.Mesh.SizeInBytes;

            if (job.Lod == 0)
            {
                region.ChunkInFlight[job.ChunkIndex] = false;
                if (job.Version > 0)
                {
                    region.PendingChunks[job.ChunkIndex] = result;
                    if (!region.HasPendingChunks)
                    {
                        region.HasPendingChunks = true;
                        _regionsWithResults.Add(region);
                    }
                }

                continue;
            }

            var level = region.Lods[job.Lod];
            level.InFlight = false;
            if (job.Version > 0 && job.Version >= level.BuiltVersion)
            {
                UploadLevel(level, region, result.Mesh, job.Version);
            }
        }

        foreach (var region in _regionsWithResults)
        {
            AssembleLod0(encoder, region);
        }

        _regionsWithResults.Clear();
    }

    private void UploadLevel(LevelState level, Region region, PackedMesh mesh, int version)
    {
        ReleaseLater(level);
        level.BuiltVersion = version;
        level.Available = true;
        level.Quads = mesh.QuadCount;
        if (mesh.IsEmpty)
        {
            return;
        }

        level.Buffer = CreateVertexBuffer(mesh.QuadCount);
        fixed (PackedVertex* data = mesh.Vertices)
        {
            _wgpu.QueueWriteBuffer(_queue, level.Buffer, 0, data, (nuint)mesh.SizeInBytes);
        }

        level.Min = region.Origin + ToVector(mesh.BoundsMin);
        level.Max = region.Origin + ToVector(mesh.BoundsMax);
    }

    /// <summary>Builds the region's new LOD 0 buffer from its pending chunk meshes and the old buffer.</summary>
    private void AssembleLod0(CommandEncoder* encoder, Region region)
    {
        region.HasPendingChunks = false;
        var level = region.Lods[0];
        var total = 0;
        for (var i = 0; i < ChunksPerRegion; i++)
        {
            total += region.PendingChunks[i]?.Mesh.QuadCount ?? region.ChunkQuads[i];
        }

        var old = level.Buffer;
        var buffer = total > 0 ? CreateVertexBuffer(total) : null;
        var offset = 0;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < ChunksPerRegion; i++)
        {
            var pending = region.PendingChunks[i];
            if (pending is not null)
            {
                region.PendingChunks[i] = null;
                var mesh = pending.Mesh;
                region.ChunkBuilt[i] = pending.Job.Version;
                region.ChunkQuads[i] = mesh.QuadCount;
                region.ChunkMin[i] = ToVector(mesh.BoundsMin);
                region.ChunkMax[i] = ToVector(mesh.BoundsMax);
                if (!mesh.IsEmpty)
                {
                    fixed (PackedVertex* data = mesh.Vertices)
                    {
                        _wgpu.QueueWriteBuffer(_queue, buffer, (ulong)offset * BytesPerQuad, data, (nuint)mesh.SizeInBytes);
                    }
                }
            }
            else if (region.ChunkQuads[i] > 0)
            {
                _wgpu.CommandEncoderCopyBufferToBuffer(encoder, old, (ulong)region.ChunkOffset[i] * BytesPerQuad,
                    buffer, (ulong)offset * BytesPerQuad, (ulong)region.ChunkQuads[i] * BytesPerQuad);
            }

            region.ChunkOffset[i] = offset;
            offset += region.ChunkQuads[i];
            if (region.ChunkQuads[i] > 0)
            {
                min = Vector3.Min(min, region.ChunkMin[i]);
                max = Vector3.Max(max, region.ChunkMax[i]);
            }
        }

        ReleaseLater(level);
        level.Buffer = buffer;
        level.Quads = total;
        level.Min = region.Origin + min;
        level.Max = region.Origin + max;
        level.BuiltVersion = region.Version;
        level.Available = region.AllChunksBuilt();
    }

    private Buffer* CreateVertexBuffer(int quads)
    {
        var descriptor = new BufferDescriptor
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst | BufferUsage.CopySrc,
            Size = (ulong)quads * BytesPerQuad,
        };
        _worldBytes += (long)descriptor.Size;
        return _wgpu.DeviceCreateBuffer(_device, &descriptor);
    }

    private void ReleaseLater(LevelState level)
    {
        if (level.Buffer != null)
        {
            _worldBytes -= (long)level.Quads * BytesPerQuad;
            _releaseAfterSubmit.Add((nint)level.Buffer);
            level.Buffer = null;
        }
    }

    private void CreateOriginBuffer()
    {
        var origins = new Vector4[_regions.Length];
        foreach (var region in _regions)
        {
            origins[region.Slot] = new Vector4(region.Origin, 0f);
        }

        var descriptor = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (ulong)(origins.Length * sizeof(Vector4)) };
        _originBuffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        _worldBytes += (long)descriptor.Size;
        fixed (Vector4* data = origins)
        {
            _wgpu.QueueWriteBuffer(_queue, _originBuffer, 0, data, (nuint)descriptor.Size);
        }
    }

    /// <summary>One shared index buffer with the quad pattern 0-1-2, 0-2-3 repeated (research R2).</summary>
    private void EnsureIndexCapacity(int quads)
    {
        if (quads <= _indexQuadCapacity)
        {
            return;
        }

        var capacity = Math.Max(quads, _indexQuadCapacity * 2);
        var indices = new uint[capacity * 6];
        var pattern = PackedMesh.QuadIndices;
        for (var q = 0; q < capacity; q++)
        {
            for (var i = 0; i < 6; i++)
            {
                indices[q * 6 + i] = (uint)(q * 4) + pattern[i];
            }
        }

        if (_indexBuffer != null)
        {
            _worldBytes -= (long)_indexQuadCapacity * 6 * sizeof(uint);
            _releaseAfterSubmit.Add((nint)_indexBuffer);
        }

        var descriptor = new BufferDescriptor { Usage = BufferUsage.Index | BufferUsage.CopyDst, Size = (ulong)indices.Length * sizeof(uint) };
        _indexBuffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        _worldBytes += (long)descriptor.Size;
        fixed (uint* data = indices)
        {
            _wgpu.QueueWriteBuffer(_queue, _indexBuffer, 0, data, (nuint)descriptor.Size);
        }

        _indexQuadCapacity = capacity;
    }

    private Region? FindRegion(RegionCoord coord) =>
        (uint)coord.X < _gridX && (uint)coord.Y < _gridY && (uint)coord.Z < _gridZ ? _regionGrid[GridIndex(coord)] : null;

    private int GridIndex(RegionCoord coord) => coord.X + coord.Z * _gridX + coord.Y * _gridX * _gridZ;

    private static Vector3 ToVector((int X, int Y, int Z) v) => new(v.X, v.Y, v.Z);

    private readonly record struct Candidate(float Priority, MeshJob Job);

    private readonly record struct DrawItem(nint Buffer, int Quads, int Slot);

    private sealed class PendingEdit(long timestamp)
    {
        public long Timestamp { get; } = timestamp;

        public List<(Region Region, int ChunkIndex, int Version)> Chunks { get; } = [];

        public List<Region> Regions { get; } = [];
    }

    /// <summary>GPU mesh of one level of detail of a region.</summary>
    private sealed class LevelState
    {
        public Buffer* Buffer;
        public int Quads;
        public int BuiltVersion;
        public bool InFlight;
        public bool Available;
        public Vector3 Min;
        public Vector3 Max;
    }

    private sealed class Region
    {
        public Region(RegionCoord coord, int slot)
        {
            Coord = coord;
            Slot = slot;
            var (x, y, z) = coord.Origin;
            Origin = new Vector3(x, y, z);
            for (var i = 0; i < LodCount; i++)
            {
                Lods[i] = new LevelState();
            }
        }

        public RegionCoord Coord { get; }

        public int Slot { get; }

        public Vector3 Origin { get; }

        /// <summary>Box of the region's existing chunks, for culling and LOD before meshes exist.</summary>
        public Vector3 BoxMin { get; private set; }

        public Vector3 BoxMax { get; private set; }

        public LevelState[] Lods { get; } = new LevelState[LodCount];

        public bool[] ChunkExists { get; } = new bool[ChunksPerRegion];

        /// <summary>Current version of each chunk's voxels (starts at 1; incremented by edits).</summary>
        public int[] ChunkVersion { get; } = new int[ChunksPerRegion];

        /// <summary>Version each chunk's uploaded mesh was built from (0 = never).</summary>
        public int[] ChunkBuilt { get; } = new int[ChunksPerRegion];

        public bool[] ChunkInFlight { get; } = new bool[ChunksPerRegion];

        public int[] ChunkOffset { get; } = new int[ChunksPerRegion];

        public int[] ChunkQuads { get; } = new int[ChunksPerRegion];

        public Vector3[] ChunkMin { get; } = new Vector3[ChunksPerRegion];

        public Vector3[] ChunkMax { get; } = new Vector3[ChunksPerRegion];

        public MeshResult?[] PendingChunks { get; } = new MeshResult?[ChunksPerRegion];

        public bool HasPendingChunks { get; set; }

        /// <summary>Version of the region's voxels as a whole (for simplified levels).</summary>
        public int Version { get; set; } = 1;

        public int DesiredLod { get; set; } = -1;

        public int DisplayedLod { get; set; } = -1;

        /// <summary>Drawn in the last prepared frame.</summary>
        public bool Shown { get; set; }

        public static int ChunkIndex(ChunkCoord chunk)
        {
            var (x, y, z) = RegionCoord.LocalChunk(chunk);
            return x + z * RegionCoord.ChunksPerSide + y * RegionCoord.ChunksPerSide * RegionCoord.ChunksPerSide;
        }

        public void AddChunk(ChunkCoord chunk)
        {
            var index = ChunkIndex(chunk);
            ChunkExists[index] = true;
            ChunkVersion[index] = 1;
        }

        public void FinishSetup()
        {
            var top = 0;
            for (var i = 0; i < ChunksPerRegion; i++)
            {
                if (ChunkExists[i])
                {
                    top = Math.Max(top, (i / (RegionCoord.ChunksPerSide * RegionCoord.ChunksPerSide) + 1) * Chunk.Size);
                }
            }

            BoxMin = Origin;
            BoxMax = Origin + new Vector3(RegionCoord.Size, top, RegionCoord.Size);
        }

        /// <summary><see cref="Stopwatch"/> timestamp of the last frame that chose LOD 0 for this region.</summary>
        public long LastWantedLod0 { get; set; }

        /// <summary>A chunk mesh is being built or waits to be assembled into the LOD 0 buffer.</summary>
        public bool HasChunkWork()
        {
            if (HasPendingChunks)
            {
                return true;
            }

            foreach (var inFlight in ChunkInFlight)
            {
                if (inFlight)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A simplified level is built and can be drawn in place of LOD 0.</summary>
        public bool HasCoarserLevel()
        {
            for (var lod = 1; lod < LodCount; lod++)
            {
                if (Lods[lod].Available)
                {
                    return true;
                }
            }

            return false;
        }

        public bool AllChunksBuilt()
        {
            for (var i = 0; i < ChunksPerRegion; i++)
            {
                if (ChunkExists[i] && ChunkBuilt[i] == 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The level to draw: the desired one when built, otherwise the nearest built level (coarser
        /// first, so the view never shows holes); −1 when nothing is built yet.
        /// </summary>
        public int DrawableLod()
        {
            if (Lods[DesiredLod].Available)
            {
                return DesiredLod;
            }

            for (var step = 1; step < LodCount; step++)
            {
                if (DesiredLod + step < LodCount && Lods[DesiredLod + step].Available)
                {
                    return DesiredLod + step;
                }

                if (DesiredLod - step >= 0 && Lods[DesiredLod - step].Available)
                {
                    return DesiredLod - step;
                }
            }

            return -1;
        }
    }
}
