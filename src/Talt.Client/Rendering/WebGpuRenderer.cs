using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Talt.Core.Meshing;
using Buffer = Silk.NET.WebGPU.Buffer;
using Color = Silk.NET.WebGPU.Color;

namespace Talt.Client.Rendering;

/// <summary>
/// Minimal WebGPU renderer for voxel chunk meshes. WebGPU maps to Vulkan, Metal or DirectX 12
/// depending on the platform.
/// </summary>
internal sealed unsafe class WebGpuRenderer : IDisposable
{
    private const TextureFormat DepthFormat = TextureFormat.Depth32float;

    private static readonly Vector3 SkyColor = new(0.55f, 0.75f, 0.95f);

    // Native code keeps pointers to these callbacks, so the delegates must stay alive.
    private static readonly ErrorCallback OnError = HandleError;
    private static readonly PfnErrorCallback OnErrorPfn = new(OnError);

    private readonly WebGPU _wgpu;
    private readonly List<GpuMesh> _meshes = [];

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

    private Texture* _depthTexture;
    private TextureView* _depthView;
    private Vector2D<int> _size;
    private bool _disposed;

    public WebGpuRenderer(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

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
    }

    public string AdapterDescription { get; private set; } = "unknown";

    public int TriangleCount { get; private set; }

    public void UploadMeshes(IEnumerable<ChunkMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);

        foreach (var mesh in meshes)
        {
            if (mesh.IsEmpty)
            {
                continue;
            }

            var vertexBuffer = CreateBuffer<VoxelVertex>(mesh.Vertices, BufferUsage.Vertex | BufferUsage.CopyDst);
            var indexBuffer = CreateBuffer<uint>(mesh.Indices, BufferUsage.Index | BufferUsage.CopyDst);
            _meshes.Add(new GpuMesh(vertexBuffer, indexBuffer, (ulong)(mesh.Vertices.Length * VoxelVertex.SizeInBytes), (uint)mesh.Indices.Length));
            TriangleCount += mesh.Indices.Length / 3;
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

        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            if (surfaceTexture.Texture != null)
            {
                _wgpu.TextureRelease(surfaceTexture.Texture);
            }

            Resize(_size); // Outdated or lost: reconfigure and try again next frame.
            return;
        }

        var uniforms = new Uniforms
        {
            ViewProjection = camera.ViewProjection(_size.X / (float)_size.Y),
            LightDirection = new Vector4(Vector3.Normalize(new Vector3(-0.45f, -1f, -0.3f)), 0f),
            CameraPosition = new Vector4(camera.Position, camera.Distance * 2.5f),
            SkyColor = new Vector4(SkyColor, 1f),
        };
        _wgpu.QueueWriteBuffer(_queue, _uniformBuffer, 0, &uniforms, (nuint)sizeof(Uniforms));

        var colorView = _wgpu.TextureCreateView(surfaceTexture.Texture, null);
        var encoder = _wgpu.DeviceCreateCommandEncoder(_device, null);

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

        foreach (var mesh in _meshes)
        {
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, mesh.VertexBuffer, 0, mesh.VertexBytes);
            _wgpu.RenderPassEncoderSetIndexBuffer(pass, mesh.IndexBuffer, IndexFormat.Uint32, 0, mesh.IndexCount * sizeof(uint));
            _wgpu.RenderPassEncoderDrawIndexed(pass, mesh.IndexCount, 1, 0, 0, 0);
        }

        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);

        var commands = _wgpu.CommandEncoderFinish(encoder, null);
        _wgpu.QueueSubmit(_queue, 1, &commands);
        _wgpu.SurfacePresent(_surface);

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
        foreach (var mesh in _meshes)
        {
            _wgpu.BufferRelease(mesh.VertexBuffer);
            _wgpu.BufferRelease(mesh.IndexBuffer);
        }

        _meshes.Clear();
        ReleaseDepthTexture();

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
        _wgpu.Dispose();
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

        var attributes = stackalloc VertexAttribute[3];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 24, ShaderLocation = 2 };
        var vertexLayout = new VertexBufferLayout
        {
            ArrayStride = VoxelVertex.SizeInBytes,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 3,
            Attributes = attributes,
        };

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
                Module = _shader,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &vertexLayout },
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
            _pipeline = _wgpu.DeviceCreateRenderPipeline(_device, &descriptor);
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
    }

    private readonly struct GpuMesh(Buffer* vertexBuffer, Buffer* indexBuffer, ulong vertexBytes, uint indexCount)
    {
        public Buffer* VertexBuffer { get; } = vertexBuffer;

        public Buffer* IndexBuffer { get; } = indexBuffer;

        public ulong VertexBytes { get; } = vertexBytes;

        public uint IndexCount { get; } = indexCount;
    }
}
