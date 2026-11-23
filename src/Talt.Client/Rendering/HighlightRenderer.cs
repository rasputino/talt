using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Talt.Client.Ui;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Talt.Client.Rendering;

/// <summary>A box to outline in the world (hover or selection highlight).</summary>
internal readonly record struct HighlightBox(Vector3 Min, Vector3 Max, UiColor Color);

/// <summary>
/// Draws up to <see cref="MaxBoxes"/> wire boxes with the world's depth buffer, using the voxel
/// pipeline's uniforms (camera). Each box is 12 edges, each edge drawn as three slightly offset lines
/// so it stays visible on high-resolution screens.
/// </summary>
internal sealed unsafe class HighlightRenderer : IDisposable
{
    public const int MaxBoxes = 4;

    private const int VerticesPerBox = 12 * 2 * 3;

    private readonly WebGPU _wgpu;
    private readonly Queue* _queue;
    private readonly ShaderModule* _shader;
    private readonly RenderPipeline* _pipeline;
    private readonly Buffer* _vertexBuffer;
    private readonly LineVertex[] _vertices = new LineVertex[MaxBoxes * VerticesPerBox];
    private int _vertexCount;

    public HighlightRenderer(WebGPU wgpu, Device* device, Queue* queue, PipelineLayout* worldLayout, TextureFormat surfaceFormat, TextureFormat depthFormat)
    {
        _wgpu = wgpu;
        _queue = queue;
        _shader = WebGpuShaders.CreateModule(wgpu, device, WebGpuShaders.Load("highlight.wgsl"));

        var attributes = stackalloc VertexAttribute[2];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 1 };
        var layout = new VertexBufferLayout { ArrayStride = (ulong)sizeof(LineVertex), StepMode = VertexStepMode.Vertex, AttributeCount = 2, Attributes = attributes };

        var colorTarget = new ColorTargetState { Format = surfaceFormat, Blend = null, WriteMask = ColorWriteMask.All };
        var keep = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
        var depth = new DepthStencilState
        {
            Format = depthFormat,
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.LessEqual,
            StencilFront = keep,
            StencilBack = keep,
        };
        fixed (byte* vertexEntry = "vs_main"u8)
        fixed (byte* fragmentEntry = "fs_main"u8)
        {
            var fragment = new FragmentState { Module = _shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &colorTarget };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = worldLayout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &layout },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.LineList, StripIndexFormat = IndexFormat.Undefined, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                DepthStencil = &depth,
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            _pipeline = wgpu.DeviceCreateRenderPipeline(device, &descriptor);
        }

        var bufferDescriptor = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (ulong)(_vertices.Length * sizeof(LineVertex)) };
        _vertexBuffer = wgpu.DeviceCreateBuffer(device, &bufferDescriptor);
    }

    /// <summary>Uploads the boxes to draw in the next frame.</summary>
    public void Update(ReadOnlySpan<HighlightBox> boxes, Vector3 cameraPosition)
    {
        _vertexCount = 0;
        var count = Math.Min(boxes.Length, MaxBoxes);
        for (var b = 0; b < count; b++)
        {
            AddBox(boxes[b], cameraPosition);
        }

        if (_vertexCount > 0)
        {
            fixed (LineVertex* data = _vertices)
            {
                _wgpu.QueueWriteBuffer(_queue, _vertexBuffer, 0, data, (nuint)(_vertexCount * sizeof(LineVertex)));
            }
        }
    }

    /// <summary>Records the lines into the world pass (the world bind group must be set).</summary>
    public void Encode(RenderPassEncoder* pass)
    {
        if (_vertexCount == 0)
        {
            return;
        }

        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, (ulong)(_vertexCount * sizeof(LineVertex)));
        _wgpu.RenderPassEncoderDraw(pass, (uint)_vertexCount, 1, 0, 0);
    }

    public void Dispose()
    {
        _wgpu.BufferRelease(_vertexBuffer);
        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.ShaderModuleRelease(_shader);
    }

    private void AddBox(HighlightBox box, Vector3 cameraPosition)
    {
        // Inflate by 2 % (and a little) so the lines are not hidden by the faces they outline.
        var center = (box.Min + box.Max) / 2f;
        var half = (box.Max - box.Min) / 2f * 1.02f + new Vector3(0.01f);
        var min = center - half;
        var max = center + half;

        // Offset of the extra lines: about one pixel at the box's distance.
        var thickness = MathF.Max(0.004f, Vector3.Distance(cameraPosition, center) * 0.0012f);
        Span<Vector3> corners =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ];
        ReadOnlySpan<int> edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        ReadOnlySpan<Vector3> offsets = [Vector3.Zero, new(1, 1, 0), new(0, 1, 1)];
        foreach (var offset in offsets)
        {
            for (var e = 0; e < edges.Length; e++)
            {
                _vertices[_vertexCount++] = new LineVertex(corners[edges[e]] + offset * thickness, box.Color.Packed);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct LineVertex(Vector3 Position, uint Color);
}
