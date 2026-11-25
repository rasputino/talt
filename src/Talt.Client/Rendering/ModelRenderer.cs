using Silk.NET.WebGPU;
using Talt.Core.Meshing;
using Talt.Core.Meshing.Models;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>
/// Draws voxel models (feature 018, research R6): all meshes of a <see cref="ModelLibrary"/> live in one
/// vertex buffer with one shared quad index pattern, the palettes in one texture (bound in group 1), and
/// each frame's sorted <see cref="ModelBatch"/> instances in one instance buffer; every bucket is one
/// indexed, instanced draw (base vertex = the level's first vertex, first instance = the bucket's start),
/// in the world pass and again in each shadow cascade.
/// </summary>
internal sealed unsafe class ModelRenderer : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly ModelLibrary _library;
    private readonly ShaderModule* _shader;
    private readonly WorldPipeline _pipeline;
    private readonly Buffer* _vertices;
    private readonly Buffer* _indices;
    private readonly Texture* _palette;
    private readonly TextureView* _paletteView;
    private Buffer* _instances;
    private int _instanceCapacity;
    private IReadOnlyList<ModelBucket> _buckets = [];

    public ModelRenderer(WorldPassContext context, ModelLibrary library, FrameResources frame)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(frame);
        _wgpu = context.Wgpu;
        _device = context.Device;
        _queue = context.Queue;
        _library = library;
        _shader = WebGpuShaders.CreateModule(_wgpu, _device, WebGpuShaders.Load("models.wgsl"));
        _pipeline = new WorldPipeline(context, new PipelineSpec
        {
            Shader = _shader,
            Buffers =
            [
                new VertexBufferSpec(ModelVertex.SizeInBytes, VertexStepMode.Vertex,
                [
                    new VertexAttribute { Format = VertexFormat.Sint16x4, Offset = 0, ShaderLocation = 0 },
                    new VertexAttribute { Format = VertexFormat.Uint32, Offset = 8, ShaderLocation = 1 },
                ]),
                new VertexBufferSpec((ulong)sizeof(ModelInstance), VertexStepMode.Instance,
                [
                    new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 2 },
                    new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 16, ShaderLocation = 3 },
                ]),
            ],
        });

        _vertices = CreateBuffer<ModelVertex>(library.Vertices, BufferUsage.Vertex | BufferUsage.CopyDst);
        var quads = Math.Max(1, library.MaxQuads);
        var indices = new uint[quads * 6];
        var pattern = PackedMesh.QuadIndices;
        for (var q = 0; q < quads; q++)
        {
            for (var i = 0; i < 6; i++)
            {
                indices[(q * 6) + i] = (uint)(q * 4) + pattern[i];
            }
        }

        _indices = CreateBuffer<uint>(indices, BufferUsage.Index | BufferUsage.CopyDst);
        GpuBytes = (library.Vertices.Length * (long)ModelVertex.SizeInBytes) + (indices.Length * 4L) + (library.Palettes.Length * 4L);

        var rows = Math.Max(1, library.VariantCount);
        _palette = frame.CreateTexture(256, (uint)rows, 1, TextureFormat.Rgba8UnormSrgb, TextureUsage.TextureBinding | TextureUsage.CopyDst,
            TextureViewDimension.Dimension2D, out var view);
        _paletteView = view;
        if (library.Palettes.Length > 0)
        {
            var destination = new ImageCopyTexture { Texture = _palette, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { Offset = 0, BytesPerRow = 256 * 4, RowsPerImage = (uint)rows };
            var extent = new Extent3D(256, (uint)rows, 1);
            fixed (uint* data = library.Palettes)
            {
                _wgpu.QueueWriteTexture(_queue, &destination, data, (nuint)(library.Palettes.Length * 4), &layout, &extent);
            }
        }

        frame.SetPalette(_paletteView);
    }

    /// <summary>Draw calls of the last world pass.</summary>
    public int Draws { get; private set; }

    /// <summary>Instances uploaded for the last frame.</summary>
    public int Instances { get; private set; }

    /// <summary>Triangles of the last world pass.</summary>
    public long Triangles { get; private set; }

    /// <summary>GPU bytes of meshes, indices and palettes (instances excluded).</summary>
    public long GpuBytes { get; }

    /// <summary>Uploads the frame's sorted instances.</summary>
    public void Upload(ModelBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var instances = batch.Instances;
        _buckets = batch.Buckets;
        Instances = instances.Length;
        if (instances.Length == 0)
        {
            return;
        }

        if (instances.Length > _instanceCapacity)
        {
            if (_instances != null)
            {
                _wgpu.BufferRelease(_instances);
            }

            _instanceCapacity = Math.Max(instances.Length, Math.Max(1024, _instanceCapacity * 2));
            var descriptor = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (ulong)(_instanceCapacity * sizeof(ModelInstance)) };
            _instances = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        }

        fixed (ModelInstance* data = instances)
        {
            _wgpu.QueueWriteBuffer(_queue, _instances, 0, data, (nuint)(instances.Length * sizeof(ModelInstance)));
        }
    }

    /// <summary>Draws the models in the world pass (bind groups 0 and 1 set).</summary>
    public void Encode(RenderPassEncoder* pass)
    {
        Draws = 0;
        Triangles = 0;
        EncodeWith(pass, _pipeline.Main, count: true);
    }

    /// <summary>Draws the models into a shadow cascade.</summary>
    public void EncodeShadow(RenderPassEncoder* pass) => EncodeWith(pass, _pipeline.Shadow, count: false);

    public void Dispose()
    {
        if (_instances != null)
        {
            _wgpu.BufferRelease(_instances);
        }

        _wgpu.BufferRelease(_vertices);
        _wgpu.BufferRelease(_indices);
        _wgpu.TextureViewRelease(_paletteView);
        _wgpu.TextureRelease(_palette);
        _pipeline.Dispose();
        _wgpu.ShaderModuleRelease(_shader);
    }

    private void EncodeWith(RenderPassEncoder* pass, RenderPipeline* pipeline, bool count)
    {
        if (Instances == 0 || _buckets.Count == 0 || _library.Vertices.Length == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _vertices, 0, (ulong)(_library.Vertices.Length * ModelVertex.SizeInBytes));
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _instances, 0, (ulong)(Instances * sizeof(ModelInstance)));
        _wgpu.RenderPassEncoderSetIndexBuffer(pass, _indices, IndexFormat.Uint32, 0, (ulong)Math.Max(1, _library.MaxQuads) * 6 * sizeof(uint));
        for (var b = 0; b < _buckets.Count; b++)
        {
            var bucket = _buckets[b];
            // Shadows use one level coarser: they are blurred and small, and every cascade draws all models.
            var level = count ? bucket.Level : Math.Min(bucket.Level + 1, ModelMesh.LevelCount - 1);
            var (firstVertex, quads) = _library.Levels[(bucket.Variant * ModelMesh.LevelCount) + level];
            if (quads == 0)
            {
                continue;
            }

            _wgpu.RenderPassEncoderDrawIndexed(pass, (uint)quads * 6, (uint)bucket.Count, 0, firstVertex, (uint)bucket.FirstInstance);
            if (count)
            {
                Draws++;
                Triangles += quads * 2L * bucket.Count;
            }
        }
    }

    private Buffer* CreateBuffer<T>(T[] data, BufferUsage usage)
        where T : unmanaged
    {
        var size = (ulong)(data.Length * sizeof(T));
        var descriptor = new BufferDescriptor { Usage = usage, Size = Math.Max(16, (size + 3) & ~3UL) };
        var buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);
        if (size > 0)
        {
            fixed (T* pointer = data)
            {
                _wgpu.QueueWriteBuffer(_queue, buffer, 0, pointer, (nuint)size);
            }
        }

        return buffer;
    }
}
