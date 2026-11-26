using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Talt.Client.Ui;
using Talt.Core.Maps;
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
/// <remarks>
/// Feature 018 frame: when shadows are on, one depth-only pass per shadow cascade (every opaque renderer
/// draws into it with its shadow pipeline and the cascade's uniforms), then the world pass into a
/// multisampled target when anti-aliasing is on (resolved into the surface), then the interface. Bind
/// group 0 holds the uniforms (camera or cascade), group 1 the frame resources (shadow map, water
/// texture, model palettes).
/// </remarks>
internal sealed unsafe class WebGpuRenderer : IDisposable
{
    private const TextureFormat DepthFormat = TextureFormat.Depth32float;

    /// <summary>
    /// Frames the CPU may submit before waiting for the GPU. Without this limit, when vertical sync
    /// does not throttle (window hidden behind others, some drivers) the CPU queues frames and buffer
    /// uploads faster than the GPU finishes them until memory runs out.
    /// </summary>
    private const int MaxFramesInFlight = 2;

    // Native code keeps pointers to these callbacks, so the delegates must stay alive.
    private static readonly ErrorCallback OnError = HandleError;
    private static readonly PfnErrorCallback OnErrorPfn = new(OnError);

    private readonly WebGPU _wgpu;
    private readonly ShadowCascade[] _cascades = new ShadowCascade[ShadowCascades.MaxCascades];
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
    private PipelineLayout* _shadowLayout;
    private WorldPassContext _context = null!;
    private FrameResources _frame = null!;
    private ShadowRenderer _shadows = null!;
    private WorldPipeline _pipeline = null!;
    private Buffer* _uniformBuffer;
    private BindGroup* _bindGroup;

    private ShaderModule* _vehicleShader;
    private WorldPipeline _vehiclePipeline = null!;
    private Buffer* _cubeVertexBuffer;
    private Buffer* _cubeIndexBuffer;
    private ulong _cubeVertexBytes;
    private uint _cubeIndexCount;
    private Buffer* _instanceBuffer;
    private int _instanceCapacity;
    private int _instanceCount;

    private ShaderModule* _industryShader;
    private WorldPipeline _industryPipeline = null!;
    private Buffer* _industryBuffer;
    private int _industryCapacity;
    private int _industryCount;

    private RoadRenderer? _roadRenderer;
    private RoadRenderer? _railRenderer;
    private TownRenderer? _townRenderer;
    private ModelRenderer? _modelRenderer;
    private ModelLibrary? _models;
    private Texture* _depthTexture;
    private TextureView* _depthView;
    private Texture* _msaaTexture;
    private TextureView* _msaaView;
    private int _targetSampleCount;
    private Vector2D<int> _size;
    private readonly ulong[] _submissions = new ulong[MaxFramesInFlight];
    private long _frameNumber;
    private bool _disposed;
    private bool _townsPrepared;

