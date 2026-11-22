using System.Numerics;

namespace Talt.Client.Rendering;

/// <summary>
/// The six planes of a camera's view volume, for culling boxes on the CPU.
/// </summary>
internal readonly struct Frustum
{
    private readonly Vector4 _left;
    private readonly Vector4 _right;
    private readonly Vector4 _bottom;
    private readonly Vector4 _top;
    private readonly Vector4 _near;
    private readonly Vector4 _far;

    /// <summary>
    /// Planes of a System.Numerics view-projection matrix (row vectors, clip = v × M, depth 0..1).
    /// Each plane is (normal, d) with the inside where dot(normal, p) + d ≥ 0.
    /// </summary>
    public Frustum(Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _left = c4 + c1;
        _right = c4 - c1;
        _bottom = c4 + c2;
        _top = c4 - c2;
        _near = c3;
        _far = c4 - c3;
    }

    /// <summary>False only when the box is certainly outside the view volume.</summary>
    public bool Intersects(Vector3 min, Vector3 max) =>
        Inside(_left, min, max) && Inside(_right, min, max) && Inside(_bottom, min, max)
        && Inside(_top, min, max) && Inside(_near, min, max) && Inside(_far, min, max);

    private static bool Inside(Vector4 plane, Vector3 min, Vector3 max)
    {
        // The box corner furthest along the plane normal.
        var p = new Vector3(
            plane.X >= 0 ? max.X : min.X,
            plane.Y >= 0 ? max.Y : min.Y,
            plane.Z >= 0 ? max.Z : min.Z);
        return plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z + plane.W >= 0;
    }
}
