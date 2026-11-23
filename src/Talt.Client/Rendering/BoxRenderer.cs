using System.Numerics;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>An axis-aligned box in world space with a linear colour and alpha (render-only floats).</summary>
internal readonly record struct BoxInstance(Vector4 Min, Vector4 Max, Vector4 Color)
{
    public static BoxInstance Create(Vector3 min, Vector3 max, Vector4 color) => new(new Vector4(min, 0f), new Vector4(max, 0f), color);
}

/// <summary>
/// Draws instanced boxes in the world pass (feature 006, research R12): one opaque layer (trees, depth
/// written) and one translucent layer (the construction preview, alpha blended, drawn last without
/// depth writes). Uses the world pipeline's uniforms (camera, light, fog).
/// </summary>
internal sealed unsafe class BoxRenderer : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly ShaderModule* _shader;
    private readonly RenderPipeline* _opaquePipeline;
    private readonly RenderPipeline* _translucentPipeline;
    private readonly Buffer* _cubeVertices;
    private readonly Buffer* _cubeIndices;
    private readonly ulong _cubeVertexBytes;
    private readonly uint _cubeIndexCount;
    private Layer _opaque;
    private Layer _translucent;

    public BoxRenderer(WebGPU wgpu, Device* device, Queue* queue, PipelineLayout* worldLayout, TextureFormat surfaceFormat, TextureFormat depthFormat)
    {
        _wgpu = wgpu;
        _device = device;
        _queue = queue;
        _shader = WebGpuShaders.CreateModule(wgpu, device, WebGpuShaders.Load("boxes.wgsl"));
        _opaquePipeline = CreatePipeline(worldLayout, surfaceFormat, depthFormat, translucent: false);
        _translucentPipeline = CreatePipeline(worldLayout, surfaceFormat, depthFormat, translucent: true);

        var (vertices, indices) = UnitCube.Build();
        _cubeVertices = CreateBuffer(vertices, BufferUsage.Vertex | BufferUsage.CopyDst);
        _cubeIndices = CreateBuffer(indices, BufferUsage.Index | BufferUsage.CopyDst);
        _cubeVertexBytes = (ulong)(vertices.Length * sizeof(float));
        _cubeIndexCount = (uint)indices.Length;
    }

    /// <summary>Replaces the opaque boxes (trees). Call only when they changed.</summary>
    public void UpdateOpaque(ReadOnlySpan<BoxInstance> boxes) => Upload(ref _opaque, boxes);

    /// <summary>Replaces the translucent boxes (preview). Call only when they changed.</summary>
    public void UpdateTranslucent(ReadOnlySpan<BoxInstance> boxes) => Upload(ref _translucent, boxes);

    /// <summary>Opaque boxes; call after the terrain, with the world bind group set.</summary>
    public void EncodeOpaque(RenderPassEncoder* pass) => Encode(pass, _opaquePipeline, _opaque);

    /// <summary>Translucent boxes; call last in the world pass, with the world bind group set.</summary>
    public void EncodeTranslucent(RenderPassEncoder* pass) => Encode(pass, _translucentPipeline, _translucent);

    public void Clear()
    {
        _opaque.Count = 0;
        _translucent.Count = 0;
    }

    public void Dispose()
    {
        Release(_opaque);
        Release(_translucent);
        _wgpu.BufferRelease(_cubeVertices);
        _wgpu.BufferRelease(_cubeIndices);
        _wgpu.RenderPipelineRelease(_opaquePipeline);
        _wgpu.RenderPipelineRelease(_translucentPipeline);
        _wgpu.ShaderModuleRelease(_shader);
    }

    private void Upload(ref Layer layer, ReadOnlySpan<BoxInstance> boxes)
    {
        if (boxes.Length > layer.Capacity)
        {
            Release(layer);
            layer.Capacity = Math.Max(boxes.Length, Math.Max(256, layer.Capacity * 2));
            var descriptor = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (ulong)(layer.Capacity * sizeof(BoxInstance)) };
            layer.Buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        }

        layer.Count = boxes.Length;
        if (boxes.Length > 0)
        {
            fixed (BoxInstance* data = boxes)
            {
                _wgpu.QueueWriteBuffer(_queue, layer.Buffer, 0, data, (nuint)(boxes.Length * sizeof(BoxInstance)));
            }
        }
    }

    private void Encode(RenderPassEncoder* pass, RenderPipeline* pipeline, Layer layer)
    {
        if (layer.Count == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertices, 0, _cubeVertexBytes);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, layer.Buffer, 0, (ulong)(layer.Count * sizeof(BoxInstance)));
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndices, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
        _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)layer.Count, 0, 0, 0);
    }

    private void Release(Layer layer)
    {
        if (layer.Buffer != null)
        {
            _wgpu.BufferRelease(layer.Buffer);
        }
    }

    private RenderPipeline* CreatePipeline(PipelineLayout* layout, TextureFormat surfaceFormat, TextureFormat depthFormat, bool translucent)
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
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(BoxInstance), StepMode = VertexStepMode.Instance, AttributeCount = 3, Attributes = instanceAttributes };

        var blend = new BlendState
        {
            Color = new BlendComponent { SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
        };
        var colorTarget = new ColorTargetState { Format = surfaceFormat, Blend = translucent ? &blend : null, WriteMask = ColorWriteMask.All };
        var keep = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
        var depth = new DepthStencilState
        {
            Format = depthFormat,
            DepthWriteEnabled = !translucent,
            DepthCompare = translucent ? CompareFunction.LessEqual : CompareFunction.Less,
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

    private struct Layer
    {
        public Buffer* Buffer;
        public int Capacity;
        public int Count;
    }
}
