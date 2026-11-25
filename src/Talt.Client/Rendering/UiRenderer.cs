using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Talt.Client.Ui;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>
/// Draws a <see cref="UiDrawList"/> on top of the frame: one pipeline, one atlas texture, one dynamic
/// vertex buffer and a shared quad index buffer; one draw call per clip rectangle (research R1).
/// </summary>
internal sealed unsafe class UiRenderer : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly UiAtlas _atlas;

    private ShaderModule* _shader;
    private BindGroupLayout* _bindGroupLayout;
    private PipelineLayout* _pipelineLayout;
    private RenderPipeline* _pipeline;
    private Buffer* _uniformBuffer;
    private Texture* _texture;
    private TextureView* _textureView;
    private Sampler* _linearSampler;
    private Sampler* _nearestSampler;
    private BindGroup* _bindGroup;
    private Buffer* _vertexBuffer;
    private Buffer* _indexBuffer;
    private int _quadCapacity;
    private int _uploadedAtlasVersion = -1;
    private bool _disposed;

    public UiRenderer(WebGPU wgpu, Device* device, Queue* queue, TextureFormat surfaceFormat, UiAtlas atlas, string shaderSource)
    {
        _wgpu = wgpu;
        _device = device;
        _queue = queue;
        _atlas = atlas;
        CreateResources(surfaceFormat, shaderSource);
    }

    /// <summary>Uploads the atlas (when changed) and the frame's vertices.</summary>
    public void Upload(UiDrawList list, float width, float height)
    {
        if (_uploadedAtlasVersion != _atlas.Version)
        {
            UploadAtlas();
        }

        var uniforms = new Vector4(width, height, 0, 0);
        _wgpu.QueueWriteBuffer(_queue, _uniformBuffer, 0, &uniforms, (nuint)sizeof(Vector4));

        var quads = list.QuadCount;
        if (quads == 0)
        {
            return;
        }

        EnsureCapacity(quads);
        fixed (UiVertex* vertices = list.Vertices)
        {
            _wgpu.QueueWriteBuffer(_queue, _vertexBuffer, 0, vertices, (nuint)(quads * 4 * UiVertex.SizeInBytes));
        }
    }

    /// <summary>Records the draw calls into a pass whose colour target already holds the world.</summary>
    public void Encode(RenderPassEncoder* pass, UiDrawList list, int width, int height)
    {
        if (list.QuadCount == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, (ulong)(list.QuadCount * 4 * UiVertex.SizeInBytes));
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _indexBuffer, IndexFormat.Uint32, 0, (ulong)(_quadCapacity * 6 * sizeof(uint)));
        foreach (var command in list.Commands)
        {
            var x = (uint)Math.Clamp((int)MathF.Floor(command.Clip.X), 0, width);
            var y = (uint)Math.Clamp((int)MathF.Floor(command.Clip.Y), 0, height);
            var right = (uint)Math.Clamp((int)MathF.Ceiling(command.Clip.Right), 0, width);
            var bottom = (uint)Math.Clamp((int)MathF.Ceiling(command.Clip.Bottom), 0, height);
            if (right <= x || bottom <= y)
            {
                continue;
            }

            _wgpu.RenderPassEncoderSetScissorRect(pass, x, y, right - x, bottom - y);
            _wgpu.RenderPassEncoderDrawIndexed(pass, (uint)(command.QuadCount * 6), 1, (uint)(command.FirstQuad * 6), 0, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_vertexBuffer != null)
        {
            _wgpu.BufferRelease(_vertexBuffer);
            _wgpu.BufferRelease(_indexBuffer);
        }

        _wgpu.BindGroupRelease(_bindGroup);
        _wgpu.SamplerRelease(_linearSampler);
        _wgpu.SamplerRelease(_nearestSampler);
        _wgpu.TextureViewRelease(_textureView);
        _wgpu.TextureDestroy(_texture);
        _wgpu.TextureRelease(_texture);
        _wgpu.BufferRelease(_uniformBuffer);
        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.PipelineLayoutRelease(_pipelineLayout);
        _wgpu.BindGroupLayoutRelease(_bindGroupLayout);
        _wgpu.ShaderModuleRelease(_shader);
    }

    private void UploadAtlas()
    {
        var destination = new ImageCopyTexture { Texture = _texture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)(_atlas.Width * 4), RowsPerImage = (uint)_atlas.Height };
        var size = new Extent3D((uint)_atlas.Width, (uint)_atlas.Height, 1);
        fixed (byte* pixels = _atlas.Pixels)
        {
            _wgpu.QueueWriteTexture(_queue, &destination, pixels, (nuint)_atlas.Pixels.Length, &layout, &size);
        }

        _uploadedAtlasVersion = _atlas.Version;
    }

    /// <summary>Grows the vertex and index buffers by doubling (rare: only when more quads appear).</summary>
    private void EnsureCapacity(int quads)
    {
        if (quads <= _quadCapacity)
        {
            return;
        }

        if (_vertexBuffer != null)
        {
            _wgpu.BufferRelease(_vertexBuffer);
            _wgpu.BufferRelease(_indexBuffer);
        }

        _quadCapacity = Math.Max(quads, Math.Max(4096, _quadCapacity * 2));
        var vertexDescriptor = new BufferDescriptor
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = (ulong)(_quadCapacity * 4 * UiVertex.SizeInBytes),
        };
        _vertexBuffer = _wgpu.DeviceCreateBuffer(_device, &vertexDescriptor);

        var indices = new uint[_quadCapacity * 6];
        for (var q = 0; q < _quadCapacity; q++)
        {
            var v = (uint)(q * 4);
            indices[q * 6] = v;
            indices[q * 6 + 1] = v + 2;
            indices[q * 6 + 2] = v + 1;
            indices[q * 6 + 3] = v;
            indices[q * 6 + 4] = v + 3;
            indices[q * 6 + 5] = v + 2;
        }

        var indexDescriptor = new BufferDescriptor { Usage = BufferUsage.Index | BufferUsage.CopyDst, Size = (ulong)(indices.Length * sizeof(uint)) };
        _indexBuffer = _wgpu.DeviceCreateBuffer(_device, &indexDescriptor);
        fixed (uint* data = indices)
        {
            _wgpu.QueueWriteBuffer(_queue, _indexBuffer, 0, data, (nuint)(indices.Length * sizeof(uint)));
        }
    }

    private void CreateResources(TextureFormat surfaceFormat, string shaderSource)
    {
        _shader = WebGpuShaders.CreateModule(_wgpu, _device, shaderSource);

        var entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(Vector4) },
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D },
        };
        entries[2] = new BindGroupLayoutEntry { Binding = 2, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } };
        entries[3] = new BindGroupLayoutEntry { Binding = 3, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } };
        var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 4, Entries = entries };
        _bindGroupLayout = _wgpu.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);
        var bindGroupLayout = _bindGroupLayout;
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &bindGroupLayout };
        _pipelineLayout = _wgpu.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);

        var uniformDescriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)sizeof(Vector4) };
        _uniformBuffer = _wgpu.DeviceCreateBuffer(_device, &uniformDescriptor);

        var textureDescriptor = new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)_atlas.Width, (uint)_atlas.Height, 1),
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _texture = _wgpu.DeviceCreateTexture(_device, &textureDescriptor);
        _textureView = _wgpu.TextureCreateView(_texture, null);

        var linear = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        };
        _linearSampler = _wgpu.DeviceCreateSampler(_device, &linear);
        var nearest = linear with { MagFilter = FilterMode.Nearest, MinFilter = FilterMode.Nearest };
        _nearestSampler = _wgpu.DeviceCreateSampler(_device, &nearest);

        var groupEntries = stackalloc BindGroupEntry[4];
        groupEntries[0] = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Size = (ulong)sizeof(Vector4) };
        groupEntries[1] = new BindGroupEntry { Binding = 1, TextureView = _textureView };
        groupEntries[2] = new BindGroupEntry { Binding = 2, Sampler = _linearSampler };
        groupEntries[3] = new BindGroupEntry { Binding = 3, Sampler = _nearestSampler };
        var groupDescriptor = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 4, Entries = groupEntries };
        _bindGroup = _wgpu.DeviceCreateBindGroup(_device, &groupDescriptor);

        var attributes = stackalloc VertexAttribute[5];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 16, ShaderLocation = 2 };
        attributes[3] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 24, ShaderLocation = 3 };
        attributes[4] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 28, ShaderLocation = 4 };
        var vertexLayout = new VertexBufferLayout { ArrayStride = UiVertex.SizeInBytes, StepMode = VertexStepMode.Vertex, AttributeCount = 5, Attributes = attributes };

        var blend = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
        };
        var colorTarget = new ColorTargetState { Format = surfaceFormat, Blend = &blend, WriteMask = ColorWriteMask.All };
        fixed (byte* vertexEntry = "vs_main"u8)
        fixed (byte* fragmentEntry = "fs_main"u8)
        {
            var fragment = new FragmentState { Module = _shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &colorTarget };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &vertexLayout },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None,
                },
                DepthStencil = null,
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            _pipeline = _wgpu.DeviceCreateRenderPipeline(_device, &descriptor);
        }
    }
}

