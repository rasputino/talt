using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Talt.Core.Meshing;
using Talt.Core.Voxels;
using Buffer = Silk.NET.WebGPU.Buffer;
using Color = Silk.NET.WebGPU.Color;

namespace Talt.Client.Rendering;

/// <summary>
/// WebGPU renderer: owns the device, the pipelines and the frame; the voxel world is drawn by
/// <see cref="WorldRenderer"/> and vehicles by instancing. WebGPU maps to Vulkan, Metal or DirectX 12
/// depending on the platform.
/// </summary>
internal sealed unsafe class WebGpuRenderer : IDisposable
{
    private const TextureFormat DepthFormat = TextureFormat.Depth32float;

    /// <summary>
    /// Frames the CPU may submit before waiting for the GPU. Without this limit, when vertical sync
    /// does not throttle (window hidden behind others, some drivers) the CPU queues frames and buffer
    /// uploads faster than the GPU finishes them until memory runs out.
    /// </summary>
    private const int MaxFramesInFlight = 2;

    private static readonly Vector3 SkyColor = new(0.55f, 0.75f, 0.95f);

    // Native code keeps pointers to these callbacks, so the delegates must stay alive.
    private static readonly ErrorCallback OnError = HandleError;
    private static readonly PfnErrorCallback OnErrorPfn = new(OnError);

    private readonly WebGPU _wgpu;
    private WorldRenderer? _worldRenderer;

    private Instance* _instance;
    private Surface* _surface;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private TextureFormat _surfaceFormat;

    private ShaderModule* _shader;
    private BindGroupLayout* _bindGroupLayout;
    private PipelineLayout* _pipelineLayout;
    private RenderPipeline* _pipeline;
    private Buffer* _uniformBuffer;
    private BindGroup* _bindGroup;

    private ShaderModule* _vehicleShader;
    private RenderPipeline* _vehiclePipeline;
    private Buffer* _cubeVertexBuffer;
    private Buffer* _cubeIndexBuffer;
    private ulong _cubeVertexBytes;
    private uint _cubeIndexCount;
    private Buffer* _instanceBuffer;
    private int _instanceCapacity;
    private int _instanceCount;

    private Texture* _depthTexture;
    private TextureView* _depthView;
    private Vector2D<int> _size;
    private readonly ulong[] _submissions = new ulong[MaxFramesInFlight];
    private long _frameNumber;
    private bool _disposed;

    public WebGpuRenderer(IWindow window, VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(world);

        _wgpu = WebGPU.GetApi();

        var instanceDescriptor = new InstanceDescriptor();
        _instance = _wgpu.CreateInstance(&instanceDescriptor);
        _surface = window.CreateWebGPUSurface(_wgpu, _instance);

        RequestAdapter();
        RequestDevice();
        _queue = _wgpu.DeviceGetQueue(_device);
        _wgpu.DeviceSetUncapturedErrorCallback(_device, OnErrorPfn, null);

        _surfaceFormat = ChooseSurfaceFormat();
        Resize(window.FramebufferSize);
        CreatePipeline();
        CreateVehiclePipeline();
        _worldRenderer = new WorldRenderer(_wgpu, _device, _queue, world);
    }

    public string AdapterDescription { get; private set; } = "unknown";

    /// <summary>Counters of the last drawn frame.</summary>
    public RenderStatistics Statistics => _worldRenderer?.Statistics ?? default;

    /// <summary>Where the last <see cref="Draw"/> spent its time, for diagnosing long frames.</summary>
    public DrawTimings LastDrawTimings { get; private set; }

    /// <inheritdoc cref="WorldRenderer.NotifyVoxelsChanged"/>
    public int NotifyVoxelsChanged(IReadOnlyList<(int X, int Y, int Z)> voxels, long timestamp) =>
        _worldRenderer?.NotifyVoxelsChanged(voxels, timestamp) ?? 0;

