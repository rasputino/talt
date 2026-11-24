using System.Diagnostics;
using System.Numerics;
using Silk.NET.WebGPU;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>
/// Draws the roads as their own meshes on top of the terrain (research R10, FR-050). The map is split into
/// 128 × 128-tile areas (the terrain's regions): near the camera an area is drawn from detailed meshes of its
/// 32 × 32-tile blocks, farther away from one simplified mesh. Meshes are built on the render thread within
/// a time budget per frame, only for areas that are visible and have roads, and rebuilt only where road pieces
/// changed; detailed meshes unused for a few seconds are released. Culling: fog distance and frustum.
/// </summary>
internal sealed unsafe class RoadRenderer : IDisposable
{
    public const int BlockTiles = 32;
    public const int AreaTiles = 128;
    private const int BlocksPerArea = AreaTiles / BlockTiles;

    /// <summary>Areas whose box is nearer than this to the camera (tiles) are drawn in full detail.</summary>
    private const float DetailDistance = 220f;

    /// <summary>Most milliseconds of mesh building and uploading per frame.</summary>
    private const double BuildBudgetMs = 2.0;

    private const double ReleaseAfterSeconds = 5.0;

    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly ShaderModule* _shader;
    private readonly RenderPipeline* _pipeline;
    private readonly RoadLayer _roads;
    private readonly RoadMesher _mesher;
    private readonly List<int> _changed = [];
    private readonly int _blocksX;
    private readonly int _areasX;
    private readonly Mesh[] _blocks;
    private readonly Mesh[] _areas;
    private readonly int[] _blockRoads;
    private readonly int[] _areaRoads;
    private readonly int[] _drawList;
    private readonly float _maxHeight;
    private int _drawCount;
    private double _lastReleaseCheck;

    public RoadRenderer(WebGPU wgpu, Device* device, Queue* queue, PipelineLayout* worldLayout, TextureFormat surfaceFormat, TextureFormat depthFormat,
        RoadLayer roads, MapData map, RoadRules rules)
    {
        _wgpu = wgpu;
        _device = device;
        _queue = queue;
        _roads = roads;
        _mesher = new RoadMesher(roads, map, rules);
        _shader = WebGpuShaders.CreateModule(wgpu, device, WebGpuShaders.Load("roads.wgsl"));
        _pipeline = CreatePipeline(worldLayout, surfaceFormat, depthFormat);
        _blocksX = (map.Width + BlockTiles - 1) / BlockTiles;
        var blocksZ = (map.Depth + BlockTiles - 1) / BlockTiles;
        _areasX = (map.Width + AreaTiles - 1) / AreaTiles;
        var areasZ = (map.Depth + AreaTiles - 1) / AreaTiles;
        _blocks = new Mesh[_blocksX * blocksZ];
        _areas = new Mesh[_areasX * areasZ];
        _blockRoads = new int[_blocks.Length];
        _areaRoads = new int[_areas.Length];
        _drawList = new int[_blocks.Length + _areas.Length];
        _maxHeight = ConstructionHeight(map);
        for (var tile = 0; tile < roads.Width * roads.Depth; tile++)
        {
            if (roads.HasRoad(tile) || (roads.FlagsAt(tile) & RoadFeatures.Passing) != 0)
            {
                _blockRoads[BlockOf(tile)]++;
            }
        }

        for (var b = 0; b < _blocks.Length; b++)
        {
            _areaRoads[AreaOfBlock(b)] += _blockRoads[b];
        }
    }

    /// <summary>Road draw calls of the last frame.</summary>
    public int Draws { get; private set; }

    /// <summary>Road triangles drawn in the last frame.</summary>
    public long Triangles { get; private set; }

    /// <summary>GPU bytes held by road meshes.</summary>
    public long GpuBytes { get; private set; }

