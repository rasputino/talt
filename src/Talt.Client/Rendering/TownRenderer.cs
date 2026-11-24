using System.Diagnostics;
using System.Numerics;
using Silk.NET.WebGPU;
using Talt.Core.Maps;
using Talt.Core.Towns;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>
/// Draws the town buildings as instanced boxes (feature 008, research R12). The map is split into 128 × 128-tile
/// areas (like the roads); each area keeps one instance buffer with the walls first and the roofs after, so far
/// areas draw only the walls. Areas are rebuilt on the render thread within a time budget per frame, only when
/// visible and when a building in them changed. Culling: fog distance and frustum.
/// </summary>
internal sealed unsafe class TownRenderer : IDisposable
{
    public const int AreaTiles = 128;

    /// <summary>Areas nearer than this to the camera (tiles) are drawn with roofs.</summary>
    private const float DetailDistance = 320f;

    private const double BuildBudgetMs = 2.0;

    /// <summary>Highest box a building may reach above its base (32 storeys plus roof).</summary>
    private const float MaxBuildingHeight = (32 * TownInstances.StoreyHeight) + 1f;

    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly ShaderModule* _shader;
    private readonly RenderPipeline* _pipeline;
    private readonly Buffer* _cubeVertices;
    private readonly Buffer* _cubeIndices;
    private readonly ulong _cubeVertexBytes;
    private readonly uint _cubeIndexCount;
    private readonly TownRegistry _towns;
    private readonly MapData _map;
    private readonly int _areasX;
    private readonly Area[] _areas;
    private readonly int[] _areaBuildingCount;
    private readonly List<int> _changed = [];
    private readonly List<TownInstance> _walls = [];
    private readonly List<TownInstance> _roofs = [];
    private readonly (int Area, int Count)[] _drawList;
    private readonly float _maxGround;
    private int[] _areaStart;
    private int[] _areaSlots = [];
    private bool _indexDirty = true;
    private int _drawCount;

    public TownRenderer(WebGPU wgpu, Device* device, Queue* queue, PipelineLayout* worldLayout, TextureFormat surfaceFormat, TextureFormat depthFormat,
        TownRegistry towns, MapData map)
    {
        _wgpu = wgpu;
        _device = device;
        _queue = queue;
        _towns = towns;
        _map = map;
        _shader = WebGpuShaders.CreateModule(wgpu, device, WebGpuShaders.Load("towns.wgsl"));
        _pipeline = CreatePipeline(worldLayout, surfaceFormat, depthFormat);
        var (vertices, indices) = UnitCube.Build();
        _cubeVertices = CreateBuffer(vertices, BufferUsage.Vertex | BufferUsage.CopyDst);
        _cubeIndices = CreateBuffer(indices, BufferUsage.Index | BufferUsage.CopyDst);
        _cubeVertexBytes = (ulong)(vertices.Length * sizeof(float));
        _cubeIndexCount = (uint)indices.Length;
        _areasX = (map.Width + AreaTiles - 1) / AreaTiles;
        var areasZ = (map.Depth + AreaTiles - 1) / AreaTiles;
        _areas = new Area[_areasX * areasZ];
        _areaBuildingCount = new int[_areas.Length];
        _areaStart = new int[_areas.Length + 1];
        _drawList = new (int, int)[_areas.Length];
        var maxGround = 0;
        foreach (var h in map.Heights)
        {
            maxGround = Math.Max(maxGround, h);
        }

        _maxGround = maxGround;
        for (var a = 0; a < _areas.Length; a++)
        {
            _areas[a].Version = 1; // Everything needs building once.
        }

        towns.Buildings.TrackChanges = true;
        towns.Buildings.DiscardChangedTiles();
    }

    /// <summary>Draw calls of the last frame.</summary>
    public int Draws { get; private set; }

    /// <summary>Boxes drawn in the last frame.</summary>
    public long Instances { get; private set; }

    /// <summary>GPU bytes held by building instances.</summary>
    public long GpuBytes { get; private set; }