    /// <inheritdoc cref="WorldRenderer.TryTakeCompletedEdit"/>
    public bool TryTakeCompletedEdit(out long timestamp, out int chunks)
    {
        timestamp = 0;
        chunks = 0;
        return _worldRenderer is not null && _worldRenderer.TryTakeCompletedEdit(out timestamp, out chunks);
    }

    /// <summary>Replaces the vehicles drawn in the next frames. Grows the GPU buffer when needed.</summary>
    public void UpdateVehicles(ReadOnlySpan<VehicleInstance> instances)
    {
        if (_disposed || _size.X <= 0 || _size.Y <= 0)
        {
            return; // Minimized: nothing is submitted, so uploads would only pile up.
        }

        if (instances.Length > _instanceCapacity)
        {
            if (_instanceBuffer != null)
            {
                _wgpu.BufferRelease(_instanceBuffer);
            }

            _instanceCapacity = Math.Max(instances.Length, _instanceCapacity * 2);
            var descriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Size = (ulong)(_instanceCapacity * sizeof(VehicleInstance)),
            };
            _instanceBuffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        }

        _instanceCount = instances.Length;
        if (_instanceCount > 0)
        {
            fixed (VehicleInstance* data = instances)
            {
                _wgpu.QueueWriteBuffer(_queue, _instanceBuffer, 0, data, (nuint)(_instanceCount * sizeof(VehicleInstance)));
            }
        }
    }

    public void Resize(Vector2D<int> size)
    {
        _size = size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return; // Minimized.
        }

        var configuration = new SurfaceConfiguration
        {
            Device = _device,
            Format = _surfaceFormat,
            Usage = TextureUsage.RenderAttachment,
            AlphaMode = CompositeAlphaMode.Auto,
            Width = (uint)size.X,
            Height = (uint)size.Y,
            PresentMode = PresentMode.Fifo,
        };
        _wgpu.SurfaceConfigure(_surface, &configuration);

        ReleaseDepthTexture();
        var depthDescriptor = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)size.X, (uint)size.Y, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _depthTexture = _wgpu.DeviceCreateTexture(_device, &depthDescriptor);
        _depthView = _wgpu.TextureCreateView(_depthTexture, null);
    }

    public void Draw(OrbitCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (_disposed || _size.X <= 0 || _size.Y <= 0)
        {
            return;
        }

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        var acquired = System.Diagnostics.Stopwatch.GetTimestamp();
        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            if (surfaceTexture.Texture != null)
            {
                _wgpu.TextureRelease(surfaceTexture.Texture);
            }

            Resize(_size); // Outdated or lost: reconfigure and try again next frame.
            return;
        }

        var viewProjection = camera.ViewProjection(_size.X / (float)_size.Y);
        var fogDistance = camera.Distance * 2.5f;
        var uniforms = new Uniforms
        {
            ViewProjection = viewProjection,
            LightDirection = new Vector4(Vector3.Normalize(new Vector3(-0.45f, -1f, -0.3f)), 0f),
            CameraPosition = new Vector4(camera.Position, fogDistance),
            SkyColor = new Vector4(SkyColor, 1f),
        };
        for (var m = 0; m < Uniforms.PaletteSize; m++)
        {
            uniforms.Palette[m] = new Vector4(MaterialPalette.BaseColor((VoxelMaterial)m), 1f);
        }

        _wgpu.QueueWriteBuffer(_queue, _uniformBuffer, 0, &uniforms, (nuint)sizeof(Uniforms));

        var colorView = _wgpu.TextureCreateView(surfaceTexture.Texture, null);
        var encoder = _wgpu.DeviceCreateCommandEncoder(_device, null);
        _worldRenderer!.Prepare(encoder, viewProjection, camera.Position, fogDistance, _size.Y, camera.FieldOfView);
        var prepared = System.Diagnostics.Stopwatch.GetTimestamp();

        var colorAttachment = new RenderPassColorAttachment
        {
            View = colorView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(SkyColor.X, SkyColor.Y, SkyColor.Z, 1),
            DepthSlice = uint.MaxValue, // WGPU_DEPTH_SLICE_UNDEFINED: not a 3D texture.
        };
        var depthAttachment = new RenderPassDepthStencilAttachment
        {
            View = _depthView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1f,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
        };
        var passDescriptor = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
            DepthStencilAttachment = &depthAttachment,
        };

        var pass = _wgpu.CommandEncoderBeginRenderPass(encoder, &passDescriptor);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);

        _worldRenderer.Encode(pass);

        if (_instanceCount > 0)
        {
            _wgpu.RenderPassEncoderSetPipeline(pass, _vehiclePipeline);
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertexBuffer, 0, _cubeVertexBytes);
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _instanceBuffer, 0, (ulong)(_instanceCount * sizeof(VehicleInstance)));
            _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndexBuffer, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
            _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)_instanceCount, 0, 0, 0);
        }

        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);

        var commands = _wgpu.CommandEncoderFinish(encoder, null);
        var encoded = System.Diagnostics.Stopwatch.GetTimestamp();
        WaitForFrameSlot();
        var waited = System.Diagnostics.Stopwatch.GetTimestamp();
        _submissions[_frameNumber % MaxFramesInFlight] = NativeWgpu.QueueSubmitForIndex(_queue, 1, &commands);
        _frameNumber++;
        _worldRenderer.AfterSubmit();
        _wgpu.SurfacePresent(_surface);
        var presented = System.Diagnostics.Stopwatch.GetTimestamp();
        LastDrawTimings = new DrawTimings(
            Ms(start, acquired), Ms(acquired, prepared), Ms(prepared, encoded), Ms(encoded, waited), Ms(waited, presented));

        _wgpu.CommandBufferRelease(commands);
        _wgpu.CommandEncoderRelease(encoder);
        _wgpu.TextureViewRelease(colorView);
        _wgpu.TextureRelease(surfaceTexture.Texture);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _worldRenderer?.Dispose();
        _worldRenderer = null;
        ReleaseDepthTexture();

        if (_instanceBuffer != null)
        {
            _wgpu.BufferRelease(_instanceBuffer);
        }

        _wgpu.BufferRelease(_cubeVertexBuffer);
        _wgpu.BufferRelease(_cubeIndexBuffer);
        _wgpu.RenderPipelineRelease(_vehiclePipeline);
        _wgpu.ShaderModuleRelease(_vehicleShader);

        _wgpu.BindGroupRelease(_bindGroup);
        _wgpu.BufferRelease(_uniformBuffer);
        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.PipelineLayoutRelease(_pipelineLayout);
        _wgpu.BindGroupLayoutRelease(_bindGroupLayout);
        _wgpu.ShaderModuleRelease(_shader);
        _wgpu.QueueRelease(_queue);
        _wgpu.DeviceRelease(_device);
        _wgpu.AdapterRelease(_adapter);
        _wgpu.SurfaceRelease(_surface);
        _wgpu.InstanceRelease(_instance);

        // Do not call _wgpu.Dispose(): it unloads wgpu-native while the library's internal threads
        // may still be shutting down, which crashes the process on exit (access violation). The OS
        // frees the library when the process ends.
    }

    private static double Ms(long from, long to) => System.Diagnostics.Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    /// <summary>Blocks until the frame submitted <see cref="MaxFramesInFlight"/> frames ago is finished.</summary>
    private void WaitForFrameSlot()
    {
        if (_frameNumber < MaxFramesInFlight)
        {
            return;
        }

        var index = new NativeWgpu.WrappedSubmissionIndex
        {
            Queue = _queue,
            SubmissionIndex = _submissions[_frameNumber % MaxFramesInFlight],
        };
        _ = NativeWgpu.DevicePoll(_device, 1, &index); // Returns whether the queue is empty; not needed.
    }

    private void RequestAdapter()
    {
        var options = new RequestAdapterOptions
        {
            CompatibleSurface = _surface,
            PowerPreference = PowerPreference.HighPerformance,
        };

        string? error = null;
        // wgpu-native invokes this callback before the call returns.
        var callback = new PfnRequestAdapterCallback((status, adapter, message, _) =>
        {
            if (status == RequestAdapterStatus.Success)
            {
                _adapter = adapter;
            }
            else
            {
                error = SilkMarshal.PtrToString((nint)message);
            }
        });
        _wgpu.InstanceRequestAdapter(_instance, &options, callback, null);

        if (_adapter == null)
        {
            throw new InvalidOperationException($"No compatible GPU adapter found: {error}");
        }

        AdapterProperties properties;
        _wgpu.AdapterGetProperties(_adapter, &properties);
        AdapterDescription = $"{SilkMarshal.PtrToString((nint)properties.Name)} ({properties.BackendType})";
    }

    private void RequestDevice()
    {
        var descriptor = new DeviceDescriptor();
        string? error = null;
        var callback = new PfnRequestDeviceCallback((status, device, message, _) =>
        {
            if (status == RequestDeviceStatus.Success)
            {
                _device = device;
            }
            else
            {
                error = SilkMarshal.PtrToString((nint)message);
            }
        });
        _wgpu.AdapterRequestDevice(_adapter, &descriptor, callback, null);

        if (_device == null)
        {
            throw new InvalidOperationException($"Could not create GPU device: {error}");
        }
    }

    /// <summary>Prefers an sRGB format so the GPU converts our linear colors automatically.</summary>
    private TextureFormat ChooseSurfaceFormat()
    {
        SurfaceCapabilities capabilities;
        _wgpu.SurfaceGetCapabilities(_surface, _adapter, &capabilities);

        if (capabilities.FormatCount == 0)
        {
            throw new InvalidOperationException("The window surface reports no supported formats.");
        }

        var formats = new ReadOnlySpan<TextureFormat>(capabilities.Formats, (int)capabilities.FormatCount);
        foreach (var format in formats)
        {
            if (format is TextureFormat.Bgra8UnormSrgb or TextureFormat.Rgba8UnormSrgb)
            {
                return format;
            }
        }

        return formats[0];
    }

    private void CreatePipeline()
    {
        _shader = CreateShaderModule(LoadShaderSource("voxel.wgsl"));

        var layoutEntry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(Uniforms) },
        };
        var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &layoutEntry };
        _bindGroupLayout = _wgpu.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);

        var bindGroupLayout = _bindGroupLayout;
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &bindGroupLayout };
        _pipelineLayout = _wgpu.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);

        var uniformDescriptor = new BufferDescriptor
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = (ulong)sizeof(Uniforms),
        };
        _uniformBuffer = _wgpu.DeviceCreateBuffer(_device, &uniformDescriptor);

        var bindGroupEntry = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Offset = 0, Size = (ulong)sizeof(Uniforms) };
        var bindGroupDescriptor = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 1, Entries = &bindGroupEntry };
        _bindGroup = _wgpu.DeviceCreateBindGroup(_device, &bindGroupDescriptor);

        // Buffer 0: packed vertices (contracts/meshing-api.md). Buffer 1: region origin per instance.
        var vertexAttribute = new VertexAttribute { Format = VertexFormat.Uint32x2, Offset = 0, ShaderLocation = 0 };
        var originAttribute = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 1 };
        var layouts = stackalloc VertexBufferLayout[2];
        layouts[0] = new VertexBufferLayout { ArrayStride = PackedVertex.SizeInBytes, StepMode = VertexStepMode.Vertex, AttributeCount = 1, Attributes = &vertexAttribute };
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(Vector4), StepMode = VertexStepMode.Instance, AttributeCount = 1, Attributes = &originAttribute };

        _pipeline = CreateRenderPipeline(_shader, layouts, 2);
    }

    /// <summary>Pipeline for vehicles: a unit cube drawn once per vehicle (instancing).</summary>
    private void CreateVehiclePipeline()
    {
        _vehicleShader = CreateShaderModule(LoadShaderSource("vehicles.wgsl"));

        var cubeAttributes = stackalloc VertexAttribute[2];
        cubeAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        cubeAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        var instanceAttributes = stackalloc VertexAttribute[2];
        instanceAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 2 };
        instanceAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 16, ShaderLocation = 3 };

        var layouts = stackalloc VertexBufferLayout[2];
        layouts[0] = new VertexBufferLayout { ArrayStride = 24, StepMode = VertexStepMode.Vertex, AttributeCount = 2, Attributes = cubeAttributes };
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(VehicleInstance), StepMode = VertexStepMode.Instance, AttributeCount = 2, Attributes = instanceAttributes };
        _vehiclePipeline = CreateRenderPipeline(_vehicleShader, layouts, 2);

        var (vertices, indices) = UnitCube.Build();
        _cubeVertexBuffer = CreateBuffer(vertices, BufferUsage.Vertex | BufferUsage.CopyDst);
        _cubeIndexBuffer = CreateBuffer(indices, BufferUsage.Index | BufferUsage.CopyDst);
        _cubeVertexBytes = (ulong)(vertices.Length * sizeof(float));
        _cubeIndexCount = (uint)indices.Length;
    }

    private RenderPipeline* CreateRenderPipeline(ShaderModule* shader, VertexBufferLayout* vertexLayouts, int layoutCount)
    {
        var colorTarget = new ColorTargetState { Format = _surfaceFormat, Blend = null, WriteMask = ColorWriteMask.All };
        var keep = new StencilFaceState
        {
            Compare = CompareFunction.Always,
            FailOp = StencilOperation.Keep,
            DepthFailOp = StencilOperation.Keep,
            PassOp = StencilOperation.Keep,
        };
        var depthStencil = new DepthStencilState
        {
            Format = DepthFormat,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.Less,
            StencilFront = keep,
            StencilBack = keep,
        };

        fixed (byte* vertexEntry = "vs_main"u8)
        fixed (byte* fragmentEntry = "fs_main"u8)
        {
            var fragment = new FragmentState
            {
                Module = shader,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState { Module = shader, EntryPoint = vertexEntry, BufferCount = (nuint)layoutCount, Buffers = vertexLayouts },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.Back,
                },
                DepthStencil = &depthStencil,
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            return _wgpu.DeviceCreateRenderPipeline(_device, &descriptor);
        }
    }

    private ShaderModule* CreateShaderModule(string source)
    {
        var code = SilkMarshal.StringToPtr(source);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = (byte*)code,
            };
            var descriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            return _wgpu.DeviceCreateShaderModule(_device, &descriptor);
        }
        finally
        {
            SilkMarshal.Free(code);
        }
    }

    private Buffer* CreateBuffer<T>(T[] data, BufferUsage usage)
        where T : unmanaged
    {
        var size = (ulong)(data.Length * sizeof(T));
        var descriptor = new BufferDescriptor { Usage = usage, Size = size };
        var buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);

        fixed (T* pointer = data)
        {
            _wgpu.QueueWriteBuffer(_queue, buffer, 0, pointer, (nuint)size);
        }

        return buffer;
    }

    private void ReleaseDepthTexture()
    {
        if (_depthView != null)
        {
            _wgpu.TextureViewRelease(_depthView);
            _depthView = null;
        }

        if (_depthTexture != null)
        {
            _wgpu.TextureDestroy(_depthTexture);
            _wgpu.TextureRelease(_depthTexture);
            _depthTexture = null;
        }
    }

    private static string LoadShaderSource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void HandleError(ErrorType type, byte* message, void* userData)
    {
        Console.Error.WriteLine($"[WebGPU {type}] {SilkMarshal.PtrToString((nint)message)}");
    }

    /// <summary>Uniform buffer layout. Must match <c>Uniforms</c> in voxel.wgsl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms
    {
        public Matrix4x4 ViewProjection;
        public Vector4 LightDirection;
        public Vector4 CameraPosition;
        public Vector4 SkyColor;
        public PaletteColors Palette;

        public const int PaletteSize = 8;
    }

    /// <summary>Linear base colour per <see cref="VoxelMaterial"/> value (index 0, air, is unused).</summary>
    [System.Runtime.CompilerServices.InlineArray(Uniforms.PaletteSize)]
    private struct PaletteColors
    {
        private Vector4 _element;
    }
}
