using System.Numerics;
using System.Runtime.InteropServices;

namespace Talt.Core.Meshing;

/// <summary>
/// Vertex produced by the chunk mesher. Layout matches the GPU vertex buffer (36 bytes).
/// </summary>
/// <param name="Position">World position.</param>
/// <param name="Normal">Face normal (unit axis vector).</param>
/// <param name="Color">Linear RGB color with ambient occlusion already applied.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct VoxelVertex(Vector3 Position, Vector3 Normal, Vector3 Color)
{
    public const int SizeInBytes = 36;
}
