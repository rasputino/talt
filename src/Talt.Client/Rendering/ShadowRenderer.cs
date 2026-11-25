using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>
/// The sun's cascaded shadow map (feature 018, research R1): a depth texture array with one layer per
/// cascade, a view per layer to render into, an array view to sample, and one uniform buffer and bind
/// group per cascade, so every renderer's vertex shader draws into the shadow map unchanged (its
/// <c>view_proj</c> is the cascade's light matrix). Recreated when the quality changes.
/// </summary>
internal sealed unsafe class ShadowRenderer : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly ulong _uniformSize;
    private readonly Buffer*[] _uniforms = new Buffer*[ShadowCascades.MaxCascades];
    private readonly BindGroup*[] _bindGroups = new BindGroup*[ShadowCascades.MaxCascades];
    private readonly TextureView*[] _layerViews = new TextureView*[ShadowCascades.MaxCascades];
    private Texture* _texture;
    private TextureView* _arrayView;

    public ShadowRenderer(WebGPU wgpu, Device* device, BindGroupLayout* uniformLayout, ulong uniformSize)
    {
        _wgpu = wgpu;
        _device = device;
        _uniformSize = uniformSize;
        for (var i = 0; i < ShadowCascades.MaxCascades; i++)
        {
            var descriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = uniformSize };
            _uniforms[i] = wgpu.DeviceCreateBuffer(device, &descriptor);
            var entry = new BindGroupEntry { Binding = 0, Buffer = _uniforms[i], Offset = 0, Size = uniformSize };
            var groupDescriptor = new BindGroupDescriptor { Layout = uniformLayout, EntryCount = 1, Entries = &entry };
            _bindGroups[i] = wgpu.DeviceCreateBindGroup(device, &groupDescriptor);
        }
    }

    /// <summary>Texels per side of each cascade (0 before the first <see cref="Ensure"/>).</summary>
    public int Size { get; private set; }

    /// <summary>Cascades in the texture.</summary>
    public int Cascades { get; private set; }

    /// <summary>Array view for sampling, or null when there is no texture.</summary>
    public TextureView* ArrayView => _arrayView;

    /// <summary>GPU bytes of the shadow texture.</summary>
    public long GpuBytes => (long)Size * Size * Cascades * 4;

    /// <summary>Creates the texture for <paramref name="cascades"/> × <paramref name="size"/>² texels when it changed; returns true then.</summary>
    public bool Ensure(int size, int cascades)
    {
        if (size == Size && cascades == Cascades && _texture != null)
        {
            return false;
        }

        ReleaseTexture();
        Size = size;
        Cascades = cascades;
        var descriptor = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)size, (uint)size, (uint)cascades),
            Format = TextureFormat.Depth32float,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _texture = _wgpu.DeviceCreateTexture(_device, &descriptor);
        var arrayDescriptor = new TextureViewDescriptor
        {
            Format = TextureFormat.Depth32float,
            Dimension = TextureViewDimension.Dimension2DArray,
            MipLevelCount = 1,
            ArrayLayerCount = (uint)cascades,
            Aspect = TextureAspect.All,
        };
        _arrayView = _wgpu.TextureCreateView(_texture, &arrayDescriptor);
        for (var i = 0; i < cascades; i++)
        {
            var layerDescriptor = arrayDescriptor with { Dimension = TextureViewDimension.Dimension2D, BaseArrayLayer = (uint)i, ArrayLayerCount = 1 };
            _layerViews[i] = _wgpu.TextureCreateView(_texture, &layerDescriptor);
        }

        return true;
    }

    /// <summary>Releases the texture (shadows switched off).</summary>
    public void Release()
    {
        ReleaseTexture();
        Size = 0;
        Cascades = 0;
    }

    /// <summary>Writes the uniforms of cascade <paramref name="cascade"/> (same layout as the world pass).</summary>
    public void WriteUniforms(Queue* queue, int cascade, void* data) =>
        _wgpu.QueueWriteBuffer(queue, _uniforms[cascade], 0, data, (nuint)_uniformSize);

    /// <summary>Begins the depth-only pass of a cascade with its bind group set.</summary>
    public RenderPassEncoder* BeginPass(CommandEncoder* encoder, int cascade)
    {
        var depth = new RenderPassDepthStencilAttachment
        {
            View = _layerViews[cascade],
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1f,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
        };
        var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 0, ColorAttachments = null, DepthStencilAttachment = &depth };
        var pass = _wgpu.CommandEncoderBeginRenderPass(encoder, &descriptor);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[cascade], 0, null);
        return pass;
    }

    public void Dispose()
    {
        ReleaseTexture();
        for (var i = 0; i < ShadowCascades.MaxCascades; i++)
        {
            _wgpu.BindGroupRelease(_bindGroups[i]);
            _wgpu.BufferRelease(_uniforms[i]);
        }
    }

    private void ReleaseTexture()
    {
        for (var i = 0; i < _layerViews.Length; i++)
        {
            if (_layerViews[i] != null)
            {
                _wgpu.TextureViewRelease(_layerViews[i]);
                _layerViews[i] = null;
            }
        }

        if (_arrayView != null)
        {
            _wgpu.TextureViewRelease(_arrayView);
            _arrayView = null;
        }

        if (_texture != null)
        {
            _wgpu.TextureRelease(_texture);
            _texture = null;
        }
    }
}
