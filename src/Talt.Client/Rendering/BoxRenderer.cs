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
    private readonly WorldPipeline _opaquePipeline;
    private readonly WorldPipeline _translucentPipeline;
    private readonly Buffer* _cubeVertices;
    private readonly Buffer* _cubeIndices;
    private readonly ulong _cubeVertexBytes;
    private readonly uint _cubeIndexCount;
    private Layer _opaque;
    private Layer _translucent;

    public BoxRenderer(WorldPassContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var wgpu = context.Wgpu;
        var device = context.Device;
        _wgpu = wgpu;
        _device = device;
        _queue = context.Queue;
        _shader = WebGpuShaders.CreateModule(wgpu, device, WebGpuShaders.Load("boxes.wgsl"));
        _opaquePipeline = new WorldPipeline(context, PipelineSpecs.InstancedCube(_shader, (ulong)sizeof(BoxInstance), 3));
        _translucentPipeline = new WorldPipeline(context, PipelineSpecs.InstancedCube(_shader, (ulong)sizeof(BoxInstance), 3, translucent: true));

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
    public void EncodeOpaque(RenderPassEncoder* pass) => Encode(pass, _opaquePipeline.Main, _opaque);

    /// <summary>Opaque boxes into a shadow cascade (feature 018).</summary>
    public void EncodeShadow(RenderPassEncoder* pass) => Encode(pass, _opaquePipeline.Shadow, _opaque);

    /// <summary>Translucent boxes; call last in the world pass, with the world bind group set.</summary>
    public void EncodeTranslucent(RenderPassEncoder* pass) => Encode(pass, _translucentPipeline.Main, _translucent);

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
        _opaquePipeline.Dispose();
        _translucentPipeline.Dispose();
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
