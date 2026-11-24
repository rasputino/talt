using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Talt.Client.Ui;
using Talt.Core.Meshing;
using Talt.Core.Voxels;
using Buffer = Silk.NET.WebGPU.Buffer;
using Color = Silk.NET.WebGPU.Color;

namespace Talt.Client.Rendering;

/// <summary>
/// WebGPU renderer: owns the device, the pipelines and the frame; the voxel world is drawn by
/// <see cref="WorldRenderer"/>, vehicles by instancing, highlight boxes by <see cref="HighlightRenderer"/>
/// and the interface on top by <see cref="UiRenderer"/>. The world is optional: menus draw only the
/// interface. WebGPU maps to Vulkan, Metal or DirectX 12 depending on the platform.
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
    private UiRenderer? _uiRenderer;
    private HighlightRenderer? _highlightRenderer;
    private BoxRenderer? _boxRenderer;
    private PresentMode _presentMode = PresentMode.Fifo;
    private PresentMode _uncappedMode = PresentMode.Fifo;

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

    private ShaderModule* _industryShader;
    private RenderPipeline* _industryPipeline;
    private Buffer* _industryBuffer;
    private int _industryCapacity;
    private int _industryCount;

    private RoadRenderer? _roadRenderer;
    private TownRenderer? _townRenderer;
    private Texture* _depthTexture;
    private TextureView* _depthView;
    private Vector2D<int> _size;
    private readonly ulong[] _submissions = new ulong[MaxFramesInFlight];
    private long _frameNumber;
    private bool _disposed;

    public WebGpuRenderer(IWindow window, bool verticalSync = true)
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
        _presentMode = verticalSync ? PresentMode.Fifo : _uncappedMode;
        Resize(window.FramebufferSize);
        CreatePipeline();
        CreateVehiclePipeline();
        CreateIndustryPipeline();
        _highlightRenderer = new HighlightRenderer(_wgpu, _device, _queue, _pipelineLayout, _surfaceFormat, DepthFormat);
        _boxRenderer = new BoxRenderer(_wgpu, _device, _queue, _pipelineLayout, _surfaceFormat, DepthFormat);
    }

    /// <summary>Whether a world is being drawn.</summary>
    public bool HasWorld => _worldRenderer is not null;

    /// <summary>Milliseconds the last frame spent uploading and encoding the interface.</summary>
    public double LastUiMilliseconds { get; private set; }

    /// <summary>Starts drawing <paramref name="world"/> (a new game).</summary>
    public void SetWorld(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ClearWorld();
        _worldRenderer = new WorldRenderer(_wgpu, _device, _queue, world);
    }

    /// <summary>Starts drawing the roads of the game (feature 007); call after <see cref="SetWorld"/>.</summary>
    public void SetRoads(Talt.Core.Roads.RoadLayer roads, Talt.Core.Maps.MapData map, Talt.Core.Roads.RoadRules rules)
    {
        _roadRenderer?.Dispose();
        _roadRenderer = new RoadRenderer(_wgpu, _device, _queue, _pipelineLayout, _surfaceFormat, DepthFormat, roads, map, rules);
    }

    /// <summary>Starts drawing the town buildings of the game (feature 008); call after <see cref="SetWorld"/>.</summary>
    public void SetTowns(Talt.Core.Towns.TownRegistry towns, Talt.Core.Maps.MapData map)
    {
        _townRenderer?.Dispose();
        _townRenderer = new TownRenderer(_wgpu, _device, _queue, _pipelineLayout, _surfaceFormat, DepthFormat, towns, map);
    }

    /// <summary>Building draw calls, boxes and GPU bytes of the last frame.</summary>
    public (int Draws, long Instances, long Bytes) TownStatistics =>
        _townRenderer is null ? default : (_townRenderer.Draws, _townRenderer.Instances, _townRenderer.GpuBytes);

    /// <summary>Road draw calls, triangles and GPU bytes of the last frame.</summary>
    public (int Draws, long Triangles, long Bytes) RoadStatistics =>
        _roadRenderer is null ? default : (_roadRenderer.Draws, _roadRenderer.Triangles, _roadRenderer.GpuBytes);

    /// <summary>Stops drawing the world and releases its GPU memory (back to the main menu).</summary>
    public void ClearWorld()
    {
        if (_worldRenderer is null)
        {
            return;
        }

        _ = NativeWgpu.DevicePoll(_device, 1, null); // Let the GPU finish frames that use the world.
        _worldRenderer.Dispose();
        _worldRenderer = null;
        _roadRenderer?.Dispose();
        _roadRenderer = null;
        _townRenderer?.Dispose();
        _townRenderer = null;
        _instanceCount = 0;
        _industryCount = 0;
        _highlightRenderer?.Update([], Vector3.Zero);
        _boxRenderer?.Clear();
    }

    /// <summary>Replaces the trees drawn near the camera (feature 006). Call only when they changed.</summary>
    public void UpdateTrees(ReadOnlySpan<BoxInstance> boxes)
    {
        if (!_disposed && _worldRenderer is not null)
        {
            _boxRenderer?.UpdateOpaque(boxes);
        }
    }

    /// <summary>Replaces the construction preview boxes (feature 006). Call only when they changed.</summary>
    public void UpdatePreview(ReadOnlySpan<BoxInstance> boxes)
    {
        if (!_disposed && _worldRenderer is not null)
        {
            _boxRenderer?.UpdateTranslucent(boxes);
        }
    }

    /// <summary>Creates the interface pipeline for <paramref name="atlas"/>.</summary>
    public void AttachUi(UiAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        _uiRenderer?.Dispose();
        _uiRenderer = new UiRenderer(_wgpu, _device, _queue, _surfaceFormat, atlas, WebGpuShaders.Load("ui.wgsl"));
    }

    /// <summary>Vertical sync on (present at the display refresh) or off (when the surface supports it).</summary>
    public void SetVerticalSync(bool enabled)
    {
        var mode = enabled ? PresentMode.Fifo : _uncappedMode;
        if (mode != _presentMode)
        {
            _presentMode = mode;
            Resize(_size);
        }
    }

    /// <summary>Boxes outlined in the world in the next frames.</summary>
    public void SetHighlights(ReadOnlySpan<HighlightBox> boxes, Vector3 cameraPosition) =>
        _highlightRenderer?.Update(_worldRenderer is null ? [] : boxes, cameraPosition);

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

    /// <summary>Replaces the industry boxes drawn in the next frames. Grows the GPU buffer when needed.</summary>
    public void UpdateIndustries(ReadOnlySpan<IndustryInstance> instances)
    {
        if (_disposed || _size.X <= 0 || _size.Y <= 0)
        {
            return;
        }

        if (instances.Length > _industryCapacity)
        {
            if (_industryBuffer != null)
            {
                _wgpu.BufferRelease(_industryBuffer);
            }

            _industryCapacity = Math.Max(instances.Length, _industryCapacity * 2);
            var descriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Size = (ulong)(_industryCapacity * sizeof(IndustryInstance)),
            };
            _industryBuffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        }

        _industryCount = instances.Length;
        if (_industryCount > 0)
        {
            fixed (IndustryInstance* data = instances)
            {
                _wgpu.QueueWriteBuffer(_queue, _industryBuffer, 0, data, (nuint)(_industryCount * sizeof(IndustryInstance)));
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
            PresentMode = _presentMode,
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

    /// <summary>
    /// Draws a frame: the world seen from <paramref name="camera"/> when a world is set (otherwise the
    /// <paramref name="background"/> colour), then the interface.
    /// </summary>
    public void Draw(OrbitCamera camera, UiDrawList? ui, Vector3 background)
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
        var world = _worldRenderer;
        world?.Prepare(encoder, viewProjection, camera.Position, fogDistance, _size.Y, camera.FieldOfView);
        if (world is not null)
        {
            _roadRenderer?.Prepare(viewProjection, camera.Position, fogDistance);
            _townRenderer?.Prepare(viewProjection, camera.Position, fogDistance);
        }
        var clear = world is not null ? SkyColor : background;
        var prepared = System.Diagnostics.Stopwatch.GetTimestamp();

        var colorAttachment = new RenderPassColorAttachment
        {
            View = colorView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(clear.X, clear.Y, clear.Z, 1),
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

        world?.Encode(pass);
        if (world is not null)
        {
            _roadRenderer?.Encode(pass); // Roads on top of the terrain (feature 007).
            _townRenderer?.Encode(pass); // Town buildings (feature 008).
            _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        }

        if (world is not null && _instanceCount > 0)
        {
            _wgpu.RenderPassEncoderSetPipeline(pass, _vehiclePipeline);
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertexBuffer, 0, _cubeVertexBytes);
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _instanceBuffer, 0, (ulong)(_instanceCount * sizeof(VehicleInstance)));
            _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndexBuffer, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
            _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)_instanceCount, 0, 0, 0);
        }

        if (world is not null && _industryCount > 0)
        {
            _wgpu.RenderPassEncoderSetPipeline(pass, _industryPipeline);
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertexBuffer, 0, _cubeVertexBytes);
            _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _industryBuffer, 0, (ulong)(_industryCount * sizeof(IndustryInstance)));
            _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndexBuffer, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
            _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)_industryCount, 0, 0, 0);
        }

        if (world is not null)
        {
            _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            _boxRenderer?.EncodeOpaque(pass);
            _highlightRenderer?.Encode(pass);
            _boxRenderer?.EncodeTranslucent(pass); // Last: blended over everything drawn before.
        }

        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);

        var uiStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (ui is not null && _uiRenderer is not null && ui.QuadCount > 0)
        {
            _uiRenderer.Upload(ui, _size.X, _size.Y);
            var uiColor = new RenderPassColorAttachment
            {
                View = colorView,
                ResolveTarget = null,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store,
                DepthSlice = uint.MaxValue,
            };
            var uiPassDescriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &uiColor };
            var uiPass = _wgpu.CommandEncoderBeginRenderPass(encoder, &uiPassDescriptor);
            _uiRenderer.Encode(uiPass, ui, _size.X, _size.Y);
            _wgpu.RenderPassEncoderEnd(uiPass);
            _wgpu.RenderPassEncoderRelease(uiPass);
        }

        LastUiMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(uiStart).TotalMilliseconds;
        var commands = _wgpu.CommandEncoderFinish(encoder, null);
        var encoded = System.Diagnostics.Stopwatch.GetTimestamp();
        WaitForFrameSlot();
        var waited = System.Diagnostics.Stopwatch.GetTimestamp();
        _submissions[_frameNumber % MaxFramesInFlight] = NativeWgpu.QueueSubmitForIndex(_queue, 1, &commands);
        _frameNumber++;
        world?.AfterSubmit();
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
        _uiRenderer?.Dispose();
        _highlightRenderer?.Dispose();
        _boxRenderer?.Dispose();
        ReleaseDepthTexture();

        if (_instanceBuffer != null)
        {
            _wgpu.BufferRelease(_instanceBuffer);
        }

        if (_industryBuffer != null)
        {
            _wgpu.BufferRelease(_industryBuffer);
        }

        _wgpu.BufferRelease(_cubeVertexBuffer);
        _wgpu.BufferRelease(_cubeIndexBuffer);
        _wgpu.RenderPipelineRelease(_vehiclePipeline);
        _wgpu.ShaderModuleRelease(_vehicleShader);
        _wgpu.RenderPipelineRelease(_industryPipeline);
        _wgpu.ShaderModuleRelease(_industryShader);

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

        // Vertical sync off uses Mailbox (no tearing) or Immediate when available; Fifo is always supported.
        var presentModes = new ReadOnlySpan<PresentMode>(capabilities.PresentModes, (int)capabilities.PresentModeCount);
        _uncappedMode = presentModes.Contains(PresentMode.Mailbox) ? PresentMode.Mailbox
            : presentModes.Contains(PresentMode.Immediate) ? PresentMode.Immediate
            : PresentMode.Fifo;

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

    /// <summary>Pipeline for industries: a unit cube per model box, with per-instance extent and colour.</summary>
    private void CreateIndustryPipeline()
    {
        _industryShader = CreateShaderModule(LoadShaderSource("industries.wgsl"));

        var cubeAttributes = stackalloc VertexAttribute[2];
        cubeAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        cubeAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        var instanceAttributes = stackalloc VertexAttribute[3];
        instanceAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 2 };
        instanceAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 16, ShaderLocation = 3 };
        instanceAttributes[2] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 32, ShaderLocation = 4 };

        var layouts = stackalloc VertexBufferLayout[2];
        layouts[0] = new VertexBufferLayout { ArrayStride = 24, StepMode = VertexStepMode.Vertex, AttributeCount = 2, Attributes = cubeAttributes };
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(IndustryInstance), StepMode = VertexStepMode.Instance, AttributeCount = 3, Attributes = instanceAttributes };
        _industryPipeline = CreateRenderPipeline(_industryShader, layouts, 2);
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

    private ShaderModule* CreateShaderModule(string source) => WebGpuShaders.CreateModule(_wgpu, _device, source);

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

    private static string LoadShaderSource(string fileName) => WebGpuShaders.Load(fileName);

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
