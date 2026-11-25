using Silk.NET.WebGPU;

namespace Talt.Client.Rendering;

/// <summary>One vertex buffer of a pipeline, kept in managed memory so pipelines can be created later.</summary>
internal sealed record VertexBufferSpec(ulong Stride, VertexStepMode StepMode, VertexAttribute[] Attributes);

/// <summary>How a world pipeline draws (feature 018): shader, vertex buffers and fixed state.</summary>
internal sealed unsafe class PipelineSpec
{
    public required ShaderModule* Shader { get; init; }

    public required VertexBufferSpec[] Buffers { get; init; }

    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;

    public CullMode Cull { get; init; } = CullMode.Back;

    /// <summary>Alpha blending with depth test but no depth writes (translucent layers).</summary>
    public bool Translucent { get; init; }

    /// <summary>Depth bias of the world pass (roads use a negative bias to win against the terrain).</summary>
    public int DepthBias { get; init; }

    public float DepthBiasSlopeScale { get; init; }

    /// <summary>Depth test of the world pass; null for Less (opaque) or LessEqual (translucent).</summary>
    public CompareFunction? DepthCompare { get; init; }

    /// <summary>Whether the pipeline also gets a depth-only variant for the shadow pass.</summary>
    public bool CastsShadow { get; init; } = true;
}

/// <summary>Pipeline specs shared by several renderers.</summary>
internal static unsafe class PipelineSpecs
{
    /// <summary>
    /// A unit cube (buffer 0: position and normal, see <see cref="UnitCube"/>) drawn once per instance
    /// (buffer 1: <paramref name="vec4Count"/> vec4 attributes at locations 2, 3, ...).
    /// </summary>
    public static PipelineSpec InstancedCube(ShaderModule* shader, ulong instanceStride, int vec4Count, bool translucent = false, bool castsShadow = true)
    {
        var instance = new VertexAttribute[vec4Count];
        for (var i = 0; i < vec4Count; i++)
        {
            instance[i] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = (ulong)(i * 16), ShaderLocation = (uint)(2 + i) };
        }

        return new PipelineSpec
        {
            Shader = shader,
            Buffers =
            [
                new VertexBufferSpec(24, VertexStepMode.Vertex,
                [
                    new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 },
                    new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 },
                ]),
                new VertexBufferSpec(instanceStride, VertexStepMode.Instance, instance),
            ],
            Translucent = translucent,
            CastsShadow = castsShadow && !translucent,
        };
    }
}

/// <summary>
/// What every world renderer needs to build pipelines (feature 018): device, formats, the two pipeline
/// layouts (world pass: groups 0 and 1; shadow pass: group 0 only, research R1) and the current MSAA
/// sample count, which <see cref="WebGpuRenderer"/> updates when anti-aliasing changes.
/// </summary>
internal sealed unsafe class WorldPassContext
{
    public required WebGPU Wgpu { get; init; }

    public required Device* Device { get; init; }

    public required Queue* Queue { get; init; }

    public required PipelineLayout* MainLayout { get; init; }

    public required PipelineLayout* ShadowLayout { get; init; }

    public required TextureFormat SurfaceFormat { get; init; }

    public required TextureFormat DepthFormat { get; init; }

    /// <summary>Samples per pixel of the world pass: 1 or 4 (WebGPU guarantees both).</summary>
    public int SampleCount { get; set; } = 1;
}

/// <summary>
/// The pipelines of one world renderer: one per MSAA sample count, created on first use so anti-aliasing
/// can change without a restart (research R4), plus the depth-only shadow pipeline.
/// </summary>
internal sealed unsafe class WorldPipeline : IDisposable
{
    private readonly WorldPassContext _context;
    private readonly PipelineSpec _spec;
    private RenderPipeline* _single;
    private RenderPipeline* _multi;
    private RenderPipeline* _shadow;

    public WorldPipeline(WorldPassContext context, PipelineSpec spec)
    {
        _context = context;
        _spec = spec;
    }

    /// <summary>The pipeline for the world pass at the current sample count.</summary>
    public RenderPipeline* Main
    {
        get
        {
            if (_context.SampleCount > 1)
            {
                return _multi != null ? _multi : _multi = Create(_context.SampleCount, shadow: false);
            }

            return _single != null ? _single : _single = Create(1, shadow: false);
        }
    }

    /// <summary>The depth-only pipeline for the shadow pass (null when the spec casts no shadow).</summary>
    public RenderPipeline* Shadow
    {
        get
        {
            if (!_spec.CastsShadow)
            {
                return null;
            }

            return _shadow != null ? _shadow : _shadow = Create(1, shadow: true);
        }
    }

    public void Dispose()
    {
        foreach (var pipeline in new[] { (nint)_single, (nint)_multi, (nint)_shadow })
        {
            if (pipeline != 0)
            {
                _context.Wgpu.RenderPipelineRelease((RenderPipeline*)pipeline);
            }
        }

        _single = _multi = _shadow = null;
    }

    private RenderPipeline* Create(int sampleCount, bool shadow)
    {
        var buffers = _spec.Buffers;
        var handles = new System.Runtime.InteropServices.GCHandle[buffers.Length];
        var layouts = stackalloc VertexBufferLayout[buffers.Length];
        try
        {
            for (var i = 0; i < buffers.Length; i++)
            {
                handles[i] = System.Runtime.InteropServices.GCHandle.Alloc(buffers[i].Attributes, System.Runtime.InteropServices.GCHandleType.Pinned);
                layouts[i] = new VertexBufferLayout
                {
                    ArrayStride = buffers[i].Stride,
                    StepMode = buffers[i].StepMode,
                    AttributeCount = (nuint)buffers[i].Attributes.Length,
                    Attributes = (VertexAttribute*)handles[i].AddrOfPinnedObject(),
                };
            }

            var blend = new BlendState
            {
                Color = new BlendComponent { SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
                Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            };
            var colorTarget = new ColorTargetState { Format = _context.SurfaceFormat, Blend = _spec.Translucent ? &blend : null, WriteMask = ColorWriteMask.All };
            var keep = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
            var depth = new DepthStencilState
            {
                Format = _context.DepthFormat,
                DepthWriteEnabled = shadow || !_spec.Translucent,
                DepthCompare = shadow ? CompareFunction.Less : _spec.DepthCompare ?? (_spec.Translucent ? CompareFunction.LessEqual : CompareFunction.Less),
                StencilFront = keep,
                StencilBack = keep,
                // Slope-scaled bias keeps lit faces from shadowing themselves (research R1).
                DepthBias = shadow ? 2 : _spec.DepthBias,
                DepthBiasSlopeScale = shadow ? 2.0f : _spec.DepthBiasSlopeScale,
            };

            fixed (byte* vertexEntry = "vs_main"u8)
            fixed (byte* fragmentEntry = "fs_main"u8)
            {
                var fragment = new FragmentState { Module = _spec.Shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &colorTarget };
                var descriptor = new RenderPipelineDescriptor
                {
                    Layout = shadow ? _context.ShadowLayout : _context.MainLayout,
                    Vertex = new VertexState { Module = _spec.Shader, EntryPoint = vertexEntry, BufferCount = (nuint)buffers.Length, Buffers = layouts },
                    Primitive = new PrimitiveState
                    {
                        Topology = _spec.Topology,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = shadow ? CullMode.None : _spec.Cull,
                    },
                    DepthStencil = &depth,
                    Multisample = new MultisampleState { Count = (uint)sampleCount, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                    Fragment = shadow ? null : &fragment,
                };
                return _context.Wgpu.DeviceCreateRenderPipeline(_context.Device, &descriptor);
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
    }
}