    /// <summary>Picks up changed buildings and builds the instance buffers the coming frame draws.</summary>
    public void Prepare(Matrix4x4 viewProjection, Vector3 cameraPosition, float fogDistance)
    {
        _changed.Clear();
        if (_towns.Buildings.TryTakeChangedTiles(_changed))
        {
            foreach (var tile in _changed)
            {
                _areas[AreaOfTile(tile)].Version++;
            }

            _indexDirty = true;
        }

        if (_indexDirty)
        {
            RebuildIndex();
        }

        var frustum = new Frustum(viewProjection);
        var start = Stopwatch.GetTimestamp();
        _drawCount = 0;
        Instances = 0;
        for (var area = 0; area < _areas.Length; area++)
        {
            if (_areaBuildingCount[area] == 0)
            {
                continue;
            }

            var ax = (area % _areasX) * AreaTiles;
            var az = (area / _areasX) * AreaTiles;
            var min = new Vector3(ax, 0, az);
            var max = new Vector3(ax + AreaTiles + 2, _maxGround + MaxBuildingHeight, az + AreaTiles + 2);
            var distance = Distance(cameraPosition, min, max);
            if (distance > fogDistance * 1.5f || !frustum.Intersects(min, max))
            {
                continue;
            }

            ref var entry = ref _areas[area];
            if (entry.BuiltVersion != entry.Version && (!OverBudget(start) || entry.Buffer == null))
            {
                if (OverBudget(start))
                {
                    continue; // Never built and out of time: next frame.
                }

                Build(area, ref entry);
            }

            var count = distance < DetailDistance ? entry.Walls + entry.Roofs : entry.Walls;
            if (count > 0 && entry.Buffer != null)
            {
                _drawList[_drawCount++] = (area, count);
                Instances += count;
            }
        }
    }

