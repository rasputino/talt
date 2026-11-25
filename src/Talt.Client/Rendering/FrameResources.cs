using Silk.NET.WebGPU;

namespace Talt.Client.Rendering;

/// <summary>
/// Bind group 1 of the world pass (feature 018, plan "Bind groups"): the shadow map array with its
/// comparison sampler, the water texture with a linear sampler, and the model palette texture. Each slot
/// has a 1 × 1 placeholder until the real texture exists; the bind group is rebuilt only when a slot
/// changes.
/// </summary>
internal sealed unsafe class FrameResources : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Texture* _dummyShadow;
    private readonly TextureView* _dummyShadowView;
    private readonly Texture* _dummyColor;
    private readonly TextureView* _dummyColorView;
    private readonly Sampler* _comparisonSampler;
    private readonly Sampler* _linearSampler;
    private TextureView* _shadowView;
    private TextureView* _waterView;
    private TextureView* _paletteView;
    private BindGroup* _bindGroup;

    public FrameResources(WebGPU wgpu, Device* device)
    {
        _wgpu = wgpu;
        _device = device;

        var entries = stackalloc BindGroupLayoutEntry[5];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.Dimension2DArray },
        };
        entries[1] = new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Comparison } };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D },
        };
        entries[3] = new BindGroupLayoutEntry { Binding = 3, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D },
        };
        var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 5, Entries = entries };
        Layout = wgpu.DeviceCreateBindGroupLayout(device, &layoutDescriptor);

        _dummyShadow = CreateTexture(1, 1, 1, TextureFormat.Depth32float, TextureUsage.TextureBinding | TextureUsage.RenderAttachment, TextureViewDimension.Dimension2DArray, out _dummyShadowView);
        _dummyColor = CreateTexture(1, 1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, TextureViewDimension.Dimension2D, out _dummyColorView);

        var comparison = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            Compare = CompareFunction.LessEqual,
            MaxAnisotropy = 1,
            LodMaxClamp = 32,
        };
        _comparisonSampler = wgpu.DeviceCreateSampler(device, &comparison);
        var linear = comparison with { Compare = CompareFunction.Undefined };
        _linearSampler = wgpu.DeviceCreateSampler(device, &linear);

        _shadowView = _dummyShadowView;
        _waterView = _dummyColorView;
        _paletteView = _dummyColorView;
    }

    /// <summary>Layout of group 1.</summary>
    public BindGroupLayout* Layout { get; }

    /// <summary>Current bind group (rebuilt after a slot changed).</summary>
    public BindGroup* BindGroup
    {
        get
        {
            if (_bindGroup == null)
            {
                Rebuild();
            }

            return _bindGroup;
        }
    }

    /// <summary>Uses <paramref name="arrayView"/> (2-D array, depth) as shadow map; null for the placeholder.</summary>
    public void SetShadowMap(TextureView* arrayView) => Set(ref _shadowView, arrayView != null ? arrayView : _dummyShadowView);

    /// <summary>Uses <paramref name="view"/> as water texture; null for the placeholder.</summary>
    public void SetWater(TextureView* view) => Set(ref _waterView, view != null ? view : _dummyColorView);

    /// <summary>Uses <paramref name="view"/> as model palette; null for the placeholder.</summary>
    public void SetPalette(TextureView* view) => Set(ref _paletteView, view != null ? view : _dummyColorView);

    public void Dispose()
    {
        if (_bindGroup != null)
        {
            _wgpu.BindGroupRelease(_bindGroup);
        }

        _wgpu.SamplerRelease(_comparisonSampler);
        _wgpu.SamplerRelease(_linearSampler);
        _wgpu.TextureViewRelease(_dummyShadowView);
        _wgpu.TextureRelease(_dummyShadow);
        _wgpu.TextureViewRelease(_dummyColorView);
        _wgpu.TextureRelease(_dummyColor);
        _wgpu.BindGroupLayoutRelease(Layout);
    }

    /// <summary>Creates a texture and a view of it.</summary>
    internal Texture* CreateTexture(uint width, uint height, uint layers, TextureFormat format, TextureUsage usage, TextureViewDimension dimension, out TextureView* view)
    {
        var descriptor = new TextureDescriptor
        {
            Usage = usage,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, layers),
            Format = format,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        var texture = _wgpu.DeviceCreateTexture(_device, &descriptor);
        var viewDescriptor = new TextureViewDescriptor
        {
            Format = format,
            Dimension = dimension,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = layers,
            Aspect = TextureAspect.All,
        };
        view = _wgpu.TextureCreateView(texture, &viewDescriptor);
        return texture;
    }

    private void Set(ref TextureView* slot, TextureView* view)
    {
        if (slot == view)
        {
            return;
        }

        slot = view;
        if (_bindGroup != null)
        {
            _wgpu.BindGroupRelease(_bindGroup); // WebGPU keeps it alive until frames using it finish.
            _bindGroup = null;
        }
    }

    private void Rebuild()
    {
        var entries = stackalloc BindGroupEntry[5];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = _shadowView };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _comparisonSampler };
        entries[2] = new BindGroupEntry { Binding = 2, TextureView = _waterView };
        entries[3] = new BindGroupEntry { Binding = 3, Sampler = _linearSampler };
        entries[4] = new BindGroupEntry { Binding = 4, TextureView = _paletteView };
        var descriptor = new BindGroupDescriptor { Layout = Layout, EntryCount = 5, Entries = entries };
        _bindGroup = _wgpu.DeviceCreateBindGroup(_device, &descriptor);
    }
}
