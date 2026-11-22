namespace Talt.Core.Meshing;

/// <summary>
/// The six faces of a voxel, shared by the meshers and the shader (the face index is stored in each
/// packed vertex). U × V = Normal, so corners (0,0), (1,0), (1,1), (0,1) in (U, V) are
/// counter-clockwise when seen from outside.
/// </summary>
public static class FaceDirections
{
    /// <summary>Number of face directions.</summary>
    public const int Count = 6;

    /// <summary>Index of the face pointing down (−Y).</summary>
    public const int Down = 3;

    /// <summary>Faces in index order: +X, −X, +Y, −Y, +Z, −Z.</summary>
    internal static readonly Face[] Faces =
    [
        new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
        new(new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
        new(new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)),
        new(new(0, -1, 0), new(1, 0, 0), new(0, 0, 1)),
        new(new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)),
        new(new(0, 0, -1), new(0, 1, 0), new(1, 0, 0)),
    ];

    /// <summary>Quad corners in (U, V) order 0..3.</summary>
    internal static readonly (int U, int V)[] Corners = [(0, 0), (1, 0), (1, 1), (0, 1)];

    /// <summary>Brightness for ambient occlusion level 0..3 (0..3 unoccluded neighbours around a vertex).</summary>
    internal static readonly float[] AoBrightness = [0.55f, 0.72f, 0.86f, 1f];

    /// <summary>Brightness of an ambient occlusion level (0 darkest, 3 unoccluded).</summary>
    public static float Brightness(int ambientOcclusion) => AoBrightness[ambientOcclusion];

    /// <summary>Unit normal of a face direction.</summary>
    public static (int X, int Y, int Z) Normal(int faceIndex)
    {
        var n = Faces[faceIndex].Normal;
        return (n.X, n.Y, n.Z);
    }

    /// <summary>Index of the face whose normal is the given unit axis vector.</summary>
    public static int IndexOf(int x, int y, int z)
    {
        for (var i = 0; i < Count; i++)
        {
            var n = Faces[i].Normal;
            if (n.X == x && n.Y == y && n.Z == z)
            {
                return i;
            }
        }

        throw new ArgumentException($"({x}, {y}, {z}) is not a unit axis vector.");
    }

    internal readonly record struct Int3(int X, int Y, int Z)
    {
        /// <summary>Index (0 = X, 1 = Y, 2 = Z) of the single non-zero component.</summary>
        public int Axis => X != 0 ? 0 : Y != 0 ? 1 : 2;

        /// <summary>The non-zero component (+1 or −1 for unit vectors).</summary>
        public int Sign => X + Y + Z;
    }

    internal readonly record struct Face(Int3 Normal, Int3 U, Int3 V);
}