/// <summary>Shader helpers shared by the renderers.</summary>
internal static unsafe class WebGpuShaders
{
    public static ShaderModule* CreateModule(WebGPU wgpu, Device* device, string source)
    {
        var code = Silk.NET.Core.Native.SilkMarshal.StringToPtr(source);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = (byte*)code,
            };
            var descriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            return wgpu.DeviceCreateShaderModule(device, &descriptor);
        }
        finally
        {
            Silk.NET.Core.Native.SilkMarshal.Free(code);
        }
    }

    /// <summary>Embedded shader source by file name, with <c>// #include "file.wgsl"</c> lines expanded.</summary>
    public static string Load(string fileName) => ExpandIncludes(LoadRaw(fileName), LoadRaw);

    /// <summary>
    /// Replaces each line <c>// #include "name.wgsl"</c> with that file's text (one level: included files
    /// may not include others), so every world shader shares lighting.wgsl (feature 018, research R2).
    /// </summary>
    internal static string ExpandIncludes(string source, Func<string, string> load)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(load);
        const string Directive = "// #include \"";
        const char NewLine = (char)10;
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (var line in source.Split(NewLine))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(Directive, StringComparison.Ordinal) && trimmed.EndsWith('"'))
            {
                var name = trimmed[Directive.Length..^1];
                var included = load(name);
                if (included.Contains(Directive, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Shader include '{name}' may not include other files.");
                }

                builder.Append(included).Append(NewLine);
            }
            else
            {
                builder.Append(line).Append(NewLine);
            }
        }

        return builder.ToString();
    }

    private static string LoadRaw(string fileName)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("." + fileName, StringComparison.Ordinal) || name == fileName);
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
