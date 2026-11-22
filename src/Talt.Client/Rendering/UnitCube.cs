using System.Numerics;
using System.Runtime.InteropServices;

namespace Talt.Client.Rendering;

/// <summary>
/// Per-instance data for one vehicle. Layout must match the instance attributes in vehicles.wgsl.
/// </summary>
/// <param name="Position">xyz: center of the vehicle's base; w: 0 when moving along X, 1 along Z.</param>
/// <param name="Color">Linear RGB body color (w unused).</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct VehicleInstance(Vector4 Position, Vector4 Color);

/// <summary>
/// Cube mesh centered on X and Z, from 0 to 1 on Y, with per-face normals.
/// </summary>
internal static class UnitCube
{
    /// <returns>Interleaved vertices (position xyz, normal xyz) and 16-bit triangle indices.</returns>
    public static (float[] Vertices, ushort[] Indices) Build()
    {
        // Normal, then two tangents U and V with U × V = Normal, so corners are counter-clockwise.
        (Vector3 N, Vector3 U, Vector3 V)[] faces =
        [
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
        ];

        var vertices = new List<float>();
        var indices = new List<ushort>();
        var center = new Vector3(0f, 0.5f, 0f);

        foreach (var (n, u, v) in faces)
        {
            var first = (ushort)(vertices.Count / 6);
            foreach (var (cu, cv) in new[] { (-0.5f, -0.5f), (0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f) })
            {
                var p = center + n * 0.5f + u * cu + v * cv;
                vertices.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z]);
            }

            indices.AddRange([first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)]);
        }

        return ([.. vertices], [.. indices]);
    }
}