    /// <summary>Picks up changed road pieces and builds the meshes the coming frame draws.</summary>
    public void Prepare(Matrix4x4 viewProjection, Vector3 cameraPosition, float fogDistance)
    {
        _changed.Clear();
        if (_roads.TryTakeChangedTiles(_changed))
        {
            foreach (var tile in _changed)
            {
                var block = BlockOf(tile);
                _blocks[block].Version++;
                _areas[AreaOfBlock(block)].Version++;
            }

            RecountChangedBlocks();
        }

        var frustum = new Frustum(viewProjection);
        var start = Stopwatch.GetTimestamp();
        var now = start / (double)Stopwatch.Frequency;
        _drawCount = 0;
        Triangles = 0;
        for (var area = 0; area < _areas.Length; area++)
        {
            if (_areaRoads[area] == 0)
            {
                continue;
            }

            var ax = (area % _areasX) * AreaTiles;
            var az = (area / _areasX) * AreaTiles;
            var min = new Vector3(ax, 0, az);
            var max = new Vector3(ax + AreaTiles, _maxHeight, az + AreaTiles);
            var distance = Distance(cameraPosition, min, max);
            if (distance > fogDistance * 1.5f || !frustum.Intersects(min, max))
            {
                continue;
            }

            if (distance < DetailDistance && DrawDetail(area, frustum, start, now))
            {
                continue;
            }

            ref var mesh = ref _areas[area];
            if (!mesh.Built || mesh.BuiltVersion != mesh.Version)
            {
                if (OverBudget(start) && !mesh.Built)
                {
                    continue;
                }

                if (!OverBudget(start))
                {
                    _mesher.BuildSimplified(ax, az, ax + AreaTiles, az + AreaTiles);
                    Upload(ref mesh);
                }
            }

            Add(_blocks.Length + area, mesh);
        }

        if (now - _lastReleaseCheck > 1.0)
        {
            _lastReleaseCheck = now;
            ReleaseUnused(now);
        }
    }

    /// <summary>Draws the roads; call in the world pass with the world bind group set.</summary>
    public void Encode(RenderPassEncoder* pass)
    {
        Draws = 0;
        if (_drawCount == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        for (var i = 0; i < _drawCount; i++)
        {
            var index = _drawList[i];
            var mesh = index < _blocks.Length ? _blocks[index] : _areas[index - _blocks.Length];
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, mesh.Buffer, 0, (ulong)(mesh.Vertices * sizeof(RoadVertex)));
            _wgpu.RenderPassEncoderDraw(pass, (uint)mesh.Vertices, 1, 0, 0);
            Draws++;
        }
    }

    public void Dispose()
    {
        foreach (ref var mesh in _blocks.AsSpan())
        {
            Release(ref mesh);
        }

        foreach (ref var mesh in _areas.AsSpan())
        {
            Release(ref mesh);
        }

        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.ShaderModuleRelease(_shader);
    }

    /// <summary>Queues the visible detailed blocks of an area; false when one is not ready (the area is drawn simplified).</summary>
    private bool DrawDetail(int area, Frustum frustum, long start, double now)
    {
        var abx = (area % _areasX) * BlocksPerArea;
        var abz = (area / _areasX) * BlocksPerArea;
        var blocksZ = _blocks.Length / _blocksX;
        var first = _drawCount;
        for (var bz = abz; bz < Math.Min(abz + BlocksPerArea, blocksZ); bz++)
        {
            for (var bx = abx; bx < Math.Min(abx + BlocksPerArea, _blocksX); bx++)
            {
                var block = bx + (bz * _blocksX);
                if (_blockRoads[block] == 0)
                {
                    continue;
                }

                ref var mesh = ref _blocks[block];
                if (!mesh.Built || mesh.BuiltVersion != mesh.Version)
                {
                    if (OverBudget(start))
                    {
                        if (!mesh.Built)
                        {
                            _drawCount = first; // Not ready: draw the whole area simplified this frame.
                            return false;
                        }
                    }
                    else
                    {
                        _mesher.BuildDetail(bx * BlockTiles, bz * BlockTiles, (bx + 1) * BlockTiles, (bz + 1) * BlockTiles);
                        Upload(ref mesh);
                    }
                }

                mesh.LastUsed = now;
                if (mesh.Vertices > 0 && frustum.Intersects(mesh.Min, mesh.Max))
                {
                    Add(block, mesh);
                }
            }
        }

        return true;
    }

    private void Add(int index, in Mesh mesh)
    {
        if (mesh.Vertices == 0 || mesh.Buffer == null)
        {
            return;
        }

        _drawList[_drawCount++] = index;
        Triangles += mesh.Vertices / 3;
    }

    private void Upload(ref Mesh mesh)
    {
        var vertices = _mesher.Vertices;
        mesh.BuiltVersion = mesh.Version;
        mesh.Built = true;
        mesh.Min = _mesher.Min;
        mesh.Max = _mesher.Max;
        mesh.Vertices = vertices.Length;
        var bytes = (ulong)(vertices.Length * sizeof(RoadVertex));
        if (mesh.Buffer == null || mesh.Capacity < bytes)
        {
            Release(ref mesh);
            if (vertices.Length == 0)
            {
                return;
            }

            var size = Math.Max(bytes, 4096UL);
            var descriptor = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (size + 3) & ~3UL };
            mesh.Buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
            mesh.Capacity = descriptor.Size;
            GpuBytes += (long)descriptor.Size;
        }