    /// <summary>Draws the buildings; call in the world pass with the world bind group set.</summary>
    public void Encode(RenderPassEncoder* pass)
    {
        Draws = 0;
        if (_drawCount == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertices, 0, _cubeVertexBytes);
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndices, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
        for (var i = 0; i < _drawCount; i++)
        {
            var (area, count) = _drawList[i];
            ref var entry = ref _areas[area];
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, entry.Buffer, 0, (ulong)(count * sizeof(TownInstance)));
            _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)count, 0, 0, 0);
            Draws++;
        }
    }

    public void Dispose()
    {
        _towns.Buildings.TrackChanges = false;
        foreach (ref var entry in _areas.AsSpan())
        {
            if (entry.Buffer != null)
            {
                _wgpu.BufferRelease(entry.Buffer);
                entry.Buffer = null;
            }
        }

        _wgpu.BufferRelease(_cubeVertices);
        _wgpu.BufferRelease(_cubeIndices);
        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.ShaderModuleRelease(_shader);
    }

    /// <summary>Buckets the building slots by area (counting sort over all buildings, after any change).</summary>
    private void RebuildIndex()
    {
        _indexDirty = false;
        var buildings = _towns.Buildings;
        Array.Clear(_areaBuildingCount);
        for (var slot = 0; slot < buildings.Count; slot++)
        {
            _areaBuildingCount[AreaOf(buildings.XAt(slot), buildings.ZAt(slot))]++;
        }

        _areaStart[0] = 0;
        for (var a = 0; a < _areas.Length; a++)
        {
            _areaStart[a + 1] = _areaStart[a] + _areaBuildingCount[a];
        }

        if (_areaSlots.Length < buildings.Count)
        {
            _areaSlots = new int[Math.Max(buildings.Count, _areaSlots.Length * 2)];
        }

        Span<int> fill = _areas.Length <= 1024 ? stackalloc int[_areas.Length] : new int[_areas.Length];
        for (var a = 0; a < _areas.Length; a++)
        {
            fill[a] = _areaStart[a];
        }

        for (var slot = 0; slot < buildings.Count; slot++)
        {
            _areaSlots[fill[AreaOf(buildings.XAt(slot), buildings.ZAt(slot))]++] = slot;
        }
    }

    private void Build(int area, ref Area entry)
    {
        _walls.Clear();
        _roofs.Clear();
        var buildings = _towns.Buildings;
        for (var i = _areaStart[area]; i < _areaStart[area + 1]; i++)
        {
            var slot = _areaSlots[i];
            TownInstances.Append(buildings.TypeDefinitionAt(slot), buildings.XAt(slot), buildings.ZAt(slot), buildings.BaseHeightAt(slot), buildings.IdAt(slot), _walls, _roofs);
        }

        entry.BuiltVersion = entry.Version;
        entry.Walls = _walls.Count;
        entry.Roofs = _roofs.Count;
        var total = _walls.Count + _roofs.Count;
        if (total == 0)
        {
            return;
        }

        var bytes = (ulong)(total * sizeof(TownInstance));
        if (entry.Buffer == null || entry.Capacity < bytes)
        {
            if (entry.Buffer != null)
            {
                _wgpu.BufferRelease(entry.Buffer);
                GpuBytes -= (long)entry.Capacity;
            }

            var size = Math.Max(bytes + (bytes / 4), 4096UL);
            var descriptor = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (size + 3) & ~3UL };
            entry.Buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
            entry.Capacity = descriptor.Size;
            GpuBytes += (long)descriptor.Size;
        }

        var walls = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_walls);
        var roofs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_roofs);
        fixed (TownInstance* w = walls)
        {
            _wgpu.QueueWriteBuffer(_queue, entry.Buffer, 0, w, (nuint)(walls.Length * sizeof(TownInstance)));
        }

        if (roofs.Length > 0)
        {
            fixed (TownInstance* r = roofs)
            {
                _wgpu.QueueWriteBuffer(_queue, entry.Buffer, (ulong)(walls.Length * sizeof(TownInstance)), r, (nuint)(roofs.Length * sizeof(TownInstance)));
            }
        }
    }

    private int AreaOf(int x, int z) => (x / AreaTiles) + ((z / AreaTiles) * _areasX);

    private int AreaOfTile(int tile) => AreaOf(tile % _map.Width, tile / _map.Width);

    private static bool OverBudget(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds > BuildBudgetMs;

    private static float Distance(Vector3 point, Vector3 min, Vector3 max) => Vector3.Distance(point, Vector3.Clamp(point, min, max));

    private Buffer* CreateBuffer<T>(T[] data, BufferUsage usage)
        where T : unmanaged
    {
        var size = (ulong)(data.Length * sizeof(T));
        var descriptor = new BufferDescriptor { Usage = usage, Size = (size + 3) & ~3UL };
        var buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        fixed (T* pointer = data)
        {
            _wgpu.QueueWriteBuffer(_queue, buffer, 0, pointer, (nuint)size);
        }

        return buffer;
    }

    private RenderPipeline* CreatePipeline(PipelineLayout* layout, TextureFormat surfaceFormat, TextureFormat depthFormat)
    {
        var cubeAttributes = stackalloc VertexAttribute[2];
        cubeAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        cubeAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        var instanceAttributes = stackalloc VertexAttribute[3];
        instanceAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 2 };
        instanceAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 16, ShaderLocation = 3 };
        instanceAttributes[2] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 32, ShaderLocation = 4 };
        var layouts = stackalloc VertexBufferLayout[2];
        layouts[0] = new VertexBufferLayout { ArrayStride = 24, StepMode = VertexStepMode.Vertex, AttributeCount = 2, Attributes = cubeAttributes };
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(TownInstance), StepMode = VertexStepMode.Instance, AttributeCount = 3, Attributes = instanceAttributes };
        var colorTarget = new ColorTargetState { Format = surfaceFormat, Blend = null, WriteMask = ColorWriteMask.All };
        var keep = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
        var depth = new DepthStencilState
        {
            Format = depthFormat,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.Less,
            StencilFront = keep,
            StencilBack = keep,
        };
        fixed (byte* vertexEntry = "vs_main"u8)
        fixed (byte* fragmentEntry = "fs_main"u8)
        {
            var fragment = new FragmentState { Module = _shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &colorTarget };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = layout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vertexEntry, BufferCount = 2, Buffers = layouts },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, StripIndexFormat = IndexFormat.Undefined, FrontFace = FrontFace.Ccw, CullMode = CullMode.Back },
                DepthStencil = &depth,
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            return _wgpu.DeviceCreateRenderPipeline(_device, &descriptor);
        }
    }

    private struct Area
    {
        public Buffer* Buffer;
        public ulong Capacity;
        public int Walls;
        public int Roofs;
        public int Version;
        public int BuiltVersion;
    }
}