    private GraphicsParameters _graphics = GraphicsPresets.Default.Resolve(GraphicsSettings.For(GraphicsPreset.Medium));
    private LightState _light = LightState.Day;
    private float _seconds;
    private MapData? _map;
    private WaterTexture? _water;
    private Texture* _waterTexture;
    private TextureView* _waterView;

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
        CreatePipeline();
        Resize(window.FramebufferSize);
        CreateVehiclePipeline();
        CreateIndustryPipeline();
        _highlightRenderer = new HighlightRenderer(_context);
        _boxRenderer = new BoxRenderer(_context);
    }

    /// <summary>Whether a world is being drawn.</summary>
    public bool HasWorld => _worldRenderer is not null;

    /// <summary>Milliseconds the last frame spent uploading and encoding the interface.</summary>
    public double LastUiMilliseconds { get; private set; }

    /// <summary>Shadow cascades, texels per side, terrain shadow triangles and shadow GPU bytes of the last frame.</summary>
    public (int Cascades, int Size, long Triangles, long Bytes) ShadowStatistics { get; private set; }

    /// <summary>The loaded voxel models, or null before <see cref="AttachModels"/>.</summary>
    public ModelLibrary? Models => _models;

    /// <summary>Viewport aspect ratio (width / height).</summary>
    public float AspectRatio => _size.Y > 0 ? _size.X / (float)_size.Y : 1f;

    /// <summary>MSAA samples of the last frame.</summary>
    public int SampleCount => _targetSampleCount;

    /// <summary>Starts drawing <paramref name="world"/> (a new game).</summary>
    public void SetWorld(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ClearWorld();
        _worldRenderer = new WorldRenderer(_wgpu, _device, _queue, world);
    }

    /// <summary>
    /// Uses <paramref name="map"/> for the water shader's shoreline and depth texture (feature 018); call after
    /// <see cref="SetWorld"/>.
    /// </summary>
    public void SetMap(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _water = new WaterTexture(map);
        ReleaseWaterTexture();
        TextureView* view;
        _waterTexture = _frame.CreateTexture((uint)_water.Width, (uint)_water.Depth, 1, TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst, TextureViewDimension.Dimension2D, out view);
        _waterView = view;
        UploadWater(0, 0, _water.Width, _water.Depth);
        _frame.SetWater(_waterView);
    }

    /// <summary>Starts drawing the roads of the game (feature 007); call after <see cref="SetWorld"/>.</summary>
    public void SetRoads(Talt.Core.Roads.RoadLayer roads, MapData map, Talt.Core.Roads.RoadRules rules)
    {
        _roadRenderer?.Dispose();
        _roadRenderer = new RoadRenderer(_context, roads, map, rules);
    }

    /// <summary>Starts drawing the track of the game (feature 015); call after <see cref="SetWorld"/>.</summary>
    public void SetRails(Talt.Core.Rails.RailLayer rails, MapData map, Talt.Core.Rails.RailRules rules)
    {
        _railRenderer?.Dispose();
        _railRenderer = new RoadRenderer(_context, new RailMesher(rails, map, rules), map);
    }

    /// <summary>Starts drawing the town buildings of the game (feature 008); call after <see cref="SetWorld"/>.</summary>
    public void SetTowns(Talt.Core.Towns.TownRegistry towns, MapData map)
    {
        _townRenderer?.Dispose();
        _townRenderer = new TownRenderer(_context, towns, map);
        _townRenderer.UseModels(_models);
    }

    /// <summary>Uploads the voxel models (feature 018); call once after creating the renderer.</summary>
    public void AttachModels(ModelLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _modelRenderer?.Dispose();
        _models = library;
        _modelRenderer = new ModelRenderer(_context, library, _frame);
        _townRenderer?.UseModels(library);
    }

    /// <summary>
    /// Culls the town areas for this frame's camera ahead of <see cref="Draw"/>, so the buildings drawn as models
    /// can be added to the model batch first.
    /// </summary>
    public void PrepareTowns(OrbitCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_townRenderer is null || _worldRenderer is null || _size.X <= 0 || _size.Y <= 0)
        {
            return;
        }

        _townRenderer.Prepare(camera.ViewProjection(_size.X / (float)_size.Y), camera.Position, camera.Distance * 2.5f);
        _townsPrepared = true;
    }

    /// <summary>Adds the town buildings that have models to <paramref name="batch"/> (after <see cref="PrepareTowns"/>).</summary>
    public void AppendTownModels(ModelBatch batch) => _townRenderer?.AppendModels(batch);

    /// <summary>Uploads the frame's model instances (call once per drawn frame, before <see cref="Draw"/>).</summary>
    public void UpdateModels(ModelBatch batch)
    {
        if (!_disposed && _size.X > 0 && _size.Y > 0)
        {
            _modelRenderer?.Upload(batch);
        }
    }

    /// <summary>Model draw calls, instances, triangles and GPU bytes of the last frame.</summary>
    public (int Draws, int Instances, long Triangles, long Bytes) ModelStatistics =>
        _modelRenderer is null ? default : (_modelRenderer.Draws, _modelRenderer.Instances, _modelRenderer.Triangles, _modelRenderer.GpuBytes);

    /// <summary>Uses these graphics settings from the next frame (feature 018); cheap, call every frame.</summary>
    public void SetGraphics(in GraphicsParameters graphics) => _graphics = graphics;

    /// <summary>The light of the next frame (fixed daylight or the day/night cycle).</summary>
    public void SetLight(in LightState light) => _light = light;

    /// <summary>Real time in seconds for animations that run even while paused (water).</summary>
    public void SetTime(double seconds) => _seconds = (float)(seconds % 3600.0);

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
        _railRenderer?.Dispose();
        _railRenderer = null;
        _townRenderer?.Dispose();
        _townRenderer = null;
        _instanceCount = 0;
        _industryCount = 0;
        _highlightRenderer?.Update([], Vector3.Zero);
        _boxRenderer?.Clear();
        _frame.SetWater(null);
        ReleaseWaterTexture();
        _water = null;
        _map = null;
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
    public int NotifyVoxelsChanged(IReadOnlyList<(int X, int Y, int Z)> voxels, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        if (_water is not null && voxels.Count > 0)
        {
            // Terraforming may move shores: rebuild the water texture around the edited tiles.
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            foreach (var (x, _, z) in voxels)
            {
                (x0, z0, x1, z1) = (Math.Min(x0, x), Math.Min(z0, z), Math.Max(x1, x + 1), Math.Max(z1, z + 1));
            }

            var (tx, tz, tw, td) = _water.RebuildTiles(x0, z0, x1, z1);
            UploadWater(tx, tz, tw, td);
        }

        return _worldRenderer?.NotifyVoxelsChanged(voxels, timestamp) ?? 0;
    }

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
        CreateTargets(_context.SampleCount);
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

        if (_graphics.SampleCount != _context.SampleCount)
        {
            _context.SampleCount = _graphics.SampleCount; // Pipelines for the new count are created on first use.
            CreateTargets(_context.SampleCount);
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

        var world = _worldRenderer;
        var aspect = _size.X / (float)_size.Y;
        var viewProjection = camera.ViewProjection(aspect);
        var fogDistance = camera.Distance * 2.5f;
        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var cascadeCount = world is null ? 0 : _graphics.ShadowCascades;
        var shadowDistance = MathF.Min(_graphics.ShadowDistance, fogDistance * 1.5f);
        if (cascadeCount > 0)
        {
            if (_shadows.Ensure(_graphics.ShadowMapSize, cascadeCount))
            {
                _frame.SetShadowMap(_shadows.ArrayView);
            }

            ShadowCascades.Compute(camera.Position, forward, camera.FieldOfView, aspect, camera.NearPlane, shadowDistance,
                _light.SunDirection, _graphics.ShadowMapSize, _cascades.AsSpan(0, cascadeCount));
        }
        else if (_shadows.Size != 0)
        {
            _frame.SetShadowMap(null);
            _shadows.Release();
        }

        var uniforms = new Uniforms
        {
            ViewProjection = viewProjection,
            SunDirection = new Vector4(_light.SunDirection, _light.Night),
            SunColor = new Vector4(_light.SunColor, _light.Exposure),
            SkyAmbient = new Vector4(_light.SkyAmbient, _graphics.AmbientOcclusion),
            GroundAmbient = new Vector4(_light.GroundAmbient, _seconds),
            CameraPosition = new Vector4(camera.Position, fogDistance),
            CameraForward = new Vector4(forward, _graphics.WaterEffects ? 1f : 0f),
            SkyColor = new Vector4(_light.SkyColor, 0f),
            Shadow = new Vector4(cascadeCount, shadowDistance, 0f, _graphics.ShadowTaps),
            CascadeSplits = new Vector4(
                _cascades[0].FarDepth, _cascades[1].FarDepth, _cascades[2].FarDepth,
                _map is null ? 0f : 1f / _map.Width),
            CascadeTexel = new Vector4(
                _cascades[0].TexelSize, _cascades[1].TexelSize, _cascades[2].TexelSize,
                _map is null ? 0f : 1f / _map.Depth),
        };
        for (var c = 0; c < ShadowCascades.MaxCascades; c++)
        {
            uniforms.Cascades[c] = _cascades[c].ViewProjection;
        }

        for (var m = 0; m < Uniforms.PaletteSize; m++)
        {
            uniforms.Palette[m] = new Vector4(MaterialPalette.BaseColor((VoxelMaterial)m), 1f);
        }

        _wgpu.QueueWriteBuffer(_queue, _uniformBuffer, 0, &uniforms, (nuint)sizeof(Uniforms));
        for (var c = 0; c < cascadeCount; c++)
        {
            var cascadeUniforms = uniforms;
            cascadeUniforms.ViewProjection = _cascades[c].ViewProjection;
            _shadows.WriteUniforms(_queue, c, &cascadeUniforms);
        }

        var colorView = _wgpu.TextureCreateView(surfaceTexture.Texture, null);
        var encoder = _wgpu.DeviceCreateCommandEncoder(_device, null);
        world?.Prepare(encoder, viewProjection, camera.Position, fogDistance, _size.Y, camera.FieldOfView);
        if (world is not null)
        {
            _roadRenderer?.Prepare(viewProjection, camera.Position, fogDistance);
            _railRenderer?.Prepare(viewProjection, camera.Position, fogDistance);
            if (!_townsPrepared)
            {
                _townRenderer?.Prepare(viewProjection, camera.Position, fogDistance);
            }

            _townsPrepared = false;
            for (var c = 0; c < cascadeCount; c++)
            {
                world.PrepareShadow(c, _cascades[c].ViewProjection);
            }
        }

        var sky = Lighting.ToneMap(_light.SkyColor, _light.Exposure);
        var clear = world is not null ? sky : background;
        var prepared = System.Diagnostics.Stopwatch.GetTimestamp();

        if (world is not null)
        {
            for (var c = 0; c < cascadeCount; c++)
            {
                EncodeShadowPass(encoder, world, c);
            }
        }

        ShadowStatistics = (cascadeCount, cascadeCount > 0 ? _shadows.Size : 0, cascadeCount > 0 ? world!.ShadowTriangles : 0, _shadows.GpuBytes);

        var multisampled = _targetSampleCount > 1;
        var colorAttachment = new RenderPassColorAttachment
        {
            View = multisampled ? _msaaView : colorView,
            ResolveTarget = multisampled ? colorView : null,
            LoadOp = LoadOp.Clear,
            StoreOp = multisampled ? StoreOp.Discard : StoreOp.Store,
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
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 1, _frame.BindGroup, 0, null);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline.Main);

        world?.Encode(pass);
        if (world is not null)
        {
            _roadRenderer?.Encode(pass); // Roads on top of the terrain (feature 007).
            _railRenderer?.Encode(pass); // Track, after roads so level crossings show the rails (feature 015).
            _townRenderer?.Encode(pass); // Town buildings (feature 008).
            _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            _modelRenderer?.Encode(pass); // Voxel models (feature 018).
        }

        if (world is not null)
        {
            EncodeVehicles(pass, _vehiclePipeline.Main);
            EncodeIndustries(pass, _industryPipeline.Main);
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
        _railRenderer?.Dispose();
        _roadRenderer?.Dispose();
        _townRenderer?.Dispose();
        _modelRenderer?.Dispose();
        _uiRenderer?.Dispose();
        _highlightRenderer?.Dispose();
        _boxRenderer?.Dispose();
        ReleaseTargets();
        ReleaseWaterTexture();
        _shadows.Dispose();

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
        _vehiclePipeline.Dispose();
        _wgpu.ShaderModuleRelease(_vehicleShader);
        _industryPipeline.Dispose();
        _wgpu.ShaderModuleRelease(_industryShader);

        _wgpu.BindGroupRelease(_bindGroup);
        _wgpu.BufferRelease(_uniformBuffer);
        _pipeline.Dispose();
        _wgpu.PipelineLayoutRelease(_pipelineLayout);
        _wgpu.PipelineLayoutRelease(_shadowLayout);
        _frame.Dispose();
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

    /// <summary>Depth-only pass of one shadow cascade: every opaque renderer with its shadow pipeline.</summary>
    private void EncodeShadowPass(CommandEncoder* encoder, WorldRenderer world, int cascade)
    {
        var pass = _shadows.BeginPass(encoder, cascade);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline.Shadow);
        world.EncodeShadow(pass, cascade);
        _roadRenderer?.EncodeShadow(pass);
        _railRenderer?.EncodeShadow(pass);
        _townRenderer?.EncodeShadow(pass);
        if (cascade < 2)
        {
            // Model shadows in the third (farthest) cascade are a few texels wide: terrain only there.
            _modelRenderer?.EncodeShadow(pass);
        }
        EncodeVehicles(pass, _vehiclePipeline.Shadow);
        EncodeIndustries(pass, _industryPipeline.Shadow);
        _boxRenderer?.EncodeShadow(pass);
        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);
    }

    private void EncodeVehicles(RenderPassEncoder* pass, RenderPipeline* pipeline)
    {
        if (_instanceCount == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertexBuffer, 0, _cubeVertexBytes);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _instanceBuffer, 0, (ulong)(_instanceCount * sizeof(VehicleInstance)));
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndexBuffer, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
        _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)_instanceCount, 0, 0, 0);
    }

    private void EncodeIndustries(RenderPassEncoder* pass, RenderPipeline* pipeline)
    {
        if (_industryCount == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVertexBuffer, 0, _cubeVertexBytes);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _industryBuffer, 0, (ulong)(_industryCount * sizeof(IndustryInstance)));
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeIndexBuffer, IndexFormat.Uint16, 0, _cubeIndexCount * sizeof(ushort));
        _wgpu.RenderPassEncoderDrawIndexed(pass, _cubeIndexCount, (uint)_industryCount, 0, 0, 0);
    }

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
        _frame = new FrameResources(_wgpu, _device);

        // World pass: group 0 (uniforms) and group 1 (frame resources). Shadow pass: group 0 only.
        var groups = stackalloc BindGroupLayout*[2];
        groups[0] = _bindGroupLayout;
        groups[1] = _frame.Layout;
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 2, BindGroupLayouts = groups };
        _pipelineLayout = _wgpu.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
        var shadowLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = groups };
        _shadowLayout = _wgpu.DeviceCreatePipelineLayout(_device, &shadowLayoutDescriptor);

        _context = new WorldPassContext
        {
            Wgpu = _wgpu,
            Device = _device,
            Queue = _queue,
            MainLayout = _pipelineLayout,
            ShadowLayout = _shadowLayout,
            SurfaceFormat = _surfaceFormat,
            DepthFormat = DepthFormat,
            SampleCount = _graphics.SampleCount,
        };
        _shadows = new ShadowRenderer(_wgpu, _device, _bindGroupLayout, (ulong)sizeof(Uniforms));

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
        _pipeline = new WorldPipeline(_context, new PipelineSpec
        {
            Shader = _shader,
            Buffers =
            [
                new VertexBufferSpec(PackedVertex.SizeInBytes, VertexStepMode.Vertex,
                    [new VertexAttribute { Format = VertexFormat.Uint32x2, Offset = 0, ShaderLocation = 0 }]),
                new VertexBufferSpec((ulong)sizeof(Vector4), VertexStepMode.Instance,
                    [new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 1 }]),
            ],
        });
    }

    /// <summary>Pipeline for vehicles: a unit cube drawn once per vehicle (instancing).</summary>
    private void CreateVehiclePipeline()
    {
        _vehicleShader = CreateShaderModule(LoadShaderSource("vehicles.wgsl"));
        _vehiclePipeline = new WorldPipeline(_context, PipelineSpecs.InstancedCube(_vehicleShader, (ulong)sizeof(VehicleInstance), 2));

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
        _industryPipeline = new WorldPipeline(_context, PipelineSpecs.InstancedCube(_industryShader, (ulong)sizeof(IndustryInstance), 3));
    }

    /// <summary>Depth target (and the multisampled colour target when MSAA is on) for the window size.</summary>
    private void CreateTargets(int sampleCount)
    {
        ReleaseTargets();
        if (_size.X <= 0 || _size.Y <= 0)
        {
            return;
        }

        var depthDescriptor = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)_size.X, (uint)_size.Y, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = (uint)sampleCount,
        };
        _depthTexture = _wgpu.DeviceCreateTexture(_device, &depthDescriptor);
        _depthView = _wgpu.TextureCreateView(_depthTexture, null);
        if (sampleCount > 1)
        {
            var colorDescriptor = depthDescriptor with { Format = _surfaceFormat };
            _msaaTexture = _wgpu.DeviceCreateTexture(_device, &colorDescriptor);
            _msaaView = _wgpu.TextureCreateView(_msaaTexture, null);
        }

        _targetSampleCount = sampleCount;
    }

    private ShaderModule* CreateShaderModule(string source) => WebGpuShaders.CreateModule(_wgpu, _device, source);

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

    private void UploadWater(int x, int z, int width, int depth)
    {
        if (_water is null || _waterTexture == null || width <= 0 || depth <= 0)
        {
            return;
        }

        var destination = new ImageCopyTexture
        {
            Texture = _waterTexture,
            MipLevel = 0,
            Origin = new Origin3D((uint)x, (uint)z, 0),
            Aspect = TextureAspect.All,
        };
        var rowBytes = (uint)(_water.Width * WaterTexture.BytesPerTexel);
        var layout = new TextureDataLayout { Offset = (ulong)((x + (z * _water.Width)) * WaterTexture.BytesPerTexel), BytesPerRow = rowBytes, RowsPerImage = (uint)depth };
        var extent = new Extent3D((uint)width, (uint)depth, 1);
        fixed (byte* texels = _water.Texels)
        {
            _wgpu.QueueWriteTexture(_queue, &destination, texels, (nuint)_water.Texels.Length, &layout, &extent);
        }
    }

    private void ReleaseWaterTexture()
    {
        if (_waterView != null)
        {
            _wgpu.TextureViewRelease(_waterView);
            _waterView = null;
        }

        if (_waterTexture != null)
        {
            _wgpu.TextureRelease(_waterTexture);
            _waterTexture = null;
        }
    }

    private void ReleaseTargets()
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

        if (_msaaView != null)
        {
            _wgpu.TextureViewRelease(_msaaView);
            _msaaView = null;
        }

        if (_msaaTexture != null)
        {
            _wgpu.TextureDestroy(_msaaTexture);
            _wgpu.TextureRelease(_msaaTexture);
            _msaaTexture = null;
        }
    }

    private static string LoadShaderSource(string fileName) => WebGpuShaders.Load(fileName);

    private static void HandleError(ErrorType type, byte* message, void* userData)
    {
        Console.Error.WriteLine($"[WebGPU {type}] {SilkMarshal.PtrToString((nint)message)}");
    }

    /// <summary>Uniform buffer layout. Must match <c>Uniforms</c> in lighting.wgsl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms
    {
        public Matrix4x4 ViewProjection;
        public Vector4 SunDirection;
        public Vector4 SunColor;
        public Vector4 SkyAmbient;
        public Vector4 GroundAmbient;
        public Vector4 CameraPosition;
        public Vector4 CameraForward;
        public Vector4 SkyColor;
        public Vector4 Shadow;
        public Vector4 CascadeSplits;
        public Vector4 CascadeTexel;
        public CascadeMatrices Cascades;
        public PaletteColors Palette;

        public const int PaletteSize = 8;
    }

    [System.Runtime.CompilerServices.InlineArray(ShadowCascades.MaxCascades)]
    private struct CascadeMatrices
    {
        private Matrix4x4 _element;
    }

    /// <summary>Linear base colour per <see cref="VoxelMaterial"/> value (index 0, air, is unused).</summary>
    [System.Runtime.CompilerServices.InlineArray(Uniforms.PaletteSize)]
    private struct PaletteColors
    {
        private Vector4 _element;
    }
}