        if (vertices.Length > 0)
        {
            fixed (RoadVertex* data = vertices)
            {
                _wgpu.QueueWriteBuffer(_queue, mesh.Buffer, 0, data, (nuint)bytes);
            }
        }
    }

    private void Release(ref Mesh mesh)
    {
        if (mesh.Buffer != null)
        {
            _wgpu.BufferRelease(mesh.Buffer);
            GpuBytes -= (long)mesh.Capacity;
            mesh.Buffer = null;
            mesh.Capacity = 0;
        }
    }

    private void ReleaseUnused(double now)
    {
        for (var b = 0; b < _blocks.Length; b++)
        {
            ref var mesh = ref _blocks[b];
            if (mesh.Built && now - mesh.LastUsed > ReleaseAfterSeconds)
            {
                Release(ref mesh);
                mesh.Built = false;
            }
        }
    }

    private void RecountChangedBlocks()
    {
        foreach (var tile in _changed)
        {
            var block = BlockOf(tile);
            if (_blockRoads[block] < 0)
            {
                continue; // Already recounted this frame.
            }

            var old = _blockRoads[block];
            var bx = (block % _blocksX) * BlockTiles;
            var bz = (block / _blocksX) * BlockTiles;
            var count = 0;
            for (var z = bz; z < Math.Min(bz + BlockTiles, _roads.Depth); z++)
            {
                for (var x = bx; x < Math.Min(bx + BlockTiles, _roads.Width); x++)
                {
                    var t = x + (z * _roads.Width);
                    if (_roads.HasRoad(t) || (_roads.FlagsAt(t) & RoadFeatures.Passing) != 0)
                    {
                        count++;
                    }
                }
            }

            _areaRoads[AreaOfBlock(block)] += count - old;
            _blockRoads[block] = -1 - count; // Marked as recounted.
        }

        foreach (var tile in _changed)
        {
            var block = BlockOf(tile);
            if (_blockRoads[block] < 0)
            {
                _blockRoads[block] = -1 - _blockRoads[block];
            }
        }
    }

    private int BlockOf(int tile) => ((tile % _roads.Width) / BlockTiles) + ((tile / _roads.Width / BlockTiles) * _blocksX);

    private int AreaOfBlock(int block) => ((block % _blocksX) / BlocksPerArea) + ((block / _blocksX / BlocksPerArea) * _areasX);

    private static bool OverBudget(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds > BuildBudgetMs;

    private static float Distance(Vector3 point, Vector3 min, Vector3 max)
    {
        var clamped = Vector3.Clamp(point, min, max);
        return Vector3.Distance(point, clamped);
    }

    private static float ConstructionHeight(MapData map)
    {
        var max = 0;
        foreach (var h in map.Heights)
        {
            max = Math.Max(max, h);
        }

        return max + 3f;
    }

    private RenderPipeline* CreatePipeline(PipelineLayout* layout, TextureFormat surfaceFormat, TextureFormat depthFormat)
    {
        var attributes = stackalloc VertexAttribute[3];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 24, ShaderLocation = 2 };
        var vertexLayout = new VertexBufferLayout { ArrayStride = (ulong)sizeof(RoadVertex), StepMode = VertexStepMode.Vertex, AttributeCount = 3, Attributes = attributes };
        var colorTarget = new ColorTargetState { Format = surfaceFormat, Blend = null, WriteMask = ColorWriteMask.All };
        var keep = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
        var depth = new DepthStencilState
        {
            Format = depthFormat,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.LessEqual,
            StencilFront = keep,
            StencilBack = keep,
            DepthBias = -2,
            DepthBiasSlopeScale = -1f,
        };
        fixed (byte* vertexEntry = "vs_main"u8)
        fixed (byte* fragmentEntry = "fs_main"u8)
        {
            var fragment = new FragmentState { Module = _shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &colorTarget };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = layout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &vertexLayout },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, StripIndexFormat = IndexFormat.Undefined, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                DepthStencil = &depth,
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            return _wgpu.DeviceCreateRenderPipeline(_device, &descriptor);
        }
    }

    private struct Mesh
    {
        public Buffer* Buffer;
        public ulong Capacity;
        public int Vertices;
        public int Version;
        public int BuiltVersion;
        public bool Built;
        public Vector3 Min;
        public Vector3 Max;
        public double LastUsed;
    }
}
